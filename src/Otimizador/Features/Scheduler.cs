using Otimizador.Core;

namespace Otimizador.Features;

internal enum ScheduleFrequency { Diaria, Semanal, Mensal }

internal enum MaintenanceTask { LimpezaRapida, LiberarMemoria, LimparDns, EsvaziarLixeira, ManutencaoCompleta, OtimizarUnidades }

internal sealed class Schedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public MaintenanceTask Task { get; set; } = MaintenanceTask.ManutencaoCompleta;
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Semanal;
    public int Hour { get; set; } = 12;
    public int Minute { get; set; }
    public DayOfWeek WeekDay { get; set; } = DayOfWeek.Monday;
    public int MonthDay { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }

    public static string TaskName(MaintenanceTask t) => t switch
    {
        MaintenanceTask.LimpezaRapida => "Limpeza rápida",
        MaintenanceTask.LiberarMemoria => "Liberar memória",
        MaintenanceTask.LimparDns => "Limpar cache DNS",
        MaintenanceTask.EsvaziarLixeira => "Esvaziar Lixeira",
        MaintenanceTask.ManutencaoCompleta => "Manutenção completa",
        MaintenanceTask.OtimizarUnidades => "Otimizar unidades (TRIM/desfragmentar)",
        _ => t.ToString(),
    };

    public string Describe()
    {
        var hm = $"{Hour:00}:{Minute:00}";
        return Frequency switch
        {
            ScheduleFrequency.Diaria => $"Todos os dias às {hm}",
            ScheduleFrequency.Semanal => $"{(WeekDay is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Todo" : "Toda")} {AppInfo.PtBr.DateTimeFormat.GetDayName(WeekDay)} às {hm}",
            _ => $"Todo dia {MonthDay} do mês às {hm}",
        };
    }

    /// <summary>Próxima ocorrência estritamente depois de <paramref name="after"/>.</summary>
    public DateTime ComputeNext(DateTime after)
    {
        var time = new TimeSpan(Hour, Minute, 0);
        switch (Frequency)
        {
            case ScheduleFrequency.Diaria:
            {
                var c = after.Date + time;
                return c > after ? c : c.AddDays(1);
            }
            case ScheduleFrequency.Semanal:
            {
                int delta = ((int)WeekDay - (int)after.DayOfWeek + 7) % 7;
                var c = after.Date.AddDays(delta) + time;
                return c > after ? c : c.AddDays(7);
            }
            default:
            {
                DateTime At(int y, int m) => new DateTime(y, m, Math.Min(MonthDay, DateTime.DaysInMonth(y, m))) + time;
                var c = At(after.Year, after.Month);
                if (c > after) return c;
                var n = after.AddMonths(1);
                return At(n.Year, n.Month);
            }
        }
    }
}

/// <summary>
/// Agendamentos persistentes (a versão anterior perdia tudo ao fechar o app).
/// Executam enquanto o app está aberto ou na bandeja; tarefas perdidas rodam ao abrir.
/// </summary>
internal sealed class Scheduler : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30_000 };
    private bool _running;

    public event Action<Schedule, MaintenanceSummary>? Ran;

    public Scheduler()
    {
        foreach (var s in Settings.Current.Schedules)
            s.NextRun ??= s.ComputeNext(DateTime.Now);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    public static List<Schedule> All => Settings.Current.Schedules;

    public static void Upsert(Schedule s)
    {
        s.NextRun = s.ComputeNext(DateTime.Now);
        var list = Settings.Current.Schedules;
        int i = list.FindIndex(x => x.Id == s.Id);
        if (i >= 0) list[i] = s; else list.Add(s);
        Settings.Save();
    }

    public static void Remove(Guid id)
    {
        Settings.Current.Schedules.RemoveAll(x => x.Id == id);
        Settings.Save();
    }

    public static void SetEnabled(Schedule s, bool enabled)
    {
        s.Enabled = enabled;
        if (enabled) s.NextRun = s.ComputeNext(DateTime.Now);
        Settings.Save();
    }

    private async Task TickAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            var now = DateTime.Now;
            foreach (var s in Settings.Current.Schedules.Where(s => s.Enabled && s.NextRun <= now).ToList())
            {
                Log.Info($"Agendamento iniciado: {s.Name}");
                var summary = await Maintenance.RunAsync(s.Task, null, CancellationToken.None);
                s.LastRun = DateTime.Now;
                s.NextRun = s.ComputeNext(DateTime.Now);
                Settings.Save();
                Ran?.Invoke(s, summary);
            }
        }
        catch (Exception ex) { Log.Error("Agendador", ex); }
        finally { _running = false; }
    }

    public void Dispose() => _timer.Dispose();
}
