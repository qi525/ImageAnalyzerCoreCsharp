// 文件名：TfidfProcessor.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading.Tasks;

// 假设我们使用 ML.NET 或自定义结构来处理 TF-IDF 向量
// using Microsoft.ML.Data; 
// using Microsoft.ML.Transforms.Text;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// TF-IDF处理器：负责使用TF-IDF算法从清洗后的标签中提取最有代表性的关键词。
    /// 对应 Python 源码中的 tfidf_processor.py。
    /// 难度系数：7/10 (因涉及复杂的矩阵运算和文本特征工程的C#实现)
    /// </summary>
    public class TfidfProcessor
    {
        // 对应 Python 源码中的 TFIDF_TOP_N 参数
        private const int TopNKeywords = 5; 

        /// <summary>
        /// [核心功能] 从标签数据中计算 TF-IDF 矩阵，并提取每个文档的 Top N 关键词。
        /// 对应 Python 中的 process_tfidf_features 函数。
        /// </summary>
        /// <param name="imageData">包含所有图片信息的列表。</param>
        /// <returns>一个字典，键为文件路径，值为 Top N 关键词列表。</returns>
        public Dictionary<string, List<string>> ExtractTfidfTags(List<ImageInfo> imageData)
        {
            Console.WriteLine("\n>>> 开始 TF-IDF 关键词提取处理...");
            
            // 1. 预处理：获取有效的标签文档 (对应 Python preprocess_tags)
            // 确保只处理有 CleanedTags 的 ImageInfo 对象
            var validDocuments = imageData
                .Where(info => !string.IsNullOrWhiteSpace(info.CleanedTags))
                .ToList();

            if (!validDocuments.Any())
            {
                Console.WriteLine("[WARN] 没有有效的标签数据用于 TF-IDF 计算。");
                return new Dictionary<string, List<string>>();
            }
            
            // 文档（标签字符串）列表
            var corpus = validDocuments.Select(info => info.CleanedTags).ToList();
            
            // 2. 模拟 TF-IDF 向量化 (C# 实现的难点和核心)
            // ⚠️ 实际项目需依赖 ML.NET 或手动实现，这里使用占位方法模拟结果
            var (tfidfMatrix, featureNames) = SimulateTfidfVectorization(corpus);
            
            if (tfidfMatrix == null || !featureNames.Any())
            {
                Console.WriteLine("[ERROR] TF-IDF 向量化失败。");
                return new Dictionary<string, List<string>>();
            }

            // 3. 并行处理：提取每个文档的 Top N 关键词 (对应 Python 中的并行逻辑)
            // 使用 ConcurrentDictionary 存储结果
            var resultsMap = new ConcurrentDictionary<string, List<string>>();
            int totalDocuments = validDocuments.Count;
            
            Console.WriteLine($"TF-IDF 矩阵计算完毕。开始并行提取 Top {TopNKeywords} 关键词...");

            // 使用 Parallel.For 模拟多进程/多线程加速
            Parallel.For(0, totalDocuments, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, i =>
            {
                var info = validDocuments[i];
                var rowVector = tfidfMatrix[i]; // 获取当前文档的 TF-IDF 分值向量
                
                // 提取 Top N 关键词的逻辑 (对应 Python 源码中对矩阵行的处理)
                var topTags = ExtractTopNTags(rowVector, featureNames);
                
                resultsMap.TryAdd(info.FilePath, topTags);

                // 实时预览/计数器
                // Console.Write($"\rTF-IDF 进度: {resultsMap.Count}/{totalDocuments}");
            });

            Console.WriteLine("\nTF-IDF 关键词提取完成。");
            
            // 4. 收集最终结果 (转换为 Path -> TagList 字典)
            return resultsMap.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>
        /// 占位函数：模拟 C# 环境下 TF-IDF 向量化的结果。
        /// 实际应使用 ML.NET/Numpy-like 库进行计算。
        /// </summary>
        /// <returns>Tuple：(TF-IDF 矩阵 [文档数 x 特征数], 特征词列表)</returns>
        private static (double[][]? Matrix, List<string> FeatureNames) SimulateTfidfVectorization(List<string> corpus)
        {
            // ⚠️ 此处为简化和占位，实际需要完整的 TF-IDF 算法实现。
            // 假定词汇表：
            var uniqueWords = corpus
                .SelectMany(doc => doc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()))
                .Distinct()
                .OrderBy(w => w)
                .ToList();
            
            if (!uniqueWords.Any())
            {
                return (null, new List<string>());
            }

            int docCount = corpus.Count;
            int featureCount = uniqueWords.Count;
            
            // 创建一个模拟的 TF-IDF 矩阵
            double[][] matrix = new double[docCount][];
            Random rand = new Random();

            for (int i = 0; i < docCount; i++)
            {
                matrix[i] = new double[featureCount];
                // 随机填充非零值，模拟稀疏矩阵
                for (int j = 0; j < featureCount; j++)
                {
                    if (rand.Next(0, 10) < 2) // 20% 概率为非零
                    {
                        matrix[i][j] = rand.NextDouble();
                    }
                }
            }
            // Loguru 替代
            Console.WriteLine($"[INFO] 模拟 TF-IDF 向量化完成。文档数: {docCount}, 特征数: {featureCount}");
            return (matrix, uniqueWords);
        }

        /// <summary>
        /// 从 TF-IDF 向量中提取分值最高的 Top N 关键词。
        /// </summary>
        /// <param name="vector">单个文档的 TF-IDF 分值向量。</param>
        /// <param name="featureNames">特征词列表，索引与向量对应。</param>
        /// <returns>Top N 关键词列表。</returns>
        private static List<string> ExtractTopNTags(double[] vector, List<string> featureNames)
        {
            // 将分值和关键词打包成匿名对象，按分值降序排序
            var rankedFeatures = vector
                .Select((score, index) => new { Score = score, Tag = featureNames[index] })
                .Where(f => f.Score > 0) // 只保留有分值的词
                .OrderByDescending(f => f.Score)
                .Take(TopNKeywords) // 提取 Top N
                .Select(f => f.Tag)
                .ToList();

            return rankedFeatures;
        }

        /// <summary>
        /// 将 TF-IDF 关键词列表格式化为文件名后缀字符串。
        /// 对应 Python 源码中的 format_tfidf_tags_for_filename 函数。
        /// </summary>
        public static string FormatTfidfTagsForFilename(List<string> tagList)
        {
            if (tagList == null || !tagList.Any())
            {
                return string.Empty;
            }

            // 格式化为 ___tag1___tag2 格式
            return AnalyzerConfig.TagDelimiter + string.Join(AnalyzerConfig.TagDelimiter, tagList);
        }
    }
}