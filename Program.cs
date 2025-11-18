using System;
using System.Threading.Tasks;
using static System.Console;

namespace ImageAnalyzerCore
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            WriteLine("[INFO] 欢迎使用图片分析报告生成工具");
            WriteLine("-----------------------------------");
            
            while (true)
            {
                DisplayMenu();
                Write("请输入您的选择 (1-10): ");
                string choice = ReadLine()?.Trim() ?? string.Empty;

                try
                {
                    switch (choice)
                    {   
                        case "1": WorkflowManager.Feature1_ScanAndReport(); break;
                        case "2": WorkflowManager.Feature2_Archive(); break;
                        case "3": WorkflowManager.Feature3_Categorize(); break;
                        case "4": WorkflowManager.Feature4_ScoreOrganizer(); break;
                        case "5": WorkflowManager.Feature5_AutoTag(); break;
                        case "6": WorkflowManager.Feature6_AutoScoreAndTag(); break;
                        case "7": WorkflowManager.Feature7_FullFlow(); break;
                        case "8": WorkflowManager.Feature8_ExtractStyleWords(); break;
                        case "9": WorkflowManager.Feature9_ExtractCoreWords(); break;
                        case "10":
                            WriteLine("[INFO] 退出程序。");
                            return;
                        default:
                            WriteLine("[WARNING] 无效的选项，请重新输入。");
                            break;
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
            WriteLine("  1. 仅扫描生成表格（只读）");
            WriteLine("  2. 仅归档到历史文件夹（移动）"); 
            WriteLine("  3. 仅分类历史文件夹（移动）"); 
            WriteLine("  4. 仅移动历史评分到外层（移动）"); 
            WriteLine("  5. 仅自动添加 10 个 tag（重命名）");
            WriteLine("  6. 仅自动添加评分（重命名）");
            WriteLine("  7. 完整流程 (Scan -> TF-IDF -> Report -> Scoring)");
            WriteLine("  8. 提取风格词");
            WriteLine("  9. 提取纯核心词");
            WriteLine("  10. 退出程序");
        }
    }
}
