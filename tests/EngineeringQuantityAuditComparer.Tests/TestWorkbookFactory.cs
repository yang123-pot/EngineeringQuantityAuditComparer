using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

internal sealed record TestSection(
    string Title,
    int Level,
    decimal Total,
    IReadOnlyList<BillItem> DirectItems,
    IReadOnlyList<TestSection> Children);

internal sealed record TestUnitSheet(string TabName, string EngineeringName, IReadOnlyList<BillItem> Items)
{
    public IReadOnlyList<TestSection> Sections { get; init; } = [];
    public decimal? RootTotal { get; init; }
    public bool AppendUnmatchedSectionSubtotal { get; init; }
    public bool OmitLastSectionSubtotal { get; init; }
    public string? MeasureTitle { get; init; }
    public IReadOnlyList<BillItem> MeasureItems { get; init; } = [];
    public decimal? MeasureTotal { get; init; }
    public bool UseAdjacentEngineeringNameCell { get; init; }
}

internal static class TestWorkbookFactory
{
    public static string Create(
        string directory,
        string fileName,
        IReadOnlyList<TaxInclusiveSummaryItem> summary,
        IReadOnlyList<TestUnitSheet> units)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var workbook = new XLWorkbook();
        WriteSummary(workbook, summary);
        foreach (var unit in units)
        {
            WriteUnit(workbook, unit);
        }

        workbook.SaveAs(path);
        return path;
    }

    public static string CreateWithoutEngineeringName(string directory, string fileName, string tabName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var workbook = new XLWorkbook();
        WriteSummary(workbook, [new TaxInclusiveSummaryItem(tabName, 0m)]);
        var sheet = workbook.Worksheets.Add(tabName);
        sheet.Cell(1, 1).Value = "分部分项工程和单价措施项目清单与计价表";
        WriteBillHeaders(sheet, 2);
        workbook.SaveAs(path);
        return path;
    }

    public static string CreateWithBillHeaders(
        string directory,
        string fileName,
        string tabName,
        string engineeringName,
        IReadOnlyList<string> headers,
        BillItem? firstItem = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var workbook = new XLWorkbook();
        WriteSummary(workbook, [new TaxInclusiveSummaryItem(engineeringName, 0m)]);
        var sheet = workbook.Worksheets.Add(tabName);
        sheet.Cell(1, 1).Value = "分部分项工程和单价措施项目清单与计价表";
        sheet.Cell(2, 1).Value = $"工程名称：{engineeringName}";
        for (var index = 0; index < headers.Count; index++)
        {
            sheet.Cell(3, index + 1).Value = headers[index];
        }

        if (firstItem is not null)
        {
            var row = 4;
            WriteItem(sheet, ref row, firstItem);
        }

        workbook.SaveAs(path);
        return path;
    }

    private static void WriteSummary(XLWorkbook workbook, IReadOnlyList<TaxInclusiveSummaryItem> summary)
    {
        var sheet = workbook.Worksheets.Add("单项工程汇总");
        sheet.Cell(1, 1).Value = "单项工程投标报价汇总表";
        sheet.Cell(2, 1).Value = "工程名称：测试项目";
        sheet.Cell(3, 1).Value = "序号";
        sheet.Cell(3, 2).Value = "单位工程名称";
        sheet.Cell(3, 3).Value = "含税总造价";
        for (var index = 0; index < summary.Count; index++)
        {
            var row = index + 4;
            sheet.Cell(row, 1).Value = index + 1;
            sheet.Cell(row, 2).Value = summary[index].Name;
            sheet.Cell(row, 3).Value = summary[index].Amount;
        }
    }

    private static void WriteUnit(XLWorkbook workbook, TestUnitSheet unit)
    {
        var sheet = workbook.Worksheets.Add(unit.TabName);
        sheet.Cell(1, 1).Value = "分部分项工程和单价措施项目清单与计价表";
        if (unit.UseAdjacentEngineeringNameCell)
        {
            sheet.Cell(2, 1).Value = "工程名称：";
            sheet.Cell(2, 2).Value = unit.EngineeringName;
        }
        else
        {
            sheet.Cell(2, 1).Value = $"工程名称：{unit.EngineeringName}";
        }
        WriteBillHeaders(sheet, 3);
        var row = 4;
        if (unit.Sections.Count == 0)
        {
            foreach (var item in unit.Items)
            {
                WriteItem(sheet, ref row, item);
            }

            if (unit.AppendUnmatchedSectionSubtotal)
            {
                sheet.Cell(row, 3).Value = "分部小计";
                sheet.Cell(row, 8).Value = unit.Items.Sum(x => x.Total ?? 0m);
                row++;
            }

            if (unit.RootTotal is null)
            {
                return;
            }

            sheet.Cell(row, 3).Value = "分部分项合计";
            sheet.Cell(row, 8).Value = unit.RootTotal.Value;
            row++;
            if (!string.IsNullOrWhiteSpace(unit.MeasureTitle))
            {
                sheet.Cell(row, 3).Value = unit.MeasureTitle;
                row++;
            }

            foreach (var item in unit.MeasureItems)
            {
                WriteItem(sheet, ref row, item);
            }

            if (unit.MeasureTotal is not null)
            {
                sheet.Cell(row, 3).Value = "单价措施合计";
                sheet.Cell(row, 8).Value = unit.MeasureTotal.Value;
            }

            return;
        }

        for (var index = 0; index < unit.Sections.Count; index++)
        {
            var omitSubtotal = unit.OmitLastSectionSubtotal && index == unit.Sections.Count - 1;
            WriteSection(sheet, ref row, unit.Sections[index], expectedLevel: 1, omitSubtotal);
        }

        if (unit.AppendUnmatchedSectionSubtotal)
        {
            sheet.Cell(row, 3).Value = "分部小计";
            sheet.Cell(row, 8).Value = unit.Sections.Sum(x => x.Total);
            row++;
        }

        sheet.Cell(row, 3).Value = "分部分项合计";
        sheet.Cell(row, 8).Value = unit.RootTotal ?? 0m;
    }

    private static void WriteBillHeaders(IXLWorksheet sheet, int row)
    {
        sheet.Cell(row, 1).Value = "序号";
        sheet.Cell(row, 2).Value = "项目编码";
        sheet.Cell(row, 3).Value = "项目名称";
        sheet.Cell(row, 4).Value = "项目特征描述";
        sheet.Cell(row, 5).Value = "计量单位";
        sheet.Cell(row, 6).Value = "工程量";
        sheet.Cell(row, 7).Value = "综合单价";
        sheet.Cell(row, 8).Value = "合价";
    }

    private static void WriteSection(
        IXLWorksheet sheet,
        ref int row,
        TestSection section,
        int expectedLevel,
        bool omitSubtotal)
    {
        if (section.Level != expectedLevel)
        {
            throw new ArgumentException(
                $"测试小标题“{section.Title}”的 Level={section.Level}，递归层级应为 {expectedLevel}。",
                nameof(section));
        }

        sheet.Cell(row, 3).Value = section.Title;
        row++;
        foreach (var item in section.DirectItems)
        {
            WriteItem(sheet, ref row, item);
        }

        foreach (var child in section.Children)
        {
            WriteSection(sheet, ref row, child, expectedLevel + 1, omitSubtotal: false);
        }

        if (!omitSubtotal)
        {
            sheet.Cell(row, 3).Value = "分部小计";
            sheet.Cell(row, 8).Value = section.Total;
            row++;
        }
    }

    private static void WriteItem(IXLWorksheet sheet, ref int row, BillItem item)
    {
        sheet.Cell(row, 1).Value = item.SourceSequence ?? string.Empty;
        sheet.Cell(row, 2).Value = item.Code;
        sheet.Cell(row, 3).Value = item.Name;
        sheet.Cell(row, 4).Value = item.Feature;
        sheet.Cell(row, 5).Value = item.Unit;
        sheet.Cell(row, 6).Value = item.Quantity ?? item.ContractQuantity ?? 0m;
        sheet.Cell(row, 7).Value = item.UnitPrice ?? 0m;
        sheet.Cell(row, 8).Value = item.Total ?? 0m;
        row++;
    }
}
