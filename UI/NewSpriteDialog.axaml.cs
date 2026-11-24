using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// Modal shown by <see cref="MainWindow.NewSprite"/>. Lists every SpriteTemplate and
/// returns the chosen one, or null if cancelled.
/// </summary>
public partial class NewSpriteDialog : Window
{
    public NewSpriteDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static NewSpriteDialog Create()
    {
        var dlg = new NewSpriteDialog();
        dlg.Populate();
        return dlg;
    }

    private void Populate()
    {
        var list = this.FindControl<ListBox>("TemplateList")!;
        foreach (var t in SpriteTemplates.All) list.Items.Add(t.Name);
        list.SelectedIndex = 0;

        var descTb = this.FindControl<TextBlock>("DescTb")!;
        list.SelectionChanged += (_, _) => descTb.Text = DescribeSelected(list.SelectedIndex);
        descTb.Text = DescribeSelected(list.SelectedIndex);

        this.FindControl<Button>("OkBtn")!.Click += (_, _) =>
        {
            int idx = list.SelectedIndex;
            Close(idx >= 0 && idx < SpriteTemplates.All.Count ? (SpriteTemplate?)SpriteTemplates.All[idx] : null);
        };
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close((SpriteTemplate?)null);
    }

    private static string DescribeSelected(int idx)
    {
        if (idx < 0 || idx >= SpriteTemplates.All.Count) return "";
        var t = SpriteTemplates.All[idx];
        string desc =
            $"Height:      {t.SpriteHeight} rows\n" +
            $"Line res:    {t.LineResolution}  ({(t.LineResolution == 1 ? "1 line per pixel" : "2 lines per pixel")})\n" +
            $"Mode:        {(t.Freestyle ? "Freestyle" : t.MergeMode.ToString())}\n" +
            $"Gap 0-1:     {t.SpriteGap01}\n" +
            $"Gap 2-3:     {t.SpriteGap23}\n" +
            $"Pair gap:    {t.PairGap}";
        if (t.Freestyle)
            desc += $"\nOffsets:     P0={t.P0Offset} P1={t.P1Offset} P2={t.P2Offset} P3={t.P3Offset}";
        return desc;
    }
}
