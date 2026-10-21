using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class SimilarItemAggregatorTests
{
    private static BillItem Item(
        string code,
        string name,
        string feature,
        string unit,
        decimal quantity,
        decimal total,
        int sourceRow) =>
        new(null, code, name, feature, unit, null, quantity,
            quantity == 0m ? 0m : total / quantity, total)
        {
            SourceRowIndex = sourceRow
        };

    private static ComparisonRow Row(BillItem? submission, BillItem? initial, BillItem? final = null) =>
        new()
        {
            Submission = submission,
            InitialReview = initial,
            FinalReview = final,
            PairComparisons = new Dictionary<DifferenceKind, PairComparison>()
        };

    private static UnitProject Project(
        string name,
        WorkbookRole role,
        string system,
        params BillItem[] items) =>
        new(name, UnitProjectKind.ContractPrice, items)
        {
            SourceSheetIndex = role switch
            {
                WorkbookRole.Submission => 1,
                WorkbookRole.InitialReview => 2,
                WorkbookRole.FinalReview => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            },
            Sections =
            [
                new BillSection("整个工程", 0, 1, items.Sum(x => x.Total ?? 0m), [], true),
                new BillSection(system, 1, 10, items.Sum(x => x.Total ?? 0m),
                    items.Select(x => x.SourceRowIndex).ToList(), false)
            ]
        };

    private static UnitComparison Unit(
        string name,
        IReadOnlyList<ComparisonRow> rows,
        string submissionSystem,
        string initialSystem,
        string finalSystem = "未分组")
    {
        var submissionItems = rows.Select(x => x.Submission).Where(x => x is not null).Cast<BillItem>().ToList();
        var initialItems = rows.Select(x => x.InitialReview).Where(x => x is not null).Cast<BillItem>().ToList();
        var finalItems = rows.Select(x => x.FinalReview).Where(x => x is not null).Cast<BillItem>().ToList();
        return new UnitComparison(
            name,
            UnitProjectKind.ContractPrice,
            Project(name, WorkbookRole.Submission, submissionSystem, submissionItems.ToArray()),
            Project(name, WorkbookRole.InitialReview, initialSystem, initialItems.ToArray()),
            finalItems.Count == 0 ? null : Project(name, WorkbookRole.FinalReview, finalSystem, finalItems.ToArray()),
            rows,
            rows.Select(x => (DetailLine)new ItemDetailLine(x, 2)).ToList(),
            string.Empty);
    }

    private static ComparisonSession Session(params UnitComparison[] units)
    {
        static CostWorkbook Book(string path) => new(path, "项目", [], []);
        return new ComparisonSession(
            new ComparisonRequest(Book("送审.xlsx"), Book("初审.xlsx"), Book("终审.xlsx")),
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsInitial, DifferenceKind.FinalVsSubmission],
            [], units, [], [], ComparisonSettings.Default);
    }

    private static ComparisonSession InitialOnlySession(params UnitComparison[] units)
    {
        static CostWorkbook Book(string path) => new(path, "项目", [], []);
        return new ComparisonSession(
            new ComparisonRequest(Book("送审.xlsx"), Book("初审.xlsx"), null),
            [DifferenceKind.InitialVsSubmission],
            [], units, [], [], ComparisonSettings.Default);
    }

    [Fact]
    public void Groups_by_normalized_feature_and_unit_without_netting_opposite_unit_movements()
    {
        var first = Unit("1#楼",
        [
            Row(Item("A", "电缆甲", "铜 芯", "m", 10m, 100m, 11),
                Item("B", "电缆甲", "铜芯", "ｍ", 12m, 120m, 21)),
            Row(Item("C", "单机", "设备", "项", 1m, 50m, 12),
                Item("D", "单机", "设备", "项", 2m, 75m, 22))
        ], "电气", "电气");
        var second = Unit("2#楼",
        [
            Row(Item("X", "电缆乙", "铜芯", "M", 10m, 100m, 31),
                Item("Y", "电缆乙", "铜芯", "m", 8m, 80m, 41))
        ], "给排水", "给排水");

        var rows = SimilarItemAggregator.Aggregate(
            Session(first, second), DifferenceKind.InitialVsSubmission);

        var cable = Assert.Single(rows, x => x.Feature == "铜 芯" && x.Unit == "m");
        Assert.Equal(["电缆甲", "电缆乙"], cable.ItemNames);
        Assert.Equal(2, cable.UnitProjectCount);
        Assert.Equal(20m, cable.EarlierQuantity);
        Assert.Equal(20m, cable.LaterQuantity);
        Assert.Equal(2m, cable.QuantityIncrease);
        Assert.Equal(2m, cable.QuantityDecrease);
        Assert.Equal(0m, cable.QuantityNetChange);
        Assert.Equal(20m, cable.AmountIncrease);
        Assert.Equal(20m, cable.AmountDecrease);
        Assert.Equal(0m, cable.AmountNetChange);
        Assert.Equal(["1#楼／电气", "2#楼／给排水"], cable.UnitSystemNotes);
        Assert.Contains(rows, x => x.Feature == "设备" && x.Unit == "项" && x.UnitProjectCount == 1);
    }

    [Fact]
    public void Empty_feature_falls_back_to_name_and_different_units_stay_separate()
    {
        var unit = Unit("研发楼",
        [
            Row(Item("A", "照 明 灯", "", "套", 10m, 100m, 11),
                Item("B", "照明灯", "", "套", 12m, 120m, 21)),
            Row(Item("C", "照明灯", "", "个", 1m, 10m, 12),
                Item("D", "照明灯", "", "个", 2m, 20m, 22))
        ], "照明", "照明");

        var rows = SimilarItemAggregator.Aggregate(Session(unit), DifferenceKind.InitialVsSubmission);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.Equal(string.Empty, x.Feature));
        Assert.Equal(["套", "个"], rows.Select(x => x.Unit));
    }

    [Fact]
    public void Changed_key_splits_old_decrease_and_new_increase_with_stage_specific_notes()
    {
        var row = Row(
            Item("A", "风管", "镀锌板 0.5", "m2", 10m, 100m, 11),
            Item("A", "风管", "镀锌板 0.8", "m2", 12m, 180m, 21));
        var rows = SimilarItemAggregator.Aggregate(
            Session(Unit("2#楼", [row], "通风", "消防")),
            DifferenceKind.InitialVsSubmission);

        var oldKey = Assert.Single(rows, x => x.Feature == "镀锌板 0.5");
        Assert.Equal(100m, oldKey.AmountDecrease);
        Assert.Equal(0m, oldKey.AmountIncrease);
        Assert.Equal(["2#楼／从通风删除"], oldKey.UnitSystemNotes);
        var newKey = Assert.Single(rows, x => x.Feature == "镀锌板 0.8");
        Assert.Equal(180m, newKey.AmountIncrease);
        Assert.Equal(0m, newKey.AmountDecrease);
        Assert.Equal(["2#楼／新增至消防"], newKey.UnitSystemNotes);
    }

    [Fact]
    public void Same_key_system_move_uses_arrow_and_missing_sections_use_ungrouped()
    {
        var moved = Row(
            Item("A", "桥架", "100x50", "m", 10m, 100m, 11),
            Item("B", "桥架", "100x50", "m", 11m, 110m, 21));
        var added = Row(null, Item("C", "传感器", "温感", "只", 1m, 30m, 22));
        var unit = Unit("3#楼", [moved, added], "给排水", "消防");
        unit = unit with
        {
            InitialReview = unit.InitialReview! with { Sections = [unit.InitialReview.Sections.Single(x => x.IsRoot)] }
        };

        var rows = SimilarItemAggregator.Aggregate(Session(unit), DifferenceKind.InitialVsSubmission);

        Assert.Equal(["3#楼／给排水→未分组"],
            Assert.Single(rows, x => x.Feature == "100x50").UnitSystemNotes);
        Assert.Equal(["3#楼／新增至未分组"],
            Assert.Single(rows, x => x.Feature == "温感").UnitSystemNotes);
    }

    [Fact]
    public void Omits_only_groups_below_both_tolerances_and_sorts_by_total_impact_then_quantity_then_key()
    {
        var unit = Unit("排序楼",
        [
            Row(Item("A", "不变", "不变", "项", 1m, 10m, 11),
                Item("A2", "不变", "不变", "项", 1.00001m, 10.001m, 21)),
            Row(Item("B", "金额优先", "B", "项", 1m, 10m, 12),
                Item("B2", "金额优先", "B", "项", 1m, 50m, 22)),
            Row(Item("C", "数量次序", "C", "m", 1m, 10m, 13),
                Item("C2", "数量次序", "C", "m", 5m, 10m, 23)),
            Row(Item("D", "键次序", "D", "m", 1m, 10m, 14),
                Item("D2", "键次序", "D", "m", 5m, 10m, 24))
        ], "系统", "系统");

        var rows = SimilarItemAggregator.Aggregate(Session(unit), DifferenceKind.InitialVsSubmission);

        Assert.DoesNotContain(rows, x => x.Feature == "不变");
        Assert.Equal(["金额优先", "数量次序", "键次序"], rows.Select(x => x.DisplayName));
    }

    [Fact]
    public void Rejects_a_difference_that_is_inactive_for_the_session_mode()
    {
        var unit = Unit("研发楼",
        [
            Row(Item("A", "电缆", "铜芯", "m", 1m, 10m, 11),
                Item("B", "电缆", "铜芯", "m", 2m, 20m, 21))
        ], "送审系统", "初审系统");

        var exception = Assert.Throws<ArgumentException>(() =>
            SimilarItemAggregator.Aggregate(
                InitialOnlySession(unit), DifferenceKind.FinalVsSubmission));

        Assert.Equal("difference", exception.ParamName);
    }

    [Fact]
    public void Aggregates_the_final_vs_initial_active_pair_from_its_actual_stages()
    {
        var unit = Unit("研发楼",
        [
            Row(
                Item("S", "桥架", "100x50", "m", 1m, 10m, 11),
                Item("I", "桥架", "100x50", "m", 10m, 100m, 21),
                Item("F", "桥架", "100x50", "m", 13m, 156m, 31))
        ], "送审系统", "初审系统", "终审系统");

        var summary = Assert.Single(SimilarItemAggregator.Aggregate(
            Session(unit), DifferenceKind.FinalVsInitial));

        Assert.Equal(10m, summary.EarlierQuantity);
        Assert.Equal(13m, summary.LaterQuantity);
        Assert.Equal(100m, summary.EarlierTotal);
        Assert.Equal(156m, summary.LaterTotal);
        Assert.Equal(3m, summary.QuantityIncrease);
        Assert.Equal(56m, summary.AmountIncrease);
        Assert.Equal(["研发楼／初审系统→终审系统"], summary.UnitSystemNotes);
    }

    [Fact]
    public void Uses_the_deepest_directly_owning_non_root_section_for_system_notes()
    {
        var nestedRow = Row(
            Item("S", "风管", "镀锌板", "m2", 10m, 100m, 11),
            Item("I", "风管", "镀锌板", "m2", 12m, 120m, 21));
        var outerRow = Row(
            Item("S2", "设备", "外层设备", "台", 1m, 50m, 12),
            Item("I2", "设备", "外层设备", "台", 2m, 100m, 22));
        var unit = Unit("研发楼", [nestedRow, outerRow], "送审外层", "初审外层");
        unit = unit with
        {
            Submission = unit.Submission! with
            {
                Sections =
                [
                    unit.Submission.Sections.Single(x => x.IsRoot),
                    new BillSection("送审外层", 1, 5, 150m, [12], false),
                    new BillSection("送审内层", 2, 10, 100m, [11], false)
                ]
            },
            InitialReview = unit.InitialReview! with
            {
                Sections =
                [
                    unit.InitialReview.Sections.Single(x => x.IsRoot),
                    new BillSection("初审外层", 1, 5, 220m, [22], false),
                    new BillSection("初审内层", 2, 10, 120m, [21], false)
                ]
            }
        };

        var summary = Assert.Single(
            SimilarItemAggregator.Aggregate(Session(unit), DifferenceKind.InitialVsSubmission),
            x => x.Feature == "镀锌板");

        Assert.Equal(["研发楼／送审内层→初审内层"], summary.UnitSystemNotes);
    }

    [Fact]
    public void Counts_reference_distinct_aligned_units_even_when_their_names_match()
    {
        var first = Unit("同名楼",
        [
            Row(Item("S1", "电缆", "铜芯", "m", 10m, 100m, 11),
                Item("I1", "电缆", "铜芯", "m", 11m, 110m, 21))
        ], "电气", "电气");
        var second = Unit("同名楼",
        [
            Row(Item("S2", "电缆", "铜芯", "m", 20m, 200m, 31),
                Item("I2", "电缆", "铜芯", "m", 22m, 220m, 41))
        ], "电气", "电气");

        var summary = Assert.Single(SimilarItemAggregator.Aggregate(
            Session(first, second), DifferenceKind.InitialVsSubmission));

        Assert.Equal(2, summary.UnitProjectCount);
        Assert.Equal(30m, summary.EarlierQuantity);
        Assert.Equal(33m, summary.LaterQuantity);
    }

    [Fact]
    public void Amount_dashboard_uses_submission_based_reductions_and_keeps_increases_negative()
    {
        var unit = Unit("研发楼",
        [
            Row(
                Item("S", "桥架", "100x50", "m", 10m, 100m, 11),
                Item("I", "桥架", "100x50", "m", 6m, 60m, 21),
                Item("F", "桥架", "100x50", "m", 7m, 70m, 31))
        ], "送审系统", "初审系统", "终审系统");

        var row = Assert.Single(SimilarItemAmountAggregator.Aggregate(
            Session(unit),
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsSubmission]));

        Assert.Equal("桥架", row.DisplayName);
        Assert.Equal(40m, row.StageAmounts[DifferenceKind.InitialVsSubmission].Reduction);
        Assert.Equal(0m, row.StageAmounts[DifferenceKind.InitialVsSubmission].Increase);
        Assert.Equal(30m, row.StageAmounts[DifferenceKind.FinalVsSubmission].Reduction);
        Assert.Equal(0m, row.StageAmounts[DifferenceKind.FinalVsSubmission].Increase);
        Assert.Equal(70m, row.TotalImpact);
    }

    [Fact]
    public void Amount_dashboard_omits_quantity_only_changes_with_zero_amount_impact()
    {
        var unit = Unit("研发楼",
        [
            Row(
                Item("S", "电缆", "铜芯", "m", 10m, 100m, 11),
                Item("I", "电缆", "铜芯", "m", 20m, 100m, 21),
                Item("F", "电缆", "铜芯", "m", 30m, 100m, 31))
        ], "电气", "电气", "电气");

        Assert.Empty(SimilarItemAmountAggregator.Aggregate(
            Session(unit),
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsSubmission]));
    }

    [Fact]
    public void Amount_dashboard_sorts_by_all_stage_absolute_amount_impact_then_normalized_key()
    {
        var unit = Unit("排序楼",
        [
            Row(Item("SA", "甲", "A", "m", 1m, 10m, 11),
                Item("IA", "甲", "A", "m", 1m, 30m, 21),
                Item("FA", "甲", "A", "m", 1m, 40m, 31)),
            Row(Item("SD", "丁", "D", "m", 1m, 10m, 12),
                Item("ID", "丁", "D", "m", 1m, 20m, 22),
                Item("FD", "丁", "D", "m", 1m, 5m, 32)),
            Row(Item("SB", "乙", "B", "m", 1m, 10m, 13),
                Item("IB", "乙", "B", "m", 1m, 20m, 23),
                Item("FB", "乙", "B", "m", 1m, 10m, 33)),
            Row(Item("SC", "丙", "C", "m", 1m, 10m, 14),
                Item("IC", "丙", "C", "m", 1m, 20m, 24),
                Item("FC", "丙", "C", "m", 1m, 20m, 34)),
            Row(Item("SE", "戊", "E", "m", 1m, 10m, 15),
                Item("IE", "戊", "E", "m", 1m, 15m, 25),
                Item("FE", "戊", "E", "m", 1m, 20m, 35))
        ], "系统", "系统", "系统");

        var rows = SimilarItemAmountAggregator.Aggregate(
            Session(unit),
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsSubmission]);

        Assert.Equal(["A|M", "C|M", "D|M", "E|M", "B|M"], rows.Select(x => x.NormalizedKey));
        Assert.Equal([50m, 20m, 15m, 15m, 10m], rows.Select(x => x.TotalImpact));
    }
}
