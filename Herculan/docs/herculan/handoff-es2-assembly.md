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
3. **Done.** Settings catalogs: core `TweakSettingDefinitions` keeps `HighResolutionWidescreen` and core `A11ySettingDefinitions` keeps `LargerFont`; every other setting moved to `Es2TweakSettingDefinitions` and `Es2A11ySettingDefinitions` (IDs unchanged, so saved files still load). `TweakSettings`/`A11ySettings` take a game catalog after the core's, and `Program.cs` sets `Current` with ES2's for either game, because the files are shared and a save writes only the settings it holds. The menus list `settings.All`. No ES1 code reads a tweak yet; `PerPixelDepthSorting` and `SmootherTurretMovement` are ES2's because their readers and descriptions are, and move to core only if ES1 gets a painter's-order renderer or turret rounding to switch. `BeamAppearance` (DBSIM's `BEAM.DAT`/`BEAMTEX.DBA`, ES2-only users) was sorted to ES2.
4. **Done.** `SceneRenderer` seam, the user's option B: no core hook; ES2 owns its ground draw and the core offers drawing steps.
   - `SceneRenderer`'s public steps: `BeginPass` (viewport, sky, per-pass uniforms), `DrawItem`, `DrawItems(items, skip)`, `DrawUndepthed`, the paint-rank test (`BindPaintRanks(rankTexture)`, `BeginRankTest`, `EndRankTest`), `PassProjection`, `IsDrawn` and `PaintRankUnit`. The plain `Render` is `BeginPass` then `DrawItems`. `SpriteRenderer.DrawRanked` (was the internal `DrawEntry`) and `ExtendDepth` are public too.
   - ES2's `GroundLayerPass` (new) holds what left: the object filing for the view, the `TerrainPaintRankBuffer`, the per-pixel ground draw (`Draw`, the old `Render` overload) and the painter's-order start (`BeginPainted`). `WorldDrawPass` owns one and is now `IDisposable` (`WorldPassRenderer` disposes it); the editor's `EditorHost` holds its own.
   - Core interfaces: `ILightSubject` (`Position`, `ShapeRadius`), which `SimObject` implements and `SceneItem.LightSubject` takes; `IDrawFiling` (`Drawn`), which `DrawEntry` implements and `SceneItem.Filing`/`SpriteBatch.Filing` take; `IBandRampSource`, which `SceneRenderer.DistanceBands` takes. `BandRamp` moved to core `Render/BandRamp.cs` with `MaxPerItem`. ES2 code that needs the `DrawEntry` or `SimObject` back pattern-matches for it (`WorldDrawPass`, `GroundLayerPass`, `WorldDrawItems`, `DistanceColorBands.For`).
   - `DistanceColorBands` is ES2's (DBSIM's `SHADES.DAT` and `.WLD` bands; ES1 has its own `Es1Shading`) and implements `IBandRampSource` from the item's `DrawEntry`. `LightAnchor` stays as ES1's way to set a light position per item.
5. **Done.** The last seven, and the checker exits 0:
   - ES2's, with no ES1 or core user: `SoundBank`, `SquadVoice`, `SquadMessages` (the editor's `TriggerMessages` uses it), `IPointerDevice`, `ISimulatorInput`.
   - `KeyState.cs` split: the set-1 scancode table is core `Scancodes.KeysOf` (ES1's `Es1HostKeyboard`, `KeyRepeat` and `VkToScancode` read it); `TapeKeys` moved to its own ES2 file `Input/TapeKeys.cs` with `RecordedScancodes`. `IKeyState`, `LiveKeys` and `KeysWithout` stay core.
   - `PaletteRampTable.Build` takes the ground-fill shade from its caller (`GroundRow` is row 0 without one); ES2's `MissionModels` passes `TerrainMeshBuilder.UntexturedRowShade`. ES1 passes none: it draws no `GroundFill` item.
6. Create the ES2 assembly and move everything `es2-files.txt` lists. The user decides whether namespaces change. A project reference then replaces `Es1BoundaryTests` and the checker.

## Where it stands

After step 5 the checker lists nothing and exits 0; step 6 is next. A run takes about 20 seconds.

Checks after step 5: both builds 0 warnings, all tests pass (1,134 engine tests). The user's render check in the editor after step 4 looked right; the simulator has not been run since step 4. With `GenerateDocumentationFile` on, `Herculan.Engine` has 26 distinct broken doc `cref` warnings (CS1574/1580/1584), none in steps 3 to 5's lines. `doc_links.py` reports only the three broken `difficulty.md` links that were already there.
