using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class SimilarItemAmountDashboardSheetBuilder
{
    public static void Build(IXLWorksheet worksheet, ComparisonSession session)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        ArgumentNullException.ThrowIfNull(session);

        var scopes = DashboardAuditScopes.SimilarItems(session.Request);
        var differences = scopes.Select(x => x.Pair.Kind).ToList();
        var rows = SimilarItemAmountAggregator.Aggregate(session, differences);
        var noteColumn = 5 + scopes.Count * 2;
        var dataRow = 5;

        WriteHeaders(worksheet, session, scopes, noteColumn);
        if (rows.Count == 0)
        {
            worksheet.Cell(dataRow, 1).Value = "无审增审减清单";
            worksheet.Range(dataRow, 1, dataRow, noteColumn).Merge();
            FormatSheet(worksheet, dataRow, noteColumn, scopes, null);
            return;
        }

        foreach (var item in rows)
        {
            WriteDataRow(worksheet, dataRow++, item, scopes, noteColumn);
        }

        WriteTotals(worksheet, dataRow, scopes, noteColumn);
        FormatSheet(worksheet, dataRow, noteColumn, scopes, dataRow - 1);
    }

    private static void WriteHeaders(
        IXLWorksheet worksheet,
        ComparisonSession session,
        IReadOnlyList<DashboardAuditScope> scopes,
        int noteColumn)
    {
        worksheet.Cell(1, 1).Value = "同类清单审增审减汇总看板";
        worksheet.Range(1, 1, 1, noteColumn).Merge();
        WorksheetFormatting.ApplyTitle(worksheet.Range(1, 1, 1, noteColumn));
        worksheet.Cell(2, 1).Value = $"工程名称：{session.Submission.ProjectName}";
        worksheet.Range(2, 1, 2, noteColumn).Merge();
        worksheet.Range(2, 1, 2, noteColumn).Style.Fill.BackgroundColor =
            WorksheetFormatting.BaseHeaderColor;
        worksheet.Range(3, 1, 3, 4).Merge().Value = "清单信息";
        for (var index = 0; index < scopes.Count; index++)
        {
            var start = 5 + index * 2;
            worksheet.Range(3, start, 3, start + 1).Merge().Value = scopes[index].AuditLabel;
        }
        worksheet.Cell(3, noteColumn).Value = "备注";

        var headers = new List<object?> { "序号", "项目名称", "项目特征描述", "计量单位" };
        foreach (var scope in scopes)
        {
            headers.Add(scope.ReductionHeader);
            headers.Add(scope.IncreaseHeader);
        }
        headers.Add("备注");
        WorksheetFormatting.SetRowValues(worksheet, 4, 1, headers);
        WorksheetFormatting.StyleHeader(
            worksheet.Range(3, 1, 4, noteColumn),
            WorksheetFormatting.BaseHeaderColor);
    }

    private static void WriteDataRow(
        IXLWorksheet worksheet,
        int row,
        SimilarItemAmountRow item,
        IReadOnlyList<DashboardAuditScope> scopes,
        int noteColumn)
    {
        worksheet.Cell(row, 1).Value = row - 4;
        worksheet.Cell(row, 2).Value = item.DisplayName;
        worksheet.Cell(row, 3).Value = item.Feature;
        worksheet.Cell(row, 4).Value = item.Unit;
        for (var index = 0; index < scopes.Count; index++)
        {
            var amount = item.StageAmounts[scopes[index].Pair.Kind];
            worksheet.Cell(row, 5 + index * 2).Value = amount.Reduction;
            worksheet.Cell(row, 6 + index * 2).Value = amount.Increase;
        }
        worksheet.Cell(row, noteColumn).Value = BuildNote(item, scopes);
    }

    private static string BuildNote(
        SimilarItemAmountRow item,
        IReadOnlyList<DashboardAuditScope> scopes)
    {
        var parts = new List<string>();
        foreach (var scope in scopes)
        {
            var notes = item.StageAmounts[scope.Pair.Kind].UnitSystemNotes;
            if (notes.Count == 0)
            {
                continue;
            }

            var text = string.Join("；", notes);
            parts.Add(scopes.Count == 1
                ? text
                : $"{(scope.Pair.Kind == DifferenceKind.InitialVsSubmission ? "初审阶段" : "终审结算")}：{text}");
        }
        return string.Join("；", parts);
    }

    private static void WriteTotals(
        IXLWorksheet worksheet,
        int totalRow,
        IReadOnlyList<DashboardAuditScope> scopes,
        int noteColumn)
    {
        worksheet.Cell(totalRow, 1).Value = "合计";
        for (var index = 0; index < scopes.Count; index++)
        {
            foreach (var column in new[] { 5 + index * 2, 6 + index * 2 })
            {
                var letter = worksheet.Column(column).ColumnLetter();
                worksheet.Cell(totalRow, column).FormulaA1 = $"SUM({letter}5:{letter}{totalRow - 1})";
            }
        }
        WorksheetFormatting.StyleSummaryRow(worksheet.Range(totalRow, 1, totalRow, noteColumn));
    }

    private static void FormatSheet(
        IXLWorksheet worksheet,
        int lastRow,
        int lastColumn,
        IReadOnlyList<DashboardAuditScope> scopes,
        int? lastDataRow)
    {
        worksheet.ShowGridLines = false;
        worksheet.Range(5, 5, lastRow, lastColumn - 1).Style.NumberFormat.Format =
            "#,##0.00;-#,##0.00;0.00";
        if (lastDataRow is not null)
        {
            WorksheetFormatting.AddSignConditionalFormats(
                worksheet.Range(5, 5, lastRow, lastColumn - 1));
        }
        worksheet.Range(4, 1, lastDataRow ?? 4, lastColumn).SetAutoFilter();
        worksheet.SheetView.FreezeRows(4);
        worksheet.Range(1, 1, lastRow, lastColumn).Style.Font.FontName = "宋体";
        worksheet.Range(3, 1, lastRow, lastColumn).Style.Alignment.WrapText = true;
        WorksheetFormatting.ApplyBorders(worksheet.Range(3, 1, lastRow, lastColumn));
        SetBoundedWidth(worksheet.Column(1), lastRow, 6, 8);
        SetBoundedWidth(worksheet.Column(2), lastRow, 18, 30);
        SetBoundedWidth(worksheet.Column(3), lastRow, 24, 40);
        SetBoundedWidth(worksheet.Column(4), lastRow, 8, 10);
        for (var column = 5; column < lastColumn; column++)
        {
            SetBoundedWidth(worksheet.Column(column), lastRow, 16, 18);
        }
        SetBoundedWidth(worksheet.Column(lastColumn), lastRow, 32, 42);
        WorksheetFormatting.ConfigurePrint(worksheet, lastRow, lastColumn);
    }

    private static void SetBoundedWidth(IXLColumn column, int lastRow, double minimum, double maximum)
    {
        column.AdjustToContents(4, lastRow);
        column.Width = Math.Clamp(column.Width, minimum, maximum);
    }
}
