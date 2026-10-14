using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using System.Numerics;
using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Editor;
using Herculan.Engine.Render;
using Herculan.Engine.Shell;
using Herculan.Engine.World;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;

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
EditorMission mission;
try {
	mission = EditorMission.Open(content, scriptPath, Path.GetFileName(scriptPath));
} catch (MissingHandoffFileException missing) {
	Console.Error.WriteLine($"Cannot load {scriptPath}: {missing.Message}");
	return 1;
}

ReportLoaded(mission);
Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, click to select, Esc quits.");

// A mission opened from the archives is generated as the shell would generate it, into this scratch directory
// rather than the install's DATA\, so opening one leaves the install's own handoff alone. The simulator's
// settings are still read from DATA\.
string installData = ShellWorkingFiles.DataDirectory(installRoot);
string missionCache = Path.Combine(Path.GetTempPath(), "herculan-editor");

// VSHELL's mount, which holds the career table and the missions: mounted the first time File > Load from VOL
// opens, since a session that never uses it has no need of it.
GameContent? shellContent = null;
GameContent ShellContent() => shellContent ??= GameContent.MountShell(installRoot);
var volBrowser = new VolMissionBrowser(() => MissionFileLaunch.List(ShellContent()));

// A load the browser asked for. It runs at the start of the frame after the one that drew the browser's
// "Loading" line, so the window says what it is doing before it stops to do it.
string? pendingLoad = null;
bool loadNoticeDrawn = false;

using var window = new EngineWindow(mission.WindowTitle);

GL? glContext = null;
SceneRenderer? renderer = null;
WireframeRenderer? wireframe = null;
ScaledImGui? imgui = null;
MissionDrawables? drawables = null;
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
editorCamera.ResetTo(mission.Scene.Camera.Position, mission.Scene.Camera.Heading);

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
	glContext = gl;
	renderer = new SceneRenderer(gl, editorGrid: true);
	wireframe = new WireframeRenderer(gl);
	drawables = new MissionDrawables(gl, renderer, mission, settings);

	keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
	mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

	// Same reasoning as Herculan.Engine.Host: DTS geometry isn't reliably wound, so nothing here
	// is backface-culled.
	gl.Disable(EnableCap.CullFace);

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
				mission.Selection.Current = mission.Picker.Pick(camera, m.Position, new Vector2(size.X, size.Y), settings);
				Console.WriteLine($"[pick] {mission.Selection.Current?.ToString() ?? "nothing"} at {m.Position}");
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
	if (pendingLoad != null && loadNoticeDrawn) {
		LoadFromVol(pendingLoad);
		pendingLoad = null;
		loadNoticeDrawn = false;
		volBrowser.Loading = null;
	}

	if (renderer == null || drawables == null || wireframe == null) {
		return;
	}

	var size = window.FramebufferSize;
	float aspect = (float)size.X / MathF.Max(size.Y, 1);

	// Pushed every frame rather than only when the checkbox moves: the panel edits the settings
	// object directly and Cancel restores it just as directly, so there is no change event to hang
	// this off, and the assignment is a field write.
	renderer.FogEnabled = settings.RenderFog;
	drawables.Items[0].ShowGrid = settings.ShowGrid;
	foreach (var item in drawables.WaitingItems) {
		item.Visible = settings.WaitingGroups == WaitingGroupDisplay.Drawn;
	}

	// SceneRenderer.Render deliberately does not clear — the simulator host draws three cockpit
	// panels into one frame, so clearing is the caller's job, once per frame. The editor draws a
	// single panel but still owes the same call: without it the depth buffer keeps the previous
	// frame's values and rejects every triangle from frame two onward.
	renderer.Clear();

	// Full-window viewport: the editor draws one 3D view, unlike the simulator host's three cockpit
	// panels, which is what SceneRenderer.Render's x/y origin exists for.
	renderer.Render(camera, drawables.Items, drawables.GroundLayer, 0, 0, size.X, size.Y);

	var selection = mission.Selection;
	mission.Overlay.Draw(wireframe, camera, aspect, settings, selection);
	mission.GroupOverlay.DrawObjects(wireframe, camera, aspect, settings, selection);
	mission.GroupOverlay.DrawSelectedGroup(wireframe, camera, aspect, selection);

	var display = ImGui.GetIO().DisplaySize;
	float menuBarHeight = BuildMenuBar();
	float compassMargin = ScaledImGui.Scaled(CompassMargin);
	CompassGizmo.Draw(camera,
		new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth) + compassMargin, menuBarHeight + compassMargin),
		ScaledImGui.Scaled(CompassSize));
	mission.Overlay.DrawLabels(camera, settings, selection);
	mission.GroupOverlay.DrawLabels(camera, settings, selection);
	mission.Outliner.Draw(display, menuBarHeight);
	mission.Properties.Draw(display, menuBarHeight);
	settingsPanel.Draw();
	volBrowser.Draw();
	imgui?.Render();

	if (pendingLoad != null) {
		loadNoticeDrawn = true;
	} else if (volBrowser.TakeRequest() is { } requested) {
		pendingLoad = requested;
		volBrowser.Loading = requested;
	}
};

window.Closing += () => {
	imgui?.Dispose();
	drawables?.Dispose();
	renderer?.Dispose();
	wireframe?.Dispose();
};

window.Run();

return 0;

// Generates the handoff for the archive mission <name> into the scratch directory and swaps the editor over to
// it. On failure the open mission stays, and the browser says why.
void LoadFromVol(string name) {
	if (glContext == null || renderer == null) {
		return;
	}

	EditorMission opened;
	try {
		ClearMissionCache();
		if (MissionFileLaunch.Write(ShellContent(), installRoot, name, missionCache, out string? failure) is not { } generated) {
			volBrowser.Failure = failure;
			Console.Error.WriteLine($"Cannot load {name}: {failure}");
			return;
		}

		opened = EditorMission.Open(content, generated, Path.GetFileNameWithoutExtension(name), installData);
	} catch (Exception ex) when (ex is MissingHandoffFileException or InvalidDataException or IOException
			or UnauthorizedAccessException) {
		volBrowser.Failure = ex.Message;
		Console.Error.WriteLine($"Cannot load {name}: {ex.Message}");
		return;
	}

	drawables?.Dispose();
	mission = opened;
	drawables = new MissionDrawables(glContext, renderer, mission, settings);
	editorCamera.ResetTo(mission.Scene.Camera.Position, mission.Scene.Camera.Heading);
	window.Title = mission.WindowTitle;
	volBrowser.Close();
	ReportLoaded(mission);
}

// Removes the last generated handoff, since the shell's writer does not truncate (ShellWorkingFiles.WriteMissionFile)
// and a shorter mission would otherwise keep the longer one's tail.
void ClearMissionCache() {
	foreach (string file in new[] {
			MissionLoader.ScriptFileName, MissionLoader.TextFileName, MissionLoader.PlayerFileName,
			MissionLoader.CountersFileName }) {
		string path = CaseInsensitivePath.Combine(missionCache, file);
		if (File.Exists(path)) {
			File.Delete(path);
		}
	}
}

static void ReportLoaded(EditorMission opened) {
	Console.WriteLine(
		$"Mission {opened.Name}: zone {opened.Mission.Header.ZoneIndex}, theater {opened.Mission.Header.TheaterIndex}. " +
		$"Placed {opened.Scene.Objects.Count} objects, {opened.Scene.UnmodelledCount} without a model.");
}

// Draws the main menu bar and returns its height, which is what the rest of the frame's overlays
// hang below.
float BuildMenuBar() {
	if (!ImGui.BeginMainMenuBar()) {
		return 0f;
	}

	if (ImGui.BeginMenu("File")) {
		if (ImGui.MenuItem("Load from VOL", null, false, pendingLoad == null)) {
			volBrowser.Open();
		}

		ImGui.EndMenu();
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

	// A bare item rather than a menu: there is one entry, and burying it under a dropdown would cost a
	// click for nothing.
	if (ImGui.MenuItem("Editor Settings")) {
		settingsPanel.Open();
	}

	float height = ImGui.GetWindowSize().Y;
	ImGui.EndMainMenuBar();
	return height;
}
