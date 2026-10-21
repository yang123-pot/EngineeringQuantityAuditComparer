using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

public sealed record StageColumns(WorkbookRole Stage, int Quantity, int UnitPrice, int Total);

public sealed record DifferenceColumns(
    DifferenceKind Kind,
    int Quantity,
    int UnitPrice,
    int Total,
    int TotalRate);

public sealed record DetailTarget(string SheetName, int Row);

public sealed record DetailColumnLayout(
    int FixedColumnCount,
    IReadOnlyDictionary<WorkbookRole, StageColumns> Stages,
    IReadOnlyDictionary<DifferenceKind, DifferenceColumns> Differences,
    int CommentColumn,
    int? MatchStatusColumn,
    int LastColumn)
{
    public static DetailColumnLayout Create(UnitProjectKind kind, bool hasInitialReview, bool hasFinalReview)
    {
        var fixedCount = kind == UnitProjectKind.ContractPrice ? 6 : 5;
        var next = fixedCount + 1;
        var stages = new Dictionary<WorkbookRole, StageColumns>();

        void AddStage(WorkbookRole stage)
        {
            stages.Add(stage, new StageColumns(stage, next, next + 1, next + 2));
            next += 3;
        }

        AddStage(WorkbookRole.Submission);
        if (hasInitialReview)
        {
            AddStage(WorkbookRole.InitialReview);
        }

        if (hasFinalReview)
        {
            AddStage(WorkbookRole.FinalReview);
        }

        var differences = new Dictionary<DifferenceKind, DifferenceColumns>();

        void AddDifference(DifferenceKind difference)
        {
            differences.Add(difference, new DifferenceColumns(difference, next, next + 1, next + 2, next + 3));
            next += 4;
        }

        if (hasInitialReview)
        {
            AddDifference(DifferenceKind.InitialVsSubmission);
        }

        if (hasFinalReview)
        {
            AddDifference(DifferenceKind.FinalVsSubmission);
        }

        var comment = next++;
        int? matchStatus = kind == UnitProjectKind.NewPrice ? next++ : null;
        return new DetailColumnLayout(fixedCount, stages, differences, comment, matchStatus, next - 1);
    }
}
