namespace EngineeringQuantityAuditComparer.Core;

public sealed class ComparisonEngine : IComparisonEngine
{
    // Kept only while the existing two-file callers are migrated to ComparisonRequest.
    public ComparisonSession Compare(CostWorkbook submission, CostWorkbook review, ComparisonSettings settings) =>
        Compare(new ComparisonRequest(submission, review, null), settings);

    public ComparisonSession Compare(ComparisonRequest request, ComparisonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        request.Validate();

        var activePairs = ComparisonPairs.ActiveFor(request);
        var unitResult = UnitProjectMatcher.Match(request, settings);
        var unitComparisons = new List<UnitComparison>(unitResult.Units.Count);
        var itemConfirmations = new List<ItemMatchConfirmation>();

        foreach (var unit in unitResult.Units)
        {
            var itemResult = BillItemMatcher.Match(unit, activePairs, settings);
            itemConfirmations.AddRange(itemResult.Confirmations);
            var detailLines = SectionMatcher.Match(unit.Submission, unit.InitialReview, unit.FinalReview, itemResult.Rows);
            unitComparisons.Add(new UnitComparison(
                unit.Name,
                unit.Kind,
                unit.Submission,
                unit.InitialReview,
                unit.FinalReview,
                itemResult.Rows,
                detailLines,
                unit.SummaryNote(request)));
        }

        return new ComparisonSession(
            request,
            activePairs.Select(x => x.Kind).ToList(),
            ProjectSummary(request, unitResult.Units),
            unitComparisons,
            unitResult.Confirmations,
            itemConfirmations,
            settings);
    }

    private static IReadOnlyList<SummaryComparisonRow> ProjectSummary(
        ComparisonRequest request,
        IReadOnlyList<AlignedUnit> units)
    {
        var usedSubmission = new HashSet<int>();
        var usedInitial = new HashSet<int>();
        var usedFinal = new HashSet<int>();
        var rows = new List<SummaryComparisonRow>(units.Count);

        foreach (var unit in units)
        {
            var submission = FindAmount(request.Submission.TaxInclusiveSummary, unit.Submission?.Name, usedSubmission, out var submissionIndex);
            var initial = FindAmount(request.InitialReview?.TaxInclusiveSummary, unit.InitialReview?.Name, usedInitial, out _);
            var final = FindAmount(request.FinalReview?.TaxInclusiveSummary, unit.FinalReview?.Name, usedFinal, out _);
            var amounts = new Dictionary<WorkbookRole, decimal>
            {
                [WorkbookRole.Submission] = submission,
                [WorkbookRole.InitialReview] = initial,
                [WorkbookRole.FinalReview] = final
            };

            rows.Add(new SummaryComparisonRow(
                submissionIndex is int index ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                unit.Name,
                amounts,
                unit.SummaryNote(request)));
        }

        return rows;
    }

    private static decimal FindAmount(
        IReadOnlyList<TaxInclusiveSummaryItem>? items,
        string? unitName,
        ISet<int> used,
        out int? matchedIndex)
    {
        matchedIndex = null;
        if (items is null || string.IsNullOrWhiteSpace(unitName))
        {
            return 0m;
        }

        var normalizedName = TextNormalizer.Normalize(unitName);
        for (var index = 0; index < items.Count; index++)
        {
            if (!used.Contains(index) && TextNormalizer.Normalize(items[index].Name) == normalizedName)
            {
                used.Add(index);
                matchedIndex = index;
                return items[index].Amount;
            }
        }

        return 0m;
    }
}
