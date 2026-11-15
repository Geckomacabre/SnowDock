using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SnowDock.Widgets.Online;

namespace SnowDock.Widgets.Accounts;

/// <summary>
/// A tile that reads a Google account through <see cref="GoogleAuth"/>. Its card takes the person's own OAuth client
/// (ID and secret) and runs the browser sign-in; every Google tile shares the one connection.
/// </summary>
internal abstract class GoogleTile<T> : ServiceTile<T> where T : class
{
    /// <summary>The read-only scope this tile needs.</summary>
    protected abstract string Scope { get; }
    /// <summary>"Gmail API", "Google Search Console API".</summary>
    protected abstract string ApiName { get; }

    protected override string Service => "google-oauth-refresh";
    protected override string ServiceName => "Google";
    protected override string KeyLabel => "OAuth client";
    protected override string? DashboardUrl => "https://console.cloud.google.com/auth/clients";
    protected override string[] Steps => new[]
    {
        $"This needs your own Google OAuth client. In Google Cloud Console, create (or pick) a project and enable the {ApiName}.",
        "Under Google Auth Platform, set up the consent screen as External and add your own Google account as a test user.",
        "Under Clients, create a client of type Desktop app, then paste its client ID and client secret below.",
        "Click Connect with Google and allow read-only access to Gmail and Search Console. All Google widgets share this connection.",
        "While the app's publishing status is Testing, Google ends the sign-in after 7 days and you'll need to connect again. Publishing it avoids that (Google will warn that your own app is unverified; that's expected)."
    };

    protected override bool CanFetch => GoogleAuth.Connected && GoogleAuth.Granted(Scope) && Fields.All(f => Field(f.Id).Length > 0);

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 360 };
        string status = GoogleAuth.Connected && !GoogleAuth.Granted(Scope)
            ? $"Connected to Google, but without access to the {ApiName}. Connect again and allow it."
            : IsSample ? "Not connected: showing a sample. Connect Google below."
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
        var link = new Hyperlink(new Run("Open Google Cloud Console")) { NavigateUri = new Uri(DashboardUrl!) };
        link.RequestNavigate += (_, e) => DockWindow.ShellOpen(e.Uri.AbsoluteUri);
        steps.Inlines.Add(link);
        panel.Children.Add(steps);

        TextBox Box(string label, string text)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
            var box = new TextBox { Text = text, Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
            panel.Children.Add(box);
            return box;
        }
        var idBox = Box("OAuth client ID", GoogleAuth.ClientId);
        panel.Children.Add(new TextBlock { Text = "Client secret" + (GoogleAuth.HasSecret ? "  (saved; paste a new one to replace it)" : ""), FontSize = 12 });
        var secretBox = new PasswordBox { Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(secretBox);
        var fieldBoxes = Fields.ToDictionary(f => f.Id, f => Box(f.Label + (f.Hint.Length > 0 ? $"  ({f.Hint})" : ""), Field(f.Id)));

        var progress = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        panel.Children.Add(progress);
        void Say(string text, bool bad)
        {
            progress.Text = text;
            progress.Foreground = bad ? p.Bad : p.SubInk;
            progress.Visibility = Visibility.Visible;
        }
        void SaveSettings()
        {
            GoogleAuth.SaveClient(idBox.Text, secretBox.Password);
            secretBox.Clear();
            foreach (var (id, box) in fieldBoxes) Set(id, box.Text.Trim());
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var connect = new Button
        {
            Content = GoogleAuth.Connected ? "Connect again" : "Connect with Google",
            Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0)
        };
        connect.Click += async (_, _) =>
        {
            SaveSettings();
            connect.IsEnabled = false;
            Say("Finish signing in in your browser. SnowDock is waiting for Google's reply…", false);
            string? error = await GoogleAuth.ConnectAsync();
            connect.IsEnabled = true;
            RefreshSoon();
            Host?.Redraw();
            if (error == null) Say("Connected to Google.", false);
            else Say(error, true);
        };
        buttons.Children.Add(connect);
        var save = new Button { Content = "Save", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) => { SaveSettings(); RefreshSoon(); Host?.Redraw(); Host?.CloseCard(); };
        buttons.Children.Add(save);
        if (GoogleAuth.Connected)
        {
            var forget = new Button { Content = "Disconnect", Padding = new Thickness(10, 3, 10, 3) };
            forget.Click += (_, _) => { GoogleAuth.Forget(); RefreshSoon(); Host?.Redraw(); Host?.CloseCard(); };
            buttons.Children.Add(forget);
        }
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = "SnowDock asks only for read-only access. The client secret and Google's sign-in token are encrypted for your Windows account and only sent to Google.",
            FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        return new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
