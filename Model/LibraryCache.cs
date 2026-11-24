using System;
using System.IO;
using System.Text.Json;

namespace SpriteEditXL.Model;

/// <summary>
/// On-disk cache of remote-library pages. Shared between remote sources.
/// Pages live under %APPDATA%/SpriteEditXL/cache/{sourceId}/ as JSON files.
/// Default TTL is 24h; FetchPageAsync can also force-bypass the cache.
/// </summary>
public static class LibraryCache
{
    public static TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    private static string RootDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpriteEditXL", "cache");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string PageFile(string sourceId, int pageIndex, int pageSize, string query)
    {
        var dir = Path.Combine(RootDir(), sourceId);
        Directory.CreateDirectory(dir);
        // Normalize the query so an empty + non-empty query don't collide on disk.
        var qkey = string.IsNullOrEmpty(query) ? "all" : SanitizeKey(query);
        return Path.Combine(dir, $"q-{qkey}_sz-{pageSize}_p-{pageIndex}.json");
    }

    private static string SanitizeKey(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        if (s.Length > 60) s = s.Substring(0, 60);
        return s;
    }

    /// <summary>Try to read a cached page. Returns null if missing, stale, or unreadable.</summary>
    public static LibraryPage? TryRead(string sourceId, int pageIndex, int pageSize, string query, TimeSpan? ttl = null)
    {
        try
        {
            var path = PageFile(sourceId, pageIndex, pageSize, query);
            if (!File.Exists(path)) return null;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age > (ttl ?? DefaultTtl)) return null;
            var json = File.ReadAllText(path);
            var page = JsonSerializer.Deserialize<LibraryPage>(json, JsonOpts);
            return page;
        }
        catch { return null; }
    }

    public static void Write(string sourceId, LibraryPage page, string query)
    {
        try
        {
            var path = PageFile(sourceId, page.PageIndex, page.PageSize, query);
            File.WriteAllText(path, JsonSerializer.Serialize(page, JsonOpts));
        }
        catch { /* best effort */ }
    }

    /// <summary>Delete all cached pages for a source (used by the "force refresh" button).</summary>
    public static void Clear(string sourceId)
    {
        try
        {
            var dir = Path.Combine(RootDir(), sourceId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
