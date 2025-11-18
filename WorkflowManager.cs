using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static System.Console;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 工作流管理器：集中管理所有功能流程（1-9）的实现。
    /// Program.cs 只负责菜单和分发，真正的逻辑都在这里。
    /// </summary>
    public static class WorkflowManager
    {
        private const string ExcelDirectory = @"C:\个人数据\C#Code\ImageAnalyzerCore";
        private const string FallbackFolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";

        /// <summary>
        /// 功能1: 仅扫描生成表格（只读）
        /// </summary>
        public static async void Feature1_ScanAndReport()
        {
            WriteLine("\n[INFO] >>> 1. 仅扫描生成表格（只读） <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            string excelPath = GenerateExcelPath();
            ExcelReportGenerator.CreateExcelReport(imageData, excelPath);
            OpenFile(excelPath);
        }

        /// <summary>
        /// 功能2: 仅归档到历史文件夹（移动）
        /// </summary>
        public static async void Feature2_Archive()
        {
            WriteLine("\n[INFO] >>> 2. 仅归档到历史文件夹（移动） <<<");
            var archiver = new FileArchiver();
            archiver.ExecuteArchiving();
        }

        /// <summary>
        /// 功能3: 仅分类历史文件夹（移动）
        /// </summary>
        public static async void Feature3_Categorize()
        {
            WriteLine("\n[INFO] >>> 3. 仅分类历史文件夹（移动） <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            var categorizer = new FileCategorizer();
            categorizer.CategorizeAndMoveImages(imageData, FallbackFolderToScan);
        }

        /// <summary>
        /// 功能4: 仅移动历史评分到外层（移动）
        /// </summary>
        public static async void Feature4_ScoreOrganizer()
        {
            WriteLine("\n[INFO] >>> 4. 仅移动历史评分到外层（移动） <<<");
            WriteLine($"文件来源: {ScoreOrganizer.StaticSourceRootDir}");
            WriteLine($"目标目录: {ScoreOrganizer.stringStaticTargetBaseDir}");
            
            Write("评分输入 (例如 80-99 或 80): ");
            string userInput = ReadLine()?.Trim() ?? string.Empty;
            
            if (!string.IsNullOrEmpty(userInput))
            {
                var organizer = new ScoreOrganizer();
                organizer.OrganizeFiles(userInput);
            }
            else
            {
                WriteLine("[WARNING] 用户未提供评分输入，流程中止。");
            }
        }

        /// <summary>
        /// 功能5: 仅自动添加 10 个 tag（重命名）
        /// </summary>
        public static async void Feature5_AutoTag()
        {
            WriteLine("\n[INFO] >>> 5. 仅自动添加 10 个 tag（重命名） <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            var tfidfProcessor = new TfidfProcessor();
            var tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);
            FilenameTagger.TagFiles(imageData, tfidfTagsMap);
        }

        /// <summary>
        /// 功能6: 仅自动添加评分（重命名）
        /// </summary>
        public static async void Feature6_AutoScoreAndTag()
        {
            WriteLine("\n[INFO] >>> 6. 仅自动添加评分（重命名） <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            var tfidfProcessor = new TfidfProcessor();
            var tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);
            
            var scorer = new ImageScorer();
            scorer.PredictAndApplyScores(imageData);
            
            FilenameTagger.TagFiles(imageData, tfidfTagsMap, includeScore: true);
        }

        /// <summary>
        /// 功能7: 完整流程（Scan -> TF-IDF -> Report -> Scoring）
        /// </summary>
        public static async void Feature7_FullFlow()
        {
            WriteLine("\n[INFO] >>> 7. 完整流程 (Scan -> TF-IDF -> Report -> Scoring) <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            string excelPath = GenerateExcelPath();
            ExcelReportGenerator.CreateExcelReport(imageData, excelPath);

            var tfidfProcessor = new TfidfProcessor();
            var tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);
            
            var scorer = new ImageScorer();
            scorer.CalculateAndWriteScores(excelPath);
            
            OpenFile(excelPath);
        }

        /// <summary>
        /// 功能8: 提取风格词
        /// </summary>
        public static async void Feature8_ExtractStyleWords()
        {
            WriteLine("\n[INFO] >>> 8. 提取风格词 <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            var styleWords = ImageScanner.ExtractStyleWords(imageData, occurrenceThreshold: 10, minStyleWordLength: 30);
            
            if (styleWords.Count == 0)
            {
                WriteLine("[INFO] 未检测到高频风格词。");
                return;
            }

            WriteLine($"\n✓ 成功提取 {styleWords.Count} 个高频风格词");
            int index = 1;
            foreach (var kv in styleWords)
            {
                WriteLine($"{index}. 频率: {kv.Value:D2} 次 | 内容: {kv.Key}");
                index++;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string excelPath = Path.Combine(ExcelDirectory, $"风格词统计_{timestamp}.xlsx");
            
            if (ImageScanner.ExportStyleWordsToExcel(styleWords, excelPath))
            {
                WriteLine($"[SUCCESS] 风格词Excel已保存: {excelPath}");
                OpenFile(excelPath);
            }
        }

        /// <summary>
        /// 功能9: 提取纯核心词
        /// </summary>
        public static async void Feature9_ExtractCoreWords()
        {
            WriteLine("\n[INFO] >>> 9. 提取纯核心词 <<<");
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            WriteLine("\n[INFO] 提取风格词...");
            var styleWords = ImageScanner.ExtractStyleWords(imageData, occurrenceThreshold: 10, minStyleWordLength: 30);

            WriteLine("[INFO] 提取纯核心词...\n");
            int coreWordsCount = 0;

            ProgressBarHelper.RunCoreWordsProgress(ctx =>
            {
                var task = ctx.AddTask("[cyan]提取核心词[/]", maxValue: imageData.Count);
                for (int i = 0; i < imageData.Count; i++)
                {
                    var info = imageData[i];
                    if (!string.IsNullOrWhiteSpace(info.CleanedTags))
                    {
                        string coreWordsOnly = ImageScanner.ExtractCoreWordsOnly(info.CleanedTags, styleWords);
                        if (!string.IsNullOrWhiteSpace(coreWordsOnly))
                        {
                            info.CoreKeywords = coreWordsOnly;
                            Interlocked.Increment(ref coreWordsCount);
                        }
                    }
                    task.Value = i + 1;
                }
                task.StopTask();
            });

            WriteLine($"\n--- 统计 ---");
            WriteLine($"总数: {imageData.Count} | 成功: {coreWordsCount}");

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string excelPath = Path.Combine(ExcelDirectory, $"C#版图片信息报告_{timestamp}.xlsx");
            if (ExcelReportGenerator.CreateExcelReport(imageData, excelPath))
            {
                WriteLine($"[SUCCESS] 报告已保存: {excelPath}");
                OpenFile(excelPath);
            }
        }

        // ========== 辅助方法 ==========
        
        private static List<ImageInfo> ScanImages()
        {
            WriteLine("[INFO] 开始扫描图片...");
            var scanner = new ImageScanner();
            return scanner.ScanAndExtractInfo(FallbackFolderToScan);
        }

        private static string GenerateExcelPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(ExcelDirectory, $"C#版图片信息报告_{timestamp}.xlsx");
        }

        private static void OpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                WriteLine($"[WARNING] 无法打开文件: {ex.Message}");
            }
        }
    }
}
