using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class AnalysisReportBuilder
{
    private const string AmountFormat = "#,##0.00;-#,##0.00;0.00";
    private const string RateFormat = "0.00%;-0.00%;0.00%";

    public static void Build(XLWorkbook workbook, ComparisonSession session)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(session);

        var pairs = ComparisonPairs.OutputFor(session.Request);
        foreach (var pair in pairs)
        {
            var prefix = pairs.Count == 1 ? string.Empty : pair.Later == WorkbookRole.InitialReview ? "初审" : "终审";
            BuildOverview(workbook.Worksheets.Add(prefix.Length == 0 ? "审减概况" : prefix + "概况"), session, pair);
            BuildDetail(workbook.Worksheets.Add(prefix + "扣减项明细"), session, pair);
        }
    }

    private static void BuildOverview(IXLWorksheet sheet, ComparisonSession session, StagePair pair)
    {
        var stageName = pair.Later == WorkbookRole.InitialReview ? "初审" : "终审";
        sheet.Cell("A1").Value = $"{stageName}工程量审减概况（含税总造价）";
        sheet.Range("A1:G1").Merge();
        WorksheetFormatting.ApplyTitle(sheet.Range("A1:G1"));
        sheet.Cell("A2").Value = $"工程名称：{session.Submission.ProjectName}";
        sheet.Range("A2:G2").Merge();

        sheet.Cell("A4").Value = "送审含税总造价";
        sheet.Cell("C4").Value = $"{stageName}含税总造价";
        sheet.Cell("A5").Value = "净审减额";
        sheet.Cell("C5").Value = "审减率";
        WorksheetFormatting.StyleHeader(sheet.Range("A4:A5"), WorksheetFormatting.BaseHeaderColor);
        WorksheetFormatting.StyleHeader(sheet.Range("C4:C5"), WorksheetFormatting.BaseHeaderColor);

        string[] headers = ["序号", "单位工程名称", "送审含税造价", $"{stageName}含税造价", "净审减额", "审减率", "备注"];
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(7, column).Value = headers[column - 1];
        }
        WorksheetFormatting.StyleHeader(sheet.Range("A7:G7"), WorksheetFormatting.DifferenceHeaderColor);

        var row = 8;
        foreach (var summary in session.SummaryRows)
        {
            sheet.Cell(row, 1).Value = summary.SubmissionSequence ?? (row - 7).ToString(System.Globalization.CultureInfo.InvariantCulture);
            sheet.Cell(row, 2).Value = summary.Name;
            sheet.Cell(row, 3).Value = summary.StageAmounts.GetValueOrDefault(WorkbookRole.Submission);
            sheet.Cell(row, 4).Value = summary.StageAmounts.GetValueOrDefault(pair.Later);
            WriteDifference(sheet, row);
            WorksheetFormatting.WriteOptionalText(sheet.Cell(row, 7), summary.Note);
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 2).Value = "合计";
        WriteSumOrZero(sheet.Cell(totalRow, 3), "C", 8, totalRow - 1);
        WriteSumOrZero(sheet.Cell(totalRow, 4), "D", 8, totalRow - 1);
        WriteDifference(sheet, totalRow);
        sheet.Cell("B4").FormulaA1 = $"C{totalRow}";
        sheet.Cell("D4").FormulaA1 = $"D{totalRow}";
        sheet.Cell("B5").FormulaA1 = $"E{totalRow}";
        sheet.Cell("D5").FormulaA1 = $"F{totalRow}";

        sheet.Range(8, 3, totalRow, 5).Style.NumberFormat.Format = AmountFormat;
        sheet.Range(8, 6, totalRow, 6).Style.NumberFormat.Format = RateFormat;
        sheet.Cell("B4").Style.NumberFormat.Format = AmountFormat;
        sheet.Cell("D4").Style.NumberFormat.Format = AmountFormat;
        sheet.Cell("B5").Style.NumberFormat.Format = AmountFormat;
        sheet.Cell("D5").Style.NumberFormat.Format = RateFormat;
        WorksheetFormatting.AddSignConditionalFormats(sheet.Range(8, 5, totalRow, 6));
        WorksheetFormatting.AddSignConditionalFormats(sheet.Cell("B5").AsRange());
        WorksheetFormatting.AddSignConditionalFormats(sheet.Cell("D5").AsRange());
        WorksheetFormatting.StyleSummaryRow(sheet.Range(totalRow, 1, totalRow, 7));
        WorksheetFormatting.ApplyBorders(sheet.Range(7, 1, totalRow, 7));
        sheet.Range(1, 1, totalRow, 7).Style.Font.FontName = "宋体";
        sheet.Column(1).Width = 8;
        sheet.Column(2).Width = 38;
        sheet.Columns(3, 6).Width = 21;
        sheet.Column(7).Width = 30;
        sheet.SheetView.FreezeRows(7);
        WorksheetFormatting.ConfigurePrint(sheet, totalRow, 7);
    }

    private static void BuildDetail(IXLWorksheet sheet, ComparisonSession session, StagePair pair)
    {
        var stageName = pair.Later == WorkbookRole.InitialReview ? "初审" : "终审";
        sheet.Cell("A1").Value = $"{stageName}扣减项明细（不含税合价）";
        sheet.Range("A1:H1").Merge();
        WorksheetFormatting.ApplyTitle(sheet.Range("A1:H1"));
        sheet.Cell("A2").Value = $"工程名称：{session.Submission.ProjectName}";
        sheet.Range("A2:H2").Merge();
        string[] headers = ["序号", "项目名称", "计量单位", "审减金额", "审增金额", "净扣减额", "占净扣减比", "名称核查（特征相同）"];
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(4, column).Value = headers[column - 1];
        }
        WorksheetFormatting.StyleHeader(sheet.Range("A4:H4"), WorksheetFormatting.DifferenceHeaderColor);

        var groups = Aggregate(session, pair);
        var totalRow = groups.Count + 5;
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var row = index + 5;
            sheet.Cell(row, 1).Value = index + 1;
            sheet.Cell(row, 2).Value = group.Name;
            sheet.Cell(row, 3).Value = group.Unit;
            sheet.Cell(row, 4).Value = group.Reduction;
            sheet.Cell(row, 5).Value = group.Increase;
            sheet.Cell(row, 6).FormulaA1 = $"D{row}-E{row}";
            sheet.Cell(row, 7).FormulaA1 = $"IF($F${totalRow}=0,0,F{row}/$F${totalRow})";
            if (group.OtherNames.Count > 0)
            {
                sheet.Cell(row, 8).Value = "疑似同类：" + string.Join("、", group.OtherNames);
            }
        }

        sheet.Cell(totalRow, 2).Value = "合计";
        WriteSumOrZero(sheet.Cell(totalRow, 4), "D", 5, totalRow - 1);
        WriteSumOrZero(sheet.Cell(totalRow, 5), "E", 5, totalRow - 1);
        sheet.Cell(totalRow, 6).FormulaA1 = $"D{totalRow}-E{totalRow}";
        sheet.Cell(totalRow, 7).FormulaA1 = $"IF(F{totalRow}=0,0,1)";

        sheet.Range(5, 4, totalRow, 6).Style.NumberFormat.Format = AmountFormat;
        sheet.Range(5, 7, totalRow, 7).Style.NumberFormat.Format = RateFormat;
        WorksheetFormatting.AddSignConditionalFormats(sheet.Range(5, 4, totalRow, 7));
        WorksheetFormatting.StyleSummaryRow(sheet.Range(totalRow, 1, totalRow, 8));
        WorksheetFormatting.ApplyBorders(sheet.Range(4, 1, totalRow, 8));
        sheet.Range(1, 1, totalRow, 8).Style.Font.FontName = "宋体";
        sheet.Range(4, 1, totalRow, 8).Style.Alignment.WrapText = true;
        sheet.Column(1).Width = 8;
        sheet.Column(2).Width = 34;
        sheet.Column(3).Width = 13;
        sheet.Columns(4, 7).Width = 20;
        sheet.Column(8).Width = 44;
        sheet.SheetView.FreezeRows(4);
        WorksheetFormatting.ConfigurePrint(sheet, totalRow, 8);
    }

    private static IReadOnlyList<DetailGroup> Aggregate(ComparisonSession session, StagePair pair)
    {
        var groups = new Dictionary<(string Name, string Unit), DetailGroup>();
        foreach (var row in session.UnitComparisons.SelectMany(unit => unit.Rows))
        {
            var difference = row.Total(WorkbookRole.Submission) - row.Total(pair.Later);
            if (difference == 0m)
            {
                continue;
            }

            var item = row.Submission ?? row.Item(pair.Later);
            if (item is null)
            {
                continue;
            }

            var key = (Name: TextNormalizer.Normalize(item.Name), Unit: TextNormalizer.Normalize(item.Unit));
            if (!groups.TryGetValue(key, out var group))
            {
                group = new DetailGroup(item.Name, item.Unit, key.Name, key.Unit);
                groups.Add(key, group);
            }

            if (difference > 0m)
            {
                group.Reduction += difference;
            }
            else
            {
                group.Increase -= difference;
            }

            var feature = TextNormalizer.Normalize(item.Feature);
            if (feature.Length > 0 && key.Unit.Length > 0)
            {
                group.Features.Add(feature);
            }
        }

        var byDescription = new Dictionary<(string Feature, string Unit), HashSet<DetailGroup>>();
        foreach (var group in groups.Values)
        {
            foreach (var feature in group.Features)
            {
                var key = (feature, group.NormalizedUnit);
                if (!byDescription.TryGetValue(key, out var matches))
                {
                    matches = [];
                    byDescription.Add(key, matches);
                }
                matches.Add(group);
            }
        }

        foreach (var matches in byDescription.Values)
        {
            foreach (var group in matches)
            {
                foreach (var other in matches.Where(other => other.NormalizedName != group.NormalizedName))
                {
                    group.OtherNames.Add(other.Name);
                }
            }
        }

        return groups.Values
            .OrderByDescending(group => group.Reduction + group.Increase)
            .ThenBy(group => group.NormalizedName, StringComparer.Ordinal)
            .ToList();
    }

    private static void WriteDifference(IXLWorksheet sheet, int row)
    {
        sheet.Cell(row, 5).FormulaA1 = $"C{row}-D{row}";
        sheet.Cell(row, 6).FormulaA1 = $"IF(C{row}=0,IF(D{row}=0,0,SIGN(E{row})),E{row}/ABS(C{row}))";
    }

    private static void WriteSumOrZero(IXLCell cell, string letter, int start, int end)
    {
        if (end < start)
        {
            cell.Value = 0m;
            return;
        }
        cell.FormulaA1 = $"SUM({letter}{start}:{letter}{end})";
    }

    private sealed class DetailGroup(string name, string unit, string normalizedName, string normalizedUnit)
    {
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public string NormalizedName { get; } = normalizedName;
        public string NormalizedUnit { get; } = normalizedUnit;
        public decimal Reduction { get; set; }
        public decimal Increase { get; set; }
        public HashSet<string> Features { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> OtherNames { get; } = new(StringComparer.Ordinal);
    }
}
