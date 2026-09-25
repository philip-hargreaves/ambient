# The shell

Three production projects and their tests. The dependency arrow runs one way:

```
Ambient.App  ->  Ambient.App.Core  ->  Ambient.Client  -> (named pipe) ->  engine
```

| Project | Holds | Rule |
|---|---|---|
| `Ambient.App` | XAML views, their code-behind, themes, and the WinUI implementations of Core's ports | The only project that references WinUI. If a file needs a XAML type to compile, it lives here. |
| `Ambient.App.Core` | View models, the ports they depend on, engine supervision, preferences, metrics | No WinUI reference, so every view model is a plain object a test constructs off the UI thread. |
| `Ambient.App.Platform` | The Win32, registry and WMI adapters of Core's ports: engine launcher and job object, crash dumps, machine and power readers, process memory, the file logger | What Core must not know. References Core, never WinUI. |
| `Ambient.Client` | `IEngineApi` and its typed replies, `IEngineTransport`, message framing, the pipe transport | The engine's SDK. Knows nothing about the app. |

## Folders

Both shell projects use the same feature names, so a view and its view model sit at mirrored paths:

```
Ambient.App/                      Ambient.App.Core/
  Shell/       window, status bar    Shell/       shell and status view models
  Features/                          Features/
    Consultation/  the live screen     Consultation/
    Documents/     note, patient       Documents/
                   sheet, transcript
    Guidance/      guideline cards     Guidance/
    Sessions/      stored consults     Sessions/
    Appraisal/     reflections         Appraisal/
    Settings/                          Settings/
    Help/          guide and About
    Demo/          replay tray         Demo/
  Controls/    reusable, no VM       Ports/       interfaces only, implemented outside Core
               (page header, tabs,   Common/      helpers every feature shares: engine
               icon label, busy                   call, words, session text, clipboard
               caption, stale notice)
  Platform/    WinUI adapters        Hosting/     the engine process: launch, supervise, connect
  Themes/      tokens and styles     Metrics/  Preferences/
```

`Ambient.App.Tests` mirrors `Ambient.App.Core`; its fakes are in `TestDoubles/` and shared
helpers in `Support/`. Tests that need the built engine carry `Requires=Engine` (the whole of
`Ambient.Client.Tests/Contract/`, plus the replay and transport-recovery tests in `App.Tests`);
`Requires=EngineSlow` marks the ten-minute 1x replay and `Requires=CrashBattery` the crash set.
The fast filter is `Requires!=Engine&Requires!=EngineSlow&Requires!=CrashBattery`.

The presentation pattern and its rules are in ADR-0014 (`docs/production/adr/` in the research
repository).

The settings page is built on the Community Toolkit's settings controls
(`CommunityToolkit.WinUI.Controls.SettingsControls`), the one third-party UI dependency; every
other control is the platform's.
