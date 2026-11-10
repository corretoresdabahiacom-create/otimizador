using Otimizador.Core;

namespace Otimizador.Features;

internal sealed record MaintenanceSummary(long BytesFreed, int Files, long RamFreed, IReadOnlyList<string> Lines)
{
    public string Short()
    {
        var parts = new List<string>();
        if (BytesFreed > 0) parts.Add($"{Fmt.Bytes(BytesFreed)} de arquivos removidos");
        if (RamFreed > 0) parts.Add($"{Fmt.Bytes(RamFreed)} de memória liberada");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Tudo em ordem, nada a limpar.";
    }
}

/// <summary>Rotinas compostas usadas por "Otimizar agora", agendamentos e manutenção automática.</summary>
internal static class Maintenance
{
    public static async Task<MaintenanceSummary> RunAsync(MaintenanceTask task, Action<string>? log, CancellationToken ct)
    {
        var lines = new List<string>();
        void L(string s) { lines.Add(s); log?.Invoke(s); }
        long bytes = 0, ram = 0; int files = 0;

        async Task Clean(IEnumerable<CleanCategory> cats)
        {
            foreach (var c in cats.Where(c => c.Available))
            {
                ct.ThrowIfCancellationRequested();
                var r = await Task.Run(() => c.Clean(ct), ct);
                bytes += r.Bytes; files += r.Files;
                L($"✓ {c.Title}: {(r.Files > 0 ? $"{Fmt.Num(r.Files)} arquivo(s), {Fmt.Bytes(r.Bytes)}" : "nada a remover")}"
                  + (r.Note != null ? $" — {r.Note}" : ""));
            }
        }

        async Task Ram()
        {
            var m = await Memory.OptimizeAsync();
            ram += m.FreedBytes;
            L($"✓ Memória: {Fmt.Bytes(m.FreedBytes)} liberados de {m.Processes} processos");
        }

        async Task Dns()
        {
            var ok = await SystemTools.FlushDnsAsync();
            L(ok ? "✓ Cache DNS limpo" : "✗ Não foi possível limpar o cache DNS");
        }

        switch (task)
        {
            case MaintenanceTask.LimpezaRapida:
                await Clean(Cleaner.ForAuto);
                break;
            case MaintenanceTask.LiberarMemoria:
                await Ram();
                break;
            case MaintenanceTask.LimparDns:
                await Dns();
                break;
            case MaintenanceTask.EsvaziarLixeira:
                await Clean(Cleaner.Categories.Where(c => c.Id == "recycle"));
                break;
            case MaintenanceTask.ManutencaoCompleta:
                await Clean(Cleaner.Categories.Where(c => c.DefaultOn && c.Id != "recycle"));
                await Ram();
                await Dns();
                break;
            case MaintenanceTask.OtimizarUnidades:
                var ok = await SystemTools.OptimizeDrivesAsync(s => { }, ct);
                L(ok ? "✓ Unidades otimizadas" : "✗ Falha ao otimizar unidades");
                break;
        }

        var st = Settings.Current;
        st.TotalBytesFreed += bytes;
        st.LastOptimization = DateTime.Now;
        Settings.Save();
        Log.Info($"Manutenção '{task}': {Fmt.Bytes(bytes)} em {files} arquivos, RAM {Fmt.Bytes(ram)}");
        return new MaintenanceSummary(bytes, files, ram, lines);
    }

    /// <summary>Estimativa rápida do lixo (categorias marcadas por padrão), usada no Painel.</summary>
    public static Task<long> EstimateJunkAsync(CancellationToken ct) => Task.Run(() =>
    {
        long total = 0;
        foreach (var c in Cleaner.Categories.Where(c => c.DefaultOn && c.Available))
        {
            ct.ThrowIfCancellationRequested();
            try { total += c.Scan(ct).Bytes; } catch (OperationCanceledException) { throw; } catch { }
        }
        Settings.Current.LastJunkEstimate = total;
        Settings.Save();
        return total;
    }, ct);
}
