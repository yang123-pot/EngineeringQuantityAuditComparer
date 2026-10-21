namespace EngineeringQuantityAuditComparer.Core;

internal static class AutomaticCommentBuilder
{
    public static ComparisonRow Build(
        AlignedItem aligned,
        IReadOnlyList<StagePair> activePairs,
        ComparisonSettings settings)
    {
        var comparisons = activePairs.ToDictionary(
            x => x.Kind,
            x => BuildPair(aligned, x, settings));

        var chronological = new List<string>(2);
        if (comparisons.TryGetValue(DifferenceKind.InitialVsSubmission, out var initial) &&
            !string.IsNullOrWhiteSpace(initial.AutomaticComment))
        {
            chronological.Add(initial.AutomaticComment);
        }

        if (comparisons.ContainsKey(DifferenceKind.FinalVsSubmission) &&
            (aligned.Submission is not null || aligned.FinalReview is not null))
        {
            var initialIsProvided = aligned.InitialReview is not null && !IsNotProvided(aligned.InitialReview);
            var finalConclusion = initialIsProvided && comparisons.TryGetValue(DifferenceKind.FinalVsInitial, out var finalVsInitial)
                ? finalVsInitial.AutomaticComment
                : comparisons[DifferenceKind.FinalVsSubmission].AutomaticComment;
            if (!string.IsNullOrWhiteSpace(finalConclusion))
            {
                chronological.Add(finalConclusion);
            }
        }

        var automaticComment = activePairs.All(pair =>
            Total(aligned, pair.Earlier) == Total(aligned, pair.Later))
            ? string.Empty
            : string.Join("；", chronological);

        return new ComparisonRow
        {
            Submission = aligned.Submission,
            InitialReview = aligned.InitialReview,
            FinalReview = aligned.FinalReview,
            PairComparisons = comparisons,
            AutomaticComment = automaticComment
        };
    }

    private static PairComparison BuildPair(AlignedItem aligned, StagePair pair, ComparisonSettings settings)
    {
        var earlier = Item(aligned, pair.Earlier);
        var later = Item(aligned, pair.Later);
        var status = aligned.MatchStatuses.TryGetValue(pair.Kind, out var explicitStatus)
            ? explicitStatus
            : DeriveStatus(earlier, later);
        var comment = BuildPairComment(pair, earlier, later, settings);
        return new PairComparison(pair.Kind, status, comment);
    }

    private static string BuildPairComment(
        StagePair pair,
        BillItem? earlier,
        BillItem? later,
        ComparisonSettings settings)
    {
        var stage = pair.Later == WorkbookRole.InitialReview ? "初审" : "终审";
        if (later is null)
        {
            return earlier is null ? string.Empty : $"{stage}未计";
        }

        if (IsNotProvided(later))
        {
            return $"{stage}未给";
        }

        if (earlier is null)
        {
            return $"{stage}新增";
        }

        var prefix = pair.Kind == DifferenceKind.FinalVsSubmission ? "终审较送审" : stage;
        var changes = new List<string>(2);
        if (TextNormalizer.Normalize(earlier.Code) != TextNormalizer.Normalize(later.Code))
        {
            changes.Add("编码变更");
        }

        var quantityChanged = Math.Abs((later.Quantity ?? 0m) - (earlier.Quantity ?? 0m)) > settings.QuantityTolerance;
        var priceChanged = Math.Abs((later.UnitPrice ?? 0m) - (earlier.UnitPrice ?? 0m)) > settings.AmountTolerance;
        changes.AddRange((quantityChanged, priceChanged) switch
        {
            (true, true) => ["工程量及单价均调整"],
            (true, false) => ["工程量调整"],
            (false, true) => ["单价调整"],
            _ => []
        });

        return changes.Count == 0 ? string.Empty : prefix + string.Join("、", changes);
    }

    private static MatchStatus DeriveStatus(BillItem? earlier, BillItem? later)
    {
        if (earlier is null && later is null)
        {
            return MatchStatus.NotApplicable;
        }

        if (later is null)
        {
            return MatchStatus.EarlierOnly;
        }

        if (earlier is null)
        {
            return MatchStatus.LaterOnly;
        }

        return TextNormalizer.Normalize(earlier.Code) == TextNormalizer.Normalize(later.Code)
            ? MatchStatus.CodeMatched
            : MatchStatus.CodeChanged;
    }

    private static BillItem? Item(AlignedItem aligned, WorkbookRole role) => role switch
    {
        WorkbookRole.Submission => aligned.Submission,
        WorkbookRole.InitialReview => aligned.InitialReview,
        WorkbookRole.FinalReview => aligned.FinalReview,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static decimal Total(AlignedItem aligned, WorkbookRole role) => Item(aligned, role)?.Total ?? 0m;

    private static bool IsNotProvided(BillItem item) =>
        (item.Quantity ?? 0m) == 0m && (item.UnitPrice ?? 0m) == 0m;
}
