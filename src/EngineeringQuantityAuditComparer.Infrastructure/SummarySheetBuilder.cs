using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class SummarySheetBuilder
{
    private const string NumberFormat = "#,##0.00;-#,##0.00;0.00";
    private const string PercentFormat = "0.00%;-0.00%;0.00%";

    public static void Build(IXLWorksheet worksheet, ComparisonSession session) =>
        Build(worksheet, session,
            new Dictionary<UnitComparison, string>(ReferenceEqualityComparer.Instance));

    public static void Build(
        IXLWorksheet worksheet,
        ComparisonSession session,
        IReadOnlyDictionary<UnitComparison, string> unitSheetTargets)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(unitSheetTargets);

        var stages = ActiveStages(session.Request);
        var pairs = ComparisonPairs.OutputFor(session.Request);
        var stageColumns = stages
            .Select((stage, index) => (stage, column: 3 + index))
            .ToDictionary(x => x.stage, x => x.column);
        var pairColumns = pairs
            .Select((pair, index) => (pair, amount: 3 + stages.Count + index * 2, rate: 4 + stages.Count + index * 2))
            .ToDictionary(x => x.pair.Kind, x => (x.amount, x.rate));
        var noteColumn = 3 + stages.Count + pairs.Count * 2;

        worksheet.Cell(1, 1).Value = "工程量审减分析汇总表（含税总造价）";
        worksheet.Range(1, 1, 1, noteColumn).Merge();
        WorksheetFormatting.ApplyTitle(worksheet.Range(1, 1, 1, noteColumn));
        worksheet.Cell(2, 1).Value = $"工程名称：{session.Submission.ProjectName}";
        worksheet.Range(2, 1, 2, noteColumn).Merge();
        worksheet.Range(2, 1, 2, noteColumn).Style.Fill.BackgroundColor = WorksheetFormatting.BaseHeaderColor;

        worksheet.Cell(3, 1).Value = "序号";
        worksheet.Cell(3, 2).Value = "单位工程名称";
        WorksheetFormatting.StyleHeader(worksheet.Range(3, 1, 3, 2), WorksheetFormatting.BaseHeaderColor);
        foreach (var stage in stages)
        {
            var column = stageColumns[stage];
            worksheet.Cell(3, column).Value = $"{StageName(stage)}含税总造价（元）";
            WorksheetFormatting.StyleHeader(worksheet.Cell(3, column).AsRange(), WorksheetFormatting.HeaderColor(stage));
        }

        foreach (var pair in pairs)
        {
            var columns = pairColumns[pair.Kind];
            worksheet.Cell(3, columns.amount).Value = $"项目造价差（{pair.DifferenceLabel}）";
            worksheet.Cell(3, columns.rate).Value = "审减率";
            WorksheetFormatting.StyleHeader(
                worksheet.Range(3, columns.amount, 3, columns.rate),
                WorksheetFormatting.DifferenceHeaderColor);
        }

        worksheet.Cell(3, noteColumn).Value = "备注";
        WorksheetFormatting.StyleHeader(worksheet.Cell(3, noteColumn).AsRange(), WorksheetFormatting.BaseHeaderColor);

        var row = 4;
        for (var index = 0; index < session.SummaryRows.Count; index++)
        {
            var summary = session.SummaryRows[index];
            WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, 1), summary.SubmissionSequence);
            if (index < session.UnitComparisons.Count &&
                unitSheetTargets.TryGetValue(session.UnitComparisons[index], out var sheetName))
            {
                WorksheetFormatting.SetInternalHyperlink(worksheet.Cell(row, 2), summary.Name, sheetName, 1);
            }
            else
            {
                worksheet.Cell(row, 2).Value = summary.Name;
            }

            foreach (var stage in stages)
            {
                worksheet.Cell(row, stageColumns[stage]).Value = summary.StageAmounts.GetValueOrDefault(stage);
            }

            foreach (var pair in pairs)
            {
                var columns = pairColumns[pair.Kind];
                WriteReductionFormulas(worksheet, row, columns.amount, columns.rate,
                    stageColumns[pair.Earlier], stageColumns[pair.Later]);
            }

            WorksheetFormatting.WriteOptionalText(worksheet.Cell(row, noteColumn), summary.Note);
            row++;
        }

        var totalRow = row;
        worksheet.Cell(totalRow, 2).Value = "合计";
        foreach (var stage in stages)
        {
            WriteSumOrZero(worksheet.Cell(totalRow, stageColumns[stage]), stageColumns[stage], 4, totalRow - 1);
        }

        foreach (var pair in pairs)
        {
            var columns = pairColumns[pair.Kind];
            WriteReductionFormulas(worksheet, totalRow, columns.amount, columns.rate,
                stageColumns[pair.Earlier], stageColumns[pair.Later]);
        }

        var numericEnd = noteColumn - 1;
        if (numericEnd >= 3)
        {
            worksheet.Range(4, 3, totalRow, numericEnd).Style.NumberFormat.Format = NumberFormat;
        }

        foreach (var pair in pairs)
        {
            var columns = pairColumns[pair.Kind];
            worksheet.Range(4, columns.rate, totalRow, columns.rate).Style.NumberFormat.Format = PercentFormat;
            WorksheetFormatting.AddSignConditionalFormats(worksheet.Range(4, columns.amount, totalRow, columns.rate));
        }

        WorksheetFormatting.StyleSummaryRow(worksheet.Range(totalRow, 1, totalRow, noteColumn));
        WorksheetFormatting.ApplyBorders(worksheet.Range(3, 1, totalRow, noteColumn));
        worksheet.Range(1, 1, totalRow, noteColumn).Style.Font.FontName = "宋体";
        worksheet.Range(1, 1, totalRow, noteColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Range(3, 1, totalRow, noteColumn).Style.Alignment.WrapText = true;
        worksheet.Column(1).Width = 8;
        worksheet.Column(2).Width = 38;
        worksheet.Columns(3, noteColumn - 1).Width = 22;
        worksheet.Column(noteColumn).Width = 28;
        worksheet.SheetView.FreezeRows(3);
        WorksheetFormatting.ConfigurePrint(worksheet, totalRow, noteColumn);
    }

    private static void WriteSumOrZero(IXLCell cell, int column, int firstDataRow, int lastDataRow)
    {
        if (lastDataRow >= firstDataRow)
        {
            var letter = cell.Worksheet.Column(column).ColumnLetter();
            cell.FormulaA1 = $"SUM({letter}{firstDataRow}:{letter}{lastDataRow})";
        }
        else
        {
            cell.Value = 0m;
        }
    }

    private static void WriteReductionFormulas(
        IXLWorksheet worksheet,
        int row,
        int amountColumn,
        int rateColumn,
        int earlierColumn,
        int laterColumn)
    {
        var earlier = worksheet.Cell(row, earlierColumn).Address.ToStringRelative();
        var later = worksheet.Cell(row, laterColumn).Address.ToStringRelative();
        var amount = worksheet.Cell(row, amountColumn).Address.ToStringRelative();
        worksheet.Cell(row, amountColumn).FormulaA1 = $"{earlier}-{later}";
        worksheet.Cell(row, rateColumn).FormulaA1 =
            $"IF({earlier}=0,IF({later}=0,0,SIGN({amount})),{amount}/ABS({earlier}))";
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
}
