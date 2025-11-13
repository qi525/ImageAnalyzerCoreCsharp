// 文件名：Program.cs

using System;
using System.IO;
using System.Linq;
using ImageAnalyzerCore;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

// 引入 Logger 机制 (Loguru 的 C# 替代方案，这里使用简单的 Console.WriteLine 占位)
using static System.Console; // 简化 Console.WriteLine 为 WriteLine

namespace ImageAnalyzerCore
{
    class Program
    {
        // --- 1. 定义您的文件路径和配置 ---
        // AnalyzerConfig 是静态类，无需实例化，直接通过类名访问成员。
        private static readonly string FolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
        private static readonly string ExcelPath = @"C:\个人数据\C#Code\ImageAnalyzerCore\图片信息报告.xlsx";

        public static async Task Main(string[] args)
        {
            WriteLine("[INFO] 欢迎使用图片分析报告生成工具");
            WriteLine("-----------------------------------");
            
            while (true)
            {
                DisplayMenu();
                Write("请输入您的选择 (1-4): ");
                // Bug Fix: 使用 ?? string.Empty 确保 choice 永远是不可为 null 的 string
                string choice = ReadLine()?.Trim() ?? string.Empty; 

                try
                {
                    switch (choice)
                    {
                        case "1":
                            // 选项 1: 运行完整分析流程 (扫描 -> TF-IDF -> 报告 -> 评分)
                            await RunFullAnalysisFlowAsync();
                            break;
                        case "2":
                            // 选项 2: (重构中) 跳过扫描和 TF-IDF，只生成报告和评分。
                            WriteLine("[INFO] ⚠️ 选择 [2. 只生成 Excel 报告]，目前将执行完整流程 (扫描 -> TF-IDF -> 报告 -> 评分)。");
                            WriteLine("[INFO] 未来重构时，此选项将只执行报告和评分步骤。");
                            await RunFullAnalysisFlowAsync(); 
                            break;
                        case "3":
                            // 选项 3: (用户新定义) 仅运行扫描和报告生成 (Scan -> Report -> Open)
                            await RunScanAndReportFlowAsync();
                            break;
                        case "4":
                            WriteLine("[INFO] 退出程序。");
                            return;
                        default:
                            WriteLine("[WARNING] 无效的选项，请重新输入 1-4 之间的数字。");
                            break;
                    }
                    WriteLine("\n-----------------------------------\n");
                }
                catch (Exception ex)
                {
                    WriteLine($"[FATAL] 发生严重错误: {ex.Message}");
                    // 确保不会因为一个错误退出整个程序循环
                }
            }
        }

        /// <summary>
        /// 显示用户菜单选项。
        /// </summary>
        private static void DisplayMenu()
        {
            WriteLine("请选择您要执行的操作：");
            WriteLine("  1. 完整流程 (扫描 -> 关键词提取 -> 报告生成 -> 评分预测)");
            WriteLine("  2. 只生成 Excel 报告 (跳过扫描和关键词提取，但需要预先的数据缓存)");
            WriteLine("  3. 仅扫描和报告生成 (只运行扫描，不进行关键词提取和评分，完成后自动打开报告)");
            WriteLine("  4. 退出程序");
        }

        /// <summary>
        /// 完整分析流程：扫描 -> TF-IDF -> 报告 -> 评分 (选项 1)
        /// </summary>
        private static async Task RunFullAnalysisFlowAsync()
        {
            // 1. 运行扫描和报告生成 (提取公共部分)
            List<ImageInfo> imageData = await RunScanAndReportGenerationAsync();

            if (!imageData.Any())
            {
                // 如果是空列表，则流程已中止
                return;
            }

            // --- 2. TF-IDF/关键词提取阶段 (对应 Python 的 tfidf_processor.py) ---
            WriteLine("\n[INFO] >>> 2. 开始 TF-IDF 关键词提取 <<<");
            var tfidfProcessor = new TfidfProcessor();
            
            // 提取 TF-IDF 关键词
            Dictionary<string, List<string>> tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);

            // 更新 ImageInfo 对象中的 TF-IDF 结果
            foreach (var info in imageData)
            {
                if (tfidfTagsMap.TryGetValue(info.FilePath, out var tags))
                {
                    info.CoreKeywords = TfidfProcessor.FormatTfidfTagsForFilename(tags, "___");
                }
            }

            // --- 3. 评分/预测阶段 (对应 Python 的 image_scorer_supervised.py) ---
            WriteLine("\n[INFO] >>> 3. 开始评分预测与结果写入 <<<");
            // 运行评分并打开报告
            ExecutePostReportActions(ExcelPath, runScoring: true); 
        }
        
        /// <summary>
        /// 仅运行扫描和报告生成流程 (选项 3)
        /// </summary>
        private static async Task RunScanAndReportFlowAsync()
        {
            // 1. 运行扫描和报告生成
            List<ImageInfo> imageData = await RunScanAndReportGenerationAsync();

            if (!imageData.Any())
            {
                // 如果是空列表，则流程已中止
                return;
            }

            // 2. 自动打开报告 (不运行评分)
            ExecutePostReportActions(ExcelPath, runScoring: false);
        }
        
        /// <summary>
        /// 扫描图片并生成报告的公共部分。
        /// </summary>
        /// <returns>扫描到的图片信息列表，如果失败则返回一个空列表。</returns>
        private static async Task<List<ImageInfo>> RunScanAndReportGenerationAsync()
        {
            // --- 1. 扫描阶段 (对应 Python 的 image_scanner.py) ---
            WriteLine("[INFO] >>> 1. 开始扫描图片并提取元数据 <<<");
            var scanner = new ImageScanner();
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FolderToScan);

            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，流程中止。");
                // 修复：返回空列表而不是 null
                return new List<ImageInfo>(); 
            }

            // --- 2. 报告生成阶段 (对应 Python 的 create_excel_report) ---
            WriteLine("\n[INFO] >>> 2. 开始生成 Excel 报告 <<<");
            bool reportSuccess = SimulateCreateExcelReport(imageData, ExcelPath);

            if (!reportSuccess)
            {
                WriteLine("[ERROR] Excel 报告生成失败，流程中止。");
                // 修复：返回空列表而不是 null
                return new List<ImageInfo>(); 
            }
            
            return imageData;
        }

        // ⚠️ 占位函数：模拟 Excel 报告创建
        private static bool SimulateCreateExcelReport(List<ImageInfo> imageData, string path)
        {
            // 实际 C# 代码中需要使用 ClosedXML/EPPlus 等库来创建并填充 Excel 文件
            WriteLine($"[ALERT] ！！！ Excel 报告问题警告 ！！！");
            WriteLine($"[ALERT] 当前函数只是【模拟】创建报告文件: {path} (包含 {imageData.Count} 条数据)。");
            WriteLine($"[ALERT] 实际的 Excel 内容填充功能【尚未实现】。你需要引入 ClosedXML 或 EPPlus 等库，并编写逻辑来填充 {nameof(ImageInfo)} 列表中的数据。");
            
            // 确保文件路径存在，以便后续可以“打开”它
            try
            {
                // 创建一个空文件作为占位
                File.WriteAllText(path, "Simulated Excel Content Placeholder");
                return true;
            }
            catch (Exception ex)
            {
                WriteLine($"[ERROR] 模拟文件创建失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 执行报告生成后的通用操作：可选的评分/预测和报告打开。
        /// </summary>
        private static void ExecutePostReportActions(string excelPath, bool runScoring)
        {
            if (runScoring)
            {
                WriteLine("[INFO] 开始执行评分/预测...");
                var scorer = new ImageScorer();
                // 在实际项目中，这里需要将 Excel 文件路径传入，让评分器读取并写入结果
                scorer.CalculateAndWriteScores(excelPath);
            }
            
            // --- 最终报告打开逻辑 ---
            WriteLine($"\n[INFO] 操作完成，报告文件路径: {excelPath}");
            try
            {
                // Windows 平台专属启动命令
                Process.Start(new ProcessStartInfo(excelPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                WriteLine($"[WARNING] 无法自动打开报告文件。请手动打开。错误: {ex.Message}");
            }
        }
    }
}