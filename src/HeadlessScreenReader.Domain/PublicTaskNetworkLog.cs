using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Bounded diagnostic observations. Callers supply time; this log has no I/O or completion authority.</summary>
public sealed class PublicTaskNetworkLog(string policy, DateTimeOffset startedAt)
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PublicTaskNetworkEvent> _requests = [];
    private readonly List<PublicTaskNetworkEvent> _events = [];
    private int _count, _sequence, _denials, _dropped;
    private double _elapsed;

    public int Begin(string url, string method, string resourceType, double elapsed)
    {
        lock (_gate)
        {
            if (_count >= 100000) { Drop(); return 0; }
            var id = ++_count;
            var entry = new PublicTaskNetworkEvent(0, id, 0, "Request",
                method.Length is > 0 and <= 16 && method.All(c => char.IsAsciiLetterUpper(c) || c == '-') ? method : "OTHER",
                Resource(resourceType), PublicTaskNetworkTrace.OriginOf(url), PublicTaskRules.Hash(url), null, null);
            // Retain metadata only for bounded tracked requests; totals and truncation remain explicit.
            if (_requests.Count < 2048) _requests.Add(id, entry);
            Append(entry, elapsed);
            return id;
        }
    }

    public void Record(int id, string phase, double elapsed, int? status = null, string? code = null)
    {
        lock (_gate)
        {
            if (phase == "PolicyDenied") _denials = Math.Min(100000, _denials + 1);
            if (!_requests.TryGetValue(id, out var request)) { Drop(); return; }
            Append(request with { Phase = phase, Status = status, Code = code }, elapsed);
            if (phase is "Finished" or "Failed") _requests.Remove(id);
        }
    }

    private void Append(PublicTaskNetworkEvent entry, double elapsed)
    {
        _elapsed = Math.Max(_elapsed, double.IsFinite(elapsed) ? Math.Clamp(elapsed, 0, 180000) : 0);
        entry = entry with { Sequence = ++_sequence, ElapsedMilliseconds = _elapsed };
        if (_events.Count < PublicTaskNetworkTrace.MaximumEvents) { _events.Add(entry); return; }
        Drop();
        if (entry.Code is not null)
        {
            var replace = _events.FindIndex(e => e.Code is null);
            if (replace >= 0) { _events.RemoveAt(replace); _events.Add(entry); }
        }
    }
    private void Drop() => _dropped = Math.Min(1000000, _dropped + 1);
    public PublicTaskNetworkTrace Snapshot()
    {
        lock (_gate) return new(PublicTaskNetworkTrace.CurrentSchema, policy, startedAt,
            _count, _denials, _dropped, _events.ToImmutableArray());
    }
    private static string Resource(string value) => value is "document" or "stylesheet" or "image" or "media" or "font" or "script" or "texttrack"
        or "xhr" or "fetch" or "eventsource" or "websocket" or "manifest" or "beacon" or "ping" or "cspreport" or "connect" ? value : "other";
}
