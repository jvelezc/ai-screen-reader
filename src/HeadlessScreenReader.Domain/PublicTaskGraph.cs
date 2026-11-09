using System.Collections.Immutable;
using System.Text.Json;

namespace HeadlessScreenReader.Domain;

public sealed record PublicTaskGraphNode(string Id, string DocumentUrl, PublicTaskFocus? Focus, bool FocusObserved);
public sealed record PublicTaskGraphEdge(int Sequence, string From, string To, string Action, double ElapsedMilliseconds,
    bool InputAttempted, bool InputCompleted);
public sealed record PublicTaskGraph(ImmutableArray<PublicTaskGraphNode> Nodes, ImmutableArray<PublicTaskGraphEdge> Edges)
{
    public static PublicTaskGraph Build(PublicTaskAttempt attempt)
    {
        var nodes = new Dictionary<string, PublicTaskGraphNode>(StringComparer.Ordinal);
        var edges = ImmutableArray.CreateBuilder<PublicTaskGraphEdge>();
        string Node(PublicTaskFocus? focus, string url)
        {
            var id = PublicTaskRules.Hash(JsonSerializer.Serialize(new { attempt.Id, Url = url, Focus = focus }));
            nodes.TryAdd(id, new(id, url, focus, focus is not null)); return id;
        }
        foreach (var item in attempt.Events)
            edges.Add(new(item.Sequence, Node(item.Before, item.DocumentUrl), Node(item.After, item.Action is "Enter" or "Pointer"
                ? attempt.FinalUrl ?? item.DocumentUrl : item.DocumentUrl), item.Action, item.ElapsedMilliseconds, item.InputAttempted, item.InputCompleted));
        return new(nodes.Values.ToImmutableArray(), edges.ToImmutable());
    }
}
