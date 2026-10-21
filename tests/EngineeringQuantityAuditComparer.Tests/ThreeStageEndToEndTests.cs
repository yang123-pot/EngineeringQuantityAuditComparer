using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.App.ViewModels;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class ThreeStageEndToEndTests
{
    [Theory]
    [InlineData(true, false, 1, 1)]
    [InlineData(false, true, 1, 1)]
    [InlineData(true, true, 3, 2)]
    public async Task Parses_compares_and_exports_each_supported_mode(
        bool includeInitial,
        bool includeFinal,
        int expectedInternalPairCount,
        int expectedOutputScopeCount)
    {
        var modeName = includeInitial && includeFinal
            ? "送审-初审-终审"
            : includeInitial ? "送审-初审" : "送审-终审";
        var requestedOutputRoot = Environment.GetEnvironmentVariable("AUDIT_GOLDEN_OUTPUT_DIR");
        var preserveOutput = !string.IsNullOrWhiteSpace(requestedOutputRoot);
        var directory = preserveOutput
            ? Path.Combine(Path.GetFullPath(requestedOutputRoot!), $"{modeName}-{Guid.NewGuid():N}")
            : Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var commonSubmission = new BillItem("1", "A01", "电缆", "铜芯", "m", 1m, 1m, 100m, 100m);
            var commonInitial = commonSubmission with { SourceSequence = null, Quantity = 0m, UnitPrice = 0m, Total = 0m };
            var commonFinal = commonSubmission with { SourceSequence = null, Quantity = 1m, UnitPrice = 90m, Total = 90m };
            var deleted = new BillItem("2", "DEL", "送审后删除", "", "项", 1m, 1m, 20m, 20m);
            var initialOnly = new BillItem(null, "INIT", "初审新增", "", "项", 1m, 1m, 10m, 10m);
            var finalOnly = new BillItem(null, "FINAL", "终审新增", "", "项", 1m, 1m, 15m, 15m);
            var zero = new BillItem("3", "ZERO", "零值项", "", "项", 0m, 0m, 0m, 0m);

            static TestUnitSheet Sheet(string tab, decimal total, params BillItem[] items) =>
                new(tab, "研发楼安装", [])
                {
                    RootTotal = total,
                    Sections = [new("安装工程", 1, total, items, [])]
                };

            var submissionPath = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼安装", 120m)],
                [Sheet("送审批注页签", 120m, commonSubmission, deleted, zero)]);
            var initialPath = TestWorkbookFactory.Create(directory, "初审.xlsx", [new("研发楼安装", 10m)],
                [Sheet("初审批注页签", 10m, commonInitial, initialOnly, zero with { SourceSequence = null })]);
            var finalPath = TestWorkbookFactory.Create(directory, "终审.xlsx", [new("研发楼安装", 105m)],
                [Sheet("终审批注页签", 105m, commonFinal, finalOnly, zero with { SourceSequence = null })]);

            var viewModel = new MainViewModel(new WorkbookImporter(), new ComparisonEngine(), new ReportExporter())
            {
                SubmissionPath = submissionPath,
                InitialReviewPath = includeInitial ? initialPath : string.Empty,
                FinalReviewPath = includeFinal ? finalPath : string.Empty
            };

            await viewModel.AnalyzeAsync();

            Assert.Equal(2, viewModel.CurrentStep);
            Assert.NotEmpty(viewModel.UnitMatchRows);
            Assert.Equal(includeInitial, viewModel.UnitMatchRows.Any(x => x.InitialReview is not null));
            Assert.Equal(includeFinal, viewModel.UnitMatchRows.Any(x => x.FinalReview is not null));
            Assert.DoesNotContain(viewModel.UnitMatchRows, x => x.IsBlocking);

            viewModel.ConfirmAllExceptions();
            Assert.Equal(3, viewModel.CurrentStep);
            var session = Assert.IsType<ComparisonSession>(viewModel.Session);
            Assert.Equal(expectedInternalPairCount, session.ActiveDifferences.Count);

            var output = Path.Combine(directory, $"{modeName}-黄金样例.xlsx");
            var template = Path.Combine(AppContext.BaseDirectory, "Assets", "ReportTemplate.xlsx");
            await viewModel.ExportAsync(output, template);
            Assert.Equal($"已导出：{output}", viewModel.StatusMessage);

            using var workbook = new XLWorkbook(output);
            Assert.Contains(workbook.Worksheets, x => x.Name == "研发楼安装");
            Assert.Equal(session.UnitComparisons.Count + 3, workbook.Worksheets.Count);
            Assert.Equal("绝对差额 Top 20看板", workbook.Worksheet(1).Name);
            Assert.Equal("同类清单审增审减汇总看板", workbook.Worksheet(2).Name);
            Assert.Equal("汇总表", workbook.Worksheet(3).Name);
            Assert.DoesNotContain(workbook.Worksheets, x => x.Name == "分析看板");

            Assert.Equal(expectedOutputScopeCount,
                workbook.Worksheet("绝对差额 Top 20看板").Column(1).CellsUsed().Skip(3)
                    .Select(x => x.GetString()).Distinct().Count());
            Assert.Equal(expectedOutputScopeCount,
                workbook.Worksheet("同类清单审增审减汇总看板").Row(3).CellsUsed().Count(x =>
                    x.GetString().Contains("（", StringComparison.Ordinal)));
            Assert.Equal("'研发楼安装'!A1",
                workbook.Worksheet("汇总表").Cell("B4").GetHyperlink().InternalAddress);
            Assert.DoesNotContain(workbook.Worksheets.SelectMany(x => x.CellsUsed(c => c.HasFormula)),
                x => x.FormulaA1.Contains("#REF!", StringComparison.OrdinalIgnoreCase));

            var detail = workbook.Worksheet("研发楼安装");
            int Row(string name) => detail.Column(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == name).Address.RowNumber;
            int TotalColumn(string stage) => detail.Row(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == stage).Address.ColumnNumber + 2;

            var rootRow = Row("整个工程");
            var sectionRow = Row("安装工程");
            var commonRow = detail.Column(2).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == "A01").Address.RowNumber;
            Assert.Equal("1", detail.Cell(commonRow, 1).GetString());
            Assert.Equal(120m, detail.Cell(rootRow, TotalColumn("送审")).GetValue<decimal>());
            Assert.Equal(120m, detail.Cell(sectionRow, TotalColumn("送审")).GetValue<decimal>());
            if (includeInitial)
            {
                Assert.Equal(10m, detail.Cell(rootRow, TotalColumn("初审")).GetValue<decimal>());
            }

            if (includeFinal)
            {
                Assert.Equal(105m, detail.Cell(rootRow, TotalColumn("终审")).GetValue<decimal>());
            }

            Assert.Equal(XLOutlineSummaryVLocation.Top, detail.Outline.SummaryVLocation);
            Assert.Equal(0, detail.Row(rootRow).OutlineLevel);
            Assert.Equal(1, detail.Row(sectionRow).OutlineLevel);
            Assert.Equal(2, detail.Row(commonRow).OutlineLevel);
            Assert.All(detail.Rows(rootRow, detail.CellsUsed(XLCellsUsedOptions.Contents).Max(x => x.Address.RowNumber)),
                row => Assert.False(row.IsHidden));

            var topTwenty = workbook.Worksheet("绝对差额 Top 20看板");
            var sequenceCell = topTwenty.Column(4).CellsUsed().First(x => x.HasHyperlink);
            var itemName = topTwenty.Cell(sequenceCell.Address.RowNumber, 5).GetString();
            var linkedDetailRow = detail.Column(3).CellsUsed(XLCellsUsedOptions.Contents)
                .Single(x => x.GetString() == itemName).Address.RowNumber;
            Assert.Equal($"'研发楼安装'!A{linkedDetailRow}", sequenceCell.GetHyperlink().InternalAddress);

            var comment = session.UnitComparisons.Single().Rows
                .Single(x => x.Item(WorkbookRole.Submission)?.Code == "A01").AutomaticComment;
            if (includeInitial)
            {
                Assert.Contains("初审未给", comment, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.DoesNotContain("初审", comment, StringComparison.OrdinalIgnoreCase);
            }

            var similarItems = workbook.Worksheet("同类清单审增审减汇总看板");
            Assert.DoesNotContain(similarItems.CellsUsed(XLCellsUsedOptions.Contents),
                x => x.GetString() == "整个工程");
        }
        finally
        {
            if (!preserveOutput)
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
