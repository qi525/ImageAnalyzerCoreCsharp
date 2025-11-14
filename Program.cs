// 文件名：Program.cs

using System;
using System.IO;
using System.Linq;
using ImageAnalyzerCore;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
// [新增] 引入 ClosedXML 库，用于实际写入 XLSX 文件
using ClosedXML.Excel; 


// 引入 Logger 机制 (Loguru 的 C# 替代方案，这里使用简单的 Console.WriteLine 占位)
using static System.Console; // 简化 Console.WriteLine 为 WriteLine

namespace ImageAnalyzerCore
{
    class Program
    {
        // @@    27-33,28-31   @@
        // --- 1. 定义您的文件路径和配置 (改为静态字段) ---
        // Excel 报告的固定存储目录
        private const string ExcelDirectory = @"C:\个人数据\C#Code\ImageAnalyzerCore";
        // Excel 报告的基础文件名：已根据要求修改为 "C#版图片信息报告_"
        private const string ExcelBaseName = "C#版图片信息报告_"; 

        public static async Task Main(string[] args)
        {
            WriteLine("[INFO] 欢迎使用图片分析报告生成工具");
            WriteLine("-----------------------------------");
            
            while (true)
            {
                // **【新增逻辑】**：动态生成带时间戳的报告文件名
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // 文件名格式: C#版图片信息报告_YYYYMMDD_HHmmss.xlsx
                string finalExcelFileName = $"{ExcelBaseName}{timestamp}.xlsx"; 
                string excelPath = Path.Combine(ExcelDirectory, finalExcelFileName);
                
                DisplayMenu();
                Write("请输入您的选择 (1-8): "); // 菜单项已增至 8
                // Bug Fix: 使用 ?? string.Empty 确保 choice 永远是不可为 null 的 string
                string choice = ReadLine()?.Trim() ?? string.Empty; 

                try
                {
                    // @@    53-61,53-61   @@  流程顺序已调整：1-只读 -> 2/3/4-移动 -> 5/6-重命名 -> 7-完整流程 -> 8-退出
                    switch (choice)
                    {   
                        case "1":
                            // 选项 1: 仅运行扫描和报告生成 (Scan -> Report -> Open / 只读)
                            await RunScanAndReportFlowAsync(excelPath); // 传递动态路径
                            break;
                        case "2":
                            // 选项 2: 仅归档流程 (Scan -> Archive / 移动)
                            await RunScoreArchiveFlowAsync();
                            break;
                        case "3":
                            // 选项 3: 仅仅分类流程 (Scan -> Categorize / 移动)
                            await RunCategorizeFlowAsync();
                            break;
                        case "4":
                            // 选项 4: 指定评分图片移动到外层 (Score Organizer / 移动)
                            await RunScoreOrganizerFlowAsync();
                            break;
                        case "5":
                            // 选项 5: 仅自动添加 10 个 tag (Tagging Flow / 重命名)
                            await RunTagFlowAsync();
                            break;
                        case "6":
                            // 选项 6: 仅自动添加评分 (Scoring and Tagging Flow / 重命名)
                            await RunScoreTagFlowAsync();
                            break;
                        case "7":
                            // 选项 7: 完整流程 (Full Flow)
                            await RunFullAnalysisFlowAsync(excelPath); 
                            break;
                        case "8":
                            WriteLine("[INFO] 退出程序。");
                            return;
                        default:
                            WriteLine("[WARNING] 无效的选项，请重新输入 1-8 之间的数字。"); // 提示更新
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
            // 选项已按简单到复杂重新排序
            WriteLine("请选择您要执行的操作：");
            WriteLine("  1. 仅扫描生成表格（只读，Scan -> Report）");
            WriteLine("  2. 仅归档到历史文件夹（移动，Scan -> Archive）"); 
            WriteLine("  3. 仅分类历史文件夹（移动，Scan -> Categorize）"); 
            WriteLine("  4. 仅移动历史评分到外层（移动，Score Organizer）"); 
            WriteLine("  5. 仅自动添加 10 个 tag（重命名，Scan -> TF-IDF -> Tagging）");
            WriteLine("  6. 仅自动添加评分（重命名，Scan -> TF-IDF -> Scoring -> Tagging）");
            WriteLine("  7. 完整流程 (Scan -> TF-IDF -> Report -> Scoring)");
            WriteLine("  8. 退出程序"); // 退出选项改为 8
        }

        /// <summary>
        /// 完整分析流程：扫描 -> TF-IDF -> 报告 -> 评分 (选项 7)
        /// </summary>
        private static async Task RunFullAnalysisFlowAsync(string excelPath)
        {
            // 1. 运行扫描和报告生成 (提取公共部分)
            List<ImageInfo> imageData = await RunScanAndReportGenerationAsync(excelPath);

            if (!imageData.Any())
            {
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
                    // 假设 TfidfProcessor.FormatTfidfTagsForFilename 存在于 TfidfProcessor.cs 中
                    info.CoreKeywords = TfidfProcessor.FormatTfidfTagsForFilename(tags, "___");
                }
            }

            // --- 3. 评分/预测阶段 (对应 Python 的 image_scorer_supervised.py) ---
            WriteLine("\n[INFO] >>> 3. 开始评分预测与结果写入 <<<");
            // 运行评分并打开报告
            ExecutePostReportActions(excelPath, runScoring: true); 
        }
        
        /// <summary>
        /// 仅运行扫描和报告生成流程 (选项 1 - 只读)
        /// </summary>
        private static async Task RunScanAndReportFlowAsync(string excelPath)
        {
            // 1. 运行扫描和报告生成
            List<ImageInfo> imageData = await RunScanAndReportGenerationAsync(excelPath);

            if (!imageData.Any())
            {
                return;
            }

            // 2. 自动打开报告 (不运行评分)
            ExecutePostReportActions(excelPath, runScoring: false);
        }

        /// <summary>
        /// 指定评分图片移动到外层流程 (选项 4 - 评分整理/移动)
        /// </summary>
        private static async Task RunScoreOrganizerFlowAsync()
        {
            WriteLine("\n[INFO] >>> 4. 指定评分图片移动到外层 (Score Organizer / 移动) <<<");
            // 假设 ScoreOrganizer 类已在 ImageAnalyzerCore 命名空间中定义
            var organizer = new ScoreOrganizer(); 
            
            // 假设 ScoreOrganizer 包含这些静态配置
            WriteLine($"文件来源 (历史目录): {ScoreOrganizer.StaticSourceRootDir}");
            WriteLine($"目标目录 (评分XX父目录): {ScoreOrganizer.stringStaticTargetBaseDir}");
            
            WriteLine("\n请在控制台输入您想要整理的评分范围（如 '80-99'）或单个评分（如 '80'）：");
            Write("评分输入 (例如 80-99 或 80): ");
            
            // 使用 ?? string.Empty 确保 userInput 永远是不可为 null 的 string
            string userInput = ReadLine()?.Trim() ?? string.Empty;
            
            if (!string.IsNullOrEmpty(userInput))
            {
                // 注意: 评分整理功能通常不需要先扫描，因为它直接操作文件系统
                organizer.OrganizeFiles(userInput);
            }
            else
            {
                WriteLine("[WARNING] 用户未提供评分输入，流程中止。");
            }
        }
        
        /// <summary>
        /// 仅归档流程 (选项 2: Scan -> Archive / 移动)
        /// 【修正】直接调用 FileArchiver，以实现所有安全和归档逻辑。
        /// </summary>
        // @@    271-295,271-280   @@
        private static async Task RunScoreArchiveFlowAsync()
        {
            WriteLine("\n[INFO] >>> 2. 仅归档流程 (Scan -> Archive / 移动) <<<");
            
            // 1. 执行归档逻辑 (直接调用 FileArchiver，它内部包含了扫描、安全检查和移动)
            WriteLine("[INFO] 开始执行安全文件归档操作...");
            var archiver = new FileArchiver(); 
            // FileArchiver 的 ExecuteArchiving 方法封装了完整的归档流程
            archiver.ExecuteArchiving(); 
            
            // 流程结束，FileArchiver 已打印最终统计结果
        }

        /// <summary>
        /// 仅仅分类流程 (选项 3: Scan -> Categorize / 移动)
        /// </summary>
        // @@    297-320,297-319   @@
        private static async Task RunCategorizeFlowAsync()
        {
            WriteLine("\n[INFO] >>> 3. 仅仅分类流程 (Scan -> Categorize / 移动) <<<");

            // 1. 扫描图片并提取元数据，获取图片信息列表
            WriteLine("[INFO] 开始扫描图片并提取元数据...");
            var scanner = new ImageScanner();
            // 【注意】FolderToScan 已被移除，这里需要依赖一个默认或传入的目录
            // 为了安全，暂时将 FolderToScan 替换为 FileArchiver 里的硬编码目录（但更好的做法是引入一个中央配置类）
            // 考虑到分类流程的耦合性较小，暂时使用一个占位符或假设 ImageScanner 知道自己的扫描目标
            // 假设分类流程仍然针对历史目录进行（如原代码定义），这里暂时使用硬编码路径作为扫描根目录
            // 但如果 ImageScanner 依赖 FolderToScan，这里可能会出问题。由于原代码使用 FolderToScan，且已被移除，
            // 临时将该路径定义在 Program.cs 中，或者从 FileArchiver 中借用 (为保持 FileArchiver 纯净，不推荐)
            // 最佳实践是重新引入一个中央配置项，但为了最小改动，先用一个临时值（假设历史目录）
            const string FallbackFolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FallbackFolderToScan);
            
            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，分类流程中止。");
                return;
            }

            // 2. 执行分类逻辑 (使用 FileCategorizer)
            WriteLine("\n[INFO] 开始执行图片两级分类操作...");
            var categorizer = new FileCategorizer();
            // 分类目标目录就是扫描的根目录
            categorizer.CategorizeAndMoveImages(imageData, FallbackFolderToScan);
        }
        
        /// <summary>
        /// 仅自动添加 10 个 tag 流程 (选项 5: Scan -> TF-IDF -> Tagging / 重命名)
        /// </summary>
        // @@    321-349,321-348   @@
        private static async Task RunTagFlowAsync()
        {
            WriteLine("\n[INFO] >>> 5. 仅自动添加 10 个 tag 流程 (重命名) <<<");

            // 1. 扫描图片并提取元数据
            WriteLine("[INFO] 开始扫描图片并提取元数据...");
            var scanner = new ImageScanner();
            // 【注意】FolderToScan 已被移除，使用临时值
            const string FallbackFolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FallbackFolderToScan);
            
            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，流程中止。");
                return;
            }
            
            // 2. TF-IDF/关键词提取阶段
            WriteLine("\n[INFO] >>> 2. 开始 TF-IDF 关键词提取 <<<");
            var tfidfProcessor = new TfidfProcessor();
            Dictionary<string, List<string>> tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);

            // 3. 重命名/Tagging 阶段
            WriteLine("\n[INFO] >>> 3. 开始执行文件名标记 (Tagging) 操作 <<<");
            // 【TODO 修正】: FilenameTagger.cs 中需要定义一个接收三个参数（imageData, tfidfTagsMap, includeScore: false）的静态公共方法 TagFiles
            FilenameTagger.TagFiles(imageData, tfidfTagsMap); 
        }

        /// <summary>
        /// 仅自动添加评分流程 (选项 6: Scan -> TF-IDF -> Scoring -> Tagging / 重命名)
        /// </summary>
        // @@    351-383,351-382   @@
        private static async Task RunScoreTagFlowAsync()
        {
            WriteLine("\n[INFO] >>> 6. 仅自动添加评分流程 (重命名) <<<");
            
            // 1. 扫描图片并提取元数据
            WriteLine("[INFO] 开始扫描图片并提取元数据...");
            var scanner = new ImageScanner();
            // 【注意】FolderToScan 已被移除，使用临时值
            const string FallbackFolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FallbackFolderToScan);
            
            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，流程中止。");
                return;
            }

            // 2. TF-IDF/关键词提取阶段
            WriteLine("\n[INFO] >>> 2. 开始 TF-IDF 关键词提取 <<<");
            var tfidfProcessor = new TfidfProcessor();
            Dictionary<string, List<string>> tfidfTagsMap = tfidfProcessor.ExtractTfidfTags(imageData);

            // 3. 评分/预测阶段 (核心：将评分结果写回 ImageInfo 对象或直接进行操作)
            WriteLine("\n[INFO] >>> 3. 开始评分预测 <<<");
            var scorer = new ImageScorer(); // 假设 ImageScorer 存在且可实例化
            // 【TODO 修正】: ImageScorer.cs 中需要定义一个接收 List<ImageInfo> 的公共方法 PredictAndApplyScores
            scorer.PredictAndApplyScores(imageData); 

            // 4. 重命名/Tagging 阶段 (这次会包含评分信息)
            WriteLine("\n[INFO] >>> 4. 开始执行文件名标记 (Tagging) 操作，包含评分 <<<");
            // 【TODO 修正】: FilenameTagger.cs 中需要定义一个接收三个参数（imageData, tfidfTagsMap, includeScore: true）的静态公共方法 TagFiles
            FilenameTagger.TagFiles(imageData, tfidfTagsMap, includeScore: true); 
        }

        /// <summary>
        /// 统一过滤掉路径中包含 ".bf" 文件夹的文件。
        /// 【删除】FileArchiver 已实现归档时的安全检查和跳过逻辑，不再需要这个公共过滤函数
        /// </summary>
        // @@    385-410,385-385   @@

        /// <summary>
        /// 扫描图片并生成报告的公共部分。
        /// </summary>
        /// <returns>扫描到的图片信息列表，如果失败则返回一个空列表。</returns>
        private static async Task<List<ImageInfo>> RunScanAndReportGenerationAsync(string excelPath)
        {
            // --- 1. 扫描阶段 (对应 Python 的 image_scanner.py) ---
            WriteLine("[INFO] >>> 1. 开始扫描图片并提取元数据 <<<");
            var scanner = new ImageScanner();
            // 【注意】FolderToScan 已被移除，使用临时值
            const string FallbackFolderToScan = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
            List<ImageInfo> imageData = scanner.ScanAndExtractInfo(FallbackFolderToScan);

            // 【应用统一过滤】 - 已移除，假设 ImageScanner 自身处理文件筛选或在后续流程中处理
            
            if (!imageData.Any())
            {
                WriteLine("[INFO] 没有找到可处理的图片，流程中止。");
                // 修复：返回空列表而不是 null
                return new List<ImageInfo>(); 
            }

            // --- 2. 报告生成阶段 (对应 Python 的 create_excel_report) ---
            WriteLine("\n[INFO] >>> 2. 开始生成 Excel 报告 <<<");
            // 调用新的 ExcelReportGenerator 类进行报告创建 (包含计时和固定列宽逻辑)
            // 假设 ExcelReportGenerator 存在且静态方法 CreateExcelReport 可用
            bool reportSuccess = ExcelReportGenerator.CreateExcelReport(imageData, excelPath); 

            if (!reportSuccess)
            {
                WriteLine("[ERROR] Excel 报告生成失败，流程中止。");
                // 修复：返回空列表而不是 null
                return new List<ImageInfo>(); 
            }
            
            return imageData;
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
                // 假设 ImageScorer 存在并具有 CalculateAndWriteScores 方法，对应 Python 中的 ImageScorer.process_excel_file
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