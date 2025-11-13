// 文件名：FileCategorizer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件分类器：负责根据图片的核心关键词（通常是第一个关键词）将文件移动到对应的子文件夹。
    /// 对应 Python 源码中的 file_categorizer.py。
    /// 难度系数：5/10 (涉及文件I/O操作和复杂的安全检查)
    /// </summary>
    public class FileCategorizer
    {
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// 检查路径是否被配置为受保护路径，不允许自动移动。
        /// 对应 Python 源码中的 is_path_protected 函数。
        /// </summary>
        /// <param name="filePath">文件的完整路径。</param>
        private static bool IsPathProtected(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            string directoryName = Path.GetFileName(Path.GetDirectoryName(filePath))?.ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(directoryName)) return false;

            // 1. 完整匹配检查 (如 "超级精选")
            if (AnalyzerConfig.ProtectedFolderNames.Any(p => p.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 2. 模糊匹配检查 (如 包含 "特殊" 的文件夹名)
            if (AnalyzerConfig.FuzzyProtectedKeywords.Any(k => directoryName.Contains(k)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 处理单个文件的分类和移动操作。
        /// 对应 Python 源码中的 process_single_categorization 函数。
        /// </summary>
        private void ProcessSingleCategorization(ImageInfo imageInfo, string rootDirectory)
        {
            void LogError(string msg) => Console.WriteLine($"[ERROR] [{imageInfo.FileName}] {msg}");
            
            // 默认状态：安全跳过
            imageInfo.Status = "未分类/未移动";

            try
            {
                string currentPath = imageInfo.FilePath;
                string currentDir = imageInfo.DirectoryName;
                string filename = imageInfo.FileName;

                // 1. 安全检查：受保护路径检查 (对应 Python 源码的 if is_path_protected)
                if (IsPathProtected(currentPath))
                {
                    imageInfo.Status = $"安全跳过 (保护路径): {Path.GetFileName(currentDir)}";
                    _statusCounts.AddOrUpdate("安全跳过 (保护路径)", 1, (key, count) => count + 1);
                    return;
                }

                // 2. 确定分类目标关键词 (取 CleanedTags 中的第一个词)
                // 对应 Python 源码中的 first_keyword = tags[0]
                string firstKeyword = imageInfo.CleanedTags.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                                          .Select(t => t.Trim())
                                                          .FirstOrDefault();

                string targetDir;
                string statusKey;

                // 3. 确定目标目录
                if (string.IsNullOrEmpty(firstKeyword))
                {
                    // 无法分类：移入 '未分类' 目录
                    targetDir = Path.Combine(rootDirectory, AnalyzerConfig.UnclassifiedFolderName);
                    statusKey = "成功移入/保留在 '未分类' 目录";
                    
                    if (currentDir.EndsWith(AnalyzerConfig.UnclassifiedFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 如果文件已在未分类目录，则跳过 I/O
                        imageInfo.Status = "因路径相同而跳过I/O (未分类)";
                        statusKey = "因路径相同而跳过I/O";
                        _statusCounts.AddOrUpdate(statusKey, 1, (key, count) => count + 1);
                        return;
                    }
                }
                else
                {
                    // 成功分类：移入 关键词/作者 目录
                    // 目标目录结构: rootDirectory / [关键词]
                    targetDir = Path.Combine(rootDirectory, firstKeyword);
                    statusKey = "成功分类到关键词目录";

                    if (currentDir.EndsWith(firstKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        // 如果文件已在正确分类目录，则跳过 I/O
                        imageInfo.Status = "因路径相同而跳过I/O (已分类)";
                        statusKey = "因路径相同而跳过I/O";
                        _statusCounts.AddOrUpdate(statusKey, 1, (key, count) => count + 1);
                        return;
                    }
                }

                // 4. 确保目标目录存在
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // 5. 解决文件名冲突并确定最终路径
                // ⚠️ 依赖 FilenameTagger 中的 GetUniqueFilename 函数 (此处为简化，直接使用原始文件名)
                // 实际应使用 FilenameTagger.GetUniqueFilename(targetDir, filename)
                // 考虑到我们是移动文件，冲突解决至关重要
                string uniqueFilename = FilenameTagger.GetUniqueFilename(targetDir, filename);
                string newPath = Path.Combine(targetDir, uniqueFilename);
                
                // 6. 移动文件
                File.Move(currentPath, newPath);
                
                // 7. 更新 ImageInfo
                imageInfo.FilePath = newPath;
                imageInfo.DirectoryName = targetDir;
                imageInfo.Status = statusKey;
                _statusCounts.AddOrUpdate(statusKey, 1, (key, count) => count + 1);

                Console.WriteLine($"[MOVE] {filename} -> {Path.GetFileName(targetDir)}/{uniqueFilename}");
            }
            catch (Exception ex)
            {
                LogError($"移动失败/其他异常: {ex.Message}");
                imageInfo.Status = "移动失败/其他异常";
                _statusCounts.AddOrUpdate("移动失败/其他异常", 1, (key, count) => count + 1);
            }
        }

        /// <summary>
        /// 启动图片的两级分类和移动过程。
        /// 对应 Python 源码中的 categorize_images 函数。
        /// </summary>
        /// <param name="imageData">包含所有图片信息的列表。</param>
        /// <param name="rootDirectory">扫描的根目录（分类目标目录将在此目录下创建）。</param>
        public void CategorizeAndMoveImages(List<ImageInfo> imageData, string rootDirectory)
        {
            Console.WriteLine($"\n>>> 开始图片两级分类操作到根目录: {rootDirectory}");
            
            if (!imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过分类。");
                return;
            }

            int totalImages = imageData.Count;
            
            // 1. 阶段：并行处理每个文件的分类和移动
            Parallel.ForEach(imageData, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, info =>
            {
                ProcessSingleCategorization(info, rootDirectory);
            });

            // 2. 阶段：打印最终统计结果 (满足用户要求的计数器格式)
            int classifiedCount = _statusCounts.GetValueOrDefault("成功分类到关键词目录", 0);
            int unclassifiedCount = _statusCounts.GetValueOrDefault("成功移入/保留在 '未分类' 目录", 0);
            int skippedProtected = _statusCounts.GetValueOrDefault("安全跳过 (保护路径)", 0);
            int skippedIo = _statusCounts.GetValueOrDefault("因路径相同而跳过I/O", 0);
            int failedCount = _statusCounts.GetValueOrDefault("移动失败/其他异常", 0);

            Console.WriteLine("\n--- 图片两级分类操作完成 ---");
            Console.WriteLine($"总共处理图片: {totalImages} 张");
            Console.WriteLine($"因路径相同而跳过I/O: {skippedIo} 张");
            Console.WriteLine($"【安全跳过】的图片数量: {skippedProtected} 张");
            Console.WriteLine($"成功分类到关键词目录: {classifiedCount} 张");
            Console.WriteLine($"成功移入/保留在 '未分类' 目录: {unclassifiedCount} 张");
            Console.WriteLine($"移动失败/其他异常: {failedCount} 张");

            if (failedCount > 0)
            {
                Console.WriteLine("[ALERT] 异常警报：文件分类或移动操作失败，请检查文件权限或路径问题。");
            }
        }
    }
}