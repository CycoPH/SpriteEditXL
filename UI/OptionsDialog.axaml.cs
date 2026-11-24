using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

public partial class OptionsDialog : Window
{
    private Options _opt = new();

    public OptionsDialog()
    {
        InitializeComponent();
    }

    public static OptionsDialog Create(Options current)
    {
        var dlg = new OptionsDialog();
        dlg._opt = current.Clone();
        dlg.PopulateTemplateList();
        dlg.LoadFromOptions();
        dlg.WireEvents();
        return dlg;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PopulateTemplateList()
    {
        var cb = this.FindControl<ComboBox>("SpriteTemplateCb")!;
        cb.Items.Clear();
        cb.Items.Add("Select template");
        foreach (var t in SpriteTemplates.All) cb.Items.Add(t.Name);
        cb.SelectedIndex = _opt.SpriteTemplate + 1;
    }

    private void WireEvents()
    {
        this.FindControl<Button>("OkBtn")!.Click += async (_, _) =>
        {
            if (!await TryValidateAndSave()) return;
            Close(_opt);
        };
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close(null);

        var fsCb = this.FindControl<ComboBox>("SpriteTemplateCb")!;
        fsCb.SelectionChanged += (_, _) =>
        {
            int idx = fsCb.SelectedIndex - 1;
            if (idx < 0 || idx >= SpriteTemplates.All.Count) return;
            SpriteTemplates.All[idx].ApplyTo(_opt);
            LoadFromOptions();
        };
    }

    private async System.Threading.Tasks.Task<bool> TryValidateAndSave()
    {
        // Live validation of integer fields. Red-underlines a field and blocks OK if out of range.
        bool ok = true;
        ok &= CheckRange("HeightNud",       1,  240);
        ok &= CheckRange("AnimSpeedNud",    1,  255);
        ok &= CheckRange("CellSizeNud",     0,  Defines.ZoomCellSize.Length - 1);
        ok &= CheckRange("Gap01Nud",        0,  15);
        ok &= CheckRange("Gap23Nud",        0,  15);
        ok &= CheckRange("PairGapNud",      0,  15);
        ok &= CheckRange("BytesPerLineNud", 1,  256);
        ok &= CheckRange("LineStepNud",     1, 1000);
        ok &= CheckRange("GifScaleNud",     1,  32);
        if (!ok)
        {
            await ShowAlert("Some fields are out of range (highlighted in red). Please fix them before continuing.");
            return false;
        }

        bool dliWant    = this.FindControl<CheckBox>("DliOnCb")!.IsChecked == true;
        bool commonWant = this.FindControl<CheckBox>("CommonPaletteCb")!.IsChecked == true;
        if (dliWant && commonWant)
        {
            await ShowAlert(
                "DLI mode and Common Palette are mutually exclusive.\n\n" +
                "Turn one off before saving.");
            return false;
        }

        SaveToOptions();
        return true;
    }

    private bool CheckRange(string nudName, int min, int max)
    {
        var nud = this.FindControl<TinyNumericUpDown>(nudName);
        if (nud == null) return true;
        int v = nud.Value;
        bool inRange = v >= min && v <= max;
        nud.BorderBrush = inRange
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88))
            : Avalonia.Media.Brushes.Red;
        nud.BorderThickness = new Avalonia.Thickness(inRange ? 1 : 2);
        return inRange;
    }

    private async System.Threading.Tasks.Task ShowAlert(string message)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var w = new Window
        {
            Title = "Options", Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, IsCancel = true,
                              HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        ok.Click += (_, _) => { tcs.TrySetResult(true); w.Close(); };
        w.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                 Children = { ok } }
            }
        };
        _ = w.ShowDialog(this).ContinueWith(_ => tcs.TrySetResult(true));
        await tcs.Task;
    }

    private void LoadFromOptions()
    {
        this.FindControl<CheckBox>("SquarePixelCb")!.IsChecked = _opt.SquarePixel;
        this.FindControl<ComboBox>("PaletteCb")!.SelectedIndex = (int)_opt.Palette;
        this.FindControl<CheckBox>("CommonPaletteCb")!.IsChecked = _opt.CommonPalette;
        this.FindControl<TinyNumericUpDown>("CellSizeNud")!.Value = _opt.CellSize;
        this.FindControl<TinyNumericUpDown>("AnimSpeedNud")!.Value = _opt.AnimationSpeed;
        this.FindControl<CheckBox>("ShowGridCb")!.IsChecked = _opt.ShowGrid > 0;
        this.FindControl<CheckBox>("OrOutsideCb")!.IsChecked = _opt.OrDrawsOutside;
        this.FindControl<CheckBox>("NoOverlapCb")!.IsChecked = _opt.NoOverlap;
        this.FindControl<CheckBox>("WrapEditorCb")!.IsChecked = _opt.WrapEditor;
        this.FindControl<CheckBox>("MarkActiveCb")!.IsChecked = _opt.MarkActiveRegion;
        this.FindControl<CheckBox>("DliOnCb")!.IsChecked = _opt.DliOn;
        this.FindControl<CheckBox>("MultiFrameCb")!.IsChecked = _opt.MultiFrameEdit;
        this.FindControl<CheckBox>("FrameDelayCb")!.IsChecked = _opt.FrameDelayMode;
        this.FindControl<CheckBox>("DrawOnPlayCb")!.IsChecked = _opt.DrawOnPlay;

        this.FindControl<TinyNumericUpDown>("BackOffsetNud")!.Value = _opt.BackOffset;
        this.FindControl<TinyNumericUpDown>("BackImageWidthNud")!.Value = _opt.BackImageWidth;
        this.FindControl<TinyNumericUpDown>("BackOffsetHNud")!.Value = _opt.BackOffsetH;
        this.FindControl<TinyNumericUpDown>("BackOffsetVNud")!.Value = _opt.BackOffsetV;

        this.FindControl<ComboBox>("LineResCb")!.SelectedIndex = _opt.LineResolution == 2 ? 1 : 0;
        this.FindControl<TinyNumericUpDown>("HeightNud")!.Value = _opt.SpriteHeight;
        this.FindControl<ComboBox>("MergeModeCb")!.SelectedIndex = _opt.MergeMode switch
        {
            SpriteMode.P0P1 => 0, SpriteMode.PM0PM1 => 1, SpriteMode.MP0MP1 => 2,
            SpriteMode.P0P1P2P3 => 3, SpriteMode.PM0PM1PM2PM3 => 4, SpriteMode.MP0MP1MP2MP3 => 5, _ => 0
        };
        this.FindControl<TinyNumericUpDown>("Gap01Nud")!.Value = _opt.SpriteGap01;
        this.FindControl<TinyNumericUpDown>("Gap23Nud")!.Value = _opt.SpriteGap23;
        this.FindControl<TinyNumericUpDown>("PairGapNud")!.Value = _opt.PairGap;

        this.FindControl<TinyNumericUpDown>("P0Nud")!.Value = _opt.P0Offset;
        this.FindControl<TinyNumericUpDown>("P1Nud")!.Value = _opt.P1Offset;
        this.FindControl<TinyNumericUpDown>("P2Nud")!.Value = _opt.P2Offset;
        this.FindControl<TinyNumericUpDown>("P3Nud")!.Value = _opt.P3Offset;
        this.FindControl<TinyNumericUpDown>("M0Nud")!.Value = _opt.M0Offset;
        this.FindControl<TinyNumericUpDown>("M1Nud")!.Value = _opt.M1Offset;
        this.FindControl<TinyNumericUpDown>("M2Nud")!.Value = _opt.M2Offset;
        this.FindControl<TinyNumericUpDown>("M3Nud")!.Value = _opt.M3Offset;

        this.FindControl<ComboBox>("BytesExportCb")!.SelectedIndex = (int)_opt.BytesExport;
        this.FindControl<TinyNumericUpDown>("BytesPerLineNud")!.Value = _opt.BytesPerLine;
        this.FindControl<TextBox>("LabelPrefixTb")!.Text = _opt.LabelPrefix;
        this.FindControl<TinyNumericUpDown>("StartingLineNud")!.Value = _opt.StartingLine;
        this.FindControl<TinyNumericUpDown>("LineStepNud")!.Value = _opt.LineStep;
        this.FindControl<TinyNumericUpDown>("GifScaleNud")!.Value = _opt.GifExportScale;

        this.FindControl<StackPanel>("StandardFields")!.IsVisible = !_opt.Freestyle;
        this.FindControl<StackPanel>("FreestyleFields")!.IsVisible = _opt.Freestyle;
    }

    private void SaveToOptions()
    {
        _opt.SquarePixel = this.FindControl<CheckBox>("SquarePixelCb")!.IsChecked == true;
        _opt.Palette = (PaletteKind)this.FindControl<ComboBox>("PaletteCb")!.SelectedIndex;
        _opt.CommonPalette = this.FindControl<CheckBox>("CommonPaletteCb")!.IsChecked == true;
        _opt.CellSize = this.FindControl<TinyNumericUpDown>("CellSizeNud")!.Value;
        _opt.AnimationSpeed = this.FindControl<TinyNumericUpDown>("AnimSpeedNud")!.Value;
        _opt.ShowGrid = this.FindControl<CheckBox>("ShowGridCb")!.IsChecked == true ? 1 : 0;
        _opt.OrDrawsOutside = this.FindControl<CheckBox>("OrOutsideCb")!.IsChecked == true;
        _opt.NoOverlap = this.FindControl<CheckBox>("NoOverlapCb")!.IsChecked == true;
        _opt.WrapEditor = this.FindControl<CheckBox>("WrapEditorCb")!.IsChecked == true;
        _opt.MarkActiveRegion = this.FindControl<CheckBox>("MarkActiveCb")!.IsChecked == true;
        _opt.DliOn = this.FindControl<CheckBox>("DliOnCb")!.IsChecked == true;
        _opt.MultiFrameEdit = this.FindControl<CheckBox>("MultiFrameCb")!.IsChecked == true;
        _opt.FrameDelayMode = this.FindControl<CheckBox>("FrameDelayCb")!.IsChecked == true;
        _opt.DrawOnPlay = this.FindControl<CheckBox>("DrawOnPlayCb")!.IsChecked == true;

        _opt.BackOffset = this.FindControl<TinyNumericUpDown>("BackOffsetNud")!.Value;
        _opt.BackImageWidth = this.FindControl<TinyNumericUpDown>("BackImageWidthNud")!.Value;
        _opt.BackOffsetH = this.FindControl<TinyNumericUpDown>("BackOffsetHNud")!.Value;
        _opt.BackOffsetV = this.FindControl<TinyNumericUpDown>("BackOffsetVNud")!.Value;

        _opt.LineResolution = this.FindControl<ComboBox>("LineResCb")!.SelectedIndex == 1 ? 2 : 1;
        _opt.SpriteHeight = this.FindControl<TinyNumericUpDown>("HeightNud")!.Value;
        _opt.MergeMode = this.FindControl<ComboBox>("MergeModeCb")!.SelectedIndex switch
        {
            0 => SpriteMode.P0P1, 1 => SpriteMode.PM0PM1, 2 => SpriteMode.MP0MP1,
            3 => SpriteMode.P0P1P2P3, 4 => SpriteMode.PM0PM1PM2PM3, 5 => SpriteMode.MP0MP1MP2MP3, _ => SpriteMode.P0P1
        };
        _opt.SpriteGap01 = this.FindControl<TinyNumericUpDown>("Gap01Nud")!.Value;
        _opt.SpriteGap23 = this.FindControl<TinyNumericUpDown>("Gap23Nud")!.Value;
        _opt.PairGap = this.FindControl<TinyNumericUpDown>("PairGapNud")!.Value;

        _opt.P0Offset = this.FindControl<TinyNumericUpDown>("P0Nud")!.Value;
        _opt.P1Offset = this.FindControl<TinyNumericUpDown>("P1Nud")!.Value;
        _opt.P2Offset = this.FindControl<TinyNumericUpDown>("P2Nud")!.Value;
        _opt.P3Offset = this.FindControl<TinyNumericUpDown>("P3Nud")!.Value;
        _opt.M0Offset = this.FindControl<TinyNumericUpDown>("M0Nud")!.Value;
        _opt.M1Offset = this.FindControl<TinyNumericUpDown>("M1Nud")!.Value;
        _opt.M2Offset = this.FindControl<TinyNumericUpDown>("M2Nud")!.Value;
        _opt.M3Offset = this.FindControl<TinyNumericUpDown>("M3Nud")!.Value;

        _opt.BytesExport = (BytesExportFormat)this.FindControl<ComboBox>("BytesExportCb")!.SelectedIndex;
        _opt.BytesPerLine = this.FindControl<TinyNumericUpDown>("BytesPerLineNud")!.Value;
        _opt.LabelPrefix = this.FindControl<TextBox>("LabelPrefixTb")!.Text ?? "";
        _opt.StartingLine = this.FindControl<TinyNumericUpDown>("StartingLineNud")!.Value;
        _opt.LineStep = this.FindControl<TinyNumericUpDown>("LineStepNud")!.Value;
        _opt.GifExportScale = this.FindControl<TinyNumericUpDown>("GifScaleNud")!.Value;

        // DLI & common palette are mutually exclusive
        if (_opt.DliOn && _opt.CommonPalette) _opt.DliOn = false;
    }
}
