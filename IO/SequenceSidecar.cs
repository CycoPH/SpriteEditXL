using System;
using System.IO;
using System.Text.Json;
using SpriteEditXL.Model;

namespace SpriteEditXL.IO;

/// <summary>
/// Reads/writes <c>&lt;file&gt;.spr.seq.json</c> next to a .spr file.
/// Only non-Primary sequences get persisted (Primary auto-syncs to the frame pool on load).
/// </summary>
public static class SequenceSidecar
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private static string SidecarPath(string sprPath) => sprPath + ".seq.json";

    public static void TrySaveFrom(string sprPath, Workspace ws)
    {
        try
        {
            var custom = ws.Sequences
                .FindAll(s => !s.IsPrimary);
            if (custom.Count == 0)
            {
                // Clean up stale sidecar
                var sc = SidecarPath(sprPath);
                if (File.Exists(sc)) File.Delete(sc);
                return;
            }
            File.WriteAllText(SidecarPath(sprPath), JsonSerializer.Serialize(custom, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    public static void TryLoadInto(string sprPath, Workspace ws)
    {
        try
        {
            var sc = SidecarPath(sprPath);
            if (!File.Exists(sc)) return;
            var custom = JsonSerializer.Deserialize<AnimationSequence[]>(File.ReadAllText(sc), JsonOpts);
            if (custom == null) return;
            // Replace any existing non-Primary sequences with the sidecar list.
            ws.Sequences.RemoveAll(s => !s.IsPrimary);
            foreach (var s in custom) ws.Sequences.Add(s);
        }
        catch { /* best-effort */ }
    }
}
