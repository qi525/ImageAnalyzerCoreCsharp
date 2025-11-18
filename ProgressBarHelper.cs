using System;
using System.Collections.Generic;
using Spectre.Console;

namespace ImageAnalyzerCore
{
    /// <summary>
    /// 基于 Spectre.Console 的进度条助手。
    /// 提供类似 Python tqdm 的进度条显示，包括百分比、速度、耗时、ETA 等。
    /// </summary>
    public static class ProgressBarHelper
    {
        /// <summary>
        /// 扫描图片文件并实时显示进度。
        /// </summary>
        public static void RunScanProgress(Action<ProgressContext> scanAction)
        {
            AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),      // 任务描述
                    new ProgressBarColumn(),          // 进度条
                    new PercentageColumn(),           // 百分比
                    new RemainingTimeColumn(),        // 剩余时间 (ETA)
                    new SpinnerColumn(),              // 旋转图标
                    new ElapsedTimeColumn(),          // 已耗时
                })
                .Start(scanAction);
        }

        /// <summary>
        /// 核心词提取进度显示。
        /// </summary>
        public static void RunCoreWordsProgress(Action<ProgressContext> coreAction)
        {
            AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),      // 任务描述
                    new ProgressBarColumn(),          // 进度条
                    new PercentageColumn(),           // 百分比
                    new RemainingTimeColumn(),        // 剩余时间 (ETA)
                    new SpinnerColumn(),              // 旋转图标
                    new ElapsedTimeColumn(),          // 已耗时
                })
                .Start(coreAction);
        }
    }
}
