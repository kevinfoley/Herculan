# Handoff: ES2's host wiring out of the host

Branch `features/es1-refactor`. Steps 1 to 6 moved ES2's game code into `Herculan.Es2` (the commits say what moved where); the user's simulator and editor run after step 6 looked right. 7a is done. Step 7 applies the rest of [planning.md, "How the two games share code"](planning.md#how-the-two-games-share-code): "Host code that wires one game's screens goes with that game." ES1 already does this (`Herculan.Es1/Host/Es1Launcher.cs`, `Herculan.Es1/Shell/Es1ShellHost.cs`, menus handed in as `Func<IFrontEndMenus>`); ES2's launcher, shell host and simulator host are still in `Herculan.Engine.Host`.

## What the host holds now

Sorted by what each file's types and usings need. The game-agnostic UI and `HostMenuBar` are in the core since 7a: `Herculan.Engine/Menus/` (namespace `Herculan.Engine.Menus`: the bar, its own panels and dialogs, `UpdateChecks`, `PanelCentring`, and the `IMenuBarPanel` / `IDrawnMenuBarPanel` interfaces a game's panels come in as), `Herculan.Engine/Localization/` (`LocalizationTable`, `InterfaceLanguageCombo`, `LocalizedImGuiHelpers`), and `Herculan.Engine/Platform/` (`NativeAlert` beside `NativePathPicker`, which it uses, and `IEscapeMenu` beside `IFrontEndMenus`). The bar's on-line manual notice is a `manualOpening` delegate, which the ES2 shell and simulator pass as `() => OnlineManual.Opening`. The licence the About window shows is embedded in `Herculan.Engine.csproj`.

- **ES2's wiring:** `Launcher.cs`, `HostSession.cs` (its disc is `GameInstall.OpenDisc`), `MovieHost.cs`, `JoystickSource.cs`, `OnlineManual.cs`, `Install/*` (all on ES2's `GameInstall`; `Program.cs` calls `InstallPrompt` and `DiscInstall`), `Settings/SettingsWindow.cs`, `Settings/ControllerWindow.cs`, `Shell/*` and `Shell/Tabs/*` except `Es1ShellMenus.cs`, `Simulator/*`, `Debugging/*`. Of these, only `ShellHost`, `ShellMenus`, `ShellManual`, `MainMenuPanels`, `CustomMissionsWindow`, `SaveNames`, `PauseMenu`, `SimulatorHost`, `SimulatorStartup`, `WindowKeys` and `WorldPassRenderer` name a host type at all, and those are `HostSession`, `HostOptions`, `MovieHost`, `OnlineManual` and `DebugOptions`.
- **ES1's wiring:** `Shell/Es1ShellMenus.cs` and `Es1Options` (in `HostOptions.cs`).
- **The application itself:** `Program.cs`, `HostArguments.cs`, `HostOptions.cs` (the parser), `HostLog.cs`, `PrintScreenFiles.cs` and `WindowIcon.cs` (both use `HercWorks.Help.Html`, which the core does not reference), and the editor turn in `Launcher.cs` (`EditorHost.Run`): `Herculan.Engine.Editor` references `Herculan.Es2`, so `Herculan.Es2` cannot call it.

## Step 7, in four commits

Each sub-step builds both solutions at 0 warnings and passes the tests before the next starts. `git mv` throughout, so history follows. Moved types that cross an assembly become `public`; say which in the commit.

- **7b. ES2's wiring into `Herculan.Es2/Host/`,** at the same relative paths (`Shell/ShellHost.cs` → `Herculan.Es2/Host/Shell/ShellHost.cs`, namespaces `Herculan.Es2.Host.<folder>`), which mirrors `Herculan.Es1/Host` and keeps clear of `Herculan.Es2/Shell`'s game classes. The seams:
  - The editor turn: ES2's launcher takes a delegate the host builds over `EditorHost.Run`, answering "closed", "back to the shell" or "play this launch, then reopen this mission"; `RunEditorTurns`' loop stays on the ES2 side.
  - The window: the host opens the `HostWindow`, attaches `PrintScreenFiles`, and hands it to ES2's launcher, so `PrintScreenFiles` and `WindowIcon` stay in the host.
  - The options: `ShellOptions` and ES2's part of `HostOptions` (staging, tapes, music track, `--developer`, `--no-sound`, screenshot) become an ES2 options type in `Herculan.Es2/Host` that `HostOptions` holds, as it holds `Es1Options`; the parser and `HostArguments.Usage` stay in the host. `SimulatorHost` and `SimulatorStartup` take the ES2 type in place of `HostOptions`.
  - The panels: `SettingsWindow`, `ControllerWindow`, `DebugPanel`, `MissionScriptingWindow` and `CustomMissionsWindow` already implement 7a's panel interfaces, so they move as they are.
  - The tests: `PanelLayoutTests` draws ES2's install prompt and Settings window as well as the core's panels, so it stays in `tests/Herculan.Engine.Host.Tests`, which the core gives `InternalsVisibleTo`; once its ES2 types are in `Herculan.Es2`, that assembly needs the same.
- **7c. ES1's wiring into `Herculan.Es1/Host/`:** `Es1ShellMenus.cs` and `Es1Options`.
- **7d. Tools and docs.** `doc_lint.py`'s `CSHARP_SOURCE_DIRS` walks `Herculan.Engine` and `Herculan.Es2` whole, so the moved files need no change there; grep `known_symbols_*.json`, `ROADMAP.md`, `KNOWN_ISSUES.md` and `docs/` for `Herculan.Engine.Host/` paths and moved type names; run `doc_links.py --code`. planning.md's core list names "install detection and launcher": after 7b, the core's part is `RetailGameDetector` and ES2's `GameInstall` and launcher are ES2's, so reword that line.

What stays in `Herculan.Engine.Host` afterwards: `Program.cs`, `HostArguments`, `HostOptions` (the parser), `HostLog`, `PrintScreenFiles`, `WindowIcon` and the editor delegate.

## Baseline to compare against

Both solutions build at 0 warnings; 1,133 engine tests and 59 host tests pass. `dotnet build HerculanEngine.sln -p:GenerateDocumentationFile=true` reports 46 distinct unresolved crefs (CS1574, deduplicated by file and position), none in the host's menu or panel files. `doc_links.py --code` reports only the three `difficulty.md` links. Every sub-step should leave these unchanged. A cref to a type that moved out of reach becomes plain text, as Es1's `InputTapePlayer` cref did in step 6.

Not checked by the build: the shell's and a mission's menu bar (every item, greyed ones included), Settings' install and disc switch, the Editor item and its Play, Custom Missions' Play, `--movie`, and ES1's shell bar. That run is the user's, after 7b and again after 7c.
