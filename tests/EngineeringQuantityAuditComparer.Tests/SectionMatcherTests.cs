using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class SectionMatcherTests
{
    [Fact]
    public void Merges_same_subtitles_and_preserves_each_source_total()
    {
        var submission = U(1, [I(10, "A")], Root(90, 100m), S("电气", 1, 5, 100m, 10));
        var initial = U(1, [I(20, "A")], Root(90, 80m), S("电 气", 1, 5, 80m, 20));
        var final = U(1, [I(30, "A")], Root(90, 70m), S("电气", 1, 5, 70m, 30));

        var unit = CompareSections(submission, initial, final);
        var sections = unit.DetailLines.OfType<SectionDetailLine>().Select(x => x.Section).ToList();

        Assert.Equal(["整个工程", "电气"], sections.Select(x => x.Title));
        Assert.Equal(0, sections[0].Level);
        Assert.Equal(1, sections[1].Level);
        Assert.Equal(100m, sections[1].Total(WorkbookRole.Submission));
        Assert.Equal(80m, sections[1].Total(WorkbookRole.InitialReview));
        Assert.Equal(70m, sections[1].Total(WorkbookRole.FinalReview));
        Assert.Equal(2, Assert.Single(unit.DetailLines.OfType<ItemDetailLine>()).OutlineLevel);
    }

    [Fact]
    public void Inserts_stage_only_subtitles_by_item_anchors_and_keeps_one_item_line()
    {
        var submission = U(1, [I(10, "A"), I(40, "B")],
            Root(90, 20m), S("甲", 1, 5, 10m, 10), S("乙", 1, 35, 10m, 40));
        var initial = U(1, [I(20, "A"), I(25, "I"), I(50, "B")],
            Root(90, 30m), S("甲", 1, 5, 10m, 20), S("初审新增", 1, 22, 10m, 25), S("乙", 1, 45, 10m, 50));
        var final = U(1, [I(30, "A"), I(35, "F"), I(60, "B")],
            Root(90, 30m), S("甲", 1, 5, 10m, 30), S("终审新增", 1, 32, 10m, 35), S("乙", 1, 55, 10m, 60));

        var lines = CompareSections(submission, initial, final).DetailLines;
        var labels = lines.Select(x => x switch
        {
            SectionDetailLine section => $"S:{section.Section.Title}",
            ItemDetailLine item => $"I:{(item.Row.Submission ?? item.Row.InitialReview ?? item.Row.FinalReview)!.Code}",
            _ => throw new InvalidOperationException()
        });

        Assert.Equal(["S:整个工程", "S:甲", "I:A", "S:初审新增", "I:I", "S:终审新增", "I:F", "S:乙", "I:B"], labels);
        Assert.Equal(4, lines.OfType<ItemDetailLine>().Count());
    }

    [Fact]
    public void A_cross_stage_item_uses_submission_section_and_is_never_duplicated()
    {
        var submission = U(1, [I(10, "A")], Root(90, 10m), S("电气", 1, 5, 10m, 10));
        var final = U(1, [I(30, "A")], Root(90, 10m), S("给排水", 1, 25, 10m, 30));

        var lines = CompareSections(submission, null, final).DetailLines;

        Assert.Single(lines.OfType<ItemDetailLine>());
        Assert.Equal(["整个工程", "电气", "给排水"], lines.OfType<SectionDetailLine>().Select(x => x.Section.Title));
        Assert.True(lines.ToList().FindIndex(x => x is ItemDetailLine) <
            lines.ToList().FindIndex(x => x is SectionDetailLine s && s.Section.Title == "给排水"));
    }

    [Fact]
    public void Matches_duplicate_titles_only_inside_their_item_anchor_intervals()
    {
        var submission = U(1, [I(10, "A"), I(20, "B"), I(30, "C")],
            Root(90, 30m), S("重复", 1, 5, 10m, 10), S("中间", 1, 15, 10m, 20), S("重复", 1, 25, 10m, 30));
        var initial = U(1, [I(40, "A"), I(50, "B"), I(60, "C")],
            Root(90, 24m), S("重 复", 1, 35, 8m, 40), S("中间", 1, 45, 8m, 50), S("重复", 1, 55, 8m, 60));

        var duplicates = CompareSections(submission, initial, null).DetailLines
            .OfType<SectionDetailLine>().Where(x => TextNormalizer.Normalize(x.Section.Title) == "重复").ToList();

        Assert.Equal(2, duplicates.Count);
        Assert.Equal([10m, 10m], duplicates.Select(x => x.Section.Total(WorkbookRole.Submission)));
        Assert.Equal([8m, 8m], duplicates.Select(x => x.Section.Total(WorkbookRole.InitialReview)));
    }

    [Fact]
    public void Preserves_nested_levels_and_makes_the_item_one_level_deeper()
    {
        var submission = U(1, [I(20, "A")], Root(90, 10m),
            S("父标题", 1, 5, 10m), S("子标题", 2, 10, 10m, 20));

        var lines = CompareSections(submission, null,
            U(2, [], Root(90, 0m))).DetailLines;

        Assert.Equal([0, 1, 2], lines.OfType<SectionDetailLine>().Select(x => x.OutlineLevel));
        Assert.Equal(3, Assert.Single(lines.OfType<ItemDetailLine>()).OutlineLevel);
    }

    [Fact]
    public void Merges_parent_titles_by_their_descendant_item_anchors()
    {
        var submission = U(1, [I(20, "A")], Root(90, 10m),
            S("父标题", 1, 5, 10m), S("子标题", 2, 10, 10m, 20));
        var initial = U(1, [I(40, "A")], Root(90, 8m),
            S("父 标题", 1, 25, 8m), S("子标题", 2, 30, 8m, 40));

        var parents = CompareSections(submission, initial, null).DetailLines
            .OfType<SectionDetailLine>()
            .Where(x => TextNormalizer.Normalize(x.Section.Title) == "父标题")
            .ToList();

        var parent = Assert.Single(parents).Section;
        Assert.Equal(10m, parent.Total(WorkbookRole.Submission));
        Assert.Equal(8m, parent.Total(WorkbookRole.InitialReview));
    }

    [Fact]
    public void Retains_an_empty_stage_only_subtitle_without_a_fake_child()
    {
        var submission = U(1, [I(10, "A")], Root(90, 10m), S("电气", 1, 5, 10m, 10));
        var final = U(1, [I(30, "A")], Root(90, 10m), S("给排水", 1, 25, 10m, 30));

        var lines = CompareSections(submission, null, final).DetailLines;

        Assert.IsType<SectionDetailLine>(lines[^1]);
        Assert.Equal("给排水", ((SectionDetailLine)lines[^1]).Section.Title);
        Assert.Single(lines.OfType<ItemDetailLine>());
    }

    [Fact]
    public void Keeps_a_flat_stage_only_item_as_a_direct_child_of_the_root()
    {
        var submission = U(1, [I(10, "A")], Root(90, 10m), S("电气", 1, 5, 10m, 10));
        var final = U(1, [I(30, "A"), I(40, "F")], Root(90, 20m));

        var line = CompareSections(submission, null, final).DetailLines
            .OfType<ItemDetailLine>()
            .Single(x => x.Row.FinalReview?.Code == "F");

        Assert.Null(line.Row.Submission);
        Assert.Equal(1, line.OutlineLevel);
    }

    [Fact]
    public void Merges_same_title_sections_when_one_stage_adds_an_item_inside_the_anchor_interval()
    {
        var submission = U(1, [I(10, "A"), I(30, "B")],
            Root(90, 100m), S("电气", 1, 5, 100m, 10, 30));
        var initial = U(1, [I(20, "A"), I(25, "I"), I(40, "B")],
            Root(90, 80m), S("电 气", 1, 5, 80m, 20, 25, 40));

        var section = Assert.Single(
            CompareSections(submission, initial, null).DetailLines.OfType<SectionDetailLine>(),
            x => TextNormalizer.Normalize(x.Section.Title) == "电气").Section;

        Assert.Equal(100m, section.Total(WorkbookRole.Submission));
        Assert.Equal(80m, section.Total(WorkbookRole.InitialReview));
    }

    private static BillItem I(int row, string code) =>
        new(null, code, code, "", "项", 1m, 1m, 10m, 10m) { SourceRowIndex = row };

    private static BillSection S(string title, int level, int sourceRow, decimal total, params int[] directItemRows) =>
        new(title, level, sourceRow, total, directItemRows, IsRoot: false);

    private static BillSection Root(int sourceRow, decimal total) =>
        new("整个工程", 0, sourceRow, total, [], IsRoot: true);

    private static UnitProject U(int sheet, BillItem[] items, params BillSection[] sections) =>
        new("研发楼", UnitProjectKind.ContractPrice, items)
        {
            SourceSheetIndex = sheet,
            SourceSheetName = $"S{sheet}",
            Sections = sections
        };

    private static CostWorkbook B(string path, UnitProject unit) =>
        new(path, "项目", [new(unit.Name, unit.UntaxedTotal)], [unit]);

    private static UnitComparison CompareSections(UnitProject submission, UnitProject? initial, UnitProject? final) =>
        new ComparisonEngine().Compare(
            new ComparisonRequest(B("送审.xlsx", submission), initial is null ? null : B("初审.xlsx", initial), final is null ? null : B("终审.xlsx", final)),
            ComparisonSettings.Default).UnitComparisons.Single();
}
