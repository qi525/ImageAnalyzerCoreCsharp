using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            WriteLine("\n[INFO] >>> 1. 仅扫描生成表格（只读）【未！！！完成】 <<<");
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
            WriteLine("\n[INFO] >>> 2. 仅归档到历史文件夹（移动）【已实现】【单文件】 <<<");
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
        /// 功能8: 获取风格词前缀（只读）【已完成】【目前核心功能】
        /// 直接去掉"1girl"及其后面的所有词，出表格
        /// 然后使用USELESS_STYLE_WORD_SUFFIXES.txt进行二次清洗
        /// </summary>
        public static async void Feature8_ExtractStyleWords()
        {
            var imageData = ScanImages();
            if (!imageData.Any()) return;

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var styleWords = new Dictionary<string, int>();

            // 逐行处理：去掉"1girl"及其后面的所有词
            foreach (var info in imageData)
            {
                if (string.IsNullOrWhiteSpace(info.CleanedTags)) continue;

                int idx = info.CleanedTags.ToLower().IndexOf("1girl");
                if (idx > 0)
                {
                    string word = info.CleanedTags.Substring(0, idx).ToLower();
                    if (!string.IsNullOrWhiteSpace(word) && word.Length >= 30)
                    {
                        if (styleWords.ContainsKey(word))
                            styleWords[word]++;
                        else
                            styleWords[word] = 1;
                    }
                }
            }

            // 筛选出现10次以上的
            styleWords = styleWords.Where(kv => kv.Value >= 10).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (styleWords.Count == 0) return;

            // 导出步骤1
            string path1 = Path.Combine(ExcelDirectory, $"风格词前缀_1.原始_{timestamp}.xlsx");
            if (ImageScanner.ExportStyleWordsToExcel(styleWords, path1))
                OpenFile(path1);

            // 步骤2: 二次清洗 - 使用USELESS_STYLE_WORD_SUFFIXES.txt
            var suffixes = LoadUselessSuffixes();
            var cleaned = new Dictionary<string, int>();

            foreach (var kv in styleWords)
            {
                string word = kv.Key;
                
                // 逐个移除无用词汇后缀
                foreach (var suffix in suffixes)
                {
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        word = Regex.Replace(word, Regex.Escape(suffix.Trim()), "", RegexOptions.IgnoreCase);
                    }
                }

                word = word.Trim();
                if (!string.IsNullOrWhiteSpace(word))
                {
                    if (cleaned.ContainsKey(word))
                        cleaned[word] += kv.Value;
                    else
                        cleaned[word] = kv.Value;
                }
            }

            if (cleaned.Count == 0) return;

            // 重新排序
            cleaned = cleaned.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);

            // 导出步骤2
            string path2 = Path.Combine(ExcelDirectory, $"风格词前缀_2.清洗后_{timestamp}.xlsx");
            if (ImageScanner.ExportStyleWordsToExcel(cleaned, path2))
                OpenFile(path2);
        }

        /// <summary>
        /// 加载无用词汇清单（跳过注释和空行）
        /// </summary>
        private static List<string> LoadUselessSuffixes()
        {
            var suffixes = new List<string>();
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                var resourcePath = Path.Combine(baseDir, "Resources", "USELESS_STYLE_WORD_SUFFIXES.txt");

                if (File.Exists(resourcePath))
                {
                    var lines = File.ReadAllLines(resourcePath);
                    suffixes = lines
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                WriteLine($"[ERROR] 加载无用词汇清单失败: {ex.Message}");
            }

            return suffixes;
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
