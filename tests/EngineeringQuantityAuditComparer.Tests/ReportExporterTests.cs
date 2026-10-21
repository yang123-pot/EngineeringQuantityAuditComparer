using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class ReportExporterTests
{
    [Fact]
    public async Task Exports_summary_and_details_with_live_formulas_numeric_zeros_and_conditional_formats()
    {
        var session = CreateSession();
        var output = Path.Combine(Path.GetTempPath(), $"审减分析-{Guid.NewGuid():N}.xlsx");
        try
        {
            await new ReportExporter().ExportAsync(session, Template(), output, CancellationToken.None);

            using var workbook = new XLWorkbook(output);
            Assert.Equal(
                ["绝对差额 Top 20看板", "同类清单审增审减汇总看板", "汇总表"],
                workbook.Worksheets.Take(3).Select(x => x.Name));
            Assert.DoesNotContain(workbook.Worksheets, x => x.Name == "分析看板");
            Assert.Equal(session.UnitComparisons.Count + 3, workbook.Worksheets.Count);
            var summary = workbook.Worksheet("汇总表");
            Assert.Equal(1000m, summary.Cell("C4").GetValue<decimal>());
            Assert.Equal(800m, summary.Cell("D4").GetValue<decimal>());
            Assert.Equal("项目造价差（送审-初审）", summary.Cell("E3").GetString());
            Assert.Equal("C4-D4", summary.Cell("E4").FormulaA1);
            Assert.True(summary.ConditionalFormats.Count() >= 2);

            var detail = workbook.Worksheets.Single(x => x.Name.Contains("研发楼", StringComparison.Ordinal));
            Assert.Equal("9", detail.Cell("A5").GetString());
            Assert.Equal(0m, detail.Cell("J5").GetValue<decimal>());
            Assert.Equal(0m, detail.Cell("K5").GetValue<decimal>());
            Assert.Equal(0m, detail.Cell("L5").GetValue<decimal>());
            Assert.Equal("G5-J5", detail.Cell("M5").FormulaA1);
            Assert.Equal("H5-K5", detail.Cell("N5").FormulaA1);
            Assert.Contains("IF(I5=0", detail.Cell("P5").FormulaA1, StringComparison.Ordinal);

            Assert.Equal("ADD", detail.Cell("B6").GetString());
            Assert.True(detail.Cell("A6").IsEmpty());
            Assert.Equal("I6-L6", detail.Cell("O6").FormulaA1);
            Assert.Equal("IF(I6=0,IF(L6=0,0,SIGN(O6)),O6/ABS(I6))", detail.Cell("P6").FormulaA1);
            Assert.Equal("小计", detail.Cell("C7").GetString());
            Assert.Equal(17, detail.LastColumnUsed()!.ColumnNumber());
            Assert.True(detail.ConditionalFormats.Count() >= 8);
            using var document = SpreadsheetDocument.Open(output, false);
            var calculation = document.WorkbookPart!.Workbook.CalculationProperties!;
            Assert.Equal(DocumentFormat.OpenXml.Spreadsheet.CalculateModeValues.Auto, calculation.CalculationMode?.Value);
            Assert.True(calculation.ForceFullCalculation?.Value);
            Assert.True(calculation.FullCalculationOnLoad?.Value);
            var differentialFormats = document.WorkbookPart.WorkbookStylesPart!.Stylesheet.DifferentialFormats!
                .Elements<DocumentFormat.OpenXml.Spreadsheet.DifferentialFormat>()
                .ToList();
            Assert.Contains(differentialFormats, format =>
                format.Font?.Color?.Rgb?.Value == "FFC00000" &&
                format.Fill?.PatternFill?.BackgroundColor?.Rgb?.Value == "FFF4CCCC");
            Assert.Contains(differentialFormats, format =>
                format.Font?.Color?.Rgb?.Value == "FF006100" &&
                format.Fill?.PatternFill?.BackgroundColor?.Rgb?.Value == "FFE2F0D9");
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public void Sign_conditional_formats_map_positive_to_red_and_negative_to_green()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("动态颜色");

        WorksheetFormatting.AddSignConditionalFormats(worksheet.Range("A1:A2"));

        var formats = worksheet.ConditionalFormats.ToList();
        var positive = Assert.Single(formats, x => x.Operator == XLCFOperator.GreaterThan);
        var negative = Assert.Single(formats, x => x.Operator == XLCFOperator.LessThan);
        Assert.Equal(XLColor.FromHtml("#C00000"), positive.Style.Font.FontColor);
        Assert.Equal(XLColor.FromHtml("#F4CCCC"), positive.Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#006100"), negative.Style.Font.FontColor);
        Assert.Equal(XLColor.FromHtml("#E2F0D9"), negative.Style.Fill.BackgroundColor);
    }

    [Fact]
    public async Task Export_leaves_item_comment_blank_when_all_total_difference_rates_are_zero()
    {
        static BillItem Item(string? sequence) =>
            new(sequence, "A01", "配电箱", "测试特征", "台", 0m, 0m, 0m, 0m)
            {
                SourceRowIndex = 5
            };

        static UnitProject Unit(BillItem item) =>
            new("研发楼安装", UnitProjectKind.ContractPrice, [item])
            {
                SourceSheetIndex = 1,
                SourceSheetName = "来源页"
            };

        static CostWorkbook Book(string path, UnitProject unit) =>
            new(path, "项目", [new TaxInclusiveSummaryItem(unit.Name, 0m)], [unit]);

        var session = new ComparisonEngine().Compare(
            new ComparisonRequest(Book("送审.xlsx", Unit(Item("1"))), Book("初审.xlsx", Unit(Item(null))), null),
            ComparisonSettings.Default);
        var output = TemporaryXlsxPath();
        try
        {
            await Export(session, output);

            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");
            var itemRow = detail.Column(2).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == "A01").Address.RowNumber;
            var commentColumn = detail.Row(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == "批注").Address.ColumnNumber;

            Assert.True(detail.Cell(itemRow, commentColumn).IsEmpty());
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Three_stage_export_writes_two_new_dashboards_in_final_order()
    {
        var session = CreateExportSession(true, true, UnitProjectKind.ContractPrice);
        var output = TemporaryXlsxPath();
        try
        {
            await Export(session, output);

            using var workbook = new XLWorkbook(output);
            Assert.Equal(
                ["绝对差额 Top 20看板", "同类清单审增审减汇总看板", "汇总表"],
                workbook.Worksheets.Take(3).Select(x => x.Name));
            Assert.Equal(session.UnitComparisons.Count + 3, workbook.Worksheets.Count);
            Assert.DoesNotContain(workbook.Worksheets, x => x.Name == "分析看板");
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Top_twenty_export_has_two_submission_based_scopes_and_live_detail_links()
    {
        var session = CreateExportSession(true, true, UnitProjectKind.ContractPrice);
        var output = TemporaryXlsxPath();
        try
        {
            await Export(session, output);

            using var workbook = new XLWorkbook(output);
            var dashboard = workbook.Worksheet("绝对差额 Top 20看板");
            Assert.Equal(
            [
                "初审阶段（送审－初审）",
                "终审结算（送审－终审）"
            ], dashboard.Column(1).CellsUsed().Skip(3).Select(x => x.GetString()).Distinct());

            var detail = workbook.Worksheet("研发楼安装");
            var linkedSequences = dashboard.Column(4).CellsUsed().Where(x => x.HasHyperlink).ToList();
            Assert.NotEmpty(linkedSequences);
            foreach (var sequenceCell in linkedSequences)
            {
                var itemName = dashboard.Cell(sequenceCell.Address.RowNumber, 5).GetString();
                var detailRow = detail.Column(3).CellsUsed()
                    .Single(x => x.GetString() == itemName).Address.RowNumber;
                Assert.Equal($"'研发楼安装'!A{detailRow}", sequenceCell.GetHyperlink().InternalAddress);
            }
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Similar_item_export_has_two_amount_groups_without_quantity_columns()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateCrossUnitSummarySession(), output);

            using var workbook = new XLWorkbook(output);
            var dashboard = workbook.Worksheet("同类清单审增审减汇总看板");
            Assert.Equal(
                ["初审阶段（送审－初审）", "终审结算（送审－终审）"],
                dashboard.Row(3).CellsUsed()
                    .Select(x => x.GetString())
                    .Where(x => x.Contains("（", StringComparison.Ordinal)));
            Assert.Equal(
            [
                "序号", "项目名称", "项目特征描述", "计量单位", "初审审减金额", "初审审增金额",
                "终审结算审减金额", "终审结算审增金额", "备注"
            ], dashboard.Range("A4:I4").Cells().Select(x => x.GetString()));
            Assert.DoesNotContain(dashboard.CellsUsed(), x =>
                x.GetString().Contains("工程量", StringComparison.Ordinal) ||
                x.GetString().Contains("阶段合价", StringComparison.Ordinal) ||
                x.GetString().Contains("金额净变化", StringComparison.Ordinal));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Export_omits_old_dashboard_unit_comparison_and_difference_statistics()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateExportSession(true, true, UnitProjectKind.ContractPrice), output);

            using var workbook = new XLWorkbook(output);
            var text = workbook.Worksheets
                .SelectMany(x => x.CellsUsed(XLCellsUsedOptions.Contents))
                .Select(x => x.GetString())
                .ToList();
            Assert.DoesNotContain(workbook.Worksheets, x => x.Name == "分析看板");
            Assert.DoesNotContain("工程量审减分析看板", text);
            Assert.DoesNotContain("单位工程含税总造价对比", text);
            Assert.DoesNotContain(text, x => x.Contains("差异类型统计", StringComparison.Ordinal));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Export_reopens_with_resolved_summary_and_detail_hyperlinks()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateExportSession(true, true, UnitProjectKind.ContractPrice), output);

            using var reopened = new XLWorkbook(output);
            var detail = reopened.Worksheet("研发楼安装");
            Assert.Equal("'研发楼安装'!A1", reopened.Worksheet("汇总表").Cell("B4")
                .GetHyperlink().InternalAddress);

            var dashboard = reopened.Worksheet("绝对差额 Top 20看板");
            var sequenceCell = dashboard.Column(4).CellsUsed().First(x => x.HasHyperlink);
            var itemName = dashboard.Cell(sequenceCell.Address.RowNumber, 5).GetString();
            var detailRow = detail.Column(3).CellsUsed()
                .Single(x => x.GetString() == itemName).Address.RowNumber;
            Assert.Equal($"'研发楼安装'!A{detailRow}", sequenceCell.GetHyperlink().InternalAddress);
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Theory]
    [InlineData("'研发楼", "研发楼")]
    [InlineData("研发楼'", "研发楼")]
    public async Task Export_normalizes_boundary_apostrophes_and_reopens_links_to_the_final_sheet_name(
        string unitName,
        string expectedSheetName)
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateSheetNameExportSession(unitName), output);

            using var reopened = new XLWorkbook(output);
            var detail = reopened.Worksheet(expectedSheetName);
            var expectedPrefix = $"'{expectedSheetName.Replace("'", "''", StringComparison.Ordinal)}'!A";
            Assert.Equal(expectedPrefix + "1", reopened.Worksheet("汇总表").Cell("B4")
                .GetHyperlink().InternalAddress);

            var dashboard = reopened.Worksheet("绝对差额 Top 20看板");
            var sequenceCell = dashboard.Column(4).CellsUsed().Single(x => x.HasHyperlink);
            var itemName = dashboard.Cell(sequenceCell.Address.RowNumber, 5).GetString();
            var detailRow = detail.Column(3).CellsUsed()
                .Single(x => x.GetString() == itemName).Address.RowNumber;
            Assert.Equal(expectedPrefix + detailRow, sequenceCell.GetHyperlink().InternalAddress);
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Export_keeps_legal_inner_apostrophes_and_allocates_unique_names_after_sanitizing()
    {
        string[] unitNames = ["O'Brien", "[]:*?/\\", "汇总表", "研发/楼", "研发:楼"];
        string[] expectedSheetNames = ["O'Brien", "单位工程", "汇总表-2", "研发 楼", "研发 楼-2"];
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateSheetNameExportSession(unitNames), output);

            using var reopened = new XLWorkbook(output);
            var summary = reopened.Worksheet("汇总表");
            var dashboard = reopened.Worksheet("绝对差额 Top 20看板");
            for (var index = 0; index < unitNames.Length; index++)
            {
                var sheetName = expectedSheetNames[index];
                var escapedSheetName = sheetName.Replace("'", "''", StringComparison.Ordinal);
                var detail = reopened.Worksheet(sheetName);
                var summaryCell = summary.Column(2).CellsUsed()
                    .Single(x => x.GetString() == unitNames[index]);
                Assert.Equal($"'{escapedSheetName}'!A1", summaryCell.GetHyperlink().InternalAddress);

                var sequenceCell = dashboard.Column(3).CellsUsed()
                    .Single(x => x.GetString() == unitNames[index])
                    .WorksheetRow().Cell(4);
                var itemName = sequenceCell.WorksheetRow().Cell(5).GetString();
                var detailRow = detail.Column(3).CellsUsed()
                    .Single(x => x.GetString() == itemName).Address.RowNumber;
                Assert.Equal($"'{escapedSheetName}'!A{detailRow}", sequenceCell.GetHyperlink().InternalAddress);
            }
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Final_only_contract_detail_has_one_four_column_difference_group()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateExportSession(false, true, UnitProjectKind.ContractPrice), output);
            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");
            var headers = detail.Range("A3:Q4").Cells().Select(x => x.GetString()).ToList();

            Assert.Contains("送审", headers);
            Assert.Contains("终审", headers);
            Assert.Contains("差异（送审-终审）", headers);
            Assert.DoesNotContain("初审", headers);
            Assert.Equal(["工程量差", "单价差", "合价差", "合价差异率"],
                detail.Range("M4:P4").Cells().Select(x => x.GetString()));
            Assert.Equal(17, detail.LastColumnUsed()!.ColumnNumber());
            var addedRow = detail.Column(2).CellsUsed().Single(x => x.GetString() == "NEW").Address.RowNumber;
            Assert.True(detail.Cell(addedRow, 1).IsEmpty());
            Assert.Equal(0m, detail.Cell(addedRow, 7).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(addedRow, 8).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(addedRow, 9).GetValue<decimal>());
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Three_stage_contract_detail_uses_two_submission_based_groups_and_live_formulas()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateExportSession(true, true, UnitProjectKind.ContractPrice), output);
            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");

            Assert.Equal(24, detail.LastColumnUsed()!.ColumnNumber());
            Assert.Equal("批注", detail.Cell("X3").GetString());
            Assert.Equal("G5-J5", detail.Cell("P5").FormulaA1);
            Assert.Equal("H5-K5", detail.Cell("Q5").FormulaA1);
            Assert.Equal("I5-L5", detail.Cell("R5").FormulaA1);
            Assert.Equal("IF(I5=0,IF(L5=0,0,SIGN(R5)),R5/ABS(I5))", detail.Cell("S5").FormulaA1);
            Assert.Equal("G5-M5", detail.Cell("T5").FormulaA1);
            Assert.Equal("H5-N5", detail.Cell("U5").FormulaA1);
            Assert.Equal("I5-O5", detail.Cell("V5").FormulaA1);
            Assert.Equal("IF(I5=0,IF(O5=0,0,SIGN(V5)),V5/ABS(I5))", detail.Cell("W5").FormulaA1);
            Assert.DoesNotContain(detail.Row(4).CellsUsed().Select(x => x.GetString()), x => x == "差异率");
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Three_stage_detail_preserves_subtitle_totals_and_writes_expanded_summary_above_outlines()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateSectionedExportSession(), output);
            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");
            int Row(string name) => detail.Column(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == name).Address.RowNumber;

            var rootRow = Row("整个工程");
            var electricalRow = Row("电气");
            var itemRow = Row("A01");
            var finalOnlyRow = Row("终审新增专业");
            var addedItemRow = Row("NEW");

            Assert.Equal(["整个工程", "电气", "A01", "终审新增专业", "NEW"],
                detail.Range(rootRow, 3, addedItemRow, 3).Cells().Select(x => x.GetString()));
            Assert.Equal(XLOutlineSummaryVLocation.Top, detail.Outline.SummaryVLocation);
            Assert.Equal(0, detail.Row(rootRow).OutlineLevel);
            Assert.Equal(1, detail.Row(electricalRow).OutlineLevel);
            Assert.Equal(2, detail.Row(itemRow).OutlineLevel);
            Assert.Equal(1, detail.Row(finalOnlyRow).OutlineLevel);
            Assert.Equal(2, detail.Row(addedItemRow).OutlineLevel);
            Assert.All(detail.Rows(rootRow, addedItemRow), row => Assert.False(row.IsHidden));

            Assert.Equal(200m, detail.Cell(rootRow, 9).GetValue<decimal>());
            Assert.Equal(180m, detail.Cell(rootRow, 12).GetValue<decimal>());
            Assert.Equal(200m, detail.Cell(rootRow, 15).GetValue<decimal>());
            Assert.Equal(200m, detail.Cell(electricalRow, 9).GetValue<decimal>());
            Assert.Equal(180m, detail.Cell(electricalRow, 12).GetValue<decimal>());
            Assert.Equal(150m, detail.Cell(electricalRow, 15).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(finalOnlyRow, 9).GetValue<decimal>());
            Assert.Equal(0m, detail.Cell(finalOnlyRow, 12).GetValue<decimal>());
            Assert.Equal(50m, detail.Cell(finalOnlyRow, 15).GetValue<decimal>());

            foreach (var column in new[] { 1, 2, 4, 5, 6, 7, 8, 10, 11, 13, 14, 16, 17, 20, 21, 24 })
            {
                Assert.True(detail.Cell(electricalRow, column).IsEmpty());
            }
            Assert.Equal($"I{electricalRow}-L{electricalRow}", detail.Cell(electricalRow, 18).FormulaA1);
            Assert.Equal($"IF(I{electricalRow}=0,IF(L{electricalRow}=0,0,SIGN(R{electricalRow})),R{electricalRow}/ABS(I{electricalRow}))",
                detail.Cell(electricalRow, 19).FormulaA1);
            Assert.Equal($"I{electricalRow}-O{electricalRow}", detail.Cell(electricalRow, 22).FormulaA1);
            Assert.Equal($"IF(I{electricalRow}=0,IF(O{electricalRow}=0,0,SIGN(V{electricalRow})),V{electricalRow}/ABS(I{electricalRow}))",
                detail.Cell(electricalRow, 23).FormulaA1);
            Assert.DoesNotContain(detail.CellsUsed(XLCellsUsedOptions.Contents),
                x => x.GetString() is "分部小计" or "小计");
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Empty_stage_only_subtitle_writes_one_title_row_without_a_fake_child()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateSectionedExportSession(includeEmptySection: true), output);
            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");
            var emptyRow = detail.Column(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == "空标题").Address.RowNumber;
            var lastContentRow = detail.CellsUsed(XLCellsUsedOptions.Contents)
                .Max(x => x.Address.RowNumber);

            Assert.Equal(lastContentRow, emptyRow);
            Assert.Equal(1, detail.Row(emptyRow).OutlineLevel);
            Assert.True(detail.Row(emptyRow + 1).IsEmpty());
            Assert.Equal(0, detail.Row(emptyRow + 1).OutlineLevel);
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task New_price_subtitle_leaves_comment_and_match_status_blank()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateSectionedExportSession(kind: UnitProjectKind.NewPrice), output);
            using var workbook = new XLWorkbook(output);
            var detail = workbook.Worksheet("研发楼安装");
            var titleRow = detail.Column(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == "电气").Address.RowNumber;

            var commentColumn = detail.Row(3).CellsUsed().Single(x => x.GetString() == "批注").Address.ColumnNumber;
            var statusColumn = detail.Row(3).CellsUsed().Single(x => x.GetString() == "匹配状态").Address.ColumnNumber;
            Assert.True(detail.Cell(titleRow, commentColumn).IsEmpty());
            Assert.True(detail.Cell(titleRow, statusColumn).IsEmpty());
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public async Task Three_stage_summary_has_three_amounts_two_submission_based_pairs_and_a_note()
    {
        var output = TemporaryXlsxPath();
        try
        {
            await Export(CreateExportSession(true, true, UnitProjectKind.ContractPrice), output);
            using var workbook = new XLWorkbook(output);
            var summary = workbook.Worksheet("汇总表");

            Assert.Equal(
                ["序号", "单位工程名称", "送审含税总造价（元）", "初审含税总造价（元）", "终审含税总造价（元）",
                    "项目造价差（送审-初审）", "审减率", "项目造价差（送审-终审）", "审减率", "备注"],
                summary.Range("A3:J3").Cells().Select(x => x.GetString()));
            Assert.Equal("C4-D4", summary.Cell("F4").FormulaA1);
            Assert.Equal("C4-E4", summary.Cell("H4").FormulaA1);
            var totalRow = summary.LastRowUsed()!.RowNumber();
            Assert.Contains($"F{totalRow}/ABS(C{totalRow})", summary.Cell(totalRow, 7).FormulaA1, StringComparison.Ordinal);
            Assert.DoesNotContain("SUM(", summary.Cell(totalRow, 7).FormulaA1, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public void Summary_builder_links_unit_names_by_unit_reference_and_leaves_total_plain()
    {
        var session = CreateCrossUnitSummarySession();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("汇总表");
        var targets = new Dictionary<UnitComparison, string>(ReferenceEqualityComparer.Instance)
        {
            [session.UnitComparisons[0]] = "1#楼（安装）",
            [session.UnitComparisons[1]] = "O'Brien-2"
        };

        SummarySheetBuilder.Build(worksheet, session, targets);

        Assert.Equal("'1#楼（安装）'!A1", worksheet.Cell("B4").GetHyperlink().InternalAddress);
        Assert.Equal("'O''Brien-2'!A1", worksheet.Cell("B5").GetHyperlink().InternalAddress);
        var totalRow = worksheet.LastRowUsed()!.RowNumber();
        Assert.Equal("合计", worksheet.Cell(totalRow, 2).GetString());
        Assert.False(worksheet.Cell(totalRow, 2).HasHyperlink);
    }

    [Fact]
    public async Task Summary_marks_each_kind_of_audit_only_unit_with_numeric_missing_amounts()
    {
        static UnitProject U(string name, int index) => new(name, UnitProjectKind.ContractPrice, [])
        {
            SourceSheetIndex = index,
            SourceSheetName = $"S{index}"
        };
        static CostWorkbook B(string path, (UnitProject Unit, decimal Amount)[] values) =>
            new(path, "项目", values.Select(x => new TaxInclusiveSummaryItem(x.Unit.Name, x.Amount)).ToList(),
                values.Select(x => x.Unit).ToList());

        var submission = B("送审.xlsx", [(U("锚点甲", 1), 100m), (U("锚点乙", 2), 200m)]);
        var initial = B("初审.xlsx", [(U("锚点甲", 1), 90m), (U("初审独有", 2), 10m),
            (U("初终共有", 3), 15m), (U("锚点乙", 4), 190m)]);
        var final = B("终审.xlsx", [(U("锚点甲", 1), 80m), (U("初终共有", 2), 12m),
            (U("终审独有", 3), 20m), (U("锚点乙", 4), 180m)]);
        var session = new ComparisonEngine().Compare(
            new ComparisonRequest(submission, initial, final),
            ComparisonSettings.Default);
        var output = TemporaryXlsxPath();
        try
        {
            await Export(session, output);
            using var workbook = new XLWorkbook(output);
            var summary = workbook.Worksheet("汇总表");
            int Row(string name) => summary.Column(2).CellsUsed().Single(x => x.GetString() == name).Address.RowNumber;

            var initialRow = Row("初审独有");
            Assert.True(summary.Cell(initialRow, 1).IsEmpty());
            Assert.Equal(0m, summary.Cell(initialRow, 3).GetValue<decimal>());
            Assert.Equal($"C{initialRow}-D{initialRow}", summary.Cell(initialRow, 6).FormulaA1);
            Assert.Equal(
                $"IF(C{initialRow}=0,IF(D{initialRow}=0,0,SIGN(F{initialRow})),F{initialRow}/ABS(C{initialRow}))",
                summary.Cell(initialRow, 7).FormulaA1);
            Assert.Equal("初审新增", summary.Cell(initialRow, 10).GetString());
            Assert.Equal("初审新增、终审新增", summary.Cell(Row("初终共有"), 10).GetString());
            Assert.Equal("终审新增", summary.Cell(Row("终审独有"), 10).GetString());
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [Fact]
    public void Top_twenty_builder_writes_one_long_table_for_two_submission_based_scopes()
    {
        var session = CreateExportSession(true, true, UnitProjectKind.ContractPrice);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("绝对差额 Top 20看板");
        var targets = session.UnitComparisons
            .SelectMany(unit => unit.Rows.Select((row, index) => (row, target: new DetailTarget(unit.Name, index + 5))))
            .ToDictionary(x => x.row, x => x.target);

        AbsoluteDifferenceTop20SheetBuilder.Build(worksheet, session, targets);

        Assert.Equal(
        [
            "审核口径", "排名", "单位工程", "清单序号", "项目名称",
            "送审不含税合价", "初审不含税合价", "终审结算不含税合价",
            "审增审减额", "绝对差额", "类型"
        ], worksheet.Range("A3:K3").Cells().Select(x => x.GetString()));
        Assert.Equal(
        [
            "初审阶段（送审－初审）",
            "终审结算（送审－终审）"
        ], worksheet.Column(1).CellsUsed().Skip(3).Select(x => x.GetString()).Distinct());
        var differenceColumn = 9;
        var dataRows = worksheet.RowsUsed().Where(x => x.RowNumber() >= 4 && x.Cell(differenceColumn).DataType == XLDataType.Number);
        Assert.All(dataRows, row => Assert.Equal(
            row.Cell(differenceColumn).GetValue<decimal>() > 0m ? "审减" : "审增",
            row.Cell(differenceColumn + 2).GetString()));
        Assert.All(worksheet.Column(4).CellsUsed().Skip(1), cell => Assert.True(cell.HasHyperlink));
        Assert.DoesNotContain(worksheet.CellsUsed(), cell =>
            cell.GetString().Contains("前阶段", StringComparison.Ordinal) ||
            cell.GetString().Contains("后阶段", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, "初审不含税合价", "终审结算不含税合价")]
    [InlineData(false, true, "终审结算不含税合价", "初审不含税合价")]
    public void Top_twenty_builder_omits_unselected_stage_columns(
        bool hasInitial,
        bool hasFinal,
        string expectedHeader,
        string forbiddenHeader)
    {
        var session = CreateExportSession(hasInitial, hasFinal, UnitProjectKind.ContractPrice);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("绝对差额 Top 20看板");
        AbsoluteDifferenceTop20SheetBuilder.Build(
            worksheet,
            session,
            new Dictionary<ComparisonRow, DetailTarget>());

        var headers = worksheet.Row(3).CellsUsed().Select(x => x.GetString()).ToList();
        Assert.Contains(expectedHeader, headers);
        Assert.DoesNotContain(forbiddenHeader, headers);
    }

    [Fact]
    public void Top_twenty_builder_takes_twenty_globally_and_excludes_zero_differences()
    {
        var session = CreateTopTwentyLimitSession();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("绝对差额 Top 20看板");
        AbsoluteDifferenceTop20SheetBuilder.Build(
            worksheet,
            session,
            new Dictionary<ComparisonRow, DetailTarget>());

        var dataRows = worksheet.RowsUsed().Where(x => x.RowNumber() >= 4).ToList();
        Assert.Equal(20, dataRows.Count);
        Assert.Equal(Enumerable.Range(1, 20), dataRows.Select(x => x.Cell(2).GetValue<int>()));
        Assert.All(dataRows, x => Assert.NotEqual(0m, x.Cell(8).GetValue<decimal>()));
    }

    [Fact]
    public void Top_twenty_builder_writes_empty_scope_message_and_dashboard_structure()
    {
        var session = CreateUnchangedInitialReviewSession();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("绝对差额 Top 20看板");

        AbsoluteDifferenceTop20SheetBuilder.Build(
            worksheet,
            session,
            new Dictionary<ComparisonRow, DetailTarget>());

        Assert.Equal("初审阶段（送审－初审）", worksheet.Cell("A4").GetString());
        Assert.Equal("无审增审减清单", worksheet.Cell("E4").GetString());
        Assert.False(worksheet.ShowGridLines);
        Assert.Equal(3, worksheet.SheetView.SplitRow);
        Assert.Equal(3, worksheet.AutoFilter.Range?.FirstRow().RowNumber());
        Assert.Equal(10, worksheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.ColumnNumber);
    }

    [Fact]
    public void Similar_item_amount_builder_uses_reduction_then_negative_increase_with_requested_colors()
    {
        var session = CreateCrossUnitSummarySession();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("同类清单审增审减汇总看板");

        SimilarItemAmountDashboardSheetBuilder.Build(worksheet, session);

        Assert.Equal("初审阶段（送审－初审）", worksheet.Cell("E3").GetString());
        Assert.Equal("终审结算（送审－终审）", worksheet.Cell("G3").GetString());
        Assert.Equal(
        [
            "序号", "项目名称", "项目特征描述", "计量单位", "初审审减金额", "初审审增金额",
            "终审结算审减金额", "终审结算审增金额", "备注"
        ], worksheet.Range("A4:I4").Cells().Select(x => x.GetString()));
        Assert.Equal(20m, worksheet.Cell("E5").GetValue<decimal>());
        Assert.Equal(-20m, worksheet.Cell("F5").GetValue<decimal>());
        Assert.Equal(40m, worksheet.Cell("G5").GetValue<decimal>());
        Assert.Equal(-40m, worksheet.Cell("H5").GetValue<decimal>());
        Assert.Contains("初审阶段：", worksheet.Cell("I5").GetString(), StringComparison.Ordinal);
        Assert.Contains("终审结算：", worksheet.Cell("I5").GetString(), StringComparison.Ordinal);
        Assert.Equal("SUM(E5:E5)", worksheet.Cell("E6").FormulaA1);
        Assert.Equal("SUM(F5:F5)", worksheet.Cell("F6").FormulaA1);
        Assert.Equal("SUM(G5:G5)", worksheet.Cell("G6").FormulaA1);
        Assert.Equal("SUM(H5:H5)", worksheet.Cell("H6").FormulaA1);
        Assert.Contains(worksheet.ConditionalFormats, format =>
            format.Range.RangeAddress.ToString() == "E5:H6" && format.Operator == XLCFOperator.GreaterThan);
        Assert.Contains(worksheet.ConditionalFormats, format =>
            format.Range.RangeAddress.ToString() == "E5:H6" && format.Operator == XLCFOperator.LessThan);
        Assert.Equal("#,##0.00;-#,##0.00;0.00", worksheet.Cell("F5").Style.NumberFormat.Format);
        Assert.False(worksheet.ShowGridLines);
        Assert.Equal(4, worksheet.SheetView.SplitRow);
        Assert.Equal(4, worksheet.AutoFilter.Range?.FirstRow().RowNumber());
        Assert.Equal(9, worksheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.ColumnNumber);
        Assert.DoesNotContain(worksheet.CellsUsed(), cell =>
            cell.GetString().Contains("涉及单位工程数", StringComparison.Ordinal) ||
            cell.GetString().Contains("工程量", StringComparison.Ordinal) ||
            cell.GetString().Contains("阶段合价", StringComparison.Ordinal) ||
            cell.GetString().Contains("金额净变化", StringComparison.Ordinal) ||
            cell.GetString().Contains("前阶段", StringComparison.Ordinal) ||
            cell.GetString().Contains("后阶段", StringComparison.Ordinal));
    }

    [Fact]
    public void Similar_item_amount_builder_sets_readable_bounded_column_widths()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("同类清单列宽");

        SimilarItemAmountDashboardSheetBuilder.Build(worksheet, CreateCrossUnitSummarySession());

        Assert.InRange(worksheet.Column(1).Width, 6d, 8d);
        Assert.InRange(worksheet.Column(2).Width, 18d, 30d);
        Assert.InRange(worksheet.Column(3).Width, 24d, 40d);
        Assert.InRange(worksheet.Column(4).Width, 8d, 10d);
        for (var column = 5; column <= 8; column++)
        {
            Assert.InRange(worksheet.Column(column).Width, 16d, 18d);
        }
        Assert.InRange(worksheet.Column(9).Width, 32d, 42d);
        Assert.True(worksheet.Cell("I5").Style.Alignment.WrapText);
        Assert.Equal(1, worksheet.PageSetup.PagesWide);
        Assert.InRange(worksheet.Columns(1, 9).Sum(x => x.Width), 0d, 202d);
    }

    [Fact]
    public void Similar_item_amount_builder_uses_dynamic_columns_for_each_two_stage_mode()
    {
        using var workbook = new XLWorkbook();
        var initialOnlyWorksheet = workbook.Worksheets.Add("初审阶段");
        var finalOnlyWorksheet = workbook.Worksheets.Add("终审结算");

        SimilarItemAmountDashboardSheetBuilder.Build(
            initialOnlyWorksheet,
            CreateExportSession(true, false, UnitProjectKind.ContractPrice));
        SimilarItemAmountDashboardSheetBuilder.Build(
            finalOnlyWorksheet,
            CreateExportSession(false, true, UnitProjectKind.ContractPrice));

        Assert.Equal(7, initialOnlyWorksheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("初审阶段（送审－初审）", initialOnlyWorksheet.Cell("E3").GetString());
        Assert.DoesNotContain(initialOnlyWorksheet.CellsUsed(), x =>
            x.GetString().Contains("终审结算", StringComparison.Ordinal));

        Assert.Equal(7, finalOnlyWorksheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("终审结算（送审－终审）", finalOnlyWorksheet.Cell("E3").GetString());
        Assert.DoesNotContain(finalOnlyWorksheet.CellsUsed(), x =>
            x.GetString().Contains("初审阶段", StringComparison.Ordinal));
    }

    [Fact]
    public void Similar_item_amount_builder_keeps_empty_message_in_merged_dynamic_region()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("无变化");

        SimilarItemAmountDashboardSheetBuilder.Build(worksheet, CreateCrossUnitSummarySession(unchanged: true));

        Assert.Equal("无审增审减清单", worksheet.Cell("A5").GetString());
        Assert.Contains("A5:I5", worksheet.MergedRanges.Select(range => range.RangeAddress.ToString()));
        Assert.Equal(4, worksheet.AutoFilter.Range?.FirstRow().RowNumber());
        Assert.Equal(9, worksheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.ColumnNumber);
    }

    [Fact]
    public void Similar_item_amount_builder_writes_numeric_zero_for_missing_single_scope_reduction()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("单口径零值");

        SimilarItemAmountDashboardSheetBuilder.Build(worksheet, CreateSingleScopeAmountSession());

        Assert.Equal(XLDataType.Number, worksheet.Cell("E5").DataType);
        Assert.Equal(0m, worksheet.Cell("E5").GetValue<decimal>());
        Assert.Equal(-20m, worksheet.Cell("F5").GetValue<decimal>());
        Assert.Equal("#,##0.00;-#,##0.00;0.00", worksheet.Cell("F5").Style.NumberFormat.Format);
    }

    [Fact]
    public void Similar_item_amount_builder_keeps_single_scope_note_unprefixed()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("单口径备注");

        SimilarItemAmountDashboardSheetBuilder.Build(worksheet, CreateSingleScopeAmountSession());

        var note = worksheet.Cell("G5").GetString();
        Assert.Equal("研发楼／送审系统→初审系统", note);
        Assert.DoesNotContain("初审阶段：", note, StringComparison.Ordinal);
        Assert.DoesNotContain("终审结算：", note, StringComparison.Ordinal);
    }

    private static ComparisonSession CreateSession()
    {
        var sentItems = new[] { new BillItem("9", "DEL", "送审项目", "特征", "项", 1m, 1m, 100m, 100m) };
        var reviewItems = new[] { new BillItem("11", "ADD", "初审新增", "特征", "项", 2m, 2m, 30m, 60m) };
        var submission = new CostWorkbook("送审.xls", "测试项目", [new("研发楼", 1000m)], [new("研发楼", UnitProjectKind.ContractPrice, sentItems)]);
        var review = new CostWorkbook("初审.xls", "测试项目", [new("研发楼", 800m)], [new("研发楼", UnitProjectKind.ContractPrice, reviewItems)]);
        return new ComparisonEngine().Compare(submission, review, ComparisonSettings.Default);
    }

    private static ComparisonSession CreateSingleScopeAmountSession()
    {
        static BillItem Item(string code, decimal total, int sourceRow) =>
            new("1", code, "桥架", "100x50", "m", 10m, 10m, total / 10m, total)
            {
                SourceRowIndex = sourceRow
            };

        static UnitProject Unit(string sheetName, decimal total, string system, BillItem item) =>
            new("研发楼", UnitProjectKind.ContractPrice, [item])
            {
                SourceSheetIndex = 1,
                SourceSheetName = sheetName,
                Sections =
                [
                    new BillSection("整个工程", 0, 1, total, [], true),
                    new BillSection(system, 1, 5, total, [item.SourceRowIndex], false)
                ]
            };

        static CostWorkbook Book(string path, UnitProject unit) =>
            new(path, "项目", [new TaxInclusiveSummaryItem(unit.Name, unit.UntaxedTotal)], [unit]);

        var submission = Book(
            "送审.xlsx",
            Unit("送审来源", 100m, "送审系统", Item("S1", 100m, 11)));
        var initial = Book(
            "初审.xlsx",
            Unit("初审来源", 120m, "初审系统", Item("I1", 120m, 21)));
        return new ComparisonEngine().Compare(
            new ComparisonRequest(submission, initial, null),
            ComparisonSettings.Default);
    }

    private static ComparisonSession CreateSheetNameExportSession(params string[] unitNames)
    {
        static BillItem Item(int unitIndex, decimal total) =>
            new((unitIndex + 1).ToString(), $"C{unitIndex + 1}", $"清单{unitIndex + 1}", "特征", "项",
                1m, 1m, total, total)
            {
                SourceRowIndex = unitIndex + 5
            };

        static UnitProject Unit(string name, int unitIndex, decimal total) =>
            new(name, UnitProjectKind.ContractPrice, [Item(unitIndex, total)])
            {
                SourceSheetIndex = unitIndex + 1,
                SourceSheetName = $"来源{unitIndex + 1}"
            };

        static CostWorkbook Book(string path, IReadOnlyList<UnitProject> units) =>
            new(path, "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, x.UntaxedTotal)).ToList(), units);

        var submissionUnits = unitNames.Select((name, index) => Unit(name, index, 100m)).ToList();
        var initialUnits = unitNames.Select((name, index) => Unit(name, index, 120m)).ToList();
        return new ComparisonEngine().Compare(
            new ComparisonRequest(Book("送审.xlsx", submissionUnits), Book("初审.xlsx", initialUnits), null),
            ComparisonSettings.Default);
    }

    private static string Template() =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "ReportTemplate.xlsx");

    private static string TemporaryXlsxPath() =>
        Path.Combine(Path.GetTempPath(), $"审减分析-{Guid.NewGuid():N}.xlsx");

    private static Task Export(ComparisonSession session, string output) =>
        new ReportExporter().ExportAsync(
            session,
            Template(),
            output,
            CancellationToken.None);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ComparisonSession CreateExportSession(bool hasInitial, bool hasFinal, UnitProjectKind kind)
    {
        static BillItem Bill(int row, string? sequence, string code, decimal quantity, decimal price) =>
            new(sequence, code, code, "特征", "项", quantity, quantity, price, quantity * price)
            {
                SourceRowIndex = row
            };
        static UnitProject Unit(int sheet, UnitProjectKind kind, params BillItem[] items) =>
            new("研发楼安装", kind, items)
            {
                SourceSheetIndex = sheet,
                SourceSheetName = $"来源{sheet}"
            };
        static CostWorkbook Book(string path, UnitProject unit) =>
            new(path, "项目", [new(unit.Name, unit.UntaxedTotal)], [unit]);

        var submission = Book("送审.xlsx", Unit(1, kind,
            Bill(5, "1", "A01", 2m, 100m),
            Bill(6, "2", "DEL", 1m, 20m)));
        var initial = hasInitial
            ? Book("初审.xlsx", Unit(1, kind,
                Bill(5, null, "A01", 1m, 90m),
                Bill(6, null, "NEW", 1m, 30m)))
            : null;
        var final = hasFinal
            ? Book("终审.xlsx", Unit(1, kind,
                Bill(5, null, "A01", 1m, 80m),
                Bill(6, null, "NEW", 2m, 25m)))
            : null;
        return new ComparisonEngine().Compare(new ComparisonRequest(submission, initial, final), ComparisonSettings.Default);
    }

    private static ComparisonSession CreateTopTwentyLimitSession()
    {
        static UnitProject Unit(string name, int sheet, int count, bool addZero, bool initial)
        {
            var itemCount = count + (addZero ? 1 : 0);
            var items = Enumerable.Range(1, itemCount).Select(index =>
            {
                var submissionTotal = 100m + index;
                var isZero = addZero && index == itemCount;
                var total = initial && !isZero ? submissionTotal + index : submissionTotal;
                return new BillItem(index.ToString(), $"C{sheet}-{index}", $"清单{sheet}-{index}",
                    $"特征{sheet}-{index}", "项", 1m, 1m, total, total)
                {
                    SourceRowIndex = index + 4
                };
            }).ToList();
            return new UnitProject(name, UnitProjectKind.ContractPrice, items)
            {
                SourceSheetIndex = sheet,
                SourceSheetName = $"来源{sheet}"
            };
        }

        static CostWorkbook Book(string path, params UnitProject[] units) =>
            new(path, "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, x.UntaxedTotal)).ToList(), units);

        var submission = Book("送审.xlsx",
            Unit("甲楼", 1, 15, false, false),
            Unit("乙楼", 2, 10, true, false));
        var initial = Book("初审.xlsx",
            Unit("甲楼", 1, 15, false, true),
            Unit("乙楼", 2, 10, true, true));
        return new ComparisonEngine().Compare(
            new ComparisonRequest(submission, initial, null),
            ComparisonSettings.Default);
    }

    private static ComparisonSession CreateUnchangedInitialReviewSession()
    {
        static CostWorkbook Book(string path) => new(
            path,
            "项目",
            [new TaxInclusiveSummaryItem("甲楼", 100m)],
            [new UnitProject("甲楼", UnitProjectKind.ContractPrice,
                [new BillItem("1", "A01", "清单", "特征", "项", 1m, 1m, 100m, 100m)
                {
                    SourceRowIndex = 5
                }])]);

        return new ComparisonEngine().Compare(
            new ComparisonRequest(Book("送审.xlsx"), Book("初审.xlsx"), null),
            ComparisonSettings.Default);
    }

    private static ComparisonSession CreateSectionedExportSession(
        bool includeEmptySection = false,
        UnitProjectKind kind = UnitProjectKind.ContractPrice)
    {
        static BillItem Bill(int row, string? sequence, string code, decimal quantity, decimal price) =>
            new(sequence, code, code, "特征", "项", quantity, quantity, price, quantity * price)
            {
                SourceRowIndex = row
            };

        static UnitProject Unit(
            string sourceName,
            UnitProjectKind kind,
            decimal rootTotal,
            decimal electricalTotal,
            bool includeFinalOnlySection,
            bool includeEmptySection)
        {
            var electrical = Bill(7, sourceName == "送审" ? "1" : null, "A01",
                sourceName == "送审" ? 2m : 1m,
                sourceName == "送审" ? 100m : sourceName == "初审" ? 180m : 150m);
            var items = new List<BillItem> { electrical };
            var sections = new List<BillSection>
            {
                new("整个工程", 0, 99, rootTotal, [], true),
                new("电气", 1, 5, electricalTotal, [7], false)
            };
            if (includeFinalOnlySection)
            {
                items.Add(Bill(11, null, "NEW", 1m, 50m));
                sections.Add(new BillSection("终审新增专业", 1, 9, 50m, [11], false));
            }
            if (includeEmptySection)
            {
                sections.Add(new BillSection("空标题", 1, 13, 0m, [], false));
            }

            return new UnitProject("研发楼安装", kind, items)
            {
                SourceSheetIndex = 1,
                SourceSheetName = $"{sourceName}来源页",
                Sections = sections
            };
        }

        static CostWorkbook Book(string path, UnitProject unit) =>
            new(path, "项目", [new(unit.Name, unit.Sections.Single(x => x.IsRoot).Total)], [unit]);

        var submission = Book("送审.xlsx", Unit("送审", kind, 200m, 200m, false, false));
        var initial = Book("初审.xlsx", Unit("初审", kind, 180m, 180m, false, false));
        var final = Book("终审.xlsx", Unit("终审", kind, 200m, 150m, true, includeEmptySection));
        return new ComparisonEngine().Compare(
            new ComparisonRequest(submission, initial, final),
            ComparisonSettings.Default);
    }

    private static ComparisonSession CreateCrossUnitSummarySession(bool unchanged = false)
    {
        static BillItem I(string sequence, string code, string name, string feature,
            string unit, decimal quantity, decimal total, int sourceRow) =>
            new(sequence, code, name, feature, unit, null, quantity,
                quantity == 0m ? 0m : total / quantity, total)
            {
                SourceRowIndex = sourceRow
            };

        static UnitProject U(string name, int sheetIndex, string system, BillItem item) =>
            new(name, UnitProjectKind.ContractPrice, [item])
            {
                SourceSheetIndex = sheetIndex,
                SourceSheetName = $"源页签{sheetIndex}",
                Sections =
                [
                    new BillSection("整个工程", 0, 1, item.Total ?? 0m, [], true),
                    new BillSection(system, 1, 10, item.Total ?? 0m, [item.SourceRowIndex], false)
                ]
            };

        static CostWorkbook B(string path, params UnitProject[] units) =>
            new(path, "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, x.UntaxedTotal)).ToList(), units);

        var submission = B("送审.xlsx",
            U("1#楼", 1, "电气", I("1", "S1", "电缆甲", "铜 芯", "m", 10m, 100m, 11)),
            U("2#楼", 2, "给排水", I("1", "S2", "电缆乙", "铜芯", "M", 10m, 100m, 11)));
        var initial = unchanged
            ? B("初审.xlsx",
                U("1#楼", 1, "电气", I("1", "I1", "电缆甲", "铜芯", "ｍ", 10m, 100m, 21)),
                U("2#楼", 2, "给排水", I("1", "I2", "电缆乙", "铜芯", "m", 10m, 100m, 21)))
            : B("初审.xlsx",
                U("1#楼", 1, "电气", I("1", "I1", "电缆甲", "铜芯", "ｍ", 12m, 120m, 21)),
                U("2#楼", 2, "给排水", I("1", "I2", "电缆乙", "铜芯", "m", 8m, 80m, 21)));
        var final = unchanged
            ? B("终审.xlsx",
                U("1#楼", 1, "电气", I("1", "F1", "电缆甲", "铜芯", "m", 10m, 100m, 31)),
                U("2#楼", 2, "给排水", I("1", "F2", "电缆乙", "铜芯", "m", 10m, 100m, 31)))
            : B("终审.xlsx",
                U("1#楼", 1, "电气", I("1", "F1", "电缆甲", "铜芯", "m", 14m, 140m, 31)),
                U("2#楼", 2, "给排水", I("1", "F2", "电缆乙", "铜芯", "m", 6m, 60m, 31)));

        return new ComparisonEngine().Compare(
            new ComparisonRequest(submission, initial, final),
            ComparisonSettings.Default);
    }
}
