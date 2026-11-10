using Otimizador.UI;

namespace Otimizador.Features;

/// <summary>
/// Catálogo de ajustes. Todos são documentados, reversíveis e sem "mitos" de otimização
/// (removidos: LargeSystemCache, DisablePagingExecutive, limpeza do Prefetch, bloqueio de portas,
/// desativar Windows Search, remover apps da Microsoft, trocar DNS de adaptador fixo etc.).
/// </summary>
internal static class Catalog
{
    private const string Desktop = @"Control Panel\Desktop";
    private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static List<Tweak> Build() => new()
    {
        // ── DESEMPENHO ────────────────────────────────────────────────────
        new Tweak
        {
            Id = "power_high", Group = "desempenho", Glyph = Icons.Lightning,
            Title = "Plano de energia Alto desempenho",
            Description = "O processador não reduz a frequência para economizar energia. Ideal para PCs de mesa e jogos.",
            IsAppliedCustom = PowerPlans.IsHighPerformanceAsync,
            ApplyCustom = PowerPlans.SetHighPerformanceAsync,
            RevertCustom = PowerPlans.RestorePreviousAsync,
        },
        new Tweak
        {
            Id = "animations", Group = "desempenho", Glyph = Icons.Speed,
            Title = "Reduzir animações do Windows",
            Description = "Desliga animações de janelas, da barra de tarefas e o \"Aero Peek\". O sistema parece mais rápido.",
            RestartNote = "Vale após sair e entrar de novo na conta.",
            Values = new[]
            {
                new RegValue(Hive.User, Desktop + @"\WindowMetrics", "MinAnimate", "0", "1"),
                new RegValue(Hive.User, ExplorerAdv, "TaskbarAnimations", 0, 1),
                new RegValue(Hive.User, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, 1),
            },
        },
        new Tweak
        {
            Id = "menu_delay", Group = "desempenho", Glyph = Icons.List,
            Title = "Menus mais rápidos",
            Description = "Reduz o atraso para abrir submenus de 400 ms para 100 ms.",
            RestartNote = "Vale após sair e entrar de novo na conta.",
            Values = new[] { new RegValue(Hive.User, Desktop, "MenuShowDelay", "100", "400") },
        },
        new Tweak
        {
            Id = "startup_delay", Group = "desempenho", Glyph = Icons.Power,
            Title = "Sem atraso para apps da inicialização",
            Description = "O Windows espera alguns segundos antes de abrir os programas da inicialização. Este ajuste remove a espera.",
            Values = new[] { new RegValue(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0) },
        },
        new Tweak
        {
            Id = "hung_apps", Group = "desempenho", Glyph = Icons.Clock,
            Title = "Encerrar apps travados mais rápido",
            Description = "Reduz o tempo que o Windows espera por programas que não respondem ao desligar.",
            Values = new[]
            {
                new RegValue(Hive.User, Desktop, "WaitToKillAppTimeout", "5000"),
                new RegValue(Hive.User, Desktop, "HungAppTimeout", "3000"),
            },
        },
        new Tweak
        {
            Id = "game_mode", Group = "desempenho", Glyph = Icons.Game,
            Title = "Modo de Jogo do Windows",
            Description = "Prioriza o jogo aberto e evita que o Windows Update instale drivers ou reinicie durante a partida.",
            Values = new[]
            {
                new RegValue(Hive.User, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
                new RegValue(Hive.User, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
            },
        },
        new Tweak
        {
            Id = "game_dvr", Group = "desempenho", Glyph = Icons.Video,
            Title = "Desligar gravação de jogos em segundo plano",
            Description = "Desativa o Game DVR (Xbox Game Bar), que grava continuamente e consome GPU e disco.",
            Values = new[]
            {
                new RegValue(Hive.User, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                new RegValue(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
            },
        },
        new Tweak
        {
            Id = "foreground", Group = "desempenho", Glyph = Icons.Processing, RequiresAdmin = true,
            Title = "Prioridade para o programa em primeiro plano",
            Description = "Dá fatias de tempo maiores ao programa que você está usando (Win32PrioritySeparation = 38).",
            Values = new[] { new RegValue(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, 2) },
        },
        new Tweak
        {
            Id = "hags", Group = "desempenho", Glyph = Icons.Display, RequiresAdmin = true,
            Title = "Agendamento de GPU acelerado por hardware",
            Description = "Reduz a latência em jogos em placas de vídeo compatíveis (NVIDIA GTX 1000+, AMD RX 5000+, Intel Arc).",
            RestartNote = "Requer reiniciar o computador.",
            Values = new[] { new RegValue(Hive.Machine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) },
        },
        new Tweak
        {
            Id = "battery_cpu", Group = "economia", Glyph = Icons.Battery,
            Title = "Processador a 99% na bateria",
            Description = "Desliga o Turbo apenas na bateria: o notebook esquenta menos e a bateria dura mais. Na tomada nada muda.",
            IsAppliedCustom = async () => await PowerPlans.GetBatteryCpuMaxAsync() is int v && v < 100,
            ApplyCustom = () => PowerPlans.SetBatteryCpuMaxAsync(99),
            RevertCustom = () => PowerPlans.SetBatteryCpuMaxAsync(100),
        },

        // ── PRIVACIDADE ───────────────────────────────────────────────────
        new Tweak
        {
            Id = "telemetry", Group = "privacidade", Glyph = Icons.Shield, RequiresAdmin = true,
            Title = "Reduzir telemetria do Windows",
            Description = "Envia à Microsoft apenas os dados de diagnóstico obrigatórios.",
            Values = new[] { new RegValue(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0) },
        },
        new Tweak
        {
            Id = "ad_id", Group = "privacidade", Glyph = Icons.Contact,
            Title = "Desativar ID de publicidade",
            Description = "Impede que apps usem um identificador para mostrar anúncios personalizados.",
            Values = new[] { new RegValue(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1) },
        },
        new Tweak
        {
            Id = "suggestions", Group = "privacidade", Glyph = Icons.Info,
            Title = "Sem sugestões e anúncios no Iniciar",
            Description = "Desliga dicas, apps sugeridos e instalação automática de apps patrocinados.",
            Values = new[]
            {
                new RegValue(Hive.User, Cdm, "SystemPaneSuggestionsEnabled", 0, 1),
                new RegValue(Hive.User, Cdm, "SubscribedContent-338388Enabled", 0, 1),
                new RegValue(Hive.User, Cdm, "SubscribedContent-338389Enabled", 0, 1),
                new RegValue(Hive.User, Cdm, "SubscribedContent-353698Enabled", 0, 1),
                new RegValue(Hive.User, Cdm, "SilentlyInstalledAppsEnabled", 0, 1),
                new RegValue(Hive.User, Cdm, "SoftLandingEnabled", 0, 1),
            },
        },
        new Tweak
        {
            Id = "tailored", Group = "privacidade", Glyph = Icons.Permissions,
            Title = "Sem experiências personalizadas",
            Description = "A Microsoft não usa seus dados de diagnóstico para dicas e ofertas personalizadas.",
            Values = new[] { new RegValue(Hive.User, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1) },
        },
        new Tweak
        {
            Id = "web_search", Group = "privacidade", Glyph = Icons.Search,
            Title = "Pesquisa do Iniciar sem resultados da web",
            Description = "A busca do menu Iniciar procura só no seu computador (sem Bing).",
            RestartNote = "Vale após reiniciar o Explorador ou o PC.",
            Values = new[] { new RegValue(Hive.User, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1) },
        },
        new Tweak
        {
            Id = "activity", Group = "privacidade", Glyph = Icons.History, RequiresAdmin = true,
            Title = "Não registrar histórico de atividades",
            Description = "O Windows deixa de guardar e enviar a linha do tempo de apps e arquivos abertos.",
            Values = new[]
            {
                new RegValue(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new RegValue(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
            },
        },
        new Tweak
        {
            Id = "widgets", Group = "privacidade", Glyph = Icons.Globe, RequiresAdmin = true,
            Title = "Desativar Widgets / Notícias",
            Description = "Remove o painel de notícias e clima da barra de tarefas.",
            RestartNote = "Vale após reiniciar o PC.",
            Values = new[] { new RegValue(Hive.Machine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) },
        },
        new Tweak
        {
            Id = "copilot", Group = "privacidade", Glyph = Icons.Chat,
            Title = "Desativar Copilot do Windows",
            Description = "Oculta e desativa o Copilot integrado ao Windows.",
            RestartNote = "Vale após reiniciar o Explorador ou o PC.",
            Values = new[] { new RegValue(Hive.User, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1) },
        },
    };
}
