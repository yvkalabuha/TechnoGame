# Singularity.Client.Spike

Godot 4 .NET client prototype: spike (TASK-0067) continued as v0.2 (TASK-0069): signal field, actor inset, tech tree A/B, wired to `Singularity.Session` (background thread, ADR-0014 п. 4). Not production code, not in `Singularity.sln`
(its own `Directory.Build.props` stops `src/Directory.Build.props`). Report: `docs/ui/prototypes/globe-spike-report.md`.

## Setup
- Godot 4.7.2 stable mono (linux x86_64) in `~/.local/opt/godot/`, never committed:
  `https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_linux_x86_64.zip`
  (sha256 `129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247`; sha512 matches the official SHA512-SUMS.txt).
- Godot 4.7.2 targets .NET 8 (`GodotPlugins.runtimeconfig.json`: net8.0, rollForward LatestMajor); SDK 8.0.426 is installed next to 10.0.403.
  The csproj uses `Godot.NET.Sdk/4.7.2` + `net8.0`. NuGet packages Godot.NET.Sdk/GodotSharp 4.7.2 were found in `~/.nuget/packages`.

```
G=~/.local/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64
$G --headless --path . --import            # once: imports glb/obj (creates .godot/, ignored)
$G --headless --path . --build-solutions --quit
$G --path .                                 # interactive: LMB drag rotate, wheel zoom, click pick, H hatch space
$G --path . -- --bench --out bench-out      # automatic benchmark + screenshots, then quits
```

## Data and assets (see `LICENSES.md`)
Committed because small: `assets/data/ne_50m_admin_0_countries_min.json` (derived from Natural Earth),
`assets/models/*` (6 Kenney glb, 1 OpenGameArt obj).
Regenerate the data: download
`https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson`,
keep per feature `NAME`, `ISO_A3_EH`, geometry rounded to 3 decimals.

## v0.2 (TASK-0069)
- net10.0: the Godot 4.7.2 host runs on .NET 8 by default but its `GodotPlugins.runtimeconfig.json` has `rollForward: LatestMajor`, so with the .NET 10 runtime installed the net10.0 game assembly and Core/Kernel/Session (ProjectReference) load as is. `dotnet build` of the csproj needs the .NET 10 SDK on PATH (`run-globe-spike.sh` does it).
- `./run-globe-spike.sh --shots` plays a scripted session and writes screenshots and `run-log.txt` to `docs/ui/prototypes/client-v02/`.
- Keys: Space pause, 1/2/3 tempo, T tech tree. Everything marked «макет» has no Session API.
