namespace EngineeringQuantityAuditComparer.Core;

public sealed record SimilarItemStageAmount(
    decimal Reduction,
    decimal Increase,
    IReadOnlyList<string> UnitSystemNotes);

public sealed record SimilarItemAmountRow(
    string NormalizedKey,
    IReadOnlyList<string> ItemNames,
    string Feature,
    string Unit,
    IReadOnlyDictionary<DifferenceKind, SimilarItemStageAmount> StageAmounts)
{
    public string DisplayName => string.Join("；", ItemNames);
    public decimal TotalImpact => StageAmounts.Values.Sum(x => x.Reduction + Math.Abs(x.Increase));
}

public static class SimilarItemAmountAggregator
{
    public static IReadOnlyList<SimilarItemAmountRow> Aggregate(
        ComparisonSession session,
        IReadOnlyList<DifferenceKind> differences)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(differences);

        var requested = differences.Distinct().ToList();
        var active = ComparisonPairs.OutputFor(session.Request).Select(x => x.Kind).ToHashSet();
        if (requested.Count == 0 || requested.Any(x => !active.Contains(x)))
        {
            throw new ArgumentException("所选差异组不属于当前比较会话。", nameof(differences));
        }

        var accumulators = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var difference in requested)
        {
            foreach (var source in SimilarItemAggregator.Aggregate(session, difference))
            {
                if (!accumulators.TryGetValue(source.NormalizedKey, out var accumulator))
                {
                    accumulator = new Accumulator(source);
                    accumulators.Add(source.NormalizedKey, accumulator);
                }

                accumulator.Add(difference, source);
            }
        }

        return accumulators.Values
            .Select(x => x.ToRow(requested))
            .Where(x => x.TotalImpact > session.Settings.AmountTolerance)
            .OrderByDescending(x => x.TotalImpact)
            .ThenBy(x => x.NormalizedKey, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class Accumulator
    {
        private readonly List<string> _itemNames = [];
        private readonly HashSet<string> _itemNameSet = new(StringComparer.Ordinal);
        private readonly Dictionary<DifferenceKind, SimilarItemStageAmount> _stageAmounts = [];

        public Accumulator(SimilarItemSummaryRow source)
        {
            NormalizedKey = source.NormalizedKey;
            Feature = source.Feature;
            Unit = source.Unit;
        }

        public string NormalizedKey { get; }
        public string Feature { get; }
        public string Unit { get; }

        public void Add(DifferenceKind difference, SimilarItemSummaryRow source)
        {
            foreach (var name in source.ItemNames)
            {
                if (_itemNameSet.Add(name))
                {
                    _itemNames.Add(name);
                }
            }

            _stageAmounts[difference] = new SimilarItemStageAmount(
                source.AmountDecrease,
                -source.AmountIncrease,
                source.UnitSystemNotes);
        }

        public SimilarItemAmountRow ToRow(IReadOnlyList<DifferenceKind> differences)
        {
            var amounts = differences.ToDictionary(
                difference => difference,
                difference => _stageAmounts.TryGetValue(difference, out var value)
                    ? value
                    : new SimilarItemStageAmount(0m, 0m, []));
            return new SimilarItemAmountRow(
                NormalizedKey,
                _itemNames,
                Feature,
                Unit,
                amounts);
        }
    }
}
