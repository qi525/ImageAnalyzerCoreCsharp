// 文件名：TfidfProcessor.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics; // 用于计时
using System.Threading; // 用于 Interlocked 计数器

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

            // --- 计数器/进度跟踪变量初始化 ---
            var stopWatch = Stopwatch.StartNew(); // 启动计时器
            int successCount = 0; // 成功任务计数
            int failedCount = 0;  // 失败任务计数
            
            Console.WriteLine($"[INFO] TF-IDF 矩阵计算完毕。开始并行提取 Top {TopNKeywords} 关键词...");
            // -----------------------------

            // 使用 Parallel.For 模拟多进程/多线程加速
            Parallel.For(0, totalDocuments, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, i =>
            {
                var info = validDocuments[i];
                
                try
                {
                    var rowVector = tfidfMatrix[i]; // 获取当前文档的 TF-IDF 分值向量
                    
                    // 提取 Top N 关键词的逻辑 (对应 Python 源码中对矩阵行的处理)
                    var topTags = ExtractTopNTags(rowVector, featureNames);
                    
                    resultsMap.TryAdd(info.FilePath, topTags);

                    // 成功任务计数 +1 (线程安全)
                    Interlocked.Increment(ref successCount);
                    
                }
                catch (Exception ex)
                {
                    // 某个任务失败，记录异常警报
                    string error_message = $"【TF-IDF 异常警报】任务失败，文件路径: {info.FilePath}。错误: {ex.Message}";
                    Console.WriteLine(error_message);
                    
                    // 失败任务计数 +1 (线程安全)
                    Interlocked.Increment(ref failedCount);
                    
                    // 失败的任务在结果集中标记为空列表
                    resultsMap.TryAdd(info.FilePath, new List<string>());
                }

                // 实时进度反馈（模仿 tqdm，但仅是简单的计数覆盖）
                // 仅在控制台运行时有效
                // @@    136-136,137-137   @@ 修正：直接读取 int 变量
                int completedCount = successCount + failedCount; 
                if (completedCount % 100 == 0 || completedCount == totalDocuments) // 每处理100个或任务结束时更新
                {
                    Console.Write($"\rTF-IDF关键词提取: {completedCount}/{totalDocuments}");
                }
            });

            // 停止计时并获取总耗时
            stopWatch.Stop();
            double finalElapsedTime = stopWatch.Elapsed.TotalSeconds;

            // 最终打印完成信息和计数器总结
            Console.WriteLine($"\rTF-IDF关键词提取: {totalDocuments}/{totalDocuments} [完成]");
            
            string finalLog = (
                $"【TF-IDF 计数器总结】总数量: {totalDocuments}，" +
                $"成功: {successCount}，失败: {failedCount}。" +
                $"总耗时: {finalElapsedTime:.2f} 秒。"
            );
            Console.WriteLine(finalLog);

            
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
            // 使用Guid作为随机种子，确保每个线程使用不同的随机序列，避免在Parallel中出现重复随机数
            Random rand = new Random(Guid.NewGuid().GetHashCode()); 

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
        public static string FormatTfidfTagsForFilename(List<string> tagList, string tagDelimiter)
        {
            if (tagList == null || !tagList.Any())
            {
                return string.Empty;
            }

            // 格式化为 ___tag1___tag2 格式 (遵循 Python 源码逻辑：分隔符 + Join)
            return tagDelimiter + string.Join(tagDelimiter, tagList);
        }
    }
}