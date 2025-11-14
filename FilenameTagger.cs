// 文件名：FilenameTagger.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks; 
using System.Threading; 
using System.Collections.Concurrent; 

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

        /// <summary>
        /// [批量功能] 根据TF-IDF结果批量重命名文件。
        /// 对应 Python 源码中的 filename_tagger.TagFiles 函数。
        /// 难度系数：3/10 (线程安全修复)
        /// </summary>
        /// <param name="imageData">ImageScanner扫描到的图片信息列表。</param>
        /// <param name="tfidfTagsMap">TF-IDF处理器返回的关键词映射。</param>
        public static void TagFiles(List<ImageInfo> imageData, Dictionary<string, List<string>> tfidfTagsMap)
        {
            if (imageData == null || !imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过文件重命名。");
                return;
            }

            int totalImages = imageData.Count;
            // 使用 Interlocked 确保计数器在并行中的安全性
            int successCount = 0; 
            
            // 使用并行处理加快文件操作速度
            Parallel.ForEach(imageData, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, (info, state, index) =>
            {
                try
                {
                    // 1. 获取 TF-IDF 关键词：使用 'out var' 进行内联声明，避免 CS8600 错误。
                    //    结合空值检查 (tags != null) 解决 CS8601 警告，并且代码更简洁。
                    if (tfidfTagsMap.TryGetValue(info.FilePath, out var tags) && tags != null)
                    {
                        // 2. 格式化标签并调用核心重命名逻辑
                        string tagSuffix = TfidfProcessor.FormatTfidfTagsForFilename(tags, AnalyzerConfig.TagDelimiter);
                        
                        bool success = ProcessSingleImageRename(info, tagSuffix, (int)index, totalImages);
                        if (success)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                    }
                    else
                    {
                        // 没有 TF-IDF 结果或标签列表为 null，跳过重命名
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 批量重命名文件 '{info.FilePath}' 失败. 错误: {ex.Message}");
                }
            });

            // 打印最终统计结果
            Console.WriteLine($"\n--- 文件批量重命名完成 ---");
            Console.WriteLine($"总共处理图片: {totalImages} 张");
            Console.WriteLine($"成功重命名: {successCount} 张");
            Console.WriteLine($"失败/跳过: {totalImages - successCount} 张");
        }

        /// <summary>
        /// [核心功能] 解决文件冲突：当目标文件已存在时，添加递增后缀 (e.g., file(1).png)。
        /// 对应 Python 源码中的 get_unique_filename 函数。
        /// </summary>
        /// <param name="targetDir">目标目录。</param>
        /// <param name="filename">原始文件名 (含扩展名)。</param>
        /// <returns>一个在目标目录中独一无二的文件名。</returns>
        public static string GetUniqueFilename(string targetDir, string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return string.Empty;
            }
            
            string name = Path.GetFileNameWithoutExtension(filename);
            string ext = Path.GetExtension(filename);
            
            // 确保目录存在 (GetUniqueFilename 应只处理文件名，但为了安全考虑，保留检查)
            if (!Directory.Exists(targetDir))
            {
                 // 如果目录不存在，无法检查冲突，直接返回原始文件名
                 return filename;
            }
            
            string uniquePath = Path.Combine(targetDir, filename);

            // 检查目标文件是否已存在
            if (!File.Exists(uniquePath))
            {
                return filename; // 文件名唯一
            }

            // 文件名冲突，开始添加递增后缀
            // key 基于基础文件名和目录，以确保不同目录下的相同文件名能独立计数
            string baseKey = Path.Combine(targetDir, name).ToLowerInvariant();
            
            // 递增计数器并获取新值
            // 使用 ConcurrentDictionary 的 AddOrUpdate 确保原子操作
            int count = ConflictCounters.AddOrUpdate(
                baseKey, 
                1, // 如果键不存在，则从 1 开始
                (key, existingVal) => existingVal + 1 // 如果键存在，则递增
            );
            
            // 构造新的唯一文件名
            string newName = $"{name} ({count}){ext}";
            
            // 考虑到 GetUniqueFilename 可能会被递归调用（虽然本场景中没有），
            // 这里的实现返回当前的递增结果，简化处理。
            return newName; 
        }

        /// <summary>
        /// [核心功能] 执行单个文件的重命名操作，并更新 ImageInfo 数据。
        /// 对应 Python 源码中的 TagFiles 中的主要循环体。
        /// </summary>
        /// <param name="imageInfo">要处理的图片信息对象。</param>
        /// <param name="tfidfSuffix">TF-IDF关键词格式化的后缀字符串。</param>
        /// <param name="index">当前处理序号（用于日志输出）。</param>
        /// <param name="totalImages">总图片数量（用于日志输出）。</param>
        /// <returns>如果重命名成功或无需重命名则返回 true，否则返回 false。</returns>
        private static bool ProcessSingleImageRename(ImageInfo imageInfo, string tfidfSuffix, int index, int totalImages)
        {
            string currentImagePath = imageInfo.FilePath;
            string imageFilename = imageInfo.FileName; // 当前文件名
            string imageDir = imageInfo.DirectoryName;  // 当前目录

            if (string.IsNullOrWhiteSpace(currentImagePath) || string.IsNullOrWhiteSpace(imageDir))
            {
                Console.WriteLine($"[WARN] ({index}/{totalImages}): 文件路径信息缺失，跳过重命名。");
                return false;
            }

            // 1. 获取原始标签作为前置后缀 (对应 Python 源码中的 tag_suffix 逻辑)
            string tagSuffix = FormatOriginalTags(imageInfo.ExtractedTagsRaw, AnalyzerConfig.TagDelimiter);

            // 2. 组合最终后缀: 原始标签后缀 + TFIDF标签后缀
            // 示例: (___tag1___tag2) + (___tagA___tagB)
            string finalSuffix = tagSuffix + tfidfSuffix; 

            // 3. 解析原始基础文件名 (不含任何标签后缀)
            (string baseNameWithoutTags, string ext) = ParseBaseFilename(imageFilename, AnalyzerConfig.TagDelimiter); 
            
            // 4. 确定新的完整文件名 (使用干净的基础名 + 最终后缀)
            string newBaseName = baseNameWithoutTags + finalSuffix; // 核心文件名 + 最终后缀
            string newFilename = newBaseName + ext;
            
            // 5. 使用 GetUniqueFilename 确保新文件名在目标目录中是独一无二的
            // 即使目标文件名已存在，GetUniqueFilename 也会返回带 (n) 后缀的唯一文件名
            string uniqueNewFilename = GetUniqueFilename(imageDir, newFilename);
            string newImagePath = Path.Combine(imageDir, uniqueNewFilename);

            // 6. 【幂等性保护机制】检查是否已经等于目标文件名
            // 必须使用 GetUniqueFilename 返回的路径进行对比
            if (currentImagePath.Equals(newImagePath, StringComparison.OrdinalIgnoreCase))
            {
                 Console.WriteLine($"[INFO] ({index}/{totalImages}): 重命名 '{imageFilename}' 标签已精确匹配，跳过重命名。");
                 return true; 
            }
            
            // 7. 执行重命名操作
            try
            {
                File.Move(currentImagePath, newImagePath);
                
                // 8. 更新 image_data 中的路径信息
                imageInfo.FilePath = newImagePath;
                imageInfo.FileName = uniqueNewFilename; // 使用唯一的新文件名

                Console.WriteLine($"[SUCCESS] ({index}/{totalImages}): 重命名 '{imageFilename}' -> '{uniqueNewFilename}'");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] ({index}/{totalImages}): 重命名失败 '{imageFilename}' -> '{newFilename}'. 错误: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 解析文件名，去除已有的标记后缀，获取干净的基础文件名。
        /// 对应 Python 源码中的 _parse_base_filename 内部函数。
        /// </summary>
        private static (string baseName, string ext) ParseBaseFilename(string filename, string delimiter)
        {
            string ext = Path.GetExtension(filename);
            string baseName = Path.GetFileNameWithoutExtension(filename);

            // 尝试去除所有已知的标签后缀
            // 使用正则表达式匹配: [基础名] + (___tagA___tagB) + [扩展名]
            // C# 的 Regex.Escape 用于处理分隔符中的特殊字符
            string escapedDelimiter = Regex.Escape(delimiter);
            
            // 匹配模式：(Delimiter + 至少一个非Delimiter字符)+，直到文件名末尾
            // e.g., name___tag1___tag2
            string pattern = $@"(?:{escapedDelimiter}[^{escapedDelimiter}]*)+$"; 
            
            string cleanedBaseName = Regex.Replace(baseName, pattern, string.Empty, RegexOptions.IgnoreCase);

            // 检查：如果清理后的名字和原始名字相同，则说明没有标签后缀
            if (string.IsNullOrEmpty(cleanedBaseName) || cleanedBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            {
                // 再次检查文件名中可能存在的自定义评分标记
                // 模式：[基础名] + @@@评分XX (例如: file@@@评分95)
                string scorePattern = $@"{Regex.Escape(AnalyzerConfig.CustomScorePrefix)}\d+(\.\d+)?$";
                cleanedBaseName = Regex.Replace(baseName, scorePattern, string.Empty, RegexOptions.IgnoreCase);

                if (string.IsNullOrEmpty(cleanedBaseName) || cleanedBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    // 最终返回原始基础名
                    return (baseName, ext);
                }
            }
            
            // 返回清理后的基础名和扩展名
            return (cleanedBaseName, ext);
        }

        /// <summary>
        /// [核心功能] 从原始标签字符串中提取用户设定的关键词（例如画师名），并格式化为文件名后缀。
        /// 对应 Python 源码中的 TagFiles 内部的原始标签处理逻辑。
        /// </summary>
        /// <param name="rawTags">原始标签字符串 (e.g., tagA, tagB, artist_name)。</param>
        /// <param name="delimiter">用于分隔标签的字符串 (e.g., ___)</param>
        /// <returns>格式化后的标签后缀字符串 (e.g., ___artist_name) 或空字符串。</returns>
        private static string FormatOriginalTags(string rawTags, string delimiter)
        {
            if (string.IsNullOrWhiteSpace(rawTags))
            {
                return string.Empty;
            }

            var tags = rawTags.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.Trim().ToLowerInvariant())
                              .ToHashSet(); // 去重和标准化

            var foundTags = new List<string>();

            // 遍历配置中的关键词，看原始标签中是否包含
            foreach (var keyword in AnalyzerConfig.TaggingKeywords)
            {
                if (tags.Contains(keyword.ToLowerInvariant()))
                {
                    // 找到匹配的关键词
                    foundTags.Add(keyword);
                }
            }

            if (foundTags.Any())
            {
                // 格式化为后缀：___tagA___tagB
                return delimiter + string.Join(delimiter, foundTags.Select(tag => tag.Replace(" ", "_")));
            }

            return string.Empty;
        }
    }
}