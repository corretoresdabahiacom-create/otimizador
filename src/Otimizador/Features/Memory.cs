using System.Diagnostics;
using Otimizador.Core;

namespace Otimizador.Features;

internal readonly record struct MemoryOutcome(long FreedBytes, int Processes, MemoryInfo After);

/// <summary>
/// "Liberar memória": reduz o conjunto de trabalho (working set) dos processos, devolvendo
/// páginas pouco usadas ao Windows. Mede a memória disponível antes e depois — o número
/// mostrado é real (a versão anterior só limpava a memória do próprio otimizador).
/// </summary>
internal static class Memory
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "services", "lsass",
        "audiodg", "dwm", "MsMpEng", "fontdrvhost",
    };

    public static Task<MemoryOutcome> OptimizeAsync() => Task.Run(() =>
    {
        var before = SystemStats.Memory();
        int trimmed = 0;
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.Id <= 4 || Skip.Contains(p.ProcessName)) continue;
                    var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_QUOTA, false, p.Id);
                    if (h == IntPtr.Zero) continue;
                    try { if (Native.EmptyWorkingSet(h)) trimmed++; }
                    finally { Native.CloseHandle(h); }
                }
                catch { /* processo encerrou ou sem acesso */ }
            }
        }
        Thread.Sleep(600); // dá tempo ao gerenciador de memória atualizar as listas
        var after = SystemStats.Memory();
        long freed = (long)after.Available - (long)before.Available;
        return new MemoryOutcome(Math.Max(0, freed), trimmed, after);
    });
}
