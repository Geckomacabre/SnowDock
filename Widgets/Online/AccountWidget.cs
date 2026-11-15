using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnowDock.Widgets.Online;

/// <summary>A non-secret setting an account widget needs besides its key (a site, a channel, a store…).</summary>
internal sealed record AccountField(string Id, string Label, string Hint = "");

/// <summary>
/// A widget that reads one of your online accounts with a key you paste into its card. Until connected it shows
/// a marked sample. The key is stored encrypted (see <see cref="KeyStore"/>) under <see cref="Service"/>, so
/// every widget for the same service shares it.
/// </summary>
internal abstract class AccountWidget<T> : OnlineWidget<T> where T : class
{
    /// <summary>Key-store id, e.g. "stripe". Widgets for the same service share it.</summary>
    protected abstract string Service { get; }
    protected abstract string ServiceName { get; }
    /// <summary>What the key is called in that service, e.g. "Restricted API key".</summary>
    protected abstract string KeyLabel { get; }
    /// <summary>How to create the key, in short steps.</summary>
    protected abstract string[] Steps { get; }
    protected virtual string? DashboardUrl => null;
    protected virtual AccountField[] Fields => Array.Empty<AccountField>();
    /// <summary>Some services need no key, only fields (e.g. a public profile).</summary>
    protected virtual bool NeedsKey => true;

    protected string Key => KeyStore.Get(Service) ?? "";
    protected string Field(string id) => Get(id, "").Trim();

    protected override bool CanFetch =>
        (!NeedsKey || KeyStore.Has(Service)) && Fields.All(f => Field(f.Id).Length > 0);

    public override bool HasCard => true;

    /// <summary>Extra content for the card when connected (details, a bigger chart). Optional.</summary>
    protected virtual FrameworkElement? BuildDetail(WidgetPaint p) => null;

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 360 };
        string status = IsSample ? $"Not connected: showing a sample. Connect {ServiceName} below."
            : Error != null ? $"Connected, but: {Error}"
            : UpdatedAt is DateTime u ? $"Connected · updated {u:t}" : "Connected · loading…";
        panel.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = Error != null ? p.Bad : p.SubInk });

        if (!IsSample && BuildDetail(p) is { } detail)
        {
            panel.Children.Add(detail);
            panel.Children.Add(new Border { Height = 1, Background = p.Track, Margin = new Thickness(0, 10, 0, 10) });
        }

        var steps = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < Steps.Length; i++) steps.Inlines.Add(new Run($"{i + 1}. {Steps[i]}\n"));
        if (DashboardUrl != null)
        {
            var link = new Hyperlink(new Run($"Open {ServiceName}")) { NavigateUri = new Uri(DashboardUrl) };
            link.RequestNavigate += (_, e) => DockWindow.ShellOpen(e.Uri.AbsoluteUri);
            steps.Inlines.Add(link);
        }
        panel.Children.Add(steps);

        PasswordBox? keyBox = null;
        if (NeedsKey)
        {
            panel.Children.Add(new TextBlock { Text = KeyLabel + (KeyStore.Has(Service) ? "  (saved; paste a new one to replace it)" : ""), FontSize = 12 });
            keyBox = new PasswordBox { Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
            panel.Children.Add(keyBox);
        }
        var fieldBoxes = new Dictionary<string, TextBox>();
        foreach (var f in Fields)
        {
            panel.Children.Add(new TextBlock { Text = f.Label + (f.Hint.Length > 0 ? $"  ({f.Hint})" : ""), FontSize = 12 });
            var box = new TextBox { Text = Field(f.Id), Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
            fieldBoxes[f.Id] = box;
            panel.Children.Add(box);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) =>
        {
            if (keyBox != null && keyBox.Password.Trim().Length > 0) KeyStore.Set(Service, keyBox.Password);
            foreach (var (id, box) in fieldBoxes) Set(id, box.Text.Trim());
            RefreshSoon();
            Host?.Redraw();
            Host?.CloseCard();
        };
        buttons.Children.Add(save);
        if (NeedsKey && KeyStore.Has(Service))
        {
            var forget = new Button { Content = "Disconnect", Padding = new Thickness(10, 3, 10, 3) };
            forget.Click += (_, _) => { KeyStore.Set(Service, null); RefreshSoon(); Host?.Redraw(); Host?.CloseCard(); };
            buttons.Children.Add(forget);
        }
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = $"The key is encrypted for your Windows account and only sent to {ServiceName}.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        return new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
