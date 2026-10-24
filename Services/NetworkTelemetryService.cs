using System.Diagnostics;
using System.Net.NetworkInformation;

namespace AgentsHub.Services;

/// <summary>
/// Samples network throughput on a background timer.
/// </summary>
public static class NetworkTelemetryService
{
    private const int SampleIntervalMs = 2000;
    private const int CounterRefreshMs = 30000;

    private static readonly Lock SampleLock = new();

    private static System.Threading.Timer? _sampler;
#if WINDOWS
    private static List<PerformanceCounter> _counters = [];
    private static DateTime _countersRefreshUtc = DateTime.MinValue;
#else
    private static long _lastTotalBytes;
    private static DateTime _lastSampleUtc = DateTime.UtcNow;
#endif

    // Written by the sampler thread, read by the UI thread. A plain int keeps the write atomic,
    // so the reader never needs the sample lock and can never be blocked by a slow pass.
    private static int _kiloBytesPerSec;

    /// <summary>Current combined network throughput in kilobytes per second.</summary>
    public static double NetworkKiloBytesPerSec => Volatile.Read(ref _kiloBytesPerSec);

    /// <summary>Starts the background sampler on first use. Safe to call repeatedly.</summary>
    public static void EnsureStarted()
    {
        if (_sampler != null) return;

        lock (SampleLock)
        {
            _sampler ??= new System.Threading.Timer(_ => Sample(), null, 0, SampleIntervalMs);
        }
    }

    private static void Sample()
    {
        lock (SampleLock)
        {
            try
            {
                SampleLocked();
            }
            catch
            {
                // Adapters can vanish mid-pass; keep the previous reading rather than failing.
            }
        }
    }

    private static void SampleLocked()
    {
#if WINDOWS
        var now = DateTime.UtcNow;
        if (_counters.Count == 0 || (now - _countersRefreshUtc).TotalMilliseconds >= CounterRefreshMs)
        {
            RefreshCountersLocked(now);
        }

        double totalBytesPerSec = 0.0;

        for (int i = _counters.Count - 1; i >= 0; i--)
        {
            try
            {
                totalBytesPerSec += _counters[i].NextValue();
            }
            catch
            {
                // Adapter disappeared; drop the counter and carry on.
                try { _counters[i].Dispose(); } catch { }
                _counters.RemoveAt(i);
            }
        }

        long kbps = (long)Math.Round(Math.Max(0.0, totalBytesPerSec) / 1024.0);
        Volatile.Write(ref _kiloBytesPerSec, (int)Math.Clamp(kbps, 0, int.MaxValue));
#else
        var now = DateTime.UtcNow;
        double elapsedSec = (now - _lastSampleUtc).TotalSeconds;
        if (elapsedSec <= 0) return;

        long currentTotalBytes = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = ni.GetIPStatistics();
                currentTotalBytes += stats.BytesReceived + stats.BytesSent;
            }

            if (_lastTotalBytes > 0 && currentTotalBytes >= _lastTotalBytes)
            {
                long deltaBytes = currentTotalBytes - _lastTotalBytes;
                long kbps = (long)Math.Round((deltaBytes / elapsedSec) / 1024.0);
                Volatile.Write(ref _kiloBytesPerSec, (int)Math.Clamp(kbps, 0, int.MaxValue));
            }
            _lastTotalBytes = currentTotalBytes;
            _lastSampleUtc = now;
        }
        catch { }
#endif
    }

#if WINDOWS
    /// <summary>
    /// Rebuilds the adapter counter set, reusing the instances that still exist so their delta
    /// baselines survive the refresh.
    /// </summary>
    private static void RefreshCountersLocked(DateTime now)
    {
        Dictionary<string, PerformanceCounter>? reusable = null;
        if (_counters.Count > 0)
        {
            reusable = new Dictionary<string, PerformanceCounter>(_counters.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var counter in _counters)
            {
                reusable[counter.InstanceName] = counter;
            }
        }

        string[] instances;
        try
        {
            instances = new PerformanceCounterCategory("Network Interface").GetInstanceNames();
        }
        catch
        {
            DisposeAll(_counters);
            _counters = [];
            return;
        }

        var rebuilt = new List<PerformanceCounter>(instances.Length);

        foreach (var instance in instances)
        {
            if (IsVirtualInterface(instance)) continue;

            if (reusable != null && reusable.TryGetValue(instance, out var existing))
            {
                rebuilt.Add(existing);
                reusable.Remove(instance);
                continue;
            }

            try
            {
                var counter = new PerformanceCounter("Network Interface", "Bytes Total/sec", instance, true);
                counter.NextValue(); // Establish the delta baseline; the value itself is meaningless.
                rebuilt.Add(counter);
            }
            catch
            {
                // Adapter disappeared between enumeration and creation.
            }
        }

        if (reusable != null)
        {
            DisposeAll(reusable.Values);
        }

        _counters = rebuilt;
        _countersRefreshUtc = now;
    }

    /// <summary>
    /// Filters out loopback, tunnel and virtual adapters so the reading reflects real traffic.
    /// </summary>
    private static bool IsVirtualInterface(string instanceName)
    {
        string[] excluded =
        [
            "loopback", "isatap", "teredo", "6to4", "tunnel", "pseudo",
            "vethernet", "vmware", "virtualbox", "hyper-v", "bluetooth"
        ];

        foreach (var token in excluded)
        {
            if (instanceName.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static void DisposeAll(IEnumerable<PerformanceCounter> counters)
    {
        foreach (var counter in counters)
        {
            try { counter.Dispose(); } catch { }
        }
    }
#endif
}
