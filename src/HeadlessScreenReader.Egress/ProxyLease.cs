using System.Security.Cryptography;
using System.Text;

namespace HeadlessScreenReader.Egress;

/// <summary>Resource lease only; it cannot expand target or network authorization.</summary>
public sealed class ProxyLease(string token, TimeProvider time)
{
    private readonly byte[] _token = Encoding.ASCII.GetBytes(token);
    private long _lastPulse = time.GetTimestamp();
    private bool _seenPulse;
    private readonly object _gate = new();

    public bool Touch(string token)
    {
        if (!Matches(token)) return false;
        lock (_gate)
        {
            if (time.GetElapsedTime(_lastPulse) >= TimeSpan.FromSeconds(_seenPulse ? 15 : 60)) return false;
            _lastPulse = time.GetTimestamp(); _seenPulse = true;
        }
        return true;
    }

    public bool Matches(string token) => CryptographicOperations.FixedTimeEquals(_token, Encoding.ASCII.GetBytes(token));

    public bool Expired
    {
        get { lock (_gate) return time.GetElapsedTime(_lastPulse) >= TimeSpan.FromSeconds(_seenPulse ? 15 : 60); }
    }
}
