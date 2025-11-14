// 文件名：FilenameTagger.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks; 
using System.Threading; 
using System.Collections.Concurrent; 
// 确保引用 ImageInfo 所在的命名空间
using ImageAnalyzerCore; 

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件名打标器：负责根据提取的标签和TF-IDF结果，构建新的文件名，并处理重命名冲突。
    /// 对应 Python 源码中的 filename_tagger.py。
    /// </summary>
    public static class FilenameTagger
    {
        // 使用 ConcurrentDictionary 确保在 Parallel.ForEach 中的多线程安全性
        private static readonly ConcurrentDictionary<string, int> ConflictCounters = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>(); // 用于统计重命名状态

        /// <summary>
        /// [批量功能] 根据TF-IDF结果批量重命名文件。
        /// 对应 Python 源码中的 filename_tagger.TagFiles 函数。
        /// </summary>
        /// <param name="imageData">ImageScanner扫描到的图片信息列表（包含 PredictedScore）。</param>
        /// <param name="tfidfTagsMap">TF-IDF处理器返回的关键词映射。</param>
        /// <param name="includeScore">【修复 Program.cs Bug #2】是否在文件名中包含预测评分。</param>
        public static void TagFiles(List<ImageInfo> imageData, Dictionary<string, List<string>> tfidfTagsMap, bool includeScore = false)
        {
            if (imageData == null || !imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过文件名标记。");
                return;
            }

            Console.WriteLine($"[INFO] 开始执行文件名标记操作。包含评分: {includeScore}");
            _statusCounts.Clear(); // 清空状态计数器

            // 并行处理所有图片文件
            Parallel.ForEach(imageData, info =>
            {
                // CoreKeywords 已经在 ImageInfo 中，可以直接使用
                if (string.IsNullOrWhiteSpace(info.CoreKeywords))
                {
                    _statusCounts.AddOrUpdate("跳过: 无CoreKeywords", 1, (k, v) => v + 1);
                    return;
                }

                // 1. 构造标签部分 (使用 CoreKeywords)
                // 将标签分隔符标准化为 ___
                string tagString = info.CoreKeywords.Replace(", ", "___").Replace(",", "___").Trim('_');
                
                // 2. 构造评分部分 (如果需要)
                string scorePrefix = string.Empty;
                // 仅当 includeScore 为 true 且预测评分大于 0 时才添加
                if (includeScore && info.PredictedScore > 0)
                {
                    // 格式化为 X.X分 (例如: (95.5分))
                    scorePrefix = $"({info.PredictedScore:F1}分)"; 
                }
                
                // 3. 构建新的文件名基础部分
                string oldFileNameWithoutExt = Path.GetFileNameWithoutExtension(info.FilePath);
                string extension = Path.GetExtension(info.FilePath);

                // 移除原文件名中可能存在的旧评分或标签，防止重复。
                string baseName = RemoveExistingTagsAndScores(oldFileNameWithoutExt);
                
                // 新的文件名格式: [清理后的原文件名][评分部分]_[TF-IDF关键词]
                string newFileNameWithoutExt = $"{baseName}{scorePrefix}_{tagString}";
                
                // 4. 处理文件名冲突和构建最终路径
                // 这里调用了 EnsureUniqueFilename，它内部包含了防止重复的逻辑
                string finalUniqueFileName = EnsureUniqueFilename(newFileNameWithoutExt, extension, info.DirectoryName);
                string newFilePath = Path.Combine(info.DirectoryName, finalUniqueFileName);

                // 5. 执行重命名操作
                try
                {
                    if (!info.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(info.FilePath, newFilePath);
                        _statusCounts.AddOrUpdate("成功标记并重命名", 1, (k, v) => v + 1);
                    }
                    else
                    {
                         _statusCounts.AddOrUpdate("跳过: 文件名未更改", 1, (k, v) => v + 1);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 重命名失败: {info.FilePath} -> {finalUniqueFileName}. 错误: {ex.Message}");
                    _statusCounts.AddOrUpdate("失败: I/O异常", 1, (k, v) => v + 1);
                }
            });

            Console.WriteLine("\n--- 文件名标记统计 ---");
            Console.WriteLine($"总任务量: {imageData.Count}");
            Console.WriteLine($"成功标记并重命名: {_statusCounts.GetValueOrDefault("成功标记并重命名", 0)}");
            Console.WriteLine($"失败量: {_statusCounts.GetValueOrDefault("失败: I/O异常", 0)}");
        }

        /// <summary>
        /// [修复 Program.cs Bug] 确保文件名在目标目录下是唯一的，并在冲突时添加计数后缀 (例如 file (1).ext)。
        /// </summary>
        /// <param name="targetDir">目标目录的完整路径。</param>
        /// <param name="filename">原始文件名 (包含扩展名，例如: image.png)。</param>
        /// <returns>唯一的、带扩展名的文件名。</returns>
        public static string GetUniqueFilename(string targetDir, string filename)
        {
            // 1. 检查文件是否已存在
            string fullPath = Path.Combine(targetDir, filename);

            if (!File.Exists(fullPath))
            {
                return filename;
            }

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filename);
            string ext = Path.GetExtension(filename);
            int counter = 1;

            string uniqueFilename;
            string uniquePath;
            
            // 2. 循环直到找到唯一名称
            do
            {
                // 构造带后缀的新文件名
                uniqueFilename = $"{fileNameWithoutExt} ({counter}){ext}";
                uniquePath = Path.Combine(targetDir, uniqueFilename);
                counter++;
            } 
            // 循环条件：文件仍存在 且 计数器未超限 (安全保护)
            while (File.Exists(uniquePath) && counter < 10000); 

            return uniqueFilename;
        }

        /// <summary>
        /// 移除旧的评分和标签后缀，以获取干净的文件名基础。
        /// </summary>
        private static string RemoveExistingTagsAndScores(string baseName)
        {
            // 匹配并移除 "(X.X分)" 格式的评分
            string cleaned = Regex.Replace(baseName, @"\s*\(\d{1,3}\.\d分\)", "", RegexOptions.IgnoreCase);
            
            // 匹配并移除末尾的 "_tagA___tagB" 格式的标签
            // 移除末尾可能的 _[标签组] 部分
            cleaned = Regex.Replace(cleaned, @"(_+[\w]+)*$", "", RegexOptions.IgnoreCase).TrimEnd('_');

            return cleaned;
        }


        /// <summary>
        /// 确保新文件名在目标目录下是唯一的，并在冲突时添加计数后缀。
        /// 此方法主要供 TagFiles 内部使用，处理已包含 TF-IDF 标签的文件名。
        /// </summary>
        private static string EnsureUniqueFilename(string newFileNameWithoutExt, string ext, string directoryName)
        {
             // 清理文件名中不允许的字符
            string cleanedBaseName = Regex.Replace(newFileNameWithoutExt, @"[^\w\-\.\s\(\)_]", "", RegexOptions.None).Trim('_');

            string fullPath = Path.Combine(directoryName, cleanedBaseName + ext);
            if (!File.Exists(fullPath))
            {
                return cleanedBaseName + ext;
            }

            int counter = 1;
            string uniquePath;
            do
            {
                string tentativeName = $"{cleanedBaseName} ({counter})";
                uniquePath = Path.Combine(directoryName, tentativeName + ext);
                counter++;
            } while (File.Exists(uniquePath) && counter < 1000); 

            return Path.GetFileName(uniquePath);
        }
    }
}