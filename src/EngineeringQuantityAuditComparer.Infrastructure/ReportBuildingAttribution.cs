using System.Text.RegularExpressions;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static partial class ReportBuildingAttribution
{
    public static string ForRow(UnitComparison unit, ComparisonRow row, WorkbookRole later)
    {
        var submission = ForItem(unit.Submission, row.Submission);
        var review = ForItem(later == WorkbookRole.InitialReview ? unit.InitialReview : unit.FinalReview,
            row.Item(later));
        if (submission.Length == 0) return review;
        if (review.Length == 0 || submission == review) return submission;
        return "楼栋归属待核查";
    }

    private static string ForItem(UnitProject? unit, BillItem? item)
    {
        if (unit is null || item is null) return string.Empty;

        var fallback = BuildingCode(unit.Name);
        if (fallback.Length == 0)
        {
            fallback = unit.Name.Contains("室外", StringComparison.Ordinal)
                ? "室外工程"
                : $"未识别楼栋：{unit.Name}";
        }

        var hierarchy = new Stack<(int Level, string Building)>();
        foreach (var section in unit.Sections.Where(x => !x.IsRoot).OrderBy(x => x.SourceRowIndex))
        {
            while (hierarchy.Count > 0 && hierarchy.Peek().Level >= section.Level)
            {
                hierarchy.Pop();
            }

            var ownBuilding = BuildingCode(section.Title);
            var building = ownBuilding.Length > 0 ? ownBuilding
                : hierarchy.Count > 0 ? hierarchy.Peek().Building : fallback;
            if (section.DirectItemSourceRows.Contains(item.SourceRowIndex)) return building;
            hierarchy.Push((section.Level, building));
        }

        return fallback;
    }

    private static string BuildingCode(string value)
    {
        var match = BuildingPrefix().Match(value.Trim());
        return match.Success
            ? match.Groups[1].Value.Replace('－', '-').Replace('—', '-').Replace('＃', '#').Replace(" ", "", StringComparison.Ordinal)
            : string.Empty;
    }

    [GeneratedRegex(@"^(\d+(?:[-－—]\d+)*\s*[#＃])")]
    private static partial Regex BuildingPrefix();
}
