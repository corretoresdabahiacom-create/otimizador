using System.Diagnostics;

namespace Otimizador.Core;

internal readonly record struct MemoryInfo(ulong Total, ulong Available, int LoadPercent)
{
    public ulong Used => Total > Available ? Total - Available : 0;
}

/// <summary>
/// Métricas leves: CPU por GetSystemTimes e memória por GlobalMemoryStatusEx.
/// Nada de WMI ou PerformanceCounter (que são lentos e pesados).
/// </summary>
internal sealed class SystemStats
{
    private long _idle, _kernel, _user;

    public SystemStats() => Native.GetSystemTimes(out _idle, out _kernel, out _user);

    /// <summary>Uso de CPU (%) desde a última chamada.</summary>
    public double SampleCpu()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long dIdle = idle - _idle, dKernel = kernel - _kernel, dUser = user - _user;
        _idle = idle; _kernel = kernel; _user = user;
        long total = dKernel + dUser; // kernel já inclui o tempo ocioso
        if (total <= 0) return 0;
        return Math.Clamp((total - dIdle) * 100.0 / total, 0, 100);
    }

    public static MemoryInfo Memory()
    {
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        return Native.GlobalMemoryStatusEx(ref m)
            ? new MemoryInfo(m.ullTotalPhys, m.ullAvailPhys, (int)m.dwMemoryLoad)
            : new MemoryInfo(0, 0, 0);
    }

    public static DriveInfo? SystemDrive()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return d.IsReady ? d : null;
        }
        catch { return null; }
    }

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public static int ProcessCount()
    {
        try
        {
            var ps = Process.GetProcesses();
            foreach (var p in ps) p.Dispose();
            return ps.Length;
        }
        catch { return 0; }
    }
}
