// 文件名：ImageScorer.cs
// 难度系数：1/10 (仅修复了 using 语句和结构定义)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent; 
using System.Threading; // 引入 Interlocked
using System.Diagnostics; // 引入 Stopwatch
// 假设已引入用于Excel操作的库，例如 ClosedXML 或 EPPlus
// using ClosedXML.Excel; 
// 【修复：取消注释】引入 ML.NET 相关的库
using Microsoft.ML; 
using Microsoft.ML.Data; 
// using Microsoft.ML.Trainers;

namespace ImageAnalyzerCore
{
    // --- 辅助数据结构 (对应 Python 训练数据和预测结果) ---
    
    /// <summary>
    /// 用于 ML.NET 训练/预测的输入数据结构。
    /// 对应 Excel 的每一行数据。
    /// </summary>
    public class ScorerInput
    {
        // 核心特征：用于 TF-IDF 向量化
        [LoadColumn(0)] // 【修复：已引入 Microsoft.ML.Data】
        public string CoreKeywords { get; set; } = string.Empty; 
        
        // 训练目标：原始分数 (Y值)
        [LoadColumn(1)] // 【修复：已引入 Microsoft.ML.Data】
        public float OriginalScore { get; set; } // 对应 Python 中的 df['OriginalScore']
    }

    /// <summary>
    /// ML.NET 预测结果的数据结构。
    /// </summary>
    public class ScorerPrediction
    {
        // ML.NET 默认输出的预测列名
        [ColumnName("Score")] // 【修复：已引入 Microsoft.ML.Data】
        public float PredictedScore { get; set; }
    }
    // ----------------------------------------------------
    
    /// <summary>
    /// 监督式图片评分器：使用机器学习模型预测图片的推荐评分。
    /// 对应 Python 源码中的 image_scorer_supervised.py。
    /// 难度系数：8/10 (涉及 ML.NET 库的封装和 Excel 复杂操作)
    /// </summary>
    public class ImageScorer
    {
        // 模拟 ML.NET 核心对象 (占位)
        // private readonly MLContext _mlContext; 
        // private ITransformer _model;

        // 用于模拟模型学习到的特征权重（简化）
        private static readonly Dictionary<string, double> SimulatedKeywordWeights = new Dictionary<string, double>
        {
            { "masterpiece", 1.5 },
            { "best_quality", 1.2 },
            { "absurdres", 1.1 }
        };

        // 【新增】评分映射 (对应 image_scorer_supervised.py 中的 RATING_MAP)
        private static readonly IReadOnlyDictionary<string, double> RatingMap = new Dictionary<string, double>
        {
            { "特殊：98分", 98.0 },
            { "超绝", 95.0 },
            { "特殊画风", 90.0 },
            { "超级精选", 85.0 },
            { "精选", 80.0 }
        };
        
        /// <summary>
        /// 【新增】未被人工标记的图片的默认中性分数。
        /// </summary>
        private const double DefaultNeutralScore = 50.0;

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
            // 然后使用 Ridge 或其他回归模型训练模型。
            Console.WriteLine("[INFO] 评分器：模型加载/训练完成（当前为模拟占位）。");
        }

        /// <summary>
        /// 预测评分的核心模拟逻辑。
        /// </summary>
        /// <returns>预测的评分 (0-100)。</returns>
        private double PredictScore(string tags, string filePath)
        {
            // ... (代码不变) ...

            // 2. 模拟模型预测：计算标签权重
            double predictedScore = DefaultNeutralScore;
            double weightSum = 0;
            
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var weightPair in SimulatedKeywordWeights)
                {
                    if (tags.ToLowerInvariant().Contains(weightPair.Key))
                    {
                        weightSum += weightPair.Value;
                    }
                }
            }
            
            // 3. 模拟基准分调整 (对应 Python 中基于文件夹/配置的基准分)
            double baseScore = DefaultNeutralScore;
            
            // 检查 CoreKeywords 是否与 RatingMap 中的关键词匹配
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var ratingPair in RatingMap) // <--- 引用已更新
                {
                    // 如果关键词中包含任何配置的关键词，则使用最高评分作为基准
                    if (tags.ToLowerInvariant().Contains(ratingPair.Key.ToLowerInvariant()))
                    {
                        baseScore = ratingPair.Value;
                        break; // 使用第一个匹配到的最高基准分
                    }
                }
            }
            
            // 4. 最终模拟预测结果: (基准分 + 权重调整 + 随机噪声)
            // 假设模型主要影响是微调基准分
            Random rand = new Random();
            predictedScore = baseScore + weightSum * 5 + rand.NextDouble() * 5 - 2.5; // 随机波动 +/- 2.5
            
            return Math.Clamp(predictedScore, 0, 100);
        }

        // ⚠️ 占位函数：模拟读取 Excel 数据
        private List<Dictionary<string, string>> SimulateReadExcelData(string path)
        {
            // 实际中需要从 Excel 读取至少包含 "FilePath" 和 AnalyzerConfig.CoreKeywordColumnName 的列
            // 这里为了演示，我们假设有 15 条数据，其中一些包含配置中的高分关键词
            var data = new List<Dictionary<string, string>>();
            
            // 假设 AnalyzerConfig 是可访问的
            // 假设 AnalyzerConfig.CustomScorePrefix 和 AnalyzerConfig.CoreKeywordColumnName 存在

            for (int i = 1; i <= 15; i++)
            {
                string tags = $"tag_A, tag_B, tag_{i}";
                
                if (i % 3 == 0) tags += $", masterpiece, {RatingMap.Keys.First()}"; // 添加高分关键词
                if (i % 5 == 0) tags += ", best_quality";
                
                // 模拟文件名中自带评分
                // ⚠️ 依赖于 AnalyzerConfig.CustomScorePrefix
                // string filename = i == 10 ? $"file_{i}{AnalyzerConfig.CustomScorePrefix}99.5.png" : $"file_{i}.png";
                string filename = i == 10 ? $"file_{i}@@@评分99.5.png" : $"file_{i}.png"; // 使用硬编码值模拟

                
                data.Add(new Dictionary<string, string>
                {
                    { "FilePath", Path.Combine("C:\\test\\", filename) },
                    // ⚠️ 依赖于 AnalyzerConfig.CoreKeywordColumnName
                    { "CoreKeywords", tags } // 使用硬编码值模拟
                });
            }
            
            // 模拟预测结果列表
            var scoredResults = data.Select(d => new ImageInfo 
            {
                FilePath = d["FilePath"],
                PredictedScore = (float)(d.ContainsKey("CoreKeywords") && d["CoreKeywords"].Contains("masterpiece") ? 90.0 : 50.0) 
            }).ToList();
            
            int total = scoredResults.Count;
            // 检查有多少个评分是大于默认中性分的，以模拟实际更新
            int updated = scoredResults.Count(info => info.PredictedScore > DefaultNeutralScore);
            
            Console.WriteLine($"[INFO] 模拟写入 {total} 个评分结果到 Excel。");
            Console.WriteLine($"[INFO] 评分成功更新数量: {updated}。");

            // 假设 ExcelReportGenerator 存在一个静态方法来完成这个更新操作
            // ExcelReportGenerator.UpdateScores(path, scoredResults);
            
            return data;
        }
    }
}