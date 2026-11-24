using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpriteEditXL.IO;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

public partial class MainWindow : Window
{
    private Workspace _ws;
    private Options _opt;
    private readonly EditorSession _session = new();
    private readonly UndoStack _undo;
    private readonly DispatcherTimer _playTimer = new();
    private bool _playing;
    private Workspace? _beforeStroke;

    public MainWindow()
    {
        InitializeComponent();
        _opt = Storage.LoadOptions();
        _ws = Storage.LoadWorkspace();
        EnsureInitialFrame();
        TryReloadReferenceFromPath();
        _undo = new UndoStack(_opt.UndoLevels);
        _playTimer.Tick += OnPlayTick;

        Opened += (_, _) => Bootstrap();

        // Accept drag-dropped .sprj / .spr / .apl files.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static readonly string[] DroppableExtensions = { ".sprj", ".spr", ".apl" };

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (TryGetDroppablePath(e) != null)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = TryGetDroppablePath(e);
        if (path == null) return;
        e.Handled = true;
        await LoadPath(path);
    }

    private static string? TryGetDroppablePath(DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files == null) return null;
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (p == null) continue;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (Array.IndexOf(DroppableExtensions, ext) >= 0) return p;
        }
        return null;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void EnsureInitialFrame()
    {
        if (_ws.Frames.Count == 0)
            _ws.Frames.Add(Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor));
        foreach (var f in _ws.Frames) f.SetHeight(_opt.SpriteHeight);
        if (_ws.SelectedFrame >= _ws.Frames.Count) _ws.SelectedFrame = 0;
        _ws.SyncPrimary();
    }

    private void Bootstrap()
    {
        this.FindControl<ColorStrip>("ColorStripView")!.SetState(_ws, _opt, _session);
        this.FindControl<DliPanel>("DliPanelView")!.SetState(_ws, _opt, _session);
        this.FindControl<SpriteCanvas>("EditorView")!.SetState(_ws, _opt, _session);
        RebuildSequencesPanel();

        BindButtons();
        BindMenus();
        WireEditorEvents();
        AddKeyBindings();
        this.FindControl<TextBlock>("VersionLbl")!.Text = $"{Defines.ProductName} v.{Defines.Version}";
        // Full refresh syncs minimap + reference indicator/info + undo/redo state in one pass.
        RefreshView();
    }

    // -------------------- sequences panel

    private readonly System.Collections.Generic.List<SequenceRow> _sequenceRows = new();
    private int _playCursor;              // current slot index within the playing sequence

    private void RebuildSequencesPanel()
    {
        _ws.SyncPrimary();
        var panel = this.FindControl<StackPanel>("SequencesPanel")!;
        panel.Children.Clear();
        _sequenceRows.Clear();

        for (int i = 0; i < _ws.Sequences.Count; i++)
        {
            var row = new SequenceRow();
            int idx = i;
            row.SetState(_ws, _opt, _session, idx);
            row.SelectionChanged += (_, _) => RefreshView();
            row.DragStarted += OnFrameDragStarted;
            row.DelayEditRequested += async (_, e) => await OnDelayEditRequested(e);
            row.PlayRequested += (_, _) =>
            {
                _ws.SelectedSequence = idx;
                _playCursor = 0;
                if (_ws.Sequences[idx].FrameIndices.Count > 0)
                    _ws.SelectedFrame = _ws.Sequences[idx].FrameIndices[0];
                StartPlayback();
                RefreshView();
            };
            row.StopRequested += (_, _) => StopPlayback();
            row.DeleteRequested += (_, _) => DeleteSequence(idx);
            row.Renamed += (_, _) => Storage.SaveWorkspace(_ws);
            panel.Children.Add(row);
            _sequenceRows.Add(row);
        }

        var addBtn = new Button
        {
            Content = "+ Add animation sequence",
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        addBtn.Click += (_, _) => AddSequence();
        panel.Children.Add(addBtn);
    }

    private void AddSequence()
    {
        PushUndo();
        string baseName = "Seq";
        int n = 1;
        while (_ws.Sequences.Exists(s => s.Name == $"{baseName} {n}")) n++;
        _ws.Sequences.Add(new AnimationSequence { Name = $"{baseName} {n}" });
        _ws.SelectedSequence = _ws.Sequences.Count - 1;
        RebuildSequencesPanel();
        Storage.SaveWorkspace(_ws);
    }

    private async void DeleteSequence(int idx)
    {
        if (idx <= 0 || idx >= _ws.Sequences.Count) return; // can't delete Primary
        var name = _ws.Sequences[idx].Name;
        if (!await AskConfirm($"Delete animation sequence ‘{name}’?")) return;
        PushUndo();
        _ws.Sequences.RemoveAt(idx);
        if (_ws.SelectedSequence >= _ws.Sequences.Count) _ws.SelectedSequence = 0;
        RebuildSequencesPanel();
        Storage.SaveWorkspace(_ws);
    }

    // ---- drag-and-drop controller ----

    private sealed class DragState
    {
        public FrameStrip SourceStrip = null!;
        public int SourceSequenceIndex;
        public int SourceSlot;
        public int PoolFrameIndex;
        public Avalonia.Controls.Image Ghost = null!;
        public FrameStrip? CurrentTargetStrip;
    }
    private DragState? _drag;

    private async Task OnDelayEditRequested(DelayEditEventArgs e)
    {
        if (e.PoolFrameIndex < 0 || e.PoolFrameIndex >= _ws.Frames.Count) return;
        var v = await PromptInt(
            "Frame delay",
            $"Delay for frame {e.PoolFrameIndex} (in 1/50 s units).\n" +
             "Choose Yes after OK to apply to the entire movie loop.",
            e.CurrentDelay, 1, 255);
        if (v is not int newDelay) return;
        PushUndo();
        bool rangeApply = _session.MovieLoop is { } ml && ml.First <= e.PoolFrameIndex && e.PoolFrameIndex <= ml.Last;
        if (rangeApply && await AskConfirm($"Apply delay {newDelay} to all frames in the movie loop ({_session.MovieLoop!.Value.First}..{_session.MovieLoop!.Value.Last})?"))
        {
            for (int i = _session.MovieLoop!.Value.First; i <= _session.MovieLoop!.Value.Last; i++)
                _ws.Frames[i].DelayTime = newDelay;
        }
        else
        {
            _ws.Frames[e.PoolFrameIndex].DelayTime = newDelay;
        }
        RefreshView();
    }

    private void OnFrameDragStarted(object? sender, FrameDragBeginEventArgs e)
    {
        if (_drag != null) return;
        var top = TopLevel.GetTopLevel(this)!;
        var overlay = this.FindControl<Canvas>("DragOverlay")!;

        // Render a thumbnail bitmap for the dragged frame and show it as the ghost.
        var bmp = BuildFrameThumbnail(e.PoolFrameIndex);
        var ghost = new Avalonia.Controls.Image
        {
            Width = FrameStrip.ThumbWidth,
            Height = FrameStrip.ThumbHeight,
            Source = bmp,
            Opacity = 0.75,
            IsHitTestVisible = false
        };
        overlay.Children.Add(ghost);

        _drag = new DragState
        {
            SourceStrip = e.SourceStrip,
            SourceSequenceIndex = e.SourceSequenceIndex,
            SourceSlot = e.SourceSlot,
            PoolFrameIndex = e.PoolFrameIndex,
            Ghost = ghost
        };

        // Mark the source thumb as "being dragged" (visual feedback).
        e.SourceStrip.ActiveDragSlot = e.SourceSlot;
        e.SourceStrip.InvalidateVisual();

        MoveGhost(e.PressScreenPoint);

        // Hook window-level events to track the drag until release.
        top.PointerMoved += OnDragPointerMoved;
        top.PointerReleased += OnDragPointerReleased;
        top.PointerCaptureLost += OnDragPointerCaptureLost;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag == null) return;
        var top = TopLevel.GetTopLevel(this)!;
        var screenPt = e.GetPosition(top);
        MoveGhost(screenPt);

        // Hit-test SequenceRows and show insertion indicator on the strip under the cursor.
        FrameStrip? newTargetStrip = null;
        int newInsertion = -1;
        foreach (var row in _sequenceRows)
        {
            if (row.Bounds.Width <= 0) continue;
            var topLeft = row.TranslatePoint(new Point(0, 0), top);
            if (topLeft == null) continue;
            var abs = new Rect(topLeft.Value, row.Bounds.Size);
            if (!abs.Contains(screenPt)) continue;

            newTargetStrip = row.Strip;
            var stripTopLeft = row.Strip.TranslatePoint(new Point(0, 0), top);
            if (stripTopLeft != null)
            {
                double localX = screenPt.X - stripTopLeft.Value.X;
                newInsertion = row.Strip.InsertionSlotAt(new Point(localX, 0));
            }
            break;
        }

        if (_drag.CurrentTargetStrip != newTargetStrip)
        {
            if (_drag.CurrentTargetStrip != null)
            {
                _drag.CurrentTargetStrip.ActiveDragInsertion = -1;
                _drag.CurrentTargetStrip.InvalidateVisual();
            }
            _drag.CurrentTargetStrip = newTargetStrip;
        }
        if (newTargetStrip != null)
        {
            newTargetStrip.ActiveDragInsertion = newInsertion;
            newTargetStrip.InvalidateVisual();
        }
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == null) return;
        var top = TopLevel.GetTopLevel(this)!;
        var screenPt = e.GetPosition(top);

        // Determine drop target.
        FrameStrip? targetStrip = null;
        int targetSeqIdx = -1;
        int insertSlot = -1;
        foreach (var row in _sequenceRows)
        {
            if (row.Bounds.Width <= 0) continue;
            var topLeft = row.TranslatePoint(new Point(0, 0), top);
            if (topLeft == null) continue;
            var abs = new Rect(topLeft.Value, row.Bounds.Size);
            if (!abs.Contains(screenPt)) continue;
            targetStrip = row.Strip;
            targetSeqIdx = row.SequenceIndex;
            var stripTopLeft = row.Strip.TranslatePoint(new Point(0, 0), top);
            if (stripTopLeft != null)
            {
                double localX = screenPt.X - stripTopLeft.Value.X;
                insertSlot = row.Strip.InsertionSlotAt(new Point(localX, 0));
            }
            break;
        }

        if (targetStrip != null && targetSeqIdx >= 0)
            PerformDrop(targetSeqIdx, insertSlot);

        EndDrag();
    }

    private void OnDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_drag == null) return;
        var top = TopLevel.GetTopLevel(this)!;
        top.PointerMoved -= OnDragPointerMoved;
        top.PointerReleased -= OnDragPointerReleased;
        top.PointerCaptureLost -= OnDragPointerCaptureLost;

        var overlay = this.FindControl<Canvas>("DragOverlay")!;
        overlay.Children.Remove(_drag.Ghost);

        _drag.SourceStrip.ActiveDragSlot = -1;
        _drag.SourceStrip.InvalidateVisual();
        if (_drag.CurrentTargetStrip != null)
        {
            _drag.CurrentTargetStrip.ActiveDragInsertion = -1;
            _drag.CurrentTargetStrip.InvalidateVisual();
        }
        _drag = null;
    }

    private void MoveGhost(Point screenPt)
    {
        if (_drag == null) return;
        // Position ghost centered on cursor (offset so pointer appears inside thumbnail).
        Canvas.SetLeft(_drag.Ghost, screenPt.X - FrameStrip.ThumbWidth / 2.0);
        Canvas.SetTop(_drag.Ghost, screenPt.Y - FrameStrip.ThumbHeight / 2.0);
    }

    private Avalonia.Media.Imaging.WriteableBitmap BuildFrameThumbnail(int poolIdx)
    {
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int cw = Math.Max(1, FrameStrip.ThumbWidth / size.Cols);
        int ch = Math.Max(1, FrameStrip.ThumbHeight / size.Rows);
        var bytes = FrameRenderer.RenderBgra(_ws, _opt, _session, poolIdx, cw, ch, out int w, out int h);
        var bmp = new Avalonia.Media.Imaging.WriteableBitmap(
            new Avalonia.PixelSize(w, h), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        unsafe
        {
            fixed (byte* src = bytes)
                Buffer.MemoryCopy(src, (void*)fb.Address, bytes.Length, bytes.Length);
        }
        return bmp;
    }

    private void PerformDrop(int targetSeqIdx, int insertSlot)
    {
        if (_drag == null) return;
        var targetSeq = _ws.Sequences[targetSeqIdx];
        PushUndo();

        if (targetSeqIdx == _drag.SourceSequenceIndex)
        {
            // Reorder within the same sequence.
            if (targetSeq.IsPrimary)
            {
                int src = _drag.SourceSlot;
                int dst = insertSlot;
                if (dst > src) dst--;
                dst = Math.Clamp(dst, 0, _ws.Frames.Count - 1);
                if (src != dst)
                {
                    // Permute the pool AND remap every sub-sequence index.
                    int count = _ws.Frames.Count;
                    var map = BuildMoveMap(count, src, dst);
                    var f = _ws.Frames[src];
                    _ws.Frames.RemoveAt(src);
                    _ws.Frames.Insert(dst, f);
                    RemapSequenceIndices(map);
                    _ws.SelectedFrame = dst;
                }
                _ws.SyncPrimary();
            }
            else
            {
                // Sub-sequence: reorder the index list only; pool untouched.
                var list = targetSeq.FrameIndices;
                var moved = list[_drag.SourceSlot];
                list.RemoveAt(_drag.SourceSlot);
                int ins = insertSlot;
                if (ins > _drag.SourceSlot) ins--;
                ins = Math.Clamp(ins, 0, list.Count);
                list.Insert(ins, moved);
            }
        }
        else
        {
            if (targetSeq.IsPrimary) return; // Primary mirrors the pool; can't inject.
            int ins = Math.Clamp(insertSlot, 0, targetSeq.FrameIndices.Count);
            targetSeq.FrameIndices.Insert(ins, _drag.PoolFrameIndex);
        }

        foreach (var row in _sequenceRows) row.Rebuild();
        Storage.SaveWorkspace(_ws);
        RefreshView();
    }

    private void BindMenus()
    {
        void M(string name, Action a) => this.FindControl<MenuItem>(name)!.Click += (_, _) => a();

        // File
        M("MiNew",         NewSprite);
        M("MiOpen",        async () => await OpenFile());
        M("MiSave",        async () => await SaveFile());
        M("MiExportSpr",   async () => await ExportSpr());
        M("MiExport",      ShowExport);
        M("MiExportGif",   async () => await ExportGif());
        M("MiLoadBack",    async () => await LoadBackground());
        M("MiToggleBack",  () => { _opt.ShowBackground = !_opt.ShowBackground; RefreshView(); });
        M("MiLoadReference", async () => await LoadReferenceImage());
        M("MiClearReference", ClearReferenceImage);
        M("MiLibrary",         async () => await ShowLibraryDialog());
        M("MiSaveToLocalLibrary", async () => await SaveToLocalLibrary());
        M("MiAnodeIn",     async () => await ANodeFromClipboard());
        M("MiAnodeOut",    async () => await ANodeToClipboard());
        M("MiExit",        () => Close());

        // Edit
        M("MiUndo",        DoUndo);
        M("MiRedo",        DoRedo);
        M("MiCopyFrame",   () => Transforms.CopyFrame(_ws, _session));
        M("MiPasteFrame",  () => Apply(() => Transforms.PasteFrame(_ws, _opt, _session)));
        M("MiClearFrame",  () => Apply(() => Transforms.ClearFrame(_ws, _opt, _session)));
        M("MiCopyColors",  () => Transforms.CopyColors(_ws, _opt, _session));
        M("MiPasteColors", () => Apply(() => Transforms.PasteColors(_ws, _opt, _session)));
        M("MiCopyDli",     () => Transforms.CopyDli(_ws, _opt, _session));
        M("MiPasteDli",    () => Apply(() => Transforms.PasteDli(_ws, _opt, _session)));
        M("MiOptions",     async () => await ShowOptions());

        // Frame
        M("MiAddFrame",    AddFrame);
        M("MiCloneFrame",  CloneFrame);
        M("MiDelFrame",    DeleteFrame);
        M("MiDelAll",      DeleteAll);
        M("MiFrameLeft",   AnimFrameLeft);
        M("MiFrameRight",  AnimFrameRight);
        M("MiPlay",        TogglePlay);

        // Transform
        M("MiFlipH",         () => Apply(() => Transforms.FlipH(_ws, _opt, _session)));
        M("MiFlipV",         () => Apply(() => Transforms.FlipV(_ws, _opt, _session)));
        M("MiMoveL",         () => Apply(() => Transforms.MoveLeft(_ws, _opt, _session)));
        M("MiMoveR",         () => Apply(() => Transforms.MoveRight(_ws, _opt, _session)));
        M("MiMoveU",         () => Apply(() => Transforms.MoveUp(_ws, _opt, _session)));
        M("MiMoveD",         () => Apply(() => Transforms.MoveDown(_ws, _opt, _session)));
        M("MiHeightUp",      () => Apply(() => Transforms.HeightUp(_ws, _opt, _session)));
        M("MiHeightDown",    () => Apply(() => Transforms.HeightDown(_ws, _opt, _session)));
        M("MiSwapPairs",     () => Apply(() => Transforms.SwapPairs(_ws, _opt, _session)));
        M("MiClearOverlaps", () => Apply(() => Transforms.ClearOverlaps(_ws, _opt, _session)));

        // View
        M("MiGrid",        () => { _opt.ShowGrid = _opt.ShowGrid > 0 ? 0 : 1; RefreshView(); });
        M("MiSq",          () => { _opt.SquarePixel = !_opt.SquarePixel; RefreshView(); });
        M("MiMark",        () => { _opt.MarkActiveRegion = !_opt.MarkActiveRegion; RefreshView(); });
        M("MiOr",          () => { _opt.OrDrawsOutside = !_opt.OrDrawsOutside; RefreshView(); });
        M("MiWrap",        ToggleWrapEditor);
        M("MiLayer01",     () => { _session.Layer01Visible = !_session.Layer01Visible; RefreshView(); });
        M("MiLayer23",     () => { _session.Layer23Visible = !_session.Layer23Visible; RefreshView(); });
        M("MiLink",        () => { _opt.LinkColors = !_opt.LinkColors; RefreshView(); });
        M("MiZoomIn",      ZoomIn);
        M("MiZoomOut",     ZoomOut);

        // Help
        M("MiHelp",        () => new HelpDialog().ShowDialog(this));
        M("MiAbout",       ShowAbout);
    }

    // ------------------------ wiring

    private void BindButtons()
    {
        void H(string name, Action a) => this.FindControl<Button>(name)!.Click += (_, _) => a();

        // Row 1 — App menu
        H("LoadBtn",    async () => await OpenFile());
        H("SaveBtn",    async () => await SaveFile());
        H("ExportBtn",  ShowExport);
        H("UndoBtn",    DoUndo);
        H("RedoBtn",    DoRedo);
        H("OptionsBtn", async () => await ShowOptions());
        H("HelpBtn",    () => new HelpDialog().ShowDialog(this));

        // Row 2 — Frame-edit menu
        H("ClearFrameBtn",   () => Apply(() => Transforms.ClearFrame(_ws, _opt, _session)));
        H("CopyFrameBtn",    () => Transforms.CopyFrame(_ws, _session));
        H("PasteFrameBtn",   () => Apply(() => Transforms.PasteFrame(_ws, _opt, _session)));
        H("FlipHBtn",        () => Apply(() => Transforms.FlipH(_ws, _opt, _session)));
        H("FlipVBtn",        () => Apply(() => Transforms.FlipV(_ws, _opt, _session)));
        H("MoveLBtn",        () => Apply(() => Transforms.MoveLeft(_ws, _opt, _session)));
        H("MoveRBtn",        () => Apply(() => Transforms.MoveRight(_ws, _opt, _session)));
        H("MoveUBtn",        () => Apply(() => Transforms.MoveUp(_ws, _opt, _session)));
        H("MoveDBtn",        () => Apply(() => Transforms.MoveDown(_ws, _opt, _session)));
        H("HeightUpBtn",     () => Apply(() => Transforms.HeightUp(_ws, _opt, _session)));
        H("HeightDownBtn",   () => Apply(() => Transforms.HeightDown(_ws, _opt, _session)));
        H("ClearOverlapBtn", () => Apply(() => Transforms.ClearOverlaps(_ws, _opt, _session)));
        H("SwapPairsBtn",    () => Apply(() => Transforms.SwapPairs(_ws, _opt, _session)));
        H("LibraryBtn",      async () => await ShowLibraryDialog());

        // Row 4 — Timeline menu
        H("PlayBtn",       StartPlayback);
        H("StopBtn",       StopPlayback);
        H("AddFrameBtn",   AddFrame);
        H("CloneFrameBtn", CloneFrame);
        H("DelFrameBtn",   DeleteFrame);
        H("FrameLBtn",     AnimFrameLeft);
        H("FrameRBtn",     AnimFrameRight);
        H("DelAllBtn",     DeleteAll);
        H("ExportGifBtn",  async () => await ExportGif());

        // Quick actions grid next to the color column (duplicates Row 2 transforms).
        H("QaFlipHBtn", () => Apply(() => Transforms.FlipH(_ws, _opt, _session)));
        H("QaFlipVBtn", () => Apply(() => Transforms.FlipV(_ws, _opt, _session)));
        H("QaMoveLBtn", () => Apply(() => Transforms.MoveLeft(_ws, _opt, _session)));
        H("QaMoveRBtn", () => Apply(() => Transforms.MoveRight(_ws, _opt, _session)));
        H("QaMoveUBtn", () => Apply(() => Transforms.MoveUp(_ws, _opt, _session)));
        H("QaMoveDBtn", () => Apply(() => Transforms.MoveDown(_ws, _opt, _session)));
        H("QaUndoBtn",  DoUndo);
        H("QaRedoBtn",  DoRedo);
        H("QaZoomInBtn",  ZoomIn);
        H("QaZoomOutBtn", ZoomOut);

        // Wrap toggle — bound two-way to _opt.WrapEditor.
        var wrapBtn = this.FindControl<ToggleButton>("QaWrapBtn")!;
        wrapBtn.IsChecked = _opt.WrapEditor;
        UpdateWrapIndicator();
        wrapBtn.IsCheckedChanged += (_, _) =>
        {
            _opt.WrapEditor = wrapBtn.IsChecked == true;
            UpdateWrapIndicator();
        };

        // DLI toggle — bound two-way to _opt.DliOn. Mutually exclusive with CommonPalette.
        var dliBtn = this.FindControl<ToggleButton>("QaDliBtn")!;
        dliBtn.IsChecked = _opt.DliOn;
        UpdateDliIndicator();
        bool suppressDliEvent = false;
        dliBtn.IsCheckedChanged += async (_, _) =>
        {
            if (suppressDliEvent) return;
            bool want = dliBtn.IsChecked == true;
            if (want == _opt.DliOn) return;

            // Turning DLI on while CommonPalette is on is destructive to the "common" semantic:
            // every frame will adopt the current (frame 0) palette and then CommonPalette flips off.
            if (want && _opt.CommonPalette)
            {
                bool ok = await AskConfirm(
                    "DLI mode requires the Common Palette to be turned off.\n\n" +
                    "The current (frame 0) palette will be copied into every frame first, " +
                    "then Common Palette will be disabled and DLI will be enabled.\n\n" +
                    "Continue?");
                if (!ok)
                {
                    suppressDliEvent = true;
                    dliBtn.IsChecked = false;
                    suppressDliEvent = false;
                    return;
                }
                PushUndo();
                PropagateCommonPaletteToAllFrames();
                _opt.CommonPalette = false;
            }

            _opt.DliOn = want;
            if (_opt.DliOn) ReseedDliFromFrameColors();
            UpdateDliIndicator();
            RefreshView();
        };

        // Reference-image show toggle.
        var refBtn = this.FindControl<ToggleButton>("ReferenceShowBtn")!;
        refBtn.IsChecked = _opt.ShowReference;
        UpdateReferenceIndicator();
        refBtn.IsCheckedChanged += (_, _) =>
        {
            _opt.ShowReference = refBtn.IsChecked == true;
            UpdateReferenceIndicator();
            this.FindControl<SpriteCanvas>("EditorView")!.SetState(_ws, _opt, _session);
        };

        // Minimap pan — re-render the sprite editor when the yellow box moves.
        var minimap = this.FindControl<ReferenceMinimap>("ReferenceMinimapView")!;
        minimap.PanChanged += (_, _) =>
        {
            this.FindControl<SpriteCanvas>("EditorView")!.SetState(_ws, _opt, _session);
            UpdateReferenceInfo();
        };

        // Load and Clear buttons under the Show reference toggle.
        H("ReferenceLoadBtn",  async () => await LoadReferenceImage());
        H("ReferenceClearBtn", ClearReferenceImage);

        // Reference-image per-axis zoom spinners. Each unit means "sample every N reference
        // pixels per sprite cell" — set to 5 if your reference art is drawn at 5× scale.
        var scaleX = this.FindControl<TinyNumericUpDown>("RefScaleXNud")!;
        var scaleY = this.FindControl<TinyNumericUpDown>("RefScaleYNud")!;
        scaleX.Value = Math.Max(1, _opt.ReferenceScaleX);
        scaleY.Value = Math.Max(1, _opt.ReferenceScaleY);
        scaleX.ValueChanged += (_, v) =>
        {
            _opt.ReferenceScaleX = Math.Max(1, v);
            RefreshReferenceViews();
        };
        scaleY.ValueChanged += (_, v) =>
        {
            _opt.ReferenceScaleY = Math.Max(1, v);
            RefreshReferenceViews();
        };
    }

    /// <summary>Re-render the editor and the minimap to reflect reference pan/scale changes.</summary>
    private void RefreshReferenceViews()
    {
        this.FindControl<SpriteCanvas>("EditorView")!.SetState(_ws, _opt, _session);
        this.FindControl<ReferenceMinimap>("ReferenceMinimapView")?.SetState(_ws, _opt);
    }

    /// <summary>
    /// Copy frame 0's base colors (COL0..3) into every frame. Used when turning off
    /// CommonPalette so each frame locks in what was previously the shared palette.
    /// </summary>
    private void PropagateCommonPaletteToAllFrames()
    {
        if (_ws.Frames.Count == 0) return;
        var template = _ws.Frames[0].Colors;
        for (int i = 1; i < _ws.Frames.Count; i++)
            for (int c = 0; c < 4; c++)
                _ws.Frames[i].Colors[c] = template[c];
    }

    /// <summary>Re-seed every frame's DliTable rows to the frame's current base colors.</summary>
    private void ReseedDliFromFrameColors()
    {
        foreach (var f in _ws.Frames)
            f.Dli = DliTable.Create(_opt.SpriteHeight, f.Colors, _ws.BackgroundColor);
    }

    /// <summary>Set the Wrap button's ✓/✗ indicator glyph to match current state.</summary>
    private void UpdateWrapIndicator()
    {
        var indicator = this.FindControl<TextBlock>("QaWrapIndicator");
        if (indicator == null) return;
        indicator.Text = _opt.WrapEditor ? "✓" : "✗";
    }

    /// <summary>Set the DLI button's ✓/✗ indicator glyph to match current state.</summary>
    private void UpdateDliIndicator()
    {
        var indicator = this.FindControl<TextBlock>("QaDliIndicator");
        if (indicator == null) return;
        indicator.Text = _opt.DliOn ? "✓" : "✗";
    }

    private void WireEditorEvents()
    {
        var editor = this.FindControl<SpriteCanvas>("EditorView")!;
        editor.StrokeBegan += (_, _) => { _beforeStroke = _ws.Clone(); };
        editor.StrokeEnded += (_, _) =>
        {
            if (_beforeStroke != null) { _undo.Push(_beforeStroke); _beforeStroke = null; }
            foreach (var row in _sequenceRows) row.Rebuild();
            // Repaint the ColorStrip so its three preview thumbnails show the new pixels.
            this.FindControl<ColorStrip>("ColorStripView")?.InvalidateVisual();
            Storage.SaveWorkspace(_ws);
        };
        editor.InsertRowRequested += (_, row) => { Apply(() => Transforms.InsertLineAt(_ws, _opt, _session, row)); };
        editor.DeleteRowRequested += (_, row) => { Apply(() => Transforms.DeleteLineAt(_ws, _opt, _session, row)); };

        var colorStrip = this.FindControl<ColorStrip>("ColorStripView")!;
        colorStrip.SelectionChanged += (_, _) => RefreshView();
        colorStrip.ColorEdited += (_, _) => { PushUndo(); RefreshView(); };
        colorStrip.VisibilityChanged += (_, _) => RefreshView();

        var dli = this.FindControl<DliPanel>("DliPanelView")!;
        dli.SelectionChanged += (_, _) => RefreshView();
        dli.ColorEdited += (_, _) => { PushUndo(); RefreshView(); };
    }

    private void AddKeyBindings()
    {
        KeyDown += (_, e) =>
        {
            if (e.Source is TextBox) return;  // skip when editing text fields
            switch (e.Key)
            {
                case Key.Space:    TogglePlay(); e.Handled = true; break;
                case Key.Left:     if (!_playing) { _session.MovieLoop = null; _ws.SelectedFrame = (_ws.SelectedFrame - 1 + _ws.Frames.Count) % _ws.Frames.Count; RefreshView(); } break;
                case Key.Right:    if (!_playing) { _session.MovieLoop = null; _ws.SelectedFrame = (_ws.SelectedFrame + 1) % _ws.Frames.Count; RefreshView(); } break;
                case Key.Home:     _ws.SelectedFrame = 0; RefreshView(); break;
                case Key.End:      _ws.SelectedFrame = _ws.Frames.Count - 1; RefreshView(); break;
                case Key.Up:       if (_opt.DliOn) { _ws.SelectedDli = (_ws.SelectedDli - 1 + _opt.SpriteHeight) % _opt.SpriteHeight; _session.DliRange = null; RefreshView(); } break;
                case Key.Down:     if (_opt.DliOn) { _ws.SelectedDli = (_ws.SelectedDli + 1) % _opt.SpriteHeight; _session.DliRange = null; RefreshView(); } break;
                case Key.Insert:   AddFrame(); break;
                case Key.Delete:   DeleteFrame(); break;

                case Key.D0: case Key.NumPad0: case Key.OemTilde:
                    _ws.SelectedColor = 0; RefreshView(); break;
                case Key.D1: case Key.NumPad1: _ws.SelectedColor = 1; RefreshView(); break;
                case Key.D2: case Key.NumPad2: _ws.SelectedColor = 2; RefreshView(); break;
                case Key.D3: case Key.NumPad3: _ws.SelectedColor = 3; RefreshView(); break;
                case Key.D4: case Key.NumPad4: _ws.SelectedColor = 5; RefreshView(); break;
                case Key.D5: case Key.NumPad5: _ws.SelectedColor = 6; RefreshView(); break;
                case Key.D6: case Key.NumPad6: _ws.SelectedColor = 7; RefreshView(); break;

                case Key.OemOpenBrackets:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) Transforms.CopyDli(_ws, _opt, _session);
                    else Transforms.CopyColors(_ws, _opt, _session);
                    break;
                case Key.OemCloseBrackets:
                    if ((e.KeyModifiers & KeyModifiers.Shift) != 0) Apply(() => Transforms.PasteDli(_ws, _opt, _session));
                    else Apply(() => Transforms.PasteColors(_ws, _opt, _session));
                    break;

                case Key.W: ToggleWrapEditor(); break;
                case Key.O when (e.KeyModifiers & KeyModifiers.Control) == 0:
                    _opt.OrDrawsOutside = !_opt.OrDrawsOutside; RefreshView(); break;
                case Key.G: _opt.ShowGrid = _opt.ShowGrid > 0 ? 0 : 1; RefreshView(); break;
                case Key.M: _opt.MarkActiveRegion = !_opt.MarkActiveRegion; RefreshView(); break;
                case Key.OemMinus: case Key.Subtract: ZoomOut(); break;
                case Key.OemPlus:  case Key.Add:      ZoomIn();  break;

                case Key.C when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    Transforms.CopyFrame(_ws, _session); break;
                case Key.V when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    Apply(() => Transforms.PasteFrame(_ws, _opt, _session)); break;
                case Key.Z when (e.KeyModifiers & KeyModifiers.Control) != 0: DoUndo(); break;
                case Key.Y when (e.KeyModifiers & KeyModifiers.Control) != 0: DoRedo(); break;
                case Key.S when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    _ = SaveFile(); e.Handled = true; break;
                case Key.O when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    _ = OpenFile(); e.Handled = true; break;
                case Key.E when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    ShowExport(); e.Handled = true; break;
                case Key.N when (e.KeyModifiers & KeyModifiers.Control) != 0:
                    NewSprite(); e.Handled = true; break;
                case Key.F1: new HelpDialog().ShowDialog(this); break;

                case Key.LeftAlt: case Key.RightAlt:
                    _session.AltPressed = true;
                    this.FindControl<SpriteCanvas>("EditorView")!.InvalidateVisual();
                    break;
            }
        };
        KeyUp += (_, e) =>
        {
            if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
            {
                _session.AltPressed = false;
                this.FindControl<SpriteCanvas>("EditorView")!.InvalidateVisual();
            }
        };
    }

    // ------------------------ frame ops

    /// <summary>Shift all custom-sequence indices >= <paramref name="insertedAt"/> by <paramref name="count"/>.</summary>
    private void ShiftSequenceIndicesAfterInsert(int insertedAt, int count)
    {
        for (int s = 1; s < _ws.Sequences.Count; s++)
        {
            var list = _ws.Sequences[s].FrameIndices;
            for (int i = 0; i < list.Count; i++)
                if (list[i] >= insertedAt) list[i] += count;
        }
    }

    /// <summary>Drop refs inside [first, first+len), shift refs after the range down by len.</summary>
    private void AdjustSequenceIndicesAfterRemove(int first, int len)
    {
        int last = first + len - 1;
        for (int s = 1; s < _ws.Sequences.Count; s++)
        {
            var list = _ws.Sequences[s].FrameIndices;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] >= first && list[i] <= last) list.RemoveAt(i);
                else if (list[i] > last) list[i] -= len;
            }
        }
    }

    /// <summary>
    /// Apply an old→new pool-index mapping to every sub-sequence. Use when a reorder
    /// operation permuted pool slots (drag Primary / move-frame-left / move-frame-right).
    /// </summary>
    private void RemapSequenceIndices(int[] oldToNew)
    {
        for (int s = 1; s < _ws.Sequences.Count; s++)
        {
            var list = _ws.Sequences[s].FrameIndices;
            for (int i = 0; i < list.Count; i++)
            {
                int old = list[i];
                if (old >= 0 && old < oldToNew.Length) list[i] = oldToNew[old];
            }
        }
    }

    /// <summary>
    /// Build an old→new index map for "move element from srcIdx to dstIdx in a 0..count-1 list",
    /// matching C#'s RemoveAt + Insert semantics (dstIdx is in the post-removal coordinate space).
    /// </summary>
    private static int[] BuildMoveMap(int count, int srcIdx, int dstIdx)
    {
        var map = new int[count];
        for (int i = 0; i < count; i++) map[i] = i;
        if (srcIdx == dstIdx || srcIdx < 0 || srcIdx >= count) return map;
        if (srcIdx < dstIdx)
        {
            // Element moves right; items in (srcIdx..dstIdx] shift left by 1.
            for (int i = srcIdx + 1; i <= dstIdx; i++) map[i] = i - 1;
            map[srcIdx] = dstIdx;
        }
        else
        {
            // Element moves left; items in [dstIdx..srcIdx) shift right by 1.
            for (int i = dstIdx; i < srcIdx; i++) map[i] = i + 1;
            map[srcIdx] = dstIdx;
        }
        return map;
    }

    private void AddFrame()
    {
        PushUndo();
        int at = _ws.SelectedFrame + 1;
        _ws.Frames.Insert(at, Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor));
        ShiftSequenceIndicesAfterInsert(at, 1);
        _ws.SelectedFrame++;
        _ws.SyncPrimary();
        RebuildSequencesPanel();
        RefreshView();
    }

    private void CloneFrame()
    {
        if (_ws.Frames.Count == 0) return;
        PushUndo();
        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        int len = last - first + 1;
        for (int f = first; f <= last; f++)
            _ws.Frames.Insert(f + len, _ws.Frames[f].Clone());
        ShiftSequenceIndicesAfterInsert(first + len, len);
        if (_opt.MultiFrameEdit && _session.MovieLoop is { } m)
            _session.MovieLoop = (m.First + len, m.Last + len);
        _ws.SelectedFrame = Math.Min(_ws.Frames.Count - 1, _ws.SelectedFrame + len);
        _ws.SyncPrimary();
        RebuildSequencesPanel();
        RefreshView();
    }

    private void DeleteFrame()
    {
        // Behavior depends on which sequence is active:
        //   Primary   → delete from the pool (destructive; every sub-sequence loses it too).
        //   Sub-seq   → unlink the slot's reference. Pool and other sequences stay intact.
        var active = _ws.ActiveSequence;
        if (!active.IsPrimary)
        {
            DeleteFromSubSequence(active);
            return;
        }

        if (_ws.Frames.Count <= 1) return;
        PushUndo();
        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        int len = last - first + 1;
        if (_ws.Frames.Count - len < 1) return;
        _ws.Frames.RemoveRange(first, len);
        AdjustSequenceIndicesAfterRemove(first, len);
        _ws.SelectedFrame = Math.Min(_ws.Frames.Count - 1, first);
        _session.MovieLoop = null;
        _ws.SyncPrimary();
        RebuildSequencesPanel();
        RefreshView();
    }

    /// <summary>
    /// Remove the current slot (or multi-frame range) from a sub-sequence only.
    /// The pool and other sequences are unchanged.
    /// </summary>
    private void DeleteFromSubSequence(AnimationSequence seq)
    {
        if (seq.FrameIndices.Count == 0) return;
        // Find the slot in this sequence that holds the currently selected pool frame.
        int slot = seq.FrameIndices.IndexOf(_ws.SelectedFrame);
        if (slot < 0) return;
        PushUndo();
        seq.FrameIndices.RemoveAt(slot);
        // Select the next slot's frame, or the previous if we removed the last slot.
        if (seq.FrameIndices.Count > 0)
        {
            int newSlot = Math.Clamp(slot, 0, seq.FrameIndices.Count - 1);
            _ws.SelectedFrame = seq.FrameIndices[newSlot];
        }
        Storage.SaveWorkspace(_ws);
        foreach (var row in _sequenceRows) row.Rebuild();
        RefreshView();
    }

    private async void DeleteAll()
    {
        var yes = await AskConfirm("Delete and erase all frames?\nNo undo for this.");
        if (!yes) return;
        PushUndo();
        _ws.Frames.Clear();
        _ws.Frames.Add(Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor));
        // Reset all sequences to empty (Primary will auto-sync).
        _ws.Sequences.Clear();
        _ws.SelectedSequence = 0;
        _ws.SelectedFrame = 0;
        _session.MovieLoop = null;
        _ws.SyncPrimary();
        RebuildSequencesPanel();
        RefreshView();
    }

    private void AnimFrameLeft()
    {
        if (!_ws.ActiveSequence.IsPrimary) { MoveSubSlot(-1); return; }
        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        if (first == 0) return;
        PushUndo();
        // Move pool frame (first - 1) past the range [first..last], so [first..last] shifts left by 1.
        int count = _ws.Frames.Count;
        int src = first - 1;
        int dst = last; // after RemoveAt(src), insert at `last` (which is now original index last after shift).
        var map = BuildMoveMap(count, src, dst);
        var tmp = _ws.Frames[src];
        _ws.Frames.RemoveAt(src);
        _ws.Frames.Insert(dst, tmp);
        RemapSequenceIndices(map);
        if (_opt.MultiFrameEdit && _session.MovieLoop is { } m)
            _session.MovieLoop = (m.First - 1, m.Last - 1);
        _ws.SelectedFrame--;
        _ws.SyncPrimary();
        foreach (var row in _sequenceRows) row.Rebuild();
        Storage.SaveWorkspace(_ws);
        RefreshView();
    }

    private void AnimFrameRight()
    {
        if (!_ws.ActiveSequence.IsPrimary) { MoveSubSlot(+1); return; }
        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        if (last == _ws.Frames.Count - 1) return;
        PushUndo();
        int count = _ws.Frames.Count;
        int src = last + 1;
        int dst = first;
        var map = BuildMoveMap(count, src, dst);
        var tmp = _ws.Frames[src];
        _ws.Frames.RemoveAt(src);
        _ws.Frames.Insert(dst, tmp);
        RemapSequenceIndices(map);
        if (_opt.MultiFrameEdit && _session.MovieLoop is { } m)
            _session.MovieLoop = (m.First + 1, m.Last + 1);
        _ws.SelectedFrame++;
        _ws.SyncPrimary();
        foreach (var row in _sequenceRows) row.Rebuild();
        Storage.SaveWorkspace(_ws);
        RefreshView();
    }

    /// <summary>
    /// Within the active sub-sequence, move the slot pointing at <see cref="Workspace.SelectedFrame"/>
    /// by <paramref name="dir"/> positions (-1 = left, +1 = right). Pool untouched.
    /// </summary>
    private void MoveSubSlot(int dir)
    {
        var seq = _ws.ActiveSequence;
        if (seq.IsPrimary) return;
        int slot = seq.FrameIndices.IndexOf(_ws.SelectedFrame);
        if (slot < 0) return;
        int dst = slot + dir;
        if (dst < 0 || dst >= seq.FrameIndices.Count) return;
        PushUndo();
        (seq.FrameIndices[slot], seq.FrameIndices[dst]) = (seq.FrameIndices[dst], seq.FrameIndices[slot]);
        Storage.SaveWorkspace(_ws);
        foreach (var row in _sequenceRows) row.Rebuild();
        RefreshView();
    }

    // ------------------------ playback

    private void TogglePlay()
    {
        if (_playing) StopPlayback(); else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_playing) return;
        var seq = _ws.ActiveSequence;
        if (seq.FrameIndices.Count <= 1) return;
        _playCursor = Math.Max(0, seq.FrameIndices.IndexOf(_ws.SelectedFrame));
        int delay = _opt.FrameDelayMode ? _ws.CurrentFrame.DelayTime : _opt.AnimationSpeed;
        _playTimer.Interval = TimeSpan.FromMilliseconds(delay * 20);
        _playTimer.Start();
        _playing = true;
        _session.AnimationOn = true;
        UpdateStatus();
    }

    private void StopPlayback()
    {
        if (!_playing) return;
        _playTimer.Stop();
        _playing = false;
        _session.AnimationOn = false;
        UpdateStatus();
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        var seq = _ws.ActiveSequence;
        if (seq.FrameIndices.Count == 0) { StopPlayback(); return; }
        _playCursor = (_playCursor + 1) % seq.FrameIndices.Count;
        _ws.SelectedFrame = seq.FrameIndices[_playCursor];
        RefreshView();
        if (_opt.FrameDelayMode)
            _playTimer.Interval = TimeSpan.FromMilliseconds(_ws.CurrentFrame.DelayTime * 20);
    }

    // ------------------------ zoom

    private void ZoomIn()
    {
        if (_opt.CellSize < Defines.ZoomCellSize.Length - 1) _opt.CellSize++;
        RefreshView();
    }

    private void ZoomOut()
    {
        if (_opt.CellSize > 0) _opt.CellSize--;
        RefreshView();
    }

    // ------------------------ undo

    private void PushUndo()
    {
        _undo.Push(_ws);
        UpdateUndoRedoState();
    }

    /// <summary>Toggle the WrapEditor option and keep the quick-actions Wrap button in sync.</summary>
    private void ToggleWrapEditor()
    {
        _opt.WrapEditor = !_opt.WrapEditor;
        var wrapBtn = this.FindControl<ToggleButton>("QaWrapBtn");
        if (wrapBtn != null) wrapBtn.IsChecked = _opt.WrapEditor;
        UpdateWrapIndicator();
    }
    private void Apply(Func<bool> op)
    {
        PushUndo();
        if (!op()) _undo.Undo(_ws);
        RefreshView();
    }

    private void DoUndo()
    {
        var p = _undo.Undo(_ws);
        if (p != null) { _ws = p; RefreshView(); }
    }

    private void DoRedo()
    {
        var p = _undo.Redo(_ws);
        if (p != null) { _ws = p; RefreshView(); }
    }

    // ------------------------ file ops

    private async void NewSprite()
    {
        var dlg = NewSpriteDialog.Create();
        var picked = await dlg.ShowDialog<SpriteTemplate?>(this);
        if (picked == null) return;

        picked.ApplyTo(_opt);
        _ws = new Workspace();
        _ws.Frames.Add(Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor));
        _session.MovieLoop = null;
        _ws.SyncPrimary();
        _undo.Clear();
        UpdateUndoRedoState();
        RebuildSequencesPanel();
        RefreshView();
    }

    private async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SpriteEditXL (.sprj)")  { Patterns = new[] { "*.sprj" } },
                new FilePickerFileType("Legacy sprite binary / APL")  { Patterns = new[] { "*.spr", "*.apl" } },
                new FilePickerFileType("All files")           { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        await LoadPath(path);
    }

    /// <summary>Load a .sprj or legacy .spr/.apl file. Used by both OpenFile and drag-drop.</summary>
    private async Task LoadPath(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Workspace ws; Options opt;

            if (ext == SprJsonFile.DefaultExtension)
            {
                (ws, opt) = SprJsonFile.Load(path);
            }
            else
            {
                // Legacy .spr / .apl path: load binary, auto-save a .sprj sibling for future opens.
                (ws, opt) = SprFile.Load(path);
                SequenceSidecar.TryLoadInto(path, ws);
                ws.SyncPrimary();
                try
                {
                    var sidecarJson = Path.ChangeExtension(path, SprJsonFile.DefaultExtension);
                    if (!File.Exists(sidecarJson))
                        SprJsonFile.Save(sidecarJson, ws, opt);
                }
                catch { /* best effort */ }
            }

            // Preserve pure-UI options that aren't part of the sprite file.
            opt.CellSize = _opt.CellSize;
            opt.SquarePixel = _opt.SquarePixel;
            opt.ShowGrid = _opt.ShowGrid;
            opt.MarkActiveRegion = _opt.MarkActiveRegion;
            opt.WrapEditor = _opt.WrapEditor;
            opt.MultiFrameEdit = _opt.MultiFrameEdit;
            opt.ShowReference = _opt.ShowReference;
            opt.ReferenceScaleX = _opt.ReferenceScaleX;
            opt.ReferenceScaleY = _opt.ReferenceScaleY;

            _ws = ws;
            _opt = opt;
            SanitizeLoadedWorkspace();
            foreach (var f in _ws.Frames) f.SetHeight(_opt.SpriteHeight);
            _ws.SyncPrimary();
            // Fresh file → previous session's undo history is no longer meaningful.
            _undo.Clear();
            UpdateUndoRedoState();
            RebuildSequencesPanel();
            RefreshView();
        }
        catch (Exception ex)
        {
            await ShowError($"Failed to load file:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
        }
    }

    /// <summary>Clamp loaded workspace fields to safe bounds so stale/foreign values don't crash the UI.</summary>
    private void SanitizeLoadedWorkspace()
    {
        if (_ws.Frames.Count == 0)
            _ws.Frames.Add(Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor));
        if (_ws.SelectedFrame < 0 || _ws.SelectedFrame >= _ws.Frames.Count) _ws.SelectedFrame = 0;
        if (_ws.SelectedColor < 0 || _ws.SelectedColor > 7) _ws.SelectedColor = 1;
        if (_ws.SelectedDli < 0 || _ws.SelectedDli >= _opt.SpriteHeight) _ws.SelectedDli = 0;
        if (_ws.HiddenSprites == null || _ws.HiddenSprites.Length != 4)
            _ws.HiddenSprites = new bool[4];
        // Guard against negative / silly sprite height from mangled files.
        if (_opt.SpriteHeight < 1) _opt.SpriteHeight = 16;
        if (_opt.SpriteHeight > 128) _opt.SpriteHeight = 128;
        // MergeMode could be any byte in a hand-crafted file; mask to valid enum range.
        int mm = (int)_opt.MergeMode;
        if (mm < 0 || mm > 7 || mm == 3 || mm == 7)   // 3 and 7 aren't defined
            _opt.MergeMode = SpriteMode.P0P1;
    }

    private async Task SaveFile()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "mysprites.sprj",
            DefaultExtension = "sprj",
            FileTypeChoices = new[] { new FilePickerFileType("SpriteEditXL") { Patterns = new[] { "*.sprj" } } }
        });
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;
        try
        {
            SprJsonFile.Save(path, _ws, _opt);
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    /// <summary>Export the current sprite as a legacy .spr binary.</summary>
    private async Task ExportSpr()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "mysprites.spr",
            DefaultExtension = "spr",
            FileTypeChoices = new[] { new FilePickerFileType("Legacy sprite binary") { Patterns = new[] { "*.spr" } } }
        });
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;
        try
        {
            SprFile.Save(path, _ws, _opt);
            // Also write the sequences sidecar so sub-sequences survive a round-trip through .spr.
            SequenceSidecar.TrySaveFrom(path, _ws);
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task LoadBackground()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length > Defines.MaxFileSize)
            {
                await ShowError($"File larger than {Defines.MaxFileSize / 1024} KB.");
                return;
            }
            _ws.BackgroundImage = BackgroundImage.Parse(bytes);
            _opt.ShowBackground = true;
            RefreshView();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    /// <summary>
    /// Load any common image format (PNG, GIF, JPG, BMP…) as a reference image shown
    /// behind transparent sprite cells. No size limit.
    /// </summary>
    private async Task LoadReferenceImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image files")
                {
                    Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.tif", "*.tiff" }
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        try
        {
            var (bgra, width, height) = ReferenceImageLoader.Load(path);
            _ws.ReferenceImage = bgra;
            _ws.ReferenceImageWidth = width;
            _ws.ReferenceImageHeight = height;
            _ws.ReferenceImagePath = path;
            _opt.ShowReference = true;
            _opt.ReferenceSourceX = 0;
            _opt.ReferenceSourceY = 0;
            Storage.SaveWorkspace(_ws);
            RefreshView();
        }
        catch (Exception ex) { await ShowError($"Could not load image: {ex.Message}"); }
    }

    /// <summary>
    /// If the workspace remembered a reference image file path, try to load it silently
    /// on startup. Failures are ignored (file may have been moved/deleted).
    /// </summary>
    private void TryReloadReferenceFromPath()
    {
        if (_ws.ReferenceImage != null) return;          // already in memory
        var path = _ws.ReferenceImagePath;
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path)) return;
        try
        {
            var (bgra, width, height) = ReferenceImageLoader.Load(path);
            _ws.ReferenceImage = bgra;
            _ws.ReferenceImageWidth = width;
            _ws.ReferenceImageHeight = height;
        }
        catch { /* file may be unreadable now; just skip */ }
    }

    private void ClearReferenceImage()
    {
        _ws.ReferenceImage = null;
        _ws.ReferenceImageWidth = 0;
        _ws.ReferenceImageHeight = 0;
        _ws.ReferenceImagePath = null;
        _opt.ShowReference = false;
        Storage.SaveWorkspace(_ws);
        RefreshView();
    }

    private void ShowExport()
    {
        var dlg = ExportDialog.Create(_ws, _opt);
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// Prompt for a sprite name and write the current workspace as a named .sprj
    /// inside the local library folder (%APPDATA%/SpriteEditXL/library).
    /// </summary>
    private async Task SaveToLocalLibrary()
    {
        var local = new LocalLibrarySource();
        var name = await PromptText("Save to local library",
            "Sprite name (used as the filename):", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var path = local.Save(name, _ws, _opt);
            ShowToast($"Saved to local library: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex) { await ShowError($"Save failed: {ex.Message}"); }
    }

    /// <summary>Ask the user for a single line of text. Returns null on cancel / empty.</summary>
    private async Task<string?> PromptText(string title, string prompt, string initial)
    {
        var tcs = new TaskCompletionSource<string?>();
        var w = new Window
        {
            Title = title, Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var tb = new TextBox { Text = initial };
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        ok.Click += (_, _) => { tcs.TrySetResult(tb.Text); w.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); w.Close(); };
        w.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 10,
            Children =
            {
                new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                tb,
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                 Children = { ok, cancel } }
            }
        };
        _ = w.ShowDialog(this).ContinueWith(_ => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(null); });
        return await tcs.Task;
    }

    /// <summary>
    /// Open the three-tab Library browser. On "Load selected", replaces the current workspace
    /// with the chosen sprite. Upload tab on SpriteEditXL online sends the current workspace.
    /// </summary>
    private async Task ShowLibraryDialog()
    {
        // Ensure we have an UploaderId minted for this install.
        if (string.IsNullOrEmpty(_opt.UploaderId))
        {
            _opt.UploaderId = Guid.NewGuid().ToString("N");
            Storage.SaveOptions(_opt);
        }

        var dlg = LibraryDialog.Create();
        dlg.CurrentWorkspace = _ws;
        dlg.CurrentOptions = _opt;
        dlg.UploaderId = _opt.UploaderId;
        dlg.DefaultAuthor = _opt.DefaultAuthorName;

        var result = await dlg.ShowDialog<(Workspace ws, Options opt)?>(this);
        if (result is not { } picked) return;

        // Preserve UI-only preferences.
        picked.opt.CellSize = _opt.CellSize;
        picked.opt.SquarePixel = _opt.SquarePixel;
        picked.opt.ShowGrid = _opt.ShowGrid;
        picked.opt.MarkActiveRegion = _opt.MarkActiveRegion;
        picked.opt.WrapEditor = _opt.WrapEditor;
        picked.opt.MultiFrameEdit = _opt.MultiFrameEdit;
        picked.opt.ShowReference = _opt.ShowReference;
        picked.opt.UploaderId = _opt.UploaderId;
        picked.opt.DefaultAuthorName = _opt.DefaultAuthorName;

        _ws = picked.ws;
        _opt = picked.opt;
        SanitizeLoadedWorkspace();
        foreach (var f in _ws.Frames) f.SetHeight(_opt.SpriteHeight);
        _ws.SyncPrimary();
        _undo.Clear();
        UpdateUndoRedoState();
        RebuildSequencesPanel();
        RefreshView();
    }

    private async Task ExportGif()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "mysprite.gif",
            FileTypeChoices = new[] { new FilePickerFileType("Animated GIF") { Patterns = new[] { "*.gif" } } }
        });
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;
        try { GifExport.WriteAnimation(path, _ws, _opt, _session, _opt.GifExportScale); }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task ShowOptions()
    {
        var dlg = OptionsDialog.Create(_opt);
        var result = await dlg.ShowDialog<Options?>(this);
        if (result is null) return;
        PushUndo();
        _opt = result;
        foreach (var f in _ws.Frames) f.SetHeight(_opt.SpriteHeight);
        Storage.SaveOptions(_opt);
        RefreshView();
    }

    private async Task ANodeFromClipboard()
    {
        var clip = Clipboard;
        if (clip is null) return;
        var text = await clip.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text) || text.Length != ANodeIcon.HexLength)
        {
            await ShowError("Clipboard does not contain a 126-char aNode icon hex string.");
            return;
        }
        var data = ANodeIcon.Parse(text.Trim());
        if (data is null) { await ShowError("Failed to parse aNode icon."); return; }

        var yes = await AskConfirm("Load aNode icon? This will replace the current workspace.");
        if (!yes) return;
        PushUndo();
        _ws = new Workspace();
        SpriteTemplates.All[2].ApplyTo(_opt); // aNode icon template
        var f = Frame.CreateEmpty(_opt.SpriteHeight, _ws.BackgroundColor);
        Array.Copy(data.P0, f.Player[0], 20);
        Array.Copy(data.P1, f.Player[1], 20);
        Array.Copy(data.M0, f.Missile[0], 20);
        Array.Copy(data.M1, f.Missile[1], 20);
        f.Colors[0] = data.C0; f.Colors[1] = data.C1;
        _ws.Frames.Add(f);
        _opt.Palette = data.IsNtsc ? PaletteKind.NTSC : PaletteKind.PAL;
        RefreshView();
    }

    private async Task ANodeToClipboard()
    {
        if (!ANodeIcon.IsAnodeMode(_opt))
        {
            await ShowError("Current layout is not aNode icon shape (10x20, PM0+PM1, line res 1).");
            return;
        }
        var text = ANodeIcon.Encode(_ws.CurrentFrame, _opt.Palette);
        if (Clipboard is { } clip) await clip.SetTextAsync(text);
    }

    // ------------------------ dialogs

    private async Task ShowError(string message)
    {
        var w = new Window
        {
            Title = "Error",
            Width = 420, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        ((Button)((StackPanel)w.Content).Children[1]).Click += (_, _) => w.Close();
        await w.ShowDialog(this);
    }

    /// <summary>Simple modal number-input prompt. Returns null if cancelled or value is unparseable.</summary>
    private async Task<int?> PromptInt(string title, string message, int initialValue, int min, int max)
    {
        var tcs = new TaskCompletionSource<int?>();
        var w = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var box = new TextBox { Text = initialValue.ToString(), Width = 120 };
        var hint = new TextBlock
        {
            Text = $"Range: {min}–{max}",
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 11
        };
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true,
                              HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true,
                                  HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        void Commit(bool accept)
        {
            if (!accept) { tcs.TrySetResult(null); w.Close(); return; }
            if (int.TryParse(box.Text, out int v))
            {
                v = Math.Clamp(v, min, max);
                tcs.TrySetResult(v);
            }
            else tcs.TrySetResult(null);
            w.Close();
        }
        ok.Click += (_, _) => Commit(true);
        cancel.Click += (_, _) => Commit(false);
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(true); };
        w.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                box, hint,
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                 Children = { ok, cancel } }
            }
        };
        _ = w.ShowDialog(this).ContinueWith(_ => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(null); });
        box.Focus();
        box.SelectAll();
        return await tcs.Task;
    }

    /// <summary>Open a URL in the system default browser. Silently swallows failures.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* best effort */ }
    }

    /// <summary>Non-blocking status message in the bottom-right corner — auto-fades after ~2 seconds.</summary>
    private void ShowToast(string message)
    {
        var host = this.FindControl<Grid>("ToastHost");
        if (host == null) return;
        var tb = new TextBlock
        {
            Text = message,
            Foreground = Avalonia.Media.Brushes.Black,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(230, 244, 212, 122)),
            Padding = new Thickness(14, 8),
            FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 20, 20)
        };
        host.Children.Add(tb);
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        timer.Tick += (_, _) => { timer.Stop(); host.Children.Remove(tb); };
        timer.Start();
    }

    private async Task<bool> AskConfirm(string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var w = new Window
        {
            Title = "Confirm",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var yes = new Button
        {
            Content = "Yes",
            Width = 90,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsDefault = true
        };
        var no = new Button
        {
            Content = "No",
            Width = 90,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsCancel = true
        };
        yes.Click += (_, _) => { tcs.SetResult(true); w.Close(); };
        no.Click  += (_, _) => { tcs.SetResult(false); w.Close(); };
        w.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                 Children = { yes, no } }
            }
        };
        _ = w.ShowDialog(this).ContinueWith(_ => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(false); });
        return await tcs.Task;
    }

    private void ShowAbout()
    {
        var okBtn = new Button
        {
            Content = "OK",
            Width = 90,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsDefault = true,
            IsCancel = true
        };
        var about = new Window
        {
            Title = "About",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        about.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Defines.ProductName, FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 18 },
                new TextBlock { Text = $"v. {Defines.Version}" },
                new TextBlock { Text = "A cross-platform Avalonia Atari sprite editor.",
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                okBtn
            }
        };
        okBtn.Click += (_, _) => about.Close();
        about.ShowDialog(this);
    }

    // ------------------------ refresh

    private void RefreshView()
    {
        _ws.SyncPrimary();
        this.FindControl<SpriteCanvas>("EditorView")!.SetState(_ws, _opt, _session);
        this.FindControl<ColorStrip>("ColorStripView")!.SetState(_ws, _opt, _session);
        this.FindControl<DliPanel>("DliPanelView")!.SetState(_ws, _opt, _session);
        // Update state for every existing sequence row (refresh selection highlight + thumbs)
        for (int i = 0; i < _sequenceRows.Count && i < _ws.Sequences.Count; i++)
            _sequenceRows[i].SetState(_ws, _opt, _session, i);
        UpdateStatus();
        UpdateUndoRedoState();
        // Keep quick-actions toggles visually in sync with option state.
        var wrapBtn = this.FindControl<ToggleButton>("QaWrapBtn");
        if (wrapBtn != null) wrapBtn.IsChecked = _opt.WrapEditor;
        UpdateWrapIndicator();
        var dliBtn = this.FindControl<ToggleButton>("QaDliBtn");
        if (dliBtn != null) dliBtn.IsChecked = _opt.DliOn;
        UpdateDliIndicator();
        // Reference minimap + toggle.
        this.FindControl<ReferenceMinimap>("ReferenceMinimapView")?.SetState(_ws, _opt);
        var refBtn = this.FindControl<ToggleButton>("ReferenceShowBtn");
        if (refBtn != null) refBtn.IsChecked = _opt.ShowReference;
        UpdateReferenceIndicator();
        UpdateReferenceInfo();
        var scaleXTb = this.FindControl<TinyNumericUpDown>("RefScaleXNud");
        var scaleYTb = this.FindControl<TinyNumericUpDown>("RefScaleYNud");
        if (scaleXTb != null && scaleXTb.Value != _opt.ReferenceScaleX) scaleXTb.Value = Math.Max(1, _opt.ReferenceScaleX);
        if (scaleYTb != null && scaleYTb.Value != _opt.ReferenceScaleY) scaleYTb.Value = Math.Max(1, _opt.ReferenceScaleY);
    }

    /// <summary>Write "WxH px, NN KB" beneath the reference minimap, or empty if no image.</summary>
    private void UpdateReferenceInfo()
    {
        var tb = this.FindControl<TextBlock>("ReferenceInfoTb");
        var posTb = this.FindControl<TextBlock>("ReferencePosTb");
        if (tb == null) return;
        if (_ws.ReferenceImage == null || _ws.ReferenceImageWidth <= 0)
        {
            tb.Text = "";
            if (posTb != null) posTb.Text = "";
            return;
        }
        int bytes = _ws.ReferenceImage.Length;
        string size = bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.##} MB"
            : $"{bytes / 1024.0:0.#} KB";
        tb.Text = $"{_ws.ReferenceImageWidth} × {_ws.ReferenceImageHeight} px   {size}";

        if (posTb != null)
        {
            var size2 = FrameRenderer.GetGridSize(_ws, _opt);
            int vpW = size2.Cols * Math.Max(1, _opt.ReferenceScaleX);
            int vpH = size2.Rows * Math.Max(1, _opt.ReferenceScaleY);
            posTb.Text = $"@ {_opt.ReferenceSourceX},{_opt.ReferenceSourceY}  ({vpW}×{vpH})";
        }
    }

    private void UpdateReferenceIndicator()
    {
        var ind = this.FindControl<TextBlock>("ReferenceShowIndicator");
        if (ind == null) return;
        ind.Text = _opt.ShowReference ? "✓" : "✗";
    }

    /// <summary>Enable/disable Undo and Redo buttons (top bar + quick-actions grid) to match stack state.</summary>
    private void UpdateUndoRedoState()
    {
        bool canUndo = _undo.CanUndo;
        bool canRedo = _undo.CanRedo;
        var undoBtn = this.FindControl<Button>("UndoBtn");
        var redoBtn = this.FindControl<Button>("RedoBtn");
        var qaUndo  = this.FindControl<Button>("QaUndoBtn");
        var qaRedo  = this.FindControl<Button>("QaRedoBtn");
        if (undoBtn != null) undoBtn.IsEnabled = canUndo;
        if (redoBtn != null) redoBtn.IsEnabled = canRedo;
        if (qaUndo  != null) qaUndo.IsEnabled = canUndo;
        if (qaRedo  != null) qaRedo.IsEnabled = canRedo;

        // Also sync menu items if they're hooked up.
        var miUndo = this.FindControl<MenuItem>("MiUndo");
        var miRedo = this.FindControl<MenuItem>("MiRedo");
        if (miUndo != null) miUndo.IsEnabled = canUndo;
        if (miRedo != null) miRedo.IsEnabled = canRedo;
    }

    private void UpdateStatus()
    {
        var seqName = _ws.Sequences.Count > 0 ? _ws.ActiveSequence.Name : "Primary";
        this.FindControl<TextBlock>("StatusTb")!.Text =
            $"Frame {_ws.SelectedFrame + 1}/{_ws.Frames.Count}    " +
            $"Seq: {seqName}    " +
            $"Mode {_opt.MergeMode}  H={_opt.SpriteHeight}  " +
            $"Zoom={Defines.ZoomCellSize[_opt.CellSize]}  " +
            $"Palette={_opt.Palette}  " +
            (_opt.DliOn ? "DLI " : "") +
            (_opt.Freestyle ? "Freestyle " : "") +
            (_playing ? "[playing]" : "");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Storage.SaveOptions(_opt);
        Storage.SaveWorkspace(_ws);
        base.OnClosing(e);
    }
}
