using System.Net.NetworkInformation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;

namespace DeskGrid.Island;

/// <summary>
/// The state of this computer for one line: processor use, memory use and network speed. Each reading is measured against
/// the one before it, so it is only meaningful when asked for about once a second; the first reading has no speed yet.
/// </summary>
internal sealed class SystemStatus
{
    private (ulong Idle, ulong Total)? _cpu;
    private (long Received, long Sent, TimeSpan At)? _network;
    private string _last = "";

    /// <summary>The line to show now, kept as it was for a reading that came too soon after the last.</summary>
    public string Read(TimeSpan now)
    {
        if (_network is { } before && now - before.At < TimeSpan.FromSeconds(1) && _last.Length > 0)
        {
            return _last;
        }

        var cpu = CpuPercent();
        var memory = MemoryPercent();
        var (received, sent) = NetworkBytes();
        var speed = "";
        if (_network is { } earlier)
        {
            var seconds = Math.Max(0.5, (now - earlier.At).TotalSeconds);
            speed = $" · ↓{Rate((received - earlier.Received) / seconds)} ↑{Rate((sent - earlier.Sent) / seconds)}";
        }

        _network = (received, sent, now);
        return _last = $"CPU {cpu:0}% · RAM {memory:0}%{speed}";
    }

    /// <summary>"1.2 MB/s", "340 KB/s", "12 B/s".</summary>
    internal static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:0.#} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{Math.Max(0, bytesPerSecond):0} B/s",
    };

    private double CpuPercent()
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var (i, total) = (ToUlong(idle), ToUlong(kernel) + ToUlong(user)); // Kernel time includes idle time.
        var result = 0.0;
        if (_cpu is { } before && total > before.Total)
        {
            result = 100.0 * (1 - (double)(i - before.Idle) / (total - before.Total));
        }

        _cpu = (i, total);
        return Math.Clamp(result, 0, 100);
    }

    private static ulong ToUlong(System.Runtime.InteropServices.ComTypes.FILETIME time) => ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    private static unsafe double MemoryPercent()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        return PInvoke.GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : 0;
    }

    private static (long Received, long Sent) NetworkBytes()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var statistics = adapter.GetIPv4Statistics();
                    (received, sent) = (received + statistics.BytesReceived, sent + statistics.BytesSent);
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No answer from the adapters: no speed this time.
        }

        return (received, sent);
    }
}
