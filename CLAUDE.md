# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Run in development
cd SurroundVisionPlayer && dotnet run

# Run tests
dotnet test

# Publish self-contained (ARM64)
dotnet publish SurroundVisionPlayer -c Release -r win-arm64 --self-contained

# Publish self-contained (x64)
dotnet publish SurroundVisionPlayer -c Release -r win-x64 --self-contained
```

The project targets .NET 8 + WPF (C# 12), no external NuGet dependencies. Test framework is xUnit.

## Architecture

**Surround Vision Player** is a WPF dashcam viewer for the 2027 Chevy Bolt EV. It plays 4 synchronized camera angles (FRONT, LEFT, REAR, RIGHT) from MP4 files recorded on a USB thumb drive.

### Component Layout

```
SurroundVisionPlayer/
├── MainWindow.xaml/.cs       — All UI, playback state, event handling (~800 lines)
├── DrivePickerWindow.xaml/.cs — Folder selection dialog
└── Logic/
    ├── RecordingScanner.cs   — Discovers MP4s matching the filename pattern
    ├── SessionGrouper.cs     — Clusters clips into trips by timestamp proximity
    ├── AppSettings.cs        — Persists settings to %APPDATA% as JSON
    └── Archiver.cs           — Copies clips to archive folder by date

SurroundVisionPlayer.Tests/
├── RecordingScannerTests.cs
└── SessionGrouperTests.cs
```

### Key Design Decisions

**Multi-angle playback without seek latency**: All 4 `MediaElement` controls decode simultaneously. The active angle is `Visibility.Visible`; others are `Visibility.Hidden` but keep decoding. Angle switching is instant — no seek or reload.

**Synchronization**: A `DispatcherTimer` fires every 200 ms and corrects any non-active player that drifts more than 500 ms from the active player's position.

**Session stitching**: Clips within 375 seconds of each other are merged into one "trip" in the sidebar. Playback auto-advances through all clips in a trip. A cumulative offset list (`_clipOffsetsMs`) maps session-level timeline positions to per-clip positions.

**Duration probing**: Clip durations are probed asynchronously via `MediaPlayer.MediaOpened` before display; the UI shows a loading indicator until all durations resolve.

### Recording File Format

Filename pattern: `ANGLE_YYYY_MM_DD_T_HH_MM_SS.mp4`  
Example: `FRONT_2026_04_16_T_15_18_19.mp4`

Default USB path:
```
D:\Android\media\com.gm.ultifi.gmconnectedcameraservice\Recordings\SurroundVisionRecorder\
```

Archive structure: `ArchiveRoot\YYYY-MM-DD\<files>`

### Settings

Stored at `%APPDATA%\SurroundVisionPlayer\settings.json`. Currently only holds the archive folder path.

## Release

GitHub Actions (`.github/workflows/release.yml`) triggers on `v*` tags, builds ARM64 + x64 single-file self-contained executables, and publishes them to GitHub Releases.
