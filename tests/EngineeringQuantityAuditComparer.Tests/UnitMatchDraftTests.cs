using EngineeringQuantityAuditComparer.App.ViewModels;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class UnitMatchDraftTests
{
    [Fact]
    public void Build_contains_every_aligned_unit_and_marks_only_ambiguous_rows_as_blocking()
    {
        var sentA = Unit(1, "送审甲");
        var sentB = Unit(2, "送审乙");
        var initialA = Unit(10, "送审甲");
        var initialExtra = Unit(11, "初审新增");
        var finalA = Unit(20, "送审甲");

        var session = Compare(
            Book(sentA, sentB),
            Book(initialA, initialExtra),
            Book(finalA));

        var rows = UnitMatchDraftService.Build(session);

        Assert.Equal(session.UnitComparisons.Count, rows.Count);
        Assert.Contains(rows, x => x.Submission == sentA && x.InitialReview == initialA && x.FinalReview == finalA);
        Assert.Contains(rows, x => x.Submission == sentB && x.Reason == "初审未给、终审未给" && !x.RequiresConfirmation);
        Assert.Contains(rows, x => x.InitialReview == initialExtra && x.Reason == "初审新增" && !x.RequiresConfirmation);
    }

    [Fact]
    public void Move_swaps_only_the_requested_stage_and_requires_reconfirmation()
    {
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(Unit(1, "送审甲"), Unit(10, "初审甲"), Unit(20, "终审甲")),
            Row(Unit(2, "送审乙"), Unit(11, "初审乙"), Unit(21, "终审乙"))
        };

        var request = new ComparisonRequest(
            Book(rows[0].Submission!, rows[1].Submission!),
            Book(rows[0].InitialReview!, rows[1].InitialReview!),
            Book(rows[0].FinalReview!, rows[1].FinalReview!));
        Assert.True(UnitMatchDraftService.TryMove(
            rows, rows[1], WorkbookRole.InitialReview, -1, request, out var error), error);

        Assert.Equal("初审乙", rows[0].InitialReview!.Name);
        Assert.Equal("初审甲", rows[1].InitialReview!.Name);
        Assert.Equal("终审甲", rows[0].FinalReview!.Name);
        Assert.Equal("终审乙", rows[1].FinalReview!.Name);
        Assert.All(rows, row =>
        {
            Assert.True(row.RequiresConfirmation);
            Assert.False(row.IsConfirmed);
            Assert.Contains("人工调整", row.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Decisions_capture_every_active_stage_pair_and_explicit_blank()
    {
        var sent = Unit(1, "送审甲");
        var initial = Unit(10, "初审甲");
        var finalOnly = Unit(20, "终审新增");
        var request = new ComparisonRequest(Book(sent), Book(initial), Book(finalOnly));
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(sent, initial, null),
            Row(null, null, finalOnly)
        };

        var decisions = UnitMatchDraftService.CreateDecisions(rows, request);

        Assert.Contains(new UnitMatchDecision(WorkbookRole.Submission, 1, WorkbookRole.InitialReview, 10), decisions);
        Assert.Contains(new UnitMatchDecision(WorkbookRole.Submission, 1, WorkbookRole.FinalReview, null), decisions);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    public void Move_rejects_table_boundaries(int index, int offset)
    {
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(Unit(1, "送审甲"), Unit(10, "初审甲"), null),
            Row(Unit(2, "送审乙"), Unit(11, "初审乙"), null)
        };

        var request = new ComparisonRequest(
            Book(rows[0].Submission!, rows[1].Submission!),
            Book(rows[0].InitialReview!, rows[1].InitialReview!),
            null);
        Assert.False(UnitMatchDraftService.TryMove(
            rows, rows[index], WorkbookRole.InitialReview, offset, request, out var error));
        Assert.Contains(offset < 0 ? "第一行" : "最后一行", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Completeness_rejects_a_repeated_stage_sheet()
    {
        var sentA = Unit(1, "送审甲");
        var sentB = Unit(2, "送审乙");
        var initial = Unit(10, "初审甲");
        var request = new ComparisonRequest(Book(sentA, sentB), Book(initial), null);
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(sentA, initial, null),
            Row(sentB, initial, null)
        };

        Assert.False(UnitMatchDraftService.TryValidateCompleteness(rows, request, out var error));
        Assert.Contains("初审", error, StringComparison.Ordinal);
        Assert.Contains("重复", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_can_fill_an_adjacent_blank_and_prunes_the_vacated_empty_audit_row()
    {
        var sent = Unit(1, "送审甲");
        var initialExtra = Unit(10, "初审新增");
        var request = new ComparisonRequest(Book(sent), Book(initialExtra), null);
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(sent, null, null),
            Row(null, initialExtra, null)
        };

        Assert.True(UnitMatchDraftService.TryMove(
            rows, rows[1], WorkbookRole.InitialReview, -1, request, out var error), error);

        var row = Assert.Single(rows);
        Assert.Same(sent, row.Submission);
        Assert.Same(initialExtra, row.InitialReview);
        Assert.True(row.RequiresConfirmation);
        Assert.True(UnitMatchDraftService.TryValidateCompleteness(rows, request, out error), error);
    }

    [Fact]
    public void Move_rejects_a_blank_selected_stage_cell()
    {
        var sent = Unit(1, "送审甲");
        var initial = Unit(10, "初审乙");
        var request = new ComparisonRequest(Book(sent, Unit(2, "送审乙")), Book(initial), null);
        var rows = new List<UnitMatchRowViewModel>
        {
            Row(sent, null, null),
            Row(Unit(2, "送审乙"), initial, null)
        };

        Assert.False(UnitMatchDraftService.TryMove(
            rows, rows[0], WorkbookRole.InitialReview, 1, request, out var error));
        Assert.Contains("为空", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Decisions_recalculate_to_the_same_draft_key_sequence()
    {
        var sentA = Unit(1, "甲");
        var sentB = Unit(2, "乙");
        var initialA = Unit(10, "甲");
        var initialB = Unit(11, "乙");
        var request = new ComparisonRequest(Book(sentA, sentB), Book(initialA, initialB), null);
        var rows = UnitMatchDraftService.Build(new ComparisonEngine().Compare(request, ComparisonSettings.Default)).ToList();
        Assert.True(UnitMatchDraftService.TryMove(
            rows, rows[1], WorkbookRole.InitialReview, -1, request, out var error), error);
        var settings = ComparisonSettings.Default with
        {
            UnitDecisions = UnitMatchDraftService.CreateDecisions(rows, request)
        };

        var recalculated = new ComparisonEngine().Compare(request, settings);

        Assert.True(UnitMatchDraftService.MatchesSession(rows, recalculated, out error), error);
    }

    private static UnitProject Unit(int sheet, string name) =>
        new(name, UnitProjectKind.ContractPrice, [])
        {
            SourceSheetIndex = sheet,
            SourceSheetName = $"Sheet{sheet}"
        };

    private static CostWorkbook Book(params UnitProject[] units) =>
        new("sample.xlsx", "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, 0m)).ToList(), units);

    private static ComparisonSession Compare(CostWorkbook submission, CostWorkbook? initial, CostWorkbook? final) =>
        new ComparisonEngine().Compare(new ComparisonRequest(submission, initial, final), ComparisonSettings.Default);

    private static UnitMatchRowViewModel Row(UnitProject? submission, UnitProject? initial, UnitProject? final) =>
        new(submission, initial, final, string.Empty, false);
}
