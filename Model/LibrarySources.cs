using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SpriteEditXL.IO;

namespace SpriteEditXL.Model;

/// <summary>A paginated chunk of library items plus total count + metadata.</summary>
public sealed class LibraryPage
{
    public List<LibraryItem> Items { get; init; } = new();
    /// <summary>Total records on the server (or in the cache) across all pages.</summary>
    public int TotalCount { get; init; }
    /// <summary>Which page we got. Zero-based.</summary>
    public int PageIndex { get; init; }
    /// <summary>Items requested per page.</summary>
    public int PageSize { get; init; }
    /// <summary>When these items were originally fetched from the network. null = never cached.</summary>
    public DateTime? CachedAt { get; init; }
}

/// <summary>Fields the library can sort on. Mirrors the original SprEd UI.</summary>
public enum LibrarySortKey { Date, Name, Frames }

/// <summary>A pluggable source of <see cref="LibraryItem"/>s.</summary>
public interface ILibrarySource
{
    LibrarySourceKind Kind { get; }
    string DisplayName { get; }

    /// <summary>
    /// Fetch one page. If <paramref name="forceRefresh"/> is false and a fresh cache exists,
    /// returns the cached page without hitting the network.
    /// </summary>
    Task<LibraryPage> FetchPageAsync(int pageIndex, int pageSize, string query = "",
                                     bool forceRefresh = false,
                                     LibrarySortKey sortKey = LibrarySortKey.Date,
                                     bool descending = true);

    /// <summary>Load the chosen item into the workspace format. Throws on failure.</summary>
    Task<(Workspace ws, Options opt)> LoadAsync(LibraryItem item);
}

/// <summary>Reads .sprj files from %APPDATA%/SpriteEditXL/library (or a user path).</summary>
public sealed class LocalLibrarySource : ILibrarySource
{
    public LibrarySourceKind Kind => LibrarySourceKind.Local;
    public string DisplayName => "Local library";
    public string Folder { get; }

    public LocalLibrarySource(string? folder = null)
    {
        Folder = folder ?? DefaultFolder();
        Directory.CreateDirectory(Folder);
    }

    public static string DefaultFolder()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpriteEditXL", "library");
        return root;
    }

    public Task<LibraryPage> FetchPageAsync(int pageIndex, int pageSize, string query = "", bool forceRefresh = false,
                                            LibrarySortKey sortKey = LibrarySortKey.Date, bool descending = true)
    {
        var items = new List<LibraryItem>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + SprJsonFile.DefaultExtension))
        {
            try
            {
                var (ws, _) = SprJsonFile.Load(file);
                string n = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(query) && !n.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new LibraryItem
                {
                    Source = LibrarySourceKind.Local,
                    Name = n,
                    FrameCount = ws.Frames.Count,
                    UploadDate = File.GetLastWriteTime(file),
                    LocalPath = file
                });
            }
            catch { /* skip malformed */ }
        }
        IOrderedEnumerable<LibraryItem> ordered = sortKey switch
        {
            LibrarySortKey.Name   => descending ? items.OrderByDescending(i => i.Name) : items.OrderBy(i => i.Name),
            LibrarySortKey.Frames => descending ? items.OrderByDescending(i => i.FrameCount) : items.OrderBy(i => i.FrameCount),
            _                     => descending ? items.OrderByDescending(i => i.UploadDate) : items.OrderBy(i => i.UploadDate),
        };
        var sorted = ordered.ToList();
        int skip = pageIndex * pageSize;
        var page = sorted.Skip(skip).Take(pageSize).ToList();
        return Task.FromResult(new LibraryPage
        {
            Items = page,
            TotalCount = sorted.Count,
            PageIndex = pageIndex,
            PageSize = pageSize
            // CachedAt stays null — local source is always "live".
        });
    }

    public Task<(Workspace, Options)> LoadAsync(LibraryItem item)
    {
        if (string.IsNullOrEmpty(item.LocalPath))
            throw new InvalidOperationException("library item has no local path");
        var (ws, opt) = SprJsonFile.Load(item.LocalPath);
        return Task.FromResult((ws, opt));
    }

    /// <summary>Write a workspace as a new sprite in this library. Returns the file path.</summary>
    public string Save(string name, Workspace ws, Options opt)
    {
        name = SanitizeFileName(name);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name required");
        string path = Path.Combine(Folder, name + SprJsonFile.DefaultExtension);
        SprJsonFile.Save(path, ws, opt);
        return path;
    }

    public void Delete(LibraryItem item)
    {
        if (!string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
            File.Delete(item.LocalPath);
    }

    private static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }
}

/// <summary>
/// Read-only HTTP client for the original SprEd public library at restdb.io.
/// Uses the same public API key the JS app uses.
/// </summary>
public sealed class SprEdRemoteLibrarySource : ILibrarySource
{
    public LibrarySourceKind Kind => LibrarySourceKind.SprEdRemote;
    public string DisplayName => "SprEd online (read-only)";

    private const string BaseUrl = "https://spred-c23d.restdb.io/rest/sprites";
    // Same public key as defines.js (restDBKey). Hard-coded and public upstream.
    private const string ApiKey = "6225278734fd621565858bd2";
    private const string SourceId = "spred-remote";

    private static readonly HttpClient Http = new();

    public async Task<LibraryPage> FetchPageAsync(int pageIndex, int pageSize, string query = "", bool forceRefresh = false,
                                                   LibrarySortKey sortKey = LibrarySortKey.Date, bool descending = true)
    {
        if (pageSize < 1) pageSize = 10;
        string sort = sortKey switch
        {
            LibrarySortKey.Name   => "spriteName",
            LibrarySortKey.Frames => "frames",
            _                     => "uploadDate"
        };
        string dir = descending ? "-1" : "1";
        string cacheKey = $"{query}|{sort}|{dir}";

        // Try cache first (unless forced).
        if (!forceRefresh)
        {
            var cached = LibraryCache.TryRead(SourceId, pageIndex, pageSize, cacheKey);
            if (cached != null) return cached;
        }

        int skip = pageIndex * pageSize;
        var url = $"{BaseUrl}?totals=true&filter={Uri.EscapeDataString(query)}&sort={sort}&dir={dir}&skip={skip}&max={pageSize}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("x-apikey", ApiKey);
        req.Headers.Add("cache-control", "no-cache");
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();

        var parsed = JsonSerializer.Deserialize<RemoteListResponse>(json, JsonOpts)
                     ?? throw new InvalidDataException("empty response");
        var items = new List<LibraryItem>();
        foreach (var r in parsed.Data ?? Array.Empty<RemoteRecord>())
        {
            items.Add(new LibraryItem
            {
                Source = LibrarySourceKind.SprEdRemote,
                Name = r.SpriteName ?? "(unnamed)",
                Author = r.AuthorName ?? "",
                Description = r.Description ?? "",
                FrameCount = r.Frames,
                UploadDate = DateTime.TryParse(r.UploadDate, out var d) ? d : DateTime.MinValue,
                RemoteId = r.Id,
                PreviewDataUrl = r.SpritePreview
            });
        }

        int total = parsed.Totals?.Total ?? items.Count;
        var page = new LibraryPage
        {
            Items = items,
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
            CachedAt = DateTime.UtcNow
        };
        LibraryCache.Write(SourceId, page, cacheKey);
        return page;
    }

    /// <summary>Clear this source's cache.</summary>
    public static void ClearCache() => LibraryCache.Clear(SourceId);

    public async Task<(Workspace, Options)> LoadAsync(LibraryItem item)
    {
        if (string.IsNullOrEmpty(item.RemoteId))
            throw new InvalidOperationException("library item has no remote id");

        var url = $"{BaseUrl}/{item.RemoteId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-apikey", ApiKey);
        req.Headers.Add("cache-control", "no-cache");
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var rec = JsonSerializer.Deserialize<RemoteRecord>(json, JsonOpts)
                  ?? throw new InvalidDataException("empty record");

        var ws = new Workspace();
        var opt = new Options();

        // Apply spriteOptions fields (same names as defaultOptions in JS).
        if (rec.SpriteOptions is { } so)
        {
            if (so.TryGetValue("lineResolution", out var v)) opt.LineResolution = GetInt(v, opt.LineResolution);
            if (so.TryGetValue("spriteHeight",   out v))    opt.SpriteHeight   = GetInt(v, opt.SpriteHeight);
            if (so.TryGetValue("spriteGap01",    out v))    opt.SpriteGap01    = GetInt(v, opt.SpriteGap01);
            if (so.TryGetValue("spriteGap23",    out v))    opt.SpriteGap23    = GetInt(v, opt.SpriteGap23);
            if (so.TryGetValue("pairGap",        out v))    opt.PairGap        = GetInt(v, opt.PairGap);
            if (so.TryGetValue("animationSpeed", out v))    opt.AnimationSpeed = GetInt(v, opt.AnimationSpeed);
            if (so.TryGetValue("palette",        out v))    opt.Palette        = GetString(v, "PAL") == "NTSC" ? PaletteKind.NTSC : PaletteKind.PAL;
            if (so.TryGetValue("mergeMode",      out v))    opt.MergeMode      = (SpriteMode)GetInt(v, (int)opt.MergeMode);
            if (so.TryGetValue("dliOn",          out v))    opt.DliOn          = GetBool(v);
            if (so.TryGetValue("frameDelayMode", out v))    opt.FrameDelayMode = GetBool(v);
            if (so.TryGetValue("commonPalette",  out v))    opt.CommonPalette  = GetBool(v);
            if (so.TryGetValue("freestyle",      out v))    opt.Freestyle      = GetBool(v);
            if (so.TryGetValue("p0offset",       out v))    opt.P0Offset       = GetInt(v, opt.P0Offset);
            if (so.TryGetValue("p1offset",       out v))    opt.P1Offset       = GetInt(v, opt.P1Offset);
            if (so.TryGetValue("p2offset",       out v))    opt.P2Offset       = GetInt(v, opt.P2Offset);
            if (so.TryGetValue("p3offset",       out v))    opt.P3Offset       = GetInt(v, opt.P3Offset);
            if (so.TryGetValue("m0offset",       out v))    opt.M0Offset       = GetInt(v, opt.M0Offset);
            if (so.TryGetValue("m1offset",       out v))    opt.M1Offset       = GetInt(v, opt.M1Offset);
            if (so.TryGetValue("m2offset",       out v))    opt.M2Offset       = GetInt(v, opt.M2Offset);
            if (so.TryGetValue("m3offset",       out v))    opt.M3Offset       = GetInt(v, opt.M3Offset);
        }

        // spriteData: { backgroundColor, frames: [...] }.
        if (rec.SpriteData is { } sd)
        {
            if (sd.TryGetValue("backgroundColor", out var bg))
                ws.BackgroundColor = (byte)GetInt(bg, 0);

            if (sd.TryGetValue("frames", out var framesEl) && framesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var fe in framesEl.EnumerateArray())
                {
                    var frame = Frame.CreateEmpty(opt.SpriteHeight, ws.BackgroundColor);
                    // JS `frame` is: { player: [[],[],[],[]], missile: [...], colors: [c0,c1,c2,c3], delayTime, dli: {...} }
                    if (fe.TryGetProperty("colors", out var colorsEl) && colorsEl.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var c in colorsEl.EnumerateArray())
                            if (idx < 4) frame.Colors[idx++] = (byte)GetInt(c, 0);
                    }
                    if (fe.TryGetProperty("player", out var playerEl) && playerEl.ValueKind == JsonValueKind.Array)
                        CopyRows(playerEl, frame.Player, opt.SpriteHeight);
                    if (fe.TryGetProperty("missile", out var missileEl) && missileEl.ValueKind == JsonValueKind.Array)
                        CopyRows(missileEl, frame.Missile, opt.SpriteHeight);
                    if (fe.TryGetProperty("delayTime", out var dte) && dte.TryGetInt32(out int dt))
                        frame.DelayTime = dt;
                    if (fe.TryGetProperty("dli", out var dliEl) && dliEl.ValueKind == JsonValueKind.Object)
                    {
                        void CopyArr(string key, byte[] dst)
                        {
                            if (!dliEl.TryGetProperty(key, out var arr)) return;
                            int i = 0;
                            foreach (var b in arr.EnumerateArray())
                                if (i < dst.Length) dst[i++] = (byte)GetInt(b, 0);
                        }
                        CopyArr("back", frame.Dli.Back);
                        CopyArr("c0",   frame.Dli.C0);
                        CopyArr("c1",   frame.Dli.C1);
                        CopyArr("c2",   frame.Dli.C2);
                        CopyArr("c3",   frame.Dli.C3);
                    }
                    ws.Frames.Add(frame);
                }
            }
        }

        ws.SyncPrimary();
        return (ws, opt);
    }

    private static void CopyRows(JsonElement arr, byte[][] dst, int height)
    {
        int p = 0;
        foreach (var playerArr in arr.EnumerateArray())
        {
            if (p >= 4) break;
            if (playerArr.ValueKind == JsonValueKind.Array)
            {
                int r = 0;
                foreach (var b in playerArr.EnumerateArray())
                {
                    if (r >= height) break;
                    dst[p][r++] = (byte)GetInt(b, 0);
                }
            }
            p++;
        }
    }

    private static int GetInt(JsonElement e, int fallback)
    {
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetInt32(out int i) ? i : (int)e.GetDouble(),
            JsonValueKind.String => int.TryParse(e.GetString(), out int si) ? si : fallback,
            JsonValueKind.True   => 1,
            JsonValueKind.False  => 0,
            _                    => fallback
        };
    }

    private static bool GetBool(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True    => true,
        JsonValueKind.False   => false,
        JsonValueKind.Number  => e.GetInt32() != 0,
        JsonValueKind.String  => e.GetString() == "true" || e.GetString() == "1",
        _                     => false
    };

    private static string GetString(JsonElement e, string fallback) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? fallback,
        _ => fallback
    };

    // ---- restdb JSON shapes ----

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RemoteListResponse
    {
        [JsonPropertyName("data")]   public RemoteRecord[]? Data { get; set; }
        [JsonPropertyName("totals")] public TotalsBlock? Totals { get; set; }
    }

    private sealed class TotalsBlock
    {
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    private sealed class RemoteRecord
    {
        [JsonPropertyName("_id")]          public string? Id { get; set; }
        [JsonPropertyName("spriteName")]   public string? SpriteName { get; set; }
        [JsonPropertyName("authorName")]   public string? AuthorName { get; set; }
        [JsonPropertyName("description")]  public string? Description { get; set; }
        [JsonPropertyName("uploadDate")]   public string? UploadDate { get; set; }
        [JsonPropertyName("frames")]       public int Frames { get; set; }
        [JsonPropertyName("spritePreview")]public string? SpritePreview { get; set; }
        [JsonPropertyName("spriteData")]   public Dictionary<string, JsonElement>? SpriteData { get; set; }
        [JsonPropertyName("spriteOptions")]public Dictionary<string, JsonElement>? SpriteOptions { get; set; }
    }
}

/// <summary>
/// The SpriteEditXL community library hosted on restdb.io. Read AND write (uploads allowed).
/// spriteData / spriteOptions are stored as JSON strings in restdb's Textbox fields.
/// </summary>
public sealed class SpriteEditXLRemoteLibrarySource : ILibrarySource
{
    public LibrarySourceKind Kind => LibrarySourceKind.SprEdRemote; // same kind reuse; UI distinguishes by tab
    public string DisplayName => "SpriteEditXL online";

    private const string BaseUrl = "https://spriteeditxl-e7eb.restdb.io/rest/sprites";
    private const string ApiKey  = "6b055e4ccb1c243b65626001";
    private const string SourceId = "spriteeditxl-remote";

    private static readonly HttpClient Http = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<LibraryPage> FetchPageAsync(int pageIndex, int pageSize, string query = "", bool forceRefresh = false,
                                                   LibrarySortKey sortKey = LibrarySortKey.Date, bool descending = true)
    {
        if (pageSize < 1) pageSize = 10;
        string sort = sortKey switch
        {
            LibrarySortKey.Name   => "spriteName",
            LibrarySortKey.Frames => "frames",
            _                     => "uploadDate"
        };
        string dir = descending ? "-1" : "1";
        string cacheKey = $"{query}|{sort}|{dir}";

        if (!forceRefresh)
        {
            var cached = LibraryCache.TryRead(SourceId, pageIndex, pageSize, cacheKey);
            if (cached != null) return cached;
        }

        string filter = "";
        if (!string.IsNullOrEmpty(query))
        {
            var f = new Dictionary<string, object> { ["spriteName"] = new { regex = query, options = "i" } };
            filter = Uri.EscapeDataString(JsonSerializer.Serialize(f));
        }
        int skip = pageIndex * pageSize;
        string url = string.IsNullOrEmpty(filter)
            ? $"{BaseUrl}?totals=true&sort={sort}&dir={dir}&skip={skip}&max={pageSize}"
            : $"{BaseUrl}?q={filter}&totals=true&sort={sort}&dir={dir}&skip={skip}&max={pageSize}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-apikey", ApiKey);
        req.Headers.Add("cache-control", "no-cache");
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();

        List<XLRecord> records;
        int totalFromServer = -1;
        try
        {
            records = JsonSerializer.Deserialize<List<XLRecord>>(json, JsonOpts) ?? new();
        }
        catch
        {
            var wrapped = JsonSerializer.Deserialize<XLListResponse>(json, JsonOpts);
            records = wrapped?.Data?.ToList() ?? new();
            if (wrapped?.Totals != null) totalFromServer = wrapped.Totals.Total;
        }

        var items = new List<LibraryItem>();
        foreach (var r in records)
        {
            items.Add(new LibraryItem
            {
                Source = LibrarySourceKind.SprEdRemote,
                Name = r.SpriteName ?? "(unnamed)",
                Author = r.AuthorName ?? "",
                Description = r.Description ?? "",
                FrameCount = r.Frames,
                UploadDate = DateTime.TryParse(r.UploadDate, out var d) ? d : DateTime.MinValue,
                RemoteId = r.Id,
                PreviewDataUrl = r.SpritePreview
            });
        }

        int total = totalFromServer >= 0 ? totalFromServer : items.Count;
        var page = new LibraryPage
        {
            Items = items,
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
            CachedAt = DateTime.UtcNow
        };
        LibraryCache.Write(SourceId, page, cacheKey);
        return page;
    }

    /// <summary>Clear this source's cache (so the next FetchPageAsync re-hits the network).</summary>
    public static void ClearCache() => LibraryCache.Clear(SourceId);

    public async Task<(Workspace, Options)> LoadAsync(LibraryItem item)
    {
        if (string.IsNullOrEmpty(item.RemoteId))
            throw new InvalidOperationException("library item has no remote id");

        var url = $"{BaseUrl}/{item.RemoteId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-apikey", ApiKey);
        req.Headers.Add("cache-control", "no-cache");
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();

        var rec = JsonSerializer.Deserialize<XLRecord>(json, JsonOpts)
                  ?? throw new InvalidDataException("empty record");

        // spriteData / spriteOptions are stored as JSON strings in the Textbox fields;
        // deserialize them back into our native types.
        if (string.IsNullOrEmpty(rec.SpriteData))
            throw new InvalidDataException("remote record missing spriteData");

        var bundle = JsonSerializer.Deserialize<SprJsonFile.Bundle>(
            "{\"Version\":1,\"Workspace\":" + rec.SpriteData + ",\"SpriteOptions\":" + (rec.SpriteOptions ?? "{}") + "}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("could not deserialize remote sprite");

        var ws = bundle.Workspace ?? new Workspace();
        var opt = new Options();
        bundle.SpriteOptions?.ApplyTo(opt);
        ws.SyncPrimary();
        return (ws, opt);
    }

    /// <summary>Upload a workspace as a new library sprite. Returns the created record id.</summary>
    public async Task<string> UploadAsync(string spriteName, string authorName, string description,
                                          string uploaderId, Workspace ws, Options opt)
    {
        // Build an animated GIF preview and base64-encode as a data URL.
        string previewDataUrl = BuildPreviewDataUrl(ws, opt);

        var record = new Dictionary<string, object?>
        {
            ["spriteName"]    = spriteName,
            ["authorName"]    = authorName,
            ["description"]   = description,
            ["uploaderId"]    = uploaderId,
            ["uploadDate"]    = DateTime.UtcNow.ToString("o"),
            ["frames"]        = ws.Frames.Count,
            ["spritePreview"] = previewDataUrl,
            ["spriteData"]    = JsonSerializer.Serialize(ws),
            ["spriteOptions"] = JsonSerializer.Serialize(SprJsonFile.SpriteOptions.From(opt))
        };
        var body = JsonSerializer.Serialize(record);
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        req.Headers.Add("x-apikey", ApiKey);
        req.Headers.Add("cache-control", "no-cache");
        req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"upload failed ({(int)resp.StatusCode}): {err}");
        }
        var created = await resp.Content.ReadAsStringAsync();
        var rec = JsonSerializer.Deserialize<XLRecord>(created, JsonOpts);
        return rec?.Id ?? "";
    }

    /// <summary>
    /// Build a "data:image/gif;base64,…" URL for a preview thumbnail of the whole animation.
    /// Returns empty string on failure (keeps upload working even if GIF encoding has a hiccup).
    /// </summary>
    private static string BuildPreviewDataUrl(Workspace ws, Options opt)
    {
        try
        {
            using var mem = new MemoryStream();
            GifExport.WriteAnimationToStream(mem, ws, opt, new EditorSession(), scale: 3);
            string b64 = Convert.ToBase64String(mem.ToArray());
            return "data:image/gif;base64," + b64;
        }
        catch { return ""; }
    }

    private sealed class XLListResponse
    {
        [JsonPropertyName("data")]   public XLRecord[]? Data { get; set; }
        [JsonPropertyName("totals")] public XLTotalsBlock? Totals { get; set; }
    }

    private sealed class XLTotalsBlock
    {
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    private sealed class XLRecord
    {
        [JsonPropertyName("_id")]          public string? Id { get; set; }
        [JsonPropertyName("spriteName")]   public string? SpriteName { get; set; }
        [JsonPropertyName("authorName")]   public string? AuthorName { get; set; }
        [JsonPropertyName("description")]  public string? Description { get; set; }
        [JsonPropertyName("uploadDate")]   public string? UploadDate { get; set; }
        [JsonPropertyName("uploaderId")]   public string? UploaderId { get; set; }
        [JsonPropertyName("frames")]       public int Frames { get; set; }
        [JsonPropertyName("spritePreview")]public string? SpritePreview { get; set; }
        [JsonPropertyName("spriteData")]   public string? SpriteData { get; set; }
        [JsonPropertyName("spriteOptions")]public string? SpriteOptions { get; set; }
    }
}
