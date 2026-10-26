using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class AnalysisReportBuilder
{
    private const string AmountFormat = "#,##0.00;-#,##0.00;0.00";
    private const string QuantityFormat = "#,##0.00#;-#,##0.00#;0.00";
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
        var report = Aggregate(session, pair);
        var buildings = report.Buildings;
        var blockWidth = buildings.Count + 1;
        var reducedQuantity = 4;
        var increasedQuantity = reducedQuantity + blockWidth;
        var reducedAmount = increasedQuantity + blockWidth;
        var increasedAmount = reducedAmount + blockWidth;
        var netAmount = increasedAmount + blockWidth;
        var rate = netAmount + 1;
        var nameCheck = rate + 1;
        var firstDetailRow = 6;
        var totalRow = firstDetailRow + report.Groups.Count;

        sheet.Cell("A1").Value = $"{stageName}扣减项明细（不含税合价）";
        sheet.Range(1, 1, 1, nameCheck).Merge();
        WorksheetFormatting.ApplyTitle(sheet.Range(1, 1, 1, nameCheck));
        sheet.Range(1, 1, 1, nameCheck).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Cell("A2").Value = $"工程名称：{session.Submission.ProjectName}";
        sheet.Range(2, 1, 2, nameCheck).Merge();
        sheet.Cell("A3").Value = $"工程量差＝送审－{stageName}，核减与核增分别列示；各楼栋列之和等于对应合计。金额按分部分项清单不含税合价差统计，无法可靠识别楼栋时单列待核查。";
        sheet.Range(3, 1, 3, nameCheck).Merge();
        sheet.Range(3, 1, 3, nameCheck).Style.Alignment.WrapText = true;
        sheet.Row(3).Height = 30;

        string[] fixedHeaders = ["序号", "项目名称", "计量单位"];
        for (var column = 1; column <= fixedHeaders.Length; column++)
        {
            sheet.Cell(4, column).Value = fixedHeaders[column - 1];
            sheet.Range(4, column, 5, column).Merge();
        }
        WriteMetricHeader(reducedQuantity, "核减工程量", "合计核减工程量");
        WriteMetricHeader(increasedQuantity, "核增工程量", "合计核增工程量");
        WriteMetricHeader(reducedAmount, "审减金额（不含税合价）", "合计审减金额");
        WriteMetricHeader(increasedAmount, "审增金额（不含税合价）", "合计审增金额");
        sheet.Cell(4, netAmount).Value = "净扣减额";
        sheet.Cell(4, rate).Value = "占净扣减比";
        sheet.Cell(4, nameCheck).Value = "名称核查（特征相同）";
        foreach (var column in new[] { netAmount, rate, nameCheck })
        {
            sheet.Range(4, column, 5, column).Merge();
        }
        WorksheetFormatting.StyleHeader(sheet.Range(4, 1, 5, nameCheck), WorksheetFormatting.DifferenceHeaderColor);

        for (var index = 0; index < report.Groups.Count; index++)
        {
            var group = report.Groups[index];
            var row = index + firstDetailRow;
            sheet.Cell(row, 1).Value = index + 1;
            sheet.Cell(row, 2).Value = group.Name;
            sheet.Cell(row, 3).Value = group.Unit;
            for (var buildingIndex = 0; buildingIndex < buildings.Count; buildingIndex++)
            {
                if (!group.ByBuilding.TryGetValue(buildings[buildingIndex], out var values)) continue;
                WriteNonZero(sheet.Cell(row, reducedQuantity + buildingIndex + 1), values.ReducedQuantity);
                WriteNonZero(sheet.Cell(row, increasedQuantity + buildingIndex + 1), values.IncreasedQuantity);
                WriteNonZero(sheet.Cell(row, reducedAmount + buildingIndex + 1), values.Reduction);
                WriteNonZero(sheet.Cell(row, increasedAmount + buildingIndex + 1), values.Increase);
            }
            foreach (var start in new[] { reducedQuantity, increasedQuantity, reducedAmount, increasedAmount })
            {
                sheet.Cell(row, start).FormulaA1 = $"SUM({sheet.Cell(row, start + 1).Address}:{sheet.Cell(row, start + buildings.Count).Address})";
            }
            sheet.Cell(row, netAmount).FormulaA1 = $"{sheet.Cell(row, reducedAmount).Address}-{sheet.Cell(row, increasedAmount).Address}";
            sheet.Cell(row, rate).FormulaA1 = $"IF(${sheet.Cell(totalRow, netAmount).Address}=0,0,{sheet.Cell(row, netAmount).Address}/${sheet.Cell(totalRow, netAmount).Address})";
            if (group.OtherNames.Count > 0)
            {
                sheet.Cell(row, nameCheck).Value = "疑似同类：" + string.Join("、", group.OtherNames);
            }
        }

        sheet.Cell(totalRow, 2).Value = "合计";
        foreach (var start in new[] { reducedAmount, increasedAmount })
        {
            for (var offset = 0; offset <= buildings.Count; offset++)
            {
                var column = start + offset;
                WriteSumOrZero(sheet.Cell(totalRow, column), sheet.Column(column).ColumnLetter(), firstDetailRow, totalRow - 1);
            }
        }
        sheet.Cell(totalRow, netAmount).FormulaA1 = $"{sheet.Cell(totalRow, reducedAmount).Address}-{sheet.Cell(totalRow, increasedAmount).Address}";
        sheet.Cell(totalRow, rate).FormulaA1 = $"IF({sheet.Cell(totalRow, netAmount).Address}=0,0,1)";

        sheet.Range(firstDetailRow, reducedQuantity, totalRow, increasedQuantity + buildings.Count).Style.NumberFormat.Format = QuantityFormat;
        sheet.Range(firstDetailRow, reducedAmount, totalRow, netAmount).Style.NumberFormat.Format = AmountFormat;
        sheet.Range(firstDetailRow, rate, totalRow, rate).Style.NumberFormat.Format = RateFormat;
        AddPositiveColor(sheet.Range(firstDetailRow, reducedQuantity, totalRow, reducedQuantity + buildings.Count), true);
        AddPositiveColor(sheet.Range(firstDetailRow, reducedAmount, totalRow, reducedAmount + buildings.Count), true);
        AddPositiveColor(sheet.Range(firstDetailRow, increasedQuantity, totalRow, increasedQuantity + buildings.Count), false);
        AddPositiveColor(sheet.Range(firstDetailRow, increasedAmount, totalRow, increasedAmount + buildings.Count), false);
        WorksheetFormatting.AddSignConditionalFormats(sheet.Range(firstDetailRow, netAmount, totalRow, netAmount));
        WorksheetFormatting.StyleSummaryRow(sheet.Range(totalRow, 1, totalRow, nameCheck));
        WorksheetFormatting.ApplyBorders(sheet.Range(4, 1, totalRow, nameCheck));
        sheet.Range(1, 1, totalRow, nameCheck).Style.Font.FontName = "宋体";
        sheet.Range(4, 1, totalRow, nameCheck).Style.Alignment.WrapText = true;
        sheet.Column(1).Width = 8;
        sheet.Column(2).Width = 34;
        sheet.Column(3).Width = 13;
        sheet.Columns(reducedQuantity, increasedAmount + buildings.Count).Width = 18;
        sheet.Column(netAmount).Width = 20;
        sheet.Column(rate).Width = 16;
        sheet.Column(nameCheck).Width = 44;
        sheet.SheetView.FreezeRows(5);
        sheet.SheetView.FreezeColumns(3);
        WorksheetFormatting.ConfigurePrint(sheet, totalRow, nameCheck);
        sheet.PageSetup.PagesWide = Math.Max(1, (nameCheck + 11) / 12);

        void WriteMetricHeader(int start, string title, string totalLabel)
        {
            sheet.Cell(4, start).Value = title;
            sheet.Range(4, start, 4, start + buildings.Count).Merge();
            sheet.Cell(5, start).Value = totalLabel;
            for (var index = 0; index < buildings.Count; index++)
            {
                sheet.Cell(5, start + index + 1).Value = buildings[index];
            }
        }
    }

    private static DetailReportData Aggregate(ComparisonSession session, StagePair pair)
    {
        var groups = new Dictionary<(string Name, string Unit), DetailGroup>();
        var buildings = new List<string>();
        var seenBuildings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in session.UnitComparisons)
        {
            foreach (var row in unit.Rows)
            {
                var difference = row.Total(WorkbookRole.Submission) - row.Total(pair.Later);
                var quantityDifference = row.Quantity(WorkbookRole.Submission) - row.Quantity(pair.Later);
                if (difference == 0m && quantityDifference == 0m)
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

                var building = ReportBuildingAttribution.ForRow(unit, row, pair.Later);
                if (seenBuildings.Add(building)) buildings.Add(building);
                if (!group.ByBuilding.TryGetValue(building, out var values))
                {
                    values = new BuildingTotals();
                    group.ByBuilding.Add(building, values);
                }

                if (quantityDifference > 0m) values.ReducedQuantity += quantityDifference;
                if (quantityDifference < 0m) values.IncreasedQuantity -= quantityDifference;

                if (difference > 0m)
                {
                    values.Reduction += difference;
                }
                else
                {
                    values.Increase -= difference;
                }

                var feature = TextNormalizer.Normalize(item.Feature);
                if (feature.Length > 0 && key.Unit.Length > 0)
                {
                    group.Features.Add(feature);
                }
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

        var orderedGroups = groups.Values
            .OrderByDescending(group => group.ByBuilding.Values.Sum(x => x.Reduction + x.Increase))
            .ThenBy(group => group.NormalizedName, StringComparer.Ordinal)
            .ToList();
        return new DetailReportData(orderedGroups, buildings);
    }

    private static void WriteNonZero(IXLCell cell, decimal value)
    {
        if (value != 0m) cell.Value = value;
    }

    private static void AddPositiveColor(IXLRange range, bool isReduction)
    {
        var format = range.AddConditionalFormat().WhenGreaterThan(0);
        format.Fill.SetBackgroundColor(XLColor.FromHtml(isReduction ? "#F4CCCC" : "#E2F0D9"));
        format.Font.SetFontColor(XLColor.FromHtml(isReduction ? "#C00000" : "#006100"));
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

    private sealed record DetailReportData(IReadOnlyList<DetailGroup> Groups, IReadOnlyList<string> Buildings);

    private sealed class BuildingTotals
    {
        public decimal ReducedQuantity { get; set; }
        public decimal IncreasedQuantity { get; set; }
        public decimal Reduction { get; set; }
        public decimal Increase { get; set; }
    }

    private sealed class DetailGroup(string name, string unit, string normalizedName, string normalizedUnit)
    {
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public string NormalizedName { get; } = normalizedName;
        public string NormalizedUnit { get; } = normalizedUnit;
        public Dictionary<string, BuildingTotals> ByBuilding { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Features { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> OtherNames { get; } = new(StringComparer.Ordinal);
    }
}
