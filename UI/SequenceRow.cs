using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// One sequence row = [name label | play button | delete button] + [horizontal frame strip].
/// Primary name is fixed; custom rows can rename/delete.
/// </summary>
public sealed class SequenceRow : UserControl
{
    private Workspace _ws = new();
    private Options _opt = new();
    private EditorSession _session = new();
    public int SequenceIndex { get; private set; }

    private readonly TextBlock _nameLbl;
    private readonly TextBox _nameEdit;
    private readonly Button _playBtn;
    private readonly Button _stopBtn;
    private readonly Button _deleteBtn;
    private readonly FrameStrip _strip;
    private readonly Grid _header;

    public event EventHandler? SelectionChanged;
    public event EventHandler<FrameDragBeginEventArgs>? DragStarted;
    public event EventHandler<DelayEditEventArgs>? DelayEditRequested;
    public event EventHandler? PlayRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? Renamed;

    public FrameStrip Strip => _strip;

    public SequenceRow()
    {
        _nameLbl = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 10, 0)
        };
        _nameLbl.PointerPressed += (_, _) => BeginRename();

        _nameEdit = new TextBox
        {
            IsVisible = false,
            MinWidth = 100,
            Margin = new Thickness(4, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _nameEdit.KeyDown += OnNameKey;
        _nameEdit.LostFocus += (_, _) => CommitRename();

        _playBtn = new Button
        {
            Content = "▶",
            FontSize = 12,
            Padding = new Thickness(4, 0),
            MinWidth = 22,
            VerticalAlignment = VerticalAlignment.Center
        };
        _playBtn.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);

        _stopBtn = new Button
        {
            Content = "■",
            FontSize = 12,
            Padding = new Thickness(4, 0),
            MinWidth = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        _stopBtn.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        _deleteBtn = new Button
        {
            Content = "×",
            FontSize = 12,
            Padding = new Thickness(4, 0),
            MinWidth = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        _deleteBtn.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        _strip = new FrameStrip
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _strip.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _strip.DragStarted += (s, e) => DragStarted?.Invoke(s, e);
        _strip.DelayEditRequested += (s, e) => DelayEditRequested?.Invoke(this, e);

        // Header: name on top, buttons on the row below. Half the previous horizontal footprint.
        var namePanel = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        namePanel.Children.Add(_nameLbl);
        namePanel.Children.Add(_nameEdit);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0
        };
        btnRow.Children.Add(_playBtn);
        btnRow.Children.Add(_stopBtn);
        btnRow.Children.Add(_deleteBtn);

        _header = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Width = 110,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 8, 0)
        };
        Grid.SetRow(namePanel, 0);
        Grid.SetRow(btnRow, 1);
        _header.Children.Add(namePanel);
        _header.Children.Add(btnRow);

        var outer = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_header, Dock.Left);
        outer.Children.Add(_header);

        var stripScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = _strip
        };
        outer.Children.Add(stripScroll);
        Content = outer;
    }

    public void SetState(Workspace ws, Options opt, EditorSession session, int sequenceIndex)
    {
        _ws = ws; _opt = opt; _session = session; SequenceIndex = sequenceIndex;
        var seq = _ws.Sequences[sequenceIndex];
        _nameLbl.Text = seq.Name;
        _deleteBtn.IsVisible = !seq.IsPrimary;
        _nameLbl.IsHitTestVisible = !seq.IsPrimary;
        _strip.SetState(_ws, _opt, _session, sequenceIndex);
        // Highlight row background if this is the active sequence
        Background = (sequenceIndex == _ws.SelectedSequence)
            ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 0))
            : Brushes.Transparent;
    }

    public void Rebuild() => _strip.Rebuild();

    private void BeginRename()
    {
        var seq = _ws.Sequences[SequenceIndex];
        if (seq.IsPrimary) return;
        _nameEdit.Text = seq.Name;
        _nameLbl.IsVisible = false;
        _nameEdit.IsVisible = true;
        _nameEdit.Focus();
        _nameEdit.SelectAll();
    }

    private void OnNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CommitRename()
    {
        if (!_nameEdit.IsVisible) return;
        var seq = _ws.Sequences[SequenceIndex];
        if (!seq.IsPrimary)
        {
            var name = (_nameEdit.Text ?? "").Trim();
            if (name.Length > 0 && name != AnimationSequence.PrimaryName)
                seq.Name = name;
        }
        _nameLbl.Text = seq.Name;
        _nameLbl.IsVisible = true;
        _nameEdit.IsVisible = false;
        Renamed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelRename()
    {
        _nameLbl.IsVisible = true;
        _nameEdit.IsVisible = false;
    }
}
