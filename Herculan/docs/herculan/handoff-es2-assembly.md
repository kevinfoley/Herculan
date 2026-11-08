# Handoff: ES2's game code out of the core

Branch `features/es1-refactor`. Goal: `Herculan.Engine` holds only game-agnostic code, so ES2's game code can move to its own assembly ([planning.md, "How the two games share code"](planning.md#how-the-two-games-share-code)). Until that assembly exists, nothing enforces the line except `tools/BoundaryCheck`.

## The checker

```
dotnet run --project tools/BoundaryCheck
dotnet run --project tools/BoundaryCheck -- --code
```

Build the engine first. It binds the whole engine compilation with Roslyn and lists each core file's references to ES2 code, split into `code`, `using` and `doc`; `--code` drops the doc-comment ones. Exit 1 while anything is listed. `tools/BoundaryCheck/es2-files.txt` is the definition of "ES2's": six folders plus single files in core folders. Sorting a file to ES2 means adding it there. A core-folders-only build is not a substitute: once a `using` fails, the compiler stops reporting that file's body errors.

## Steps

1. **Done.** Small type moves: `ViewEdgeSlopes` to `Render`, `ShapeCellFrames` to `Anim`, the light falloff constants to `EffectLightSelection`, `InvisibleMounting` to `GunLayout.HardpointEntry` (HercWorks.Core), `FlyCameraInput` into `PilotControls`. `IPointerDevice` and `CockpitMouseButtons` were judged ES2's (the modal panels' pointer, no ES1 user).
2. **Done.** Install, joystick, doc-only references:
   - Install: the whole `Install` folder (`GameInstall`, `LauncherLanguage`, `RetailInstaller`) and `InstructorVoice` are ES2's; ES1 uses none of them. Core `GameContent.MountInstall` takes the disc and the language from its caller, and owns `ArchiveFolderName` and `ArchiveDirectory`. `GameInstall.MountSimulator`/`MountShell` are ES2's mounts: they open the `drive.cfg` disc and pick the launcher language. `IsV110` left `GameContent`: its four readers call `LauncherLanguage.IsV110(content.InstallRoot)` once at setup. ES1's two mounts pass English; nothing in ES1 reads the language.
   - Joystick: `JoystickBindings` and `JoystickPilotInput` are ES2's (DBSIM's `prefs.cfg` controls arm, no ES1 user). `JoystickCapabilities` stays core as what a stick reports; `RowEnabled`, the CONTROLS panel's greying, is now private to `ControlsPanel`.
   - ES2's by content, with no ES1 user: `CdMusic` (DBSIM's `Music_*` layer), `CockpitClipRegions`, `CockpitGlance`, `CockpitHitShake`, `CockpitViewKick`. `CockpitViewGeometry` stays core: ES1's `HudFont` use reads its `CoordShift`.
   - Doc-only: 22 core files' doc comments no longer name ES2 code; each says what the core does, without its ES2 callers.
3. The settings catalogs split into core and ES2 parts. `TweakSettingDefinitions` lists ES2's tweaks beside ones core files read (`SceneRenderer`); `A11ySettingDefinitions` names `CockpitHitShake`'s flash and shake settings in doc comments.
4. `SceneRenderer` seam: the `SimObject` light subject becomes something ES1 can fill, and ES2's painter's-order terrain pass (`HeightGrid.PickDrawCell`, `TerrainVisibleRegion`, `TerrainPaintOrder`, `TerrainPaintRankBuffer`, `GroundShapeLayer`, and `DrawEntry`, which `SpriteRenderer` also takes) goes behind a core hook. Also brings the class under 1,500 lines. The one step with a design choice for the user.
5. Sort the rest the checker lists, each as ES2's (add it to `es2-files.txt`) or a seam: `SoundBank` (`SoundCatalog`), `SquadVoice` (`ComputerVoice.VoiceFolder`), `SquadMessages` (`SystemMessages.IdAttribute`), `KeyState` (`InputTapePlayer`'s modifier bits), `PaletteRampTable` (`TerrainMeshBuilder.UntexturedRowShade`), `DistanceColorBands` (`ObjectTypeTag`), `IPointerDevice`, `ISimulatorInput` (`CockpitInput`). Done when the checker exits 0.
6. Create the ES2 assembly and move everything `es2-files.txt` lists. The user decides whether namespaces change. A project reference then replaces `Es1BoundaryTests` and the checker.

## Where it stands

After step 2 the checker lists 12 core files: the 11 in steps 4 and 5 use ES2 code, and `A11ySettingDefinitions` (step 3) names it only in doc comments. The `TweakSettingDefinitions` line also has a `using` line for `Herculan.Engine.Cockpit`. A run takes about 20 seconds.

Checks after step 2: both builds 0 warnings, all tests pass (1,134 engine tests), 23 broken doc `cref`s in `Herculan.Engine` with `GenerateDocumentationFile` on (unchanged; none in step 2's lines), and `doc_links.py` reports only the three broken `difficulty.md` links that were already there.
