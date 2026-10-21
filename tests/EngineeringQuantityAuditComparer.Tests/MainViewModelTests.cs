using System.Collections.Concurrent;
using System.ComponentModel;
using EngineeringQuantityAuditComparer.App.ViewModels;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class MainViewModelTests
{
    [Theory]
    [InlineData("送审.xlsx", "初审.xlsx", "", true)]
    [InlineData("送审.xlsx", "", "终审.xlsx", true)]
    [InlineData("送审.xlsx", "初审.xlsx", "终审.xlsx", true)]
    [InlineData("送审.xlsx", "", "", false)]
    [InlineData("送审.xlsx", "同一份.xlsx", "同一份.xlsx", false)]
    [InlineData("送审.xlsx", "送审.xlsx", "", false)]
    [InlineData("送审.txt", "初审.xlsx", "", false)]
    public void Enables_analysis_only_for_valid_file_combinations(
        string submission,
        string initial,
        string final,
        bool expected)
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = submission;
        viewModel.InitialReviewPath = initial;
        viewModel.FinalReviewPath = final;

        Assert.Equal(expected, viewModel.AnalyzeCommand.CanExecute(null));
    }

    [Fact]
    public void Invalid_path_text_disables_analysis_without_throwing()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "\0送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        Assert.False(viewModel.AnalyzeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Imports_all_three_selected_roles_and_builds_three_pairs()
    {
        var importer = new StageRecordingImporter();
        var viewModel = CreateViewModel(importer);
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        Assert.Equal(
            [WorkbookRole.Submission, WorkbookRole.InitialReview, WorkbookRole.FinalReview],
            importer.Roles.OrderBy(x => x));
        Assert.Equal(3, viewModel.Session!.ActiveDifferences.Count);
        Assert.Equal(2, viewModel.CurrentStep);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Analysis_always_stops_on_full_unit_match_review_even_without_exceptions()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        Assert.Equal(2, viewModel.CurrentStep);
        var row = Assert.Single(viewModel.UnitMatchRows);
        Assert.Equal("研发楼", row.Submission!.Name);
        Assert.Equal("研发楼", row.InitialReview!.Name);
        Assert.Equal("研发楼", row.FinalReview!.Name);
        Assert.Equal("自动匹配", row.StatusText);
        Assert.True(viewModel.CanContinueToPreview);

        viewModel.ConfirmAllExceptions();

        Assert.Equal(3, viewModel.CurrentStep);
    }

    [Fact]
    public async Task Missing_review_unit_is_visible_but_does_not_require_confirmation()
    {
        var sent = UnitForViewModel(1, "研发楼");
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = BookForViewModel(sent),
            [WorkbookRole.InitialReview] = BookForViewModel(UnitForViewModel(10, "初审独有"))
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        await viewModel.AnalyzeAsync();

        var row = viewModel.UnitMatchRows.Single(x => x.Submission == sent);
        Assert.Equal("初审未给", row.Reason);
        Assert.False(row.RequiresConfirmation);
        Assert.True(viewModel.CanContinueToPreview);
    }

    [Fact]
    public async Task Rejects_suspected_misassigned_stage_files_before_import()
    {
        var importer = new StageRecordingImporter();
        var viewModel = CreateViewModel(importer);
        viewModel.SubmissionPath = "项目初审.xlsx";
        viewModel.InitialReviewPath = "项目送审.xlsx";

        await viewModel.AnalyzeAsync();

        Assert.Empty(importer.Roles);
        Assert.Contains("送审", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("初审", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_unit_confirmations_block_preview_when_items_do_not_match_strictly()
    {
        var submission = BookForViewModel(
            UnitForViewModel(1, "研发楼安装"),
            UnitForViewModel(2, "机房", ItemForViewModel(5, "DUP", "送审项")));
        var initial = BookForViewModel(
            UnitForViewModel(1, "研发楼安装工程"),
            UnitForViewModel(2, "机房",
                ItemForViewModel(5, "DUP", "初审甲"),
                ItemForViewModel(6, "DUP", "初审乙")));
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = submission,
            [WorkbookRole.InitialReview] = initial
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        await viewModel.AnalyzeAsync();

        var unitRow = Assert.Single(viewModel.UnitMatchRows, x => x.RequiresConfirmation);
        Assert.Empty(viewModel.ItemExceptions);
        Assert.Empty(viewModel.Session!.ItemConfirmations);
        Assert.DoesNotContain("清单项目", viewModel.StatusMessage, StringComparison.Ordinal);

        unitRow.IsConfirmed = true;
        viewModel.ConfirmAllExceptions();

        Assert.DoesNotContain(viewModel.UnitMatchRows, x => x.IsBlocking);
        Assert.Empty(viewModel.ItemExceptions);
        Assert.True(viewModel.CanContinueToPreview);
        Assert.Equal(3, viewModel.CurrentStep);
        Assert.Contains(viewModel.Session!.Settings.UnitDecisions, x =>
            x.AnchorStage == WorkbookRole.Submission &&
            x.AnchorSheetIndex == unitRow.Submission!.SourceSheetIndex &&
            x.CandidateStage == WorkbookRole.InitialReview &&
            x.CandidateSheetIndex is null);
        Assert.Empty(viewModel.Session.Settings.ItemDecisions);
    }

    [Fact]
    public async Task Initial_move_changes_real_pairing_and_requires_both_rows_to_be_confirmed()
    {
        var sentA = UnitForViewModel(1, "甲");
        var sentB = UnitForViewModel(2, "乙");
        var initialA = UnitForViewModel(10, "甲");
        var initialB = UnitForViewModel(11, "乙");
        var finalA = UnitForViewModel(20, "甲");
        var finalB = UnitForViewModel(21, "乙");
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = BookForViewModel(sentA, sentB),
            [WorkbookRole.InitialReview] = BookForViewModel(initialA, initialB),
            [WorkbookRole.FinalReview] = BookForViewModel(finalA, finalB)
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";
        await viewModel.AnalyzeAsync();

        viewModel.SelectedUnitMatchRow = viewModel.UnitMatchRows[1];
        viewModel.MoveInitialUpCommand.Execute(null);

        Assert.Equal("乙", viewModel.UnitMatchRows[0].InitialReview!.Name);
        Assert.Equal("甲", viewModel.UnitMatchRows[1].InitialReview!.Name);
        Assert.Equal("甲", viewModel.UnitMatchRows[0].FinalReview!.Name);
        Assert.Equal("乙", viewModel.UnitMatchRows[1].FinalReview!.Name);
        Assert.False(viewModel.CanContinueToPreview);

        Assert.All(viewModel.UnitMatchRows, row => row.IsConfirmed = true);
        viewModel.ConfirmAllExceptions();

        Assert.Equal(3, viewModel.CurrentStep);
        Assert.Equal("乙", viewModel.Session!.UnitComparisons[0].InitialReview!.Name);
        Assert.Equal("甲", viewModel.Session.UnitComparisons[1].InitialReview!.Name);
    }

    [Fact]
    public async Task Final_move_does_not_change_initial_pairing()
    {
        var viewModel = CreateTwoUnitThreeStageViewModel();
        await viewModel.AnalyzeAsync();

        viewModel.SelectedUnitMatchRow = viewModel.UnitMatchRows[0];
        viewModel.MoveFinalDownCommand.Execute(null);

        Assert.Equal(["甲", "乙"], viewModel.UnitMatchRows.Select(x => x.InitialReview!.Name));
        Assert.Equal(["乙", "甲"], viewModel.UnitMatchRows.Select(x => x.FinalReview!.Name));
        Assert.All(viewModel.UnitMatchRows, x => Assert.True(x.IsBlocking));
    }

    [Fact]
    public async Task Unconfirmed_manual_rows_block_preview_with_a_unit_count()
    {
        var viewModel = CreateTwoUnitThreeStageViewModel();
        await viewModel.AnalyzeAsync();
        viewModel.SelectedUnitMatchRow = viewModel.UnitMatchRows[1];
        viewModel.MoveInitialUpCommand.Execute(null);

        viewModel.ConfirmAllExceptions();

        Assert.Equal(2, viewModel.CurrentStep);
        Assert.Contains("2 个单位工程", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reanalysis_clears_manual_moves_and_confirmations()
    {
        var viewModel = CreateTwoUnitThreeStageViewModel();
        await viewModel.AnalyzeAsync();
        viewModel.SelectedUnitMatchRow = viewModel.UnitMatchRows[1];
        viewModel.MoveInitialUpCommand.Execute(null);
        Assert.Contains(viewModel.UnitMatchRows, x => x.RequiresConfirmation);

        await viewModel.AnalyzeAsync();

        Assert.Equal(["甲", "乙"], viewModel.UnitMatchRows.Select(x => x.InitialReview!.Name));
        Assert.DoesNotContain(viewModel.UnitMatchRows, x => x.RequiresConfirmation);
        Assert.Equal(2, viewModel.CurrentStep);
    }

    [Fact]
    public async Task Selecting_a_different_file_clears_the_previous_session_and_unit_draft()
    {
        var viewModel = CreateTwoUnitThreeStageViewModel();
        await viewModel.AnalyzeAsync();
        viewModel.SelectedUnitMatchRow = viewModel.UnitMatchRows[1];
        viewModel.MoveInitialUpCommand.Execute(null);
        Assert.NotEmpty(viewModel.UnitMatchRows);

        viewModel.InitialReviewPath = "另一份初审.xlsx";

        Assert.Null(viewModel.Session);
        Assert.Empty(viewModel.UnitMatchRows);
        Assert.Empty(viewModel.ItemExceptions);
        Assert.Equal(1, viewModel.CurrentStep);
    }

    [Fact]
    public async Task Ambiguous_items_with_pipe_in_code_do_not_create_confirmation_or_block_preview()
    {
        var submission = BookForViewModel(UnitForViewModel(
            1,
            "研发楼",
            ItemForViewModel(5, "DUP|PIPE", "送审甲"),
            ItemForViewModel(6, "DUP|PIPE", "送审乙")));
        var initial = BookForViewModel(UnitForViewModel(1, "研发楼"));
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = submission,
            [WorkbookRole.InitialReview] = initial
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        await viewModel.AnalyzeAsync();

        Assert.Empty(viewModel.ItemExceptions);
        Assert.Empty(viewModel.Session!.ItemConfirmations);
        Assert.True(viewModel.CanContinueToPreview);

        viewModel.ConfirmAllExceptions();

        Assert.Empty(viewModel.ItemExceptions);
        Assert.True(viewModel.CanContinueToPreview);
        Assert.Equal(3, viewModel.CurrentStep);
        Assert.Empty(viewModel.Session.Settings.ItemDecisions);
    }

    [Fact]
    public async Task Multiple_strict_item_candidates_remain_additions_without_manual_selection()
    {
        var submission = BookForViewModel(UnitForViewModel(
            1,
            "研发楼",
            ItemForViewModel(5, "OLD", "套管")));
        var initial = BookForViewModel(UnitForViewModel(
            1,
            "研发楼",
            ItemForViewModel(6, "NEW-1", "套管"),
            ItemForViewModel(7, "NEW-2", "套管")));
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = submission,
            [WorkbookRole.InitialReview] = initial
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        await viewModel.AnalyzeAsync();

        viewModel.ConfirmAllExceptions();

        var rows = viewModel.Session!.UnitComparisons.Single().Rows;
        Assert.Empty(viewModel.ItemExceptions);
        Assert.DoesNotContain(rows, x =>
            x.PairComparisons[DifferenceKind.InitialVsSubmission].Status == MatchStatus.ManualLinked);
        Assert.Equal(2, rows.Count(x => x.Submission is null && x.InitialReview is not null));
        Assert.Equal(3, viewModel.CurrentStep);
    }

    [Fact]
    public async Task Duplicate_item_candidates_do_not_require_selection()
    {
        var submission = BookForViewModel(UnitForViewModel(
            1,
            "研发楼",
            ItemForViewModel(5, "DUP", "送审甲"),
            ItemForViewModel(6, "DUP", "送审乙")));
        var initial = BookForViewModel(UnitForViewModel(
            1,
            "研发楼",
            ItemForViewModel(5, "DUP", "初审甲"),
            ItemForViewModel(6, "DUP", "初审乙")));
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = submission,
            [WorkbookRole.InitialReview] = initial
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";

        await viewModel.AnalyzeAsync();

        viewModel.ConfirmAllExceptions();

        Assert.Empty(viewModel.ItemExceptions);
        Assert.Empty(viewModel.ErrorMessage);
        Assert.Equal(3, viewModel.CurrentStep);
        Assert.Empty(viewModel.Session!.Settings.ItemDecisions);
    }

    [Fact]
    public async Task Final_only_preview_hides_initial_and_exposes_final_vs_submission()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        Assert.False(viewModel.HasInitialReview);
        Assert.True(viewModel.HasFinalReview);
        var preview = Assert.Single(viewModel.UnitPreviews);
        Assert.Equal([DifferenceKind.FinalVsSubmission], preview.Differences.Keys);
        Assert.Equal(20m, preview.Differences[DifferenceKind.FinalVsSubmission]);
        Assert.Equal(100m, preview.StageAmounts[WorkbookRole.Submission]);
        Assert.Equal(80m, preview.StageAmounts[WorkbookRole.FinalReview]);
        Assert.False(preview.StageAmounts.ContainsKey(WorkbookRole.InitialReview));
    }

    [Fact]
    public async Task Three_stage_preview_contains_all_stage_totals_and_only_submission_based_differences()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        var preview = Assert.Single(viewModel.UnitPreviews);
        Assert.Equal(3, preview.StageAmounts.Count);
        Assert.Equal(2, preview.Differences.Count);
        Assert.Equal(10m, preview.Differences[DifferenceKind.InitialVsSubmission]);
        Assert.Equal(20m, preview.Differences[DifferenceKind.FinalVsSubmission]);
        Assert.DoesNotContain(DifferenceKind.FinalVsInitial, preview.Differences.Keys);
    }

    [Fact]
    public async Task Unit_preview_exposes_scalar_amounts_for_runtime_binding()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        var preview = Assert.Single(viewModel.UnitPreviews);
        Assert.Equal(100m, GetBindableDecimal(preview, "SubmissionAmount"));
        Assert.Equal(90m, GetBindableDecimal(preview, "InitialAmount"));
        Assert.Equal(80m, GetBindableDecimal(preview, "FinalAmount"));
        Assert.Equal(10m, GetBindableDecimal(preview, "InitialVsSubmissionDifference"));
        Assert.Equal(20m, GetBindableDecimal(preview, "FinalVsSubmissionDifference"));
        Assert.Null(preview.GetType().GetProperty("FinalVsInitialDifference"));
    }

    [Fact]
    public async Task Difference_preview_exposes_scalar_amounts_for_runtime_binding()
    {
        var viewModel = CreateViewModel();
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        var preview = Assert.Single(viewModel.DifferencePreviews);
        Assert.Equal(100m, GetBindableDecimal(preview, "SubmissionTotal"));
        Assert.Equal(90m, GetBindableDecimal(preview, "InitialTotal"));
        Assert.Equal(80m, GetBindableDecimal(preview, "FinalTotal"));
        Assert.Equal(10m, GetBindableDecimal(preview, "InitialVsSubmissionDifference"));
        Assert.Equal(20m, GetBindableDecimal(preview, "FinalVsSubmissionDifference"));
        Assert.Null(preview.GetType().GetProperty("FinalVsInitialDifference"));
    }

    [Fact]
    public async Task Difference_preview_uses_preferred_stage_identity_and_updates_row_comment()
    {
        var submission = BookForViewModel(UnitForViewModel(1, "研发楼"));
        var finalItem = ItemForViewModel(5, "FINAL", "终审新增");
        var final = BookForViewModel(UnitForViewModel(1, "研发楼", finalItem));
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = submission,
            [WorkbookRole.FinalReview] = final
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";

        await viewModel.AnalyzeAsync();

        var preview = Assert.Single(viewModel.DifferencePreviews);
        Assert.Equal("FINAL", preview.Code);
        Assert.Equal("终审新增", preview.Name);
        Assert.Equal(10m, preview.StageAmounts[WorkbookRole.FinalReview]);
        preview.UserComment = "现场复核";
        Assert.Equal("现场复核", viewModel.Session!.UnitComparisons.Single().Rows.Single().UserComment);
        Assert.Equal("终审新增；现场复核", preview.CombinedComment);
    }

    [Fact]
    public async Task Export_uses_current_session_and_reports_success()
    {
        var exporter = new FakeExporter();
        var viewModel = new MainViewModel(new StageRecordingImporter(), new ComparisonEngine(), exporter)
        {
            SubmissionPath = "送审.xlsx",
            InitialReviewPath = "初审.xlsx"
        };
        await viewModel.AnalyzeAsync();

        await viewModel.ExportAsync("结果.xlsx", "模版.xlsx");

        Assert.Equal("结果.xlsx", exporter.OutputPath);
        Assert.Contains("已导出", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uses_user_adjusted_comparison_tolerances()
    {
        var viewModel = CreateViewModel(new PriceToleranceImporter());
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.AmountTolerance = 0.001m;

        await viewModel.AnalyzeAsync();

        Assert.Equal("初审单价调整", viewModel.Session!.UnitComparisons.Single().Rows.Single().AutomaticComment);
    }

    private static MainViewModel CreateViewModel(IWorkbookImporter? importer = null) =>
        new(importer ?? new StageRecordingImporter(), new ComparisonEngine(), new FakeExporter());

    private static MainViewModel CreateTwoUnitThreeStageViewModel()
    {
        var sentA = UnitForViewModel(1, "甲");
        var sentB = UnitForViewModel(2, "乙");
        var initialA = UnitForViewModel(10, "甲");
        var initialB = UnitForViewModel(11, "乙");
        var finalA = UnitForViewModel(20, "甲");
        var finalB = UnitForViewModel(21, "乙");
        var viewModel = CreateViewModel(new DictionaryImporter(new Dictionary<WorkbookRole, CostWorkbook>
        {
            [WorkbookRole.Submission] = BookForViewModel(sentA, sentB),
            [WorkbookRole.InitialReview] = BookForViewModel(initialA, initialB),
            [WorkbookRole.FinalReview] = BookForViewModel(finalA, finalB)
        }));
        viewModel.SubmissionPath = "送审.xlsx";
        viewModel.InitialReviewPath = "初审.xlsx";
        viewModel.FinalReviewPath = "终审.xlsx";
        return viewModel;
    }

    private static BillItem ItemForViewModel(int row, string code, string name) =>
        new(null, code, name, "", "项", 1m, 1m, 10m, 10m) { SourceRowIndex = row };

    private static UnitProject UnitForViewModel(int sheet, string name, params BillItem[] items) =>
        new(name, UnitProjectKind.ContractPrice, items)
        {
            SourceSheetIndex = sheet,
            SourceSheetName = $"Sheet{sheet}"
        };

    private static CostWorkbook BookForViewModel(params UnitProject[] units) =>
        new("sample.xlsx", "项目", units.Select(x => new TaxInclusiveSummaryItem(x.Name, x.UntaxedTotal)).ToList(), units);

    private static decimal GetBindableDecimal(object source, string propertyName)
    {
        var property = TypeDescriptor.GetProperties(source)[propertyName];
        Assert.NotNull(property);
        return Assert.IsType<decimal>(property.GetValue(source));
    }

    private sealed class StageRecordingImporter : IWorkbookImporter
    {
        public ConcurrentBag<WorkbookRole> Roles { get; } = [];

        public Task<CostWorkbook> ParseAsync(string path, WorkbookRole role, CancellationToken cancellationToken)
        {
            Roles.Add(role);
            var amount = role switch
            {
                WorkbookRole.Submission => 100m,
                WorkbookRole.InitialReview => 90m,
                WorkbookRole.FinalReview => 80m,
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
            var item = new BillItem("1", "A01", "电缆", "铜芯", "m", 1m, 1m, amount, amount)
                { SourceRowIndex = 5 };
            var unit = new UnitProject("研发楼", UnitProjectKind.ContractPrice, [item])
                { SourceSheetIndex = 1, SourceSheetName = "明细" };
            return Task.FromResult(new CostWorkbook(path, "项目", [new("研发楼", amount)], [unit]));
        }
    }

    private sealed class DictionaryImporter(IReadOnlyDictionary<WorkbookRole, CostWorkbook> books) : IWorkbookImporter
    {
        public Task<CostWorkbook> ParseAsync(string path, WorkbookRole role, CancellationToken cancellationToken) =>
            Task.FromResult(books[role]);
    }

    private sealed class FakeExporter : IReportExporter
    {
        public string? OutputPath { get; private set; }

        public Task ExportAsync(ComparisonSession session, string templatePath, string outputPath, CancellationToken cancellationToken)
        {
            OutputPath = outputPath;
            return Task.CompletedTask;
        }
    }

    private sealed class PriceToleranceImporter : IWorkbookImporter
    {
        public Task<CostWorkbook> ParseAsync(string path, WorkbookRole role, CancellationToken cancellationToken)
        {
            var price = role == WorkbookRole.Submission ? 10m : 10.005m;
            var item = new BillItem("1", "A01", "电缆", "铜芯", "m", 1m, 1m, price, price)
                { SourceRowIndex = 5 };
            var unit = new UnitProject("研发楼", UnitProjectKind.ContractPrice, [item])
                { SourceSheetIndex = 1, SourceSheetName = "明细" };
            return Task.FromResult(new CostWorkbook(path, "项目", [new("研发楼", 100m)], [unit]));
        }
    }
}
