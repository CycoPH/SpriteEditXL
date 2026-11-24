using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpriteEditXL.IO;
using SpriteEditXL.Model;
using SpriteEditXL.UI;

namespace SpriteEditXL;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            string? loadPath = args.FirstOrDefault(a => !a.StartsWith("--"));
            string? shotPath = GetArgValue(args, "--screenshot");

            // If the user passed a .sprj or .spr file, replace the auto-loaded workspace.
            if (loadPath != null && File.Exists(loadPath))
            {
                try
                {
                    string ext = Path.GetExtension(loadPath).ToLowerInvariant();
                    Workspace ws;
                    Options opt;
                    if (ext == SprJsonFile.DefaultExtension)
                    {
                        (ws, opt) = SprJsonFile.Load(loadPath);
                    }
                    else
                    {
                        (ws, opt) = SprFile.Load(loadPath);
                        SequenceSidecar.TryLoadInto(loadPath, ws);
                        // Auto-create a .sprj sibling the first time we see a legacy .spr.
                        try
                        {
                            var sibling = Path.ChangeExtension(loadPath, SprJsonFile.DefaultExtension);
                            if (!File.Exists(sibling))
                                SprJsonFile.Save(sibling, ws, opt);
                        }
                        catch { /* best effort */ }
                    }
                    ws.SyncPrimary();
                    Storage.SaveOptions(opt);
                    Storage.SaveWorkspace(ws);
                }
                catch { /* best effort */ }
            }

            var win = new MainWindow();
            desktop.MainWindow = win;

            if (shotPath != null)
            {
                win.Opened += async (_, _) =>
                {
                    // Give the UI a few frames to settle, then render to PNG and exit.
                    await Task.Delay(1200);
                    TakeScreenshot(win, shotPath);
                    await Task.Delay(200);
                    desktop.Shutdown();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        return null;
    }

    private static void TakeScreenshot(Window window, string path)
    {
        try
        {
            var size = new PixelSize(
                Math.Max(1, (int)window.Bounds.Width),
                Math.Max(1, (int)window.Bounds.Height));
            var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            window.Measure(window.Bounds.Size);
            window.Arrange(new Rect(window.Bounds.Size));
            rtb.Render(window);
            rtb.Save(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"screenshot failed: {ex}");
        }
    }
}

// local Task shim — pulls in System.Threading.Tasks without needing a using at top
file static class Task
{
    public static System.Threading.Tasks.Task Delay(int ms) =>
        System.Threading.Tasks.Task.Delay(ms);
}
