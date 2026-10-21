using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class AbsoluteDifferenceTop20SheetBuilder
{
    public static void Build(
        IXLWorksheet worksheet,
        ComparisonSession session,
        IReadOnlyDictionary<ComparisonRow, DetailTarget> detailTargets)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(detailTargets);

        var scopes = DashboardAuditScopes.TopTwenty(session.Request);
        var stages = DashboardAuditScopes.ActiveStages(session.Request);
        var lastColumn = 8 + stages.Count;
        worksheet.Cell(1, 1).Value = "绝对差额 Top 20看板";
        worksheet.Range(1, 1, 1, lastColumn).Merge();
        WorksheetFormatting.ApplyTitle(worksheet.Range(1, 1, 1, lastColumn));
        worksheet.Cell(2, 1).Value = $"工程名称：{session.Submission.ProjectName}";
        worksheet.Range(2, 1, 2, lastColumn).Merge();
        worksheet.Range(2, 1, 2, lastColumn).Style.Fill.BackgroundColor = WorksheetFormatting.BaseHeaderColor;

        var headers = new List<object?>
        {
            "审核口径", "排名", "单位工程", "清单序号", "项目名称"
        };
        headers.AddRange(stages.Select(DashboardAuditScopes.StageTotalHeader));
        headers.AddRange(["审增审减额", "绝对差额", "类型"]);
        WorksheetFormatting.SetRowValues(worksheet, 3, 1, headers);
        WorksheetFormatting.StyleHeader(
            worksheet.Range(3, 1, 3, lastColumn),
            WorksheetFormatting.BaseHeaderColor);

        var row = 4;
        foreach (var scope in scopes)
        {
            var ranked = session.UnitComparisons
                .SelectMany((unit, unitOrder) => unit.Rows.Select(item => new { unit, unitOrder, item }))
                .Where(x => x.item.Item(scope.Pair.Earlier) is not null || x.item.Item(scope.Pair.Later) is not null)
                .Select(x => new RankedRow(
                    x.unit,
                    x.unitOrder,
                    x.item,
                    ComparisonPairs.OutputDifference(scope.Pair, x.item.Total)))
                .Where(x => Math.Abs(x.Difference) > session.Settings.AmountTolerance)
                .OrderByDescending(x => Math.Abs(x.Difference))
                .ThenBy(x => x.UnitOrder)
                .ThenBy(x => SourceRow(x.Row, scope.Pair))
                .Take(20)
                .ToList();

            if (ranked.Count == 0)
            {
                worksheet.Cell(row, 1).Value = scope.AuditLabel;
                worksheet.Cell(row, 5).Value = "无审增审减清单";
                worksheet.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor =
                    DashboardAuditScopes.RowFill(scope.Pair.Kind);
                row++;
                continue;
            }

            for (var index = 0; index < ranked.Count; index++)
            {
                WriteRow(worksheet, row++, scope, stages, ranked[index], index + 1, detailTargets);
            }
        }

        FormatSheet(worksheet, Math.Max(4, row - 1), lastColumn, stages.Count);
    }

    private sealed record RankedRow(
        UnitComparison Unit,
        int UnitOrder,
        ComparisonRow Row,
        decimal Difference);

    private static int SourceRow(ComparisonRow row, StagePair pair) =>
        row.Item(pair.Later)?.SourceRowIndex
        ?? row.Item(pair.Earlier)?.SourceRowIndex
        ?? int.MaxValue;

    private static void WriteRow(
        IXLWorksheet worksheet,
        int outputRow,
        DashboardAuditScope scope,
        IReadOnlyList<WorkbookRole> stages,
        RankedRow ranked,
        int rank,
        IReadOnlyDictionary<ComparisonRow, DetailTarget> detailTargets)
    {
        worksheet.Cell(outputRow, 1).Value = scope.AuditLabel;
        worksheet.Cell(outputRow, 2).Value = rank;
        worksheet.Cell(outputRow, 3).Value = ranked.Unit.Name;
        worksheet.Range(outputRow, 1, outputRow, 8 + stages.Count).Style.Fill.BackgroundColor =
            DashboardAuditScopes.RowFill(scope.Pair.Kind);

        var sequence = ranked.Row.Submission?.SourceSequence;
        var displaySequence = string.IsNullOrWhiteSpace(sequence) ? "无序号" : sequence;
        if (detailTargets.TryGetValue(ranked.Row, out var target))
        {
            WorksheetFormatting.SetInternalHyperlink(
                worksheet.Cell(outputRow, 4), displaySequence, target.SheetName, target.Row);
        }
        else
        {
            worksheet.Cell(outputRow, 4).Value = displaySequence;
        }

        var item = ranked.Row.Item(scope.Pair.Later) ?? ranked.Row.Item(scope.Pair.Earlier)!;
        worksheet.Cell(outputRow, 5).Value = item.Name;
        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            var cell = worksheet.Cell(outputRow, 6 + index);
            if (stage != scope.Pair.Earlier && stage != scope.Pair.Later)
            {
                cell.Value = "—";
            }
            else
            {
                cell.Value = ranked.Row.Total(stage);
            }
        }

        var differenceColumn = 6 + stages.Count;
        worksheet.Cell(outputRow, differenceColumn).Value = ranked.Difference;
        worksheet.Cell(outputRow, differenceColumn + 1).Value = Math.Abs(ranked.Difference);
        worksheet.Cell(outputRow, differenceColumn + 2).Value = ranked.Difference > 0m ? "审减" : "审增";
    }

    private static void FormatSheet(
        IXLWorksheet worksheet,
        int lastRow,
        int lastColumn,
        int stageCount)
    {
        worksheet.ShowGridLines = false;
        worksheet.Range(4, 6, lastRow, 5 + stageCount).Style.NumberFormat.Format =
            "#,##0.00;-#,##0.00;0.00";
        var differenceColumn = 6 + stageCount;
        worksheet.Range(4, differenceColumn, lastRow, differenceColumn + 1)
            .Style.NumberFormat.Format = "#,##0.00;-#,##0.00;0.00";
        WorksheetFormatting.AddSignConditionalFormats(
            worksheet.Range(4, differenceColumn, lastRow, differenceColumn));
        worksheet.Range(3, 1, lastRow, lastColumn).SetAutoFilter();
        worksheet.SheetView.FreezeRows(3);
        worksheet.Range(1, 1, lastRow, lastColumn).Style.Font.FontName = "宋体";
        worksheet.Range(3, 1, lastRow, lastColumn).Style.Alignment.WrapText = true;
        WorksheetFormatting.ApplyBorders(worksheet.Range(3, 1, lastRow, lastColumn));
        worksheet.Columns(1, lastColumn).AdjustToContents(4, lastRow);
        worksheet.Column(1).Width = Math.Max(worksheet.Column(1).Width, 28);
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 18);
        worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 26);
        WorksheetFormatting.ConfigurePrint(worksheet, lastRow, lastColumn);
    }
}
