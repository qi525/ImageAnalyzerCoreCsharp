// 文件名：FileArchiver.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Spectre.Console; // 引入Spectre.Console，用于控制台UI和进度条
// using Spectre.Console.Progress; // 已移除，解决 CS0138 错误


/// 流程说明【流程说明不可删除！！！】
/// 文件夹目录说明：
/// 需要整理的文件夹："C:\stable-diffusion-webui\outputs\txt2img-images"【固定】
/// 归档文件夹："C:\stable-diffusion-webui\outputs\txt2img-images\历史"【固定】
/// 
/// 必须实现的重点要求：
/// 警告！！！！需要保护的文件夹目录包含"超"，"精"，"特"，"处"个关键词的文件夹目录下的图片文件不允许被归档，被移动。【重要，重点保护对象】
/// 跳过整理“.bf”文件夹，这个文件夹是程序生成的一些缓存文件，不允许被移动。【非目标文件】
/// 
/// 目标：
/// 归档的图片文件范围："归档文件夹"的同层级的文件夹目录中的所有图片文件。
/// 归档的图片文件需要放到图片文件对应的文件夹目录，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
/// 
/// 细节要求：
/// ！！！为了保护特定文件夹目录的内容
/// 1. 使用一个专门用于移动的函数，实现图片文件的移动和归档。
/// 2. 不仅要在主流程上进行跳过保护文件夹目录的检查，还要在移动函数中再次进行检查，确保万无失一。
/// 3. 控制台打印最终实现归档的图片文件数量，成功数量和失败数量。
/// 流程说明【流程说明不可删除！！！】
/// 
namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件归档器：核心职责是根据安全和业务规则，将图片文件从源目录安全移动到日期归档目录。
    /// 难度系数：8/10 (涉及复杂的目录扫描、多重安全检查、日期目录创建和并发计数，现增加了新库重构)
    /// </summary>
    public class FileArchiver
    {
        // --- 核心配置 ---
        // 归档流程说明中的固定路径
        private const string RootDirectory = @"C:\stable-diffusion-webui\outputs\txt2img-images"; // 需要整理的文件夹目录
        private const string ArchiveTargetDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史"; // 归档文件夹目录
        
        // 必须实现的重点要求：保护关键词
        private static readonly List<string> ProtectedKeywords = new List<string> { "超", "精", "特", "处" }; // 新增“处”关键词
        // 跳过整理的文件夹
        private const string ExcludedFolderName = ".bf";

        // 【新功能】仅处理的图片文件后缀白名单
        private static readonly List<string> ImageExtensions = new List<string> { ".png", ".jpg", ".jpeg", ".webp" };

        // 用于记录处理状态和计数的并发字典（满足计数器要求）
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        // 进度条实例字段已移除，改为局部变量 ProgressTask

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
            Console.WriteLine($"扫描范围根目录: {RootDirectory} (文件夹目录)");
            Console.WriteLine($"归档目标目录: {ArchiveTargetDir} (文件夹目录)");

            // 1. 扫描文件
            List<string> filesToArchive = ScanFilesToArchive(RootDirectory, ArchiveTargetDir);
            
            int totalFiles = filesToArchive.Count;
            Console.WriteLine($"[INFO] 待归档图片文件总数: {totalFiles} 个");

            if (totalFiles == 0)
            {
                Console.WriteLine("[INFO] 没有找到符合条件的图片文件进行归档。流程结束。");
                return;
            }

            // 2. 并行处理文件
            // 【核心重构】：使用 Spectre.Console 替换 ShellProgressBar
            AnsiConsole.Progress()
                // 【新配置】：显示 任务名称、进度条、进度百分比、和剩余时间
                .Columns(new ProgressColumn[] // 修复 CS0246：将 IProgressColumn 替换为 ProgressColumn
                {
                    new TaskDescriptionColumn(),    // 显示任务名称/描述（归档中...）
                    new ProgressBarColumn(),        // 进度条方块
                    new PercentageColumn(),         // 修复 CS0246：ProgressTextColumn 不可用，替换为 PercentageColumn
                    new RemainingTimeColumn(),      // 显示剩余预估时间
                })
                .Start(ctx =>
                {
                    // 定义进度任务
                    var progressTask = ctx.AddTask("归档中...", new ProgressTaskSettings { MaxValue = totalFiles }); 

                    // 并行处理文件
                    Parallel.ForEach(filesToArchive, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                    {
                        // 传递 progressTask 给处理函数
                        ProcessFileForArchiving(filePath, progressTask);
                    });
                    
                    // 确保任务在退出前停止
                    progressTask.StopTask();
                });

            // 3. 打印最终统计结果 (细节要求 3)
            PrintFinalCounts(totalFiles);
            
            // 【新功能】 4. 清理空文件夹
            CleanEmptyDirectories(RootDirectory, ArchiveTargetDir);
        }
        
        /// <summary>
        /// [子函数 B] 检查文件是否为白名单中的图片文件类型。
        /// </summary>
        private static bool IsImageFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            // 使用.Contains进行严格后缀匹配
            return ImageExtensions.Contains(extension);
        }

        /// <summary>
        /// [步骤 2] 扫描并过滤需要归档的图片文件列表。
        /// 归档范围：RootDirectory的子文件夹目录中，排除保护、排除项和归档文件夹目录本身。
        /// </summary>
        private List<string> ScanFilesToArchive(string rootDir, string archiveDir)
        {
            var allFiles = new List<string>();
            // 针对只处理图片文件，EnumerateFiles的searchPattern不做限制，
            // 而是通过 IsImageFile 进行严格的后缀过滤，这样能统计到被跳过的非图片文件。
            const string searchPattern = "*";
            
            if (!Directory.Exists(rootDir))
            {
                return allFiles;
            }

            // 仍只遍历 RootDirectory 的第一层子目录，以排除 ArchiveDir 和顶层受保护目录
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
                    Console.WriteLine($"[SKIP] 跳过排除项文件夹目录: {dirPath}");
                    continue;
                }
                
                // 【修正点 1】检查保护关键词：在扫描阶段，应使用 IsPathProtected 来判断目录是否受保护。
                if (IsPathProtected(dirPath, isDirectory: true))
                {
                    Console.WriteLine($"[SAFETY] 跳过受保护文件夹目录（包含'超/精/特/处'）: {dirPath}");
                    _statusCounts.AddOrUpdate("安全跳过 (保护文件夹目录)", 1, (key, count) => count + 1);
                    continue; // 跳过整个受保护文件夹目录的文件收集
                }
                // 【修正点 1 结束】

                // 收集当前目录下的所有文件 (并进行图片后缀过滤)
                try
                {
                    // 修复：改为 AllDirectories 以递归扫描子文件夹
                    var filesInDir = Directory.EnumerateFiles(dirPath, searchPattern, SearchOption.AllDirectories); 
                    foreach (var filePath in filesInDir)
                    {
                        if (IsImageFile(filePath))
                        {
                            allFiles.Add(filePath);
                        }
                        else
                        {
                            // 【新状态键】记录被过滤掉的非图片文件
                            _statusCounts.AddOrUpdate("跳过 (非图片文件)", 1, (key, count) => count + 1);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"[ERROR] 无权限访问文件夹目录: {dirPath}. {ex.Message}");
                    // 不计入失败，因为是目录权限问题，不是文件处理失败
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 扫描文件夹目录 {dirPath} 时发生错误: {ex.Message}");
                }
            }

            return allFiles;
        }

        /// <summary>
        /// [步骤 3] 处理单个图片文件，执行二次安全检查并决定最终归档路径。
        /// </summary>
        /// <param name="filePath">待归档图片文件的完整路径。</param>
        /// <param name="task">Spectre.Console 的进度任务实例。</param>
        // 【签名修改】：接受 ProgressTask 参数
        private void ProcessFileForArchiving(string filePath, ProgressTask task)
        {
            // 确保源图片文件仍然存在
            if (!File.Exists(filePath))
            {
                _statusCounts.AddOrUpdate("跳过 (图片文件丢失)", 1, (key, count) => count + 1);
                task.Increment(1); // 【更新】：使用 task.Increment(1)
                return;
            }

            // 二次安全检查（在移动函数中再次检查源目录，确保万无失一 - 细节要求 2）
            // 【修正点 2】此处传入的是图片文件路径，isDirectory=false (默认值)
            if (IsPathProtected(filePath, isDirectory: false))
            {
                // 虽然在 ScanFilesToArchive 中已检查，但这里是针对文件本身路径的二次检查，用于确保逻辑正确性
                _statusCounts.AddOrUpdate("安全跳过 (保护文件/二次检查)", 1, (key, count) => count + 1);
                task.Increment(1); // 【更新】：使用 task.Increment(1)
                return;
            }
            // 【修正点 2 结束】

            // 目标：归档的图片文件需要放到文件对应的文件夹目录，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
            string todayDate = DateTime.Now.ToString("yyyy-MM-dd");
            string targetSubDir = Path.Combine(ArchiveTargetDir, todayDate);
            
            // 执行移动操作
            MoveFileSafe(filePath, targetSubDir, task); // 传递 task
        }

        /// <summary>
        /// [子函数 A] 检查图片文件/文件夹目录路径是否被配置为受保护路径（包含"超","精","特"），不允许自动移动。
        /// </summary>
        private static bool IsPathProtected(string path, bool isDirectory = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            
            string directoryName;

            if (isDirectory)
            {
                // 【修正点 3】如果传入的是目录，直接获取其名称进行检查
                directoryName = new DirectoryInfo(path).Name;
            }
            else
            { 
                // 【修正点 3】如果传入的是图片文件，获取图片文件所在的目录名
                directoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty).Name;
            }
            // 【修正点 3 结束】

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
        /// [步骤 4] 专门用于移动图片文件的安全函数，处理文件夹目录创建、冲突和异常。
        /// </summary>
        /// <param name="sourcePath">源图片文件路径。</param>
        /// <param name="targetDirectory">目标文件夹目录路径 (如 ...\历史\2025-11-15)。</param>
        /// <param name="task">Spectre.Console 的进度任务实例。</param>
        // 【签名修改】：接受 ProgressTask 参数
        private void MoveFileSafe(string sourcePath, string targetDirectory, ProgressTask task)
        {
            try
            {
                // 1. 创建目标文件夹目录 (如果不存在)
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    Console.WriteLine($"[INFO] 创建新归档文件夹目录: {targetDirectory}");
                }

                // 2. 构造目标图片文件路径
                string fileName = Path.GetFileName(sourcePath);
                string targetPath = Path.Combine(targetDirectory, fileName);

                // 3. 检查: 目标图片文件已存在 (避免覆盖)
                if (File.Exists(targetPath))
                {
                    Console.WriteLine($"[WARN] 目标图片文件已存在: {targetPath}，跳过归档以避免覆盖。");
                    _statusCounts.AddOrUpdate("跳过 (目标图片文件冲突)", 1, (key, count) => count + 1);
                    task.Increment(1); // 【更新】：使用 task.Increment(1)
                    return;
                }
                
                // 4. 检查: 源图片文件路径和目标路径相同 (幂等性保护)
                if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _statusCounts.AddOrUpdate("跳过 (源与目标路径相同)", 1, (key, count) => count + 1);
                    task.Increment(1); // 【更新】：使用 task.Increment(1)
                    return;
                }

                // 5. 执行移动图片文件操作
                File.Move(sourcePath, targetPath);
                task.Increment(1); // 【进度条更新】：使用 task.Increment(1)
                _statusCounts.AddOrUpdate("成功归档图片文件", 1, (key, count) => count + 1);
                // Console.WriteLine($"[MOVE] 成功归档: {sourcePath} -> {targetPath}"); // 移动成功日志太多，仅记录计数
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 归档图片文件失败: {sourcePath}. 错误: {ex.Message}");
                _statusCounts.AddOrUpdate("移动失败/异常", 1, (key, count) => count + 1);
                task.Increment(1); // 【进度条更新】：使用 task.Increment(1)
            }
        }
        
        /// <summary>
        /// [步骤 5] 打印最终统计结果。
        /// </summary>
        private void PrintFinalCounts(int totalFiles)
        {
            Console.WriteLine("\n--- 文件归档操作最终统计 ---");
            Console.WriteLine($"总共扫描图片文件: {totalFiles} 个");
            
            // 成功和失败/跳过计数
            int successCount = _statusCounts.GetValueOrDefault("成功归档图片文件", 0);
            int failedOrSkipped = totalFiles - successCount;
            
            Console.WriteLine($"成功归档图片文件: {successCount} 个");
            Console.WriteLine($"失败/跳过处理图片文件: {failedOrSkipped} 个");
            
            Console.WriteLine("\n--- 详细状态分类 ---\n");
            // 按照状态名称和计数打印详细信息，统一对齐
            var sortedCounts = _statusCounts.OrderByDescending(kv => kv.Value);
            foreach (var kvp in sortedCounts)
            {
                // 【修改点 2】：增加对齐宽度到 -40，以应对双字节中文和长状态名
                Console.WriteLine($"- {kvp.Key,-40}: {kvp.Value} 个");
            }
            Console.WriteLine("-----------------------------------");
        }
        
        /// <summary>
        /// [步骤 6] 清理空文件夹：在文件归档完成后，安全地删除所有空子文件夹。
        /// 难度系数：3/10 (涉及递归扫描和安全删除检查)
        /// </summary>
        private void CleanEmptyDirectories(string rootDir, string archiveDir)
        {
            Console.WriteLine("\n--- 启动空文件夹清理流程 ---");
            int deletedCount = 0;
            
            // 1. 获取所有子目录（递归），并按路径长度降序排序，实现从最深的子目录开始处理（自底向上）。
            var allDirectories = Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories)
                                          .OrderByDescending(dir => dir.Length) 
                                          .ToList();
            
            foreach (var dirPath in allDirectories)
            {
                // 2. 安全检查：跳过排除项和保护文件夹
                string dirName = new DirectoryInfo(dirPath).Name;
                
                // 排除归档目录本身 (ArchiveTargetDir)
                if (dirPath.Equals(archiveDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue; 
                }

                // 排除“.bf”文件夹
                if (dirName.Equals(ExcludedFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                // 检查保护关键词 ("超", "精", "特", "处")
                if (IsPathProtected(dirPath, isDirectory: true))
                {
                    continue;
                }

                // 3. 检查是否为空 (快速检查是否有任何文件或子目录)
                if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    try
                    {
                        // 4. 执行删除 (高风险操作，必须try-catch)
                        Directory.Delete(dirPath);
                        Console.WriteLine($"[DELETE SUCCESS] 清理空文件夹: {dirPath}");
                        deletedCount++;
                    }
                    catch (IOException)
                    {
                        // 常见错误：目录仍在被占用，或者权限问题，或者目录已经不是空了（被其他线程/进程写入）
                        Console.WriteLine($"[DELETE FAIL] 无法清理（可能被占用或权限不足）: {dirPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FATAL DELETE ERROR] 清理空文件夹时发生未知错误: {dirPath}. 错误: {ex.Message}");
                    }
                }
            }
            
            Console.WriteLine($"\n[INFO] 空文件夹清理完成。共删除 {deletedCount} 个空文件夹。");
        }
    }
}