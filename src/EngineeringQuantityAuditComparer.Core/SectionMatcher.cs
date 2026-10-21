namespace EngineeringQuantityAuditComparer.Core;

internal static class SectionMatcher
{
    public static IReadOnlyList<DetailLine> Match(
        UnitProject? submission,
        UnitProject? initial,
        UnitProject? final,
        IReadOnlyList<ComparisonRow> rows)
    {
        if (!(submission?.HasSubtitleStructure == true ||
              initial?.HasSubtitleStructure == true ||
              final?.HasSubtitleStructure == true))
        {
            return rows.Select(x => (DetailLine)new ItemDetailLine(x, 0)).ToList();
        }

        var rowIndices = new Dictionary<ComparisonRow, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < rows.Count; index++)
        {
            rowIndices.Add(rows[index], index);
        }
        var submissionNodes = BuildNodes(submission, WorkbookRole.Submission, rows, rowIndices);
        var initialNodes = BuildNodes(initial, WorkbookRole.InitialReview, rows, rowIndices);
        var finalNodes = BuildNodes(final, WorkbookRole.FinalReview, rows, rowIndices);
        var shells = AlignSections(submissionNodes, initialNodes, finalNodes);

        var submissionRoot = submission?.Sections.FirstOrDefault(x => x.IsRoot);
        var initialRoot = initial?.Sections.FirstOrDefault(x => x.IsRoot);
        var finalRoot = final?.Sections.FirstOrDefault(x => x.IsRoot);
        var root = new SectionComparison("整个工程", 0, true, submissionRoot, initialRoot, finalRoot);
        var lines = new List<DetailLine> { new SectionDetailLine(root) };

        var ownerByRow = new Dictionary<ComparisonRow, SectionShell>(ReferenceEqualityComparer.Instance);
        AssignOwners(ownerByRow, shells, WorkbookRole.Submission);
        AssignOwners(ownerByRow, shells, WorkbookRole.InitialReview);
        AssignOwners(ownerByRow, shells, WorkbookRole.FinalReview);

        var emitted = new HashSet<SectionShell>(ReferenceEqualityComparer.Instance);
        var ownershipShells = new HashSet<SectionShell>(ownerByRow.Values, ReferenceEqualityComparer.Instance);
        var hasOwnedDescendant = new HashSet<SectionShell>(ownershipShells, ReferenceEqualityComparer.Instance);
        foreach (var shell in ownershipShells)
        {
            for (var parent = shell.Parent; parent is not null; parent = parent.Parent)
            {
                hasOwnedDescendant.Add(parent);
            }
        }

        var emptyByBoundary = shells.Where(x => !hasOwnedDescendant.Contains(x))
            .GroupBy(x => EmptyBoundary(x, rows.Count))
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(StageRank).ThenBy(y => y.SourceOrdinal).ThenBy(y => y.Level).ToList());

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            EmitEmptyAt(rowIndex, emptyByBoundary, emitted, lines);
            var row = rows[rowIndex];
            if (ownerByRow.TryGetValue(row, out var owner))
            {
                EmitSection(owner, emitted, lines);
                lines.Add(new ItemDetailLine(row, owner.Level + 1));
            }
            else
            {
                lines.Add(new ItemDetailLine(row, 1));
            }
        }

        EmitEmptyAt(rows.Count, emptyByBoundary, emitted, lines);
        foreach (var shell in shells.OrderBy(StageRank).ThenBy(x => x.SourceOrdinal).ThenBy(x => x.Level))
        {
            EmitSection(shell, emitted, lines);
        }

        return lines;
    }

    private static List<SectionNode> BuildNodes(
        UnitProject? unit,
        WorkbookRole role,
        IReadOnlyList<ComparisonRow> rows,
        IReadOnlyDictionary<ComparisonRow, int> rowIndices)
    {
        if (unit is null)
        {
            return [];
        }

        var bySourceRow = new Dictionary<int, ComparisonRow>();
        foreach (var row in rows)
        {
            var item = row.Item(role);
            if (item is not null && item.SourceRowIndex >= 0)
            {
                bySourceRow[item.SourceRowIndex] = row;
            }
        }

        var nodes = new List<SectionNode>();
        var stack = new Stack<SectionNode>();
        var nonRoot = unit.Sections.Where(x => !x.IsRoot).OrderBy(x => x.SourceRowIndex).ToList();
        for (var ordinal = 0; ordinal < nonRoot.Count; ordinal++)
        {
            var section = nonRoot[ordinal];
            while (stack.Count > 0 && stack.Peek().Section.Level >= section.Level)
            {
                stack.Pop();
            }

            var node = new SectionNode(section, role, ordinal)
            {
                Parent = stack.Count == 0 ? null : stack.Peek()
            };
            node.Parent?.Children.Add(node);
            foreach (var sourceRow in section.DirectItemSourceRows)
            {
                if (bySourceRow.TryGetValue(sourceRow, out var row))
                {
                    node.DirectRows.Add(row);
                }
            }

            nodes.Add(node);
            stack.Push(node);
        }

        foreach (var node in nodes.OrderByDescending(x => x.Section.Level))
        {
            foreach (var row in node.DirectRows)
            {
                node.EffectiveRowIndices.Add(rowIndices[row]);
                if (IsMatchedAnchor(row, role))
                {
                    node.MatchedAnchorIndices.Add(rowIndices[row]);
                }
            }

            foreach (var child in node.Children)
            {
                node.EffectiveRowIndices.UnionWith(child.EffectiveRowIndices);
                node.MatchedAnchorIndices.UnionWith(child.MatchedAnchorIndices);
            }
        }

        return nodes;
    }

    private static List<SectionShell> AlignSections(
        IReadOnlyList<SectionNode> submission,
        IReadOnlyList<SectionNode> initial,
        IReadOnlyList<SectionNode> final)
    {
        var shells = submission.Select(x => new SectionShell { Submission = x }).ToList();
        var nodeShells = new Dictionary<SectionNode, SectionShell>(ReferenceEqualityComparer.Instance);
        foreach (var shell in shells)
        {
            nodeShells.Add(shell.Submission!, shell);
        }

        AddStage(initial, WorkbookRole.InitialReview, shells, nodeShells);
        AddStage(final, WorkbookRole.FinalReview, shells, nodeShells);

        foreach (var shell in shells)
        {
            var preferred = shell.Submission ?? shell.InitialReview ?? shell.FinalReview;
            if (preferred?.Parent is not null && nodeShells.TryGetValue(preferred.Parent, out var parent))
            {
                shell.Parent = parent;
            }
        }

        return shells;
    }

    private static void AddStage(
        IReadOnlyList<SectionNode> nodes,
        WorkbookRole role,
        ICollection<SectionShell> shells,
        IDictionary<SectionNode, SectionShell> nodeShells)
    {
        var available = shells.Where(x => role == WorkbookRole.InitialReview
                ? x.InitialReview is null
                : x.FinalReview is null)
            .GroupBy(SectionKey)
            .ToDictionary(x => x.Key, x => new Queue<SectionShell>(x));

        foreach (var node in nodes)
        {
            var key = NodeKey(node);
            SectionShell shell;
            if (available.TryGetValue(key, out var candidates) && candidates.Count > 0)
            {
                shell = candidates.Dequeue();
            }
            else
            {
                shell = new SectionShell();
                shells.Add(shell);
            }

            if (role == WorkbookRole.InitialReview)
            {
                shell.InitialReview = node;
            }
            else
            {
                shell.FinalReview = node;
            }

            nodeShells[node] = shell;
        }
    }

    private static void AssignOwners(
        IDictionary<ComparisonRow, SectionShell> owners,
        IEnumerable<SectionShell> shells,
        WorkbookRole role)
    {
        foreach (var shell in shells)
        {
            var node = shell.Node(role);
            if (node is null)
            {
                continue;
            }

            foreach (var row in node.DirectRows)
            {
                owners.TryAdd(row, shell);
            }
        }
    }

    private static void EmitEmptyAt(
        int boundary,
        IReadOnlyDictionary<int, List<SectionShell>> emptyByBoundary,
        ISet<SectionShell> emitted,
        ICollection<DetailLine> lines)
    {
        if (!emptyByBoundary.TryGetValue(boundary, out var shells))
        {
            return;
        }

        foreach (var shell in shells)
        {
            EmitSection(shell, emitted, lines);
        }
    }

    private static void EmitSection(
        SectionShell shell,
        ISet<SectionShell> emitted,
        ICollection<DetailLine> lines)
    {
        if (emitted.Contains(shell))
        {
            return;
        }

        if (shell.Parent is not null)
        {
            EmitSection(shell.Parent, emitted, lines);
        }

        if (emitted.Add(shell))
        {
            lines.Add(new SectionDetailLine(shell.Comparison));
        }
    }

    private static int EmptyBoundary(SectionShell shell, int rowCount)
    {
        var anchors = shell.EffectiveRowIndices;
        return anchors.Count == 0 ? rowCount : Math.Min(rowCount, anchors.Max() + 1);
    }

    private static int StageRank(SectionShell shell) => shell.Submission is not null
        ? 0
        : shell.InitialReview is not null ? 1 : 2;

    private static string SectionKey(SectionShell shell)
    {
        var node = shell.Submission ?? shell.InitialReview ?? shell.FinalReview!;
        return NodeKey(node);
    }

    private static string NodeKey(SectionNode node) =>
        $"{TextNormalizer.Normalize(node.Section.Title)}|{AnchorInterval(node.MatchedAnchorIndices)}";

    private static string AnchorInterval(SortedSet<int> anchors) => anchors.Count == 0
        ? "none"
        : $"{anchors.Min}:{anchors.Max}";

    private static bool IsMatchedAnchor(ComparisonRow row, WorkbookRole role) => row.Item(role) is not null &&
        (role != WorkbookRole.Submission && row.Submission is not null ||
         role != WorkbookRole.InitialReview && row.InitialReview is not null ||
         role != WorkbookRole.FinalReview && row.FinalReview is not null);

    private sealed class SectionNode(BillSection section, WorkbookRole role, int ordinal)
    {
        public BillSection Section { get; } = section;
        public WorkbookRole Role { get; } = role;
        public int Ordinal { get; } = ordinal;
        public SectionNode? Parent { get; set; }
        public List<SectionNode> Children { get; } = [];
        public List<ComparisonRow> DirectRows { get; } = [];
        public SortedSet<int> EffectiveRowIndices { get; } = [];
        public SortedSet<int> MatchedAnchorIndices { get; } = [];
    }

    private sealed class SectionShell
    {
        public SectionNode? Submission { get; set; }
        public SectionNode? InitialReview { get; set; }
        public SectionNode? FinalReview { get; set; }
        public SectionShell? Parent { get; set; }
        public SectionNode Preferred => Submission ?? InitialReview ?? FinalReview!;
        public int Level => Preferred.Section.Level;
        public int SourceOrdinal => Preferred.Ordinal;
        public SortedSet<int> EffectiveRowIndices
        {
            get
            {
                var indices = new SortedSet<int>();
                if (Submission is not null) indices.UnionWith(Submission.EffectiveRowIndices);
                if (InitialReview is not null) indices.UnionWith(InitialReview.EffectiveRowIndices);
                if (FinalReview is not null) indices.UnionWith(FinalReview.EffectiveRowIndices);
                return indices;
            }
        }

        public SectionComparison Comparison => new(
            Preferred.Section.Title,
            Preferred.Section.Level,
            false,
            Submission?.Section,
            InitialReview?.Section,
            FinalReview?.Section);

        public SectionNode? Node(WorkbookRole role) => role switch
        {
            WorkbookRole.Submission => Submission,
            WorkbookRole.InitialReview => InitialReview,
            WorkbookRole.FinalReview => FinalReview,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
    }
}
