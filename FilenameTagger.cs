// 文件名：FilenameTagger.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件名打标器：负责根据提取的标签和TF-IDF结果，构建新的文件名，并处理重命名冲突。
    /// 对应 Python 源码中的 filename_tagger.py。
    /// </summary>
    public static class FilenameTagger
    {
        // 用于防止文件冲突的后缀计数器
        private static readonly Dictionary<string, int> ConflictCounters = new Dictionary<string, int>();

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
            string uniquePath = Path.Combine(targetDir, filename);

            int counter = 0;
            string key = name.ToLowerInvariant(); // 使用基础名作为计数器的键

            // 1. 检查文件是否已存在
            if (File.Exists(uniquePath))
            {
                // 2. 如果存在，检查计数器缓存
                if (ConflictCounters.ContainsKey(key))
                {
                    counter = ConflictCounters[key];
                } 
                else
                {
                    // 3. 如果不在缓存中，从 (1) 开始查找直到找到一个不存在的数字
                    // ⚠️ 这里简化处理：直接从当前数字+1开始，避免每次都从1开始扫描
                    // 实际 Python 源码可能采用更复杂的基于磁盘的查找，这里我们依赖缓存。
                    counter = 0; // 从0开始，避免重复计算
                }

                // 4. 循环查找直到找到一个不存在的文件名
                do
                {
                    counter++;
                    string newName = $"{name}({counter}){ext}";
                    uniquePath = Path.Combine(targetDir, newName);
                } while (File.Exists(uniquePath));
                
                // 5. 更新计数器缓存
                ConflictCounters[key] = counter;

                // Loguru 替代 (使用简单的 Console 输出)
                Console.WriteLine($"[CONFLICT] 目标文件 '{filename}' 已存在，重命名为: {Path.GetFileName(uniquePath)}");
            }
            
            return Path.GetFileName(uniquePath);
        }
        
        /// <summary>
        /// 根据标签和 TF-IDF 结果给图片文件打标并重命名。
        /// 对应 Python 源码中的 tag_image_file 函数。
        /// </summary>
        /// <param name="imageInfo">包含图片信息的对象，该对象会被更新。</param>
        /// <param name="tfidfTags">TF-IDF 提取的标签列表 (后续步骤中提供)。</param>
        /// <param name="totalImages">总图片数 (用于日志输出)。</param>
        /// <param name="index">当前处理索引 (用于日志输出)。</param>
        /// <returns>重命名是否成功 (或安全跳过)。</returns>
        public static bool TagImageFile(ImageInfo imageInfo, List<string> tfidfTags, int totalImages, int index)
        {
            string currentPath = imageInfo.FilePath;
            string imageFilename = imageInfo.FileName;
            string imageDir = imageInfo.DirectoryName;
            
            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            {
                Console.WriteLine($"[ERROR] 文件不存在或路径无效: {currentPath}");
                return false;
            }

            // 1. 根据 ImageInfo 中的标签匹配预设的打标关键词
            // 对应 Python 中的 tag_image_file 函数的 1-3 步
            var matchedKeywords = AnalyzerConfig.TaggingKeywords
                .Where(keyword => imageInfo.ExtractedTagsRaw.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            // 2. 格式化匹配到的关键词作为后缀
            string keywordSuffix = matchedKeywords.Any() 
                ? AnalyzerConfig.TagDelimiter + string.Join(AnalyzerConfig.TagDelimiter, matchedKeywords)
                : string.Empty;

            // 3. 格式化 TF-IDF 标签作为后缀
            string tfidfSuffix = tfidfTags.Any()
                ? AnalyzerConfig.TagDelimiter + string.Join(AnalyzerConfig.TagDelimiter, tfidfTags)
                : string.Empty;

            // 4. 合并所有后缀
            string finalSuffix = keywordSuffix + tfidfSuffix; 
            
            // 5. 解析原始基础文件名 (不含任何已有标签后缀)
            // 对应 Python 中的 _parse_base_filename 函数
            var (baseNameWithoutTags, ext) = ParseBaseFilename(imageFilename, AnalyzerConfig.TagDelimiter);
            
            // 6. 确定新的完整文件名
            string newBaseName = baseNameWithoutTags + finalSuffix;
            string newFilename = newBaseName + ext;
            string newImagePath = Path.Combine(imageDir, newFilename);
            
            // 7. 【幂等性保护机制】检查是否已经等于目标文件名
            // 对应 Python 中的 if current_image_path == new_image_path:
            if (currentPath.Equals(newImagePath, StringComparison.OrdinalIgnoreCase))
            {
                // 日志输出 (模仿 Python 的日志)
                Console.WriteLine($"[SKIP] ({index}/{totalImages}): '{imageFilename}' 标签已精确匹配，跳过重命名。");
                return true; // 视作成功操作
            }

            // 8. 执行重命名操作
            try
            {
                // 检查目标文件名是否已存在 (避免重命名覆盖)
                if (File.Exists(newImagePath))
                {
                    // Python 源码是跳过重命名，避免冲突
                    Console.WriteLine($"[WARN] 目标文件名 '{newFilename}' 已存在，跳过重命名以避免冲突。");
                    return false;
                }
                
                File.Move(currentPath, newImagePath);
                
                // 9. 更新 image_data 中的路径信息
                imageInfo.FilePath = newImagePath;
                imageInfo.FileName = newFilename;

                Console.WriteLine($"[SUCCESS] ({index}/{totalImages}): 重命名 '{imageFilename}' -> '{newFilename}'");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 重命名失败 '{imageFilename}' -> '{newFilename}'. 错误: {ex.Message}");
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
            string pattern = $@"({escapedDelimiter}[^{escapedDelimiter}]*)+$"; 
            
            string cleanedBaseName = Regex.Replace(baseName, pattern, "", RegexOptions.IgnoreCase);

            return (cleanedBaseName, ext);
        }
    }
}