namespace EngineeringQuantityAuditComparer.Core;

public enum WorkbookRole
{
    Submission,
    InitialReview,
    FinalReview,
    Review = InitialReview
}

public enum DifferenceKind
{
    InitialVsSubmission,
    FinalVsInitial,
    FinalVsSubmission
}

public enum UnitProjectKind
{
    ContractPrice,
    NewPrice
}

public enum MatchStatus
{
    NotApplicable,
    EarlierOnly,
    LaterOnly,
    CodeMatched,
    CodeChanged,
    NeedsConfirmation,
    ManualLinked,
    SubmissionOnly = EarlierOnly,
    ReviewOnly = LaterOnly
}

public sealed record StagePair(
    DifferenceKind Kind,
    WorkbookRole Earlier,
    WorkbookRole Later,
    string DifferenceLabel,
    string ReductionLabel);

public sealed record ComparisonRequest(CostWorkbook Submission, CostWorkbook? InitialReview, CostWorkbook? FinalReview)
{
    public bool HasInitialReview => InitialReview is not null;
    public bool HasFinalReview => FinalReview is not null;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Submission);
        if (InitialReview is null && FinalReview is null)
        {
            throw new ArgumentException("初审和终审至少选择一份。", nameof(InitialReview));
        }
    }
}

public sealed record TaxInclusiveSummaryItem(string Name, decimal Amount);

public sealed record BillSection(
    string Title,
    int Level,
    int SourceRowIndex,
    decimal Total,
    IReadOnlyList<int> DirectItemSourceRows,
    bool IsRoot);

public sealed record BillItem(
    string? SourceSequence,
    string Code,
    string Name,
    string Feature,
    string Unit,
    decimal? ContractQuantity,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Total)
{
    public int SourceRowIndex { get; init; } = -1;
}

public sealed record UnitProject(string Name, UnitProjectKind Kind, IReadOnlyList<BillItem> Items)
{
    public int SourceSheetIndex { get; init; } = -1;
    public string SourceSheetName { get; init; } = string.Empty;
    public IReadOnlyList<BillSection> Sections { get; init; } = [];
    public bool HasSubtitleStructure => Sections.Any(x => !x.IsRoot);
    public decimal UntaxedTotal => Items.Sum(x => x.Total ?? 0m);
}

public sealed record CostWorkbook(
    string SourcePath,
    string ProjectName,
    IReadOnlyList<TaxInclusiveSummaryItem> TaxInclusiveSummary,
    IReadOnlyList<UnitProject> UnitProjects);

public sealed record UnitMatchDecision(
    WorkbookRole AnchorStage,
    int AnchorSheetIndex,
    WorkbookRole CandidateStage,
    int? CandidateSheetIndex);

public sealed record ItemMatchDecision(
    string UnitProjectName,
    WorkbookRole AnchorStage,
    int AnchorRowIndex,
    WorkbookRole CandidateStage,
    int? CandidateRowIndex);

public sealed record ComparisonSettings(decimal AmountTolerance, decimal QuantityTolerance)
{
    public IReadOnlyList<UnitMatchDecision> UnitDecisions { get; init; } = [];
    public IReadOnlyList<ItemMatchDecision> ItemDecisions { get; init; } = [];

    // Temporary source-compatibility projection for the pre-stage UI. The stage-aware engine
    // treats these as InitialReview decisions until that UI is migrated in its own task.
    public IReadOnlyList<ManualUnitLink> ManualUnitLinks { get; init; } = [];
    public IReadOnlyList<ManualItemLink> ManualItemLinks { get; init; } = [];

    public static ComparisonSettings Default => new(0.01m, 0.0001m);
}

public sealed record ManualUnitLink(string SubmissionUnitName, string ReviewUnitName);

public sealed record ManualItemLink(
    string UnitProjectName,
    string SubmissionCode,
    string SubmissionName,
    string ReviewCode,
    string ReviewName);

public sealed record UnitMatchConfirmation(
    string Id,
    string UnitProjectName,
    string Reason,
    UnitProject? Submission,
    UnitProject? InitialReview,
    UnitProject? FinalReview,
    IReadOnlyList<UnitProject> InitialCandidates,
    IReadOnlyList<UnitProject> FinalCandidates)
{
    public WorkbookRole AnchorStage { get; init; }
    public WorkbookRole CandidateStage { get; init; }
}

public sealed record ItemMatchConfirmation(
    string Id,
    string UnitProjectName,
    string Reason,
    BillItem? Submission,
    BillItem? InitialReview,
    BillItem? FinalReview,
    IReadOnlyList<BillItem> InitialCandidates,
    IReadOnlyList<BillItem> FinalCandidates);

public sealed record PairComparison(DifferenceKind Kind, MatchStatus Status, string AutomaticComment);

public sealed record SectionComparison(
    string Title,
    int Level,
    bool IsRoot,
    BillSection? Submission,
    BillSection? InitialReview,
    BillSection? FinalReview)
{
    public BillSection? Section(WorkbookRole role) => role switch
    {
        WorkbookRole.Submission => Submission,
        WorkbookRole.InitialReview => InitialReview,
        WorkbookRole.FinalReview => FinalReview,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public decimal Total(WorkbookRole role) => Section(role)?.Total ?? 0m;
}

public abstract record DetailLine(int OutlineLevel);

public sealed record SectionDetailLine(SectionComparison Section) : DetailLine(Section.Level);

public sealed record ItemDetailLine(ComparisonRow Row, int OutlineLevel) : DetailLine(OutlineLevel);

public sealed class ComparisonRow
{
    public BillItem? Submission { get; init; }
    public BillItem? InitialReview { get; init; }
    public BillItem? FinalReview { get; init; }
    public required IReadOnlyDictionary<DifferenceKind, PairComparison> PairComparisons { get; init; }
    public string AutomaticComment { get; init; } = string.Empty;
    public string UserComment { get; set; } = string.Empty;
    public string CombinedComment => string.Join("；", new[] { AutomaticComment, UserComment }
        .Where(x => !string.IsNullOrWhiteSpace(x)));

    public BillItem? Item(WorkbookRole role) => role switch
    {
        WorkbookRole.Submission => Submission,
        WorkbookRole.InitialReview => InitialReview,
        WorkbookRole.FinalReview => FinalReview,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public decimal Quantity(WorkbookRole role) => Item(role)?.Quantity ?? 0m;
    public decimal UnitPrice(WorkbookRole role) => Item(role)?.UnitPrice ?? 0m;
    public decimal Total(WorkbookRole role) => Item(role)?.Total ?? 0m;

    public BillItem? Review => InitialReview;
    public MatchStatus Status => PairComparisons.TryGetValue(DifferenceKind.InitialVsSubmission, out var comparison)
        ? comparison.Status
        : PairComparisons.Values.FirstOrDefault()?.Status ?? MatchStatus.NotApplicable;
    public decimal SubmissionQuantity => Quantity(WorkbookRole.Submission);
    public decimal SubmissionUnitPrice => UnitPrice(WorkbookRole.Submission);
    public decimal SubmissionTotal => Total(WorkbookRole.Submission);
    public decimal ReviewQuantity => Quantity(WorkbookRole.InitialReview);
    public decimal ReviewUnitPrice => UnitPrice(WorkbookRole.InitialReview);
    public decimal ReviewTotal => Total(WorkbookRole.InitialReview);
}

public sealed record UnitComparison(
    string Name,
    UnitProjectKind Kind,
    UnitProject? Submission,
    UnitProject? InitialReview,
    UnitProject? FinalReview,
    IReadOnlyList<ComparisonRow> Rows,
    IReadOnlyList<DetailLine> DetailLines,
    string SummaryNote)
{
    public UnitProject? Review => InitialReview;
}

public sealed record SummaryComparisonRow(
    string? SubmissionSequence,
    string Name,
    IReadOnlyDictionary<WorkbookRole, decimal> StageAmounts,
    string Note)
{
    public decimal SubmissionAmount => StageAmounts.GetValueOrDefault(WorkbookRole.Submission);
    public decimal ReviewAmount => StageAmounts.GetValueOrDefault(WorkbookRole.InitialReview);
}

public sealed record PendingConfirmation(
    string UnitProjectName,
    string Reason,
    IReadOnlyList<BillItem> SubmissionCandidates,
    IReadOnlyList<BillItem> ReviewCandidates)
{
    public IReadOnlyList<UnitProject> ReviewUnitCandidates { get; init; } = [];
}

public sealed record ComparisonSession(
    ComparisonRequest Request,
    IReadOnlyList<DifferenceKind> ActiveDifferences,
    IReadOnlyList<SummaryComparisonRow> SummaryRows,
    IReadOnlyList<UnitComparison> UnitComparisons,
    IReadOnlyList<UnitMatchConfirmation> UnitConfirmations,
    IReadOnlyList<ItemMatchConfirmation> ItemConfirmations,
    ComparisonSettings Settings)
{
    public CostWorkbook Submission => Request.Submission;
    public CostWorkbook Review => Request.InitialReview ?? Request.FinalReview!;
    public IReadOnlyList<PendingConfirmation> PendingConfirmations =>
        UnitConfirmations.Select(x => new PendingConfirmation(x.UnitProjectName, x.Reason, [], [])
        {
            ReviewUnitCandidates = x.InitialCandidates.Count > 0 ? x.InitialCandidates : x.FinalCandidates
        }).Concat(ItemConfirmations.Select(x => new PendingConfirmation(
            x.UnitProjectName,
            x.Reason,
            x.Submission is null ? [] : [x.Submission],
            x.InitialCandidates.Count > 0 ? x.InitialCandidates : x.FinalCandidates))).ToList();
}
