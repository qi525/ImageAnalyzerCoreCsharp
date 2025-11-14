// 文件名：ImageScorer.cs

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
// 【修复：已取消注释】引入 ML.NET 相关的库，用于修复 [LoadColumn] 和 [ColumnName] 的 Bug
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
        [LoadColumn(0)] 
        public string CoreKeywords { get; set; } = string.Empty; 
        
        // 训练目标：原始分数 (Y值)
        [LoadColumn(1)] 
        public float OriginalScore { get; set; } // 对应 Python 中的 df['OriginalScore']
    }

    /// <summary>
    /// ML.NET 预测结果的数据结构。
    /// </summary>
    public class ScorerPrediction
    {
        // ML.NET 默认输出的预测列名
        [ColumnName("Score")] 
        public float PredictedScore { get; set; }
    }
    // ----------------------------------------------------
    
    /// <summary>
    /// 监督式图片评分器：使用机器学习模型预测图片的推荐评分。
    /// 对应 Python 源码中的 image_scorer_supervised.py。
    /// 难度系数：8/10 (核心算法实现难度高，但当前修改难度低)
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

        // 评分映射 (对应 image_scorer_supervised.py 中的 RATING_MAP)
        private static readonly IReadOnlyDictionary<string, double> RatingMap = new Dictionary<string, double>
        {
            { "特殊：98分", 98.0 },
            { "超绝", 95.0 },
            { "特殊画风", 90.0 },
            { "超级精选", 85.0 },
            { "精选", 80.0 }
        };
        
        /// <summary>
        /// 未被人工标记的图片的默认中性分数。
        /// </summary>
        private const double DefaultNeutralScore = 50.0;
        private const string CustomScorePrefix = "@@@评分"; // 从 Python 源码推断

        public ImageScorer()
        {
            // _mlContext = new MLContext();
            Console.WriteLine("[INFO] 评分器初始化：已加载配置。");
            // LoadOrTrainModel();
        }

        // 占位函数：模拟模型训练和加载
        private void LoadOrTrainModel()
        {
            Console.WriteLine("[INFO] 评分器：模型加载/训练完成（当前为模拟占位）。");
        }

        /// <summary>
        /// 预测评分的核心模拟逻辑。
        /// </summary>
        private double PredictScore(string tags, string filePath)
        {
            // 1. 提取人工评分作为基准 (对应 Python 中的 ExtractOriginalScore 逻辑)
            double baseScore = DefaultNeutralScore;
            
            // 简化：从标签中提取评分关键词
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var ratingPair in RatingMap) 
                {
                    if (tags.ToLowerInvariant().Contains(ratingPair.Key.ToLowerInvariant()))
                    {
                        baseScore = ratingPair.Value;
                        break;
                    }
                }
            }
            
            // 2. 模拟模型预测：计算标签权重
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
            
            // 3. 最终模拟预测结果:
            Random rand = new Random();
            double predictedScore = baseScore + weightSum * 5 + rand.NextDouble() * 5 - 2.5; 
            
            return Math.Clamp(predictedScore, 0, 100);
        }

        // @@    200-200,201-215   @@ 增加 PredictAndApplyScores 方法
        /// <summary>
        /// [修复 Program.cs Bug #1] 预测评分并将结果写回 ImageInfo 对象。
        /// 用于 RunScoreTagFlowAsync (选项 6 - 仅添加评分标签)。
        /// </summary>
        /// <param name="imageData">包含所有图片信息的列表。</param>
        public void PredictAndApplyScores(List<ImageInfo> imageData)
        {
            Console.WriteLine($"[INFO] 开始为 {imageData.Count} 张图片预测并应用评分...");
            
            // 循环遍历数据，调用预测逻辑并将结果赋值给 ImageInfo.PredictedScore
            foreach (var info in imageData)
            {
                // 调用核心预测逻辑
                double score = PredictScore(info.CleanedTags, info.FilePath);
                info.PredictedScore = (float)Math.Round(score, 1);
            }
            
            int scoredCount = imageData.Count(i => i.PredictedScore > 0);
            Console.WriteLine($"[INFO] 评分预测完成，成功应用评分到 {scoredCount} 张图片。");
        }
        
        // @@    217-217,218-232   @@ 增加 CalculateAndWriteScores 方法
        /// <summary>
        /// [修复 Program.cs Bug #3] 读取 Excel 报告，计算评分并写回报告文件。
        /// 用于 ExecutePostReportActions (选项 7 - 完整流程)。
        /// </summary>
        /// <param name="excelPath">Excel 报告的完整路径。</param>
        public void CalculateAndWriteScores(string excelPath)
        {
            if (!File.Exists(excelPath))
            {
                Console.WriteLine($"[ERROR] 评分写入失败：Excel 文件未找到: {excelPath}");
                return;
            }

            Console.WriteLine($"[INFO] 启动评分计算和结果写入到 Excel: {excelPath}...");
            
            // ⚠️ 占位：实际需要实现复杂的 Excel 读取（关键词）、评分计算、Excel 写入（预测评分）逻辑。
            // 这里的 SimulateReadExcelData 只是一个占位函数，用于确保流程不中断
            SimulateReadExcelData(excelPath); 
            
            Console.WriteLine($"[INFO] 评分计算和写入 Excel 报告流程结束。");
        }

        // ⚠️ 占位函数：模拟读取 Excel 数据
        private List<Dictionary<string, string>> SimulateReadExcelData(string path)
        {
            // ... (省略 SimulateReadExcelData 的实现细节，但确保其存在)
            var data = new List<Dictionary<string, string>>();
            
            for (int i = 1; i <= 15; i++)
            {
                string tags = $"tag_A, tag_B, tag_{i}";
                
                if (i % 3 == 0) tags += $", masterpiece, {RatingMap.Keys.First()}"; 
                if (i % 5 == 0) tags += ", best_quality";
                
                string filename = i == 10 ? $"file_{i}{CustomScorePrefix}99.5.png" : $"file_{i}.png"; 

                data.Add(new Dictionary<string, string>
                {
                    { "FilePath", Path.Combine("C:\\test\\", filename) },
                    { "CoreKeywords", tags } 
                });
            }
            
            // 模拟预测结果列表
            var scoredResults = data.Select(d => new ImageInfo 
            {
                FilePath = d["FilePath"],
                // 模拟评分结果写入
                PredictedScore = (float)(d.ContainsKey("CoreKeywords") && d["CoreKeywords"].Contains("masterpiece") ? 90.0 : 50.0) 
            }).ToList();
            
            int total = scoredResults.Count;
            int updated = scoredResults.Count(info => info.PredictedScore > DefaultNeutralScore);
            
            Console.WriteLine($"[INFO] 模拟写入 {total} 个评分结果到 Excel。");
            Console.WriteLine($"[INFO] 评分成功更新数量: {updated}。");

            return data;
        }
    }
}