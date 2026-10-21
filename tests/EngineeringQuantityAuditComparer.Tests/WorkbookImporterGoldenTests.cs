using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class WorkbookImporterGoldenTests
{
    private readonly WorkbookImporter _importer = new();

    [Fact]
    public async Task Uses_the_in_sheet_engineering_name_instead_of_the_source_tab_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼安装", 100m)],
            [new("Sheet-001", "研发楼安装", [new("1", "A01", "电缆", "铜芯", "m", 1m, 1m, 10m, 10m)])]);
        try
        {
            var workbook = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            var unit = Assert.Single(workbook.UnitProjects);
            Assert.Equal("研发楼安装", unit.Name);
            Assert.Equal(1, unit.SourceSheetIndex);
            Assert.Equal("Sheet-001", unit.SourceSheetName);
            Assert.Equal(3, Assert.Single(unit.Items).SourceRowIndex);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Reads_engineering_name_from_an_explicit_label_and_adjacent_value_cell()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var unitSheet = new TestUnitSheet(
            "相邻标签页",
            "研发楼安装",
            [new BillItem("1", "A01", "电缆", "铜芯", "m", 1m, 1m, 10m, 10m)])
        {
            UseAdjacentEngineeringNameCell = true
        };
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼安装", 10m)], [unitSheet]);
        try
        {
            var workbook = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            Assert.Equal("研发楼安装", Assert.Single(workbook.UnitProjects).Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_a_detail_sheet_without_an_engineering_name_header()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = TestWorkbookFactory.CreateWithoutEngineeringName(directory, "终审.xlsx", "终审明细页");
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("工程名称", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_detail_item_text_that_only_contains_the_engineering_name_marker()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var item = new BillItem("1", "A", "工程名称标识牌", "", "项", 1m, 1m, 10m, 10m);
        var path = TestWorkbookFactory.Create(directory, "终审.xlsx", [new("研发楼", 10m)],
            [new("终审明细页", "", [item])]);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("工程名称", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Parses_nested_title_pairs_and_direct_item_membership_from_xlsx()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var item = new BillItem("1", "A", "项目", "", "项", 1m, 1m, 10m, 10m);
        var unit = new TestUnitSheet("任意页签", "研发楼", [])
        {
            RootTotal = 10m,
            Sections = [new("父标题", 1, 10m, [], [new("子标题", 2, 10m, [item], [])])]
        };
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 10m)], [unit]);
        try
        {
            var parsed = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            var parsedUnit = Assert.Single(parsed.UnitProjects);
            var parsedItem = Assert.Single(parsedUnit.Items);
            Assert.Equal([0, 1, 2], parsedUnit.Sections.Select(x => x.Level));
            Assert.Equal([8, 3, 4], parsedUnit.Sections.Select(x => x.SourceRowIndex));
            Assert.Equal(5, parsedItem.SourceRowIndex);
            Assert.Empty(parsedUnit.Sections.Single(x => x.Title == "父标题").DirectItemSourceRows);
            Assert.Equal([parsedItem.SourceRowIndex],
                parsedUnit.Sections.Single(x => x.Title == "子标题").DirectItemSourceRows);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_an_unclosed_subtitle_with_stage_sheet_title_and_expected_subtotal()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var item = new BillItem("1", "A", "项目", "", "项", 1m, 1m, 10m, 10m);
        var unit = new TestUnitSheet("终审明细页", "研发楼", [])
        {
            RootTotal = 10m,
            Sections = [new("未闭合给排水", 1, 10m, [item], [])],
            OmitLastSectionSubtotal = true
        };
        var path = TestWorkbookFactory.Create(directory, "终审.xlsx", [new("研发楼", 10m)], [unit]);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("未闭合给排水", error.Message, StringComparison.Ordinal);
            Assert.Contains("分部小计", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Keeps_a_section_free_sheet_in_flat_compatibility_mode()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var item = new BillItem("1", "A", "普通清单", "", "项", 1m, 1m, 10m, 10m);
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 10m)],
            [new("普通页签", "研发楼", [item])]);
        try
        {
            var parsed = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            var unit = Assert.Single(parsed.UnitProjects);
            Assert.False(unit.HasSubtitleStructure);
            Assert.Single(unit.Items);
            Assert.DoesNotContain(unit.Sections, x => !x.IsRoot);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Compares_a_section_free_sheet_with_an_unmatched_subtotal_in_flat_mode()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var submissionPath = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 10m)],
        [
            new TestUnitSheet("送审页签", "研发楼",
                [new BillItem("1", "A", "普通清单", "", "项", 1m, 1m, 10m, 10m)])
            {
                RootTotal = 10m,
                AppendUnmatchedSectionSubtotal = true
            }
        ]);
        var initialPath = TestWorkbookFactory.Create(directory, "初审.xlsx", [new("研发楼", 20m)],
        [
            new TestUnitSheet("初审页签", "研发楼",
                [new BillItem("1", "A", "普通清单", "", "项", 2m, 2m, 10m, 20m)])
            {
                RootTotal = 20m,
                AppendUnmatchedSectionSubtotal = true
            }
        ]);
        try
        {
            var submission = await _importer.ParseAsync(
                submissionPath, WorkbookRole.Submission, CancellationToken.None);
            var initial = await _importer.ParseAsync(
                initialPath, WorkbookRole.InitialReview, CancellationToken.None);
            var comparison = Assert.Single(new ComparisonEngine().Compare(
                new ComparisonRequest(submission, initial, null),
                ComparisonSettings.Default).UnitComparisons);

            Assert.False(Assert.Single(submission.UnitProjects).HasSubtitleStructure);
            Assert.False(Assert.Single(initial.UnitProjects).HasSubtitleStructure);
            var line = Assert.IsType<ItemDetailLine>(Assert.Single(comparison.DetailLines));
            Assert.Equal(0, line.OutlineLevel);
            Assert.Equal(10m, line.Row.SubmissionTotal);
            Assert.Equal(20m, line.Row.ReviewTotal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_an_unmatched_subtotal_when_the_sheet_contains_explicit_subtitles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var item = new BillItem("1", "A", "普通清单", "", "项", 1m, 1m, 10m, 10m);
        var unit = new TestUnitSheet("初审页签", "研发楼", [])
        {
            RootTotal = 10m,
            Sections = [new("电气", 1, 10m, [item], [])],
            AppendUnmatchedSectionSubtotal = true
        };
        var path = TestWorkbookFactory.Create(directory, "初审.xlsx", [new("研发楼", 10m)], [unit]);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.InitialReview, CancellationToken.None));
            Assert.Contains("初审", error.Message, StringComparison.Ordinal);
            Assert.Contains("初审页签", error.Message, StringComparison.Ordinal);
            Assert.Contains("没有对应小标题的分部小计", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Keeps_flat_mode_items_after_root_without_treating_measure_title_as_a_subtitle()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ordinary = new BillItem("1", "A", "普通清单", "", "项", 1m, 1m, 10m, 10m);
        var measure = new BillItem("2", "M", "措施清单", "", "项", 1m, 1m, 5m, 5m);
        var unitSheet = new TestUnitSheet("平铺页签", "研发楼", [ordinary])
        {
            RootTotal = 10m,
            MeasureTitle = "单价措施项目",
            MeasureItems = [measure],
            MeasureTotal = 5m
        };
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 15m)], [unitSheet]);
        try
        {
            var parsed = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            var unit = Assert.Single(parsed.UnitProjects);
            Assert.False(unit.HasSubtitleStructure);
            Assert.Equal(["A", "M"], unit.Items.Select(x => x.Code));
            Assert.Single(unit.Sections, x => x.IsRoot);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Keeps_legitimate_items_whose_codes_contain_structural_words()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = TestWorkbookFactory.Create(directory, "送审.xlsx", [new("研发楼", 20m)],
        [
            new TestUnitSheet("普通页签", "研发楼",
            [
                new BillItem("1", "分部-001", "合法清单甲", "", "项", 1m, 1m, 10m, 10m),
                new BillItem("2", "合计-001", "合法清单乙", "", "项", 1m, 1m, 10m, 10m)
            ])
        ]);
        try
        {
            var parsed = await _importer.ParseAsync(path, WorkbookRole.Submission, CancellationToken.None);
            Assert.Equal(["分部-001", "合计-001"],
                Assert.Single(parsed.UnitProjects).Items.Select(x => x.Code));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_a_recognized_detail_sheet_without_the_bill_header_row()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = TestWorkbookFactory.CreateWithBillHeaders(
            directory, "终审.xlsx", "终审明细页", "研发楼", ["序号", "错误编码列", "错误名称列"]);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("项目编码", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_a_partial_bill_header_and_names_the_missing_columns()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = TestWorkbookFactory.CreateWithBillHeaders(
            directory,
            "终审.xlsx",
            "终审明细页",
            "研发楼",
            ["序号", "项目编码", "项目名称", "计量单位", "工程量", "综合单价"]);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("项目特征", error.Message, StringComparison.Ordinal);
            Assert.Contains("合价", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Rejects_missing_price_headers_even_when_first_item_text_mentions_price_and_total()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var misleadingItem = new BillItem(
            "1", "A01", "综合单价及合价调整", "综合单价与合价说明", "项", 1m, 1m, 10m, 10m);
        var path = TestWorkbookFactory.CreateWithBillHeaders(
            directory,
            "终审.xlsx",
            "终审明细页",
            "研发楼",
            ["序号", "项目编码", "项目名称", "项目特征描述", "计量单位", "工程量"],
            misleadingItem);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                _importer.ParseAsync(path, WorkbookRole.FinalReview, CancellationToken.None));
            Assert.Contains("终审", error.Message, StringComparison.Ordinal);
            Assert.Contains("终审明细页", error.Message, StringComparison.Ordinal);
            Assert.Contains("综合单价", error.Message, StringComparison.Ordinal);
            Assert.Contains("合价", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

}
