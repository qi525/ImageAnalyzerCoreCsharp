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
    // C# 9.0+ 支持顶级语句，但为了兼容性和清晰性，使用传统 Program/Main 结构
    class Program
    {
        public static async Task Main(string[] args)
        {
            // --- 1. 初始化配置 ---
            // AnalyzerConfig 是静态类，无需实例化，直接通过类名访问成员。
            
            // --- 2. 定义您的文件路径和流程 ---
            // 已根据用户要求更新为新的图片存储目录
            string folderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史"; 
            string excelPath = @"C:\个人数据\C#Code\ImageAnalyzerCore\图片信息报告.xlsx";
            
            // --- 3. 扫描阶段 (对应 Python 的 image_scanner.py) ---
            WriteLine("[INFO] >>> 1. 开始扫描图片并提取元数据 <<<");
            var scanner = new ImageScanner();
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(folderToScan);

            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，程序退出。");
                return;
            }

            // --- 4. TF-IDF/关键词提取阶段 (对应 Python 的 tfidf_processor.py) ---
            WriteLine("\n[INFO] >>> 2. 开始 TF-IDF 关键词提取 <<<");
            var tfidfProcessor = new TfidfProcessor();
            
            // 收集所有已清洗的标签用于 TF-IDF 计算
            var tagsCorpus = imageData.Select(i => i.CleanedTags).ToList();
            
            // 提取 TF-IDF 关键词
            Dictionary<string, List<string>> tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);

            // 更新 ImageInfo 对象中的 TF-IDF 结果
            foreach (var info in imageData)
            {
                if (tfidfTagsMap.TryGetValue(info.FilePath, out var tags))
                {
                    // 修复问题三：FormatTfidfTagsForFilename 现在接受两个参数
                    info.CoreKeywords = TfidfProcessor.FormatTfidfTagsForFilename(tags, "___");
                }
            }


            // --- 5. 报告生成阶段 (对应 Python 的 create_excel_report) ---
            // ⚠️ 假设您有一个负责生成 Excel 的类，这里使用占位函数
            WriteLine("\n[INFO] >>> 3. 开始生成 Excel 报告 <<<");
            bool reportSuccess = SimulateCreateExcelReport(imageData, excelPath); 

            if (!reportSuccess)
            {
                WriteLine("[ERROR] Excel 报告生成失败，跳过后续步骤。");
                return;
            }


            // --- 6. 评分/预测阶段 (对应 Python 的 image_scorer_supervised.py) ---
            WriteLine("\n[INFO] >>> 4. 开始评分预测与结果写入 <<<");
            var scorer = new ImageScorer();
            // 在实际项目中，这里需要将 Excel 文件路径传入，让评分器读取并写入结果
            scorer.CalculateAndWriteScores(excelPath);


            // --- 7. 最终报告打开逻辑 (无论选择哪个流程，报告生成后都尝试打开) ---
            WriteLine($"\n[INFO] 报告已生成至: {excelPath}");
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

        // ⚠️ 占位函数：模拟 Excel 报告创建
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