using CommunityToolkit.Mvvm.ComponentModel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.App.ViewModels;

public sealed class BillItemCandidateViewModel(BillItem item)
{
    public BillItem Item { get; } = item;
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Display => $"{Item.Code}｜{Item.Name}｜{Item.Feature}";
}

public sealed class ItemExceptionViewModel : ObservableObject
{
    private BillItemCandidateViewModel? _selectedInitial;
    private BillItemCandidateViewModel? _selectedFinal;

    public ItemExceptionViewModel(ItemMatchConfirmation source)
    {
        Source = source;
        SubmissionItem = Wrap(source.Submission);
        InitialItem = Wrap(source.InitialReview);
        FinalItem = Wrap(source.FinalReview);
        InitialCandidates = source.InitialCandidates.Select(x => new BillItemCandidateViewModel(x)).ToList();
        FinalCandidates = source.FinalCandidates.Select(x => new BillItemCandidateViewModel(x)).ToList();
        CandidateStages = ConfirmationStageResolver.Resolve(source.Id, InitialCandidates.Count, FinalCandidates.Count);
    }

    public ItemMatchConfirmation Source { get; }
    public string Id => Source.Id;
    public string UnitProjectName => Source.UnitProjectName;
    public string Reason => Source.Reason;
    public BillItemCandidateViewModel? SubmissionItem { get; }
    public BillItemCandidateViewModel? InitialItem { get; }
    public BillItemCandidateViewModel? FinalItem { get; }
    public IReadOnlyList<BillItemCandidateViewModel> InitialCandidates { get; }
    public IReadOnlyList<BillItemCandidateViewModel> FinalCandidates { get; }
    public IReadOnlyList<WorkbookRole> CandidateStages { get; }
    public bool IsConfirmed => false;

    public BillItemCandidateViewModel? SelectedInitial
    {
        get => _selectedInitial;
        set => SetProperty(ref _selectedInitial, value);
    }

    public BillItemCandidateViewModel? SelectedFinal
    {
        get => _selectedFinal;
        set => SetProperty(ref _selectedFinal, value);
    }

    internal WorkbookRole AnchorStage => Source.Submission is not null
        ? WorkbookRole.Submission
        : Source.InitialReview is not null
            ? WorkbookRole.InitialReview
            : WorkbookRole.FinalReview;

    internal int AnchorRowIndex => (Source.Submission ?? Source.InitialReview ?? Source.FinalReview)!.SourceRowIndex;

    internal int? SelectedRowIndex(WorkbookRole stage) => stage switch
    {
        WorkbookRole.InitialReview => SelectedInitial?.Item.SourceRowIndex,
        WorkbookRole.FinalReview => SelectedFinal?.Item.SourceRowIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static BillItemCandidateViewModel? Wrap(BillItem? item) =>
        item is null ? null : new BillItemCandidateViewModel(item);
}

internal static class ConfirmationStageResolver
{
    public static IReadOnlyList<WorkbookRole> Resolve(string id, int initialCandidateCount, int finalCandidateCount)
    {
        var stages = new List<WorkbookRole>(2);
        if (initialCandidateCount > 0)
        {
            stages.Add(WorkbookRole.InitialReview);
        }

        if (finalCandidateCount > 0)
        {
            stages.Add(WorkbookRole.FinalReview);
        }

        if (stages.Count == 0 && TryReadCandidateStage(id, out var stage))
        {
            stages.Add(stage);
        }

        return stages;
    }

    private static bool TryReadCandidateStage(string id, out WorkbookRole stage)
    {
        var parts = id.Split('|');
        for (var index = 2; index < parts.Length; index++)
        {
            if (int.TryParse(parts[index - 1], out _) &&
                Enum.TryParse(parts[index - 2], out WorkbookRole anchorStage) &&
                anchorStage is WorkbookRole.Submission or WorkbookRole.InitialReview or WorkbookRole.FinalReview &&
                Enum.TryParse(parts[index], out stage) &&
                stage is WorkbookRole.InitialReview or WorkbookRole.FinalReview)
            {
                return true;
            }
        }

        stage = default;
        return false;
    }
}
