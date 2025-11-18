using System;
using System.Collections.Generic;
using Spectre.Console;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 进度条助手：使用 Spectre.Console Progress API 显示类似 tqdm 的进度条。
    /// 包括：百分比、数量、速度、已耗时、剩余时间、预计总时间。
    /// </summary>
    public static class ProgressBarHelper
    {
        /// <summary>
        /// 运行扫描进度显示（带详细列信息）
        /// </summary>
        public static void RunScanProgress(Action<ProgressContext> scanAction)
        {
            AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),              // 任务描述
                    new ProgressBarColumn(),                  // 进度条
                    new PercentageColumn(),                   // 百分比 [100%]
                    new RemainingTimeColumn(),                // 剩余时间 [ETA]
                    new ElapsedTimeColumn(),                  // 已耗时 [Elapsed]
                    new SpinnerColumn(),                      // 旋转指示器
                })
                .Start(scanAction);
        }

        /// <summary>
        /// 运行核心词提取进度显示（带详细列信息）
        /// </summary>
        public static void RunCoreWordsProgress(Action<ProgressContext> coreAction)
        {
            AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),              // 任务描述
                    new ProgressBarColumn(),                  // 进度条
                    new PercentageColumn(),                   // 百分比 [100%]
                    new RemainingTimeColumn(),                // 剩余时间 [ETA]
                    new ElapsedTimeColumn(),                  // 已耗时 [Elapsed]
                    new SpinnerColumn(),                      // 旋转指示器
                })
                .Start(coreAction);
        }

        /// <summary>
        /// 生成详细的进度信息行（用于自定义显示）
        /// 格式: 已处理/总数 | 百分比 | 已耗时 | 剩余时间 | 总耗时 | 速度
        /// </summary>
        public static string GenerateDetailedProgressInfo(int current, int total, TimeSpan elapsed, TimeSpan? remaining = null)
        {
            if (total <= 0) return string.Empty;

            double percentage = (double)current / total * 100;
            double speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
            
            TimeSpan eta = remaining ?? (speed > 0 
                ? TimeSpan.FromSeconds((total - current) / speed) 
                : TimeSpan.Zero);
            
            TimeSpan totalTime = elapsed + eta;

            return $"[cyan]{current:N0}[/] / [yellow]{total:N0}[/] | " +
                   $"[green]{percentage:F1}%[/] | " +
                   $"耗时: [cyan]{elapsed:hh\\:mm\\:ss}[/] | " +
                   $"剩余: [yellow]{eta:hh\\:mm\\:ss}[/] | " +
                   $"总计: [magenta]{totalTime:hh\\:mm\\:ss}[/] | " +
                   $"速度: [blue]{speed:F1}[/] 文件/秒";
        }
    }
}

