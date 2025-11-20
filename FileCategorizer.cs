// 文件名：FileCategorizer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent; // 修复错误 CS0246: 未能找到类型或命名空间名“ConcurrentDictionary<,>”

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

        private static bool IsPathProtected(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            string directoryName = Path.GetFileName(Path.GetDirectoryName(filePath))?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(directoryName)) return false;

            return AnalyzerConfig.ProtectedFolderNames.Any(p => p.Equals(directoryName, StringComparison.OrdinalIgnoreCase))
                || AnalyzerConfig.FuzzyProtectedKeywords.Any(k => directoryName.Contains(k));
        }

        private void ProcessSingleCategorization(ImageInfo imageInfo, string rootDirectory)
        {
            imageInfo.Status = "未分类/未移动";

            try
            {
                if (IsPathProtected(imageInfo.FilePath))
                {
                    imageInfo.Status = $"安全跳过 (保护路径): {Path.GetFileName(imageInfo.DirectoryName)}";
                    _statusCounts.AddOrUpdate("安全跳过 (保护路径)", 1, (key, count) => count + 1);
                    return;
                }

                string? firstKeyword = imageInfo.CleanedTags.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                                          .Select(t => t.Trim())
                                                          .FirstOrDefault();

                string targetDir = string.IsNullOrEmpty(firstKeyword) 
                    ? Path.Combine(rootDirectory, AnalyzerConfig.UnclassifiedFolderName)
                    : Path.Combine(rootDirectory, firstKeyword);

                if ((string.IsNullOrEmpty(firstKeyword) && imageInfo.DirectoryName.EndsWith(AnalyzerConfig.UnclassifiedFolderName, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(firstKeyword) && imageInfo.DirectoryName.EndsWith(firstKeyword, StringComparison.OrdinalIgnoreCase)))
                {
                    imageInfo.Status = "因路径相同而跳过I/O";
                    _statusCounts.AddOrUpdate("因路径相同而跳过I/O", 1, (key, count) => count + 1);
                    return;
                }

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string uniqueFilename = FilenameTagger.GetUniqueFilename(targetDir, imageInfo.FileName);
                string newPath = Path.Combine(targetDir, uniqueFilename);
                
                File.Move(imageInfo.FilePath, newPath);
                imageInfo.FilePath = newPath;
                imageInfo.DirectoryName = targetDir;
                imageInfo.Status = "成功分类并移动";
                _statusCounts.AddOrUpdate("成功分类并移动", 1, (key, count) => count + 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [{imageInfo.FileName}] 移动失败/其他异常: {ex.Message}");
                imageInfo.Status = "移动失败/其他异常";
                _statusCounts.AddOrUpdate("移动失败/其他异常", 1, (key, count) => count + 1);
            }
        }

        public void CategorizeAndMoveImages(List<ImageInfo> imageData, string rootDirectory)
        {
            Console.WriteLine($"\n>>> 开始图片两级分类操作到根目录: {rootDirectory}");
            if (!imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过分类。");
                return;
            }

            Parallel.ForEach(imageData, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, info =>
            {
                ProcessSingleCategorization(info, rootDirectory);
            });

            int classifiedCount = _statusCounts.GetValueOrDefault("成功分类并移动", 0);
            int skippedIo = _statusCounts.GetValueOrDefault("因路径相同而跳过I/O", 0);
            int skippedProtected = _statusCounts.GetValueOrDefault("安全跳过 (保护路径)", 0);
            int failedCount = _statusCounts.GetValueOrDefault("移动失败/其他异常", 0);

            Console.WriteLine("\n--- 图片分类操作完成 ---");
            Console.WriteLine($"总共处理图片: {imageData.Count} 张");
            Console.WriteLine($"因路径相同而跳过I/O: {skippedIo} 张");
            Console.WriteLine($"安全跳过 (保护路径): {skippedProtected} 张");
            Console.WriteLine($"成功分类并移动: {classifiedCount} 张");
            Console.WriteLine($"移动失败/其他异常: {failedCount} 张");

            if (failedCount > 0)
                Console.WriteLine("[ALERT] 异常警报：文件分类或移动操作失败，请检查文件权限或路径问题。");
        }
    }
}