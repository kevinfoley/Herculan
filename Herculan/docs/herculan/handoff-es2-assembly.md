# Handoff: ES2's host wiring out of the host

Branch `features/es1-refactor`. Steps 1 to 6 moved ES2's game code into `Herculan.Es2` (the commits say what moved where); the user's simulator and editor run after step 6 looked right. 7a, 7b and 7c are done. Step 7 applies the rest of [planning.md, "How the two games share code"](planning.md#how-the-two-games-share-code): "Host code that wires one game's screens goes with that game." Both games now do: ES2's wiring is in `Herculan.Es2/Host/`, and ES1's, menus included, in `Herculan.Es1/Host/` (`Es1Launcher.cs`, `Es1ShellMenus.cs`, `Es1Options.cs`) beside `Herculan.Es1/Shell/Es1ShellHost.cs`, which takes its menus as a `Func<IFrontEndMenus>`.

## What the host holds now

Sorted by what each file's types and usings need. The game-agnostic UI and `HostMenuBar` are in the core since 7a: `Herculan.Engine/Menus/` (namespace `Herculan.Engine.Menus`: the bar, its own panels and dialogs, `UpdateChecks`, `PanelCentring`, and the `IMenuBarPanel` / `IDrawnMenuBarPanel` interfaces a game's panels come in as), `Herculan.Engine/Localization/` (`LocalizationTable`, `InterfaceLanguageCombo`, `LocalizedImGuiHelpers`), and `Herculan.Engine/Platform/` (`NativeAlert` beside `NativePathPicker`, which it uses, and `IEscapeMenu` beside `IFrontEndMenus`). The bar's on-line manual notice is a `manualOpening` delegate, which the ES2 shell and simulator pass as `() => OnlineManual.Opening`. The licence the About window shows is embedded in `Herculan.Engine.csproj`.

- **ES2's wiring** is in `Herculan.Es2/Host/` at the paths it had in the host, namespaces `Herculan.Es2.Host.<folder>`. The host reaches it through `Launcher`, `HostSession`, `Es2Options`, `InstallPrompt`, `DiscInstall`, `MovieHost` and `ShellHost` (for `DiscCheckMovie`), which are public, as are the seams' `EditorTurn` and `EditorTurnEnd`. `Herculan.Es2` references `HercWorks.Help` for `OnlineManual` and gives `Herculan.Engine.Host.Tests` `InternalsVisibleTo` for `PanelLayoutTests`.
- **ES1's wiring** is in `Herculan.Es1/Host/`, namespace `Herculan.Es1.Host`: `Es1ShellMenus` (the shell's `IFrontEndMenus` over the core's `HostMenuBar`) and `Es1Options` (ES1's flags), both public, as `Program.cs` builds the one and `HostOptions` holds the other.
- **The application itself**, everything left in `Herculan.Engine.Host`: `Program.cs`, which opens the `HostWindow` ES2's run takes and attaches `PrintScreenFiles`; `HostArguments.cs`; `HostOptions.cs`, the parser, which sets ES2's flags in its `Es2Options` and ES1's in its `Es1Options`, and copies the flags both games take (mission, screenshot, `--no-sound`, the tapes) into `Es2Options` once parsing ends; `HostEditorTurn.cs`, the `EditorTurn` over `EditorHost.Run`, since `Herculan.Engine.Editor` references `Herculan.Es2`; `HostLog.cs`; `PrintScreenFiles.cs` and `WindowIcon.cs` (both use `HercWorks.Help.Html`).

## Step 7's remaining commit

It builds both solutions at 0 warnings and passes the tests.

- **7d. Tools and docs.** `doc_lint.py`'s `CSHARP_SOURCE_DIRS` walks `Herculan.Engine` and `Herculan.Es2` whole, so the moved files need no change there; grep `known_symbols_*.json`, `ROADMAP.md`, `KNOWN_ISSUES.md` and `docs/` for `Herculan.Engine.Host/` paths and moved type names; run `doc_links.py --code`. planning.md's core list names "install detection and launcher": after 7b, the core's part is `RetailGameDetector` and ES2's `GameInstall` and launcher are ES2's, so reword that line. A grep after 7c found no doc or `known_*.json` naming `Es1ShellMenus`, `Es1Options` or `Herculan.Engine.Host/Shell`.

## Baseline to compare against

Both solutions build at 0 warnings; 1,133 engine tests and 59 host tests pass. `dotnet build HerculanEngine.sln --no-incremental -p:GenerateDocumentationFile=true` reports 46 distinct unresolved crefs (CS1574, deduplicated by file and position), none in the moved files or in what is left of the host. `doc_links.py --code` reports only the three `difficulty.md` links. 7d should leave these unchanged. A cref to a type that moved out of reach becomes plain text, as Es1's `InputTapePlayer` cref did in step 6.

Not checked by the build: the shell's and a mission's menu bar (every item, greyed ones included), Settings' install and disc switch, the Editor item and its Play, Custom Missions' Play, `--movie`, and ES1's shell bar (Accessibility and About open, the rest greyed). That run is the user's.
