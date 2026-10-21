using ClosedXML.Excel;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal sealed record DashboardAuditScope(
    StagePair Pair,
    string AuditLabel,
    string ReductionHeader,
    string IncreaseHeader);

internal static class DashboardAuditScopes
{
    public static IReadOnlyList<DashboardAuditScope> TopTwenty(ComparisonRequest request) =>
        ComparisonPairs.OutputFor(request).Select(ToScope).ToList();

    public static IReadOnlyList<DashboardAuditScope> SimilarItems(ComparisonRequest request) =>
        TopTwenty(request);

    public static IReadOnlyList<WorkbookRole> ActiveStages(ComparisonRequest request)
    {
        var result = new List<WorkbookRole> { WorkbookRole.Submission };
        if (request.HasInitialReview) result.Add(WorkbookRole.InitialReview);
        if (request.HasFinalReview) result.Add(WorkbookRole.FinalReview);
        return result;
    }

    public static string StageTotalHeader(WorkbookRole stage) => stage switch
    {
        WorkbookRole.Submission => "送审不含税合价",
        WorkbookRole.InitialReview => "初审不含税合价",
        WorkbookRole.FinalReview => "终审结算不含税合价",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    public static XLColor RowFill(DifferenceKind difference) => difference switch
    {
        DifferenceKind.InitialVsSubmission => XLColor.FromHtml("#F2F8ED"),
        DifferenceKind.FinalVsSubmission => XLColor.FromHtml("#EAF2F8"),
        _ => throw new ArgumentOutOfRangeException(nameof(difference))
    };

    private static DashboardAuditScope ToScope(StagePair pair) => pair.Kind switch
    {
        DifferenceKind.InitialVsSubmission => new(pair,
            "初审阶段（送审－初审）", "初审审减金额", "初审审增金额"),
        DifferenceKind.FinalVsSubmission => new(pair,
            "终审结算（送审－终审）", "终审结算审减金额", "终审结算审增金额"),
        _ => throw new ArgumentOutOfRangeException(nameof(pair))
    };
}
