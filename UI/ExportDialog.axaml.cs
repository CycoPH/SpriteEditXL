using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

public partial class ExportDialog : Window
{
    private Workspace _ws = new();
    private Options _opt = new();

    public ExportDialog() { InitializeComponent(); }

    public static ExportDialog Create(Workspace ws, Options opt)
    {
        var dlg = new ExportDialog { _ws = ws, _opt = opt };
        dlg.Setup();
        return dlg;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private bool _suppress; // avoid re-render while initializing fields

    private void Setup()
    {
        _suppress = true;
        var cb = this.FindControl<ComboBox>("TemplateCb")!;
        cb.Items.Clear();
        foreach (var t in ExportTemplates.All) cb.Items.Add(t.Name);
        cb.SelectedIndex = Math.Clamp(_opt.LastTemplate, 0, ExportTemplates.All.Count - 1);
        cb.SelectionChanged += (_, _) => Render();

        var fmt = this.FindControl<ComboBox>("ByteFmtCb")!;
        fmt.SelectedIndex = (int)_opt.BytesExport;
        fmt.SelectionChanged += (_, _) =>
        {
            _opt.BytesExport = (BytesExportFormat)Math.Max(0, fmt.SelectedIndex);
            Render();
        };

        BindText("LabelPrefixTb",   () => _opt.LabelPrefix,   v => _opt.LabelPrefix   = v);
        BindInt ("StartLineTb",     () => _opt.StartingLine,  v => _opt.StartingLine  = v);
        BindInt ("LineStepTb",      () => _opt.LineStep,      v => _opt.LineStep      = Math.Max(1, v));
        BindInt ("BytesPerLineTb",  () => _opt.BytesPerLine,  v => _opt.BytesPerLine  = Math.Clamp(v, 1, 256));

        this.FindControl<Button>("CopyBtn")!.Click += async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip != null) await clip.SetTextAsync(this.FindControl<TextBox>("OutputTb")!.Text ?? "");
        };
        this.FindControl<Button>("SaveBtn")!.Click += async (_, _) => await Save();
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();
        _suppress = false;
        Render();
    }

    private void BindText(string name, Func<string> get, Action<string> set)
    {
        var tb = this.FindControl<TextBox>(name)!;
        tb.Text = get();
        tb.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            set(tb.Text ?? "");
            Render();
        };
    }

    private void BindInt(string name, Func<int> get, Action<int> set)
    {
        var tb = this.FindControl<TextBox>(name)!;
        tb.Text = get().ToString();
        tb.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            if (int.TryParse(tb.Text, out int v)) { set(v); Render(); }
        };
    }

    private void Render()
    {
        var cb = this.FindControl<ComboBox>("TemplateCb")!;
        int idx = Math.Clamp(cb.SelectedIndex, 0, ExportTemplates.All.Count - 1);
        _opt.LastTemplate = idx;
        var t = ExportTemplates.All[idx];
        this.FindControl<TextBox>("OutputTb")!.Text = Exporter.Render(t, _ws, _opt);
    }

    private async Task Save()
    {
        var sp = StorageProvider;
        if (sp is null) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "sprites.asm",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text files") { Patterns = new[] { "*.asm", "*.s", "*.inc", "*.pas", "*.c", "*.bas", "*.csv", "*.txt" } }
            }
        });
        if (file is null) return;
        await using var s = await file.OpenWriteAsync();
        using var sw = new StreamWriter(s);
        await sw.WriteAsync(this.FindControl<TextBox>("OutputTb")!.Text ?? "");
    }
}
