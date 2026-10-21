namespace EngineeringQuantityAuditComparer.Core;

public sealed record SimilarItemSummaryRow(
    string NormalizedKey,
    IReadOnlyList<string> ItemNames,
    string Feature,
    string Unit,
    int UnitProjectCount,
    decimal EarlierQuantity,
    decimal LaterQuantity,
    decimal QuantityIncrease,
    decimal QuantityDecrease,
    decimal QuantityNetChange,
    decimal EarlierTotal,
    decimal LaterTotal,
    decimal AmountIncrease,
    decimal AmountDecrease,
    decimal AmountNetChange,
    IReadOnlyList<string> UnitSystemNotes)
{
    public string DisplayName => string.Join("；", ItemNames);
    public string Note => string.Join("；", UnitSystemNotes);
}

public static class SimilarItemAggregator
{
    public static IReadOnlyList<SimilarItemSummaryRow> Aggregate(
        ComparisonSession session,
        DifferenceKind difference)
    {
        ArgumentNullException.ThrowIfNull(session);
        var pair = ComparisonPairs.ActiveFor(session.Request).SingleOrDefault(x => x.Kind == difference)
            ?? throw new ArgumentException("所选差异组不属于当前比较会话。", nameof(difference));
        var accumulators = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (var unit in session.UnitComparisons)
        {
            foreach (var row in unit.Rows)
            {
                var earlier = row.Item(pair.Earlier);
                var later = row.Item(pair.Later);
                var earlierKey = Key(earlier);
                var laterKey = Key(later);

                if (earlierKey is null && laterKey is null)
                {
                    continue;
                }

                if (earlierKey == laterKey)
                {
                    AddContribution(accumulators, earlierKey!, unit, pair, earlier, later);
                    continue;
                }

                if (earlierKey is not null)
                {
                    AddContribution(accumulators, earlierKey, unit, pair, earlier, null);
                }

                if (laterKey is not null)
                {
                    AddContribution(accumulators, laterKey, unit, pair, null, later);
                }
            }
        }

        return accumulators.Values
            .Where(x => x.QuantityImpact > session.Settings.QuantityTolerance
                || x.AmountImpact > session.Settings.AmountTolerance)
            .OrderByDescending(x => x.AmountImpact)
            .ThenByDescending(x => x.QuantityImpact)
            .ThenBy(x => x.NormalizedKey, StringComparer.Ordinal)
            .Select(x => x.ToSummary())
            .ToList();
    }

    private static string? Key(BillItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var normalizedFeature = TextNormalizer.Normalize(item.Feature);
        var descriptor = normalizedFeature.Length > 0
            ? normalizedFeature
            : TextNormalizer.Normalize(item.Name);
        return $"{descriptor}|{TextNormalizer.Normalize(item.Unit)}";
    }

    private static void AddContribution(
        IDictionary<string, Accumulator> accumulators,
        string key,
        UnitComparison unit,
        StagePair pair,
        BillItem? earlier,
        BillItem? later)
    {
        if (!accumulators.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator(key, earlier ?? later!);
            accumulators.Add(key, accumulator);
        }

        var earlierSystem = earlier is null ? string.Empty : SystemName(unit, pair.Earlier, earlier);
        var laterSystem = later is null ? string.Empty : SystemName(unit, pair.Later, later);
        var note = (earlier, later) switch
        {
            (null, not null) => $"{unit.Name}／新增至{laterSystem}",
            (not null, null) => $"{unit.Name}／从{earlierSystem}删除",
            _ when earlierSystem == laterSystem => $"{unit.Name}／{earlierSystem}",
            _ => $"{unit.Name}／{earlierSystem}→{laterSystem}"
        };

        accumulator.Add(unit, earlier, later, note);
    }

    private static UnitProject? Project(UnitComparison unit, WorkbookRole role) => role switch
    {
        WorkbookRole.Submission => unit.Submission,
        WorkbookRole.InitialReview => unit.InitialReview,
        WorkbookRole.FinalReview => unit.FinalReview,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static string SystemName(UnitComparison unit, WorkbookRole role, BillItem item)
    {
        var section = Project(unit, role)?.Sections
            .Where(x => !x.IsRoot && x.DirectItemSourceRows.Contains(item.SourceRowIndex))
            .OrderByDescending(x => x.Level)
            .ThenBy(x => x.SourceRowIndex)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(section?.Title) ? "未分组" : section.Title;
    }

    private sealed class Accumulator
    {
        private readonly List<string> _itemNames = [];
        private readonly HashSet<string> _itemNameSet = new(StringComparer.Ordinal);
        private readonly HashSet<UnitComparison> _units = new(ReferenceEqualityComparer.Instance);
        private readonly List<string> _notes = [];
        private readonly HashSet<string> _noteSet = new(StringComparer.Ordinal);

        public Accumulator(string normalizedKey, BillItem displayItem)
        {
            NormalizedKey = normalizedKey;
            Feature = displayItem.Feature;
            Unit = displayItem.Unit;
        }

        public string NormalizedKey { get; }
        public string Feature { get; }
        public string Unit { get; }
        public decimal EarlierQuantity { get; private set; }
        public decimal LaterQuantity { get; private set; }
        public decimal QuantityIncrease { get; private set; }
        public decimal QuantityDecrease { get; private set; }
        public decimal EarlierTotal { get; private set; }
        public decimal LaterTotal { get; private set; }
        public decimal AmountIncrease { get; private set; }
        public decimal AmountDecrease { get; private set; }
        public decimal QuantityImpact => QuantityIncrease + QuantityDecrease;
        public decimal AmountImpact => AmountIncrease + AmountDecrease;

        public void Add(UnitComparison unit, BillItem? earlier, BillItem? later, string note)
        {
            _units.Add(unit);
            AddItemName(earlier);
            AddItemName(later);
            if (_noteSet.Add(note))
            {
                _notes.Add(note);
            }

            var earlierQuantity = earlier?.Quantity ?? 0m;
            var laterQuantity = later?.Quantity ?? 0m;
            var earlierTotal = earlier?.Total ?? 0m;
            var laterTotal = later?.Total ?? 0m;
            var quantityDifference = laterQuantity - earlierQuantity;
            var amountDifference = laterTotal - earlierTotal;

            QuantityIncrease += Math.Max(quantityDifference, 0m);
            QuantityDecrease += Math.Max(-quantityDifference, 0m);
            AmountIncrease += Math.Max(amountDifference, 0m);
            AmountDecrease += Math.Max(-amountDifference, 0m);
            EarlierQuantity += earlierQuantity;
            LaterQuantity += laterQuantity;
            EarlierTotal += earlierTotal;
            LaterTotal += laterTotal;
        }

        public SimilarItemSummaryRow ToSummary() => new(
            NormalizedKey,
            _itemNames,
            Feature,
            Unit,
            _units.Count,
            EarlierQuantity,
            LaterQuantity,
            QuantityIncrease,
            QuantityDecrease,
            QuantityIncrease - QuantityDecrease,
            EarlierTotal,
            LaterTotal,
            AmountIncrease,
            AmountDecrease,
            AmountIncrease - AmountDecrease,
            _notes);

        private void AddItemName(BillItem? item)
        {
            if (item is not null && _itemNameSet.Add(item.Name))
            {
                _itemNames.Add(item.Name);
            }
        }
    }
}
