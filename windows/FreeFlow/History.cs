using System.Text.Json;

namespace FreeFlow;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public string Text { get; set; } = "";
    public string Raw { get; set; } = "";
    public string App { get; set; } = "";
    public string Mode { get; set; } = "dictation";
    public double Seconds { get; set; }
    public double SttSeconds { get; set; }
    public double AiSeconds { get; set; }
    public int Words => Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
}

sealed class HistoryStore
{
    public static readonly HistoryStore Shared = new();
    public List<HistoryEntry> Entries { get; private set; } = new();
    public event Action Changed;
    static string FilePath => Path.Combine(Paths.AppData, "history.json");

    HistoryStore()
    {
        try { if (File.Exists(FilePath)) Entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
    }

    public void Add(HistoryEntry e)
    {
        Entries.Insert(0, e);
        if (Entries.Count > 2000) Entries.RemoveRange(2000, Entries.Count - 2000);
        Save();
    }

    public void Delete(HistoryEntry e) { Entries.Remove(e); Save(); }
    public void Clear() { Entries.Clear(); Save(); }

    void Save()
    {
        var snapshot = Entries.ToList();
        Task.Run(() => { try { File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot)); } catch { } });
        Changed?.Invoke();
    }

    public int TotalWords => Entries.Sum(e => e.Words);
    public double TotalSeconds => Entries.Sum(e => e.Seconds);
}

sealed class HistoryForm : Form
{
    readonly Action<string> paste;
    readonly TextBox search = new() { PlaceholderText = "Search history", Dock = DockStyle.Fill };
    readonly ListView list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    readonly Label stats = new() { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Padding = new Padding(4, 8, 4, 8) };
    readonly TextBox detail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };

    public HistoryForm(Action<string> paste)
    {
        this.paste = paste;
        Text = "FreeFlow History";
        Size = new Size(820, 620);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Program.AppIcon;

        list.Columns.Add("When", 130);
        list.Columns.Add("App", 120);
        list.Columns.Add("Text", 420);
        list.Columns.Add("Speed", 120);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(Btn("Copy", () => { if (Sel() is { } e) Inserter.SetClipboard(e.Text, false); }));
        buttons.Controls.Add(Btn("Paste into previous app", () => { if (Sel() is { } e) { Hide(); paste(e.Text); } }));
        buttons.Controls.Add(Btn("Show original", () => { if (Sel() is { } e) detail.Text = "ORIGINAL TRANSCRIPT:\r\n" + e.Raw.Replace("\n", "\r\n"); }));
        buttons.Controls.Add(Btn("Delete", () => { if (Sel() is { } e) HistoryStore.Shared.Delete(e); }));
        buttons.Controls.Add(Btn("Clear all…", () =>
        {
            if (MessageBox.Show(this, "Delete all history?", "FreeFlow", MessageBoxButtons.YesNo) == DialogResult.Yes) HistoryStore.Shared.Clear();
        }));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(stats, 0, 0);
        layout.Controls.Add(search, 0, 1);
        layout.Controls.Add(list, 0, 2);
        layout.Controls.Add(detail, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        search.TextChanged += (_, _) => Fill();
        list.SelectedIndexChanged += (_, _) => { if (Sel() is { } e) detail.Text = e.Text.Replace("\n", "\r\n"); };
        list.DoubleClick += (_, _) => { if (Sel() is { } e) Inserter.SetClipboard(e.Text, false); };
        HistoryStore.Shared.Changed += OnChanged;
        FormClosed += (_, _) => HistoryStore.Shared.Changed -= OnChanged;
        Fill();
    }

    void OnChanged() { if (IsHandleCreated) BeginInvoke(Fill); }

    static Button Btn(string t, Action a)
    {
        var b = new Button { Text = t, AutoSize = true };
        b.Click += (_, _) => a();
        return b;
    }

    HistoryEntry Sel() => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as HistoryEntry : null;

    void Fill()
    {
        var st = HistoryStore.Shared;
        int wpm = st.TotalSeconds > 5 ? (int)(st.TotalWords / (st.TotalSeconds / 60)) : 0;
        double saved = Math.Max(0, st.TotalWords / 40.0 - st.TotalSeconds / 60);
        stats.Text = $"{st.TotalWords:N0} words dictated   ·   {st.Entries.Count} dictations   ·   {wpm} wpm   ·   " +
                     (saved >= 60 ? $"{saved / 60:0.0} h saved vs typing" : $"{(int)saved} min saved vs typing");

        var q = search.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var e in st.Entries)
        {
            if (q.Length > 0 && !e.Text.Contains(q, StringComparison.OrdinalIgnoreCase) && !e.App.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var timing = e.AiSeconds > 0 ? $"voice {e.SttSeconds:0.0}s · AI {e.AiSeconds:0.0}s" : $"voice {e.SttSeconds:0.0}s";
            var item = new ListViewItem(new[] { e.Date.ToString("g"), (e.Mode == "command" ? "✨ " : "") + e.App, e.Text.Replace("\n", " ⏎ "), timing }) { Tag = e };
            list.Items.Add(item);
        }
        list.EndUpdate();
    }
}
