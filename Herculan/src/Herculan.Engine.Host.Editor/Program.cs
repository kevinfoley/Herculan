using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Host.Editor;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;
using Silk.NET.Input;

// The mission editor: a second thin host next to Herculan.Engine.Host, sharing every loading and
// rendering utility but running a different loop — no sim ticking, so placed objects stand still,
// plus a free editor camera, click-to-select, and an ImGui Properties panel. This is the "possible
// future mission editor" docs/herculan/planning.md's host/library split was kept open for.

string? installRoot = GameInstall.Locate(args.Length > 0 ? args[0] : null);
if (installRoot == null) {
	Console.Error.WriteLine(
		"Could not find an Earthsiege 2 installation.\n" +
		$"Pass its path as the first argument, or set {GameInstall.PathVariable}.\n" +
		$"The path should be the folder containing the '{GameInstall.ArchiveFolderName}' directory.");
	return 1;
}
GameInstall.Remember(installRoot);

string scriptPath = args.Length > 1 ? args[1] : MissionLoader.DefaultScriptPath(installRoot);
if (!File.Exists(scriptPath)) {
	Console.Error.WriteLine(
		$"No mission at {scriptPath}.\n" +
		$"Pass one as the second argument — {MissionLoader.ScriptFileName} from the install's " +
		$"{MissionLoader.DataFolderName} folder, with the {MissionLoader.CountersFileName} and " +
		$"{MissionLoader.PlayerFileName} beside it.");
	return 1;
}

Console.WriteLine($"HERCULAN Mission Editor — loading {scriptPath} from {installRoot}");

var content = GameContent.MountSimulator(installRoot);
MissionScene scene;
try {
	scene = MissionScene.Load(content, scriptPath);
} catch (MissingHandoffFileException missing) {
	Console.Error.WriteLine($"Cannot load {scriptPath}: {missing.Message}");
	return 1;
}

var mission = scene.Mission;

Console.WriteLine(
	$"Mission: zone {mission.Header.ZoneIndex}, theater {mission.Header.TheaterIndex}. " +
	$"Placed {scene.Objects.Count} objects, {scene.UnmodelledCount} without a model.");
Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, click to select, Esc quits.");

// The mission's own features, cross-referenced once: nothing moves while the editor shows it.
var selection = new SelectionState();
var index = new MissionIndex(scene, SquadMessages.LoadCommand(content, mission.Header.TrainingMissionNumber));
var drape = new DrapedLines(scene.World.Terrain);
var overlay = new MissionOverlay(index, drape);
var picker = new ScenePicker(scene, index, overlay);
var groupOverlay = new GroupOverlay(index, drape, picker);
var outliner = new MissionOutliner(index, new MissionLint(index), selection);
var properties = new PropertiesPanel(index, selection);

using var window = new EngineWindow($"HERCULAN Mission Editor — zone {mission.Header.ZoneIndex}");

SceneRenderer? renderer = null;
WireframeRenderer? wireframe = null;
ScaledImGui? imgui = null;
GpuMesh? terrainMesh = null;
GpuTexture? terrainTexture = null;
var modelMeshes = new Dictionary<string, GpuMesh>();
var modelCells = new Dictionary<string, GpuMesh[]>();
var modelTextures = new Dictionary<string, GpuTexture>();
var groundMeshes = new Dictionary<string, GpuMesh>();
var disposables = new List<IDisposable>();
SceneItem[]? items = null;

// The terrain with each structure's ground plane painted in its cell order, as the simulator draws
// them — see SceneRenderer.Render. Without it a structure's shadow lies in the terrain's own plane and
// ties with it in the depth buffer.
GroundShapeLayer? groundLayer = null;

// The items of the objects whose group waits on an action, which the settings may stop drawing.
var waitingItems = new List<SceneItem>();
IKeyboard? keyboard = null;
IMouse? mouse = null;

var settings = EditorSettings.Load();
var settingsPanel = new EditorSettingsPanel(settings);

// Where the orientation gizmo sits: inset from the window's top-left corner, below the menu bar, in pixels on a 100%
// display (ScaledImGui.Scaled).
const float CompassSize = 108f;
const float CompassMargin = 12f;

var camera = new Camera();
var editorCamera = new EditorCamera();
editorCamera.ResetTo(scene.Camera.Position, scene.Camera.Heading);

bool looking = false;
Vector2 lastMousePos = Vector2.Zero;

// Set when look mode begins, and cleared by the first frame that samples the cursor afterwards.
// That first sample only re-syncs lastMousePos; it never becomes camera rotation.
//
// Grabbing the pointer (CursorMode.Disabled) warps it to the centre of the window, and the cursor
// position that comes back across that warp is not comparable with the one from before it. GLFW
// accumulates disabled-mode motion as virtual += x - lastCursorPos and resets lastCursorPos to the
// centre as part of the warp, so any WM_MOUSEMOVE already queued when the button went down is still
// carrying a real client position and gets measured against the centre instead — a jump of however
// far the pointer happened to be from the middle of the window. Hence the symptom: the camera snaps
// only when the mouse is already moving as the button goes down, since a still mouse has no queued
// motion to be misread.
bool lookNeedsResync = false;
Vector2 leftDownPos = Vector2.Zero;
bool leftDownOverViewport = false;

string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static", "OpenSans-Regular.ttf");

window.Load += (gl, input) => {
	renderer = new SceneRenderer(gl, editorGrid: true);

	// Same as Herculan.Engine.Host: fog distances, fog colour and the banded sky come off the zone
	// and its theater rather than being hand-picked — see Scene.Atmosphere. Without this the renderer
	// has no sky gradient, so DrawSky paints nothing and the view has no background at all.
	scene.Models.Atmosphere.ApplyTo(renderer);

	// The theater's shaded-surface colours � what a TSShadedPoly is actually drawn through. See
	// SurfaceRampTable.
	renderer.SetShadeRamps(scene.Models.ShadeRamps);
	renderer.SetPaletteRamp(scene.Models.PaletteRamp);

	wireframe = new WireframeRenderer(gl);

	terrainMesh = new GpuMesh(gl, scene.Models.TerrainMesh);
	terrainTexture = scene.Models.TerrainBank != null ? scene.Models.TerrainBank.Atlas.Upload(gl, indexed: true) : null;

	foreach (var model in scene.Models.All) {
		// A shape split by cell — a structure or a flyer — is drawn a piece at a time as the simulator
		// draws it, so that a TSBSPPart's children and a decal's paint layer are painted in order
		// (BspDrawGroup); the flat mesh would leave them to the depth test.
		if (model.Cells.Length > 0) {
			modelCells[model.Key] = model.Cells
				.Select(cell => new GpuMesh(gl, cell.Vertices, cell.TriangleVertexCount, cell.PointVertexCount))
				.ToArray();
		} else {
			modelMeshes[model.Key] = new GpuMesh(gl, model.Mesh, model.TriangleVertexCount, model.PointVertexCount);
		}

		if (model.GroundMesh is { Vertices.Length: > 0 } ground) {
			groundMeshes[model.Key] = new GpuMesh(gl, ground.Vertices, ground.TriangleVertexCount,
				ground.PointVertexCount);
		}

		if (model.Atlas != null) {
			modelTextures[model.Key] = model.Atlas.Upload(gl, indexed: true);
		}
	}

	disposables.AddRange(modelMeshes.Values);
	disposables.AddRange(modelCells.Values.SelectMany(cells => cells));
	disposables.AddRange(groundMeshes.Values);
	disposables.AddRange(modelTextures.Values);

	// The terrain, and the only item the measuring grid is painted onto.
	var built = new List<SceneItem> {
		new(terrainMesh, Matrix4x4.Identity, terrainTexture?.Handle) { ShowGrid = settings.ShowGrid, CellQuantisedFog = true, GroundFill = true }
	};

	// Nothing is filed by cell here, so a ground plane is drawn wherever the camera is, as every object is.
	groundLayer = new GroundShapeLayer(built[0], scene.World.Terrain) { ShapesFollowWalk = false };

	foreach (var sceneObject in scene.Objects) {
		if (sceneObject.Model is not { } model) {
			continue;
		}

		uint? textureHandle = modelTextures.TryGetValue(model.Key, out var texture) ? texture.Handle : null;
		var transform = MissionScene.TransformOf(sceneObject);
		bool waiting = index.IsWaiting(sceneObject);
		if (modelCells.TryGetValue(model.Key, out var cellMeshes)) {
			// What the flat mesh is: every sequence on its first cell and every detail part at its finest
			// level. The ground plane stays with groundMeshes below.
			var groups = new Dictionary<BspTree, BspDrawGroup>();
			for (int i = 0; i < cellMeshes.Length; i++) {
				var cell = model.Cells[i];
				if (cell.Ground || !cell.Gate.VisibleIn(null)
						|| (cell.Gate.Detail is { } detail && cell.Gate.Level != detail.LevelCount - 1)) {
					continue;
				}

				var part = new SceneItem(cellMeshes[i], transform, textureHandle);
				if (cell.Leaf is { } leaf) {
					if (!groups.TryGetValue(leaf.Tree, out var group)) {
						groups[leaf.Tree] = group = BspDrawGroup.AtRest(leaf.Tree, () => transform);
					}

					part.BspGroup = group;
					part.BspLeaf = leaf.Index;
					part.PaintLayer = cell.Layer;
				}

				built.Add(part);
				if (waiting) {
					waitingItems.Add(part);
				}
			}
		} else if (modelMeshes.TryGetValue(model.Key, out var mesh)) {
			var item = new SceneItem(mesh, transform, textureHandle);
			built.Add(item);
			if (waiting) {
				waitingItems.Add(item);
			}
		} else {
			continue;
		}

		// Filed as the simulator's submit files a structure: by its position and its body radius.
		if (groundMeshes.TryGetValue(model.Key, out var groundMesh)) {
			var groundItem = new SceneItem(groundMesh, MissionScene.TransformOf(sceneObject), textureHandle);
			groundLayer.Shapes.Add(new GroundShapeDraw(groundItem, sceneObject.Object.Position,
				sceneObject.Object.HitRadius));
			if (waiting) {
				waitingItems.Add(groundItem);
			}
		}
	}

	items = built.ToArray();
	keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
	mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

	// Same reasoning as Herculan.Engine.Host: DTS geometry isn't reliably wound, so nothing here
	// is backface-culled.
	gl.Disable(Silk.NET.OpenGL.EnableCap.CullFace);

	imgui = new ScaledImGui(gl, window, input, fontPath);

	if (mouse != null) {
		mouse.MouseDown += (m, button) => {
			if (button == MouseButton.Right) {
				if (ImGui.GetIO().WantCaptureMouse) {
					return;
				}

				looking = true;
				lookNeedsResync = true;
				m.Cursor.CursorMode = CursorMode.Disabled;
			} else if (button == MouseButton.Left) {
				leftDownPos = m.Position;
				leftDownOverViewport = !ImGui.GetIO().WantCaptureMouse;
			}
		};

		mouse.MouseUp += (m, button) => {
			if (button == MouseButton.Right) {
				if (looking) {
					looking = false;
					m.Cursor.CursorMode = CursorMode.Normal;
				}
			} else if (button == MouseButton.Left && leftDownOverViewport
					&& Vector2.Distance(m.Position, leftDownPos) < 4f) {
				var size = window.FramebufferSize;
				selection.Current = picker.Pick(camera, m.Position, new Vector2(size.X, size.Y), settings);
				Console.WriteLine($"[pick] {selection.Current?.ToString() ?? "nothing"} at {m.Position}");
			}
		};
	}
};

window.Update += deltaSeconds => {
	imgui?.Update((float)deltaSeconds);

	if (keyboard?.IsKeyPressed(Key.Escape) == true) {
		window.Close();
		return;
	}

	Vector2 lookDelta = Vector2.Zero;
	if (looking && mouse != null) {
		var pos = mouse.Position;
		if (lookNeedsResync) {
			lookNeedsResync = false;
		} else {
			lookDelta = pos - lastMousePos;
		}

		lastMousePos = pos;
	}

	bool acceptKeyboard = imgui == null || !ImGui.GetIO().WantCaptureKeyboard;
	editorCamera.Update(deltaSeconds, keyboard, lookDelta, looking, acceptKeyboard);
	editorCamera.ApplyTo(camera);
};

window.Render += (_, gl) => {
	if (renderer == null || items == null || wireframe == null) {
		return;
	}

	var size = window.FramebufferSize;
	float aspect = (float)size.X / MathF.Max(size.Y, 1);

	// Pushed every frame rather than only when the checkbox moves: the panel edits the settings
	// object directly and Cancel restores it just as directly, so there is no change event to hang
	// this off, and the assignment is a field write.
	renderer.FogEnabled = settings.RenderFog;
	items[0].ShowGrid = settings.ShowGrid;
	foreach (var item in waitingItems) {
		item.Visible = settings.WaitingGroups == WaitingGroupDisplay.Drawn;
	}

	// SceneRenderer.Render deliberately does not clear — the simulator host draws three cockpit
	// panels into one frame, so clearing is the caller's job, once per frame. The editor draws a
	// single panel but still owes the same call: without it the depth buffer keeps the previous
	// frame's values and rejects every triangle from frame two onward.
	renderer.Clear();

	// Full-window viewport: the editor draws one 3D view, unlike the simulator host's three cockpit
	// panels, which is what SceneRenderer.Render's x/y origin exists for.
	renderer.Render(camera, items, groundLayer, 0, 0, size.X, size.Y);

	overlay.Draw(wireframe, camera, aspect, settings, selection);
	groupOverlay.DrawObjects(wireframe, camera, aspect, settings, selection);
	groupOverlay.DrawSelectedGroup(wireframe, camera, aspect, selection);

	var display = ImGui.GetIO().DisplaySize;
	float menuBarHeight = BuildMenuBar();
	float compassMargin = ScaledImGui.Scaled(CompassMargin);
	CompassGizmo.Draw(camera,
		new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth) + compassMargin, menuBarHeight + compassMargin),
		ScaledImGui.Scaled(CompassSize));
	overlay.DrawLabels(camera, settings, selection);
	groupOverlay.DrawLabels(camera, settings, selection);
	outliner.Draw(display, menuBarHeight);
	properties.Draw(display, menuBarHeight);
	settingsPanel.Draw();
	imgui?.Render();
};

window.Closing += () => {
	imgui?.Dispose();
	renderer?.Dispose();
	wireframe?.Dispose();
	terrainMesh?.Dispose();
	terrainTexture?.Dispose();
	foreach (var disposable in disposables) {
		disposable.Dispose();
	}
};

window.Run();

return 0;

// Draws the main menu bar and returns its height, which is what the rest of the frame's overlays
// hang below.
float BuildMenuBar() {
	if (!ImGui.BeginMainMenuBar()) {
		return 0f;
	}

	// The overlay toggles take effect, and are saved, as they are clicked: they are view state, with
	// nothing to confirm.
	if (ImGui.BeginMenu("View")) {
		bool changed = false;
		bool areas = settings.ShowTriggerAreas;
		if (ImGui.MenuItem("Trigger Areas", null, ref areas)) {
			settings.ShowTriggerAreas = areas;
			changed = true;
		}

		bool routes = settings.ShowRoutes;
		if (ImGui.MenuItem("Routes", null, ref routes)) {
			settings.ShowRoutes = routes;
			changed = true;
		}

		bool box = settings.ShowMissionBox;
		if (ImGui.MenuItem("Mission Box", null, ref box)) {
			settings.ShowMissionBox = box;
			changed = true;
		}

		bool pads = settings.ShowBasePads;
		if (ImGui.MenuItem("Base Pads", null, ref pads)) {
			settings.ShowBasePads = pads;
			changed = true;
		}

		if (ImGui.BeginMenu("Waiting Groups")) {
			foreach (var display in Enum.GetValues<WaitingGroupDisplay>()) {
				if (ImGui.MenuItem(display.ToString(), null, settings.WaitingGroups == display)) {
					settings.WaitingGroups = display;
					changed = true;
				}
			}

			ImGui.EndMenu();
		}

		if (changed) {
			settings.Save();
		}

		ImGui.EndMenu();
	}

	// A bare item rather than a menu: there is one entry, and burying it under a "File"-style
	// dropdown would cost a click for nothing.
	if (ImGui.MenuItem("Editor Settings")) {
		settingsPanel.Open();
	}

	float height = ImGui.GetWindowSize().Y;
	ImGui.EndMainMenuBar();
	return height;
}
