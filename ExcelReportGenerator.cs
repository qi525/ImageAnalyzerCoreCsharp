// 文件名：ExcelReportGenerator.cs

using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using System.Diagnostics; // 用于计时
using System.Linq; // 用于简化字典操作

// 注意：确保 ImageAnalyzerCore 命名空间与 Program.cs 保持一致
namespace ImageAnalyzerCore 
{
    // 假设 AnalyzerConfig 是一个静态配置类，这里是为了使用它的常量
    // ⚠️ 实际项目中需要确保 AnalyzerConfig 和 ImageInfo 已被定义
    // public static class AnalyzerConfig { public const string CoreKeywordColumnName = "提取正向词的核心词"; }
    
    /// <summary>
    /// 负责所有 Excel 报告的生成和写入操作。
    /// 设为静态类，方便直接调用，无需实例化。
    /// </summary>
    public static class ExcelReportGenerator
    {
        // Excel报告中使用的固定列宽值。
        private const double FixedColumnWidth = 15.0; // 本地常量，不使用全局配置

        // 【重构点 1：更新列头顺序和名称】使用字典定义表头顺序和列宽
        private static readonly Dictionary<string, double> ColumnHeadersAndWidths = new Dictionary<string, double>
        {
            // 所有列宽值都使用 FixedColumnWidth (15.0)
            { "序号", FixedColumnWidth }, 
            { "文件名", FixedColumnWidth }, 
            { "文件所在文件夹", FixedColumnWidth }, 
            { "文件路径", FixedColumnWidth }, 
            { "创建时间", FixedColumnWidth }, 
            { "修改时间", FixedColumnWidth }, 
            { "正向词", FixedColumnWidth }, // 原始正向词
            { "正向词核心词提取", FixedColumnWidth }, // 【新增】功能9 要新增的列
            { "提取正向词的核心词", FixedColumnWidth }, // 保留旧列（兼容）
            { "文件状态", FixedColumnWidth }
        };

        /// <summary>
        /// 创建并写入包含图片分析数据的 Excel 报告。
        /// </summary>
        /// <param name="imageData">包含所有图片分析信息的列表。</param>
        /// <param name="path">Excel 报告的完整输出路径。</param>
        /// <returns>操作是否成功。</returns>
        public static bool CreateExcelReport(List<ImageInfo> imageData, string path)
        {
            if (imageData == null || !imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过报告生成。");
                return true; // 即使跳过也认为操作流程成功
            }

            // 【新增】开始计时
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine($"[INFO] 开始生成 Excel 报告到: {path}");

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("图片分析报告");
                    
                    // 1. 写入表头和设置列宽（这部分是数据结构，必须保留）
                    int headerCol = 1;
                    foreach (var header in ColumnHeadersAndWidths)
                    {
                        worksheet.Cell(1, headerCol).Value = header.Key;
                        worksheet.Column(headerCol).Width = header.Value; // 设置固定列宽 15.0
                        headerCol++;
                    }

                    // 2. 格式化表头 (已注释，以减少资源消耗)
                    // @@    60-64,60-64   @@ 注释表头格式化和网格线隐藏
                    /*
                    var headerRange = worksheet.Range(1, 1, 1, ColumnHeadersAndWidths.Count);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    worksheet.ShowGridLines = false;
                    */

                    // 3. 写入数据
                    int row = 1; // 从第二行开始写入数据
                    
                    foreach (var info in imageData)
                    {
                        row++;
                        worksheet.Cell(row, 1).Value = row - 1; // 序号
                        worksheet.Cell(row, 2).Value = info.FileName;
                        worksheet.Cell(row, 3).Value = info.DirectoryName; // 文件所在文件夹
                        worksheet.Cell(row, 4).Value = info.FilePath;
                        worksheet.Cell(row, 5).Value = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 6).Value = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 7).Value = info.ExtractedTagsRaw; // 正向词
                        worksheet.Cell(row, 8).Value = info.CoreKeywords; // 【新增列】正向词核心词提取（功能9）
                        worksheet.Cell(row, 9).Value = info.CoreKeywords; // 提取正向词的核心词（保留兼容列）
                        worksheet.Cell(row, 10).Value = info.Status; // 文件状态
                    }

                    // 4. 格式化：调整列以适应内容长度 (已注释，以减少资源消耗)
                    // @@    86-89,86-89   @@ 注释自动调整列宽
                    /*
                    worksheet.Column(7).AdjustToContents(); // 正向词列，通常较长
                    worksheet.Column(8).AdjustToContents(); // 核心词列
                    worksheet.Columns(1, 2).AdjustToContents(); // 序号、文件名
                    */

                    // 5. 保存文件 (导出)
                    workbook.SaveAs(path);
                }

                // 停止计时并计算总耗时
                stopwatch.Stop();
                TimeSpan ts = stopwatch.Elapsed;
                string elapsedTime = String.Format("{0:00}时 {1:00}分 {2:00}.{3:000}秒",
                    ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);


                Console.WriteLine($"[SUCCESS] Excel 报告已成功写入到: {path}");
                Console.WriteLine($"[TIME] Excel 报告生成总耗时: {elapsedTime}"); // 打印耗时
                return true;
            }
            catch (Exception ex)
            {
                // 捕获 ClosedXML 写入失败的情况
                Console.WriteLine($"[FATAL ERROR] 实际写入 Excel 文件失败。错误信息: {ex.Message}");
                return false;
            }
        }
    }
}