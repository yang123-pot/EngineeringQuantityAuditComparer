namespace EngineeringQuantityAuditComparer.Core;

public sealed record AnchoredEntry<T>(T Value, int? SubmissionIndex);

public static class AnchorOrderer
{
    public static IReadOnlyList<T> Merge<T>(
        IReadOnlyList<T> submissionOrder,
        params IReadOnlyList<AnchoredEntry<T>>[] stageSequences)
    {
        var before = Enumerable.Range(0, submissionOrder.Count + 1)
            .ToDictionary(x => x, _ => new List<T>());

        foreach (var sequence in stageSequences)
        {
            for (var index = 0; index < sequence.Count;)
            {
                if (sequence[index].SubmissionIndex is not null)
                {
                    index++;
                    continue;
                }

                var run = new List<T>();
                while (index < sequence.Count && sequence[index].SubmissionIndex is null)
                {
                    run.Add(sequence[index].Value);
                    index++;
                }

                var followingAnchor = index < sequence.Count ? sequence[index].SubmissionIndex : null;
                before[followingAnchor ?? submissionOrder.Count].AddRange(run);
            }
        }

        var result = new List<T>(submissionOrder.Count + before.Sum(x => x.Value.Count));
        for (var index = 0; index < submissionOrder.Count; index++)
        {
            result.AddRange(before[index]);
            result.Add(submissionOrder[index]);
        }

        result.AddRange(before[submissionOrder.Count]);
        return result;
    }
}
