namespace EngineeringQuantityAuditComparer.Core;

internal sealed class AlignedItem
{
    public BillItem? Submission { get; set; }
    public BillItem? InitialReview { get; set; }
    public BillItem? FinalReview { get; set; }
    public Dictionary<DifferenceKind, MatchStatus> MatchStatuses { get; } = [];
}

internal sealed record ItemMatchResult(
    IReadOnlyList<ComparisonRow> Rows,
    IReadOnlyList<ItemMatchConfirmation> Confirmations);

internal static class BillItemMatcher
{
    public static ItemMatchResult Match(
        AlignedUnit unit,
        IReadOnlyList<StagePair> activePairs,
        ComparisonSettings settings)
    {
        var submissionShells = (unit.Submission?.Items ?? [])
            .Select(x => new AlignedItem { Submission = x })
            .ToList();
        var initialSequence = new List<AnchoredEntry<AlignedItem>>();
        var finalSequence = new List<AnchoredEntry<AlignedItem>>();

        if (unit.InitialReview is not null)
        {
            AlignWithSubmission(
                WorkbookRole.InitialReview,
                unit.InitialReview.Items,
                submissionShells,
                initialSequence);
        }

        if (unit.FinalReview is not null)
        {
            AlignWithSubmission(
                WorkbookRole.FinalReview,
                unit.FinalReview.Items,
                submissionShells,
                finalSequence);

            ReconcileInitialAndFinal(initialSequence, finalSequence);
        }

        if (unit.Submission is null)
        {
            submissionShells.Clear();
        }

        var ordered = AnchorOrderer.Merge(submissionShells, initialSequence, finalSequence);
        var rows = ordered.Select(x => AutomaticCommentBuilder.Build(x, activePairs, settings)).ToList();
        return new ItemMatchResult(rows, []);
    }

    private static void AlignWithSubmission(
        WorkbookRole candidateStage,
        IReadOnlyList<BillItem> candidates,
        IReadOnlyList<AlignedItem> submissionShells,
        ICollection<AnchoredEntry<AlignedItem>> sequence)
    {
        var consumed = new HashSet<BillItem>(ReferenceEqualityComparer.Instance);
        var matchedByCandidate = new Dictionary<BillItem, AlignedItem>(ReferenceEqualityComparer.Instance);
        var submissionItems = submissionShells.Select(x => x.Submission!).ToList();

        foreach (var shell in submissionShells)
        {
            var anchor = shell.Submission!;
            var match = FindStrictMatch(anchor, submissionItems, candidates, consumed);
            var status = MatchStatus.EarlierOnly;

            if (match is not null)
            {
                consumed.Add(match.Candidate);
                SetStage(shell, candidateStage, match.Candidate);
                matchedByCandidate.Add(match.Candidate, shell);
                status = match.Status;
            }

            shell.MatchStatuses[PairKind(candidateStage)] = status;
        }

        foreach (var candidate in candidates)
        {
            if (matchedByCandidate.TryGetValue(candidate, out var matchedShell))
            {
                sequence.Add(new AnchoredEntry<AlignedItem>(matchedShell, FindReferenceIndex(submissionShells, matchedShell)));
                continue;
            }

            if (!consumed.Contains(candidate))
            {
                var shell = new AlignedItem();
                SetStage(shell, candidateStage, candidate);
                sequence.Add(new AnchoredEntry<AlignedItem>(shell, null));
            }
        }
    }

    private static void ReconcileInitialAndFinal(
        IList<AnchoredEntry<AlignedItem>> initialSequence,
        IList<AnchoredEntry<AlignedItem>> finalSequence)
    {
        var initialOnly = initialSequence.Where(x => x.SubmissionIndex is null &&
            x.Value.Submission is null && x.Value.InitialReview is not null).Select(x => x.Value).ToList();
        var finalOnly = finalSequence.Where(x => x.SubmissionIndex is null &&
            x.Value.Submission is null && x.Value.FinalReview is not null).Select(x => x.Value).ToList();
        var initialItems = initialOnly.Select(x => x.InitialReview!).ToList();
        var finalItems = finalOnly.Select(x => x.FinalReview!).ToList();
        var consumedFinalItems = new HashSet<BillItem>(ReferenceEqualityComparer.Instance);
        var consumedFinalShells = new HashSet<AlignedItem>(ReferenceEqualityComparer.Instance);

        foreach (var initialShell in initialOnly)
        {
            var match = FindStrictMatch(
                initialShell.InitialReview!,
                initialItems,
                finalItems,
                consumedFinalItems);
            var status = MatchStatus.EarlierOnly;

            if (match is not null)
            {
                var finalShell = finalOnly.Single(x => ReferenceEquals(x.FinalReview, match.Candidate));
                initialShell.FinalReview = match.Candidate;
                consumedFinalItems.Add(match.Candidate);
                consumedFinalShells.Add(finalShell);
                status = match.Status;
            }

            initialShell.MatchStatuses[DifferenceKind.FinalVsInitial] = status;
        }

        foreach (var finalShell in consumedFinalShells)
        {
            var index = FindReferenceIndex(finalSequence.Select(x => x.Value).ToList(), finalShell);
            if (index >= 0)
            {
                finalSequence.RemoveAt(index);
            }
        }
    }

    private static StrictMatch? FindStrictMatch(
        BillItem anchor,
        IReadOnlyList<BillItem> anchors,
        IReadOnlyList<BillItem> candidates,
        ISet<BillItem> consumed)
    {
        var strictKey = TextNormalizer.CompositeKey(anchor);
        if (!HasCompositeValue(strictKey))
        {
            return null;
        }

        var anchorsWithStrictKey = anchors
            .Where(x => TextNormalizer.CompositeKey(x) == strictKey)
            .ToList();
        var candidatesWithStrictKey = candidates
            .Where(x => !consumed.Contains(x) && TextNormalizer.CompositeKey(x) == strictKey)
            .ToList();
        var code = TextNormalizer.Normalize(anchor.Code);

        if (!string.IsNullOrEmpty(code))
        {
            var anchorsWithCode = anchorsWithStrictKey.Count(x => TextNormalizer.Normalize(x.Code) == code);
            var candidatesWithCode = candidatesWithStrictKey
                .Where(x => TextNormalizer.Normalize(x.Code) == code)
                .ToList();
            if (anchorsWithCode == 1 && candidatesWithCode.Count == 1)
            {
                return new StrictMatch(candidatesWithCode[0], MatchStatus.CodeMatched);
            }
        }

        if (anchorsWithStrictKey.Count != 1 || candidatesWithStrictKey.Count != 1)
        {
            return null;
        }

        var candidate = candidatesWithStrictKey[0];
        var status = TextNormalizer.Normalize(candidate.Code) == code
            ? MatchStatus.CodeMatched
            : MatchStatus.CodeChanged;
        return new StrictMatch(candidate, status);
    }

    private static DifferenceKind PairKind(WorkbookRole stage) => stage == WorkbookRole.InitialReview
        ? DifferenceKind.InitialVsSubmission
        : DifferenceKind.FinalVsSubmission;

    private static void SetStage(AlignedItem shell, WorkbookRole stage, BillItem item)
    {
        if (stage == WorkbookRole.InitialReview)
        {
            shell.InitialReview = item;
        }
        else if (stage == WorkbookRole.FinalReview)
        {
            shell.FinalReview = item;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    private static bool HasCompositeValue(string key) =>
        !string.IsNullOrEmpty(key.Replace("|", string.Empty, StringComparison.Ordinal));

    private static int FindReferenceIndex<T>(IReadOnlyList<T> items, T target) where T : class
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record StrictMatch(BillItem Candidate, MatchStatus Status);
}
