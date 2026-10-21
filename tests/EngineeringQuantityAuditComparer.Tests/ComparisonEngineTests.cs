using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class ComparisonEngineTests
{
    [Fact]
    public void Selects_difference_pairs_from_the_file_mode()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));
        var initial = Book("初审.xlsx", Unit("研发楼"));
        var final = Book("终审.xlsx", Unit("研发楼"));

        Assert.Equal([DifferenceKind.InitialVsSubmission], Compare(submission, initial).ActiveDifferences);
        Assert.Equal([DifferenceKind.FinalVsSubmission], Compare(submission, final: final).ActiveDifferences);
        Assert.Equal(
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsInitial, DifferenceKind.FinalVsSubmission],
            Compare(submission, initial, final).ActiveDifferences);
    }

    [Fact]
    public void Selects_only_submission_based_pairs_for_user_visible_output()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));
        var initial = Book("初审.xlsx", Unit("研发楼"));
        var final = Book("终审.xlsx", Unit("研发楼"));

        Assert.Equal(
            [DifferenceKind.InitialVsSubmission],
            ComparisonPairs.OutputFor(new ComparisonRequest(submission, initial, null)).Select(x => x.Kind));
        Assert.Equal(
            [DifferenceKind.FinalVsSubmission],
            ComparisonPairs.OutputFor(new ComparisonRequest(submission, null, final)).Select(x => x.Kind));
        Assert.Equal(
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsSubmission],
            ComparisonPairs.OutputFor(new ComparisonRequest(submission, initial, final)).Select(x => x.Kind));
    }

    [Fact]
    public void Rejects_submission_without_an_audit_stage()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));
        Assert.Throws<ArgumentException>(() => Compare(submission));
    }

    [Fact]
    public void Inserts_initial_and_final_only_units_between_submission_anchors()
    {
        var submission = Book("送审.xlsx", Unit("送审甲"), Unit("送审乙"));
        var initial = Book("初审.xlsx", Unit("送审甲"), Unit("初审新增"), Unit("送审乙"));
        var final = Book("终审.xlsx", Unit("送审甲"), Unit("终审新增"), Unit("送审乙"));

        var session = Compare(submission, initial, final);

        Assert.Equal(["送审甲", "初审新增", "终审新增", "送审乙"], session.UnitComparisons.Select(x => x.Name));
        Assert.Equal("初审新增", session.UnitComparisons[1].SummaryNote);
        Assert.Equal("终审新增", session.UnitComparisons[2].SummaryNote);
    }

    [Fact]
    public void Merges_the_same_initial_and_final_audit_only_unit_at_the_initial_position()
    {
        var submission = Book("送审.xlsx", Unit("送审甲"), Unit("送审乙"));
        var initial = Book("初审.xlsx", Unit("送审甲"), Unit("审计新增"), Unit("送审乙"));
        var final = Book("终审.xlsx", Unit("送审甲"), Unit("审计新增"), Unit("送审乙"));

        var added = Compare(submission, initial, final).UnitComparisons.Single(x => x.Name == "审计新增");

        Assert.Null(added.Submission);
        Assert.NotNull(added.InitialReview);
        Assert.NotNull(added.FinalReview);
        Assert.Equal("初审新增、终审新增", added.SummaryNote);
    }

    [Fact]
    public void Places_audit_only_units_at_edges_and_after_a_no_anchor_workbook()
    {
        var submission = Book("送审.xlsx", Unit("送审甲"), Unit("送审乙"));
        var initial = Book("初审.xlsx", Unit("初审开头"), Unit("送审甲"), Unit("送审乙"), Unit("初审结尾"));
        var anchored = Compare(submission, initial);
        Assert.Equal(["初审开头", "送审甲", "送审乙", "初审结尾"], anchored.UnitComparisons.Select(x => x.Name));

        var noAnchorInitial = Book("初审.xlsx", Unit("初审一"), Unit("初审二"));
        var noAnchorFinal = Book("终审.xlsx", Unit("终审一"), Unit("终审二"));
        var noAnchor = Compare(submission, noAnchorInitial, noAnchorFinal);
        Assert.Equal(["送审甲", "送审乙", "初审一", "初审二", "终审一", "终审二"],
            noAnchor.UnitComparisons.Select(x => x.Name));
    }

    [Fact]
    public void Aligns_submission_initial_and_final_items_on_one_row()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A01", "电缆", "铜芯", "m", 3m, 10m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A01", "电缆", "铜芯", "m", 2m, 10m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "A01", "电缆", "铜芯", "m", 1m, 9m)));

        var row = Assert.Single(Assert.Single(Compare(submission, initial, final).UnitComparisons).Rows);

        Assert.NotNull(row.Submission);
        Assert.NotNull(row.InitialReview);
        Assert.NotNull(row.FinalReview);
        Assert.Equal("1", row.Submission!.SourceSequence);
    }

    [Fact]
    public void Merges_initial_and_final_added_items_and_keeps_submission_sequence_blank()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "NEW", "新增灯具", "", "套", 2m, 30m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "NEW", "新增灯具", "", "套", 1m, 28m)));

        var row = Assert.Single(Assert.Single(Compare(submission, initial, final).UnitComparisons).Rows);

        Assert.Null(row.Submission);
        Assert.NotNull(row.InitialReview);
        Assert.NotNull(row.FinalReview);
        Assert.Null(row.Submission?.SourceSequence);
    }

    [Fact]
    public void Inserts_initial_then_final_only_items_between_submission_item_anchors()
    {
        var submission = Book("送审.xlsx", Unit("研发楼",
            Item("1", "A", "锚点甲", "", "项", 1m, 1m),
            Item("2", "B", "锚点乙", "", "项", 1m, 1m)));
        var initial = Book("初审.xlsx", Unit("研发楼",
            Item(null, "A", "锚点甲", "", "项", 1m, 1m),
            Item(null, "I", "初审新增", "", "项", 1m, 1m),
            Item(null, "B", "锚点乙", "", "项", 1m, 1m)));
        var final = Book("终审.xlsx", Unit("研发楼",
            Item(null, "A", "锚点甲", "", "项", 1m, 1m),
            Item(null, "F", "终审新增", "", "项", 1m, 1m),
            Item(null, "B", "锚点乙", "", "项", 1m, 1m)));

        var rows = Compare(submission, initial, final).UnitComparisons.Single().Rows;
        Assert.Equal(["A", "I", "F", "B"], rows.Select(x =>
            (x.Submission ?? x.InitialReview ?? x.FinalReview)!.Code));
    }

    [Fact]
    public void Ambiguous_initial_items_become_additions_without_erasing_a_unique_final_match()
    {
        var sentItem = Item("1", "DUP", "电缆", "", "m", 1m, 10m);
        var submission = Book("送审.xlsx", Unit("研发楼", sentItem));
        var initial = Book("初审.xlsx", Unit("研发楼",
            Item(null, "DUP", "电缆甲", "", "m", 1m, 10m),
            Item(null, "DUP", "电缆乙", "", "m", 1m, 10m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "DUP", "电缆", "", "m", 1m, 9m)));

        var session = Compare(submission, initial, final);
        var row = session.UnitComparisons.Single().Rows.Single(x => x.Submission == sentItem);

        Assert.Empty(session.ItemConfirmations);
        Assert.Empty(session.UnitConfirmations);
        Assert.Null(row.InitialReview);
        Assert.Equal("DUP", row.FinalReview!.Code);
        Assert.Equal(2, session.UnitComparisons.Single().Rows.Count(x =>
            x.Submission is null && x.InitialReview is not null));
    }

    [Fact]
    public void Applies_manual_unit_decisions_but_ignores_legacy_manual_item_decisions()
    {
        var sentItem = Item("1", "OLD", "套管", "相同特征", "m", 1m, 1m) with { SourceRowIndex = 5 };
        var sentUnit = Unit("研发楼-安装", sentItem) with { SourceSheetIndex = 1 };
        var selected = Item(null, "NEW-2", "套管", "相同特征", "m", 1m, 1m) with { SourceRowIndex = 7 };
        var initialUnit = Unit("研发楼安装工程",
            Item(null, "NEW-1", "套管", "相同特征", "m", 1m, 1m) with { SourceRowIndex = 6 },
            selected) with { SourceSheetIndex = 4 };
        var settings = ComparisonSettings.Default with
        {
            UnitDecisions = [new(WorkbookRole.Submission, 1, WorkbookRole.InitialReview, 4)],
            ItemDecisions = [new("研发楼-安装", WorkbookRole.Submission, 5, WorkbookRole.InitialReview, 7)]
        };

        var session = Compare(Book("送审.xlsx", sentUnit), Book("初审.xlsx", initialUnit), settings: settings);
        var unit = Assert.Single(session.UnitComparisons);
        var submissionRow = unit.Rows.Single(x => x.Submission == sentItem);

        Assert.Same(initialUnit, unit.InitialReview);
        Assert.Null(submissionRow.InitialReview);
        Assert.Equal(2, unit.Rows.Count(x => x.Submission is null && x.InitialReview is not null));
        Assert.DoesNotContain(unit.Rows, x =>
            x.PairComparisons[DifferenceKind.InitialVsSubmission].Status == MatchStatus.ManualLinked);
        Assert.Empty(session.ItemConfirmations);
    }

    [Theory]
    [InlineData("应急灯具", "LED 350W", "套")]
    [InlineData("灯具", "LED 500W", "套")]
    [InlineData("灯具", "LED 350W", "盏")]
    public void Same_code_requires_matching_name_feature_and_unit(
        string reviewName,
        string reviewFeature,
        string reviewUnit)
    {
        var submissionItem = Item("1", "A01", "灯具", "LED 350W", "套", 1m, 100m);
        var reviewItem = Item(null, "A01", reviewName, reviewFeature, reviewUnit, 1m, 90m);

        var session = Compare(
            Book("送审.xlsx", Unit("研发楼", submissionItem)),
            Book("初审.xlsx", Unit("研发楼", reviewItem)));
        var rows = Assert.Single(session.UnitComparisons).Rows;

        Assert.Equal(2, rows.Count);
        Assert.Null(rows.Single(x => x.Submission == submissionItem).InitialReview);
        Assert.Null(rows.Single(x => x.InitialReview == reviewItem).Submission);
        Assert.Equal("初审未计", rows.Single(x => x.Submission == submissionItem).AutomaticComment);
        Assert.Equal("初审新增", rows.Single(x => x.InitialReview == reviewItem).AutomaticComment);
        Assert.Empty(session.ItemConfirmations);
    }

    [Fact]
    public void Matches_unique_code_and_preserves_submission_order()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("2", "A02", "灯具", "LED", "套", 2m, 10m), Item("1", "A01", "电缆", "铜芯", "m", 3m, 5m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A01", "电缆", "铜芯", "m", 2m, 5m), Item(null, "A02", "灯具", "LED", "套", 2m, 9m)));

        var rows = Compare(submission, initial).UnitComparisons.Single().Rows;

        Assert.Equal(["2", "1"], rows.Select(x => x.Submission!.SourceSequence));
        Assert.All(rows, x => Assert.Equal(MatchStatus.CodeMatched, x.PairComparisons[DifferenceKind.InitialVsSubmission].Status));
    }

    [Fact]
    public void Matches_changed_code_by_unique_composite_fields_without_comment_when_total_difference_is_zero()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "OLD", "电缆", "铜芯  4mm²", "ｍ", 3m, 5m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "NEW", "电缆", "铜芯\n4mm²", "m", 3m, 5m)));

        var row = Compare(submission, initial).UnitComparisons.Single().Rows.Single();

        Assert.Equal(MatchStatus.CodeChanged, row.PairComparisons[DifferenceKind.InitialVsSubmission].Status);
        Assert.Empty(row.AutomaticComment);
    }

    [Fact]
    public void Zero_quantity_and_price_use_not_provided_before_adjustment_comments()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A", "项目", "", "项", 1m, 100m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));

        var row = Assert.Single(Assert.Single(Compare(submission, initial, final).UnitComparisons).Rows);

        Assert.Equal("初审未给；终审未给", row.AutomaticComment);
        Assert.DoesNotContain("调整", row.AutomaticComment, StringComparison.Ordinal);
        Assert.Equal(0m, row.Quantity(WorkbookRole.InitialReview));
        Assert.Equal(0m, row.UnitPrice(WorkbookRole.FinalReview));
    }

    [Fact]
    public void Zero_total_differences_across_all_active_pairs_omit_automatic_comment()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A", "项目", "", "项", 0m, 0m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));

        var row = Assert.Single(Assert.Single(Compare(submission, initial, final).UnitComparisons).Rows);

        Assert.Empty(row.AutomaticComment);
    }

    [Fact]
    public void User_comment_remains_when_zero_total_difference_suppresses_automatic_comment()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A", "项目", "", "项", 0m, 0m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));
        var row = Assert.Single(Assert.Single(Compare(submission, initial).UnitComparisons).Rows);

        row.UserComment = "人工原因";

        Assert.Equal("人工原因", row.CombinedComment);
    }

    [Fact]
    public void Combines_missing_stage_comments_and_appends_user_reason()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A", "项目", "", "项", 1m, 100m)));
        var row = Compare(submission, Book("初审.xlsx", Unit("研发楼")), Book("终审.xlsx", Unit("研发楼")))
            .UnitComparisons.Single().Rows.Single();
        row.UserComment = "现场取消";

        Assert.Equal("初审未计；终审未计", row.AutomaticComment);
        Assert.Equal("初审未计；终审未计；现场取消", row.CombinedComment);
    }

    [Fact]
    public void Final_adjustment_falls_back_to_submission_when_initial_is_not_provided()
    {
        var submission = Book("送审.xlsx", Unit("研发楼", Item("1", "A", "项目", "", "项", 2m, 100m)));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 0m, 0m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "A", "项目", "", "项", 1m, 100m)));
        var row = Compare(submission, initial, final).UnitComparisons.Single().Rows.Single();

        Assert.Equal("初审未给；终审较送审工程量调整", row.AutomaticComment);
    }

    [Fact]
    public void Labels_single_stage_audit_additions()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));
        var initial = Book("初审.xlsx", Unit("研发楼", Item(null, "I", "初审新增项", "", "项", 1m, 10m)));
        var final = Book("终审.xlsx", Unit("研发楼", Item(null, "F", "终审新增项", "", "项", 1m, 20m)));
        var rows = Compare(submission, initial, final).UnitComparisons.Single().Rows;

        Assert.Equal("初审新增", rows.Single(x => x.InitialReview is not null).AutomaticComment);
        Assert.Equal("终审新增", rows.Single(x => x.FinalReview is not null).AutomaticComment);
    }

    [Fact]
    public void Summarizes_missing_unit_stages_only_when_that_stage_is_selected()
    {
        var submission = Book("送审.xlsx", Unit("研发楼"));

        Assert.Equal(string.Empty, Assert.Single(Compare(submission, Book("初审.xlsx", Unit("研发楼"))).UnitComparisons).SummaryNote);
        Assert.Equal(string.Empty, Assert.Single(Compare(submission, final: Book("终审.xlsx", Unit("研发楼"))).UnitComparisons).SummaryNote);

        var missing = Compare(
                submission,
                Book("初审.xlsx", Unit("初审独有")),
                Book("终审.xlsx", Unit("终审独有")))
            .UnitComparisons.Single(x => x.Submission?.Name == "研发楼");
        Assert.Equal("初审未给、终审未给", missing.SummaryNote);
    }

    [Fact]
    public void Missing_review_units_are_non_blocking_and_use_approved_notes()
    {
        var sent = Unit("研发楼") with { SourceSheetIndex = 1 };
        var initialOnly = Unit("初审独有") with { SourceSheetIndex = 10 };
        var finalOnly = Unit("终审独有") with { SourceSheetIndex = 20 };

        var session = Compare(
            Book("送审.xlsx", sent),
            Book("初审.xlsx", initialOnly),
            Book("终审.xlsx", finalOnly));

        var sentRow = session.UnitComparisons.Single(x => x.Submission == sent);
        Assert.Equal("初审未给、终审未给", sentRow.SummaryNote);
        Assert.DoesNotContain(session.UnitConfirmations, x => x.Submission == sent);
        Assert.Equal("初审新增", session.UnitComparisons.Single(x => x.InitialReview == initialOnly).SummaryNote);
        Assert.Equal("终审新增", session.UnitComparisons.Single(x => x.FinalReview == finalOnly).SummaryNote);
    }

    [Fact]
    public void Similar_audit_only_units_require_confirmation_with_explicit_stages()
    {
        var initialOnly = Unit("审计新增") with { SourceSheetIndex = 10 };
        var finalCandidate = Unit("审计新增工程") with { SourceSheetIndex = 20 };

        var confirmation = Assert.Single(Compare(
            Book("送审.xlsx", Unit("送审锚点") with { SourceSheetIndex = 1 }),
            Book("初审.xlsx", initialOnly),
            Book("终审.xlsx", finalCandidate)).UnitConfirmations);

        Assert.Same(initialOnly, confirmation.InitialReview);
        Assert.Equal([finalCandidate], confirmation.FinalCandidates);
        Assert.Equal(WorkbookRole.InitialReview, confirmation.AnchorStage);
        Assert.Equal(WorkbookRole.FinalReview, confirmation.CandidateStage);
        Assert.Contains("名称不完全一致", confirmation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reserves_a_manual_unit_candidate_before_an_earlier_auto_match()
    {
        var first = Unit("共享名称") with { SourceSheetIndex = 1 };
        var selectedAnchor = Unit("手工锚点") with { SourceSheetIndex = 2 };
        var target = Unit("共享名称") with { SourceSheetIndex = 10 };
        var settings = ComparisonSettings.Default with
        {
            UnitDecisions = [new(WorkbookRole.Submission, 2, WorkbookRole.InitialReview, 10)]
        };

        var units = Compare(Book("送审.xlsx", first, selectedAnchor), Book("初审.xlsx", target), settings: settings).UnitComparisons;

        Assert.Null(units.Single(x => x.Submission == first).InitialReview);
        Assert.Same(target, units.Single(x => x.Submission == selectedAnchor).InitialReview);
    }

    [Fact]
    public void Ignores_legacy_manual_item_decision_and_keeps_the_strict_automatic_match()
    {
        var first = Item("1", "共享", "项目甲", "", "项", 1m, 1m) with { SourceRowIndex = 1 };
        var selectedAnchor = Item("2", "手工", "项目乙", "", "项", 1m, 1m) with { SourceRowIndex = 2 };
        var target = Item(null, "共享", "项目甲", "", "项", 1m, 1m) with { SourceRowIndex = 10 };
        var settings = ComparisonSettings.Default with
        {
            ItemDecisions = [new("研发楼", WorkbookRole.Submission, 2, WorkbookRole.InitialReview, 10)]
        };

        var rows = Compare(
                Book("送审.xlsx", Unit("研发楼", first, selectedAnchor)),
                Book("初审.xlsx", Unit("研发楼", target)),
                settings: settings)
            .UnitComparisons.Single().Rows;

        Assert.Same(target, rows.Single(x => x.Submission == first).InitialReview);
        Assert.Null(rows.Single(x => x.Submission == selectedAnchor).InitialReview);
    }

    [Fact]
    public void Confirms_ambiguous_audit_only_units_between_initial_and_final()
    {
        var initialOnly = Unit("审计新增") with { SourceSheetIndex = 10 };
        var firstFinal = Unit("审计新增") with { SourceSheetIndex = 20 };
        var secondFinal = Unit("审计新增") with { SourceSheetIndex = 21 };

        var confirmation = Compare(
                Book("送审.xlsx", Unit("送审锚点")),
                Book("初审.xlsx", initialOnly),
                Book("终审.xlsx", firstFinal, secondFinal))
            .UnitConfirmations.Single(x => x.InitialReview == initialOnly);

        Assert.Null(confirmation.Submission);
        Assert.Empty(confirmation.InitialCandidates);
        Assert.Equal([firstFinal, secondFinal], confirmation.FinalCandidates);
    }

    [Fact]
    public void Keeps_ambiguous_audit_only_items_separate_without_confirmation()
    {
        var initialOnly = Item(null, "DUP", "审计新增", "", "项", 1m, 1m) with { SourceRowIndex = 10 };
        var firstFinal = Item(null, "DUP", "审计新增", "", "项", 1m, 1m) with { SourceRowIndex = 20 };
        var secondFinal = Item(null, "DUP", "审计新增", "", "项", 1m, 1m) with { SourceRowIndex = 21 };

        var session = Compare(
                Book("送审.xlsx", Unit("研发楼")),
                Book("初审.xlsx", Unit("研发楼", initialOnly)),
                Book("终审.xlsx", Unit("研发楼", firstFinal, secondFinal)));
        var rows = Assert.Single(session.UnitComparisons).Rows;

        Assert.Empty(session.ItemConfirmations);
        Assert.Null(rows.Single(x => x.InitialReview == initialOnly).FinalReview);
        Assert.Equal(2, rows.Count(x => x.FinalReview is not null && x.InitialReview is null));
    }

    [Fact]
    public void Reserves_a_later_initial_to_final_unit_decision_before_earlier_auto_reconciliation()
    {
        var earlier = Unit("共享名称") with { SourceSheetIndex = 10 };
        var selectedAnchor = Unit("手工锚点") with { SourceSheetIndex = 11 };
        var target = Unit("共享名称") with { SourceSheetIndex = 20 };
        var settings = ComparisonSettings.Default with
        {
            UnitDecisions = [new(WorkbookRole.InitialReview, 11, WorkbookRole.FinalReview, 20)]
        };

        var units = Compare(
                Book("送审.xlsx", Unit("送审锚点")),
                Book("初审.xlsx", earlier, selectedAnchor),
                Book("终审.xlsx", target),
                settings)
            .UnitComparisons;

        Assert.Null(units.Single(x => x.InitialReview == earlier).FinalReview);
        Assert.Same(target, units.Single(x => x.InitialReview == selectedAnchor).FinalReview);
    }

    [Fact]
    public void Ignores_legacy_initial_to_final_item_decision_and_keeps_the_strict_match()
    {
        var earlier = Item(null, "共享", "项目甲", "", "项", 1m, 1m) with { SourceRowIndex = 10 };
        var selectedAnchor = Item(null, "手工", "项目乙", "", "项", 1m, 1m) with { SourceRowIndex = 11 };
        var target = Item(null, "共享", "项目甲", "", "项", 1m, 1m) with { SourceRowIndex = 20 };
        var settings = ComparisonSettings.Default with
        {
            ItemDecisions = [new("研发楼", WorkbookRole.InitialReview, 11, WorkbookRole.FinalReview, 20)]
        };

        var rows = Compare(
                Book("送审.xlsx", Unit("研发楼")),
                Book("初审.xlsx", Unit("研发楼", earlier, selectedAnchor)),
                Book("终审.xlsx", Unit("研发楼", target)),
                settings)
            .UnitComparisons.Single().Rows;

        Assert.Same(target, rows.Single(x => x.InitialReview == earlier).FinalReview);
        Assert.Null(rows.Single(x => x.InitialReview == selectedAnchor).FinalReview);
    }

    private static CostWorkbook Book(string path, params UnitProject[] units) =>
        new(path, "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, x.UntaxedTotal)).ToList(), units);

    private static ComparisonSession Compare(
        CostWorkbook submission,
        CostWorkbook? initial = null,
        CostWorkbook? final = null,
        ComparisonSettings? settings = null) =>
        new ComparisonEngine().Compare(new ComparisonRequest(submission, initial, final), settings ?? ComparisonSettings.Default);

    private static UnitProject Unit(string name, params BillItem[] items) => new(name, UnitProjectKind.ContractPrice, items);

    private static BillItem Item(string? sequence, string code, string name, string feature, string unit, decimal quantity, decimal unitPrice) =>
        new(sequence, code, name, feature, unit, quantity, quantity, unitPrice, quantity * unitPrice);
}
