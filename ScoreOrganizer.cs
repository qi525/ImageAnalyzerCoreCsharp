// 文件名：ScoreOrganizer.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using static System.Console; // 简化 Console.WriteLine

namespace ImageAnalyzerCore // 确保与 Program.cs 命名空间一致
{
    // 简化日志输出类，替代 Serilog，与 Program.cs 保持一致
    public static class Log
    {
        public static void Info(string message) => WriteLine($"[INFO] {message}");
        public static void Warning(string message) => WriteLine($"[WARNING] {message}");
        public static void Error(string message) => WriteLine($"[ERROR] {message}");
        public static void Error(Exception ex, string message) => WriteLine($"[ERROR] {message} 错误: {ex.Message}");
    }

    /// <summary>
    /// ScoreOrganizer 类负责根据文件名中的评分信息（例如 "评分85"），
    /// 将符合用户指定评分范围的图片文件从源目录（SourceRootDir）移动到目标基础目录（TargetBaseDir）下的对应评分文件夹中（例如 "评分85"）。
    /// <para>核心功能包括：</para>
    /// <list type="bullet">
    /// <item>根据用户输入解析目标评分集合（ParseScoreInput）。</item>
    /// <item>扫描源目录下的所有图片文件，并在扫描时排除所有名为 ".bf" 的文件夹。</item>
    /// <item>通过 ExtractScore 方法提取文件名中的评分。</item>
    /// <item>检查文件是否位于受保护目录中，以确保操作安全。</item>
    /// <item>执行文件移动操作。**移动成功的详细日志将写入 score_organizer.log 文件。**</item>
    /// <item>提供详细的日志记录和计数总结。</item>
    /// </list>
    /// </summary>
    public class ScoreOrganizer
    {
        // --- 配置项 (与 Python 脚本保持一致) ---
        private const string SourceRootDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
        private const string TargetBaseDir = @"C:\stable-diffusion-webui\outputs\txt2img-images";
        private readonly Regex _scorePattern = new Regex(@"评分(\d{2})");
        
        // 【安全保护】受保护的目录列表 (禁止移动这些文件夹内部的文件)
        private readonly string[] ProtectedDirs = 
        {
            @"C:\stable-diffusion-webui\outputs\txt2img-images\历史\Important_Backups", // 绝对路径示例
            @"Needs_Review",                                                           // 相对于 SourceRootDir 的相对路径示例
        };
        
        public static readonly string StaticSourceRootDir = SourceRootDir;
        public static readonly string stringStaticTargetBaseDir = TargetBaseDir;
        public const string LogFile = "score_organizer.log"; // 文件日志名称

        public ScoreOrganizer()
        {
            // 在 Program.cs 中打印了配置信息，此处简化
        }

        // --- 核心工具函数 ---

        /// <summary>
        /// 使用正则表达式从文件名中提取两位数的评分。
        /// </summary>
        public int? ExtractScore(string filename)
        {
            var match = _scorePattern.Match(filename);
            if (match.Success && match.Groups.Count > 1)
            {
                if (int.TryParse(match.Groups[1].Value, out int score))
                {
                    return score;
                }
            }
            return null; // 保留 null 用于表示“未找到值”
        }

        /// <summary>
        /// 解析用户输入的评分范围或单个评分，返回需要处理的评分集合。
        /// </summary>
        /// <returns>一个包含所有有效评分（两位数）的集合，如果输入无效则返回空的 HashSet。</returns>
        public HashSet<int> ParseScoreInput(string scoreInput)
        {
            scoreInput = scoreInput.Trim();

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

                    if (!(10 <= startScore && startScore <= 99 && 10 <= endScore && endScore <= 99))
                    {
                         Log.Error($"输入的评分范围 '{scoreInput}' 必须是两位数 (10-99)。");
                         return new HashSet<int>();
                    }

                    if (startScore > endScore)
                    {
                        Log.Warning($"检测到评分范围'{scoreInput}'左边大于右边，已自动交换为 {endScore}-{startScore}。");
                        (startScore, endScore) = (endScore, startScore);
                    }
                    
                    var scoreSet = new HashSet<int>(Enumerable.Range(startScore, endScore - startScore + 1));
                    Log.Info($"已解析评分范围为: {startScore} 到 {endScore}，共 {scoreSet.Count} 个评分。");
                    return scoreSet;
                }
                catch (FormatException)
                {
                    Log.Error($"无法解析评分范围输入 '{scoreInput}'，请确保格式正确，例如 '80-99'。");
                    return new HashSet<int>();
                }
            }
            else
            {
                try
                {
                    if (!int.TryParse(scoreInput, out int singleScore))
                    {
                        throw new FormatException();
                    }

                    if (!(10 <= singleScore && singleScore <= 99))
                    {
                        Log.Error($"输入的单个评分 '{scoreInput}' 必须是一个两位数 (10-99)。");
                        return new HashSet<int>();
                    }
                    
                    Log.Info($"已解析为单个评分: {singleScore}。");
                    return new HashSet<int> { singleScore };
                }
                catch (FormatException)
                {
                    Log.Error($"无法解析评分输入 '{scoreInput}'，请确保输入的是数字或范围。");
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
        public void OrganizeFiles(string scoreInput)
        {
            
            // [新增逻辑] 任务开始前，清空或创建日志文件，确保日志内容是当前任务的。
            try
            {
                File.WriteAllText(LogFile, $"--- 文件整理任务开始：{DateTime.Now} ---\r\n");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"初始化日志文件失败: {LogFile}");
            }

            // 1. 安全检查和范围解析
            if (!Directory.Exists(SourceRootDir))
            {
                Log.Error($"源目录不存在或无法访问: {SourceRootDir}");
                return;
            }

            // 解析评分输入，获取目标评分集合
            var targetScores = ParseScoreInput(scoreInput);
            if (targetScores.Count == 0)
            {
                Log.Error("评分解析失败，程序终止。");
                return;
            }

            // --- 计数器初始化 ---
            int successfulMoves = 0;
            int failedMoves = 0;
            int skippedFiles = 0;
            
            Log.Info("--- 开始扫描文件 ---");
            
            // 2. 扫描文件并计数
            string[] searchPatterns = { "*.png", "*.jpg", "*.jpeg", "*.webp" };
            List<string> allFilesToCheck = new List<string>();

            foreach (var pattern in searchPatterns)
            { 
                try
                {
                    // 使用自定义函数递归获取文件列表，并排除 ".bf" 文件夹
                    allFilesToCheck.AddRange(EnumerateFilesExcludingDotBf(SourceRootDir, pattern));
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error(ex, $"权限不足，无法访问源目录: {SourceRootDir}");
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    // 忽略
                }
            }

            int totalFilesToCheck = allFilesToCheck.Count;
            Log.Info($"总计在源目录中找到 {totalFilesToCheck} 个图片文件等待检查。");
            Log.Info("--- 开始处理文件 ---");

            // 3. 遍历文件并处理
            for (int i = 0; i < totalFilesToCheck; i++)
            {
                string filePath = allFilesToCheck[i];
                string filename = Path.GetFileName(filePath);
                
                // 实时预览：已移除逐个文件的进度日志，过程日志全部移至 LogFile。
                
                // 【安全检查】检查文件是否在受保护目录中
                if (IsPathProtected(filePath))
                {
                    Log.Warning($"🚫 跳过：文件 '{filePath}' 位于受保护目录中，禁止移动。");
                    skippedFiles++;
                    continue; 
                }

                // 提取评分
                int? score = ExtractScore(filename);
                
                if (score == null)
                {
                    // 未找到评分的文件，跳过 (过程日志已移除)
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
                            Log.Info($"✅ 新建目标目录: {targetDir}");
                        }

                        // 核心操作：移动文件
                        if (File.Exists(targetFilePath))
                        {
                            Log.Warning($"目标文件已存在，跳过移动: '{targetFilePath}'");
                            skippedFiles++;
                        }
                        else
                        {
                            File.Move(filePath, targetFilePath);
                            AppendToLogFile($"⭐ 成功移动: '{filename}' -> '{targetDirName}'"); // 日志写入文件
                            successfulMoves++;
                        }
                    }
                    catch (Exception e)
                    {
                        failedMoves++;
                        Log.Error(e, $"❌ 移动文件失败 '{filePath}' 到 '{targetFilePath}'。");
                    }
                }
                else
                {
                    // 评分不在目标范围内的文件，跳过 (过程日志已移除)
                    skippedFiles++;
                }
            }


            // 5. 总结
            
            Log.Info("--- 文件整理任务完成 ---");
            Log.Info($"配置的评分范围/值: {scoreInput}");
            Log.Info($"目标处理评分集合: {string.Join(", ", targetScores.OrderBy(s => s))}");
            WriteLine("========================================");
            Log.Info($"总计检查文件数: {totalFilesToCheck}");
            Log.Info($"成功移动文件数: {successfulMoves}");
            Log.Info($"失败移动文件数: {failedMoves}");
            Log.Info($"跳过/不符合评分文件数: {skippedFiles}");
            WriteLine("========================================");
            
            if (successfulMoves + failedMoves + skippedFiles != totalFilesToCheck)
            {
                Log.Warning("🚨 计数器逻辑校验失败，请检查程序是否有遗漏文件。");
            }

            // 自动打开日志文件 (在 Program.cs 这种主程序中，通常不需要自动打开，这里保留 Python 原始需求)
            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = LogFile,
                    UseShellExecute = true 
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "自动打开日志文件失败。");
            }
        }

        /// <summary>
        /// 私有辅助方法，将成功移动的日志写入到日志文件中。
        /// </summary>
        private void AppendToLogFile(string message)
        {
            // 使用 File.AppendAllText 确保日志行被添加到文件末尾
            // 使用 \r\n 确保 Windows 平台的换行符正确
            File.AppendAllText(LogFile, message + "\r\n");
        }
        
        // --- 新增工具函数：用于排除特定文件夹的递归文件查找 ---

        /// <summary>
        /// 递归查找指定目录下的文件，并排除所有名为 ".bf" 的文件夹。
        /// </summary>
        private IEnumerable<string> EnumerateFilesExcludingDotBf(string rootPath, string searchPattern)
        {
            var fileList = new List<string>();

            // 1. 查找当前目录下的文件
            try
            {
                fileList.AddRange(Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.TopDirectoryOnly));
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(ex, $"权限不足，无法访问目录: {rootPath}");
            }

            // 2. 查找当前目录下的子目录并递归处理
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (dirName.Equals(".bf", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info($"跳过排除目录: {subDir}");
                        continue; // 排除 .bf 文件夹
                    }
                    fileList.AddRange(EnumerateFilesExcludingDotBf(subDir, searchPattern));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(ex, $"权限不足，无法访问目录结构: {rootPath}");
            }

            return fileList;
        }
    }
}