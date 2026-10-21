using CommunityToolkit.Mvvm.ComponentModel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.App.ViewModels;

public readonly record struct UnitMatchKey(
    int? SubmissionSheetIndex,
    int? InitialReviewSheetIndex,
    int? FinalReviewSheetIndex);

public sealed class UnitMatchRowViewModel : ObservableObject
{
    private UnitProject? _initialReview;
    private UnitProject? _finalReview;
    private string _reason;
    private bool _requiresConfirmation;
    private bool _isConfirmed;
    private bool _isManualAdjustment;

    internal UnitMatchRowViewModel(
        UnitProject? submission,
        UnitProject? initialReview,
        UnitProject? finalReview,
        string reason,
        bool requiresConfirmation)
    {
        Submission = submission;
        _initialReview = initialReview;
        _finalReview = finalReview;
        _reason = reason;
        _requiresConfirmation = requiresConfirmation;
    }

    public UnitProject? Submission { get; }
    public UnitProject? InitialReview => _initialReview;
    public UnitProject? FinalReview => _finalReview;
    public string Reason => _reason;
    public bool RequiresConfirmation => _requiresConfirmation;
    public bool IsBlocking => RequiresConfirmation && !IsConfirmed;
    public string StatusText => IsBlocking
        ? (_isManualAdjustment ? "人工调整，待确认" : "待人工确认")
        : RequiresConfirmation ? "已人工确认" : string.IsNullOrWhiteSpace(Reason) ? "自动匹配" : "非阻断备注";

    public bool IsConfirmed
    {
        get => _isConfirmed;
        set
        {
            if (!RequiresConfirmation || !SetProperty(ref _isConfirmed, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsBlocking));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public UnitMatchKey Key => new(
        Submission?.SourceSheetIndex,
        InitialReview?.SourceSheetIndex,
        FinalReview?.SourceSheetIndex);

    internal UnitProject? Unit(WorkbookRole stage) => stage switch
    {
        WorkbookRole.InitialReview => InitialReview,
        WorkbookRole.FinalReview => FinalReview,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    internal void SetUnit(WorkbookRole stage, UnitProject? unit)
    {
        if (stage == WorkbookRole.InitialReview)
        {
            SetProperty(ref _initialReview, unit, nameof(InitialReview));
        }
        else if (stage == WorkbookRole.FinalReview)
        {
            SetProperty(ref _finalReview, unit, nameof(FinalReview));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        OnPropertyChanged(nameof(Key));
    }

    internal void MarkManualAdjustment(string presenceReason)
    {
        _isManualAdjustment = true;
        _requiresConfirmation = true;
        _isConfirmed = false;
        _reason = string.Join("、", presenceReason
            .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append("人工调整")
            .Distinct(StringComparer.Ordinal));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(RequiresConfirmation));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(IsBlocking));
        OnPropertyChanged(nameof(StatusText));
    }
}
