using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class DetailSheetBuilder
{
    private const string NumberFormat = "#,##0.00;-#,##0.00;0.00";
    private const string PercentFormat = "0.00%;-0.00%;0.00%";

    public static void Build(
        IXLWorksheet worksheet,
        UnitComparison unit,
        ComparisonRequest request,
        IDictionary<ComparisonRow, DetailTarget> detailTargets)
    {
        var layout = DetailColumnLayout.Create(unit.Kind, request.HasInitialReview, request.HasFinalReview);
        var activeStages = ActiveStages(request);
        var activePairs = ComparisonPairs.OutputFor(request);
        var structured = unit.DetailLines.Any(x => x is SectionDetailLine);

        worksheet.Cell(1, 1).Value = unit.Kind == UnitProjectKind.ContractPrice
            ? "合同价清单审减分析表"
            : "新组价清单审减分析表";
        worksheet.Range(1, 1, 1, layout.LastColumn).Merge();
        WorksheetFormatting.ApplyTitle(worksheet.Range(1, 1, 1, layout.LastColumn));
        worksheet.Cell(2, 1).Value = $"工程名称：{unit.Name}";
        worksheet.Range(2, 1, 2, layout.LastColumn).Merge();
        worksheet.Range(2, 1, 2, layout.LastColumn).Style.Fill.BackgroundColor = WorksheetFormatting.BaseHeaderColor;

        WriteHeaders(worksheet, layout, activeStages, activePairs);

        var row = 5;
        foreach (var line in unit.DetailLines)
        {
            switch (line)
            {
                case SectionDetailLine sectionLine:
                    WriteSectionLine(worksheet, row, sectionLine.Section, layout, activeStages, activePairs);
                    WorksheetFormatting.StyleSummaryRow(worksheet.Range(row, 1, row, layout.LastColumn));
                    break;
                case ItemDetailLine itemLine:
                    WriteItemLine(worksheet, row, itemLine.Row, layout, activeStages, activePairs);
                    detailTargets[itemLine.Row] = new DetailTarget(worksheet.Name, row);
                    break;
            }

            if (line.OutlineLevel > 0)
            {
                worksheet.Row(row).Group(line.OutlineLevel, collapse: false);
                worksheet.Row(row).Unhide();
            }

            row++;
        }

        var lastRow = structured ? Math.Max(4, row - 1) : row;
        if (!structured)
        {
            WriteFlatSubtotal(worksheet, row, layout, activeStages, activePairs);
        }

        WorksheetFormatting.ApplyBorders(worksheet.Range(3, 1, lastRow, layout.LastColumn));
        ApplyDifferenceFormatting(worksheet, layout, lastRow);
        FormatSheet(worksheet, layout, lastRow);
    }

    private static void WriteHeaders(
        IXLWorksheet worksheet,
        DetailColumnLayout layout,
        IReadOnlyList<WorkbookRole> activeStages,
        IReadOnlyList<StagePair> activePairs)
    {
        var fixedHeaders = layout.FixedColumnCount == 6
            ? new[] { "序号", "项目编码", "项目名称", "项目特征描述", "计量单位", "合同工程量" }
            : new[] { "序号", "项目编码", "项目名称", "项目特征描述", "计量单位" };
        for (var index = 0; index < fixedHeaders.Length; index++)
        {
            worksheet.Range(3, index + 1, 4, index + 1).Merge().Value = fixedHeaders[index];
        }
        WorksheetFormatting.StyleHeader(
            worksheet.Range(3, 1, 4, layout.FixedColumnCount),
            WorksheetFormatting.BaseHeaderColor);

        foreach (var stage in activeStages)
        {
            var columns = layout.Stages[stage];
            worksheet.Range(3, columns.Quantity, 3, columns.Total).Merge().Value = StageName(stage);
            WorksheetFormatting.SetRowValues(worksheet, 4, columns.Quantity, ["工程量", "综合单价", "合价"]);
            WorksheetFormatting.StyleHeader(
                worksheet.Range(3, columns.Quantity, 4, columns.Total),
                WorksheetFormatting.HeaderColor(stage));
        }

        foreach (var pair in activePairs)
        {
            var columns = layout.Differences[pair.Kind];
            worksheet.Range(3, columns.Quantity, 3, columns.TotalRate).Merge().Value = $"差异（{pair.DifferenceLabel}）";
            WorksheetFormatting.SetRowValues(worksheet, 4, columns.Quantity, ["工程量差", "单价差", "合价差", "合价差异率"]);
            WorksheetFormatting.StyleHeader(
                worksheet.Range(3, columns.Quantity, 4, columns.TotalRate),
                WorksheetFormatting.DifferenceHeaderColor);
        }

        worksheet.Range(3, layout.CommentColumn, 4, layout.CommentColumn).Merge().Value = "批注";
        WorksheetFormatting.StyleHeader(
            worksheet.Range(3, layout.CommentColumn, 4, layout.CommentColumn),
            WorksheetFormatting.BaseHeaderColor);
        if (layout.MatchStatusColumn is int matchStatusColumn)
        {
            worksheet.Range(3, matchStatusColumn, 4, matchStatusColumn).Merge().Value = "匹配状态";
            WorksheetFormatting.StyleHeader(
                worksheet.Range(3, matchStatusColumn, 4, matchStatusColumn),
                WorksheetFormatting.BaseHeaderColor);
        }
    }

    private static void WriteSectionLine(
        IXLWorksheet worksheet,
        int row,
        SectionComparison section,
        DetailColumnLayout layout,
        IReadOnlyList<WorkbookRole> activeStages,
        IReadOnlyList<StagePair> activePairs)
    {
        worksheet.Cell(row, 3).Value = section.Title;
        foreach (var stage in activeStages)
        {
            var columns = layout.Stages[stage];
            worksheet.Cell(row, columns.Quantity).Clear(XLClearOptions.Contents);
            worksheet.Cell(row, columns.UnitPrice).Clear(XLClearOptions.Contents);
            WorksheetFormatting.WriteNumber(worksheet.Cell(row, columns.Total), section.Total(stage));
        }

        foreach (var pair in activePairs)
        {
            WriteDifferenceFormulas(worksheet, row, pair, layout, includeQuantityAndPrice: false);
        }
    }

    private static void WriteItemLine(
        IXLWorksheet worksheet,
        int row,
        ComparisonRow comparison,
        DetailColumnLayout layout,
        IReadOnlyList<WorkbookRole> activeStages,
        IReadOnlyList<StagePair> activePairs)
    {
        var item = comparison.Submission ?? comparison.InitialReview ?? comparison.FinalReview
            ?? throw new InvalidOperationException("对比行两侧均为空。");
        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 1), comparison.Submission?.SourceSequence);
        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 2), item.Code);
        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 3), item.Name);
        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 4), item.Feature);
        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 5), item.Unit);
        if (layout.FixedColumnCount == 6)
        {
            WorksheetFormatting.WriteNumber(worksheet.Cell(row, 6), comparison.Submission?.ContractQuantity ?? 0m);
        }

        foreach (var stage in activeStages)
        {
            var columns = layout.Stages[stage];
            WorksheetFormatting.WriteNumber(worksheet.Cell(row, columns.Quantity), comparison.Quantity(stage));
            WorksheetFormatting.WriteNumber(worksheet.Cell(row, columns.UnitPrice), comparison.UnitPrice(stage));
            WorksheetFormatting.WriteNumber(worksheet.Cell(row, columns.Total), comparison.Total(stage));
        }

        foreach (var pair in activePairs)
        {
            WriteDifferenceFormulas(worksheet, row, pair, layout, includeQuantityAndPrice: true);
        }

        WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, layout.CommentColumn), comparison.CombinedComment);
        if (layout.MatchStatusColumn is int matchStatusColumn)
        {
            WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, matchStatusColumn), StatusName(comparison));
        }
    }

    private static void WriteFlatSubtotal(
        IXLWorksheet worksheet,
        int row,
        DetailColumnLayout layout,
        IReadOnlyList<WorkbookRole> activeStages,
        IReadOnlyList<StagePair> activePairs)
    {
        worksheet.Cell(row, 3).Value = "小计";
        var lastItemRow = row - 1;
        if (layout.FixedColumnCount == 6)
        {
            WriteSumOrZero(worksheet.Cell(row, 6), 6, lastItemRow);
        }

        foreach (var stage in activeStages)
        {
            var columns = layout.Stages[stage];
            WriteSumOrZero(worksheet.Cell(row, columns.Quantity), columns.Quantity, lastItemRow);
            WriteSumOrZero(worksheet.Cell(row, columns.UnitPrice), columns.UnitPrice, lastItemRow);
            WriteSumOrZero(worksheet.Cell(row, columns.Total), columns.Total, lastItemRow);
        }

        foreach (var pair in activePairs)
        {
            WriteDifferenceFormulas(worksheet, row, pair, layout, includeQuantityAndPrice: true);
        }

        WorksheetFormatting.StyleSummaryRow(worksheet.Range(row, 1, row, layout.LastColumn));
    }

    private static void WriteSumOrZero(IXLCell cell, int column, int lastItemRow)
    {
        if (lastItemRow >= 5)
        {
            var letter = cell.Worksheet.Column(column).ColumnLetter();
            cell.FormulaA1 = $"SUM({letter}5:{letter}{lastItemRow})";
        }
        else
        {
            cell.Value = 0m;
        }
    }

    private static void WriteDifferenceFormulas(
        IXLWorksheet worksheet,
        int row,
        StagePair pair,
        DetailColumnLayout layout,
        bool includeQuantityAndPrice)
    {
        var columns = layout.Differences[pair.Kind];
        var earlier = layout.Stages[pair.Earlier];
        var later = layout.Stages[pair.Later];
        var earlierValues = new[] { earlier.Quantity, earlier.UnitPrice, earlier.Total };
        var laterValues = new[] { later.Quantity, later.UnitPrice, later.Total };
        var differenceValues = new[] { columns.Quantity, columns.UnitPrice, columns.Total };
        for (var index = includeQuantityAndPrice ? 0 : 2; index < 3; index++)
        {
            var earlierAddress = worksheet.Cell(row, earlierValues[index]).Address.ToStringRelative();
            var laterAddress = worksheet.Cell(row, laterValues[index]).Address.ToStringRelative();
            worksheet.Cell(row, differenceValues[index]).FormulaA1 = $"{earlierAddress}-{laterAddress}";
        }

        var earlierTotal = worksheet.Cell(row, earlier.Total).Address.ToStringRelative();
        var laterTotal = worksheet.Cell(row, later.Total).Address.ToStringRelative();
        var differenceTotal = worksheet.Cell(row, columns.Total).Address.ToStringRelative();
        worksheet.Cell(row, columns.TotalRate).FormulaA1 =
            $"IF({earlierTotal}=0,IF({laterTotal}=0,0,SIGN({differenceTotal})),{differenceTotal}/ABS({earlierTotal}))";
        if (!includeQuantityAndPrice)
        {
            worksheet.Cell(row, columns.Quantity).Clear(XLClearOptions.Contents);
            worksheet.Cell(row, columns.UnitPrice).Clear(XLClearOptions.Contents);
        }
    }

    private static void ApplyDifferenceFormatting(IXLWorksheet worksheet, DetailColumnLayout layout, int lastRow)
    {
        if (lastRow < 5)
        {
            return;
        }

        foreach (var columns in layout.Differences.Values)
        {
            for (var column = columns.Quantity; column <= columns.TotalRate; column++)
            {
                WorksheetFormatting.AddSignConditionalFormats(worksheet.Range(5, column, lastRow, column));
            }
        }
    }

    private static void FormatSheet(IXLWorksheet worksheet, DetailColumnLayout layout, int lastRow)
    {
        worksheet.Range(1, 1, lastRow, layout.LastColumn).Style.Font.FontName = "宋体";
        worksheet.Range(3, 1, lastRow, layout.LastColumn).Style.Font.FontSize = 10;
        worksheet.Range(3, 1, lastRow, layout.LastColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Range(3, 1, lastRow, layout.LastColumn).Style.Alignment.WrapText = true;
        worksheet.Columns(1, layout.LastColumn).Width = 11;
        worksheet.Column(1).Width = 7;
        worksheet.Column(2).Width = 18;
        worksheet.Column(3).Width = 22;
        worksheet.Column(4).Width = 48;
        worksheet.Column(5).Width = 10;
        worksheet.Column(layout.CommentColumn).Width = 24;
        if (layout.MatchStatusColumn is int matchStatusColumn)
        {
            worksheet.Column(matchStatusColumn).Width = 26;
        }

        if (lastRow >= 5)
        {
            foreach (var columns in layout.Stages.Values)
            {
                worksheet.Range(5, columns.Quantity, lastRow, columns.Total).Style.NumberFormat.Format = NumberFormat;
            }

            foreach (var columns in layout.Differences.Values)
            {
                worksheet.Range(5, columns.Quantity, lastRow, columns.Total).Style.NumberFormat.Format = NumberFormat;
                worksheet.Range(5, columns.TotalRate, lastRow, columns.TotalRate).Style.NumberFormat.Format = PercentFormat;
            }
        }

        worksheet.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;
        worksheet.SheetView.FreezeRows(4);
        worksheet.SheetView.FreezeColumns(layout.FixedColumnCount);
        WorksheetFormatting.ConfigurePrint(worksheet, lastRow, layout.LastColumn);
    }

    private static IReadOnlyList<WorkbookRole> ActiveStages(ComparisonRequest request)
    {
        var stages = new List<WorkbookRole>(3) { WorkbookRole.Submission };
        if (request.HasInitialReview)
        {
            stages.Add(WorkbookRole.InitialReview);
        }

        if (request.HasFinalReview)
        {
            stages.Add(WorkbookRole.FinalReview);
        }

        return stages;
    }

    private static string StageName(WorkbookRole stage) => stage switch
    {
        WorkbookRole.Submission => "送审",
        WorkbookRole.InitialReview => "初审",
        WorkbookRole.FinalReview => "终审",
        _ => stage.ToString()
    };

    private static string StatusName(ComparisonRow comparison)
    {
        var statusPair = comparison.PairComparisons
            .FirstOrDefault(x => x.Value.Status != MatchStatus.NotApplicable);
        var status = statusPair.Value?.Status ?? MatchStatus.NotApplicable;
        var pair = statusPair.Key;
        return status switch
        {
            MatchStatus.CodeMatched => "编码匹配",
            MatchStatus.CodeChanged => "编码变更",
            MatchStatus.ContentMatched => "名称特征单位匹配（编码缺失）",
            MatchStatus.NeedsConfirmation => "待确认",
            MatchStatus.ManualLinked => "人工关联",
            MatchStatus.LaterOnly => pair == DifferenceKind.FinalVsSubmission ? "终审新增" : "初审新增",
            MatchStatus.EarlierOnly => pair == DifferenceKind.FinalVsInitial ? "初审独有" : "送审独有",
            _ => status.ToString()
        };
    }
}
