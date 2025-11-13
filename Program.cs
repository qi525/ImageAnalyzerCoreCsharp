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
                            // 选项 2: 只生成报告 (跳过扫描和 TF-IDF，需要有缓存数据)
                            // ⚠️ 注意：此流程需依赖 ImageAnalyzerCore 内部的缓存或预处理逻辑，目前先占位。
                            // 按照最简单的实现，先沿用选项1的完整流程，但理论上需要重构以跳过前两步。
                            WriteLine("[INFO] ⚠️ 选择 [2. 只生成 Excel 报告]，目前将执行完整流程 (扫描 -> TF-IDF -> 报告 -> 评分)。");
                            WriteLine("[INFO] 未来重构时，此选项将只执行报告和评分步骤。");
                            await RunFullAnalysisFlowAsync(); 
                            break;
                        case "3":
                            // 选项 3: 仅运行评分/预测 (需要已存在的 Excel 报告)
                            RunScoringOnlyFlow();
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
            WriteLine("  3. 仅运行评分/预测 (对已生成的 Excel 报告进行评分)");
            WriteLine("  4. 退出程序");
        }

        /// <summary>
        /// 完整分析流程：扫描 -> TF-IDF -> 报告 -> 评分
        /// </summary>
        private static async Task RunFullAnalysisFlowAsync()
        {
            // --- 1. 扫描阶段 (对应 Python 的 image_scanner.py) ---
            WriteLine("[INFO] >>> 1. 开始扫描图片并提取元数据 <<<");
            var scanner = new ImageScanner();
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FolderToScan);

            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，流程中止。");
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
                    // 修复问题三：FormatTfidfTagsForFilename 现在接受两个参数
                    // ⚠️ 这里仍然需要 ImageAnalyzerCore 内部的 FormatTfidfTagsForFilename 方法支持
                    info.CoreKeywords = TfidfProcessor.FormatTfidfTagsForFilename(tags, "___");
                }
            }


            // --- 3. 报告生成阶段 (对应 Python 的 create_excel_report) ---
            WriteLine("\n[INFO] >>> 3. 开始生成 Excel 报告 <<<");
            bool reportSuccess = SimulateCreateExcelReport(imageData, ExcelPath); 

            if (!reportSuccess)
            {
                WriteLine("[ERROR] Excel 报告生成失败，流程中止。");
                return;
            }

            // --- 4. 评分/预测阶段 (对应 Python 的 image_scorer_supervised.py) ---
            WriteLine("\n[INFO] >>> 4. 开始评分预测与结果写入 <<<");
            // 直接调用评分流程
            ExecuteScoring(ExcelPath);
        }
        
        /// <summary>
        /// 仅运行评分/预测流程 (选项 3)
        /// </summary>
        private static void RunScoringOnlyFlow()
        {
            WriteLine("\n[INFO] >>> 1. 开始评分预测与结果写入 (仅评分模式) <<<");
            
            if (!File.Exists(ExcelPath))
            {
                WriteLine($"[WARNING] 找不到报告文件: {ExcelPath}。请先运行完整流程或确保文件存在。");
                return;
            }

            // 直接调用评分流程
            ExecuteScoring(ExcelPath);
        }
        
        /// <summary>
        /// 执行评分/预测的通用逻辑，包括报告打开。
        /// </summary>
        private static void ExecuteScoring(string excelPath)
        {
            var scorer = new ImageScorer();
            // 在实际项目中，这里需要将 Excel 文件路径传入，让评分器读取并写入结果
            scorer.CalculateAndWriteScores(excelPath);
            
            // --- 5. 最终报告打开逻辑 (无论选择哪个流程，报告生成/评分后都尝试打开) ---
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


        // ⚠️ 占位函数：模拟 Excel 报告创建 (原 SimulateCreateExcelReport)
        private static bool SimulateCreateExcelReport(List<ImageInfo> imageData, string path)
        {
            // 实际 C# 代码中需要使用 ClosedXML/EPPlus 等库来创建并填充 Excel 文件
            WriteLine($"[INFO] 模拟创建报告文件: {path} (包含 {imageData.Count} 条数据)");
            
            // 确保文件路径存在，以便后续 ImageScorer 可以“读取”它
            // 简单模拟文件创建
            try
            {
                // 创建一个空文件作为占位
                File.WriteAllText(path, "Simulated Excel Content");
                return true;
            }
            catch (Exception ex)
            {
                WriteLine($"[ERROR] 模拟文件创建失败: {ex.Message}");
                return false;
            }
        }
    }
}