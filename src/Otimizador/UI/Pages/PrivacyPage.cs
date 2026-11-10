namespace Otimizador.UI.Pages;

internal sealed class PrivacyPage : Page
{
    private readonly List<(PrivacyItem item, UiCheck check)> _items = new();
    private readonly UiButton _clean = new("Limpar rastros", ButtonKind.Primary, Icons.Clean);
    private readonly TweakList _tweaks = new(Tweaks.InGroup("privacidade"));
    private bool _loaded;

    public PrivacyPage() : base("Privacidade", "Apague rastros de uso e reduza o que o Windows coleta e exibe.")
    {
        Header.AddRight(_clean);
        _clean.Click += async (_, _) => await CleanAsync();

        Content.Controls.Add(new TextLabel("Rastros de uso", Theme.H3));
        var card = new Card { Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6)), Gap = 0 };
        bool first = true;
        foreach (var it in PrivacyCleaner.Items)
        {
            var check = new UiCheck { Checked = it.Id != "clipboard" };
            card.Controls.Add(new SettingRow(Icons.History, it.Title, it.Description, Tone.Accent, check) { ShowDivider = !first });
            _items.Add((it, check));
            first = false;
        }
        Content.Controls.Add(card);

        Content.Controls.Add(new TextLabel("Configurações de privacidade do Windows", Theme.H3));
        Content.Controls.Add(_tweaks);
    }

    public override async void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        await _tweaks.RefreshAsync();
    }

    private async Task CleanAsync()
    {
        _clean.Enabled = false;
        int total = 0;
        try
        {
            foreach (var (item, check) in _items.Where(x => x.check.Checked))
                total += item.UiThread ? item.Clean() : await Task.Run(item.Clean);
            Log.Info($"Privacidade: {total} rastros removidos.");
            MainForm.Instance?.ShowToast(total > 0 ? $"{Fmt.Num(total)} rastros removidos." : "Nenhum rastro encontrado.");
        }
        finally { _clean.Enabled = true; }
    }
}
