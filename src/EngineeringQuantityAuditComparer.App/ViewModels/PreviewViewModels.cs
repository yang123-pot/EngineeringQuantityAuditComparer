using CommunityToolkit.Mvvm.ComponentModel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.App.ViewModels;

public sealed record UnitPreviewViewModel(
    string Name,
    IReadOnlyDictionary<WorkbookRole, decimal> StageAmounts,
    IReadOnlyDictionary<DifferenceKind, decimal> Differences,
    int ExceptionCount)
{
    public decimal SubmissionAmount => StageAmounts.GetValueOrDefault(WorkbookRole.Submission);
    public decimal InitialAmount => StageAmounts.GetValueOrDefault(WorkbookRole.InitialReview);
    public decimal FinalAmount => StageAmounts.GetValueOrDefault(WorkbookRole.FinalReview);
    public decimal InitialVsSubmissionDifference => Differences.GetValueOrDefault(DifferenceKind.InitialVsSubmission);
    public decimal FinalVsSubmissionDifference => Differences.GetValueOrDefault(DifferenceKind.FinalVsSubmission);

    // Kept for compatibility with earlier consumers.
    public decimal ReviewAmount => StageAmounts.GetValueOrDefault(WorkbookRole.InitialReview);
    public decimal Difference => Differences.GetValueOrDefault(DifferenceKind.InitialVsSubmission);
}

public sealed class DifferencePreviewViewModel : ObservableObject
{
    private readonly ComparisonRow _row;

    public DifferencePreviewViewModel(
        string unitProjectName,
        ComparisonRow row,
        IReadOnlyList<WorkbookRole> activeStages,
        IReadOnlyList<StagePair> activePairs)
    {
        UnitProjectName = unitProjectName;
        _row = row;
        StageAmounts = activeStages.ToDictionary(stage => stage, row.Total);
        Differences = activePairs.ToDictionary(
            pair => pair.Kind,
            pair => ComparisonPairs.OutputDifference(pair, row.Total));
    }

    public string UnitProjectName { get; }
    public string Sequence => PreferredItem?.SourceSequence ?? string.Empty;
    public string Code => PreferredItem?.Code ?? string.Empty;
    public string Name => PreferredItem?.Name ?? string.Empty;
    public IReadOnlyDictionary<WorkbookRole, decimal> StageAmounts { get; }
    public IReadOnlyDictionary<DifferenceKind, decimal> Differences { get; }
    public string AutomaticComment => _row.AutomaticComment;
    public string CombinedComment => _row.CombinedComment;

    public string UserComment
    {
        get => _row.UserComment;
        set
        {
            if (string.Equals(_row.UserComment, value, StringComparison.Ordinal))
            {
                return;
            }

            _row.UserComment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CombinedComment));
        }
    }

    public decimal SubmissionTotal => StageAmounts.GetValueOrDefault(WorkbookRole.Submission);
    public decimal InitialTotal => StageAmounts.GetValueOrDefault(WorkbookRole.InitialReview);
    public decimal FinalTotal => StageAmounts.GetValueOrDefault(WorkbookRole.FinalReview);
    public decimal InitialVsSubmissionDifference => Differences.GetValueOrDefault(DifferenceKind.InitialVsSubmission);
    public decimal FinalVsSubmissionDifference => Differences.GetValueOrDefault(DifferenceKind.FinalVsSubmission);

    // Kept for compatibility with earlier consumers.
    public decimal ReviewTotal => StageAmounts.GetValueOrDefault(WorkbookRole.InitialReview);
    public decimal Difference => Differences.GetValueOrDefault(DifferenceKind.InitialVsSubmission);

    private BillItem? PreferredItem => _row.Submission ?? _row.InitialReview ?? _row.FinalReview;
}
