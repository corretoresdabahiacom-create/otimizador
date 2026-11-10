namespace Otimizador.UI.Pages;

internal sealed class ToolsPage : Page
{
    private readonly LogView _log = new() { Height = Theme.S(220) };
    private readonly Card _logCard = new() { Visible = false };
    private readonly TextLabel _logTitle = new("Saída", Theme.H3);
    private readonly UiButton _cancel = new("Cancelar", ButtonKind.Secondary, Icons.Cancel) { Visible = false };
    private readonly StackPanel _security = new() { Gap = 0 };
    private readonly Pill _secPill = new("…", Tone.Muted);
    private readonly List<UiButton> _runButtons = new();
    private CancellationTokenSource? _cts;
    private bool _secLoaded;

    public ToolsPage() : base("Ferramentas", "Utilitários do próprio Windows, com a saída mostrada aqui.")
    {
        // Segurança
        var sec = new Card { Padding = new Padding(Theme.S(20), Theme.S(16), Theme.S(20), Theme.S(8)), Gap = Theme.S(4) };
        var head = new Row { FixedHeight = Theme.S(30) };
        head.SetFill(new TextLabel("Proteção do Windows", Theme.H3));
        var openSec = new UiButton("Segurança do Windows", ButtonKind.Ghost, Icons.Open);
        openSec.Click += (_, _) => Shell.Open("windowsdefender:");
        head.AddRight(_secPill, openSec);
        sec.Controls.Add(head);
        sec.Controls.Add(_security);
        Content.Controls.Add(sec);

        // Ferramentas
        var grid = new Grid { MinColumnWidth = Theme.S(260), MaxColumns = 3 };
        grid.Controls.Add(Tool(Icons.Disk, "Otimizar unidades", "TRIM em SSD e desfragmentação em HD (o mesmo que o Windows faz semanalmente).", Tone.Accent,
            "Otimizar", ct => SystemTools.OptimizeDrivesAsync(_log.AddLine, ct), cancellable: true));
        grid.Controls.Add(Tool(Icons.Search, "Verificar disco", "Procura erros no sistema de arquivos sem precisar reiniciar (chkdsk /scan).", Tone.Warning,
            "Verificar", ct => SystemTools.CheckDiskAsync(_log.AddLine, ct), cancellable: true));
        grid.Controls.Add(Tool(Icons.Repair, "Reparar o Windows", "DISM + SFC: corrige arquivos do sistema corrompidos. Abre em uma janela própria (pode levar 30 min).", Tone.Danger,
            "Abrir reparo", _ => Task.FromResult(SystemTools.OpenSystemRepair()), showLog: false));
        grid.Controls.Add(Tool(Icons.Restore, "Ponto de restauração", "Cria um ponto para voltar o Windows a este estado se algo der errado.", Tone.Success,
            "Criar", _ => SystemTools.CreateRestorePointAsync(_log.AddLine)));
        grid.Controls.Add(Tool(Icons.Globe, "Limpar cache DNS", "Resolve sites que não abrem depois de mudanças na rede.", Tone.Accent,
            "Limpar", _ => SystemTools.FlushDnsAsync(_log.AddLine)));
        grid.Controls.Add(Tool(Icons.Network, "Redefinir rede", "Redefine Winsock e TCP/IP. Use quando a internet não funciona. Requer reiniciar.", Tone.Warning,
            "Redefinir", async _ =>
            {
                if (MessageBox.Show(FindForm(), "A rede será redefinida e será preciso reiniciar o computador. Continuar?", "Redefinir rede",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return true;
                return await SystemTools.ResetNetworkAsync(_log.AddLine);
            }));
        grid.Controls.Add(Tool(Icons.Clean, "Limpeza de Disco do Windows", "Abre o utilitário oficial (inclui limpar arquivos de atualização antigos).", Tone.Success,
            "Abrir", _ => Task.FromResult(Shell.Open(Shell.Sys("cleanmgr.exe"))), showLog: false));
        grid.Controls.Add(Tool(Icons.Sync, "Windows Update", "Verifique e instale atualizações de segurança.", Tone.Accent,
            "Abrir", _ => Task.FromResult(Shell.Open("ms-settings:windowsupdate")), showLog: false));
        grid.Controls.Add(Tool(Icons.Display, "Drivers", "Abre o Gerenciador de Dispositivos. Para atualizar, prefira o Windows Update ou o site do fabricante.", Tone.Muted,
            "Abrir", _ => Task.FromResult(Shell.Open(Shell.Sys("devmgmt.msc"))), showLog: false));
        Content.Controls.Add(grid);

        var lh = new Row { FixedHeight = Theme.S(36) };
        lh.SetFill(_logTitle);
        lh.AddRight(_cancel);
        _cancel.Click += (_, _) => _cts?.Cancel();
        _logCard.Controls.Add(lh);
        _logCard.Controls.Add(_log);
        Content.Controls.Add(_logCard);
    }

    public override async void OnShown()
    {
        if (_secLoaded) return;
        _secLoaded = true;
        await LoadSecurityAsync();
    }

    private async Task LoadSecurityAsync()
    {
        var r = await SystemTools.SecurityAsync();
        foreach (Control c in _security.Controls.Cast<Control>().ToList()) c.Dispose();

        void Add(string glyph, string title, bool? ok, string okText, string badText, Control? action = null)
        {
            var tone = ok == true ? Tone.Success : ok == false ? Tone.Danger : Tone.Muted;
            var row = new SettingRow(glyph, title, ok == true ? okText : ok == false ? badText : "Não foi possível verificar.", tone) { ShowDivider = _security.Controls.Count > 0 };
            row.AddPill(new Pill(ok == true ? "OK" : ok == false ? "Atenção" : "?", tone));
            if (action != null && ok == false) row.AddRight(action);
            _security.Controls.Add(row);
        }

        var enableFw = new UiButton("Ativar firewall", ButtonKind.Primary, Icons.Shield);
        enableFw.Click += async (_, _) =>
        {
            enableFw.Enabled = false;
            bool ok = await SystemTools.EnableFirewallAsync();
            MainForm.Instance?.ShowToast(ok ? "Firewall ativado em todos os perfis." : "Não foi possível ativar o firewall.", ok ? Tone.Success : Tone.Danger);
            await LoadSecurityAsync();
        };
        Add(Icons.Shield, "Firewall do Windows", r.Firewall, "Ativo em todos os perfis de rede.", "Desligado em pelo menos um perfil de rede.", enableFw);
        Add(Icons.Heart, "Antivírus (Microsoft Defender)", r.Defender == false ? null : r.Defender,
            "Proteção em tempo real em execução.", "", null);
        if (r.Defender == false)
            ((SettingRow)_security.Controls[^1]).DescriptionLabel.Text = "O Defender está parado. Normal se você usa outro antivírus; caso contrário, verifique a Segurança do Windows.";
        Add(Icons.Lock, "Controle de Conta de Usuário (UAC)", r.Uac, "Ativo: programas pedem permissão antes de alterar o sistema.", "Desativado: qualquer programa pode alterar o sistema sem pedir.");
        Add(Icons.Sync, "Atualizações", !r.RebootPending, "Nenhuma reinicialização pendente.", "Há atualizações aguardando reinício do computador.");

        _secPill.Text = $"Nota {r.Score}/100";
        _secPill.Tone = r.Score >= 80 ? Tone.Success : r.Score >= 55 ? Tone.Warning : Tone.Danger;
        Ui.Relayout(_security);
    }

    private Card Tool(string glyph, string title, string desc, Tone tone, string action, Func<CancellationToken, Task<bool>> run,
        bool showLog = true, bool cancellable = false)
    {
        var c = new Card { Padding = new Padding(Theme.S(20)), Gap = Theme.S(10) };
        c.Controls.Add(new Row(new IconBadge(glyph, tone, 40)) { FixedHeight = Theme.S(40) });
        c.Controls.Add(new TextLabel(title, Theme.H3));
        c.Controls.Add(new TextLabel(desc, Theme.Small, Tone.Muted));
        var btn = new UiButton(action, ButtonKind.Secondary, Icons.Play);
        c.Controls.Add(new Row(btn));
        if (showLog) _runButtons.Add(btn);
        btn.Click += async (_, _) =>
        {
            if (!showLog)
            {
                if (!await run(CancellationToken.None)) MainForm.Instance?.ShowToast("Não foi possível abrir.", Tone.Danger);
                return;
            }
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            foreach (var b in _runButtons) b.Enabled = false;
            _logCard.Visible = true;
            _logTitle.Text = title;
            _cancel.Visible = cancellable;
            _log.Clear();
            _log.Add($"Iniciado às {DateTime.Now:HH:mm:ss}", Tone.Muted);
            try
            {
                bool ok = await run(_cts.Token);
                _log.Add(ok ? "✓ Concluído." : "✗ Terminou com erro. Veja as mensagens acima.");
                MainForm.Instance?.ShowToast(ok ? title + ": concluído." : title + ": terminou com erro.", ok ? Tone.Success : Tone.Danger);
            }
            catch (OperationCanceledException) { _log.Add("⚠ Cancelado."); }
            catch (Exception ex)
            {
                Log.Error(title, ex);
                _log.Add("✗ " + ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _cancel.Visible = false;
                foreach (var b in _runButtons) b.Enabled = true;
            }
        };
        return c;
    }
}
