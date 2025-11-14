// 文件名：ExcelReportGenerator.cs

using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using System.Diagnostics; // 新增：用于计时

// 注意：确保 ImageAnalyzerCore 命名空间与 Program.cs 保持一致
namespace ImageAnalyzerCore 
{
    /// <summary>
    /// 负责所有 Excel 报告的生成和写入操作。
    /// 设为静态类，方便直接调用，无需实例化。
    /// </summary>
    public static class ExcelReportGenerator
    {
        /// <summary>
        /// 实际的 Excel 报告创建函数，使用 ClosedXML 写入数据。
        /// </summary>
        /// <param name="imageData">包含待写入数据的图片信息列表。</param>
        /// <param name="path">Excel 文件的完整输出路径。</param>
        /// <returns>报告是否成功生成。</returns>
        public static bool CreateExcelReport(List<ImageInfo> imageData, string path)
        {
            Console.WriteLine($"[INFO] 正在使用 ClosedXML 库创建 Excel 报告: {path} (包含 {imageData.Count} 条数据)。");
            
            // 确保目录存在
            // 解决编译器警告：Path.GetDirectoryName(path) 可能返回 null。使用 ?? 确保非 null 引用。
            string? directoryPath = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directoryPath ?? string.Empty);

            // 【新增】开始计时
            Stopwatch stopwatch = Stopwatch.StartNew();
            
            try
            {
                // 1. 创建工作簿
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("图片信息报告");

                    // 2. 写入表头 (使用 AnalyzerConfig 定义的列名)
                    // 这是管理表头和格式的关键部分
                    worksheet.Cell("A1").Value = "序号";
                    worksheet.Cell("B1").Value = "文件名";
                    worksheet.Cell("C1").Value = "文件路径";
                    worksheet.Cell("D1").Value = "创建时间";
                    worksheet.Cell("E1").Value = "修改时间";
                    worksheet.Cell("F1").Value = "原始标签";
                    worksheet.Cell("G1").Value = AnalyzerConfig.CoreKeywordColumnName; // 提取正向词的核心词
                    worksheet.Cell("H1").Value = "文件状态";
                    
                    // 3. 写入数据行
                    for (int i = 0; i < imageData.Count; i++)
                    {
                        var info = imageData[i];
                        int row = i + 2; // 数据从第 2 行开始

                        worksheet.Cell(row, 1).Value = i + 1; // 序号
                        worksheet.Cell(row, 2).Value = info.FileName;
                        worksheet.Cell(row, 3).Value = info.FilePath;
                        worksheet.Cell(row, 4).Value = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 5).Value = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 6).Value = info.ExtractedTagsRaw;
                        worksheet.Cell(row, 7).Value = info.CoreKeywords;
                        worksheet.Cell(row, 8).Value = info.Status;
                    }

                    // 4. 格式化：自动调整列宽
                    worksheet.Columns().AdjustToContents();

                    // 5. 保存文件 (导出)
                    workbook.SaveAs(path);
                }

                // 【新增】停止计时并计算总耗时
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
                Console.WriteLine($"[FATAL ERROR] 实际的 Excel 报告生成失败。");
                Console.WriteLine($"[CHECK] 请【确保】您已通过 NuGet 安装了 'ClosedXML' 包。");
                Console.WriteLine($"错误详情: {ex.Message}");
                return false;
            }
        }
    }
}