namespace Otimizador;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        if (!SingleInstance.TryAcquire()) return 0; // a instância aberta é trazida para a frente

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log.Error("Erro fatal", ex); };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Tarefa", e.Exception); e.SetObserved(); };

        Theme.InitScale();
        Theme.InitFonts();
        Settings.Load();
        Theme.Apply(Settings.Current.Theme);
        Log.Info($"Iniciado v{AppInfo.Version} (admin: {AppInfo.IsAdmin}, bandeja: {tray})");
        Migration.Run();

        // 1) Termos de uso — recusar apenas fecha o app (a versão antiga apagava a pasta inteira do .exe).
        if (Settings.Current.TermsAcceptedVersion != AppInfo.TermsVersion)
        {
            using var terms = new TermsForm();
            if (terms.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show("Para usar o " + AppInfo.Name + " é preciso aceitar os termos de uso.\n\n" +
                                "Para remover o programa, use Configurações › Aplicativos do Windows.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            Settings.Current.TermsAcceptedVersion = AppInfo.TermsVersion;
            Settings.Current.TermsAcceptedAt = DateTime.Now;
            Settings.Save();
        }

        // 2) Licença (assinatura digital verificada localmente)
        var license = LicenseService.Current();
        if (!license.AllowsUse)
        {
            string? reason = license.State == LicenseState.Expired ? "Sua assinatura venceu. Digite a nova chave de licença para continuar." : null;
            using var form = new LicenseForm(reason);
            if (form.ShowDialog() != DialogResult.OK) return 1;
            tray = false; // acabou de ativar: mostra a janela
        }

        var main = new MainForm(startHidden: tray);
        SingleInstance.Listen(() => main.RestoreFromTray());
        Application.Run(main);
        SingleInstance.Release();
        Log.Info("Encerrado.");
        return 0;
    }

    private static void Crash(Exception ex)
    {
        Log.Error("Erro não tratado", ex);
        MessageBox.Show("Ocorreu um erro inesperado, mas o app continua funcionando.\n\n" + ex.Message +
                        "\n\nDetalhes foram gravados no log (Configurações › Registro de atividades).",
            AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
