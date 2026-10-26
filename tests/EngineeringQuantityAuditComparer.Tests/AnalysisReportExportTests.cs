using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class AnalysisReportExportTests
{
    [Fact]
    public async Task Exports_companion_report_with_taxed_overview_and_untaxed_unit_project_item_groups()
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
            Assert.Equal(5, detail.SheetView.SplitRow);
            Assert.Equal(3, detail.SheetView.SplitColumn);
            int Row(string name) => detail.Column(2).CellsUsed()
                .Single(cell => cell.GetString() == name).Address.RowNumber;
            var support = Row("支架");
            var misspelled = Row("支架误写");
            var box = Row("接线盒");
            Assert.Contains("分部分项", detail.Cell("A3").GetString(), StringComparison.Ordinal);
            Assert.Contains("不含税合价", detail.Cell("A3").GetString(), StringComparison.Ordinal);
            Assert.Equal("项目名称", detail.Cell("B4").GetString());
            Assert.Equal("核减工程量", detail.Cell("D4").GetString());
            Assert.Equal("核增工程量", detail.Cell("F4").GetString());
            Assert.Equal("审减金额（不含税合价）", detail.Cell("H4").GetString());
            Assert.Equal("未识别楼栋：研发楼安装", detail.Cell("E5").GetString());
            Assert.Equal(2m, detail.Cell(support, 4).GetValue<decimal>());
            Assert.Equal(2m, detail.Cell(misspelled, 4).GetValue<decimal>());
            Assert.Equal(3m, detail.Cell(box, 6).GetValue<decimal>());
            Assert.Equal(20m, detail.Cell(support, 8).GetValue<decimal>());
            Assert.Equal(50m, detail.Cell(misspelled, 8).GetValue<decimal>());
            Assert.Equal(10m, detail.Cell(box, 10).GetValue<decimal>());
            Assert.Contains("支架误写", detail.Cell(support, 14).GetString(), StringComparison.Ordinal);
            Assert.Contains("支架", detail.Cell(misspelled, 14).GetString(), StringComparison.Ordinal);
            Assert.True(detail.Cell(box, 14).IsEmpty());
            Assert.Equal($"H{support}-J{support}", detail.Cell(support, 12).FormulaA1);
            var totalRow = detail.LastRowUsed()!.RowNumber();
            Assert.True(detail.Cell(totalRow, 4).IsEmpty());
            Assert.True(detail.Cell(totalRow, 6).IsEmpty());
            Assert.Equal(70m, detail.Cell(totalRow, 8).GetValue<decimal>());
            Assert.Equal(10m, detail.Cell(totalRow, 10).GetValue<decimal>());
            Assert.Equal(60m, detail.Cell(totalRow, 12).GetValue<decimal>());
            Assert.Contains(detail.ConditionalFormats,
                format => format.Operator == XLCFOperator.GreaterThan &&
                    format.Style.Font.FontColor == XLColor.FromHtml("#C00000"));
            Assert.Contains(detail.ConditionalFormats,
                format => format.Operator == XLCFOperator.GreaterThan &&
                    format.Style.Font.FontColor == XLColor.FromHtml("#006100"));
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
            var initialDetail = report.Worksheet("初审扣减项明细");
            var finalDetail = report.Worksheet("终审扣减项明细");
            var initialSupportRow = initialDetail.Column(2).CellsUsed()
                .Single(cell => cell.GetString() == "支架").Address.RowNumber;
            var finalSupportRow = finalDetail.Column(2).CellsUsed()
                .Single(cell => cell.GetString() == "支架").Address.RowNumber;
            Assert.Equal(2m, initialDetail.Cell(initialSupportRow, 4).GetValue<decimal>());
            Assert.Equal(3m, finalDetail.Cell(finalSupportRow, 4).GetValue<decimal>());
            Assert.Contains("送审－终审", finalDetail.Cell("A3").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Detail_merges_the_same_item_and_breaks_out_its_amounts_by_building()
    {
        static BillItem Pipe(decimal quantity, decimal total) =>
            new("1", "P01", "配管", "PVC", "m", quantity, quantity, total / quantity, total);
        static CostWorkbook Book(string path, decimal firstQuantity, decimal firstTotal,
            decimal secondQuantity, decimal secondTotal) =>
            new(path, "测试工程", [new("1#楼安装", firstTotal), new("2#楼安装", secondTotal)],
            [
                new("1#楼安装", UnitProjectKind.ContractPrice, [Pipe(firstQuantity, firstTotal)]),
                new("2#楼安装", UnitProjectKind.ContractPrice, [Pipe(secondQuantity, secondTotal)])
            ]);
        var session = new ComparisonEngine().Compare(new ComparisonRequest(
            Book("送审.xlsx", 5m, 100m, 7m, 140m),
            Book("初审.xlsx", 3m, 60m, 2m, 40m), null), ComparisonSettings.Default);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "分楼栋.xlsx");
            await new ReportExporter().ExportAsync(session, string.Empty, output, CancellationToken.None);
            using var report = new XLWorkbook(ReportExporter.GetAnalysisReportPath(output));
            var detail = report.Worksheet("扣减项明细");
            var rows = detail.Column(2).CellsUsed()
                .Where(cell => cell.GetString() == "配管")
                .Select(cell => cell.Address.RowNumber).ToList();
            var row = Assert.Single(rows);
            Assert.Equal("1#", detail.Cell("E5").GetString());
            Assert.Equal("2#", detail.Cell("F5").GetString());
            Assert.Equal(7m, detail.Cell(row, 4).GetValue<decimal>());
            Assert.Equal(2m, detail.Cell(row, 5).GetValue<decimal>());
            Assert.Equal(5m, detail.Cell(row, 6).GetValue<decimal>());
            Assert.Equal(140m, detail.Cell(row, 10).GetValue<decimal>());
            Assert.Equal(40m, detail.Cell(row, 11).GetValue<decimal>());
            Assert.Equal(100m, detail.Cell(row, 12).GetValue<decimal>());
            Assert.Equal(140m, detail.Cell(detail.LastRowUsed()!.RowNumber(), 16).GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Detail_allocates_reduced_and_increased_quantities_and_amounts_to_source_buildings()
    {
        static BillItem Item(string code, string name, int sourceRow, decimal quantity, decimal total) =>
            new("1", code, name, code, "m", quantity, quantity, total / quantity, total)
            {
                SourceRowIndex = sourceRow
            };
        static CostWorkbook Book(string path, decimal firstPipeQuantity, decimal firstPipeTotal,
            decimal secondPipeQuantity, decimal secondPipeTotal, decimal boxQuantity, decimal boxTotal) =>
            new(path, "测试工程", [new("气动系统", firstPipeTotal + secondPipeTotal + boxTotal)],
            [new UnitProject("气动系统", UnitProjectKind.ContractPrice,
                [
                    Item("P1", "配管", 10, firstPipeQuantity, firstPipeTotal),
                    Item("P2", "配管", 20, secondPipeQuantity, secondPipeTotal),
                    Item("B2", "接线盒", 21, boxQuantity, boxTotal)
                ])
            {
                Sections =
                [
                    new("1#研发楼压缩空气系统", 1, 9, firstPipeTotal, [10], false),
                    new("2#研发楼压缩空气系统", 1, 19, secondPipeTotal + boxTotal, [20, 21], false)
                ]
            }]);

        var session = new ComparisonEngine().Compare(new ComparisonRequest(
            Book("送审.xlsx", 5m, 50m, 3m, 30m, 1m, 5m),
            Book("初审.xlsx", 3m, 30m, 2m, 20m, 4m, 20m), null), ComparisonSettings.Default);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "按楼栋.xlsx");
            await new ReportExporter().ExportAsync(session, string.Empty, output, CancellationToken.None);
            using var report = new XLWorkbook(ReportExporter.GetAnalysisReportPath(output));
            var detail = report.Worksheet("扣减项明细");
            Assert.Equal("核减工程量", detail.Cell("D4").GetString());
            Assert.Equal("合计核减工程量", detail.Cell("D5").GetString());
            Assert.Equal("1#", detail.Cell("E5").GetString());
            Assert.Equal("2#", detail.Cell("F5").GetString());
            Assert.Equal("核增工程量", detail.Cell("G4").GetString());
            Assert.Equal("审减金额（不含税合价）", detail.Cell("J4").GetString());
            Assert.Equal("审增金额（不含税合价）", detail.Cell("M4").GetString());

            var pipe = detail.Column(2).CellsUsed().Single(x => x.GetString() == "配管").Address.RowNumber;
            var box = detail.Column(2).CellsUsed().Single(x => x.GetString() == "接线盒").Address.RowNumber;
            Assert.Equal(3m, detail.Cell(pipe, 4).GetValue<decimal>());
            Assert.Equal(2m, detail.Cell(pipe, 5).GetValue<decimal>());
            Assert.Equal(1m, detail.Cell(pipe, 6).GetValue<decimal>());
            Assert.Equal(30m, detail.Cell(pipe, 10).GetValue<decimal>());
            Assert.Equal(20m, detail.Cell(pipe, 11).GetValue<decimal>());
            Assert.Equal(10m, detail.Cell(pipe, 12).GetValue<decimal>());
            Assert.Equal(3m, detail.Cell(box, 7).GetValue<decimal>());
            Assert.Equal(3m, detail.Cell(box, 9).GetValue<decimal>());
            Assert.Equal(15m, detail.Cell(box, 13).GetValue<decimal>());
            Assert.Equal(15m, detail.Cell(box, 15).GetValue<decimal>());
            Assert.Equal($"SUM(E{pipe}:F{pipe})", detail.Cell(pipe, 4).FormulaA1);
            Assert.Equal($"SUM(K{pipe}:L{pipe})", detail.Cell(pipe, 10).FormulaA1);
            Assert.Equal(30m, detail.Cell(detail.LastRowUsed()!.RowNumber(), 10).GetValue<decimal>());
            Assert.Equal(15m, detail.Cell(detail.LastRowUsed()!.RowNumber(), 13).GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Detail_keeps_zero_price_items_when_quantity_changes()
    {
        static CostWorkbook Book(string path, decimal quantity) =>
            new(path, "测试工程", [new("1#楼安装", 0m)],
            [new("1#楼安装", UnitProjectKind.ContractPrice,
                [new BillItem("1", "Z1", "零价支架", "", "个", quantity, quantity, 0m, 0m)])]);
        var session = new ComparisonEngine().Compare(new ComparisonRequest(
            Book("送审.xlsx", 5m), Book("初审.xlsx", 3m), null), ComparisonSettings.Default);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "零价项.xlsx");
            await new ReportExporter().ExportAsync(session, string.Empty, output, CancellationToken.None);
            using var report = new XLWorkbook(ReportExporter.GetAnalysisReportPath(output));
            var detail = report.Worksheet("扣减项明细");
            var itemRow = detail.Column(2).CellsUsed()
                .Single(cell => cell.GetString() == "零价支架").Address.RowNumber;
            Assert.Equal("1#", detail.Cell("E5").GetString());
            Assert.Equal(2m, detail.Cell(itemRow, 4).GetValue<decimal>());
            Assert.Equal(2m, detail.Cell(itemRow, 5).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(itemRow, 8).GetValue<decimal>());
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
            Assert.Equal(0m, detail.Cell(totalRow, 12).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(6, 13).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(7, 13).GetValue<decimal>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ComparisonSession CreateSession(bool includeFinal)
    {
        static BillItem Item(string code, string name, string feature, string unit, decimal quantity, decimal total) =>
            new("1", code, name, feature, unit, quantity, quantity, total, total);
        static CostWorkbook Book(string path, decimal taxedTotal, params BillItem[] items) =>
            new(path, "测试工程", [new("研发楼安装", taxedTotal)],
                [new("研发楼安装", UnitProjectKind.NewPrice, items)]);

        var submission = Book("送审.xlsx", 1000m,
            Item("", "支架", "规格A", "m", 5m, 100m),
            Item("X", "支架误写", "规格A", "m", 2m, 50m),
            Item("B", "接线盒", "", "个", 1m, 10m));
        var initial = Book("初审.xlsx", 900m,
            Item("", "支架", "规格A", "m", 3m, 80m),
            Item("B", "接线盒", "", "个", 4m, 20m));
        var final = includeFinal ? Book("终审.xlsx", 850m,
            Item("", "支架", "规格A", "m", 2m, 70m),
            Item("B", "接线盒", "", "个", 4m, 20m)) : null;
        return new ComparisonEngine().Compare(new ComparisonRequest(submission, initial, final),
            ComparisonSettings.Default);
    }
}
