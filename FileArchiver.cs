// 文件名：FileArchiver.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ShellProgressBar; // 引入进度条库


/// 流程说明【流程说明不可删除！！！】
/// 文件夹目录说明：
/// 需要整理的文件夹："C:\stable-diffusion-webui\outputs\txt2img-images"【固定】
/// 归档文件夹："C:\stable-diffusion-webui\outputs\txt2img-images\历史"【固定】
/// 
/// 必须实现的重点要求：
/// 警告！！！！需要保护的文件夹目录包含"超"，"精"，"特"三个关键词的文件夹目录下的图片文件不允许被归档，被移动。【重要，重点保护对象】
/// 跳过整理“.bf”文件夹，这个文件夹是程序生成的一些缓存文件，不允许被移动。【非目标文件】
/// 
/// 目标：
/// 归档的图片文件范围："归档文件夹"的同层级的文件夹目录中的所有图片文件。
/// 归档的图片文件需要放到图片文件对应的文件夹目录，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
/// 
/// 细节要求：
/// ！！！为了保护特定文件夹目录的内容
/// 1. 使用一个专门用于移动的函数，实现图片文件的移动和归档。
/// 2. 不仅要在主流程上进行跳过保护文件夹目录的检查，还要在移动函数中再次进行检查，确保万无一失。
/// 3. 控制台打印最终实现归档的图片文件数量，成功数量和失败数量。
/// 流程说明【流程说明不可删除！！！】
/// 
namespace ImageAnalyzerCore
{
    /// <summary>
    /// 文件归档器：核心职责是根据安全和业务规则，将图片文件从源目录安全移动到日期归档目录。
    /// 难度系数：7/10 (涉及复杂的目录扫描、多重安全检查、日期目录创建和并发计数)
    /// </summary>
    public class FileArchiver
    {
        // --- 核心配置 ---
        // 归档流程说明中的固定路径
        private const string RootDirectory = @"C:\stable-diffusion-webui\outputs\txt2img-images"; // 需要整理的文件夹目录
        private const string ArchiveTargetDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史"; // 归档文件夹目录
        
        // 必须实现的重点要求：保护关键词
        private static readonly List<string> ProtectedKeywords = new List<string> { "超", "精", "特" }; 
        // 跳过整理的文件夹
        private const string ExcludedFolderName = ".bf";

        // 【新功能】仅处理的图片文件后缀白名单
        private static readonly List<string> ImageExtensions = new List<string> { ".png", ".jpg", ".jpeg", ".webp" };

        // 用于记录处理状态和计数的并发字典（满足计数器要求）
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        // 进度条实例
        private IProgressBar? _progressBar; // 声明为可为 null，解决 CS8618 警告

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
            // 使用 ShellProgressBar 实现类似 tqdm 的实时预览
            // 【修改点 1】：隐藏预估剩余时间，节省右侧空间，优先显示已用时间
            var options = new ProgressBarOptions 
            { 
                ForegroundColor = ConsoleColor.Yellow, 
                BackgroundColor = ConsoleColor.DarkGray, 
                ProgressBarOnBottom = true,
                DisplayTimeInRealTime = false, // 减少实时更新的开销和潜在截断问题
                ShowEstimatedDuration = false // 隐藏预估剩余时间，释放空间以显示完整的“已用时间”
            };
            using (_progressBar = new ProgressBar(totalFiles, "图片文件归档中...", options))
            {
                Parallel.ForEach(filesToArchive, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                {
                    ProcessFileForArchiving(filePath);
                });

                // 确保进度条完成
                // 【修改点 3】：使用 PadRight 填充空格，强制消息占据一定宽度，避免进度条主体挤压计时器。
                _progressBar.Message = "归档完成。".PadRight(10, ' ');
            }

            // 3. 打印最终统计结果 (细节要求 3)
            PrintFinalCounts(totalFiles);
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
                    Console.WriteLine($"[SAFETY] 跳过受保护文件夹目录（包含'超/精/特'）: {dirPath}");
                    _statusCounts.AddOrUpdate("安全跳过 (保护文件夹目录)", 1, (key, count) => count + 1);
                    continue; // 跳过整个受保护文件夹目录的文件收集
                }
                // 【修正点 1 结束】

                // 收集当前目录下的所有文件 (并进行图片后缀过滤)
                try
                {
                    var filesInDir = Directory.EnumerateFiles(dirPath, searchPattern, SearchOption.TopDirectoryOnly);
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
        private void ProcessFileForArchiving(string filePath)
        {
            // 确保源图片文件仍然存在
            if (!File.Exists(filePath))
            {
                _statusCounts.AddOrUpdate("跳过 (图片文件丢失)", 1, (key, count) => count + 1);
                _progressBar?.Tick(); // 文件处理完成，更新进度条
                return;
            }

            // 二次安全检查（在移动函数中再次检查源目录，确保万无失一 - 细节要求 2）
            // 【修正点 2】此处传入的是图片文件路径，isDirectory=false (默认值)
            if (IsPathProtected(filePath, isDirectory: false))
            {
                // 虽然在 ScanFilesToArchive 中已检查，但这里是针对文件本身路径的二次检查，用于确保逻辑正确性
                _statusCounts.AddOrUpdate("安全跳过 (保护文件/二次检查)", 1, (key, count) => count + 1);
                _progressBar?.Tick(); // 文件处理完成，更新进度条
                return;
            }
            // 【修正点 2 结束】

            // 目标：归档的图片文件需要放到文件对应的文件夹目录，例如"2025-11-15"【文件夹格式：yyyy-mm-dd】
            string todayDate = DateTime.Now.ToString("yyyy-MM-dd");
            string targetSubDir = Path.Combine(ArchiveTargetDir, todayDate);
            
            // 执行移动操作
            MoveFileSafe(filePath, targetSubDir);
        }

        /// <summary>
        /// [子函数 A] 检查图片文件/文件夹目录路径是否被配置为受保护路径（包含"超","精","特"），不允许自动移动。
        /// (细节要求 2：检查逻辑)
        /// </summary>
        /// <param name="path">图片文件或文件夹目录的完整路径。</param>
        /// <param name="isDirectory">如果传入的是文件夹目录路径，则设置为 true。</param>
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
        /// (细节要求 1：专门用于移动的函数)
        /// </summary>
        /// <param name="sourcePath">源图片文件路径。</param>
        /// <param name="targetDirectory">目标文件夹目录路径 (如 ...\历史\2025-11-15)。</param>
        private void MoveFileSafe(string sourcePath, string targetDirectory)
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
                    _progressBar?.Tick(); // 移动跳过，更新进度条
                    return;
                }
                
                // 4. 检查: 源图片文件路径和目标路径相同 (幂等性保护)
                if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _statusCounts.AddOrUpdate("跳过 (源与目标路径相同)", 1, (key, count) => count + 1);
                    _progressBar?.Tick(); // 移动跳过，更新进度条
                    return;
                }

                // 5. 执行移动图片文件操作
                File.Move(sourcePath, targetPath);
                _progressBar?.Tick(); // 【进度条更新】成功移动，更新进度条
                _statusCounts.AddOrUpdate("成功归档图片文件", 1, (key, count) => count + 1);
                // Console.WriteLine($"[MOVE] 成功归档: {sourcePath} -> {targetPath}"); // 移动成功日志太多，仅记录计数
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 归档图片文件失败: {sourcePath}. 错误: {ex.Message}");
                _statusCounts.AddOrUpdate("移动失败/异常", 1, (key, count) => count + 1);
                _progressBar?.Tick(); // 【进度条更新】移动失败，更新进度条
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
            
            Console.WriteLine("\n--- 详细状态分类 ---");
            // 按照状态名称和计数打印详细信息，统一对齐
            var sortedCounts = _statusCounts.OrderByDescending(kv => kv.Value);
            foreach (var kvp in sortedCounts)
            {
                // 【修改点 2】：增加对齐宽度到 -40，以应对双字节中文和长状态名
                Console.WriteLine($"- {kvp.Key,-40}: {kvp.Value} 个");
            }
            Console.WriteLine("-----------------------------------");
        }
    }
}