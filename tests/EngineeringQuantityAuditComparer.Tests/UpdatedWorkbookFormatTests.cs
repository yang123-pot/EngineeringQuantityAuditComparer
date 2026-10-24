using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class UpdatedWorkbookFormatTests
{
    [Fact]
    public async Task Imports_updated_summary_and_amount_bearing_section_rows()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "（送审）示例.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var summary = workbook.Worksheets.Add("单项工程汇总");
                summary.Cell(1, 1).Value = "表E.1.1-2   单项工程汇总表";
                summary.Cell(2, 1).Value = "单项工程名称：安装工程";
                summary.Cell(3, 1).Value = "序号";
                summary.Cell(3, 2).Value = "项目内容";
                summary.Cell(3, 3).Value = "金额(元)";
                summary.Cell(5, 1).Value = 1;
                summary.Cell(5, 2).Value = "研发楼安装";
                summary.Cell(5, 3).Value = 123m;
                summary.Cell(6, 1).Value = 2;
                summary.Cell(6, 2).Value = "综合楼安装";
                summary.Cell(6, 3).Value = 45m;
                summary.Cell(7, 1).Value = "合  计";
                summary.Cell(7, 3).Value = 168m;

                var detail = workbook.Worksheets.Add("清单页");
                detail.Cell(1, 1).Value = "表E.2.1   分部分项工程项目清单计价表";
                detail.Cell(2, 1).Value = "工程名称：研发楼安装";
                string[] headers = ["序号", "项目编码", "项目名称", "项目特征描述", "计量 单位", "工程量", "金额（元）"];
                for (var column = 1; column <= headers.Length; column++)
                {
                    detail.Cell(3, column).Value = headers[column - 1];
                }
                detail.Cell(4, 7).Value = "综合单价";
                detail.Cell(4, 8).Value = "合价";
                detail.Cell(5, 3).Value = "电气工程";
                detail.Cell(5, 8).Value = 100m;
                detail.Cell(6, 1).Value = 1;
                detail.Cell(6, 2).Value = "A01";
                detail.Cell(6, 3).Value = "电缆";
                detail.Cell(6, 4).Value = "铜芯";
                detail.Cell(6, 5).Value = "m";
                detail.Cell(6, 6).Value = 10m;
                detail.Cell(6, 7).Value = 10m;
                detail.Cell(6, 8).Value = 100m;
                detail.Cell(7, 3).Value = "给排水";
                detail.Cell(7, 8).Value = 20m;
                detail.Cell(8, 1).Value = 2;
                detail.Cell(8, 3).Value = "水管";
                detail.Cell(8, 5).Value = "m";
                detail.Cell(8, 6).Value = 2m;
                detail.Cell(8, 7).Value = 10m;
                detail.Cell(8, 8).Value = 20m;
                detail.Cell(9, 1).Value = "合计";
                detail.Cell(9, 8).Value = 120m;
                workbook.SaveAs(path);
            }

            var parsed = await new WorkbookImporter().ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            Assert.Equal("示例", parsed.ProjectName);
            Assert.Equal(["研发楼安装", "综合楼安装"], parsed.TaxInclusiveSummary.Select(x => x.Name));
            Assert.Equal([123m, 45m], parsed.TaxInclusiveSummary.Select(x => x.Amount));
            var unit = Assert.Single(parsed.UnitProjects);
            Assert.Equal(["电缆", "水管"], unit.Items.Select(x => x.Name));
            Assert.Equal("", unit.Items[1].Code);
            Assert.Equal("", unit.Items[1].Feature);
            Assert.Equal(["整个工程", "电气工程", "给排水"], unit.Sections.Select(x => x.Title));
            Assert.Equal([120m, 100m, 20m], unit.Sections.Select(x => x.Total));
            Assert.Equal([5, 7], unit.Items.Select(x => x.SourceRowIndex));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Imports_bill_items_with_blank_code_or_description()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 20m)],
                [new("清单页", "研发楼", [
                    new BillItem("1", "", "支架", "", "m", 1m, 1m, 10m, 10m),
                    new BillItem("2", "A02", "接线盒", "", "个", 1m, 1m, 10m, 10m)
                ])]);
            var parsed = await new WorkbookImporter().ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            Assert.Equal(["支架", "接线盒"], Assert.Single(parsed.UnitProjects).Items.Select(x => x.Name));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
