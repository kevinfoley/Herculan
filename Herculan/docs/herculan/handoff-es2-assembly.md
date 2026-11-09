# Handoff: ES2's host wiring out of the host

Branch `features/es1-refactor`. Steps 1 to 6 moved ES2's game code into `Herculan.Es2` (the commits say what moved where); the user's simulator and editor run after step 6 looked right. Step 7 applies the rest of [planning.md, "How the two games share code"](planning.md#how-the-two-games-share-code): "Host code that wires one game's screens goes with that game." ES1 already does this (`Herculan.Es1/Host/Es1Launcher.cs`, `Herculan.Es1/Shell/Es1ShellHost.cs`, menus handed in as `Func<IFrontEndMenus>`); ES2's launcher, shell host and simulator host are still in `Herculan.Engine.Host`.

## What the host holds now

Sorted by what each file's types and usings need (82 types, 71 files):

- **Game-agnostic UI, used by ES2's wiring and by ES1's menus:** `Localization/LocalizationTable.cs`, `Localization/InterfaceLanguageCombo.cs`, `LocalizedImGuiHelpers.cs`, `NativeAlert.cs`, and in `Settings/`: `PanelCentring`, `AboutWindow`, `AccessibilityMenu`, `TweaksMenu`, `ErrorDialog`, `WelcomeDialog`, `UpdateDialog`, `UpdateChecks`, `KeysWindow`, `KeyLabels`, `KeyRows` (its `OnlineManual` is `KeyAction.OnlineManual`, not the class). None has an Es2 using.
- **`Settings/HostMenuBar.cs`, the hub:** game-agnostic except that it implements ES2's `IEscapeMenu` (`Herculan.Es2/Cockpit/SimulatorFrame.cs`, three members, nothing ES2 in it) and holds five ES2 panels by type: `SettingsWindow`, `ControllerWindow`, `DebugPanel`, `MissionScriptingWindow`, `CustomMissionsWindow`.
- **ES2's wiring:** `Launcher.cs`, `HostSession.cs` (its disc is `GameInstall.OpenDisc`), `MovieHost.cs`, `JoystickSource.cs`, `OnlineManual.cs`, `Install/*` (all on ES2's `GameInstall`; `Program.cs` calls `InstallPrompt` and `DiscInstall`), `Settings/SettingsWindow.cs`, `Settings/ControllerWindow.cs`, `Shell/*` and `Shell/Tabs/*` except `Es1ShellMenus.cs`, `Simulator/*`, `Debugging/*`. Of these, only `ShellHost`, `ShellMenus`, `ShellManual`, `MainMenuPanels`, `CustomMissionsWindow`, `SaveNames`, `PauseMenu`, `SimulatorHost`, `SimulatorStartup`, `WindowKeys` and `WorldPassRenderer` name a host type at all, and those are `HostSession`, `HostOptions`, `MovieHost`, `OnlineManual`, `LocalizationTable`, `NativeAlert`, the menu-bar UI and `DebugOptions`.
- **ES1's wiring:** `Shell/Es1ShellMenus.cs` and `Es1Options` (in `HostOptions.cs`).
- **The application itself:** `Program.cs`, `HostArguments.cs`, `HostOptions.cs` (the parser), `HostLog.cs`, `PrintScreenFiles.cs` and `WindowIcon.cs` (both use `HercWorks.Help.Html`, which the core does not reference), and the editor turn in `Launcher.cs` (`EditorHost.Run`): `Herculan.Engine.Editor` references `Herculan.Es2`, so `Herculan.Es2` cannot call it.

## Step 7, in four commits

Each sub-step builds both solutions at 0 warnings and passes the tests before the next starts. `git mv` throughout, so history follows. Moved types that cross an assembly become `public`; say which in the commit.

- **7a. The game-agnostic UI into the core.** The first list above and `HostMenuBar` go to `Herculan.Engine/Menus/` and `Herculan.Engine/Localization/` (namespaces `Herculan.Engine.Menus`, `Herculan.Engine.Localization`). `IEscapeMenu` moves to `Herculan.Engine/Platform/` beside `IFrontEndMenus`. `HostMenuBar`'s five ES2 panel parameters become a core panel interface (open state, open, back out, draw), keeping each panel's own slot and order, so a null still greys Settings and Controller, and ES1's bar looks as it does now. The `.lang` files stay a copy item of the host project: `LocalizationTable` reads `AppContext.BaseDirectory`, which is the host's output. `tests/Herculan.Engine.Host.Tests` (`KeysWindowTests`, `PanelLayoutTests`) tests these types as internals: give the core `InternalsVisibleTo` for it, or move both files into `Herculan.Engine.Tests`, and say which.
- **7b. ES2's wiring into `Herculan.Es2/Host/`,** at the same relative paths (`Shell/ShellHost.cs` → `Herculan.Es2/Host/Shell/ShellHost.cs`, namespaces `Herculan.Es2.Host.<folder>`), which mirrors `Herculan.Es1/Host` and keeps clear of `Herculan.Es2/Shell`'s game classes. The seams:
  - The editor turn: ES2's launcher takes a delegate the host builds over `EditorHost.Run`, answering "closed", "back to the shell" or "play this launch, then reopen this mission"; `RunEditorTurns`' loop stays on the ES2 side.
  - The window: the host opens the `HostWindow`, attaches `PrintScreenFiles`, and hands it to ES2's launcher, so `PrintScreenFiles` and `WindowIcon` stay in the host.
  - The options: `ShellOptions` and ES2's part of `HostOptions` (staging, tapes, music track, `--developer`, `--no-sound`, screenshot) become an ES2 options type in `Herculan.Es2/Host` that `HostOptions` holds, as it holds `Es1Options`; the parser and `HostArguments.Usage` stay in the host. `SimulatorHost` and `SimulatorStartup` take the ES2 type in place of `HostOptions`.
  - The panels: `SettingsWindow`, `ControllerWindow`, `DebugPanel`, `MissionScriptingWindow` and `CustomMissionsWindow` implement 7a's panel interface.
- **7c. ES1's wiring into `Herculan.Es1/Host/`:** `Es1ShellMenus.cs` and `Es1Options`.
- **7d. Tools and docs.** `doc_lint.py`'s `CSHARP_SOURCE_DIRS` walks `Herculan.Engine` and `Herculan.Es2` whole, so the moved files need no change there; grep `known_symbols_*.json`, `ROADMAP.md`, `KNOWN_ISSUES.md` and `docs/` for `Herculan.Engine.Host/` paths and moved type names; run `doc_links.py --code`. planning.md's core list names "install detection and launcher": after 7b, the core's part is `RetailGameDetector` and ES2's `GameInstall` and launcher are ES2's, so reword that line.

What stays in `Herculan.Engine.Host` afterwards: `Program.cs`, `HostArguments`, `HostOptions` (the parser), `HostLog`, `PrintScreenFiles`, `WindowIcon` and the editor delegate.

## Baseline to compare against

Both solutions build at 0 warnings; 1,133 engine tests pass. With `GenerateDocumentationFile` on, the engine solution has 40 broken doc crefs. `doc_links.py --code` reports only the three `difficulty.md` links. Every sub-step should leave these unchanged. A cref to a type that moved out of reach becomes plain text, as Es1's `InputTapePlayer` cref did in step 6.

Not checked by the build: the shell's and a mission's menu bar (every item, greyed ones included), Settings' install and disc switch, the Editor item and its Play, Custom Missions' Play, `--movie`, and ES1's shell bar. That run is the user's, after 7b and again after 7c.
