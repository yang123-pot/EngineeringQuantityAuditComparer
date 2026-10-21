using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.App.ViewModels;

internal static class UnitMatchDraftService
{
    private readonly record struct UnitAnchorKey(WorkbookRole Stage, int SheetIndex);

    internal static IReadOnlyList<UnitMatchRowViewModel> Build(ComparisonSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var confirmations = session.UnitConfirmations.ToLookup(ConfirmationKey);
        var rows = new List<UnitMatchRowViewModel>(session.UnitComparisons.Count);
        foreach (var unit in session.UnitComparisons)
        {
            var blocking = RowKeys(unit)
                .SelectMany(key => confirmations[key])
                .DistinctBy(x => x.Id)
                .ToList();
            var blockedStages = blocking.Select(x => x.CandidateStage).ToHashSet();
            var reasons = PresenceNotes(
                    unit.Submission,
                    unit.InitialReview,
                    unit.FinalReview,
                    session.Request,
                    blockedStages)
                .Concat(blocking.Select(x => x.Reason))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal);
            rows.Add(new UnitMatchRowViewModel(
                unit.Submission,
                unit.InitialReview,
                unit.FinalReview,
                string.Join("、", reasons),
                blocking.Count > 0));
        }

        return rows;
    }

    internal static bool TryMove(
        IList<UnitMatchRowViewModel> rows,
        UnitMatchRowViewModel selected,
        WorkbookRole stage,
        int offset,
        ComparisonRequest request,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(request);

        if (stage is not WorkbookRole.InitialReview and not WorkbookRole.FinalReview)
        {
            error = "只能移动初审或终审单位工程。";
            return false;
        }

        if ((stage == WorkbookRole.InitialReview && !request.HasInitialReview) ||
            (stage == WorkbookRole.FinalReview && !request.HasFinalReview))
        {
            error = $"当前未选择{StageName(stage)}文件，不能移动单位工程。";
            return false;
        }

        if (offset is not -1 and not 1)
        {
            error = "单位工程只能上移或下移一行。";
            return false;
        }

        var selectedIndex = rows.IndexOf(selected);
        if (selectedIndex < 0)
        {
            error = "所选单位工程行已不存在，请重新选择。";
            return false;
        }

        if (selected.Unit(stage) is null)
        {
            error = $"第 {selectedIndex + 1} 行{StageName(stage)}单位工程为空，无法移动。";
            return false;
        }

        var targetIndex = selectedIndex + offset;
        if (targetIndex < 0)
        {
            error = "第一行不能上移。";
            return false;
        }

        if (targetIndex >= rows.Count)
        {
            error = "最后一行不能下移。";
            return false;
        }

        var target = rows[targetIndex];
        var selectedUnit = selected.Unit(stage);
        var targetUnit = target.Unit(stage);
        selected.SetUnit(stage, targetUnit);
        target.SetUnit(stage, selectedUnit);

        MarkManualAdjustment(selected, request);
        MarkManualAdjustment(target, request);

        if (selected.Submission is null && selected.InitialReview is null && selected.FinalReview is null)
        {
            rows.Remove(selected);
        }

        if (target.Submission is null && target.InitialReview is null && target.FinalReview is null)
        {
            rows.Remove(target);
        }

        return TryValidateCompleteness(rows.ToList(), request, out error);
    }

    internal static IReadOnlyList<UnitMatchDecision> CreateDecisions(
        IReadOnlyList<UnitMatchRowViewModel> rows,
        ComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(request);
        var decisions = new List<UnitMatchDecision>();
        foreach (var row in rows)
        {
            if (row.Submission is not null)
            {
                if (request.HasInitialReview)
                {
                    decisions.Add(new UnitMatchDecision(
                        WorkbookRole.Submission,
                        row.Submission.SourceSheetIndex,
                        WorkbookRole.InitialReview,
                        row.InitialReview?.SourceSheetIndex));
                }

                if (request.HasFinalReview)
                {
                    decisions.Add(new UnitMatchDecision(
                        WorkbookRole.Submission,
                        row.Submission.SourceSheetIndex,
                        WorkbookRole.FinalReview,
                        row.FinalReview?.SourceSheetIndex));
                }
            }
            else if (row.InitialReview is not null && request.HasFinalReview)
            {
                decisions.Add(new UnitMatchDecision(
                    WorkbookRole.InitialReview,
                    row.InitialReview.SourceSheetIndex,
                    WorkbookRole.FinalReview,
                    row.FinalReview?.SourceSheetIndex));
            }
        }

        return decisions;
    }

    internal static bool TryValidateCompleteness(
        IReadOnlyList<UnitMatchRowViewModel> rows,
        ComparisonRequest request,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(request);

        foreach (var stage in ActiveStages(request))
        {
            var sourceUnits = Workbook(request, stage)!.UnitProjects;
            var draftUnits = rows.Select(row => Unit(row, stage)).Where(unit => unit is not null).Cast<UnitProject>().ToList();
            var duplicate = draftUnits
                .GroupBy(unit => unit.SourceSheetIndex)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                var unit = duplicate.First();
                error = $"{StageName(stage)}单位工程工作表“{unit.SourceSheetName}”重复占用，请检查人工匹配。";
                return false;
            }

            var sourceIndexes = sourceUnits.Select(unit => unit.SourceSheetIndex).ToHashSet();
            var draftIndexes = draftUnits.Select(unit => unit.SourceSheetIndex).ToHashSet();
            if (!sourceIndexes.SetEquals(draftIndexes))
            {
                var missing = sourceUnits.FirstOrDefault(unit => !draftIndexes.Contains(unit.SourceSheetIndex));
                var extra = draftUnits.FirstOrDefault(unit => !sourceIndexes.Contains(unit.SourceSheetIndex));
                var sheetName = missing?.SourceSheetName ?? extra?.SourceSheetName ?? "未知工作表";
                error = $"{StageName(stage)}单位工程工作表“{sheetName}”缺失或来源不一致，请检查人工匹配。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    internal static bool MatchesSession(
        IReadOnlyList<UnitMatchRowViewModel> rows,
        ComparisonSession session,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(session);
        if (!TryValidateCompleteness(rows, session.Request, out error))
        {
            return false;
        }

        var draftKeys = rows
            .Where(row => row.Submission is not null || row.InitialReview is not null || row.FinalReview is not null)
            .Select(row => row.Key);
        var sessionKeys = session.UnitComparisons.Select(unit => new UnitMatchKey(
            unit.Submission?.SourceSheetIndex,
            unit.InitialReview?.SourceSheetIndex,
            unit.FinalReview?.SourceSheetIndex));
        if (!draftKeys.SequenceEqual(sessionKeys))
        {
            error = "匹配重算结果与页面不一致，已保留当前页面，请重新检查。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static UnitAnchorKey ConfirmationKey(UnitMatchConfirmation confirmation)
    {
        var anchor = confirmation.AnchorStage switch
        {
            WorkbookRole.Submission => confirmation.Submission,
            WorkbookRole.InitialReview => confirmation.InitialReview,
            WorkbookRole.FinalReview => confirmation.FinalReview,
            _ => null
        } ?? throw new InvalidOperationException("单位工程确认缺少锚点。");
        return new UnitAnchorKey(confirmation.AnchorStage, anchor.SourceSheetIndex);
    }

    private static IEnumerable<UnitAnchorKey> RowKeys(UnitComparison unit)
    {
        if (unit.Submission is not null)
        {
            yield return new UnitAnchorKey(WorkbookRole.Submission, unit.Submission.SourceSheetIndex);
        }

        if (unit.InitialReview is not null)
        {
            yield return new UnitAnchorKey(WorkbookRole.InitialReview, unit.InitialReview.SourceSheetIndex);
        }

        if (unit.FinalReview is not null)
        {
            yield return new UnitAnchorKey(WorkbookRole.FinalReview, unit.FinalReview.SourceSheetIndex);
        }
    }

    private static IReadOnlyList<string> PresenceNotes(
        UnitProject? submission,
        UnitProject? initialReview,
        UnitProject? finalReview,
        ComparisonRequest request,
        ISet<WorkbookRole>? blockedStages = null)
    {
        var notes = new List<string>(2);
        if (submission is null)
        {
            if (request.HasInitialReview && initialReview is not null)
            {
                notes.Add("初审新增");
            }

            if (request.HasFinalReview && finalReview is not null)
            {
                notes.Add("终审新增");
            }
        }
        else
        {
            if (request.HasInitialReview && initialReview is null &&
                blockedStages?.Contains(WorkbookRole.InitialReview) != true)
            {
                notes.Add("初审未给");
            }

            if (request.HasFinalReview && finalReview is null &&
                blockedStages?.Contains(WorkbookRole.FinalReview) != true)
            {
                notes.Add("终审未给");
            }
        }

        return notes;
    }

    private static void MarkManualAdjustment(UnitMatchRowViewModel row, ComparisonRequest request)
    {
        var presenceReason = string.Join("、", PresenceNotes(
            row.Submission,
            row.InitialReview,
            row.FinalReview,
            request));
        row.MarkManualAdjustment(presenceReason);
    }

    private static IEnumerable<WorkbookRole> ActiveStages(ComparisonRequest request)
    {
        yield return WorkbookRole.Submission;
        if (request.HasInitialReview)
        {
            yield return WorkbookRole.InitialReview;
        }

        if (request.HasFinalReview)
        {
            yield return WorkbookRole.FinalReview;
        }
    }

    private static CostWorkbook? Workbook(ComparisonRequest request, WorkbookRole stage) => stage switch
    {
        WorkbookRole.Submission => request.Submission,
        WorkbookRole.InitialReview => request.InitialReview,
        WorkbookRole.FinalReview => request.FinalReview,
        _ => null
    };

    private static UnitProject? Unit(UnitMatchRowViewModel row, WorkbookRole stage) => stage switch
    {
        WorkbookRole.Submission => row.Submission,
        WorkbookRole.InitialReview => row.InitialReview,
        WorkbookRole.FinalReview => row.FinalReview,
        _ => null
    };

    private static string StageName(WorkbookRole stage) => stage switch
    {
        WorkbookRole.Submission => "送审",
        WorkbookRole.InitialReview => "初审",
        WorkbookRole.FinalReview => "终审",
        _ => "未知阶段"
    };
}
