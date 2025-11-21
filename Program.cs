using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static System.Console;

namespace ImageAnalyzerCore
{
    class Program
    {
        private static readonly Dictionary<string, (string Description, Action Action)> MenuItems = new()
        {
            { "1", ("仅扫描生成表格（只读）", WorkflowManager.Feature1_ScanAndReport) },
            { "2", ("仅归档到历史文件夹（移动）", WorkflowManager.Feature2_Archive) },
            { "3", ("仅分类历史文件夹（移动）", WorkflowManager.Feature3_Categorize) },
            { "4", ("仅移动历史评分到外层（移动）", WorkflowManager.Feature4_ScoreOrganizer) },
            { "5", ("仅自动添加 10 个 tag（重命名）", WorkflowManager.Feature5_AutoTag) },
            { "6", ("仅自动添加评分（重命名）", WorkflowManager.Feature6_AutoScoreAndTag) },
            { "7", ("完整流程 (Scan -> TF-IDF -> Report -> Scoring)", WorkflowManager.Feature7_FullFlow) },
            { "8", ("获取风格词前缀（只读）", WorkflowManager.Feature8_ExtractStyleWords) },
        };

        public static async Task Main(string[] args)
        {
            WriteLine("[INFO] 欢迎使用图片分析报告生成工具");
            WriteLine("-----------------------------------");
            
            // 如果提供了命令行参数，直接执行对应功能
            if (args.Length > 0)
            {
                string choice = args[0].TrimStart('-');
                try
                {
                    if (MenuItems.TryGetValue(choice, out var item))
                    {
                        item.Action();
                    }
                    else
                    {
                        WriteLine($"[WARNING] 无效的选项: {choice}");
                    }
                }
                catch (Exception ex)
                {
                    WriteLine($"[FATAL] 错误: {ex.Message}");
                }
                return;
            }
            
            // 否则进入交互菜单模式
            while (true)
            {
                DisplayMenu();
                Write("请输入您的选择 (1-10): ");
                string choice = ReadLine()?.Trim() ?? string.Empty;

                try
                {
                    if (choice == "10")
                    {
                        WriteLine("[INFO] 退出程序。");
                        return;
                    }

                    if (MenuItems.TryGetValue(choice, out var item))
                    {
                        item.Action();
                    }
                    else
                    {
                        WriteLine("[WARNING] 无效的选项，请重新输入。");
                        continue;
                    }
                    
                    WriteLine("\n-----------------------------------\n");
                }
                catch (Exception ex)
                {
                    WriteLine($"[FATAL] 错误: {ex.Message}");
                }
            }
        }

        private static void DisplayMenu()
        {
            WriteLine("请选择您要执行的操作：");
            foreach (var item in MenuItems)
            {
                WriteLine($"  {item.Key}. {item.Value.Description}");
            }
            WriteLine("  10. 退出程序");
        }
    }
}
