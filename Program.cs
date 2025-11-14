// 文件名：Program.cs
// 难度系数：2/10 (主要是接口和调用名匹配修正)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// 引入依赖的命名空间
using ImageAnalyzerCore; 
// 假设这些是项目中的配置和数据结构
// using AnalyzerConfig;
// using ImageInfo; 

public class Program
{
    // ----------------------------------------------------
    // [推断/骨架] ImageScanner.cs 所需的依赖结构
    // ----------------------------------------------------
    // 假设 ImageInfo 是您定义的数据结构 (已在 ImageScanner.cs 中定义，这里不需要重复)
    
    // 假设 AnalyzerConfig 是配置类
    public static class AnalyzerConfig 
    {
        public static int MaxConcurrentWorkers = Environment.ProcessorCount;
        public const string UnclassifiedFolderName = "未分类";
        public static List<string> ProtectedFolderNames = new List<string> { "protected" };
        public static List<string> FuzzyProtectedKeywords = new List<string> { "ignore" };
        
        // 匹配 ImageScanner.cs 的新依赖
        public static HashSet<string> ImageExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp" };
        public static List<string> PositivePromptStopWords = new List<string> { "masterpiece", "masterwork", "best quality", "masterpiece, best quality" };
    }
    
    // 假设 FilenameTagger 是辅助类
    public static class FilenameTagger
    {
        public static string GetUniqueFilename(string targetDir, string filename)
        {
            // 简化：总是返回原始文件名
            return filename;
        }
    }
    // ----------------------------------------------------


    // 核心的执行入口
    public static async Task Main(string[] args)
    {
        Console.WriteLine("--- 图像分析与文件管理启动 ---");
        
        // 1. 配置运行时参数 (仅为演示，实际应从命令行或配置文件读取)
        bool runTagger = true; // 是否执行标签处理
        bool runCategorizer = true; // 是否执行分类
        bool runArchiver = false; // 是否执行归档 (互斥)
        
        // ⚠️ 模拟需要处理的目录
        string FolderToScan = @"C:\TestImages"; 

        // 2. 执行扫描与TF-IDF处理 
        Console.WriteLine("[INFO] 启动文件扫描与标签/TF-IDF处理...");
        // 修复：调用修改后的方法 RunScanAndTfidfProcessing
        var imageData = await RunScanAndTfidfProcessing(runTagger, FolderToScan); 
        
        if (imageData == null || !imageData.Any())
        {
            Console.WriteLine("[FATAL] 扫描或TF-IDF处理未能返回有效数据。程序中止。");
            return;
        }
        
        // 3. 执行分类或归档 (互斥)
        if (runCategorizer)
        {
            // 运行分类器（根据第一个核心词移动）
            Console.WriteLine("[INFO] 开始执行文件自动分类...");
            var categorizer = new FileCategorizer();
            categorizer.CategorizeAndMoveImages(imageData, FolderToScan);
        }
        else if (runArchiver)
        {
            // 运行归档器（将文件移动到“已归档”目录）
            Console.WriteLine("[INFO] 开始执行归档...");
            var archiver = new ScoreArchive();
            // 注意：ArchiveImages 目前是无条件归档
            archiver.ArchiveImages(imageData, FolderToScan); 
        }
        
        Console.WriteLine("--- 图像分析与文件管理结束 ---");
    }

    /// <summary>
    /// [修复定义] 文件扫描和TF-IDF处理流程。
    /// 包含 ImageScanner 的调用以获取 imageData。
    /// </summary>
    /// <param name="runTagger">是否执行TF-IDF计算。</param>
    /// <param name="folderToScan">扫描的根目录。</param>
    /// <returns>处理后的图片信息列表。</returns>
    // @@    100-100,101-101   @@ 修改方法名以避免歧义，并修正方法逻辑
    private static async Task<List<ImageAnalyzerCore.ImageInfo>> RunScanAndTfidfProcessing(bool runTagger, string folderToScan)
    {
        // 1. 文件扫描与元数据提取 (调用 ImageScanner 的核心功能)
        var imageScanner = new ImageScanner();
        // 修正：调用 ImageScanner.cs 中的 ScanAndExtractInfo 方法获取 imageData
        var scannedData = imageScanner.ScanAndExtractInfo(folderToScan);
        
        // 如果扫描结果为空，则直接返回
        if (!scannedData.Any())
        {
            return new List<ImageAnalyzerCore.ImageInfo>();
        }

        // 2. 标签处理与 TF-IDF 提取
        if (runTagger)
        {
            var tfidfProcessor = new TfidfProcessor();
            // 此处是同步调用，为了演示逻辑，不影响主流程的异步性
            var tfidfResults = tfidfProcessor.ExtractTfidfTags(scannedData);
            
            // [TODO] 整合 TF-IDF 结果到 scannedData 中的 ImageInfo 对象（例如更新 CoreKeywords）
            foreach (var info in scannedData)
            {
                if (tfidfResults.TryGetValue(info.FilePath, out List<string>? tags) && tags.Any())
                {
                    // 将 TF-IDF 提取的 Top N 标签作为新的核心关键词
                    info.CoreKeywords = string.Join(", ", tags); 
                }
            }

            Console.WriteLine($"[INFO] TF-IDF处理完成，已更新 {tfidfResults.Count} 个文件的关键词。");
        }
        
        // 模拟异步操作的等待
        await Task.Delay(10); 
        
        return scannedData;
    }
}