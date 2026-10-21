using ClosedXML.Excel;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class OpenSourceTemplateTests
{
    [Fact]
    public void Public_report_template_contains_only_one_blank_summary_sheet()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "ReportTemplate.xlsx");

        using var workbook = new XLWorkbook(templatePath);
        var worksheet = Assert.Single(workbook.Worksheets);

        Assert.Equal("汇总表", worksheet.Name);
        Assert.Null(worksheet.RangeUsed(XLCellsUsedOptions.All));
    }
}
