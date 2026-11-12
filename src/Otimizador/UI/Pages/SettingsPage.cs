namespace Otimizador.UI.Pages;

internal sealed class SettingsPage : Page
{
    private static readonly int[] Intervals = { 1, 3, 6, 12, 24 };
    private readonly ToggleSwitch _autoStart = new() { Enabled = false };
    private readonly SettingRow _licenseRow;
    private readonly Pill _licensePill = new("", Tone.Muted);
    private bool _loaded, _syncing;

    public SettingsPage() : base("Configurações", "Aparência, inicialização, manutenção, licença e suporte.")
    {
        var s = Settings.Current;

        // Aparência
        var theme = new UiCombo(190, "Seguir o Windows", "Claro", "Escuro")
        {
            SelectedIndex = s.Theme switch { "light" => 1, "dark" => 2, _ => 0 },
        };
        theme.SelectedIndexChanged += (_, _) =>
        {
            s.Theme = theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            Settings.Save();
            Theme.Apply(s.Theme);
        };
        Section("Aparência", new SettingRow(Icons.Moon, "Tema", "Claro, escuro ou igual ao do Windows.", Tone.Accent, theme));

        // Inicialização e bandeja
        _autoStart.CheckedChanged += async (_, _) =>
        {
            if (_syncing) return;
            _autoStart.Enabled = false;
            bool ok = await AutoStart.SetAsync(_autoStart.Checked);
            if (!ok) MainForm.Instance?.ShowToast("Não foi possível alterar a inicialização com o Windows. Veja o log.", Tone.Danger);
            await SyncAutoStartAsync();
        };
        var tray = new ToggleSwitch { Checked = s.CloseToTray };
        tray.CheckedChanged += (_, _) => { s.CloseToTray = tray.Checked; Settings.Save(); };
        Section("Inicialização",
            new SettingRow(Icons.Power, "Iniciar com o Windows", "Abre minimizado na bandeja ao entrar no Windows, para manter agendamentos e manutenção automática.", Tone.Accent, _autoStart),
            new SettingRow(Icons.Apps, "Manter na bandeja ao fechar", "O botão X esconde a janela em vez de encerrar o app.", Tone.Accent, tray));

        // Manutenção automática
        var auto = new ToggleSwitch { Checked = s.AutoMaintenance };
        var interval = new UiCombo(140, Intervals.Select(h => (object)(h == 1 ? "A cada 1 hora" : $"A cada {h} horas")).ToArray());
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, s.AutoMaintenanceHours) is var i && i >= 0 ? i : 2);
        auto.CheckedChanged += (_, _) =>
        {
            s.AutoMaintenance = auto.Checked;
            if (auto.Checked) s.LastAutoMaintenance ??= DateTime.Now;
            Settings.Save();
        };
        interval.SelectedIndexChanged += (_, _) => { s.AutoMaintenanceHours = Intervals[interval.SelectedIndex]; Settings.Save(); };
        Section("Manutenção automática",
            new SettingRow(Icons.Sync, "Ativar manutenção automática", "Limpa temporários, relatórios de erro e logs antigos periodicamente, e libera memória quando passa de 85%.", Tone.Success, auto),
            new SettingRow(Icons.Clock, "Frequência", "Intervalo entre as manutenções (o app precisa estar aberto ou na bandeja).", Tone.Success, interval));

        // Licença
        var change = new UiButton("Alterar chave", ButtonKind.Secondary, Icons.Edit);
        var remove = new UiButton("Remover", ButtonKind.Ghost, Icons.Delete);
        change.Click += (_, _) =>
        {
            using var f = new LicenseForm("Cole a nova chave de licença.", standalone: false);
            if (f.ShowDialog(FindForm()) == DialogResult.OK) { UpdateLicense(); MainForm.Instance?.ShowToast("Licença atualizada."); }
        };
        remove.Click += (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "Remover a licença deste computador? O app será fechado e pedirá a chave na próxima abertura.",
                    "Licença", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            s.LicenseKey = null;
            Settings.Save();
            Log.Info("Licença removida pelo usuário.");
            MainForm.Instance?.ExitApp();
        };
        _licenseRow = new SettingRow(Icons.Permissions, "Licença", "", Tone.Accent, change, remove);
        _licenseRow.AddPill(_licensePill);
        Section("Licença", _licenseRow);

        // Ajustes
        var undoAll = new UiButton("Desfazer tudo", ButtonKind.Secondary, Icons.Restore);
        undoAll.Click += async (_, _) =>
        {
            if (MessageBox.Show(FindForm(), "Desfazer todos os ajustes de desempenho e privacidade aplicados por este app, voltando aos valores anteriores?",
                    "Desfazer ajustes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            undoAll.Enabled = false;
            int n = await Tweaks.RevertAllAsync();
            undoAll.Enabled = true;
            MainForm.Instance?.ShowToast(n > 0 ? $"{n} ajuste(s) desfeito(s)." : "Nenhum ajuste ativo para desfazer.");
        };
        Section("Ajustes do Windows",
            new SettingRow(Icons.Restore, "Desfazer todos os ajustes", "Restaura os valores originais de tudo que foi alterado nas páginas Desempenho e Privacidade.", Tone.Warning, undoAll));

        // Atualizações
        var checkUpd = new ToggleSwitch { Checked = s.CheckUpdates };
        checkUpd.CheckedChanged += (_, _) => { s.CheckUpdates = checkUpd.Checked; Settings.Save(); };
        var checkNow = new UiButton("Verificar agora", ButtonKind.Secondary, Icons.Refresh);
        checkNow.Click += async (_, _) =>
        {
            checkNow.Enabled = false;
            try
            {
                var (update, reached) = await Updater.CheckAsync();
                if (!reached) MainForm.Instance?.ShowToast("Não foi possível verificar agora. Confira sua conexão.", Tone.Warning);
                else if (update == null) MainForm.Instance?.ShowToast("Você já está na versão mais recente.");
                else if (MessageBox.Show(FindForm(), $"A versão {update.Version} está disponível (você usa a {AppInfo.Version}). Abrir a página de download?",
                             "Atualização", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Shell.Open(update.Url);
            }
            finally { checkNow.Enabled = true; }
        };
        Section("Atualizações",
            new SettingRow(Icons.Sync, "Avisar sobre novas versões", "Uma vez por dia o app consulta o GitHub para saber se há versão nova. Nenhum dado seu é enviado e nada é instalado sozinho.", Tone.Accent, checkUpd),
            new SettingRow(Icons.Refresh, "Verificar agora", "Versão instalada: " + AppInfo.Version + ".", Tone.Accent, checkNow));

        // Sobre
        var help = new UiButton("Abrir", ButtonKind.Secondary, Icons.Help);
        help.Click += (_, _) => { using var v = new TextViewer("Ajuda — " + AppInfo.Name, AppInfo.ReadResource("Ajuda.txt")); v.ShowDialog(FindForm()); };
        var terms = new UiButton("Ler", ButtonKind.Secondary, Icons.Document);
        terms.Click += (_, _) => { using var t = new TermsForm(readOnly: true); t.ShowDialog(FindForm()); };
        var mail = new UiButton("Enviar e-mail", ButtonKind.Secondary, Icons.Mail);
        mail.Click += (_, _) =>
        {
            if (!Shell.Open($"mailto:{AppInfo.SupportEmail}?subject={Uri.EscapeDataString("Suporte " + AppInfo.Name + " " + AppInfo.Version)}"))
                MessageBox.Show(FindForm(), "Envie um e-mail para " + AppInfo.SupportEmail, "Suporte", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        var logs = new UiButton("Abrir pasta", ButtonKind.Secondary, Icons.FolderOpen);
        logs.Click += (_, _) => Shell.Open(AppInfo.LogDir);
        var uninstall = new UiButton("Desinstalar", ButtonKind.Ghost, Icons.Delete);
        uninstall.Click += async (_, _) => await UninstallAsync();
        var exit = new UiButton("Sair do app", ButtonKind.Ghost, Icons.Power);
        exit.Click += (_, _) => MainForm.Instance?.ExitApp();
        Section("Sobre",
            new SettingRow(Icons.Info, AppInfo.Name + " " + AppInfo.Version, "Leve e sem coleta de dados. A única conexão é a consulta opcional de novas versões.", Tone.Accent, exit),
            new SettingRow(Icons.Help, "Ajuda", "O que cada função faz e quando usar.", Tone.Accent, help),
            new SettingRow(Icons.Document, "Termos de uso", "Contrato de licença aceito na instalação.", Tone.Accent, terms),
            new SettingRow(Icons.Mail, "Suporte", AppInfo.SupportEmail + " · resposta em até 48 h úteis.", Tone.Accent, mail),
            new SettingRow(Icons.List, "Registro de atividades", "Arquivo de log com tudo o que o app alterou (útil para o suporte).", Tone.Accent, logs),
            new SettingRow(Icons.Delete, "Desinstalar", "Remove o app pelo desinstalador do Windows. Use \"Desfazer tudo\" antes, se quiser reverter os ajustes.", Tone.Danger, uninstall));

        UpdateLicense();
    }

    private void Section(string title, params SettingRow[] rows)
    {
        Content.Controls.Add(new TextLabel(title, Theme.H3));
        var card = new Card { Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6)), Gap = 0 };
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].ShowDivider = i > 0;
            card.Controls.Add(rows[i]);
        }
        Content.Controls.Add(card);
    }

    public override async void OnShown()
    {
        UpdateLicense();
        if (_loaded) return;
        _loaded = true;
        await SyncAutoStartAsync();
    }

    private async Task SyncAutoStartAsync()
    {
        bool on = await AutoStart.IsEnabledAsync();
        _syncing = true;
        _autoStart.Checked = on;
        _syncing = false;
        _autoStart.Enabled = AppInfo.IsAdmin;
    }

    private void UpdateLicense()
    {
        var st = LicenseService.Current();
        string holder = string.IsNullOrWhiteSpace(st.Holder) ? "" : st.Holder + " · ";
        _licenseRow.DescriptionLabel.Text = holder + (st.Lifetime ? "Vitalícia" : st.LastValidDay is DateTime d ? "Válida até " + d.ToString("dd/MM/yyyy") : "")
                                            + " · Chave " + LicenseService.Mask(Settings.Current.LicenseKey);
        _licensePill.Text = st.State switch
        {
            LicenseState.Valid => "Ativa",
            LicenseState.ExpiringSoon => "Vence em breve",
            LicenseState.Grace => "Vencida (carência)",
            _ => "Inválida",
        };
        _licensePill.Tone = st.State switch { LicenseState.Valid => Tone.Success, LicenseState.ExpiringSoon or LicenseState.Grace => Tone.Warning, _ => Tone.Danger };
        (FindForm() as MainForm)?.Controls.OfType<Sidebar>().FirstOrDefault()?.UpdateLicense();
    }

    private async Task UninstallAsync()
    {
        var unins = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (MessageBox.Show(FindForm(), "Desinstalar o " + AppInfo.Name + "?", "Desinstalar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await AutoStart.SetAsync(false);
        if (File.Exists(unins))
        {
            Shell.Open(unins);
            MainForm.Instance?.ExitApp();
        }
        else Shell.Open("ms-settings:appsfeatures");
    }
}
