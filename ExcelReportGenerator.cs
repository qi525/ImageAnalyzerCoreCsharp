using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace ImageAnalyzerCore 
{
    public static class ExcelReportGenerator
    {
        private const double FixedColumnWidth = 15.0;

        private static readonly Dictionary<string, double> ColumnHeadersAndWidths = new()
        {
            { "序号", FixedColumnWidth }, 
            { "文件名", FixedColumnWidth }, 
            { "文件所在文件夹", FixedColumnWidth }, 
            { "文件路径", FixedColumnWidth }, 
            { "创建时间", FixedColumnWidth }, 
            { "修改时间", FixedColumnWidth }, 
            { "正向词", FixedColumnWidth },
            { "正向词核心词提取", FixedColumnWidth },
            { "提取正向词的核心词", FixedColumnWidth },
            { "文件状态", FixedColumnWidth }
        };

        public static bool CreateExcelReport(List<ImageInfo> imageData, string path)
        {
            if (imageData == null || !imageData.Any())
            {
                Console.WriteLine("[WARN] 图片数据列表为空，跳过报告生成。");
                return true;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine($"[INFO] 开始生成 Excel 报告到: {path}");

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("图片分析报告");
                    
                    int headerCol = 1;
                    foreach (var header in ColumnHeadersAndWidths)
                    {
                        worksheet.Cell(1, headerCol).Value = header.Key;
                        worksheet.Column(headerCol).Width = header.Value;
                        headerCol++;
                    }

                    for (int row = 2; row <= imageData.Count + 1; row++)
                    {
                        var info = imageData[row - 2];
                        worksheet.Cell(row, 1).Value = row - 1;
                        worksheet.Cell(row, 2).Value = info.FileName;
                        worksheet.Cell(row, 3).Value = info.DirectoryName;
                        worksheet.Cell(row, 4).Value = info.FilePath;
                        worksheet.Cell(row, 5).Value = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 6).Value = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                        worksheet.Cell(row, 7).Value = info.ExtractedTagsRaw;
                        worksheet.Cell(row, 8).Value = info.CoreKeywords;
                        worksheet.Cell(row, 9).Value = info.CoreKeywords;
                        worksheet.Cell(row, 10).Value = info.Status;
                    }

                    workbook.SaveAs(path);
                }

                stopwatch.Stop();
                TimeSpan ts = stopwatch.Elapsed;
                string elapsedTime = String.Format("{0:00}时 {1:00}分 {2:00}.{3:000}秒",
                    ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                Console.WriteLine($"[SUCCESS] Excel 报告已成功写入到: {path}");
                Console.WriteLine($"[TIME] Excel 报告生成总耗时: {elapsedTime}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL ERROR] 实际写入 Excel 文件失败。错误信息: {ex.Message}");
                return false;
            }
        }
    }
}