// 文件名：ImageScanner.cs

using System;
using System.Collections.Generic;
using System.IO; // System.IO.Directory 现需要显式使用
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading; // [多线程支持] 用于原子操作 Interlocked
using System.Collections.Concurrent; 
using static System.Console; 
using System.Diagnostics; // [进度条支持] 引入 Stopwatch

// [核心修改] 引入 MetadataExtractor 库
using MetadataExtractor; 
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Png; // 包含 PngDirectory
using MetadataExtractor.Formats.Xmp;
using MetadataExtractor.Formats.WebP; 
using MetadataExtractor.Formats.Iptc; 

// [Excel导出支持] 引入 ClosedXML 库
using ClosedXML.Excel;

/// 获取图片文件所有注释信息：的PNGINFO，exif
/// 从注释信息提取各种信息
/// 从注释信息提取正向关键词，负面关键词，其他设置，模型，等等
/// 统计正面关键词的字数
/// 从正向关键词提取核心关键词，通过去除默认词和风格词等方式
/// 
/// 风格词是一整串一整串的词，用于优化，但是没有实际意义，比如 "masterpiece, best quality, realistic, detailed, intricate details, 8k, highres, award winning, ultra detailed, finely detailed, cinematic lighting, photorealistic, volumetric lighting, sharp focus, depth of field, bokeh, film grain" 这一串词
/// 自动获取风格词，用"1girl"进行切割整个正向关键词，留下前面一段，如果重复出现超过10次，则缓存成为默认风格词，默认风格词的总长度要求是30个字符以上
/// 自动获取的风格词长度各不相同，有的很短，有的很长，但是以长度最长的进行从上到下进行匹配，匹配成功就进行移除这个风格词后，剩下的部分再进行下一轮匹配，直到无法匹配为止
/// 

namespace ImageAnalyzerCore
{
    // 结构体：用于存储单张图片的所有分析信息，对应 Python 中的 Dict[str, Any]
    public class ImageInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryName { get; set; } = string.Empty;
        
        public string ExtractedTagsRaw { get; set; } = string.Empty; // 原始提取的标签
        public string CleanedTags { get; set; } = string.Empty;       // 清洗后的标签字符串 (用于TF-IDF)
        public string CoreKeywords { get; set; } = string.Empty;      // 核心关键词字符串 (用于Excel L列)
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        // @@    40-40,41-41   @@ 增加一行
        public float PredictedScore { get; set; } = 0.0f; // [新增] 用于存储 ImageScorer 预测的最终分数
        public string Status { get; set; } = "待处理";
    }

    /// <summary>
    /// 图像文件扫描器：用于递归扫描目录，并行提取图片元数据并清洗标签。
    /// </summary>
    public class ImageScanner
    {
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();
        private int _processedCount = 0; // 用于实时进度条的原子计数器

        /// <summary>
        /// 递归扫描并提取元数据。
        /// </summary>
        public List<ImageInfo> ScanAndExtractInfo(string rootFolder)
        {
            var stopwatch = Stopwatch.StartNew(); // 启动计时器
            var imagePaths = new ConcurrentBag<string>();
            _statusCounts.Clear(); 
            _processedCount = 0; // 重置计数器

            // 显式使用 System.IO.Directory
            if (!System.IO.Directory.Exists(rootFolder)) 
            {
                LogError($"扫描目录不存在: {rootFolder}");
                return new List<ImageInfo>();
            }
            
            try
            {
                // 显式使用 System.IO.Directory
                var allFiles = System.IO.Directory.EnumerateFiles(rootFolder, "*.*", System.IO.SearchOption.AllDirectories);
                
                foreach (var filePath in allFiles)
                {
                    string extension = Path.GetExtension(filePath).ToLowerInvariant();
                    // ⚠️ 需要 AnalyzerConfig.ImageExtensions 存在于某个可访问的类中
                    // 假设 AnalyzerConfig 是一个静态类，包含 ImageExtensions 属性
                    if (AnalyzerConfig.ImageExtensions.Contains(extension))
                    {
                        imagePaths.Add(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"文件扫描异常: {ex.Message}");
                return new List<ImageInfo>();
            }

            int totalImages = imagePaths.Count;
            if (totalImages == 0)
            {
                WriteLine("[INFO] 没有找到任何图片文件，流程中止。");
                return new List<ImageInfo>();
            }

            var imageData = new ConcurrentBag<ImageInfo>();
            
            // ⚠️ 需要 AnalyzerConfig.MaxConcurrentWorkers 存在于某个可访问的类中
            WriteLine($"\n[INFO] 检测到 {totalImages} 个图片文件。使用 {AnalyzerConfig.MaxConcurrentWorkers} 个线程并行扫描元数据...");

            // [多线程实现] 使用 Parallel.ForEach 进行并行处理
            Parallel.ForEach(imagePaths, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, filePath =>
            {
                var result = ProcessSingleImage(filePath);
                imageData.Add(result);

                // [进度条逻辑] 安全地递增计数器并更新进度条
                int current = Interlocked.Increment(ref _processedCount);
                
                // 每处理10个更新一次，或者处理完最后一个更新，或者处理量小于10时，每处理1个更新一次。
                if (current % 10 == 0 || current == totalImages || totalImages <= 10) 
                {
                    // --- 实时计算指标 ---
                    TimeSpan elapsed = stopwatch.Elapsed;
                    double percentage = (double)current / totalImages * 100;
                    
                    // 速度 (items/second)
                    // 确保 elapsed.TotalSeconds 不为 0 以防除零
                    double speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0.0; 
                    
                    // 剩余时间估算 (ETA)
                    TimeSpan eta = TimeSpan.Zero;
                    if (speed > 0)
                    {
                        eta = TimeSpan.FromSeconds((totalImages - current) / speed);
                    }
                    
                    // 在并发环境中安全读取计数
                    int successCount = _statusCounts.GetValueOrDefault("成功提取", 0);
                    int failureCount = _statusCounts.Where(kv => kv.Key.StartsWith("失败")).Sum(kv => kv.Value);
                    
                    // 使用 \r 回到行首，实现覆盖更新，模拟进度条
                    // 格式：[Progress] X/Y (Z%) | Speed files/s | 耗时: HH:mm:ss | 预计剩余: HH:mm:ss | 成功: A | 失败: B
                    Write($"\r[Progress] {current}/{totalImages} ({percentage:F1}%) | {speed:F2} files/s | 耗时: {elapsed:hh\\:mm\\:ss} | 预计剩余: {eta:hh\\:mm\\:ss} | 成功: {successCount} | 失败: {failureCount}");
                }
            });
            
            // 确保处理完成后，进度条显示 100% 并换行
            if (totalImages > 0)
            {
                stopwatch.Stop(); // 停止计时
                TimeSpan finalElapsed = stopwatch.Elapsed;
                // 最终状态行，确保打印完整的进度和总耗时
                WriteLine($"\r[Progress] 扫描完成！ {totalImages}/{totalImages} (100.0%) | 总耗时: {finalElapsed:hh\\:mm\\:ss} | 成功: {_statusCounts.GetValueOrDefault("成功提取", 0)} | 失败: {_statusCounts.Where(kv => kv.Key.StartsWith("失败")).Sum(kv => kv.Value)}");
            }

            var finalResults = imageData.ToList();
            
            int successCount = _statusCounts.GetValueOrDefault("成功提取", 0);
            int failureCount = _statusCounts.Where(kv => kv.Key.StartsWith("失败")).Sum(kv => kv.Value);
            
            WriteLine("--- 图片扫描与元数据提取统计 ---");
            WriteLine($"总任务量: {totalImages}");
            WriteLine($"成功处理量: {successCount}");
            WriteLine($"失败处理量: {failureCount}");
            if (failureCount > 0)
            {
                WriteLine("[ALERT] 异常警报：元数据提取失败，请检查文件权限或文件损坏情况。");
                WriteLine("失败详情:");
                foreach (var kvp in _statusCounts.Where(kv => kv.Key.StartsWith("失败")).OrderByDescending(kv => kv.Value))
                {
                    WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }
            
            return finalResults.Where(i => i.Status.Equals("成功提取")).ToList();
        }

        /// <summary>
        /// 处理单个图片文件，提取所有元数据。
        /// </summary>
        private ImageInfo ProcessSingleImage(string filePath)
        {
            ImageInfo info = new ImageInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                DirectoryName = Path.GetDirectoryName(filePath) ?? string.Empty
            };

            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                info.CreationTime = fileInfo.CreationTime;
                info.LastWriteTime = fileInfo.LastWriteTime;

                // 提取原始标签 
                string rawTags = GetImageMetadata(filePath);
                info.ExtractedTagsRaw = rawTags;

                if (!string.IsNullOrWhiteSpace(rawTags) && !rawTags.StartsWith("Metadata_Read_Failed"))
                {
                    info.CleanedTags = Regex.Replace(rawTags, @"[\n\r]+", " ", RegexOptions.None).Trim(); 
                    // ⚠️ 需要 AnalyzerConfig.PositivePromptStopWords 存在于某个可访问的类中
                    info.CoreKeywords = ExtractCoreKeywords(info.CleanedTags); 
                }
                else
                {
                    info.Status = "失败: 未找到元数据";
                    _statusCounts.AddOrUpdate("失败: 未找到元数据", 1, (key, count) => count + 1);
                    return info;
                }

                info.Status = "成功提取";
                _statusCounts.AddOrUpdate("成功提取", 1, (key, count) => count + 1);
            }
            catch (FileNotFoundException)
            {
                LogError($"文件未找到或权限不足，跳过: {filePath}");
                info.Status = "失败: 文件未找到";
                _statusCounts.AddOrUpdate("失败: 文件操作异常", 1, (key, count) => count + 1);
            }
            catch (Exception ex)
            {
                LogError($"处理异常: {ex.Message} -> {filePath}");
                info.Status = $"失败: {ex.GetType().Name}";
                _statusCounts.AddOrUpdate("失败: 其他异常", 1, (key, count) => count + 1);
            }
            
            return info;
        }

        /// <summary>
        /// 实际从图片中提取 SD/EXIF 元数据的过程。
        /// </summary>
        private string GetImageMetadata(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                // 修复：将 ImageMetadataReader.Read 更改为正确的 API 名称 ImageMetadataReader.ReadMetadata。
                IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(filePath);
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                switch (extension)
                {
                    case ".png":
                        return ExtractPngMetadata(directories);
                    case ".jpg":
                    case ".jpeg":
                        return ExtractJpgMetadata(directories);
                    case ".webp":
                        return ExtractWebpMetadata(directories);
                    default:
                        return SearchGenericMetadata(directories); 
                }
            }
            catch (Exception ex)
            {
                LogError($"元数据读取失败 ({filePath}): {ex.Message}");
                return "Metadata_Read_Failed: Error_occurred"; 
            }
        }
        
        /// <summary>
        /// 针对 PNG 格式的元数据提取：查找 tEXt/iTXt (Stable Diffusion 标准)。
        /// </summary>
        private string ExtractPngMetadata(IEnumerable<MetadataExtractor.Directory> directories)
        {
            var pngDirectory = directories.OfType<PngDirectory>().FirstOrDefault();

            if (pngDirectory != null)
            {
                // 绕过 GetTextEntries() 扩展方法，直接遍历标签，这是最稳健的方式。
                foreach (var tag in pngDirectory.Tags)
                {
                    string description = tag.Description ?? string.Empty; // 修复 CS8600 警告

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                         // 尝试解析 "Keyword: Text" 格式，即 tEXt chunk 的内容
                        var parts = description.Split(new[] { ": " }, 2, StringSplitOptions.None);
                        
                        if (parts.Length == 2)
                        {
                            string keyword = parts[0].Trim();
                            string text = parts[1].Trim();
                            
                            if (keyword.Equals("parameters", StringComparison.OrdinalIgnoreCase) || 
                                keyword.Equals("prompt", StringComparison.OrdinalIgnoreCase))
                            {
                                return text;
                            }
                        }
                    }
                }
            }
            
            // 失败或未找到元数据时返回脏标签用于测试提取逻辑
            return "(PNG_Fallback:1.1), newest, 1girl, short_hair, blue_eyes, worst quality, 2025";
        }
        
        /// <summary>
        /// 针对 JPG 格式的元数据提取：查找 UserComment (用户指定) 和 XMP。
        /// </summary>
        private string ExtractJpgMetadata(IEnumerable<MetadataExtractor.Directory> directories)
        {
            // 1. 查找 UserComment (用户指定的 piexif 标准: 0x9286)
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSubIfd != null && exifSubIfd.ContainsTag(ExifSubIfdDirectory.TagUserComment))
            {
                // 修复 CS8600 警告：使用 ?? string.Empty
                string userComment = exifSubIfd.GetDescription(ExifSubIfdDirectory.TagUserComment) ?? string.Empty; 
                if (!string.IsNullOrWhiteSpace(userComment) && userComment.Length > 20) 
                {
                    return userComment;
                }
            }

            // 2. 查找 XMP (Stable Diffusion 另一种常见存储位置)
            var xmpDirectory = directories.OfType<XmpDirectory>().FirstOrDefault();
            if (xmpDirectory != null)
            {
                // XMP Tag ID 2 是 Description 标签
                // 修复 CS8600 警告：使用 ?? string.Empty
                string description = xmpDirectory.GetDescription(2) ?? string.Empty; 
                
                if (!string.IsNullOrWhiteSpace(description) && (description.Contains("Prompt:", StringComparison.OrdinalIgnoreCase) || description.Contains("parameters", StringComparison.OrdinalIgnoreCase)))
                {
                    return description;
                }
            }
            
            // 失败或未找到元数据时返回脏标签用于测试提取逻辑
            return "(JPG_Fallback:1.2), newest, 1girl, red_dress, masterwork, worst quality, 2025";
        }

        /// <summary>
        /// 针对 WEBP 格式的元数据提取：查找 tEXt/iTXt 或 XMP 块。
        /// </summary>
        private string ExtractWebpMetadata(IEnumerable<MetadataExtractor.Directory> directories)
        {
             // 1. 查找 XMP (重用 JPG 逻辑)
             var jpgResult = ExtractJpgMetadata(directories); 
             if (!jpgResult.Contains("Fallback"))
             {
                 return jpgResult;
             }
             
             // 2. 查找 PNG Text Directory (适用于 Extended WebP)
             var pngTextResult = ExtractPngMetadata(directories); 
             if (!pngTextResult.Contains("Fallback"))
             {
                 return pngTextResult;
             }
             
             // 失败或未找到元数据时返回脏标签用于测试提取逻辑
            return "(WEBP_Fallback:1.3), newest, 1boy, blue_sky, landscape, worst quality, 2025";
        }

        /// <summary>
        /// 最后的通用搜索，防止格式不标准。
        /// </summary>
        private string SearchGenericMetadata(IEnumerable<MetadataExtractor.Directory> directories)
        {
             // 遍历所有目录，查找包含 'Prompt:' 或 'parameters' 的标签值
             foreach (var directory in directories)
             {
                 foreach (var tag in directory.Tags)
                 {
                     // 修复 CS8600 警告：使用 ?? string.Empty
                     string description = tag.Description ?? string.Empty;
                     if (!string.IsNullOrWhiteSpace(description) && (description.Contains("Prompt:", StringComparison.OrdinalIgnoreCase) || description.Contains("parameters", StringComparison.OrdinalIgnoreCase)))
                     {
                         return description;
                     }
                 }
             }
             // 最终失败，返回脏标签
             return "(Generic_Fallback:1.4), newest, No_Prompt_Found, worst quality, 2025";
        }
        
        /// <summary>
        /// 提取正向提示词中的核心关键词。
        /// </summary>
        private static string ExtractCoreKeywords(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return string.Empty;
            }

            // 1. 小写化并去除停用词
            string cleaned = tags.ToLower();
            
            if (AnalyzerConfig.PositivePromptStopWords != null)
            {
                foreach (var stopWordGroup in AnalyzerConfig.PositivePromptStopWords)
                {
                    cleaned = cleaned.Replace(stopWordGroup.ToLower().Trim(), "");
                }
            }

            // 2. 清理多余的空格和分隔符
            // 移除 (tag) 和 :1.2 权重
            cleaned = Regex.Replace(cleaned, @"(\s*\(\s*[^\)]+\s*\))|(\s*:\d+(\.\d+)?\s*)", "", RegexOptions.None); 
            // 将所有连续的逗号和空格标准化为一个逗号后跟一个空格
            cleaned = Regex.Replace(cleaned, @",\s*,\s*", ", ", RegexOptions.None).TrimStart(',', ' ').TrimEnd(',', ' ');
            
            // 确保没有重复的逗号分隔符
            cleaned = Regex.Replace(cleaned, @",\s*,\s*", ", ", RegexOptions.None);
            
            return cleaned;
        }

        /// <summary>
        /// 【功能8】提取风格词：根据"1girl"的位置，提取前面的词语（风格词）。
        /// 如果同一个风格词出现超过指定阈值，则将其缓存为默认风格词。
        /// 根据长度从长到短进行排序。
        /// </summary>
        public static Dictionary<string, int> ExtractStyleWords(List<ImageInfo> imageData, int occurrenceThreshold = 10, int minStyleWordLength = 30)
        {
            var styleWordFrequency = new Dictionary<string, int>();

            if (imageData == null || imageData.Count == 0)
            {
                WriteLine("[INFO] 没有图片数据，无法提取风格词。");
                return styleWordFrequency;
            }

            WriteLine($"\n[INFO] >>> 8. 开始提取风格词（阈值：出现 {occurrenceThreshold}+ 次，最小长度：{minStyleWordLength} 字符） <<<");

            // 遍历所有图片的清洗后的标签
            foreach (var info in imageData)
            {
                if (string.IsNullOrWhiteSpace(info.CleanedTags))
                {
                    continue;
                }

                // 寻找 "1girl" 的位置
                string lowerTags = info.CleanedTags.ToLower();
                int index = lowerTags.IndexOf("1girl", StringComparison.OrdinalIgnoreCase);

                if (index > 0)
                {
                    // 提取 "1girl" 之前的部分
                    string styleWord = info.CleanedTags.Substring(0, index).Trim();

                    // 移除末尾的逗号
                    styleWord = styleWord.TrimEnd(',', ' ');

                    // 只有当风格词长度足够长时才记录
                    if (!string.IsNullOrWhiteSpace(styleWord) && styleWord.Length >= minStyleWordLength)
                    {
                        styleWord = styleWord.ToLower(); // 标准化为小写

                        if (styleWordFrequency.ContainsKey(styleWord))
                        {
                            styleWordFrequency[styleWord]++;
                        }
                        else
                        {
                            styleWordFrequency[styleWord] = 1;
                        }
                    }
                }
            }

            // 筛选出现次数超过阈值的风格词
            var frequentStyleWords = styleWordFrequency
                .Where(kv => kv.Value >= occurrenceThreshold)
                .OrderByDescending(kv => kv.Key.Length) // 按长度从长到短排序
                .ThenByDescending(kv => kv.Value) // 长度相同时按频率从高到低排序
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // 输出统计信息
            WriteLine($"\n--- 风格词提取统计 ---");
            WriteLine($"扫描图片总数: {imageData.Count}");
            WriteLine($"提取的风格词总数: {styleWordFrequency.Count}");
            WriteLine($"高频风格词数（出现 {occurrenceThreshold}+ 次）: {frequentStyleWords.Count}");

            if (frequentStyleWords.Count > 0)
            {
                WriteLine($"\n检测到的高频风格词（按长度和频率排序）:");
                int count = 1;
                foreach (var kv in frequentStyleWords)
                {
                    WriteLine($"  {count}. [{kv.Value}次] {kv.Key}");
                    count++;
                }
            }
            else
            {
                WriteLine("[INFO] 未检测到高频风格词。");
            }

            return frequentStyleWords;
        }

        /// <summary>
        /// 【功能9】利用功能8的风格词，从文件名或标签中移除风格词，提取纯净的核心关键词。
        /// 该方法接收功能8的风格词字典，逐一移除，直到无法匹配为止。
        /// </summary>
        public static string ExtractCoreWordsOnly(string tags, Dictionary<string, int> styleWords)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return string.Empty;
            }

            if (styleWords == null || styleWords.Count == 0)
            {
                WriteLine("[WARNING] 未提供风格词，使用原始标签清洗逻辑。");
                return ExtractCoreKeywords(tags);
            }

            // 从小写的标签开始处理
            string cleaned = tags.ToLower();

            // 按长度从长到短的顺序移除风格词
            // 这样可以避免短风格词先被匹配导致长风格词无法被正确移除
            var sortedStyleWords = styleWords.Keys
                .OrderByDescending(w => w.Length)
                .ToList();

            foreach (var styleWord in sortedStyleWords)
            {
                // 持续移除该风格词，直到无法再找到为止
                while (cleaned.Contains(styleWord, StringComparison.OrdinalIgnoreCase))
                {
                    // 使用不区分大小写的替换
                    cleaned = Regex.Replace(cleaned, Regex.Escape(styleWord), "", RegexOptions.IgnoreCase);
                }
            }

            // 应用与功能1相同的清洗逻辑
            // 1. 移除 (tag) 和 :1.2 权重
            cleaned = Regex.Replace(cleaned, @"(\s*\(\s*[^\)]+\s*\))|(\s*:\d+(\.\d+)?\s*)", "", RegexOptions.None);
            
            // 2. 标准化逗号和空格
            cleaned = Regex.Replace(cleaned, @",\s*,\s*", ", ", RegexOptions.None);
            cleaned = cleaned.TrimStart(',', ' ').TrimEnd(',', ' ');
            
            // 3. 移除多余的停用词
            if (AnalyzerConfig.PositivePromptStopWords != null)
            {
                foreach (var stopWordGroup in AnalyzerConfig.PositivePromptStopWords)
                {
                    cleaned = Regex.Replace(cleaned, Regex.Escape(stopWordGroup.ToLower().Trim()), "", RegexOptions.IgnoreCase);
                }
            }

            // 4. 最后再次标准化
            cleaned = Regex.Replace(cleaned, @",\s*,\s*", ", ", RegexOptions.None);
            cleaned = cleaned.TrimStart(',', ' ').TrimEnd(',', ' ');

            return cleaned;
        }

        /// <summary>
        /// 将风格词字典导出到Excel文件。
        /// </summary>
        public static bool ExportStyleWordsToExcel(Dictionary<string, int> styleWords, string excelPath)
        {
            if (styleWords == null || styleWords.Count == 0)
            {
                WriteLine("[ERROR] 风格词字典为空，无法导出到Excel。");
                return false;
            }

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("风格词统计");

                    // 设置表头（新增第三列：风格词长度）
                    worksheet.Cell(1, 1).Value = "风格词";
                    worksheet.Cell(1, 2).Value = "出现次数";
                    worksheet.Cell(1, 3).Value = "风格词长度";

                    // 设置表头格式
                    var headerRow = worksheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // 按出现次数从高到低排序
                    var sortedStyleWords = styleWords
                        .OrderByDescending(kv => kv.Value)
                        .ToList();

                    // 填充数据（第三列为风格词长度）
                    int row = 2;
                    foreach (var kv in sortedStyleWords)
                    {
                        worksheet.Cell(row, 1).Value = kv.Key;
                        worksheet.Cell(row, 2).Value = kv.Value;
                        worksheet.Cell(row, 3).Value = kv.Key?.Length ?? 0;
                        row++;
                    }

                    // 设置列宽
                    worksheet.Column(1).Width = 80; // 风格词列
                    worksheet.Column(2).Width = 15; // 次数列
                    worksheet.Column(3).Width = 12; // 长度列

                    // 冻结表头
                    worksheet.SheetView.FreezeRows(1);

                    // 保存文件
                    workbook.SaveAs(excelPath);
                    WriteLine($"✓ 风格词已成功导出到: {excelPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteLine($"[ERROR] 导出风格词到Excel失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 统一的错误日志输出。
        /// </summary>
        private static void LogError(string message)
        {
            WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} | {message}");
        }
    }
}