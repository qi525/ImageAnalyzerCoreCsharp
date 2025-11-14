// 文件名：FileArchiver.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;


/// 流程说明【流程说明不可删除！！！】
/// 文件目录说明：
/// 需要整理的文件夹："C:\stable-diffusion-webui\outputs\txt2img-images"【固定】
/// 归档文件夹："C:\stable-diffusion-webui\outputs\txt2img-images\历史"【固定】
/// 
/// 必须实现的重点要求：
/// 警告！！！！需要保护的文件夹包含"超"，"精"，"特"三个关键词的文件夹的文件不允许被归档，被移动。【重要，重点保护对象】
/// 跳过整理“.bf”文件夹，这个文件夹是程序生成的一些缓存文件，不允许被移动。【非目标文件】
/// 
/// 目标：
/// 归档的文件范围："归档文件夹"的同层级的文件夹中的所有文件。
/// 归档的文件需要放到文件对应的文件夹，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
/// 
/// 细节要求：
/// ！！！为了保护特定文件夹的内容
/// 1. 使用一个专门用于移动的函数，实现文件的移动和归档。
/// 2. 不仅要在主流程上进行跳过保护文件夹的检查，还要在移动函数中再次进行检查，确保万无一失。
/// 3. 控制台打印最终实现归档的文件数量，成功数量和失败数量。
/// 流程说明【流程说明不可删除！！！】
/// 
namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件归档器：核心职责是根据安全和业务规则，将文件从源目录安全移动到日期归档目录。
    /// 难度系数：7/10 (涉及复杂的目录扫描、多重安全检查、日期目录创建和并发计数)
    /// </summary>
    public class FileArchiver
    {
        // --- 核心配置 ---
        // 归档流程说明中的固定路径
        private const string RootDirectory = @"C:\stable-diffusion-webui\outputs\txt2img-images"; // 需要整理的文件夹
        private const string ArchiveTargetDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史"; // 归档文件夹
        
        // 必须实现的重点要求：保护关键词
        private static readonly List<string> ProtectedKeywords = new List<string> { "超", "精", "特" }; 
        // 跳过整理的文件夹
        private const string ExcludedFolderName = ".bf";

        // 用于记录处理状态和计数的并发字典（满足计数器要求）
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// [步骤 1] 启动归档主流程，负责调用所有子步骤并打印最终统计结果。
        /// </summary>
        public void ExecuteArchiving()
        {
            if (!Directory.Exists(RootDirectory))
            {
                Console.WriteLine($"[FATAL ERROR] 根目录不存在: {RootDirectory}。流程中止。");
                return;
            }

            Console.WriteLine("\n--- 文件归档流程启动 ---");
            Console.WriteLine($"扫描范围根目录: {RootDirectory}");
            Console.WriteLine($"归档目标目录: {ArchiveTargetDir}");

            // 1. 扫描文件
            List<string> filesToArchive = ScanFilesToArchive(RootDirectory, ArchiveTargetDir);
            
            int totalFiles = filesToArchive.Count;
            Console.WriteLine($"[INFO] 待归档文件总数: {totalFiles} 个");

            if (totalFiles == 0)
            {
                Console.WriteLine("[INFO] 没有找到符合条件的文件进行归档。流程结束。");
                return;
            }

            // 2. 并行处理文件
            Parallel.ForEach(filesToArchive, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
            {
                ProcessFileForArchiving(filePath);
            });

            // 3. 打印最终统计结果 (细节要求 3)
            PrintFinalCounts(totalFiles);
        }
        
        /// <summary>
        /// [步骤 2] 扫描并过滤需要归档的文件列表。
        /// 归档范围：RootDirectory的子目录中，排除保护、排除项和归档文件夹本身。
        /// </summary>
        private List<string> ScanFilesToArchive(string rootDir, string archiveDir)
        {
            var allFiles = new List<string>();
            var targetDirectories = Directory.EnumerateDirectories(rootDir, "*", SearchOption.TopDirectoryOnly)
                                             .ToList();

            foreach (var dirPath in targetDirectories)
            {
                string dirName = new DirectoryInfo(dirPath).Name;

                // 排除归档目录本身
                if (dirPath.Equals(archiveDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 排除“.bf”文件夹 (非目标文件)
                if (dirName.Equals(ExcludedFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[SKIP] 跳过排除项文件夹: {dirPath}");
                    continue;
                }
                
                // 检查保护关键词 (主流程检查 - 细节要求 2)
                if (IsPathProtected(dirPath))
                {
                    Console.WriteLine($"[SAFETY] 跳过受保护文件夹（包含'超/精/特'）: {dirPath}");
                    _statusCounts.AddOrUpdate("安全跳过 (保护路径)", 1, (key, count) => count + 1);
                    continue;
                }

                // 收集当前目录下的所有文件
                try
                {
                    allFiles.AddRange(Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly));
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"[ERROR] 无权限访问目录: {dirPath}. {ex.Message}");
                    // 不计入失败，因为是目录权限问题，不是文件处理失败
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 扫描目录 {dirPath} 时发生错误: {ex.Message}");
                }
            }

            return allFiles;
        }

        /// <summary>
        /// [步骤 3] 处理单个文件，执行二次安全检查并决定最终归档路径。
        /// </summary>
        /// <param name="filePath">待归档文件的完整路径。</param>
        private void ProcessFileForArchiving(string filePath)
        {
            // 确保源文件仍然存在
            if (!File.Exists(filePath))
            {
                _statusCounts.AddOrUpdate("跳过 (文件丢失)", 1, (key, count) => count + 1);
                return;
            }

            // 二次安全检查（在移动函数中再次检查源目录，确保万无一失 - 细节要求 2）
            if (IsPathProtected(filePath))
            {
                // 虽然在 ScanFilesToArchive 中已检查，但这里是针对文件本身路径的二次检查，用于确保逻辑正确性
                _statusCounts.AddOrUpdate("安全跳过 (二次检查)", 1, (key, count) => count + 1);
                return;
            }

            // 目标：归档的文件需要放到文件对应的文件夹，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
            string todayDate = DateTime.Now.ToString("yyyy-MM-dd");
            string targetSubDir = Path.Combine(ArchiveTargetDir, todayDate);
            
            // 执行移动操作
            MoveFileSafe(filePath, targetSubDir);
        }

        /// <summary>
        /// [子函数 A] 检查路径是否被配置为受保护路径（包含"超","精","特"），不允许自动移动。
        /// (细节要求 2：检查逻辑)
        /// </summary>
        private static bool IsPathProtected(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            // 获取文件所在的目录名 (e.g., "精选图片" -> "精选图片")
            string directoryName = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name;

            if (string.IsNullOrEmpty(directoryName)) return false;

            // 检查目录名是否包含保护关键词
            foreach (var keyword in ProtectedKeywords)
            {
                if (directoryName.Contains(keyword))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// [步骤 4] 专门用于移动文件的安全函数，处理目录创建、冲突和异常。
        /// (细节要求 1：专门用于移动的函数)
        /// </summary>
        /// <param name="sourcePath">源文件路径。</param>
        /// <param name="targetDirectory">目标文件夹路径 (如 ...\历史\2025-11-15)。</param>
        private void MoveFileSafe(string sourcePath, string targetDirectory)
        {
            try
            {
                // 1. 创建目标目录 (如果不存在)
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    Console.WriteLine($"[INFO] 创建新归档目录: {targetDirectory}");
                }

                // 2. 构造目标路径
                string fileName = Path.GetFileName(sourcePath);
                string targetPath = Path.Combine(targetDirectory, fileName);

                // 3. 检查: 目标文件已存在 (避免覆盖)
                if (File.Exists(targetPath))
                {
                    Console.WriteLine($"[WARN] 目标文件已存在: {targetPath}，跳过归档以避免覆盖。");
                    _statusCounts.AddOrUpdate("跳过 (目标冲突)", 1, (key, count) => count + 1);
                    return;
                }
                
                // 4. 检查: 源路径和目标路径相同 (幂等性保护)
                if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _statusCounts.AddOrUpdate("跳过 (路径相同)", 1, (key, count) => count + 1);
                    return;
                }

                // 5. 执行移动操作
                File.Move(sourcePath, targetPath);
                _statusCounts.AddOrUpdate("成功归档", 1, (key, count) => count + 1);
                // Console.WriteLine($"[MOVE] 成功归档: {sourcePath} -> {targetPath}"); // 移动成功日志太多，仅记录计数
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 归档文件失败: {sourcePath}. 错误: {ex.Message}");
                _statusCounts.AddOrUpdate("移动失败/异常", 1, (key, count) => count + 1);
            }
        }
        
        /// <summary>
        /// [步骤 5] 打印最终统计结果。
        /// </summary>
        private void PrintFinalCounts(int totalFiles)
        {
            Console.WriteLine("\n--- 文件归档操作最终统计 ---");
            Console.WriteLine($"总共扫描文件: {totalFiles} 个");
            
            // 成功和失败/跳过计数
            int successCount = _statusCounts.GetValueOrDefault("成功归档", 0);
            int failedOrSkipped = totalFiles - successCount;
            
            Console.WriteLine($"成功归档文件: {successCount} 个");
            Console.WriteLine($"失败/跳过处理文件: {failedOrSkipped} 个");
            
            Console.WriteLine("\n--- 详细状态分类 ---");
            // 按照状态名称和计数打印详细信息
            var sortedCounts = _statusCounts.OrderByDescending(kv => kv.Value);
            foreach (var kvp in sortedCounts)
            {
                Console.WriteLine($"- {kvp.Key,-20}: {kvp.Value} 个");
            }
            Console.WriteLine("-----------------------------");
        }
    }
}