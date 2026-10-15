using System.Numerics;
using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Shell;
using Herculan.Engine.World;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Editor;

/// <summary>How an editor run ended.</summary>
public enum EditorExit {
	/// <summary>Its window was closed, or Esc pressed outside the game.</summary>
	Closed,

	/// <summary>File &gt; Back to game, or Esc pressed in the game's editor.</summary>
	BackToGame,
}

/// <summary>What an editor run opens on.</summary>
/// <param name="InstallRoot">The install the missions and settings come from.</param>
/// <param name="SimulatorContent">DBSIM's mount of the install, which a scene loads from.</param>
/// <param name="ShellContent">VSHELL's mount of the install, which holds the missions; mounted when first asked for.</param>
/// <param name="FontPath">The font every ImGui window is drawn in.</param>
public sealed record EditorLaunch(string InstallRoot, GameContent SimulatorContent, Func<GameContent> ShellContent, string FontPath) {
	/// <summary>
	/// The handoff to open first, or null for the install's <c>DATA\script.dat</c>. When it is missing or does not
	/// load, the editor opens with no mission and File &gt; Load from VOL up.
	/// </summary>
	public string? ScriptPath { get; init; }

	/// <summary>
	/// The game's window, which the editor takes as a turn and leaves open for the game; null for a window of its own,
	/// where the editor last closed on its own (<see cref="EditorSettings.Window"/>), or maximized the first time.
	/// </summary>
	public HostWindow? Window { get; init; }

	/// <summary>Whether the editor was opened from the game, in its window, which File &gt; Back to game returns to.</summary>
	public bool FromGame => Window != null;
}

/// <summary>
/// The mission editor: a second thin host next to Herculan.Engine.Host, sharing every loading and rendering
/// utility but running a different loop — no sim ticking, so placed objects stand still, plus a free editor camera,
/// click-to-select, and an ImGui Properties panel. This is the "possible future mission editor"
/// docs/herculan/planning.md's host/library split was kept open for. It runs on its own
/// (Herculan.Engine.Host.Editor) or as a turn of the game's loop, which its Editor menu item starts.
/// </summary>
public sealed class EditorHost {
	// Where the orientation gizmo sits: inset from the window's top-left corner, below the menu bar, in pixels on a
	// 100% display (ScaledImGui.Scaled).
	private const float CompassSize = 108f;
	private const float CompassMargin = 12f;

	private readonly EditorLaunch _launch;
	private readonly EngineWindow _window;
	private readonly EditorSettings _settings = EditorSettings.Load();
	private readonly EditorSettingsPanel _settingsPanel;
	private readonly VolMissionBrowser _volBrowser;
	private readonly Camera _camera = new();
	private readonly EditorCamera _editorCamera = new();

	// A mission opened from the archives is generated as the shell would generate it, into this scratch directory
	// rather than the install's DATA\, so opening one leaves the install's own handoff alone. The simulator's
	// settings are still read from DATA\.
	private readonly string _installData;
	private readonly string _missionCache = Path.Combine(Path.GetTempPath(), "herculan-editor");

	private EditorMission? _mission;
	private EditorExit _exit = EditorExit.Closed;

	// Set by Close, which shuts the window once the frame's ImGui is rendered: the window's Closing runs as soon as
	// it is asked to close and disposes the ImGui context, so closing from inside a menu, or between ImGui's new
	// frame and its render, would leave the rest of the frame calling into a destroyed context.
	private bool _closePending;

	// A load the browser asked for. It runs at the start of the frame after the one that drew the browser's
	// "Loading" line, so the window says what it is doing before it stops to do it.
	private string? _pendingLoad;
	private bool _loadNoticeDrawn;

	private GL? _gl;
	private SceneRenderer? _renderer;
	private WireframeRenderer? _wireframe;
	private ScaledImGui? _imgui;
	private MissionDrawables? _drawables;
	private IKeyboard? _keyboard;
	private IMouse? _mouse;
	private IInputContext? _input;

	// A gamepad as the mouse: a stick moves the pointer, one trigger is the left button and the other the right, as the
	// install's herculan-joystick.cfg chooses (docs/herculan/herculan-key-bindings.md, "Gamepad in the editor"). What each trigger was last frame, so
	// its press and release reach the viewport as the button's own would; ImGui takes them through
	// ScaledImGui.HeldAsMouseButtons. The pad works only while the window has the focus, as GLFW reads it regardless.
	private readonly GamepadPointer _padPointer;
	private bool _padLeftHeld;
	private bool _padRightHeld;
	private bool _focused = true;

	private bool _looking;
	private Vector2 _lastMousePos;

	// Set when look mode begins, and cleared by the first frame that samples the cursor afterwards.
	// That first sample only re-syncs _lastMousePos; it never becomes camera rotation.
	//
	// Grabbing the pointer (CursorMode.Disabled) warps it to the centre of the window, and the cursor
	// position that comes back across that warp is not comparable with the one from before it. GLFW
	// accumulates disabled-mode motion as virtual += x - lastCursorPos and resets lastCursorPos to the
	// centre as part of the warp, so any WM_MOUSEMOVE already queued when the button went down is still
	// carrying a real client position and gets measured against the centre instead — a jump of however
	// far the pointer happened to be from the middle of the window. Hence the symptom: the camera snaps
	// only when the mouse is already moving as the button goes down, since a still mouse has no queued
	// motion to be misread.
	private bool _lookNeedsResync;
	private Vector2 _leftDownPos;
	private bool _leftDownOverViewport;

	private EditorHost(EditorLaunch launch) {
		_launch = launch;
		_installData = ShellWorkingFiles.DataDirectory(launch.InstallRoot);
		_padPointer = new GamepadPointer { Settings = JoystickMapFile.LoadIn(_installData).Mouse };
		_settingsPanel = new EditorSettingsPanel(_settings);
		_volBrowser = new VolMissionBrowser(() => MissionFileLaunch.List(launch.ShellContent()));

		string scriptPath = launch.ScriptPath ?? MissionLoader.DefaultScriptPath(launch.InstallRoot);
		Console.WriteLine($"HERCULAN Mission Editor — loading {scriptPath} from {launch.InstallRoot}");
		try {
			_mission = EditorMission.Open(launch.SimulatorContent, scriptPath, Path.GetFileName(scriptPath));
		} catch (Exception ex) when (ex is MissingHandoffFileException or FileNotFoundException or DirectoryNotFoundException
				or InvalidDataException) {
			Console.Error.WriteLine($"Cannot load {scriptPath}: {ex.Message}");
			_volBrowser.Open();
			_volBrowser.Failure = $"No mission open: {ex.Message}";
		}

		if (_mission != null) {
			ReportLoaded(_mission);
			_editorCamera.ResetTo(_mission.Scene.Camera.Position, _mission.Scene.Camera.Heading);
		}

		Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, click to select, "
			+ (launch.FromGame ? "Esc returns to the game." : "Esc quits."));

		_window = launch.Window is { } window
			? new EngineWindow(window, WindowTitle)
			: new EngineWindow(WindowTitle, placement: _settings.Window?.ToPlacement(), maximized: true);
		_window.Load += OnLoad;
		_window.Update += OnUpdate;
		_window.Render += OnRender;
		_window.Closing += OnClosing;
		_window.FocusChanged += focused => _focused = focused;
	}

	/// <summary>Runs the editor until its window closes or its turn in the game's ends, and returns how it ended.</summary>
	public static EditorExit Run(EditorLaunch launch) {
		var host = new EditorHost(launch);
		using var window = host._window;
		window.Run();

		// Where its own window was, written into the file as it stands rather than through the open settings, which may
		// hold the Settings panel's edits that were never confirmed.
		if (!launch.FromGame) {
			var saved = EditorSettings.Load();
			saved.Window = SavedWindow.From(window.Placement);
			saved.Save();
		}

		return host._exit;
	}

	private string WindowTitle => _mission?.WindowTitle ?? "HERCULAN Mission Editor";

	private void OnLoad(GL gl, IInputContext input) {
		_gl = gl;
		_renderer = new SceneRenderer(gl, editorGrid: true);
		_wireframe = new WireframeRenderer(gl);
		if (_mission != null) {
			_drawables = new MissionDrawables(gl, _renderer, _mission, _settings);
		}

		_keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
		_mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

		// Same reasoning as Herculan.Engine.Host: DTS geometry isn't reliably wound, so nothing here
		// is backface-culled.
		gl.Disable(EnableCap.CullFace);

		_imgui = new ScaledImGui(gl, _window, input, _launch.FontPath) {
			HeldAsMouseButtons = () => (_padPointer.ClickHeld, _padPointer.RightClickHeld),
		};
		_input = input;

		if (_mouse != null) {
			_mouse.MouseDown += OnMouseDown;
			_mouse.MouseUp += OnMouseUp;
		}
	}

	private void OnMouseDown(IMouse mouse, MouseButton button) {
		if (button == MouseButton.Right) {
			if (ImGui.GetIO().WantCaptureMouse) {
				return;
			}

			_looking = true;
			_lookNeedsResync = true;
			mouse.Cursor.CursorMode = CursorMode.Disabled;
		} else if (button == MouseButton.Left) {
			_leftDownPos = mouse.Position;
			_leftDownOverViewport = !ImGui.GetIO().WantCaptureMouse;
		}
	}

	private void OnMouseUp(IMouse mouse, MouseButton button) {
		if (button == MouseButton.Right) {
			if (_looking) {
				_looking = false;
				mouse.Cursor.CursorMode = CursorMode.Normal;
			}
		} else if (button == MouseButton.Left && _leftDownOverViewport && _mission != null
				&& Vector2.Distance(mouse.Position, _leftDownPos) < 4f) {
			var size = _window.FramebufferSize;
			_mission.Selection.Current = _mission.Picker.Pick(_camera, mouse.Position, new Vector2(size.X, size.Y), _settings);
			Console.WriteLine($"[pick] {_mission.Selection.Current?.ToString() ?? "nothing"} at {mouse.Position}");
		}
	}

	private void OnUpdate(double deltaSeconds) {
		// Before ImGui's frame, which reads the pointer and the triggers. Unconfined while looking, when the cursor is
		// grabbed and the stick turns the view.
		if (_input != null) {
			_padPointer.Update(_input, _mouse, _window.ClientSize, deltaSeconds, active: _focused, confine: !_looking);
		}

		_imgui?.Update((float)deltaSeconds);

		if (_mouse != null) {
			PadButton(_padPointer.ClickHeld, ref _padLeftHeld, MouseButton.Left);
			PadButton(_padPointer.RightClickHeld, ref _padRightHeld, MouseButton.Right);
		}

		if (_keyboard?.IsKeyPressed(Key.Escape) == true) {
			Close(_launch.FromGame ? EditorExit.BackToGame : EditorExit.Closed);
			return;
		}

		Vector2 lookDelta = Vector2.Zero;
		if (_looking && _mouse != null) {
			var pos = _mouse.Position;
			if (_lookNeedsResync) {
				_lookNeedsResync = false;
			} else {
				lookDelta = pos - _lastMousePos;
			}

			_lastMousePos = pos;
		}

		bool acceptKeyboard = _imgui == null || !ImGui.GetIO().WantCaptureKeyboard;
		_editorCamera.Update(deltaSeconds, _keyboard, lookDelta, _looking, acceptKeyboard);
		_editorCamera.ApplyTo(_camera);
	}

	// A trigger going down or up, as the mouse button it stands for: a left press and release selects, a right one looks.
	private void PadButton(bool held, ref bool wasHeld, MouseButton button) {
		if (held == wasHeld) {
			return;
		}

		wasHeld = held;
		if (held) {
			OnMouseDown(_mouse!, button);
		} else {
			OnMouseUp(_mouse!, button);
		}
	}

	private void OnRender(double deltaSeconds, GL gl) {
		if (_pendingLoad != null && _loadNoticeDrawn) {
			LoadFromVol(_pendingLoad);
			_pendingLoad = null;
			_loadNoticeDrawn = false;
			_volBrowser.Loading = null;
		}

		if (_renderer == null || _wireframe == null) {
			return;
		}

		// Pushed every frame rather than only when the checkbox moves: the panel edits the settings
		// object directly and Cancel restores it just as directly, so there is no change event to hang
		// this off, and the assignment is a field write.
		_renderer.FogEnabled = _settings.RenderFog;

		// SceneRenderer.Render deliberately does not clear — the simulator host draws three cockpit
		// panels into one frame, so clearing is the caller's job, once per frame. The editor draws a
		// single panel but still owes the same call: without it the depth buffer keeps the previous
		// frame's values and rejects every triangle from frame two onward.
		_renderer.Clear();

		var display = ImGui.GetIO().DisplaySize;
		if (_mission != null && _drawables != null) {
			DrawMission(_mission, _drawables, _renderer, _wireframe);
		}

		float menuBarHeight = BuildMenuBar();
		if (_mission != null) {
			float compassMargin = ScaledImGui.Scaled(CompassMargin);
			CompassGizmo.Draw(_camera,
				new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth) + compassMargin, menuBarHeight + compassMargin),
				ScaledImGui.Scaled(CompassSize));
			_mission.Overlay.DrawLabels(_camera, _settings, _mission.Selection);
			_mission.GroupOverlay.DrawLabels(_camera, _settings, _mission.Selection);
			_mission.Outliner.Draw(display, menuBarHeight);
			_mission.Properties.Draw(display, menuBarHeight);
		}

		_settingsPanel.Draw();
		_volBrowser.Draw();
		_imgui?.Render();

		if (_closePending) {
			_window.Close();
			return;
		}

		if (_pendingLoad != null) {
			_loadNoticeDrawn = true;
		} else if (_volBrowser.TakeRequest() is { } requested) {
			_pendingLoad = requested;
			_volBrowser.Loading = requested;
		}
	}

	private void DrawMission(EditorMission mission, MissionDrawables drawables, SceneRenderer renderer,
			WireframeRenderer wireframe) {
		var size = _window.FramebufferSize;
		float aspect = (float)size.X / MathF.Max(size.Y, 1);

		drawables.Items[0].ShowGrid = _settings.ShowGrid;
		foreach (var item in drawables.WaitingItems) {
			item.Visible = _settings.WaitingGroups == WaitingGroupDisplay.Drawn;
		}

		// Full-window viewport: the editor draws one 3D view, unlike the simulator host's three cockpit
		// panels, which is what SceneRenderer.Render's x/y origin exists for.
		renderer.Render(_camera, drawables.Items, drawables.GroundLayer, 0, 0, size.X, size.Y);

		var selection = mission.Selection;
		mission.Overlay.Draw(wireframe, _camera, aspect, _settings, selection);
		mission.GroupOverlay.DrawObjects(wireframe, _camera, aspect, _settings, selection);
		mission.GroupOverlay.DrawSelectedGroup(wireframe, _camera, aspect, selection);
	}

	private void OnClosing() {
		_imgui?.Dispose();
		_drawables?.Dispose();
		_renderer?.Dispose();
		_wireframe?.Dispose();
	}

	private void Close(EditorExit exit) {
		_exit = exit;
		_closePending = true;
	}

	// Generates the handoff for the archive mission <name> into the scratch directory and swaps the editor over to
	// it. On failure the open mission stays, and the browser says why.
	private void LoadFromVol(string name) {
		if (_gl == null || _renderer == null) {
			return;
		}

		EditorMission opened;
		try {
			ClearMissionCache();
			if (MissionFileLaunch.Write(_launch.ShellContent(), _launch.InstallRoot, name, _missionCache, out string? failure)
					is not { } generated) {
				_volBrowser.Failure = failure;
				Console.Error.WriteLine($"Cannot load {name}: {failure}");
				return;
			}

			opened = EditorMission.Open(_launch.SimulatorContent, generated, Path.GetFileNameWithoutExtension(name), _installData);
		} catch (Exception ex) when (ex is MissingHandoffFileException or InvalidDataException or IOException
				or UnauthorizedAccessException) {
			_volBrowser.Failure = ex.Message;
			Console.Error.WriteLine($"Cannot load {name}: {ex.Message}");
			return;
		}

		_drawables?.Dispose();
		_mission = opened;
		_drawables = new MissionDrawables(_gl, _renderer, opened, _settings);
		_editorCamera.ResetTo(opened.Scene.Camera.Position, opened.Scene.Camera.Heading);
		_window.Title = WindowTitle;
		_volBrowser.Close();
		ReportLoaded(opened);
	}

	// Removes the last generated handoff, since the shell's writer does not truncate (ShellWorkingFiles.WriteMissionFile)
	// and a shorter mission would otherwise keep the longer one's tail.
	private void ClearMissionCache() {
		foreach (string file in new[] {
				MissionLoader.ScriptFileName, MissionLoader.TextFileName, MissionLoader.PlayerFileName,
				MissionLoader.CountersFileName }) {
			string path = CaseInsensitivePath.Combine(_missionCache, file);
			if (File.Exists(path)) {
				File.Delete(path);
			}
		}
	}

	private static void ReportLoaded(EditorMission opened) {
		Console.WriteLine(
			$"Mission {opened.Name}: zone {opened.Mission.Header.ZoneIndex}, theater {opened.Mission.Header.TheaterIndex}. " +
			$"Placed {opened.Scene.Objects.Count} objects, {opened.Scene.UnmodelledCount} without a model.");
	}

	// Draws the main menu bar and returns its height, which is what the rest of the frame's overlays
	// hang below.
	private float BuildMenuBar() {
		if (!ImGui.BeginMainMenuBar()) {
			return 0f;
		}

		if (ImGui.BeginMenu("File")) {
			if (ImGui.MenuItem("Load from VOL", null, false, _pendingLoad == null)) {
				_volBrowser.Open();
			}

			// Outside the game there is no game to go back to.
			if (ImGui.MenuItem("Back to game", null, false, _launch.FromGame)) {
				Close(EditorExit.BackToGame);
			}

			ImGui.EndMenu();
		}

		// The overlay toggles take effect, and are saved, as they are clicked: they are view state, with
		// nothing to confirm.
		if (ImGui.BeginMenu("View")) {
			bool changed = false;
			bool areas = _settings.ShowTriggerAreas;
			if (ImGui.MenuItem("Trigger Areas", null, ref areas)) {
				_settings.ShowTriggerAreas = areas;
				changed = true;
			}

			bool routes = _settings.ShowRoutes;
			if (ImGui.MenuItem("Routes", null, ref routes)) {
				_settings.ShowRoutes = routes;
				changed = true;
			}

			bool box = _settings.ShowMissionBox;
			if (ImGui.MenuItem("Mission Box", null, ref box)) {
				_settings.ShowMissionBox = box;
				changed = true;
			}

			bool pads = _settings.ShowBasePads;
			if (ImGui.MenuItem("Base Pads", null, ref pads)) {
				_settings.ShowBasePads = pads;
				changed = true;
			}

			if (ImGui.BeginMenu("Waiting Groups")) {
				foreach (var display in Enum.GetValues<WaitingGroupDisplay>()) {
					if (ImGui.MenuItem(display.ToString(), null, _settings.WaitingGroups == display)) {
						_settings.WaitingGroups = display;
						changed = true;
					}
				}

				ImGui.EndMenu();
			}

			if (changed) {
				_settings.Save();
			}

			ImGui.EndMenu();
		}

		// A bare item rather than a menu: there is one entry, and burying it under a dropdown would cost a
		// click for nothing.
		if (ImGui.MenuItem("Editor Settings")) {
			_settingsPanel.Open();
		}

		float height = ImGui.GetWindowSize().Y;
		ImGui.EndMainMenuBar();
		return height;
	}
}
