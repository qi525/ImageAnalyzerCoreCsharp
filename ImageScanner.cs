// 文件名：ImageScanner.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent; // 用于并行处理的并发集合
// 假设已引入一个 C# 图像处理库来处理元数据，如 Magick.NET 或其他专门的库
// using ImageProcessing.Metadata; 

namespace ImageAnalyzerCore
{
    // 结构体：用于存储单张图片的所有分析信息，对应 Python 中的 Dict[str, Any]
    public class ImageInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string DirectoryName { get; set; }
        public string ExtractedTagsRaw { get; set; } = string.Empty; // 原始提取的标签
        public string CleanedTags { get; set; } = string.Empty;       // 清洗后的标签字符串 (用于TF-IDF)
        public string CoreKeywords { get; set; } = string.Empty;      // 核心关键词字符串 (用于Excel L列)
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Status { get; set; } = "待处理";
    }

    /// <summary>
    /// 图像文件扫描器：用于递归扫描目录，并行提取图片元数据并清洗标签。
    /// 对应 Python 源码中的 image_scanner.py。
    /// </summary>
    public class ImageScanner
    {
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// 提取正向提示词中的核心关键词。
        /// 对应 Python 中的 extract_core_keywords 函数。
        /// </summary>
        /// <param name="tags">原始标签字符串。</param>
        /// <returns>清洗后的核心关键词，以逗号分隔。</returns>
        private static string ExtractCoreKeywords(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return string.Empty;
            }

            // 1. 小写化并去除停用词
            string lowerTags = tags.ToLower();
            string cleaned = lowerTags;

            // 使用 AnalyzerConfig 中的停用词列表进行清洗
            foreach (var stopWordGroup in AnalyzerConfig.PositivePromptStopWords)
            {
                // 注意：Python 源码中的列表是长字符串，此处假设它们已按逗号分隔
                var stopWords = stopWordGroup.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(w => w.Trim())
                                             .Where(w => !string.IsNullOrWhiteSpace(w))
                                             .ToList();

                foreach (var stopWord in stopWords)
                {
                    // 使用正则边界 (\b) 或精确替换来避免误删
                    cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(stopWord)}\b", "", RegexOptions.IgnoreCase);
                }
            }

            // 2. 清理多余的空格和分隔符
            cleaned = Regex.Replace(cleaned, @"[\s,]+", ",", RegexOptions.None); // 将多个分隔符和空格替换为单个逗号
            cleaned = cleaned.Trim(',', ' '); // 移除首尾逗号和空格

            return cleaned;
        }

        /// <summary>
        /// 处理单个图片文件，提取元数据并生成 ImageInfo 对象。
        /// 对应 Python 中的 process_single_image 函数。
        /// </summary>
        private ImageInfo ProcessSingleImage(string filePath)
        {
            // 模仿 Python 的 logger 机制
            void LogError(string msg) => Console.WriteLine($"[ERROR] [{Path.GetFileName(filePath)}] {msg}");
            void LogInfo(string msg) => Console.WriteLine($"[INFO] [{Path.GetFileName(filePath)}] {msg}");

            var info = new ImageInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                DirectoryName = Path.GetDirectoryName(filePath)
            };

            try
            {
                // 1. 获取文件时间信息
                var fileInfo = new FileInfo(filePath);
                info.CreationTime = fileInfo.CreationTime;
                info.LastWriteTime = fileInfo.LastWriteTime;

                // 2. 模拟元数据提取 (PNG INFO / WEBP METADATA)
                // ⚠️ 实际C#代码需要替换为真正的图像库调用，此处使用占位逻辑
                string rawTags = GetImageMetadata(filePath); 
                info.ExtractedTagsRaw = rawTags;

                // 3. 清洗和提取核心关键词
                info.CleanedTags = Regex.Replace(rawTags, @"[\n\r]+", " ", RegexOptions.None).Trim(); // 初步清洗换行符
                info.CoreKeywords = ExtractCoreKeywords(info.CleanedTags);

                // 4. 更新状态并计数
                info.Status = "成功提取";
                _statusCounts.AddOrUpdate("成功提取", 1, (key, count) => count + 1);
            }
            catch (FileNotFoundException)
            {
                LogError("文件未找到或权限不足，跳过。");
                info.Status = "失败: 文件未找到";
                _statusCounts.AddOrUpdate("失败: 文件操作异常", 1, (key, count) => count + 1);
            }
            catch (Exception ex)
            {
                LogError($"处理异常: {ex.Message}");
                info.Status = $"失败: {ex.GetType().Name}";
                _statusCounts.AddOrUpdate("失败: 数据/图像解析异常", 1, (key, count) => count + 1);
            }

            return info;
        }

        // ⚠️ 占位函数：在实际项目中，应使用 C# 库实现此功能
        private static string GetImageMetadata(string filePath)
        {
            // 模拟从 PNG/WEBP 元数据中读取标签 (如 a1111 的 prompt)
            if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return "1girl, solo, short_hair, blue_eyes, white_shirt, best quality, master piece, (original prompt details...)";
            }
            if (filePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                return "illustration, anime style, high detail, (more tags from webp metadata...)";
            }
            return string.Empty;
        }
        
        /// <summary>
        /// 扫描指定目录并并行处理所有图片文件。
        /// 对应 Python 中的 scan_images 函数。
        /// </summary>
        /// <param name="folderToScan">要扫描的根目录。</param>
        /// <returns>包含所有图片信息的列表。</returns>
        public List<ImageInfo> ScanAndExtractInfo(string folderToScan)
        {
            Console.WriteLine($"\n>>> 开始扫描目录: {folderToScan}");
            
            // 1. 阶段：递归收集所有图片路径 (对应 Python 的 os.walk)
            var imagePaths = new List<string>();
            var imageExtensions = AnalyzerConfig.ImageExtensions;
            
            // 使用 EnumerateFiles 和 Directory.EnumerateDirectories 进行递归，避免加载所有文件到内存
            try
            {
                var allFiles = Directory.EnumerateFiles(folderToScan, "*.*", SearchOption.AllDirectories)
                                        .Where(s => imageExtensions.Contains(Path.GetExtension(s)?.ToLowerInvariant()));

                imagePaths.AddRange(allFiles);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[CRITICAL] 权限不足，无法访问目录。错误: {ex.Message}");
                return new List<ImageInfo>();
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine($"[CRITICAL] 目录不存在: {folderToScan}");
                return new List<ImageInfo>();
            }

            if (!imagePaths.Any())
            {
                Console.WriteLine("未找到任何图片文件。");
                return new List<ImageInfo>();
            }
            
            int totalImages = imagePaths.Count;
            Console.WriteLine($"检测到 {totalImages} 个图片文件。使用 {AnalyzerConfig.MaxConcurrentWorkers} 个线程并行扫描元数据...");

            // 2. 阶段：多线程/多任务并行处理 (对应 Python 的 ProcessPoolExecutor)
            var imageData = new ConcurrentBag<ImageInfo>();

            // C# 中使用 Parallel.ForEach 模拟多核并行加速
            // 考虑到这是 I/O 密集型操作 (读取文件)，多线程通常比多进程更高效
            Parallel.ForEach(imagePaths, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, filePath =>
            {
                var result = ProcessSingleImage(filePath);
                imageData.Add(result);

                // 实时预览/计数器 (模仿 Python tqdm 的日志输出)
                // Console.Write($"\r当前进度: {imageData.Count}/{totalImages} (成功:{_statusCounts.GetValueOrDefault("成功提取", 0)} / 失败:{_statusCounts.Where(kv => kv.Key.StartsWith("失败")).Sum(kv => kv.Value)})");
            });

            // 3. 阶段：收集和过滤结果并打印最终统计
            var finalResults = imageData.ToList();
            
            // 打印最终统计结果 (满足用户要求的计数器格式)
            int successCount = _statusCounts.GetValueOrDefault("成功提取", 0);
            int failureCount = _statusCounts.Where(kv => kv.Key.StartsWith("失败")).Sum(kv => kv.Value);
            
            Console.WriteLine("\n--- 图片扫描与元数据提取完成 ---");
            Console.WriteLine($"总任务量: {totalImages}");
            Console.WriteLine($"成功处理量: {successCount}");
            Console.WriteLine($"失败处理量: {failureCount}");
            if (failureCount > 0)
            {
                Console.WriteLine("失败详情:");
                foreach (var kvp in _statusCounts.Where(kv => kv.Key.StartsWith("失败")).OrderByDescending(kv => kv.Value))
                {
                    Console.WriteLine($"  - {kvp.Key}: {kvp.Value} 张");
                }
            }

            return finalResults;
        }
    }
}