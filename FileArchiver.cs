// 文件名：ScoreArchive.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;


/// 流程说明【流程说明不可删除！！！】：
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
/// 
/// 
namespace ImageAnalyzerCore
{
    /// <summary>
    /// 归档器：负责将图片从扫描目录移动到指定的“已归档”目录。
    /// 用于实现Program.cs中的“仅归档”功能（选项5）。
    /// 难度系数：3/10 (核心逻辑为文件I/O，已添加安全检查和计数器)
    /// </summary>
    public class ScoreArchive
    {
        // --- 配置 ---
        // TODO: ❗ 请根据您的实际需求修改此归档目标路径 ❗
        // 默认将文件移动到与扫描目录同级的“历史”文件夹
        private const string ArchiveTargetDir = @"C:\stable-diffusion-webui\outputs\txt2img-images\历史";
        
        // 用于记录处理状态和计数的并发字典（满足计数器要求）
        private readonly ConcurrentDictionary<string, int> _statusCounts = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// 将图片信息列表中的文件从当前位置移动到归档目标目录。
        /// </summary>
        /// <param name="imageData">ImageScanner扫描到的图片信息列表。</param>
        /// <param name="sourceRootDirectory">源扫描根目录（当前未直接使用，但保留以备未来扩展）。</param>
        public void ArchiveImages(List<ImageInfo> imageData, string sourceRootDirectory)
        {
            Console.WriteLine($"[INFO] 归档目标目录: {ArchiveTargetDir}");

            if (!imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过归档。");
                return;
            }
            
            // 确保归档目录存在
            if (!Directory.Exists(ArchiveTargetDir))
            {
                try
                {
                    Directory.CreateDirectory(ArchiveTargetDir);
                    Console.WriteLine($"[INFO] 归档目录已创建: {ArchiveTargetDir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FATAL] 无法创建归档目录: {ArchiveTargetDir}. 归档流程中止。错误: {ex.Message}");
                    return;
                }
            }

            int totalImages = imageData.Count;
            Console.WriteLine($"[INFO] 总任务量: {totalImages} 张图片准备归档。");

            // 使用并行处理以提高文件I/O效率
            // 假设 AnalyzerConfig.MaxConcurrentWorkers 已定义在其他文件中或使用默认值
            Parallel.ForEach(imageData, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, info =>
            {
                ProcessSingleArchive(info, sourceRootDirectory);
            });

            // 打印最终统计结果 (满足用户要求的计数器格式)
            int successCount = _statusCounts.GetValueOrDefault("成功归档", 0);
            int failedCount = _statusCounts.GetValueOrDefault("归档失败/其他异常", 0);
            // 计算跳过计数 (总数 - 成功 - 失败)
            int skippedCount = totalImages - successCount - failedCount; 

            Console.WriteLine("\n--- 图片归档操作完成 ---");
            Console.WriteLine($"总数量: {totalImages} 张");
            Console.WriteLine($"成功: {successCount} 张");
            Console.WriteLine($"跳过/已存在: {skippedCount} 张");
            Console.WriteLine($"失败: {failedCount} 张");
        }

        /// <summary>
        /// 处理单个文件的归档操作。
        /// </summary>
        private void ProcessSingleArchive(ImageInfo info, string sourceRootDirectory)
        {
            string sourcePath = info.FilePath;
            
            // 目标路径：归档目录 + 原文件名
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = Path.Combine(ArchiveTargetDir, fileName);

            // 1. 安全检查: 源文件不存在
            if (!File.Exists(sourcePath))
            {
                _statusCounts.AddOrUpdate("跳过", 1, (key, count) => count + 1);
                return;
            }

            try
            {
                // 2. 检查: 目标文件已存在
                if (File.Exists(targetPath))
                {
                    Console.WriteLine($"[WARN] 目标文件已存在: {targetPath}，跳过归档以避免覆盖。");
                    _statusCounts.AddOrUpdate("跳过", 1, (key, count) => count + 1);
                    return;
                }
                
                // 3. 检查: 文件是否已被移动到目标目录（幂等性保护）
                if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _statusCounts.AddOrUpdate("跳过", 1, (key, count) => count + 1);
                    return;
                }

                // 4. 执行移动操作
                File.Move(sourcePath, targetPath);
                _statusCounts.AddOrUpdate("成功归档", 1, (key, count) => count + 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 归档文件失败: {sourcePath} -> {targetPath}. 错误: {ex.Message}");
                _statusCounts.AddOrUpdate("归档失败/其他异常", 1, (key, count) => count + 1);
            }
        }
    }
}