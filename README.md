# SpriteEditXL

<p align="center">
  <img src="Assets/AppIcon.png" alt="SpriteEditXL icon" width="160" />
</p>

A cross-platform Atari Player/Missile sprite editor, written in C# / Avalonia.

SpriteEditXL is a desktop port and extension of bocianu's JavaScript
[SprEd](https://gitlab.com/bocianu/spred). It keeps full compatibility with the
original `.spr` binary format and the community library at restdb.io, and adds
a native desktop workflow — animation sub-sequences, reference image overlay,
drag-and-drop, local library — while staying faithful to SprEd's chunky brown
toolbar and gold selection highlights.

## Features

- All original SprEd sprite modes: P0, P0+P1, MP0+MP1, through MP0+MP1+MP2+MP3
- Per-row DLI color changes
- Common palette, link colors, frame delay, multi-frame edit
- Animation playback with named sub-sequences and drag-to-reorder frames
- Reference image overlay (any PNG/GIF/JPG) with minimap and scale controls
- Three-tab community library:
  - **SpriteEditXL** — read + write (upload your sprites)
  - **SprEd (read)** — read-only mirror of bocianu's original library
  - **Local** — sprites saved under `%APPDATA%/SpriteEditXL/library/`
- Sort by date / name / frame count × ascending / descending
- 24-hour on-disk cache for library pages
- Animated GIF export and atasm source export
- Drag-and-drop `.sprj` / `.spr` / `.apl` onto the window to open
- Per-frame delay editing (click the orange number)
- Undo / Redo with configurable depth

## Install

Grab a prebuilt binary from the [latest release](https://github.com/CycoPH/SpriteEditXL/releases/latest):

| Platform  | File                            |
|-----------|---------------------------------|
| Windows   | `SpriteEditXL-win-x64.zip`      |
| Linux     | `SpriteEditXL-linux-x64.tar.gz` |
| macOS Intel      | `SpriteEditXL-osx-x64.tar.gz`   |
| macOS Apple Silicon | `SpriteEditXL-osx-arm64.tar.gz` |

Each archive contains a single self-contained executable — no .NET runtime
required.

### macOS note

On first launch, macOS Gatekeeper may block the unsigned binary. Allow it with:

```bash
xattr -d com.apple.quarantine SpriteEditXL
```

Or right-click the app → **Open** → **Open anyway**.

## Build from source

Requirements: [.NET 11 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/CycoPH/SpriteEditXL.git
cd SpriteEditXL
dotnet run
```

### Release build (single-file, self-contained)

```bash
dotnet publish -c Release -r win-x64 -o publish/win-x64      # Windows
dotnet publish -c Release -r linux-x64 -o publish/linux-x64  # Linux
dotnet publish -c Release -r osx-x64 -o publish/osx-x64      # macOS Intel
dotnet publish -c Release -r osx-arm64 -o publish/osx-arm64  # macOS ARM
```

The output is a single ~48 MB executable with no external dependencies.

## File formats

- `.sprj` — primary format (JSON, human-readable, versioned)
- `.spr`  — legacy binary format, compatible with the original SprEd. Opening a
  `.spr` automatically writes a `.sprj` sibling for future opens.
- `.apl`  — alternate legacy variant (auto-detected)

## Credits

- Original JavaScript SprEd: **bocianu** — <https://gitlab.com/bocianu/spred>
- C# / Avalonia port: Peter Hinrichsen (readyplaygaming)

## License

MIT — see [LICENSE](LICENSE).
