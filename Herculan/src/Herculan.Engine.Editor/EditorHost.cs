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
	/// The handoff to open first, read-only, or null for the install's <c>DATA\script.dat</c>. When it is missing or does
	/// not load, the editor opens with no mission and File &gt; Load from VOL up.
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
	private readonly StructureToolbox _toolbox;
	private readonly Camera _camera = new();

	// How much room the top-down view leaves round the zone, as a share of the height that fits it exactly.
	private const float TopDownMargin = 1.05f;

	// The top-down view stands much higher than the free camera's clip planes reach, so while it is up they are pushed
	// out with its height, at the depth ratio below; these are the free camera's own.
	private const float TopDownDepthRatio = 4000f;
	private readonly float _flyNearPlane;
	private readonly float _flyFarPlane;
	private readonly EditorCamera _editorCamera = new();

	// A mission opened from the archives is generated as the shell would generate it, into this scratch directory
	// rather than the install's DATA\, so opening one leaves the install's own handoff alone, and is held as its .MSN,
	// which its edits write. The simulator's settings are still read from DATA\.
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
	private DropPreview? _dropPreview;
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

	// The last click in the viewport, which a second one soon after and close by makes a double-click.
	private long? _lastClickMs;
	private Vector2 _lastClickPos;

	private EditorHost(EditorLaunch launch) {
		_launch = launch;
		_installData = ShellWorkingFiles.DataDirectory(launch.InstallRoot);
		_flyNearPlane = _camera.NearPlane;
		_flyFarPlane = _camera.FarPlane;
		_padPointer = new GamepadPointer { Settings = JoystickMapFile.LoadIn(_installData).Mouse };
		_settingsPanel = new EditorSettingsPanel(_settings);
		_volBrowser = new VolMissionBrowser(() => MissionFileLaunch.List(launch.ShellContent()));
		_toolbox = new StructureToolbox(launch.SimulatorContent);

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
			_mission.Selection.FocusRequested += Frame;
		}

		Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, click to select, "
			+ "double-click to frame, V toggles top-down, drag a selected object's, waypoint's or group's arrows to move it (Alt detaches a waypoint or group from what shares its point), drag a structure from the Toolbox onto the ground to add it, Ctrl+Z/Ctrl+Y undo/redo, "
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
			_drawables = Upload(gl, _renderer, _mission);
		}

		_keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
		if (_keyboard != null) {
			_keyboard.KeyDown += OnKeyDown;
		}
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
			if (_leftDownOverViewport && _mission != null) {
				bool alt = _keyboard != null && (_keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight));
				_mission.Gizmo.TryBegin(_camera, mouse.Position, Viewport, detach: alt);
			}
		}
	}

	private void OnMouseUp(IMouse mouse, MouseButton button) {
		if (button == MouseButton.Right) {
			if (_looking) {
				_looking = false;
				mouse.Cursor.CursorMode = CursorMode.Normal;
			}
		} else if (button == MouseButton.Left && _mission != null) {
			bool click = _leftDownOverViewport && Vector2.Distance(mouse.Position, _leftDownPos) < 4f;
			if (_mission.Gizmo.Dragging) {
				// A press on a handle that moved nothing still counts toward a double-click, whose second press often
				// lands on the handles drawn over what the first selected; it leaves the selection as it is.
				if (!_mission.Gizmo.End() && click && IsDoubleClick(mouse.Position)
						&& _mission.Selection.Current is { } current) {
					_mission.Selection.Focus(current);
				}
			} else if (click) {
				var picked = _mission.Picker.Pick(_camera, mouse.Position, Viewport, _settings);
				if (IsDoubleClick(mouse.Position) && picked != null) {
					_mission.Selection.Focus(picked);
				} else {
					_mission.Selection.Current = picked;
				}

				Console.WriteLine($"[pick] {picked?.ToString() ?? "nothing"} at {mouse.Position}");
			}
		}
	}

	// Whether a click at <position> makes a double-click with the one before, within ImGui's own time and distance;
	// a click that does starts no new pair.
	private bool IsDoubleClick(Vector2 position) {
		var io = ImGui.GetIO();
		long now = Environment.TickCount64;
		bool isDouble = _lastClickMs is { } last && now - last <= io.MouseDoubleClickTime * 1000f
			&& Vector2.Distance(position, _lastClickPos) <= io.MouseDoubleClickMaxDist;
		_lastClickMs = isDouble ? null : now;
		_lastClickPos = position;
		return isDouble;
	}

	// Moves the camera to frame what a double-click chose.
	private void Frame(EditorSelection selection) {
		if (_mission != null && SelectionBounds.Of(_mission, selection) is { } bounds) {
			_editorCamera.Frame(bounds.Centre, bounds.Radius, _camera.FieldOfView,
				_camera.FarPlane * WorldScale.WorldUnitsPerMeter * 0.8f);
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
		SetClipPlanes();

		// After the camera, so the object stays under the pointer while the view moves too.
		if (_mission?.Gizmo.Dragging == true && _mouse != null) {
			_mission.Gizmo.Drag(_camera, _mouse.Position, Viewport);
		}
	}

	// The viewport in the pointer's coordinates, as ScenePicker and MoveGizmo take it.
	private Vector2 Viewport => new(_window.FramebufferSize.X, _window.FramebufferSize.Y);

	private MissionDrawables Upload(GL gl, SceneRenderer renderer, EditorMission mission) {
		var drawables = new MissionDrawables(gl, renderer, mission, _settings);
		mission.Mover.Moved += drawables.Move;
		mission.Mover.TerrainChanged += drawables.UploadTerrain;
		mission.Placer.Added += drawables.Add;
		_dropPreview = new DropPreview(mission, drawables);
		mission.Placer.Removed += drawables.Remove;
		mission.Deleter.Added += drawables.Add;
		mission.Deleter.Removed += drawables.Remove;
		return drawables;
	}

	// V toggles the top-down view, Delete deletes the selected object, Ctrl+Z undoes and Ctrl+Y or Ctrl+Shift+Z redoes,
	// unless a text field has the keyboard, which has its own undo. None of them while a drag is under way.
	private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) {
		if (_mission?.Gizmo.Dragging == true || (_imgui != null && ImGui.GetIO().WantCaptureKeyboard)) {
			return;
		}

		bool control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
		if (key == Key.V && !control) {
			ToggleTopDown();
			return;
		}

		if (key == Key.Delete && !control && _mission?.Selection.Current is ObjectSelection { Object: var selected }) {
			if (_mission.Deleter.Delete(selected) is { } reason) {
				Console.Error.WriteLine($"Cannot delete {MissionText.Describe(selected)}: {reason}");
			}

			return;
		}

		if (_mission == null || !control) {
			return;
		}

		bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
		if (key == Key.Z && !shift) {
			_mission.Mover.Undo();
		} else if (key == Key.Y || (key == Key.Z && shift)) {
			_mission.Mover.Redo();
		}
	}

	// Into the top-down view, high enough over the middle of the zone that its whole grid fits the window; or back out
	// to where the view was.
	private void ToggleTopDown() {
		if (_editorCamera.TopDown) {
			_editorCamera.LeaveTopDown();
			return;
		}

		if (_mission == null) {
			return;
		}

		var terrain = _mission.Scene.World.Terrain;
		var size = _window.FramebufferSize;
		float aspect = (float)size.X / MathF.Max(size.Y, 1);
		float tanHalfHeight = MathF.Tan(_camera.FieldOfView / 2f);
		float height = MathF.Max(terrain.WorldHeight / 2f / tanHalfHeight, terrain.WorldWidth / 2f / (tanHalfHeight * aspect));
		_editorCamera.EnterTopDown(new Vector3(terrain.WorldWidth / 2f, terrain.WorldHeight / 2f, 0f), height * TopDownMargin);
	}

	// The free camera's clip planes, or in the top-down view ones reaching twice its height: past the corners of the
	// ground in view at any window shape the editor is likely to have.
	private void SetClipPlanes() {
		if (!_editorCamera.TopDown) {
			_camera.NearPlane = _flyNearPlane;
			_camera.FarPlane = _flyFarPlane;
			return;
		}

		_camera.FarPlane = MathF.Max(_flyFarPlane, _editorCamera.WorldPosition.Z / WorldScale.WorldUnitsPerMeter * 2f);
		_camera.NearPlane = MathF.Max(_flyNearPlane, _camera.FarPlane / TopDownDepthRatio);
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
		// this off, and the assignment is a field write. The top-down view looks from beyond the fog's reach, where
		// it would bury the whole zone.
		_renderer.FogEnabled = _settings.RenderFog && !_editorCamera.TopDown;

		// SceneRenderer.Render deliberately does not clear — the simulator host draws three cockpit
		// panels into one frame, so clearing is the caller's job, once per frame. The editor draws a
		// single panel but still owes the same call: without it the depth buffer keeps the previous
		// frame's values and rejects every triangle from frame two onward.
		_renderer.Clear();

		var display = ImGui.GetIO().DisplaySize;
		if (_mission != null && _drawables != null) {
			UpdateDropPreview(_mission);
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
			int? structureGroup = SelectedStructureGroup(_mission);
			_toolbox.Draw(display, enabled: !_mission.Gizmo.Dragging && _mission.Placer.WhyNoAdd == null, structureGroup,
				structureGroup is { } joinable ? _mission.Placer.WhyNotJoin(joinable) : null, _mission.Placer.WhyNoAdd);
		}

		_settingsPanel.Draw();
		_volBrowser.Draw();
		_imgui?.Render();

		if (_closePending) {
			_window.Close();
			return;
		}

		// After the frame's ImGui, as a load is: the add changes what the panels list.
		if (_toolbox.TakeRequest() is { } request) {
			AddStructure(request);
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

		mission.Overlay.Highlight(renderer.Highlight, mission.Selection);

		// Full-window viewport: the editor draws one 3D view, unlike the simulator host's three cockpit
		// panels, which is what SceneRenderer.Render's x/y origin exists for.
		renderer.Render(_camera, drawables.Items, drawables.GroundLayer, 0, 0, size.X, size.Y);

		var selection = mission.Selection;
		mission.Overlay.Draw(wireframe, _camera, aspect, _settings, selection);
		mission.GroupOverlay.DrawObjects(wireframe, _camera, aspect, _settings, selection);
		mission.GroupOverlay.DrawSelectedGroup(wireframe, _camera, aspect, selection);
		mission.Gizmo.Draw(wireframe, _camera, aspect, _mouse?.Position ?? new Vector2(-1f), Viewport);
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

	// The structure group the selection is, or a structure of: what a Toolbox add can join. Group 0 is the player's squad.
	private static int? SelectedStructureGroup(EditorMission mission) {
		int group = mission.Selection.Current switch {
			GroupSelection selected => selected.Group,
			ObjectSelection { Object.Placement: var placement } => placement.GroupIndex,
			_ => -1
		};
		return group > 0 && mission.Index.KindOf(group) == MissionUnitKind.Base ? group : null;
	}

	// Stands the structure a Toolbox drag would add on the ground under the pointer, from the drag as the last frame's
	// panels left it; not while the pointer is over a panel, where letting go adds nothing.
	private void UpdateDropPreview(EditorMission mission) {
		var drag = _toolbox.Drag;
		var ground = drag != null && !_toolbox.DragOverPanel && _mouse != null
			? mission.Picker.GroundAt(_camera, _mouse.Position, Viewport)
			: null;
		_dropPreview?.Update(drag, ground);
	}

	// Adds what the toolbox asked for on the ground under the pointer, or at the middle of the view, else under the
	// camera when the middle of the view is sky.
	private void AddStructure(ToolboxRequest request) {
		if (_mission == null || _mission.Gizmo.Dragging) {
			return;
		}

		var picker = _mission.Picker;
		var ground = request.AtPointer
			? picker.GroundAt(_camera, _mouse?.Position ?? Viewport / 2f, Viewport)
			: picker.GroundAt(_camera, Viewport / 2f, Viewport)
				?? new Numerics.Vec3i((int)_editorCamera.WorldPosition.X, (int)_editorCamera.WorldPosition.Y, 0);
		_toolbox.Status = ground is { } at
			? _mission.Placer.Add(request.TypeIndex, request.Side, at.X, at.Y, request.Group)
			: "There is no ground under the pointer.";
	}

	// Generates the handoff for the archive mission <name> into the scratch directory, holds the mission as its .MSN and
	// swaps the editor over to it. On failure the open mission stays, and the browser says why.
	private void LoadFromVol(string name) {
		if (_gl == null || _renderer == null) {
			return;
		}

		EditorMission opened;
		try {
			ClearMissionCache();
			if (MissionFileLaunch.Open(_launch.ShellContent(), _launch.InstallRoot, name, _missionCache, out var source,
					out string? failure) is not { } generated) {
				_volBrowser.Failure = failure;
				Console.Error.WriteLine($"Cannot load {name}: {failure}");
				return;
			}

			opened = EditorMission.Open(_launch.SimulatorContent, generated, Path.GetFileNameWithoutExtension(name), _installData,
				source);
		} catch (Exception ex) when (ex is MissingHandoffFileException or InvalidDataException or IOException
				or UnauthorizedAccessException) {
			_volBrowser.Failure = ex.Message;
			Console.Error.WriteLine($"Cannot load {name}: {ex.Message}");
			return;
		}

		_drawables?.Dispose();
		_mission = opened;
		opened.Selection.FocusRequested += Frame;
		_drawables = Upload(_gl, _renderer, opened);
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
			$"Placed {opened.Scene.Objects.Count} objects, {opened.Scene.UnmodelledCount} without a model."
			+ (opened.Mover.WhyReadOnly is { } readOnly ? $" {readOnly}" : ""));
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

		if (ImGui.BeginMenu("Edit", _mission != null)) {
			if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _mission!.Mover.CanUndo && !_mission.Gizmo.Dragging)) {
				_mission.Mover.Undo();
			}

			if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _mission.Mover.CanRedo && !_mission.Gizmo.Dragging)) {
				_mission.Mover.Redo();
			}

			ImGui.EndMenu();
		}

		// The overlay toggles take effect, and are saved, as they are clicked: they are view state, with
		// nothing to confirm.
		if (ImGui.BeginMenu("View")) {
			// Camera state rather than a setting: not saved.
			if (ImGui.MenuItem("Top-down", "V", _editorCamera.TopDown, _mission != null)) {
				ToggleTopDown();
			}

			ImGui.Separator();

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
