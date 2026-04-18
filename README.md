# Surround Vision Player — WPF Edition

Native Windows 11 ARM64 dashcam viewer for the 2027 Chevy Bolt.  
Built with **C# 12 + .NET 8 + WPF**.  No external dependencies.  
Video playback uses Windows **Media Foundation** directly via WPF's
`MediaElement` — hardware-accelerated H.264 decode, works natively on
ARM64 without any codec installs or architecture mismatches.

---

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10/11 | ARM64 or x64 |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | For building |
| .NET 8 Runtime | Included when self-contained publishing |

No VLC, no Python, no extra codecs required.

---

## Build & Run

### Quick run (debug)
```powershell
cd SurroundVisionPlayer
dotnet run
```

### Publish for ARM64 (self-contained)
```powershell
dotnet publish SurroundVisionPlayer -c Release -r win-arm64 --self-contained
# Output: SurroundVisionPlayer\bin\Release\net8.0-windows\win-arm64\publish\
```

### Publish for x64
```powershell
dotnet publish SurroundVisionPlayer -c Release -r win-x64 --self-contained
```

### Run tests
```powershell
dotnet test
```

---

## Usage

1. Launch `SurroundVisionPlayer.exe`
2. If the app is run from the thumb drive, it auto-detects recordings.
   Otherwise the **Select Thumb Drive** dialog opens — browse to the
   drive root (e.g. `D:\`) and click **Open**.
3. Select a trip from the left panel.
4. Use the angle buttons or keyboard to switch camera views.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Space` | Play / Pause |
| `←` | Rewind 10 seconds |
| `→` | Fast-forward 10 seconds |
| `F` | FRONT camera |
| `L` | LEFT camera |
| `R` | REAR camera |
| `G` | RIGHT camera |
| `Ctrl+O` | Open Thumb Drive |
| `Ctrl+Q` | Quit |

---

## Project structure

```
SurroundVisionPlayer.sln
│
├── SurroundVisionPlayer/               WPF application
│   ├── Logic/
│   │   ├── RecordingScanner.cs         File scanning & SVR detection
│   │   └── SessionGrouper.cs          Trip grouping (5-min segment stitching)
│   ├── App.xaml[.cs]
│   ├── MainWindow.xaml[.cs]           Main player UI
│   └── DrivePickerWindow.xaml[.cs]    Thumb drive selection dialog
│
└── SurroundVisionPlayer.Tests/         xUnit test suite
    ├── RecordingScannerTests.cs
    └── SessionGrouperTests.cs
```

---

## How it works

**Simultaneous playback:** All four `MediaElement` controls (FRONT, LEFT,
REAR, RIGHT) are stacked in the same `Grid` cell and all play at once.
The active angle is `Visibility.Visible`; the other three are
`Visibility.Hidden` — they remain in the render tree (WPF keeps decoding)
but are invisible.  Switching angles is instant with no seek required.

**Synchronisation:** A `DispatcherTimer` fires every 200 ms.  If any
non-active player has drifted more than 500 ms from the active player's
position, it is nudged back in sync.

**Session stitching:** Clips recorded within 375 s of each other
(≤ one 5-minute segment + 25 % headroom) are grouped into a single trip
and listed as one entry in the sidebar.  The player auto-advances through
all clips in the trip seamlessly.

**Auto-advance:** When the active player fires `MediaEnded`, the player
loads the next clip in the current session and continues playing.
