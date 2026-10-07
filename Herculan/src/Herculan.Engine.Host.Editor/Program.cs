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

// Pickable set: everything drawn, i.e. everything with a model — matches what the renderer
// actually puts on screen. Objects don't move in the editor, so this is computed once.
//
// Deliberately NOT SceneModel.ShapeRadius: that is the shape file's own radius about the model's
// origin, which sits near a mech's base, so a sphere of it there need not cover the torso, head or
// raised arms. Instead this computes each model's own bounding-sphere radius from its mesh once (cached per model key,
// several objects share a model) and transforms the sphere's center by the object's full
// rotation+translation — a sphere is rotation-invariant, so the local-space radius stays correct
// after that.
var modelBounds = new Dictionary<string, (Vector3 LocalCenter, float Radius)>();
var pickables = scene.Objects
	.Where(o => o.Model != null)
	.Select(o => {
		var model = o.Model!;
		if (!modelBounds.TryGetValue(model.Key, out var bounds)) {
			bounds = ComputeBounds(model.Mesh);
			modelBounds[model.Key] = bounds;
		}

		Vector3 worldCenter = Vector3.Transform(bounds.LocalCenter, MissionScene.TransformOf(o));
		return new Pickable(o, worldCenter, MathF.Max(bounds.Radius, 1f));
	})
	.ToArray();

static (Vector3 Center, float Radius) ComputeBounds(MeshVertex[] mesh) {
	if (mesh.Length == 0) {
		return (Vector3.Zero, 1f);
	}

	Vector3 min = mesh[0].Position;
	Vector3 max = mesh[0].Position;
	foreach (var vertex in mesh) {
		min = Vector3.Min(min, vertex.Position);
		max = Vector3.Max(max, vertex.Position);
	}

	return ((min + max) * 0.5f, Vector3.Distance(min, max) * 0.5f);
}

using var window = new EngineWindow($"HERCULAN Mission Editor — zone {mission.Header.ZoneIndex}");

SceneRenderer? renderer = null;
WireframeRenderer? wireframe = null;
ScaledImGui? imgui = null;
GpuMesh? terrainMesh = null;
GpuTexture? terrainTexture = null;
var modelMeshes = new Dictionary<string, GpuMesh>();
var modelTextures = new Dictionary<string, GpuTexture>();
var disposables = new List<IDisposable>();
SceneItem[]? items = null;
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

SceneObject? selected = null;
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
	scene.Atmosphere.ApplyTo(renderer);

	// The theater's shaded-surface colours � what a TSShadedPoly is actually drawn through. See
	// SurfaceRampTable.
	renderer.SetShadeRamps(scene.ShadeRamps);
	renderer.SetPaletteRamp(scene.PaletteRamp);

	wireframe = new WireframeRenderer(gl);

	terrainMesh = new GpuMesh(gl, scene.TerrainMesh);
	terrainTexture = scene.TerrainBank != null ? scene.TerrainBank.Atlas.Upload(gl, indexed: true) : null;

	foreach (var model in scene.Models) {
		modelMeshes[model.Key] = new GpuMesh(gl, model.Mesh, model.TriangleVertexCount, model.PointVertexCount);
		if (model.Atlas != null) {
			modelTextures[model.Key] = model.Atlas.Upload(gl, indexed: true);
		}
	}

	disposables.AddRange(modelMeshes.Values);
	disposables.AddRange(modelTextures.Values);

	// The terrain, and the only item the measuring grid is painted onto.
	var built = new List<SceneItem> {
		new(terrainMesh, Matrix4x4.Identity, terrainTexture?.Handle) { ShowGrid = settings.ShowGrid, CellQuantisedFog = true, GroundFill = true }
	};

	foreach (var sceneObject in scene.Objects) {
		if (sceneObject.Model is not { } model || !modelMeshes.TryGetValue(model.Key, out var mesh)) {
			continue;
		}

		built.Add(new SceneItem(mesh, MissionScene.TransformOf(sceneObject),
			modelTextures.TryGetValue(model.Key, out var texture) ? texture.Handle : null));
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
				selected = Pick(m.Position);
				Console.WriteLine(selected != null
					? $"[pick] selected {selected.Placement.TypeName ?? selected.Placement.Kind.ToString()} at {m.Position}"
					: $"[pick] no hit at {m.Position} ({pickables.Length} pickable objects)");
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

	// SceneRenderer.Render deliberately does not clear — the simulator host draws three cockpit
	// panels into one frame, so clearing is the caller's job, once per frame. The editor draws a
	// single panel but still owes the same call: without it the depth buffer keeps the previous
	// frame's values and rejects every triangle from frame two onward.
	renderer.Clear();

	// Full-window viewport: the editor draws one 3D view, unlike the simulator host's three cockpit
	// panels, which is what SceneRenderer.Render's x/y origin exists for.
	renderer.Render(camera, items, 0, 0, size.X, size.Y);

	if (selected is { } sel) {
		var picked = Array.Find(pickables, p => p.SceneObject == sel);
		if (picked != null) {
			wireframe.DrawBox(camera, picked.CenterRender, picked.RadiusRender, new Vector3(1f, 0.85f, 0.1f), aspect);
		}
	}

	float menuBarHeight = BuildMenuBar();
	float compassMargin = ScaledImGui.Scaled(CompassMargin);
	CompassGizmo.Draw(camera, new Vector2(compassMargin, menuBarHeight + compassMargin), ScaledImGui.Scaled(CompassSize));
	BuildPropertiesPanel(size.X, size.Y, menuBarHeight, selected);
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

SceneObject? Pick(Vector2 screenPos) {
	var size = window.FramebufferSize;
	float ndcX = screenPos.X / size.X * 2f - 1f;
	float ndcY = 1f - screenPos.Y / size.Y * 2f;
	float aspect = (float)size.X / MathF.Max(size.Y, 1);
	var (origin, direction) = camera.ViewportPointToRay(new Vector2(ndcX, ndcY), aspect);

	SceneObject? best = null;
	float bestDistance = float.MaxValue;
	foreach (var pickable in pickables) {
		if (RaySphere(origin, direction, pickable.CenterRender, pickable.RadiusRender, out float t) && t < bestDistance) {
			bestDistance = t;
			best = pickable.SceneObject;
		}
	}

	return best;
}

static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t) {
	Vector3 toCenter = origin - center;
	float b = Vector3.Dot(toCenter, direction);
	float c = Vector3.Dot(toCenter, toCenter) - radius * radius;
	float discriminant = b * b - c;
	if (discriminant < 0f) {
		t = 0f;
		return false;
	}

	float sqrtDiscriminant = MathF.Sqrt(discriminant);
	float near = -b - sqrtDiscriminant;
	t = near >= 0f ? near : -b + sqrtDiscriminant;
	return t >= 0f;
}

// Draws the main menu bar and returns its height, which is what the rest of the frame's overlays
// hang below.
float BuildMenuBar() {
	if (!ImGui.BeginMainMenuBar()) {
		return 0f;
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

void BuildPropertiesPanel(int width, int height, float menuBarHeight, SceneObject? sel) {
	float panelWidth = ScaledImGui.Scaled(320f);

	ImGui.SetNextWindowPos(new Vector2(width - panelWidth, menuBarHeight));
	ImGui.SetNextWindowSize(new Vector2(panelWidth, height - menuBarHeight));
	ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

	if (sel != null) {
		ImGui.TextWrapped(sel.Placement.TypeName ?? $"{sel.Placement.Kind} #{sel.Placement.TypeIndex}");
		ImGui.Separator();
		ImGui.Text($"Kind: {sel.Placement.Kind}");
		ImGui.Text($"Type index: {sel.Placement.TypeIndex}");

		if (sel.Model is { } model) {
			ImGui.Text($"Model: {model.Key}");
			ImGui.Text($"Triangles: {model.TriangleVertexCount / 3}");
			ImGui.Text(model.Atlas is { } atlas
				? $"Texture: {atlas.FrameCount} frames ({atlas.Width}x{atlas.Height})"
				: "Texture: none");
		}

		ImGui.Separator();
		var pos = sel.Object.Position;
		ImGui.Text($"Position: {pos.X}, {pos.Y}, {pos.Z} units");
		ImGui.Text($"          {pos.X / WorldScale.WorldUnitsPerMeter:F1}, {pos.Y / WorldScale.WorldUnitsPerMeter:F1}, " +
			$"{pos.Z / WorldScale.WorldUnitsPerMeter:F1} m");
		ImGui.Text($"Heading: {BinaryAngle.ToRadians(sel.Object.Heading) * (180f / MathF.PI):F1} deg");
		ImGui.Text($"Hit radius: {sel.Object.HitRadius} units");
	} else {
		ImGui.TextWrapped("No object selected. Click a mech, flyer, or building in the scene.");
	}

	ImGui.End();
}

/// <summary>A placed, drawable object plus its cached render-space pick sphere.</summary>
internal sealed record Pickable(SceneObject SceneObject, Vector3 CenterRender, float RadiusRender);
