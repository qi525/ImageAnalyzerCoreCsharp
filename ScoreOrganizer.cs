// ScoreOrganizer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Text;

namespace FileOrganizer
{
    public class ScoreOrganizer
    {
        // --- 配置项 ---
        // 历史输出文件夹的根目录 (源目录)
        private const string SourceRootDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
        // 目标评分文件夹的根目录 (目标根目录)
        private const string TargetBaseDir = @"C:\stable-diffusion-webui\outputs\txt2img-images";
        // 文件名中评分信息的正则表达式：匹配 '评分' 后面紧跟的两位数字
        private readonly Regex _scorePattern = new Regex(@"评分(\d{2})");
        
        // 【新增】受保护的目录列表 (绝对路径或相对于 SourceRootDir 的路径)
        private readonly string[] ProtectedDirs = 
        {
            @"C:\stable-diffusion-webui\outputs\txt2img-images\历史\Important_Backups", // 绝对路径示例
            @"Needs_Review",                                                           // 相对于 SourceRootDir 的相对路径示例
        };
        
        // 公共只读字段，方便 Main 函数访问路径
        public static readonly string StaticSourceRootDir = SourceRootDir;
        public static readonly string stringStaticTargetBaseDir = TargetBaseDir;
        
        private readonly ILogger _logger; 
        public const string LogFile = "score_organizer.log";

        // 构造函数：初始化日志
        public ScoreOrganizer()
        {
            // 将静态配置好的 Log.Logger 实例赋值给私有字段 _logger
            _logger = Log.Logger; 

            _logger.Information("文件评分整理程序启动。");
            _logger.Information($"源目录: {SourceRootDir}");
            _logger.Information($"目标目录: {TargetBaseDir}");
        }

        // --- 核心工具函数 ---

        /// <summary>
        /// 使用正则表达式从文件名中提取两位数的评分。
        /// </summary>
        /// <param name="filename">文件的完整名称（包含扩展名）。</param>
        /// <returns>提取到的评分数字（int），如果未找到则返回 null。</returns>
        public int? ExtractScore(string filename)
        {
            // 此处的 return null 是 C# 可空类型 int? 的标准用法，用于表示“未找到值”。
            var match = _scorePattern.Match(filename);
            if (match.Success && match.Groups.Count > 1)
            {
                if (int.TryParse(match.Groups[1].Value, out int score))
                {
                    return score;
                }
            }
            return null;
        }

        /// <summary>
        /// 解析用户输入的评分范围或单个评分，返回需要处理的评分集合。
        /// </summary>
        /// <param name="scoreInput">用户在控制台输入的字符串，如 '80-99' 或 '80'。</param>
        /// <returns>一个包含所有有效评分（两位数）的集合，如果输入无效则返回空的 HashSet。</returns>
        public HashSet<int> ParseScoreInput(string scoreInput)
        {
            scoreInput = scoreInput.Trim();

            // 尝试解析范围输入
            if (scoreInput.Contains("-"))
            {
                try
                {
                    var parts = scoreInput.Split('-', 2);
                    if (parts.Length != 2) throw new FormatException();

                    if (!int.TryParse(parts[0].Trim(), out int startScore) || 
                        !int.TryParse(parts[1].Trim(), out int endScore))
                    {
                        throw new FormatException();
                    }

                    // 校验评分是否为两位数 (10-99)
                    if (!(10 <= startScore && startScore <= 99 && 10 <= endScore && endScore <= 99))
                    {
                         _logger.Error($"输入的评分范围 '{scoreInput}' 必须是两位数 (10-99)。");
                         // 【修正点】返回空集合代替 null
                         return new HashSet<int>();
                    }

                    // 确保左边小右边大
                    if (startScore > endScore)
                    {
                        _logger.Warning($"检测到评分范围'{scoreInput}'左边大于右边，已自动交换为 {endScore}-{startScore}。");
                        (startScore, endScore) = (endScore, startScore);
                    }
                    
                    var scoreSet = new HashSet<int>(Enumerable.Range(startScore, endScore - startScore + 1));
                    _logger.Information($"已解析评分范围为: {startScore} 到 {endScore}，共 {scoreSet.Count} 个评分。");
                    return scoreSet;
                }
                catch (FormatException)
                {
                    _logger.Error($"无法解析评分范围输入 '{scoreInput}'，请确保格式正确，例如 '80-99'。");
                    // 【修正点】返回空集合代替 null
                    return new HashSet<int>();
                }
            }
            
            // 尝试解析单个评分输入
            else
            {
                try
                {
                    if (!int.TryParse(scoreInput, out int singleScore))
                    {
                        throw new FormatException();
                    }

                    // 校验评分是否为两位数 (10-99)
                    if (!(10 <= singleScore && singleScore <= 99))
                    {
                        _logger.Error($"输入的单个评分 '{scoreInput}' 必须是一个两位数 (10-99)。");
                        // 【修正点】返回空集合代替 null
                        return new HashSet<int>();
                    }
                    
                    _logger.Information($"已解析为单个评分: {singleScore}。");
                    return new HashSet<int> { singleScore };
                }
                catch (FormatException)
                {
                    _logger.Error($"无法解析评分输入 '{scoreInput}'，请确保输入的是数字或范围。");
                    // 【修正点】返回空集合代替 null
                    return new HashSet<int>();
                }
            }
        }
        
        /// <summary>
        /// 检查文件路径是否属于受保护的目录列表。
        /// </summary>
        private bool IsPathProtected(string filePath)
        {
            string normalizedFilePath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var protectedDir in ProtectedDirs)
            {
                string fullProtectedPath;
                
                if (Path.IsPathRooted(protectedDir))
                {
                    fullProtectedPath = Path.GetFullPath(protectedDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                else
                {
                    fullProtectedPath = Path.GetFullPath(Path.Combine(SourceRootDir, protectedDir)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                
                if (normalizedFilePath.StartsWith(fullProtectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// 主逻辑函数：根据用户输入整理文件。
        /// </summary>
        /// <param name="scoreInput">用户输入的评分范围或单个评分字符串。</param>
        public void OrganizeFiles(string scoreInput)
        {
            
            // 1. 安全检查和范围解析
            if (!Directory.Exists(SourceRootDir))
            {
                _logger.Error($"源目录不存在或无法访问: {SourceRootDir}");
                return;
            }

            // 解析评分输入，获取目标评分集合
            var targetScores = ParseScoreInput(scoreInput);
            // 【修正点】ParseScoreInput 现在返回空集合，无需检查 null
            if (targetScores.Count == 0)
            {
                _logger.Error("评分解析失败，程序终止。");
                return;
            }

            // --- 计数器初始化 ---
            int successfulMoves = 0;
            int failedMoves = 0;
            int skippedFiles = 0;
            
            _logger.Information("--- 开始扫描文件 ---");
            
            // 2. 扫描文件并计数
            string[] searchPatterns = { "*.png", "*.jpg", "*.jpeg", "*.webp" };
            List<string> allFilesToCheck = new List<string>();

            foreach (var pattern in searchPatterns)
            {
                try
                {
                    allFilesToCheck.AddRange(Directory.EnumerateFiles(SourceRootDir, pattern, SearchOption.AllDirectories));
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.Error(ex, $"权限不足，无法访问目录: {SourceRootDir}");
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    // 忽略
                }
            }

            int totalFilesToCheck = allFilesToCheck.Count;
            _logger.Information($"总计在源目录中找到 {totalFilesToCheck} 个图片文件等待检查。");
            _logger.Information("--- 开始处理文件 ---");

            // 3. 遍历文件并处理
            for (int i = 0; i < totalFilesToCheck; i++)
            {
                string filePath = allFilesToCheck[i];
                string filename = Path.GetFileName(filePath);

                // 实时预览：打印进度日志
                _logger.Information($"[{i + 1}/{totalFilesToCheck}] 正在检查: {filename}");
                
                // 【安全检查】检查文件是否在受保护目录中
                if (IsPathProtected(filePath))
                {
                    _logger.Warning($"🚫 跳过：文件 '{filePath}' 位于受保护目录中，禁止移动。");
                    skippedFiles++;
                    continue; // 跳过当前文件
                }

                // 提取评分
                int? score = ExtractScore(filename);
                
                if (score == null)
                {
                    _logger.Debug($"跳过：文件 '{filename}' 中未找到 '评分XX' 信息。");
                    skippedFiles++;
                    continue;
                }

                if (targetScores.Contains(score.Value))
                {
                    // 4. 执行文件移动
                    
                    string targetDirName = $"评分{score.Value:D2}"; 
                    string targetDir = Path.Combine(TargetBaseDir, targetDirName);
                    string targetFilePath = Path.Combine(targetDir, filename);

                    try
                    {
                        // 如果目标文件夹不存在，则创建它 (安全操作)
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                            _logger.Information($"✅ 新建目标目录: {targetDir}");
                        }

                        // 核心操作：移动文件
                        if (File.Exists(targetFilePath))
                        {
                            _logger.Warning($"目标文件已存在，跳过移动: '{targetFilePath}'");
                            skippedFiles++;
                        }
                        else
                        {
                            File.Move(filePath, targetFilePath);
                            successfulMoves++;
                            _logger.Information($"⭐ 成功移动: '{filename}' -> '{targetDirName}'");
                        }
                    }
                    catch (Exception e)
                    {
                        failedMoves++;
                        _logger.Error(e, $"❌ 移动文件失败 '{filePath}' 到 '{targetFilePath}'。");
                    }
                }
                else
                {
                    _logger.Debug($"跳过：评分 {score} 不在目标范围 {targetScores} 内。");
                    skippedFiles++;
                }
            }


            // 5. 总结和日志输出
            
            _logger.Information("--- 文件整理任务完成 ---");
            _logger.Information($"配置的评分范围/值: {scoreInput}");
            _logger.Information($"目标处理评分集合: {string.Join(", ", targetScores.OrderBy(s => s))}");
            _logger.Information("========================================");
            _logger.Information($"总计检查文件数: {totalFilesToCheck}");
            _logger.Information($"成功移动文件数: {successfulMoves}");
            _logger.Information($"失败移动文件数: {failedMoves}");
            _logger.Information($"跳过/不符合评分文件数: {skippedFiles}");
            _logger.Information("========================================");
            
            // 计数器逻辑校验
            if (successfulMoves + failedMoves + skippedFiles != totalFilesToCheck)
            {
                _logger.Warning("🚨 计数器逻辑校验失败，请检查程序是否有遗漏文件。");
            }
        }
    }

    // --- 程序入口 (Main 函数) ---
    class Program
    {
        // 提取日志配置方法
        private static void SetupLogging()
        {
            // 配置 Serilog 日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // 文件日志记录 Debug 级别
                // 控制台输出 (彩色)
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information, 
                                 outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                // 文件日志配置 (保留历史记录和报错追溯)
                .WriteTo.File(ScoreOrganizer.LogFile, 
                              rollingInterval: RollingInterval.Day, // 每天滚动
                              fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                              retainedFileCountLimit: 7, // 保留7个文件
                              outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                              encoding: Encoding.UTF8) // 确保中文日志不乱码
                .CreateLogger();
        }

        static void Main(string[] args)
        {
            // 1. 初始化静态 Logger (Serilog 约定)
            SetupLogging();
            
            var organizer = new ScoreOrganizer();
            
            // 安全：提示用户操作的文件路径
            Console.WriteLine("请确认以下路径是否正确：");
            Console.WriteLine($"   - 文件来源 (历史目录): {ScoreOrganizer.StaticSourceRootDir}");
            Console.WriteLine($"   - 目标目录 (评分XX父目录): {ScoreOrganizer.stringStaticTargetBaseDir}");
            
            Console.WriteLine("\n请在控制台输入您想要整理的评分范围（如 '80-99'）或单个评分（如 '80'）：");
            Console.Write("评分输入 (例如 80-99 或 80): ");
            
            // 读取用户输入
            string userInput = Console.ReadLine()?.Trim() ?? string.Empty;
            //string userInput = Console.ReadLine()?.Trim();
            
            if (!string.IsNullOrEmpty(userInput))
            {
                organizer.OrganizeFiles(userInput);
            }
            else
            {
                Log.Warning("用户未提供评分输入，程序退出。");
            }

            // 结束时自动打开 log 文件，方便检查结果 
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo()
                {
                    FileName = ScoreOrganizer.LogFile,
                    UseShellExecute = true 
                });
                Log.Information($"已自动打开日志文件: {ScoreOrganizer.LogFile}");
            }
            catch (Exception e)
            {
                Log.Error(e, "自动打开日志文件失败。");
            }
            
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
            
            // 确保 Serilog 缓冲区清空
            Log.CloseAndFlush(); 
        }
    }
}