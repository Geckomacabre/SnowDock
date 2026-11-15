using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnowDock.Widgets;

internal static class ProductivityWidgets
{
    public static void Register(Action<string, string, string, string, Func<DockWidget>> add)
    {
        const string c = WidgetCatalog.Productivity;
        add("timer", "Timer", c, "A countdown that starts with one click.", () => new TimerWidget());
        add("stopwatch", "Stopwatch", c, "Start, pause and take laps from the dock.", () => new StopwatchWidget());
        add("pomodoro", "Pomodoro", c, "Focus rounds and breaks, started and skipped from the dock.", () => new PomodoroWidget());
        add("todos", "To-Dos", c, "Your next open task and how many are done; tick them off in the card.", () => new TodoWidget());
        add("notes", "Notes", c, "Your latest note, one click from editing.", () => new NotesWidget());
    }
}

/// <summary>A tiny JSON list stored next to config.json.</summary>
internal static class LocalList<T> where T : new()
{
    public static List<T> Load(string file)
    {
        try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(WidgetEnv.DataFile(file))) ?? new(); }
        catch { return new(); }
    }

    public static void Save(string file, List<T> items)
    {
        try { File.WriteAllText(WidgetEnv.DataFile(file), JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { AppPaths.Log($"Saving {file} failed: {ex.Message}"); }
    }
}

internal sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

internal sealed class TodoWidget : DockWidget
{
    private const string File = "todos.json";
    private List<TodoItem> _items = LocalList<TodoItem>.Load(File);

    public override string Title => _items.FirstOrDefault(i => !i.Done)?.Text is string next ? $"Next: {next}" : "To-Dos";
    public override double WidthUnits => 2.6;
    public override bool HasCard => true;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        Tile(dc, r, p);
        int done = _items.Count(i => i.Done), total = _items.Count;
        double rad = r.Height * 0.26;
        var c = new Point(r.X + r.Height * 0.48, r.Y + r.Height / 2);
        Ring(dc, c, rad, total == 0 ? 0 : (double)done / total, p.Good, p, Math.Max(3, r.Height * 0.07));
        Centered(dc, $"{done}/{total}", c, r.Height * 0.14, p.Ink, p, rad * 1.6);
        var next = _items.FirstOrDefault(i => !i.Done)?.Text ?? (total == 0 ? "Add a task" : "All done");
        var t = Text(next, Math.Min(13, r.Height * 0.2), p.Ink, p, FontWeights.Normal);
        t.MaxTextWidth = r.Right - (c.X + rad + 10) - 8;
        t.MaxLineCount = 2;
        t.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(t, new Point(c.X + rad + 10, r.Y + (r.Height - t.Height) / 2));
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var panel = new StackPanel { Width = 300 };
        var list = new StackPanel();
        var input = LauncherWidgets.Input(p);
        input.Margin = new Thickness(0, 8, 0, 0);
        void Refresh()
        {
            list.Children.Clear();
            foreach (var item in _items.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = p.SubInk };
                DockPanel.SetDock(remove, Dock.Right);
                var box = new CheckBox { Content = item.Text, IsChecked = item.Done, Foreground = p.Ink, VerticalContentAlignment = VerticalAlignment.Center };
                box.Click += (_, _) => { item.Done = box.IsChecked == true; Changed(); };
                remove.Click += (_, _) => { _items.Remove(item); Changed(); Refresh(); };
                row.Children.Add(remove);
                row.Children.Add(box);
                list.Children.Add(row);
            }
        }
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            _items.Add(new TodoItem { Text = input.Text.Trim() });
            input.Clear();
            Changed();
            Refresh();
        };
        Refresh();
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = "Type a task and press Enter.", Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) });
        panel.Loaded += (_, _) => input.Focus();
        return panel;
    }

    private void Changed()
    {
        LocalList<TodoItem>.Save(File, _items);
        Host?.Redraw();
    }
}

internal sealed class NoteItem
{
    public string Text { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
}

internal sealed class NotesWidget : DockWidget
{
    private const string File = "notes.json";
    private readonly List<NoteItem> _notes = LocalList<NoteItem>.Load(File);

    private NoteItem? Latest => _notes.OrderByDescending(n => n.Updated).FirstOrDefault();
    public override string Title => "Notes";
    public override double WidthUnits => 2.4;
    public override bool HasCard => true;

    public override void Draw(DrawingContext dc, Rect r, WidgetPaint p)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xE8, 0x7C)), p.TileRim, r, 10, 10);
        double pad = r.Height * 0.14;
        var text = Latest?.Text.Trim() is { Length: > 0 } t ? t : "New note…";
        var ft = Text(text, Math.Min(12.5, r.Height * 0.19), new SolidColorBrush(Color.FromRgb(0x3A, 0x30, 0x10)), p, FontWeights.Normal);
        ft.MaxTextWidth = r.Width - pad * 2;
        ft.MaxTextHeight = r.Height - pad * 2;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(ft, new Point(r.X + pad, r.Y + pad));
    }

    public override FrameworkElement BuildCard(WidgetPaint p)
    {
        var note = Latest ?? new NoteItem();
        if (!_notes.Contains(note)) _notes.Add(note);
        var panel = new StackPanel { Width = 340 };
        var editor = new TextBox
        {
            Text = note.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        editor.TextChanged += (_, _) =>
        {
            note.Text = editor.Text;
            note.Updated = DateTime.Now;
            LocalList<NoteItem>.Save(File, _notes);
            Host?.Redraw();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = new Button { Content = "New Note", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        var delete = new Button { Content = "Delete", Padding = new Thickness(10, 2, 10, 2) };
        add.Click += (_, _) => { _notes.Add(new NoteItem()); LocalList<NoteItem>.Save(File, _notes); Host?.ShowCard(this); };
        delete.Click += (_, _) => { _notes.Remove(note); LocalList<NoteItem>.Save(File, _notes); Host?.Redraw(); Host?.CloseCard(); };
        buttons.Children.Add(add);
        buttons.Children.Add(delete);
        panel.Children.Add(editor);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = $"{_notes.Count} note(s) · saved as you type", Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) });
        panel.Loaded += (_, _) => { editor.Focus(); editor.CaretIndex = editor.Text.Length; };
        return panel;
    }
}
