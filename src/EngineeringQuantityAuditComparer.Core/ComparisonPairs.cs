namespace EngineeringQuantityAuditComparer.Core;

public static class ComparisonPairs
{
    private static readonly StagePair InitialVsSubmission = new(
        DifferenceKind.InitialVsSubmission,
        WorkbookRole.Submission,
        WorkbookRole.InitialReview,
        "送审-初审",
        "送审-初审");

    private static readonly StagePair FinalVsInitial = new(
        DifferenceKind.FinalVsInitial,
        WorkbookRole.InitialReview,
        WorkbookRole.FinalReview,
        "终审-初审",
        "初审-终审");

    private static readonly StagePair FinalVsSubmission = new(
        DifferenceKind.FinalVsSubmission,
        WorkbookRole.Submission,
        WorkbookRole.FinalReview,
        "送审-终审",
        "送审-终审");

    public static IReadOnlyList<StagePair> ActiveFor(ComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pairs = new List<StagePair>(3);
        if (request.HasInitialReview)
        {
            pairs.Add(InitialVsSubmission);
        }

        if (request.HasInitialReview && request.HasFinalReview)
        {
            pairs.Add(FinalVsInitial);
        }

        if (request.HasFinalReview)
        {
            pairs.Add(FinalVsSubmission);
        }

        return pairs;
    }

    public static IReadOnlyList<StagePair> OutputFor(ComparisonRequest request) =>
        ActiveFor(request)
            .Where(pair => pair.Kind != DifferenceKind.FinalVsInitial)
            .ToList();

    public static decimal OutputDifference(StagePair pair, Func<WorkbookRole, decimal> amount)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(amount);
        return amount(WorkbookRole.Submission) - amount(pair.Later);
    }
}
