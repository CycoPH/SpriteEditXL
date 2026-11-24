using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// Library browser with three tabs:
///   • SpriteEditXL — community library (read + write)
///   • SprEd (read) — original bocianu library (read-only)
///   • Local        — .sprj files under %APPDATA%/SpriteEditXL/library/
/// Supports pagination + on-disk cache (24h TTL by default). Returns the chosen
/// (Workspace, Options) tuple or null if the user cancelled.
/// </summary>
public partial class LibraryDialog : Window
{
    private const int DefaultPageSize = 8;   // matches original SprEd LIBRARY_SPR_PER_PAGE

    private readonly LocalLibrarySource _local = new();
    private readonly SprEdRemoteLibrarySource _spred = new();
    private readonly SpriteEditXLRemoteLibrarySource _xl = new();

    private LibraryItem? _localSelected;
    private LibraryItem? _spredSelected;
    private LibraryItem? _xlSelected;

    // Current page index per tab.
    private int _xlPage, _spredPage, _localPage;
    // Total record count the source reported last time we fetched.
    private int _xlTotal, _spredTotal, _localTotal;

    public Workspace? CurrentWorkspace { get; set; }
    public Options? CurrentOptions { get; set; }
    public string? UploaderId { get; set; }
    public string? DefaultAuthor { get; set; }

    public LibraryDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static LibraryDialog Create()
    {
        var dlg = new LibraryDialog();
        dlg.Wire();
        return dlg;
    }

    private void Wire()
    {
        // Sort combos default to Date ↓ (newest first) on every tab.
        foreach (var name in new[] { "XlSortByCb", "SprEdSortByCb", "LocalSortByCb" })
            this.FindControl<ComboBox>(name)!.SelectedIndex = 0;
        foreach (var name in new[] { "XlSortDirCb", "SprEdSortDirCb", "LocalSortDirCb" })
            this.FindControl<ComboBox>(name)!.SelectedIndex = 0;

        // XL tab
        this.FindControl<Button>("XlRefreshBtn")!.Click      += async (_, _) => await ReloadXL(forceRefresh: false);
        this.FindControl<Button>("XlForceRefreshBtn")!.Click += async (_, _) => await ReloadXL(forceRefresh: true);
        this.FindControl<Button>("XlPrevBtn")!.Click         += async (_, _) => { if (_xlPage > 0) { _xlPage--; await ReloadXL(false); } };
        this.FindControl<Button>("XlNextBtn")!.Click         += async (_, _) => { if ((_xlPage + 1) * DefaultPageSize < _xlTotal) { _xlPage++; await ReloadXL(false); } };
        this.FindControl<TextBox>("XlSearchBox")!.KeyDown    += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _xlPage = 0; await ReloadXL(false); } };
        this.FindControl<ComboBox>("XlSortByCb")!.SelectionChanged  += async (_, _) => { _xlPage = 0; await ReloadXL(false); };
        this.FindControl<ComboBox>("XlSortDirCb")!.SelectionChanged += async (_, _) => { _xlPage = 0; await ReloadXL(false); };

        // SprEd tab
        this.FindControl<Button>("SprEdRefreshBtn")!.Click      += async (_, _) => await ReloadSprEd(forceRefresh: false);
        this.FindControl<Button>("SprEdForceRefreshBtn")!.Click += async (_, _) => await ReloadSprEd(forceRefresh: true);
        this.FindControl<Button>("SprEdPrevBtn")!.Click         += async (_, _) => { if (_spredPage > 0) { _spredPage--; await ReloadSprEd(false); } };
        this.FindControl<Button>("SprEdNextBtn")!.Click         += async (_, _) => { if ((_spredPage + 1) * DefaultPageSize < _spredTotal) { _spredPage++; await ReloadSprEd(false); } };
        this.FindControl<TextBox>("SprEdSearchBox")!.KeyDown    += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _spredPage = 0; await ReloadSprEd(false); } };
        this.FindControl<ComboBox>("SprEdSortByCb")!.SelectionChanged  += async (_, _) => { _spredPage = 0; await ReloadSprEd(false); };
        this.FindControl<ComboBox>("SprEdSortDirCb")!.SelectionChanged += async (_, _) => { _spredPage = 0; await ReloadSprEd(false); };

        // Local tab
        this.FindControl<Button>("LocalRefreshBtn")!.Click   += async (_, _) => await ReloadLocal();
        this.FindControl<Button>("LocalFolderBtn")!.Click    += (_, _) => OpenLocalFolder();
        this.FindControl<Button>("LocalDeleteBtn")!.Click    += async (_, _) => await DoDeleteLocal();
        this.FindControl<Button>("LocalPrevBtn")!.Click      += async (_, _) => { if (_localPage > 0) { _localPage--; await ReloadLocal(); } };
        this.FindControl<Button>("LocalNextBtn")!.Click      += async (_, _) => { if ((_localPage + 1) * DefaultPageSize < _localTotal) { _localPage++; await ReloadLocal(); } };
        this.FindControl<TextBox>("LocalSearchBox")!.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _localPage = 0; await ReloadLocal(); } };
        this.FindControl<ComboBox>("LocalSortByCb")!.SelectionChanged  += async (_, _) => { _localPage = 0; await ReloadLocal(); };
        this.FindControl<ComboBox>("LocalSortDirCb")!.SelectionChanged += async (_, _) => { _localPage = 0; await ReloadLocal(); };

        var localList = this.FindControl<ListBox>("LocalList")!;
        localList.SelectionChanged += (_, _) => _localSelected = localList.SelectedItem as LibraryItem;
        localList.DoubleTapped     += (_, _) => DoLoad();

        var spredList = this.FindControl<ListBox>("SprEdList")!;
        spredList.SelectionChanged += (_, _) => _spredSelected = spredList.SelectedItem as LibraryItem;
        spredList.DoubleTapped     += (_, _) => DoLoad();

        var xlList = this.FindControl<ListBox>("XlList")!;
        xlList.SelectionChanged += (_, _) => _xlSelected = xlList.SelectedItem as LibraryItem;
        xlList.DoubleTapped     += (_, _) => DoLoad();

        this.FindControl<Button>("LoadBtn")!.Click   += (_, _) => DoLoad();
        this.FindControl<Button>("CloseBtn")!.Click  += (_, _) => Close(((Workspace, Options)?)null);
        this.FindControl<Button>("UploadBtn")!.Click += async (_, _) => await DoUpload();

        this.FindControl<TextBox>("UpAuthorTb")!.Text = DefaultAuthor ?? "";

        Opened += async (_, _) =>
        {
            await ReloadXL(false);
            await ReloadSprEd(false);
            await ReloadLocal();
        };
    }

    private async void DoLoad()
    {
        var selected = GetSelectedForActiveTab();
        if (selected == null) return;
        try
        {
            ILibrarySource src = selected.Source switch
            {
                _ when selected.LocalPath != null => (ILibrarySource)_local,
                _ when IsXLTabActive()            => _xl,
                _                                 => _spred,
            };
            var (ws, opt) = await src.LoadAsync(selected);
            Close(((Workspace, Options)?)(ws, opt));
        }
        catch (Exception ex)
        {
            await ShowError($"Load failed: {ex.Message}");
        }
    }

    private LibraryItem? GetSelectedForActiveTab()
    {
        var tabs = this.FindControl<TabControl>("Tabs")!;
        return tabs.SelectedIndex switch
        {
            0 => _xlSelected,
            1 => _spredSelected,
            2 => _localSelected,
            _ => null
        };
    }

    private bool IsXLTabActive() => this.FindControl<TabControl>("Tabs")!.SelectedIndex == 0;

    private (LibrarySortKey key, bool desc) GetSort(string byName, string dirName)
    {
        var by = this.FindControl<ComboBox>(byName)!.SelectedIndex;
        var dir = this.FindControl<ComboBox>(dirName)!.SelectedIndex;
        return (
            by switch { 1 => LibrarySortKey.Name, 2 => LibrarySortKey.Frames, _ => LibrarySortKey.Date },
            dir != 1 // index 0 = ↓ (desc), index 1 = ↑ (asc)
        );
    }

    private async Task ReloadLocal()
    {
        var q = this.FindControl<TextBox>("LocalSearchBox")!.Text ?? "";
        var (sk, desc) = GetSort("LocalSortByCb", "LocalSortDirCb");
        var page = await _local.FetchPageAsync(_localPage, DefaultPageSize, q, false, sk, desc);
        _localTotal = page.TotalCount;
        if (_localPage > 0 && page.Items.Count == 0 && _localPage * DefaultPageSize >= _localTotal)
        {
            // Page overshot after a search narrowed results; reset.
            _localPage = 0;
            page = await _local.FetchPageAsync(_localPage, DefaultPageSize, q, false, sk, desc);
            _localTotal = page.TotalCount;
        }
        PopulateList("LocalList", page.Items);
        UpdatePager("LocalPageTb", "LocalPrevBtn", "LocalNextBtn", "LocalStatusTb", page);
    }

    private async Task ReloadSprEd(bool forceRefresh)
    {
        var q = this.FindControl<TextBox>("SprEdSearchBox")!.Text ?? "";
        var (sk, desc) = GetSort("SprEdSortByCb", "SprEdSortDirCb");
        try
        {
            var page = await _spred.FetchPageAsync(_spredPage, DefaultPageSize, q, forceRefresh, sk, desc);
            _spredTotal = page.TotalCount;
            PopulateList("SprEdList", page.Items);
            UpdatePager("SprEdPageTb", "SprEdPrevBtn", "SprEdNextBtn", "SprEdStatusTb", page);
        }
        catch (Exception ex) { await ShowError($"SprEd online fetch failed: {ex.Message}"); }
    }

    private async Task ReloadXL(bool forceRefresh)
    {
        var q = this.FindControl<TextBox>("XlSearchBox")!.Text ?? "";
        var (sk, desc) = GetSort("XlSortByCb", "XlSortDirCb");
        try
        {
            var page = await _xl.FetchPageAsync(_xlPage, DefaultPageSize, q, forceRefresh, sk, desc);
            _xlTotal = page.TotalCount;
            PopulateList("XlList", page.Items);
            UpdatePager("XlPageTb", "XlPrevBtn", "XlNextBtn", "XlStatusTb", page);
        }
        catch (Exception ex) { await ShowError($"SpriteEditXL online fetch failed: {ex.Message}"); }
    }

    private async Task DoDeleteLocal()
    {
        if (_localSelected == null) { await ShowError("Select a local sprite to delete."); return; }
        var name = _localSelected.Name ?? "(unnamed)";
        var w = new Window
        {
            Title = "Delete sprite",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var tcs = new TaskCompletionSource<bool>();
        var yes = new Button { Content = "Delete", Width = 90, IsDefault = true,
                               HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var no  = new Button { Content = "Cancel", Width = 90, IsCancel = true,
                               HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        yes.Click += (_, _) => { tcs.TrySetResult(true); w.Close(); };
        no.Click  += (_, _) => { tcs.TrySetResult(false); w.Close(); };
        w.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"Delete \"{name}\" from the local library? This cannot be undone.",
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                 Children = { yes, no } }
            }
        };
        _ = w.ShowDialog(this).ContinueWith(_ => tcs.TrySetResult(false));
        if (!await tcs.Task) return;
        try
        {
            _local.Delete(_localSelected);
            _localSelected = null;
            await ReloadLocal();
        }
        catch (Exception ex) { await ShowError($"Delete failed: {ex.Message}"); }
    }

    private void UpdatePager(string pageLblName, string prevName, string nextName, string statusName, LibraryPage page)
    {
        int totalPages = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)page.PageSize));
        var pageLbl = this.FindControl<TextBlock>(pageLblName);
        var prev = this.FindControl<Button>(prevName);
        var next = this.FindControl<Button>(nextName);
        var status = this.FindControl<TextBlock>(statusName);
        if (pageLbl != null) pageLbl.Text = $"Page {page.PageIndex + 1} of {totalPages}   ({page.TotalCount} sprites)";
        if (prev != null) prev.IsEnabled = page.PageIndex > 0;
        if (next != null) next.IsEnabled = (page.PageIndex + 1) * page.PageSize < page.TotalCount;
        if (status != null)
        {
            if (page.CachedAt is { } when1)
            {
                var age = DateTime.UtcNow - when1;
                string ago = age.TotalMinutes < 1 ? "just now"
                           : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min ago"
                           : age.TotalHours   < 24 ? $"{(int)age.TotalHours} h ago"
                           : $"{(int)age.TotalDays} d ago";
                status.Text = $"cached · fetched {ago}";
            }
            else
            {
                status.Text = "";
            }
        }
    }

    private void PopulateList(string listName, List<LibraryItem> items)
    {
        var lb = this.FindControl<ListBox>(listName)!;
        lb.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LibraryItem>((item, _) =>
        {
            if (item == null) return new TextBlock { Text = "" };
            Control preview = BuildPreviewControl(item);

            var nameTb = new TextBlock
            {
                Text = item.Name ?? "(unnamed)",
                FontWeight = Avalonia.Media.FontWeight.Bold,
                FontSize = 13
            };
            var meta = new TextBlock
            {
                Text = $"{item.FrameCount} frame{(item.FrameCount == 1 ? "" : "s")}    " +
                       $"{(string.IsNullOrEmpty(item.Author) ? "(no author)" : item.Author)}    " +
                       $"{(item.UploadDate == DateTime.MinValue ? "" : item.UploadDate.ToString("yyyy-MM-dd"))}",
                Foreground = Avalonia.Media.Brushes.Gray,
                FontSize = 11
            };
            var text = new StackPanel
            {
                Spacing = 2,
                Margin = new Avalonia.Thickness(10, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children = { nameTb, meta }
            };
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                text.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    FontSize = 11,
                    FontStyle = Avalonia.Media.FontStyle.Italic,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxHeight = 40
                });
            }
            return new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Margin = new Avalonia.Thickness(4),
                Children = { preview, text }
            };
        }, supportsRecycling: false);
        lb.ItemsSource = items;
    }

    private static Control BuildPreviewControl(LibraryItem item)
    {
        const double width = 80;
        const double height = 60;

        string errorReason = "";
        try
        {
            if (!string.IsNullOrEmpty(item.PreviewDataUrl))
            {
                var url = item.PreviewDataUrl!;
                int comma = url.IndexOf(',');
                if (comma > 0)
                {
                    var b64 = url.Substring(comma + 1);
                    var bytes = Convert.FromBase64String(b64);
                    using var ms = new System.IO.MemoryStream(bytes);
                    var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                    return new Image
                    {
                        Source = bmp,
                        Width = width, Height = height,
                        Stretch = Avalonia.Media.Stretch.Uniform,
                        StretchDirection = Avalonia.Media.StretchDirection.DownOnly
                    };
                }
                errorReason = "no base64 delimiter";
            }
        }
        catch (Exception ex) { errorReason = ex.GetType().Name; }

        return new Border
        {
            Width = width, Height = height,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x30, 0x20, 0x18)),
            BorderBrush = Avalonia.Media.Brushes.DimGray,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(errorReason)
                    ? "no preview"
                    : $"preview\nfailed\n{errorReason}",
                Foreground = Avalonia.Media.Brushes.Gray,
                FontSize = 9,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
    }

    private void OpenLocalFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _local.Folder,
                UseShellExecute = true
            });
        }
        catch { /* best effort */ }
    }

    private async Task DoUpload()
    {
        var nameTb = this.FindControl<TextBox>("UpNameTb")!;
        var authorTb = this.FindControl<TextBox>("UpAuthorTb")!;
        var descTb = this.FindControl<TextBox>("UpDescTb")!;
        string name = (nameTb.Text ?? "").Trim();
        string author = (authorTb.Text ?? "").Trim();
        string desc = (descTb.Text ?? "").Trim();

        if (string.IsNullOrEmpty(name))   { await ShowError("Sprite name is required."); return; }
        if (string.IsNullOrEmpty(author)) { await ShowError("Author is required."); return; }
        if (CurrentWorkspace == null || CurrentOptions == null)
        { await ShowError("No active sprite to upload."); return; }

        try
        {
            string id = await _xl.UploadAsync(name, author, desc, UploaderId ?? Guid.NewGuid().ToString(),
                                              CurrentWorkspace, CurrentOptions);
            await ShowInfo($"Uploaded (id {id}).");
            // Our upload invalidates page 0 of the cache; force a refresh to pull it back.
            SpriteEditXLRemoteLibrarySource.ClearCache();
            _xlPage = 0;
            await ReloadXL(forceRefresh: true);
        }
        catch (Exception ex) { await ShowError($"Upload failed: {ex.Message}"); }
    }

    private async Task ShowError(string message) => await ShowBlurb("Library — error", message);
    private async Task ShowInfo(string message)  => await ShowBlurb("Library", message);

    private async Task ShowBlurb(string title, string message)
    {
        var w = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var ok = new Button
        {
            Content = "OK", Width = 90, IsDefault = true, IsCancel = true,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        ok.Click += (_, _) => w.Close();
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
        await w.ShowDialog(this);
    }
}
