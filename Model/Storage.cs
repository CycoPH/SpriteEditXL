using System;
using System.IO;
using System.Text.Json;

namespace SpriteEditXL.Model;

/// <summary>
/// Persists options and workspace to %APPDATA%\SpriteEditXL (replaces localStorage).
/// </summary>
public static class Storage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        IncludeFields = false
    };

    private static string AppDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpriteEditXL");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string OptionsPath   => Path.Combine(AppDir, "options.json");
    private static string WorkspacePath => Path.Combine(AppDir, "workspace.json");

    public static void SaveOptions(Options opt)
    {
        try { File.WriteAllText(OptionsPath, JsonSerializer.Serialize(opt, JsonOpts)); }
        catch { /* best effort */ }
    }

    public static Options LoadOptions()
    {
        try
        {
            if (!File.Exists(OptionsPath)) return new Options();
            return JsonSerializer.Deserialize<Options>(File.ReadAllText(OptionsPath), JsonOpts) ?? new Options();
        }
        catch { return new Options(); }
    }

    public static void SaveWorkspace(Workspace ws)
    {
        try { File.WriteAllText(WorkspacePath, JsonSerializer.Serialize(ws, JsonOpts)); }
        catch { /* best effort */ }
    }

    public static Workspace LoadWorkspace()
    {
        try
        {
            if (!File.Exists(WorkspacePath)) return new Workspace();
            return JsonSerializer.Deserialize<Workspace>(File.ReadAllText(WorkspacePath), JsonOpts) ?? new Workspace();
        }
        catch { return new Workspace(); }
    }
}
