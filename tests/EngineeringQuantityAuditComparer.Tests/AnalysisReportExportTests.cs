using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class AnalysisReportExportTests
{
    [Fact]
    public async Task Exports_companion_report_with_taxed_overview_and_untaxed_name_unit_groups()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "对比结果.xlsx");
            await new ReportExporter().ExportAsync(CreateSession(includeFinal: false), string.Empty, output,
                CancellationToken.None);

            Assert.True(File.Exists(output));
            using (var comparison = new XLWorkbook(output))
            {
                var sheet = comparison.Worksheet("研发楼安装");
                var supportRow = sheet.Column(3).CellsUsed()
                    .Single(cell => cell.GetString() == "支架").Address.RowNumber;
                var statusColumn = sheet.Row(3).CellsUsed()
                    .Single(cell => cell.GetString() == "匹配状态").Address.ColumnNumber;
                Assert.Equal("名称特征单位匹配（编码缺失）",
                    sheet.Cell(supportRow, statusColumn).GetString());
            }
            var reportPath = Path.Combine(directory, "对比结果-审减分析报告.xlsx");
            using var report = new XLWorkbook(reportPath);
            Assert.Equal(["审减概况", "扣减项明细"], report.Worksheets.Select(x => x.Name));

            var overview = report.Worksheet("审减概况");
            Assert.Equal(1000m, overview.Cell("C8").GetValue<decimal>());
            Assert.Equal(900m, overview.Cell("D8").GetValue<decimal>());
            Assert.Equal("C8-D8", overview.Cell("E8").FormulaA1);
            Assert.Equal("C9-D9", overview.Cell("E9").FormulaA1);
            Assert.Equal(100m, overview.Cell("E9").GetValue<decimal>());
            Assert.True(overview.ConditionalFormats.Count() >= 2);

            var detail = report.Worksheet("扣减项明细");
            int Row(string name) => detail.Column(2).CellsUsed()
                .Single(cell => cell.GetString() == name).Address.RowNumber;
            var support = Row("支架");
            var misspelled = Row("支架误写");
            var box = Row("接线盒");
            Assert.Equal(20m, detail.Cell(support, 4).GetValue<decimal>());
            Assert.Equal(50m, detail.Cell(misspelled, 4).GetValue<decimal>());
            Assert.Equal(10m, detail.Cell(box, 5).GetValue<decimal>());
            Assert.Contains("支架误写", detail.Cell(support, 8).GetString(), StringComparison.Ordinal);
            Assert.Contains("支架", detail.Cell(misspelled, 8).GetString(), StringComparison.Ordinal);
            Assert.True(detail.Cell(box, 8).IsEmpty());
            Assert.Equal($"D{support}-E{support}", detail.Cell(support, 6).FormulaA1);
            var totalRow = detail.LastRowUsed()!.RowNumber();
            Assert.Equal(70m, detail.Cell(totalRow, 4).GetValue<decimal>());
            Assert.Equal(10m, detail.Cell(totalRow, 5).GetValue<decimal>());
            Assert.Equal(60m, detail.Cell(totalRow, 6).GetValue<decimal>());
            Assert.True(detail.ConditionalFormats.Count() >= 2);
            var positive = Assert.Single(detail.ConditionalFormats,
                format => format.Operator == XLCFOperator.GreaterThan);
            var negative = Assert.Single(detail.ConditionalFormats,
                format => format.Operator == XLCFOperator.LessThan);
            Assert.Equal(XLColor.FromHtml("#C00000"), positive.Style.Font.FontColor);
            Assert.Equal(XLColor.FromHtml("#006100"), negative.Style.Font.FontColor);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Three_stage_report_has_two_stage_pairs_without_an_initial_final_report()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "三阶段.xlsx");
            await new ReportExporter().ExportAsync(CreateSession(includeFinal: true), string.Empty, output,
                CancellationToken.None);
            using var report = new XLWorkbook(Path.Combine(directory, "三阶段-审减分析报告.xlsx"));
            Assert.Equal(
                ["初审概况", "初审扣减项明细", "终审概况", "终审扣减项明细"],
                report.Worksheets.Select(x => x.Name));
            Assert.Equal(850m, report.Worksheet("终审概况").Cell("D8").GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Final_only_report_uses_two_generic_sheet_names_with_final_review_values()
    {
        var threeStage = CreateSession(includeFinal: true);
        var finalOnly = new ComparisonEngine().Compare(new ComparisonRequest(
            threeStage.Request.Submission, null, threeStage.Request.FinalReview), ComparisonSettings.Default);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "终审对比.xlsx");
            await new ReportExporter().ExportAsync(finalOnly, string.Empty, output, CancellationToken.None);
            using var report = new XLWorkbook(ReportExporter.GetAnalysisReportPath(output));
            Assert.Equal(["审减概况", "扣减项明细"], report.Worksheets.Select(x => x.Name));
            Assert.Equal(850m, report.Worksheet("审减概况").Cell("D8").GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Existing_companion_report_prevents_either_output_from_being_created()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "对比结果.xlsx");
            var reportPath = Path.Combine(directory, "对比结果-审减分析报告.xlsx");
            File.WriteAllText(reportPath, "保留原文件");

            await Assert.ThrowsAsync<IOException>(() => new ReportExporter().ExportAsync(
                CreateSession(includeFinal: false), string.Empty, output, CancellationToken.None));

            Assert.False(File.Exists(output));
            Assert.Equal("保留原文件", File.ReadAllText(reportPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Existing_main_workbook_is_preserved_and_no_companion_is_written()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "对比结果.xlsx");
            File.WriteAllText(output, "保留原文件");

            await Assert.ThrowsAsync<IOException>(() => new ReportExporter().ExportAsync(
                CreateSession(includeFinal: false), string.Empty, output, CancellationToken.None));

            Assert.Equal("保留原文件", File.ReadAllText(output));
            Assert.False(File.Exists(ReportExporter.GetAnalysisReportPath(output)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Zero_net_and_zero_submitted_tax_amounts_have_safe_rates()
    {
        static BillItem Item(string code, string name, decimal total) =>
            new("1", code, name, "特征", "项", 1m, 1m, total, total);
        static CostWorkbook Book(string path, BillItem first, BillItem second) =>
            new(path, "项目", [new("研发楼", 0m)],
                [new("研发楼", UnitProjectKind.ContractPrice, [first, second])]);
        var session = new ComparisonEngine().Compare(new ComparisonRequest(
            Book("送审.xlsx", Item("A", "清单甲", 20m), Item("B", "清单乙", 0m)),
            Book("初审.xlsx", Item("A", "清单甲", 0m), Item("B", "清单乙", 20m)), null),
            ComparisonSettings.Default);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "零基数.xlsx");
            await new ReportExporter().ExportAsync(session, string.Empty, output, CancellationToken.None);
            using var report = new XLWorkbook(ReportExporter.GetAnalysisReportPath(output));
            var overview = report.Worksheet("审减概况");
            Assert.Equal(0m, overview.Cell("F8").GetValue<decimal>());
            var detail = report.Worksheet("扣减项明细");
            var totalRow = detail.LastRowUsed()!.RowNumber();
            Assert.Equal(0m, detail.Cell(totalRow, 6).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(5, 7).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(6, 7).GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ComparisonSession CreateSession(bool includeFinal)
    {
        static BillItem Item(string code, string name, string feature, string unit, decimal total) =>
            new("1", code, name, feature, unit, 1m, 1m, total, total);
        static CostWorkbook Book(string path, decimal taxedTotal, params BillItem[] items) =>
            new(path, "测试工程", [new("研发楼安装", taxedTotal)],
                [new("研发楼安装", UnitProjectKind.NewPrice, items)]);

        var submission = Book("送审.xlsx", 1000m,
            Item("", "支架", "规格A", "m", 100m),
            Item("X", "支架误写", "规格A", "m", 50m),
            Item("B", "接线盒", "", "个", 10m));
        var initial = Book("初审.xlsx", 900m,
            Item("", "支架", "规格A", "m", 80m),
            Item("B", "接线盒", "", "个", 20m));
        var final = includeFinal ? Book("终审.xlsx", 850m,
            Item("", "支架", "规格A", "m", 70m),
            Item("B", "接线盒", "", "个", 20m)) : null;
        return new ComparisonEngine().Compare(new ComparisonRequest(submission, initial, final),
            ComparisonSettings.Default);
    }
}
