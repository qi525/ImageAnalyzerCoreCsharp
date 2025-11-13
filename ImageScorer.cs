// 文件名：ImageScorer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent; // 修复错误 CS0246: 未能找到类型或命名空间名“ConcurrentDictionary<,>”
// 假设已引入用于Excel操作的库，例如 ClosedXML 或 EPPlus
// using ClosedXML.Excel; 
// 假设已引入 ML.NET 相关的库
// using Microsoft.ML; 
// using Microsoft.ML.Data;
// using Microsoft.ML.Trainers;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 监督式图片评分器：使用机器学习模型预测图片的推荐评分。
    /// 对应 Python 源码中的 image_scorer_supervised.py。
    /// 难度系数：8/10 (涉及 ML.NET 库的封装和 Excel 复杂操作)
    /// </summary>
    public class ImageScorer
    {
        // 模拟 ML.NET 核心对象
        // private readonly MLContext _mlContext; 
        // private ITransformer _model;

        public ImageScorer()
        {
            // _mlContext = new MLContext();
            Console.WriteLine("[INFO] 评分器初始化：已加载配置。");
            // 实际项目中，这里会加载或训练模型
            // LoadOrTrainModel();
        }

        // 占位函数：模拟模型训练和加载
        private void LoadOrTrainModel()
        {
            // ⚠️ 实际代码中，需要读取带评分的训练数据，进行 TF-IDF 转换，
            // 然后训练 Ridge Regression 模型 (_mlContext.Regression.Trainers.LbfgsPoissonRegression)
            Console.WriteLine("[INFO] 评分模型已加载 (占位模拟)。");
        }

        /// <summary>
        /// 提取文件夹名称中的人工评分（如 @@@评分98）
        /// 对应 Python 源码中的 extract_score_from_folder_name 函数。
        /// </summary>
        private static double ExtractCustomScore(string? folderName) // 修复形参可能传入 null 的错误
        {
            if (string.IsNullOrWhiteSpace(folderName)) return 0.0;

            // 匹配格式: @@@评分[数字]
            string pattern = $@"{Regex.Escape(AnalyzerConfig.CustomScorePrefix)}(\d+)";
            var match = Regex.Match(folderName, pattern);

            if (match.Success && double.TryParse(match.Groups[1].Value, out double score))
            {
                return score;
            }
            return 0.0;
        }

        /// <summary>
        /// 根据文件信息和 TF-IDF 结果计算和写入预测评分到 Excel 报告。
        /// 对应 Python 源码中的 calculate_and_write_scores 函数。
        /// </summary>
        /// <param name="excelPath">Excel 报告的完整路径。</param>
        public void CalculateAndWriteScores(string excelPath)
        {
            Console.WriteLine($"\n>>> 开始计算和写入推荐评分到报告: {Path.GetFileName(excelPath)}");

            if (!File.Exists(excelPath))
            {
                Console.WriteLine($"[ERROR] 文件未找到: {excelPath}");
                return;
            }

            // 1. 模拟数据加载和预处理 (对应 Python pd.read_excel)
            // ⚠️ 实际应使用 C# Excel 库读取数据
            var dataTable = SimulateReadExcelData(excelPath); 
            if (dataTable == null || !dataTable.Any())
            {
                Console.WriteLine("[WARN] Excel 数据为空或读取失败，跳过评分计算。");
                return;
            }

            int totalCount = dataTable.Count;
            int successCount = 0;
            
            // 2. 评分计算与预测 (并行)
            var scoredResults = new ConcurrentDictionary<string, double>();
            
            // 模拟 TF-IDF 特征转换 (实际需要 TfidfProcessor 介入)
            // 模拟从数据表中提取核心词汇
            var coreKeywords = dataTable.ToDictionary(
                d => d["FilePath"], 
                d => d.ContainsKey(AnalyzerConfig.CoreKeywordColumnName) ? d[AnalyzerConfig.CoreKeywordColumnName] : string.Empty);

            // ⚠️ 简化：这里跳过了复杂的 TF-IDF 特征转换和模型预测过程，直接模拟预测结果
            Parallel.ForEach(dataTable, new ParallelOptions { MaxDegreeOfParallelism = AnalyzerConfig.MaxConcurrentWorkers }, row =>
            {
                double predictedScore = 0.0;
                string filePath = row["FilePath"];

                // A. 提取人工/文件夹评分 (人工评分优先)
                string? folderName = Path.GetFileName(Path.GetDirectoryName(filePath)); // 修复局部变量可能为 null 的警告
                double customScore = ExtractCustomScore(folderName);
                
                if (customScore > 0)
                {
                    // 如果存在自定义评分标记，则使用该分数
                    predictedScore = customScore;
                }
                else
                {
                    // B. 模拟模型预测 (使用默认中性分 + 随机波动作为占位符)
                    // 实际需要：将 coreKeywords 转换成 TF-IDF 向量，然后调用 _model.Predict()
                    Random rand = new Random(filePath.GetHashCode()); // 确保同一文件得分一致
                    predictedScore = AnalyzerConfig.DefaultNeutralScore + (rand.NextDouble() * 20 - 10); // 40.0 到 60.0 之间
                    
                    // C. 应用 RATING_MAP 中的文件夹基准分（如果文件不在自定义评分文件夹）
                    foreach (var kvp in AnalyzerConfig.RatingMap)
                    {
                        if (folderName != null && folderName.Contains(kvp.Key)) // 修复可能出现的空引用解引用                        
                        {
                            // 如果文件名包含基准关键词，则提高分数作为模型学习的基础
                            predictedScore = Math.Max(predictedScore, kvp.Value * 0.95); // 稍微低于基准分
                            break;
                        }
                    }
                }

                scoredResults.TryAdd(filePath, predictedScore);
                Interlocked.Increment(ref successCount);
            });
            
            // 3. 模拟写入 Excel 报告
            SimulateWriteScoresToExcel(excelPath, scoredResults); 

            // 4. 打印最终统计结果
            int failureCount = totalCount - successCount;
            Console.WriteLine("\n--- 评分计算与写入完成 ---");
            Console.WriteLine($"总任务量: {totalCount}");
            Console.WriteLine($"成功处理量: {successCount}");
            Console.WriteLine($"失败处理量: {failureCount}");
            if (failureCount > 0)
            {
                Console.WriteLine("[ALERT] 异常警报：评分计算失败，请检查内存、磁盘空间或数据格式。");
            }
        }

        // ⚠️ 占位函数：模拟读取 Excel 数据
        private static List<Dictionary<string, string>> SimulateReadExcelData(string path)
        {
            // 实际中需要从 Excel 读取至少包含 "FilePath" 和 "提取正向词的核心词" 的列
            // 模拟 10 行数据
            return Enumerable.Range(1, 10).Select(i => new Dictionary<string, string>
            {
                { "FilePath", Path.Combine("C:\\test\\", $"file_{i}.png") },
                { AnalyzerConfig.CoreKeywordColumnName, $"tag_A, tag_B, tag_{i}" }
            }).ToList();
        }

        // ⚠️ 占位函数：模拟写入 Excel 数据
        private static void SimulateWriteScoresToExcel(string path, ConcurrentDictionary<string, double> scores)
        {
            // 实际中需要使用 C# Excel 库打开文件，定位到正确的列，并写入 scores
            Console.WriteLine($"[INFO] 评分数据已模拟写入 '{AnalyzerConfig.PredictedScoreColumnName}' 列。");
        }
    }
}