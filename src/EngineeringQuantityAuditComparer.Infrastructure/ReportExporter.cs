using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

public sealed class ReportExporter : IReportExporter
{
    public static string GetAnalysisReportPath(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        return Path.Combine(
            Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("导出路径无效。"),
            Path.GetFileNameWithoutExtension(fullPath) + "-审减分析报告.xlsx");
    }

    public Task ExportAsync(ComparisonSession session, string templatePath, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("请选择导出位置。", nameof(outputPath));
        }

        return Task.Run(() => Export(session, templatePath, outputPath, cancellationToken), cancellationToken);
    }

    private static void Export(ComparisonSession session, string templatePath, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? throw new InvalidOperationException("导出路径无效。");
        var analysisPath = GetAnalysisReportPath(fullOutputPath);
        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(fullOutputPath) || Directory.Exists(fullOutputPath) ||
            File.Exists(analysisPath) || Directory.Exists(analysisPath))
        {
            throw new IOException("对比表或审减分析报告的目标路径已存在。为保护原始数据，程序不会覆盖现有文件。");
        }

        var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileNameWithoutExtension(fullOutputPath)}.{Guid.NewGuid():N}.tmp.xlsx");
        var temporaryAnalysisPath = Path.Combine(outputDirectory, $".{Path.GetFileNameWithoutExtension(fullOutputPath)}.analysis.{Guid.NewGuid():N}.tmp.xlsx");
        var createdMain = false;
        var createdAnalysis = false;
        try
        {
            using (var workbook = CreateWorkbook(templatePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = PrepareSummaryWorksheet(workbook);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "汇总表",
                    "绝对差额 Top 20看板",
                    "同类清单审增审减汇总看板"
                };
                var unitSheetTargets = new Dictionary<UnitComparison, string>(ReferenceEqualityComparer.Instance);
                var detailTargets = new Dictionary<ComparisonRow, DetailTarget>(ReferenceEqualityComparer.Instance);
                foreach (var unit in session.UnitComparisons)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sheetName = UniqueSheetName(unit.Name, usedNames);
                    var worksheet = workbook.Worksheets.Add(sheetName);
                    unitSheetTargets.Add(unit, sheetName);
                    DetailSheetBuilder.Build(worksheet, unit, session.Request, detailTargets);
                }

                SummarySheetBuilder.Build(summary, session, unitSheetTargets);

                var topTwenty = workbook.Worksheets.Add("绝对差额 Top 20看板");
                AbsoluteDifferenceTop20SheetBuilder.Build(topTwenty, session, detailTargets);
                var similarItems = workbook.Worksheets.Add("同类清单审增审减汇总看板");
                SimilarItemAmountDashboardSheetBuilder.Build(similarItems, session);

                topTwenty.Position = 1;
                similarItems.Position = 2;
                summary.Position = 3;
                workbook.SaveAs(temporaryPath, validate: true, evaluateFormulae: false);
            }

            SetCalculationProperties(temporaryPath);
            ValidateOutput(temporaryPath, session.UnitComparisons.Count + 3);
            using (var analysis = new XLWorkbook())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalysisReportBuilder.Build(analysis, session);
                analysis.SaveAs(temporaryAnalysisPath, validate: true, evaluateFormulae: false);
            }
            SetCalculationProperties(temporaryAnalysisPath);
            ValidateOutput(temporaryAnalysisPath, ComparisonPairs.OutputFor(session.Request).Count * 2);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullOutputPath, overwrite: false);
            createdMain = true;
            File.Move(temporaryAnalysisPath, analysisPath, overwrite: false);
            createdAnalysis = true;
        }
        catch
        {
            if (createdAnalysis)
            {
                File.Delete(analysisPath);
            }
            if (createdMain)
            {
                File.Delete(fullOutputPath);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            if (File.Exists(temporaryAnalysisPath))
            {
                File.Delete(temporaryAnalysisPath);
            }
        }
    }

    private static XLWorkbook CreateWorkbook(string templatePath)
    {
        if (File.Exists(templatePath))
        {
            var workbook = new XLWorkbook(templatePath);
            var first = workbook.Worksheets.First();
            foreach (var worksheet in workbook.Worksheets.Skip(1).ToList())
            {
                worksheet.Delete();
            }

            first.Clear(XLClearOptions.All);
            first.Name = "汇总表";
            return workbook;
        }

        var fallback = new XLWorkbook();
        fallback.Worksheets.Add("汇总表");
        return fallback;
    }

    private static IXLWorksheet PrepareSummaryWorksheet(XLWorkbook workbook)
    {
        var worksheet = workbook.Worksheets.First();
        worksheet.Name = "汇总表";
        worksheet.Clear(XLClearOptions.All);
        return worksheet;
    }

    private static string UniqueSheetName(string proposed, ISet<string> used)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['[', ']', ':', '*', '?', '/', '\\']).ToHashSet();
        var cleaned = new string(proposed.Select(character => invalid.Contains(character) ? ' ' : character).ToArray())
            .Trim()
            .Trim('\'');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "单位工程";
        }

        cleaned = cleaned.Length > 31 ? cleaned[..31] : cleaned;
        var candidate = cleaned;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var ending = $"-{suffix++}";
            candidate = cleaned[..Math.Min(cleaned.Length, 31 - ending.Length)] + ending;
        }

        return candidate;
    }

    private static void SetCalculationProperties(string path)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart?.Workbook ?? throw new InvalidDataException("导出工作簿结构无效。");
        var calculation = workbook.CalculationProperties ??= new CalculationProperties();
        calculation.CalculationMode = CalculateModeValues.Auto;
        calculation.ForceFullCalculation = true;
        calculation.FullCalculationOnLoad = true;
        calculation.CalculationId = 0U;
        workbook.Save();
    }

    private static void ValidateOutput(string path, int expectedSheetCount)
    {
        using var workbook = new XLWorkbook(path);
        if (workbook.Worksheets.Count != expectedSheetCount)
        {
            throw new InvalidDataException("导出工作簿的工作表数量校验失败。");
        }

        var invalidFormula = workbook.Worksheets
            .SelectMany(x => x.CellsUsed(cell => cell.HasFormula))
            .FirstOrDefault(cell => cell.FormulaA1.Contains("#REF!", StringComparison.OrdinalIgnoreCase));
        if (invalidFormula is not null)
        {
            throw new InvalidDataException($"导出公式存在无效引用：{invalidFormula.Worksheet.Name}!{invalidFormula.Address}");
        }
    }
}
