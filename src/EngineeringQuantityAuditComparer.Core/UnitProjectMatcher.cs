namespace EngineeringQuantityAuditComparer.Core;

internal sealed class AlignedUnit
{
    public UnitProject? Submission { get; set; }
    public UnitProject? InitialReview { get; set; }
    public UnitProject? FinalReview { get; set; }

    public string Name => Submission?.Name ?? InitialReview?.Name ?? FinalReview?.Name ?? string.Empty;
    public UnitProjectKind Kind => Submission?.Kind ?? InitialReview?.Kind ?? FinalReview?.Kind ?? UnitProjectKind.ContractPrice;
    public string SummaryNote(ComparisonRequest request)
    {
        var notes = new List<string>(2);
        if (Submission is null)
        {
            if (request.HasInitialReview && InitialReview is not null) notes.Add("初审新增");
            if (request.HasFinalReview && FinalReview is not null) notes.Add("终审新增");
            return string.Join("、", notes);
        }

        if (request.HasInitialReview && InitialReview is null) notes.Add("初审未给");
        if (request.HasFinalReview && FinalReview is null) notes.Add("终审未给");
        return string.Join("、", notes);
    }
}

internal sealed record UnitMatchResult(
    IReadOnlyList<AlignedUnit> Units,
    IReadOnlyList<UnitMatchConfirmation> Confirmations);

internal static class UnitProjectMatcher
{
    public static UnitMatchResult Match(ComparisonRequest request, ComparisonSettings settings)
    {
        var confirmations = new List<UnitMatchConfirmation>();
        var submissionShells = request.Submission.UnitProjects
            .Select(x => new AlignedUnit { Submission = x })
            .ToList();
        var initialSequence = new List<AnchoredEntry<AlignedUnit>>();
        var finalSequence = new List<AnchoredEntry<AlignedUnit>>();
        var initialReserved = ReservedCandidateSheets(settings, WorkbookRole.InitialReview, WorkbookRole.Submission);
        var finalReserved = ReservedCandidateSheets(settings, WorkbookRole.FinalReview, WorkbookRole.Submission, WorkbookRole.InitialReview);

        if (request.InitialReview is not null)
        {
            AlignWithSubmission(
                WorkbookRole.InitialReview,
                request.InitialReview.UnitProjects,
                submissionShells,
                settings,
                confirmations,
                initialSequence,
                initialReserved);
        }

        if (request.FinalReview is not null)
        {
            AlignWithSubmission(
                WorkbookRole.FinalReview,
                request.FinalReview.UnitProjects,
                submissionShells,
                settings,
                confirmations,
                finalSequence,
                finalReserved);

            ReconcileInitialAndFinal(
                initialSequence,
                finalSequence,
                settings,
                confirmations);
        }

        var ordered = AnchorOrderer.Merge(submissionShells, initialSequence, finalSequence);
        return new UnitMatchResult(ordered, confirmations);
    }

    private static void AlignWithSubmission(
        WorkbookRole candidateStage,
        IReadOnlyList<UnitProject> candidates,
        IReadOnlyList<AlignedUnit> submissionShells,
        ComparisonSettings settings,
        ICollection<UnitMatchConfirmation> confirmations,
        ICollection<AnchoredEntry<AlignedUnit>> sequence,
        ISet<int> reservedCandidateSheets)
    {
        var consumed = new HashSet<UnitProject>(ReferenceEqualityComparer.Instance);
        var matchedByCandidate = new Dictionary<UnitProject, AlignedUnit>(ReferenceEqualityComparer.Instance);
        var submissionNameCounts = submissionShells
            .GroupBy(x => TextNormalizer.Normalize(x.Submission!.Name))
            .ToDictionary(x => x.Key, x => x.Count());
        var candidateNameGroups = candidates
            .GroupBy(x => TextNormalizer.Normalize(x.Name))
            .ToDictionary(x => x.Key, x => x.ToList());

        for (var submissionIndex = 0; submissionIndex < submissionShells.Count; submissionIndex++)
        {
            var shell = submissionShells[submissionIndex];
            var anchor = shell.Submission!;
            var decision = settings.UnitDecisions.LastOrDefault(x =>
                x.AnchorStage == WorkbookRole.Submission &&
                x.AnchorSheetIndex == anchor.SourceSheetIndex &&
                x.CandidateStage == candidateStage);
            var hasDecision = decision is not null;
            UnitProject? matched = null;

            if (decision?.CandidateSheetIndex is int selectedIndex)
            {
                matched = candidates.FirstOrDefault(x =>
                    x.SourceSheetIndex == selectedIndex && !consumed.Contains(x));
            }
            else if (decision is null)
            {
                matched = LegacyManualCandidate(anchor, candidateStage, candidates, consumed, reservedCandidateSheets, settings);
                if (matched is null)
                {
                    var key = TextNormalizer.Normalize(anchor.Name);
                    if (submissionNameCounts[key] == 1 &&
                        candidateNameGroups.TryGetValue(key, out var exact) &&
                        exact.Count == 1 && !consumed.Contains(exact[0]) && !IsReserved(exact[0], reservedCandidateSheets))
                    {
                        matched = exact[0];
                    }
                }
            }

            if (matched is not null)
            {
                consumed.Add(matched);
                SetStage(shell, candidateStage, matched);
                matchedByCandidate.Add(matched, shell);
                continue;
            }

            if (hasDecision)
            {
                continue;
            }

            var normalized = TextNormalizer.Normalize(anchor.Name);
            var exactCandidates = candidates.Where(x => !consumed.Contains(x) && !IsReserved(x, reservedCandidateSheets) &&
                TextNormalizer.Normalize(x.Name) == normalized).ToList();
            var suggestions = exactCandidates.Count > 0
                ? exactCandidates
                : candidates.Where(x => !consumed.Contains(x) && !IsReserved(x, reservedCandidateSheets) && LikelySameName(normalized, TextNormalizer.Normalize(x.Name))).ToList();
            var reason = exactCandidates.Count > 1
                ? "单位工程名称重复，需人工确认"
                : "单位工程名称不完全一致，需人工确认";
            if (suggestions.Count > 0)
            {
                confirmations.Add(CreateConfirmation(
                    anchor,
                    WorkbookRole.Submission,
                    candidateStage,
                    reason,
                    suggestions));
            }
        }

        foreach (var candidate in candidates)
        {
            if (matchedByCandidate.TryGetValue(candidate, out var matchedShell))
            {
                sequence.Add(new AnchoredEntry<AlignedUnit>(matchedShell, FindReferenceIndex(submissionShells, matchedShell)));
                continue;
            }

            if (!consumed.Contains(candidate))
            {
                var shell = new AlignedUnit();
                SetStage(shell, candidateStage, candidate);
                sequence.Add(new AnchoredEntry<AlignedUnit>(shell, null));
            }
        }
    }

    private static void ReconcileInitialAndFinal(
        IList<AnchoredEntry<AlignedUnit>> initialSequence,
        IList<AnchoredEntry<AlignedUnit>> finalSequence,
        ComparisonSettings settings,
        ICollection<UnitMatchConfirmation> confirmations)
    {
        var initialOnly = initialSequence.Where(x => x.SubmissionIndex is null &&
            x.Value.Submission is null && x.Value.InitialReview is not null).Select(x => x.Value).ToList();
        var finalOnlyEntries = finalSequence.Where(x => x.SubmissionIndex is null &&
            x.Value.Submission is null && x.Value.FinalReview is not null).ToList();
        var consumedFinalShells = new HashSet<AlignedUnit>(ReferenceEqualityComparer.Instance);
        var reservedFinalSheets = ReservedCandidateSheets(
            settings,
            WorkbookRole.FinalReview,
            WorkbookRole.InitialReview);

        foreach (var initialShell in initialOnly)
        {
            var initial = initialShell.InitialReview!;
            var decision = settings.UnitDecisions.LastOrDefault(x =>
                x.AnchorStage == WorkbookRole.InitialReview &&
                x.AnchorSheetIndex == initial.SourceSheetIndex &&
                x.CandidateStage == WorkbookRole.FinalReview);
            AlignedUnit? finalShell = null;
            if (decision?.CandidateSheetIndex is int selectedIndex)
            {
                finalShell = finalOnlyEntries.Select(x => x.Value).FirstOrDefault(x =>
                    !consumedFinalShells.Contains(x) && x.FinalReview!.SourceSheetIndex == selectedIndex);
            }
            else if (decision is null)
            {
                var available = finalOnlyEntries.Select(x => x.Value).Where(x =>
                    !consumedFinalShells.Contains(x) &&
                    !IsReserved(x.FinalReview!, reservedFinalSheets)).ToList();
                var key = TextNormalizer.Normalize(initial.Name);
                var initialCount = initialOnly.Count(x => TextNormalizer.Normalize(x.InitialReview!.Name) == key);
                var exact = available.Where(x => TextNormalizer.Normalize(x.FinalReview!.Name) == key).ToList();
                if (initialCount == 1 && exact.Count == 1)
                {
                    finalShell = exact[0];
                }
                else
                {
                    var suggestions = exact.Count > 0
                        ? exact
                        : available.Where(x => LikelySameName(key, TextNormalizer.Normalize(x.FinalReview!.Name))).ToList();
                    if (suggestions.Count > 0)
                    {
                        confirmations.Add(CreateConfirmation(
                            initial,
                            WorkbookRole.InitialReview,
                            WorkbookRole.FinalReview,
                            initialCount > 1 || exact.Count > 1
                                ? "单位工程名称重复，需人工确认"
                                : "单位工程名称不完全一致，需人工确认",
                            suggestions.Select(x => x.FinalReview!).ToList()));
                    }
                }
            }

            if (finalShell is null)
            {
                continue;
            }

            initialShell.FinalReview = finalShell.FinalReview;
            consumedFinalShells.Add(finalShell);
        }

        foreach (var finalShell in consumedFinalShells)
        {
            var entryIndex = finalSequence.ToList().FindIndex(x => ReferenceEquals(x.Value, finalShell));
            if (entryIndex >= 0)
            {
                finalSequence.RemoveAt(entryIndex);
            }
        }
    }

    private static UnitProject? LegacyManualCandidate(
        UnitProject anchor,
        WorkbookRole stage,
        IEnumerable<UnitProject> candidates,
        ISet<UnitProject> consumed,
        ISet<int> reservedCandidateSheets,
        ComparisonSettings settings)
    {
        if (stage != WorkbookRole.InitialReview)
        {
            return null;
        }

        var link = settings.ManualUnitLinks.LastOrDefault(x =>
            TextNormalizer.Normalize(x.SubmissionUnitName) == TextNormalizer.Normalize(anchor.Name));
        if (link is null)
        {
            return null;
        }

        var matches = candidates.Where(x => !consumed.Contains(x) && !IsReserved(x, reservedCandidateSheets) &&
            TextNormalizer.Normalize(x.Name) == TextNormalizer.Normalize(link.ReviewUnitName)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static UnitMatchConfirmation CreateConfirmation(
        UnitProject anchor,
        WorkbookRole anchorStage,
        WorkbookRole candidateStage,
        string reason,
        IReadOnlyList<UnitProject> candidates)
    {
        var id = string.Join('|', anchor.Name, anchorStage, anchor.SourceSheetIndex, candidateStage, reason);
        return new UnitMatchConfirmation(
            id,
            anchor.Name,
            reason,
            anchorStage == WorkbookRole.Submission ? anchor : null,
            anchorStage == WorkbookRole.InitialReview ? anchor : null,
            anchorStage == WorkbookRole.FinalReview ? anchor : null,
            candidateStage == WorkbookRole.InitialReview ? candidates : [],
            candidateStage == WorkbookRole.FinalReview ? candidates : [])
        {
            AnchorStage = anchorStage,
            CandidateStage = candidateStage
        };
    }

    private static void SetStage(AlignedUnit shell, WorkbookRole stage, UnitProject unit)
    {
        if (stage == WorkbookRole.InitialReview)
        {
            shell.InitialReview = unit;
        }
        else if (stage == WorkbookRole.FinalReview)
        {
            shell.FinalReview = unit;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    private static bool LikelySameName(string left, string right) =>
        left.Length >= 3 && right.Length >= 3 &&
        (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));

    private static HashSet<int> ReservedCandidateSheets(
        ComparisonSettings settings,
        WorkbookRole candidateStage,
        params WorkbookRole[] anchorStages) => settings.UnitDecisions
        .Where(x => x.CandidateStage == candidateStage && x.CandidateSheetIndex is not null && anchorStages.Contains(x.AnchorStage))
        .Select(x => x.CandidateSheetIndex!.Value)
        .ToHashSet();

    private static bool IsReserved(UnitProject candidate, ISet<int> reservedCandidateSheets) =>
        candidate.SourceSheetIndex >= 0 && reservedCandidateSheets.Contains(candidate.SourceSheetIndex);

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
}
