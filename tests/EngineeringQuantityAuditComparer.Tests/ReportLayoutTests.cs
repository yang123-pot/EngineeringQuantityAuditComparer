using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class ReportLayoutTests
{
    [Theory]
    [InlineData(UnitProjectKind.ContractPrice, true, false, 17, 17, 0)]
    [InlineData(UnitProjectKind.ContractPrice, false, true, 17, 17, 0)]
    [InlineData(UnitProjectKind.ContractPrice, true, true, 24, 24, 0)]
    [InlineData(UnitProjectKind.NewPrice, true, false, 17, 16, 17)]
    [InlineData(UnitProjectKind.NewPrice, false, true, 17, 16, 17)]
    [InlineData(UnitProjectKind.NewPrice, true, true, 24, 23, 24)]
    public void Computes_dynamic_detail_last_comment_and_status_columns(
        UnitProjectKind kind,
        bool hasInitial,
        bool hasFinal,
        int expectedLast,
        int expectedComment,
        int expectedStatus)
    {
        var layout = DetailColumnLayout.Create(kind, hasInitial, hasFinal);

        Assert.Equal(expectedLast, layout.LastColumn);
        Assert.Equal(expectedComment, layout.CommentColumn);
        Assert.Equal(expectedStatus, layout.MatchStatusColumn ?? 0);
    }

    [Fact]
    public void Three_stage_layout_keeps_only_submission_based_difference_groups()
    {
        var layout = DetailColumnLayout.Create(UnitProjectKind.ContractPrice, true, true);

        Assert.Equal(
            [DifferenceKind.InitialVsSubmission, DifferenceKind.FinalVsSubmission],
            layout.Differences.Keys);
        Assert.Equal((16, 17, 18, 19), Columns(layout.Differences[DifferenceKind.InitialVsSubmission]));
        Assert.Equal((20, 21, 22, 23), Columns(layout.Differences[DifferenceKind.FinalVsSubmission]));
    }

    private static (int Quantity, int UnitPrice, int Total, int TotalRate) Columns(DifferenceColumns columns) =>
        (columns.Quantity, columns.UnitPrice, columns.Total, columns.TotalRate);
}
