# DisplayBrightnessApp

A tiny Windows system tray app for controlling the brightness of every
connected monitor from one place. Click the tray icon, drag a slider per
monitor, optionally sync them all together — that's the whole app.

## Features

- Lives entirely in the system tray — no taskbar window, no main window.
- One slider per detected monitor, in a popup that opens next to the tray icon.
- **Synchronize** toggle: drag one slider and every monitor follows.
- Controls external monitors via **DDC/CI** and laptop-internal panels via
  **WMI**, whichever applies to each display.
- Real monitor names and native resolution, resolved from EDID data — not
  the generic "Generic PnP Monitor" string Windows normally reports.
- Visually styled to match Windows 11's own flyouts: rounded corners,
  light/dark theme awareness, the system accent color, and an open/close
  animation.
- Starts automatically on login (no toggle for this — it just does).

## Requirements

- Windows 10 or 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
  from source. (A published self-contained build does not require .NET to
  be installed on the machine that runs it — see [Publishing](#publishing).)

## Building and running

```powershell
git clone https://github.com/<your-username>/DisplayBrightnessApp.git
cd DisplayBrightnessApp
dotnet build
dotnet run
```

The app doesn't open a window on launch — look for its icon in the system
tray. The built executable lives at
`bin\Debug\net8.0-windows\DisplayBrightnessApp.exe`.

## Publishing

To produce a single, self-contained `.exe` that runs on a machine without
the .NET runtime installed:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The result is written to `bin\Release\net8.0-windows\win-x64\publish\`.

## Project structure

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point, starts the tray application context. |
| `TrayApplicationContext.cs` | Tray icon, context menu, popup lifecycle. |
| `BrightnessPopupForm.cs` | The popup itself: layout, theming, animations. |
| `FluentSlider.cs` | Owner-drawn brightness slider. |
| `FluentToggleSwitch.cs` | Owner-drawn "Synchronize" toggle. |
| `RoundedCardPanel.cs` | Rounded card surface behind the monitor rows. |
| `Badge.cs` | Small resolution chip next to a monitor's name. |
| `MonitorBrightnessService.cs` | Monitor enumeration and brightness get/set (DDC/CI + WMI). |
| `NativeMethods.cs` | Win32/DWM P/Invoke declarations. |
| `ThemeHelper.cs` | Light/dark theme and accent color detection. |
| `TrayIconFactory.cs` | Runtime-drawn tray icon glyph. |
| `StartupRegistration.cs` | Self-registers autostart in the registry. |

## Contributing

Issues and pull requests are welcome. Please keep changes focused and
match the existing style (owner-drawn Fluent-style controls, no external
UI dependencies).

## License

MIT — see [LICENSE](LICENSE).
