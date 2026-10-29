using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
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

	/// <summary>Play, in the game's editor: the game plays <see cref="EditorResult.Play"/> and opens the editor on it again.</summary>
	Play,
}

/// <summary>The mission Play asks the game to play: its saved files' name, and the handoff written for it.</summary>
/// <param name="Mission">The <c>.MSN</c> name, which the editor opens again once the mission ends.</param>
/// <param name="Launch">The handoff, written into a scratch folder with the install's <c>DATA\</c> for the simulator's settings.</param>
public sealed record EditorPlay(string Mission, ShellLaunch Launch);

/// <summary>How an editor run ended, and for <see cref="EditorExit.Play"/> what to play.</summary>
public sealed record EditorResult(EditorExit Exit, EditorPlay? Play);

/// <summary>What an editor run opens on.</summary>
/// <param name="InstallRoot">The install the missions and settings come from.</param>
/// <param name="SimulatorContent">DBSIM's mount of the install, which a scene loads from.</param>
/// <param name="ShellContent">VSHELL's mount of the install, which holds the missions; mounted when first asked for.</param>
/// <param name="FontPath">The font every ImGui window is drawn in.</param>
public sealed record EditorLaunch(string InstallRoot, GameContent SimulatorContent, Func<GameContent> ShellContent, string FontPath) {
	/// <summary>
	/// The mission to open first, named as File &gt; Load from VOL or File &gt; Load custom mission lists it (<c>C1_05</c> or
	/// <c>C1_05.MSN</c>); null for the one the editor last opened or saved (<see cref="EditorSettings.LastMission"/>). The
	/// editor starts with no mission and the list that holds it up, which names the mission being loaded, or says why when
	/// no mission has the name or it does not load.
	/// </summary>
	public string? Mission { get; init; }

	/// <summary>The bold face of <see cref="FontPath"/>'s font, for the panels' emphasised notes; null for none.</summary>
	public string? BoldFontPath { get; init; }

	/// <summary>The italic face of <see cref="FontPath"/>'s font, for the manual's italics (<see cref="HelpWindow"/>); null for none.</summary>
	public string? ItalicFontPath { get; init; }

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

	// The manual's headings are drawn in the radial menu's face, the one larger than the window's own, and its italics in
	// the italic face.
	private readonly HelpWindow _help;
	private readonly MissionBrowser _volBrowser;
	private readonly MissionBrowser _customBrowser;
	private readonly MissionSaving _saving;
	private readonly NewMissionDialog _newMission;
	private readonly EditorToolbox _toolbox;
	private readonly PlayInGame _playInGame = new();

	// Whether the mission Play started the game on still had changes the Discard dropped, so the editor opens its saved
	// files again once the game exits.
	private bool _reloadAfterPlay;
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

	// Play's handoff, written from the mission's saved files as the game's own load writes one, into a scratch directory
	// of its own, where the mission's results are written too, so a played mission leaves the install's DATA\ alone.
	private readonly string _playCache = Path.Combine(Path.GetTempPath(), "herculan-editor-play");
	private EditorPlay? _play;

	// What a Cybrid's hardpoints offer, and the zones with the retail missions that use each, read from the install's archived
	// missions the first time either is asked for, once per run.
	private readonly CybridWeapons _cybridWeapons;
	private readonly ZoneCatalog _zones;

	// The loadout presets, read once per run and written on every change, which each opened mission's HERC views offer.
	private readonly LoadoutPresets _presets = LoadoutPresets.Load();

	private EditorMission? _mission;
	private EditorExit _exit = EditorExit.Closed;

	// Set by Close, which shuts the window once the frame's ImGui is rendered: the window's Closing runs as soon as
	// it is asked to close and disposes the ImGui context, so closing from inside a menu, or between ImGui's new
	// frame and its render, would leave the rest of the frame calling into a destroyed context.
	private bool _closePending;

	// A mission a browser asked to open, and that browser, which says it is loading and why a load failed.
	private sealed record LoadRequest(string File, MissionBrowser From);

	// A load a browser asked for. It runs at the start of the frame after the second that drew the browser's "Loading"
	// line, so the window says what it is doing before it stops to do it: ImGui hides a window it has just created, whose
	// height fits its contents, for its first frame, which at start-up is the browser's.
	private const int LoadNoticeFrames = 2;
	private LoadRequest? _pendingLoad;
	private int _loadNoticeFrames;

	// A load a browser asked for that the unsaved-changes prompt has let through, started once the frame is rendered.
	private LoadRequest? _approvedLoad;

	// A new mission File > New mission asked for, built and opened as a load is: at the start of the frame after the one
	// that drew the dialog's "Creating" line.
	private NewMissionRequest? _pendingNew;
	private bool _newNoticeDrawn;

	// Whether Esc was down last frame: it acts as it goes down, so one held through a prompt's Cancel does not ask again.
	private bool _escapeHeld;

	// An Undo (true) or Redo (false) chosen from the Edit menu, made once the frame's ImGui is rendered.
	private bool? _pendingStep;

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
		var archived = new ArchivedMissions(launch.ShellContent);
		_cybridWeapons = new CybridWeapons(launch.SimulatorContent, archived, _settings);
		_zones = new ZoneCatalog(launch.SimulatorContent, archived);
		_flyNearPlane = _camera.NearPlane;
		_flyFarPlane = _camera.FarPlane;
		_padPointer = new GamepadPointer { Settings = JoystickMapFile.LoadIn(_installData).Mouse };
		_settingsPanel = new EditorSettingsPanel(_settings);
		_help = new HelpWindow(() => _imgui?.RadialFont, () => _imgui?.ItalicFont);
		IReadOnlyList<BrowsedMission> All() =>
			MissionBrowser.Merge(MissionFileLaunch.List(launch.ShellContent()), SavedMissions.List(launch.InstallRoot));
		_volBrowser = new MissionBrowser("Load from VOL", "The install's archives hold no missions.",
			() => MissionBrowser.Archived(All()));
		_customBrowser = new MissionBrowser("Load custom mission", "No custom missions are saved in the install yet.",
			() => MissionBrowser.Custom(All()));
		_saving = new MissionSaving(launch.InstallRoot, launch.ShellContent, _missionCache);
		_saving.Saved += _volBrowser.Refresh;
		_saving.Saved += _customBrowser.Refresh;
		_saving.Saved += () => Remember(_saving.Mission?.Name);
		_newMission = new NewMissionDialog(_saving, () => _zones.Zones, () => TheaterNames.Labels(launch.SimulatorContent));
		_toolbox = new EditorToolbox(launch.SimulatorContent);

		Console.WriteLine($"HERCULAN Mission Editor on {launch.InstallRoot}");
		OpenFirst(launch.Mission);

		Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, click to select, "
			+ "double-click to frame, V toggles top-down, drag a selected object's, waypoint's, group's or trigger area's arrows to move it (Alt detaches a waypoint, group or area corner from what shares its point), drag a HERC, a flyer, a ground vehicle or a structure from the Toolbox onto the ground to add it, New route then clicks on the ground place a route (Enter finishes, Esc cancels), New box or New circle then a drag on the ground places a trigger area (Esc cancels), Delete deletes the selected object, route, trigger area, trigger, countdown, objective or condition or removes the selected waypoint, Ctrl+Z/Ctrl+Y undo/redo, Ctrl+S saves, "
			+ (launch.FromGame ? "Esc returns to the game." : "Esc quits."));

		_window = launch.Window is { } window
			? new EngineWindow(window, WindowTitle)
			: new EngineWindow(WindowTitle, placement: _settings.Window?.ToPlacement(), maximized: true);
		_window.Load += OnLoad;
		_window.Update += OnUpdate;
		_window.Render += OnRender;
		_window.Closing += OnClosing;
		_window.FocusChanged += focused => _focused = focused;
		_window.CloseRequested = OnCloseRequested;
	}

	/// <summary>Runs the editor until its window closes or its turn in the game's ends, and returns how it ended.</summary>
	public static EditorResult Run(EditorLaunch launch) {
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

		return new EditorResult(host._exit, host._exit == EditorExit.Play ? host._play : null);
	}

	private string WindowTitle => _mission?.Title(_saving.Dirty) ?? "HERCULAN Mission Editor";

	// Puts up the list holding <named>, or else the one the editor last opened or saved, and loads it as a pick from that
	// list is, once the first frame has drawn its "Loading" line. A mission named at launch that no list holds leaves Load
	// from VOL up saying so; with no mission to open, or a remembered one since deleted, the start dialog comes up
	// (StartShown).
	private void OpenFirst(string? named) {
		string? first = named ?? _settings.LastMission;
		if (first != null && QueueLoad(first)) {
			return;
		}

		if (named != null) {
			_volBrowser.Open();
			_volBrowser.Failure = $"No mission is named {named}.";
			Console.Error.WriteLine(_volBrowser.Failure);
		}
	}

	// Puts up the list holding <name>, Load custom mission's or else Load from VOL's, and loads it as a pick from that list
	// is, asking nothing; whether a list holds it.
	private bool QueueLoad(string name) {
		var browser = _customBrowser.Find(name) != null ? _customBrowser : _volBrowser;
		if (browser.Find(name) is not { } found) {
			return false;
		}

		OpenBrowser(browser);
		_pendingLoad = new LoadRequest(found.File, browser);
		browser.Loading = found.File;
		return true;
	}

	// Whether the start dialog is up: while no mission is open and nothing else is up to open or make one.
	private bool StartShown => _mission == null && _pendingLoad == null && _approvedLoad == null && _pendingNew == null
		&& !_volBrowser.IsOpen && !_customBrowser.IsOpen && !_newMission.IsOpen && !_saving.IsOpen;

	// Records <name> as the mission to open next time (EditorSettings.LastMission), into the settings file as it stands
	// rather than through the open settings, which may hold the Settings panel's edits that were never confirmed.
	private void Remember(string? name) {
		if (name == null) {
			return;
		}

		string file = name.EndsWith(MissionFileLaunch.MissionExtension, StringComparison.OrdinalIgnoreCase)
			? name
			: name + MissionFileLaunch.MissionExtension;
		_settings.LastMission = file;
		var saved = EditorSettings.Load();
		saved.LastMission = file;
		saved.Save();
	}

	// The window being closed: let through when nothing is unsaved, else refused while the unsaved-changes prompt asks.
	private bool OnCloseRequested() {
		if (!_saving.Dirty) {
			return true;
		}

		_saving.Leave(_launch.FromGame ? "closing the game" : "quitting", () => Close(EditorExit.Closed));
		return false;
	}

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

		_imgui = new ScaledImGui(gl, _window, input, _launch.FontPath, _launch.ItalicFontPath, _launch.BoldFontPath) {
			HeldAsMouseButtons = () => (_padPointer.ClickHeld, _padPointer.RightClickHeld),
			PointerOverride = () => _padPointer.Override,
		};
		_input = input;
		EditorText.BoldFont = () => _imgui?.BoldFont;

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
			_leftDownPos = _padPointer.PositionOf(mouse);
			_leftDownOverViewport = !ImGui.GetIO().WantCaptureMouse;
			if (_leftDownOverViewport && _mission != null && _mission.AreaPlacing.Active) {
				// While an area is placed, a press on the ground starts its drag, and picks and moves nothing.
				_mission.AreaPlacing.Press(_mission.Picker.GroundAt(_camera, _leftDownPos, Viewport));
			} else if (_leftDownOverViewport && _mission != null && !_mission.Placing.Active) {
				bool alt = _keyboard != null && (_keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight));
				_mission.Gizmo.TryBegin(_camera, _leftDownPos, Viewport, detach: alt,
					carry: _keyboard != null && Control(_keyboard));
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
			var at = _padPointer.PositionOf(mouse);
			bool click = _leftDownOverViewport && Vector2.Distance(at, _leftDownPos) < 4f;
			if (_mission.Placing.Active) {
				// While a route is placed, a click places its next waypoint and picks nothing.
				if (click) {
					_mission.Placing.Click(_camera, at, _mission.Picker.GroundAt(_camera, at, Viewport));
				}
			} else if (_mission.AreaPlacing.Active) {
				// Let go over a panel, the area ends at the ground last under the pointer.
				_mission.AreaPlacing.Release(ImGui.GetIO().WantCaptureMouse
					? null
					: _mission.Picker.GroundAt(_camera, at, Viewport));
			} else if (_mission.Gizmo.Dragging) {
				// A press on a handle that moved nothing still counts toward a double-click, whose second press often
				// lands on the handles drawn over what the first selected; it leaves the selection as it is.
				if (!_mission.Gizmo.End() && click && IsDoubleClick(at)
						&& _mission.Selection.Current is { } current) {
					_mission.Selection.Focus(current);
				}
			} else if (click) {
				var picked = _mission.Picker.Pick(_camera, at, Viewport, _settings);
				if (IsDoubleClick(at) && picked != null) {
					_mission.Selection.Focus(picked);
				} else {
					_mission.Selection.Current = picked;
				}

				Console.WriteLine($"[pick] {picked?.ToString() ?? "nothing"} at {at}");
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

		// A key that needs no Ctrl does nothing while Ctrl is held, so Ctrl+S saves without moving the camera back.
		bool control = _keyboard != null && Control(_keyboard);
		bool escape = _keyboard?.IsKeyPressed(Key.Escape) == true;
		bool escapePressed = escape && !_escapeHeld && !control;
		_escapeHeld = escape;
		bool escapeFree = escapePressed && !_saving.IsOpen && !_newMission.IsOpen && _mission?.Presets.IsOpen != true
			&& !_playInGame.Running;
		if (escapeFree && _mission?.Placing.Active == true) {
			// Esc cancels the route being placed, and goes no further.
			_mission.Placing.Cancel();
		} else if (escapeFree && _mission?.AreaPlacing.Active == true) {
			// Esc cancels the trigger area being placed, and goes no further.
			_mission.AreaPlacing.Cancel();
		} else if (escapeFree) {
			var exit = _launch.FromGame ? EditorExit.BackToGame : EditorExit.Closed;
			if (_saving.Leave(_launch.FromGame ? "returning to the game" : "quitting", () => Close(exit))) {
				return;
			}
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

		bool acceptKeyboard = (_imgui == null || !ImGui.GetIO().WantCaptureKeyboard) && !control;
		_editorCamera.Update(deltaSeconds, _keyboard, lookDelta, _looking, acceptKeyboard);
		_editorCamera.ApplyTo(_camera);
		SetClipPlanes();

		// After the camera, so the object stays under the pointer while the view moves too.
		if (_mission?.Gizmo.Dragging == true && _mouse != null) {
			_mission.Gizmo.Drag(_camera, _padPointer.PositionOf(_mouse), Viewport);
		}
	}

	// The viewport in the pointer's coordinates, as ScenePicker and MoveGizmo take it.
	private Vector2 Viewport => new(_window.FramebufferSize.X, _window.FramebufferSize.Y);

	private MissionDrawables Upload(GL gl, SceneRenderer renderer, EditorMission mission) {
		var drawables = new MissionDrawables(gl, renderer, mission, _settings);
		mission.Mover.Moved += drawables.Move;
		mission.Mover.TerrainChanged += drawables.UploadTerrain;
		mission.Sync.Added += drawables.Add;
		mission.Sync.Removed += drawables.Remove;
		mission.Sync.Rebuilt += drawables.RefreshWaiting;
		_dropPreview = new DropPreview(mission, drawables);
		return drawables;
	}

	// What Upload hooked <drawables> to, unhooked, for drawables about to be replaced while the mission stays.
	private static void Unhook(EditorMission mission, MissionDrawables drawables) {
		mission.Mover.Moved -= drawables.Move;
		mission.Mover.TerrainChanged -= drawables.UploadTerrain;
		mission.Sync.Added -= drawables.Add;
		mission.Sync.Removed -= drawables.Remove;
		mission.Sync.Rebuilt -= drawables.RefreshWaiting;
	}

	// Listens to <mission> for what the host does for it: framing a double-clicked selection, and uploading its scene again
	// when its world changes.
	private void Follow(EditorMission mission) {
		mission.Selection.FocusRequested += Frame;
		mission.Theater.WorldChanged += () => ReloadWorld(mission);
	}

	// Uploads <mission>'s scene again once its theater or time of day has dressed it in another world
	// (TheaterChanger), freeing the uploads it replaces. The camera, the selection and the view settings stay.
	private void ReloadWorld(EditorMission mission) {
		if (_gl == null || _renderer == null || _drawables == null || !ReferenceEquals(mission, _mission)) {
			return;
		}

		Unhook(mission, _drawables);
		_drawables.Dispose();
		_drawables = Upload(_gl, _renderer, mission);
	}

	// Whether either Ctrl key is held, under which the keys that need no Ctrl do nothing.
	private static bool Control(IKeyboard keyboard) => keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

	// V toggles the top-down view, Delete deletes the selected object, route, trigger area, trigger, countdown, objective or condition or removes the selected
	// waypoint, Ctrl+Z undoes and Ctrl+Y or Ctrl+Shift+Z redoes, Ctrl+S saves, unless a text field or a prompt has the keyboard.
	// None of them while a drag is under way. While a route is placed, Enter finishes it; while a route or an area is placed,
	// Delete, Undo and Redo do nothing. V, Delete and Enter do nothing while Ctrl is held.
	private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) {
		if (_mission?.Gizmo.Dragging == true || _saving.IsOpen || _newMission.IsOpen || _mission?.Presets.IsOpen == true
				|| (_imgui != null && ImGui.GetIO().WantCaptureKeyboard)) {
			return;
		}

		bool control = Control(keyboard);
		if (key == Key.V && !control) {
			ToggleTopDown();
			return;
		}

		bool placing = _mission?.PlacingAny == true;
		if (_mission?.Placing.Active == true && key is Key.Enter or Key.KeypadEnter && !control) {
			_mission!.Placing.RequestFinish();
			return;
		}

		if (key == Key.Delete && !control && !placing) {
			if (_mission?.Selection.Current is ObjectSelection selected) {
				if (_mission.Index.ObjectOf(selected) is { } placed) {
					_mission.Deleter.Request(placed);
				}
			} else if (_mission?.Selection.Current is RouteSelection { Guid: var route, Waypoint: var waypoint }) {
				if (waypoint < 0) {
					_mission.Routes.RequestDelete(route);
				} else {
					_mission.Routes.RequestRemove(route, waypoint);
				}
			} else if (_mission?.Selection.Current is GroupSelection { Guid: var group }) {
				_mission.GroupDeleter.Request(group);
			} else if (_mission?.Selection.Current is ObjectiveSelection { Record: var objective }) {
				_mission.Objectives.RequestDelete(objective);
			} else if (_mission?.Selection.Current is AreaSelection { Guid: var area }) {
				_mission.Areas.RequestDelete(area);
			} else if (_mission?.Selection.Current is ActionSelection { Guid: var action }) {
				_mission.Actions.RequestDelete(action);
			} else if (_mission?.Selection.Current is TimerSelection { Guid: var timer }) {
				_mission.Timers.RequestDelete(timer);
			} else if (_mission?.Selection.Current is ConditionSelection { Guid: var condition }) {
				if (RandomTableEditing.IsTable(_mission.Source, condition)) {
					_mission.TableEditor.RequestDelete(condition);
				} else {
					_mission.Conditions.RequestDelete(condition);
				}
			}

			return;
		}

		if (_mission == null || !control) {
			return;
		}

		bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
		if (key == Key.S && !shift) {
			_saving.Save();
		} else if (key == Key.Z && !shift && !placing) {
			_mission.Mover.Undo();
		} else if ((key == Key.Y || (key == Key.Z && shift)) && !placing) {
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
		if (_pendingLoad != null && _loadNoticeFrames >= LoadNoticeFrames) {
			LoadMission(_pendingLoad);
			_pendingLoad.From.Loading = null;
			_pendingLoad = null;
			_loadNoticeFrames = 0;
		}

		if (_pendingNew != null && _newNoticeDrawn) {
			CreateMission(_pendingNew);
			_pendingNew = null;
			_newNoticeDrawn = false;
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
			RosterCountsOverlay.Draw(_mission.Source.Layout, display, ScaledImGui.Scaled(MissionOutliner.PanelWidth));
			_mission.Overlay.DrawLabels(_camera, _settings, _mission.Selection);
			_mission.GroupOverlay.DrawLabels(_camera, _settings, _mission.Selection);
			_mission.Placing.DrawLabels(_camera, display, menuBarHeight);
			_mission.AreaPlacing.DrawLabels(_camera, display, menuBarHeight);
			_mission.Outliner.Draw(display, menuBarHeight);
			_mission.Properties.Draw(display, menuBarHeight);
			var joinable = JoinableGroup(_mission);
			var mission = _mission;
			string? joinName = joinable is { } joining ? mission.Names.GroupBrief(joining).Full : null;
			_toolbox.Draw(display, menuBarHeight,
				enabled: !mission.Gizmo.Dragging && !mission.PlacingAny, joinable, joinName,
				joinable is { } group ? kind => WhyNotJoin(mission, group, kind) : null,
				mission.Hercs.WhyNoAdd, mission.Flyers.WhyNoAdd, mission.Vehicles.WhyNoAdd, mission.Placer.WhyNoAdd,
				mission.Placer.WhyNoBase, mission.Placing, mission.AreaPlacing);
			mission.Settings.Draw();
			mission.UnusedPoints.Draw();
		}

		_settingsPanel.Draw();
		_help.Draw();
		if (StartShown) {
			switch (StartDialog.Draw()) {
				case StartChoice.Vanilla:
					OpenBrowser(_volBrowser);
					break;
				case StartChoice.Custom:
					OpenBrowser(_customBrowser);
					break;
				case StartChoice.New:
					_newMission.Open();
					break;
			}
		}

		_volBrowser.Draw();
		_customBrowser.Draw();
		_saving.Draw();
		_newMission.Draw();
		_playInGame.Draw();
		_imgui?.Render();
		if (_window.Title != WindowTitle) {
			_window.Title = WindowTitle;
		}

		if (_closePending) {
			_window.Close();
			return;
		}

		// After the frame's ImGui, as a load is: an add, a delete, and the undo or redo of either, change what the panels
		// list.
		if (_toolbox.TakeRequest() is { } request) {
			AddFromToolbox(request);
		}

		if (_mission != null) {
			RunEditRequests(_mission);
		}

		if (_playInGame.TakeExit() is { } exitCode) {
			EndPlayInGame(exitCode);
		}

		if (_pendingLoad != null) {
			_loadNoticeFrames++;
		} else if (TakeLoadRequest() is { } requested) {
			_saving.Leave($"opening {requested.File}", () => _approvedLoad = requested);
		}

		if (_approvedLoad != null && _pendingLoad == null) {
			_pendingLoad = _approvedLoad;
			_approvedLoad.From.Loading = _approvedLoad.File;
			_approvedLoad = null;
		}

		if (_pendingNew != null) {
			_newNoticeDrawn = true;
		} else if (_newMission.TakeRequest() is { } asked) {
			_pendingNew = asked;
			_newMission.Creating = asked.Name;
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
		if (mission.PlacingAny) {
			var pointer = _mouse != null && !ImGui.GetIO().WantCaptureMouse
				? mission.Picker.GroundAt(_camera, _padPointer.PositionOf(_mouse), Viewport)
				: null;
			mission.Placing.Draw(wireframe, _camera, aspect, pointer);
			mission.AreaPlacing.Hover(pointer);
			mission.AreaPlacing.Draw(wireframe, _camera, aspect);
		} else {
			mission.Gizmo.Draw(wireframe, _camera, aspect, _mouse?.Position ?? new Vector2(-1f), Viewport);
		}
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

	// Why the menu bar's Play is unavailable, or null when it is. The editor on its own plays in the game beside it.
	private string? WhyNoPlay =>
		_mission == null ? "No mission is open."
		: _playInGame.Running ? $"{_playInGame.Mission} is being played."
		: !_launch.FromGame && PlayInGame.GamePath == null ? "The game is not beside the editor, so there is nothing to play the mission in."
		: _mission.Gizmo.Dragging || _mission.PlacingAny || _pendingLoad != null || _pendingNew != null ? "Finish what is under way first."
		: null;

	// Play, once the mission's saved files hold what is to be played, on the mission's own theater and time of day as the
	// editor shows them. In the game's editor: writes their handoff into Play's scratch directory, as the game's own load of a
	// mission named by its .MSN does (MissionFileLaunch), and ends the editor's turn for the game to play it. On its own:
	// starts the game beside the editor on them (PlayInGame) and minimizes the editor until the game exits. A play that does
	// not start says why and leaves the editor as it was.
	private void StartPlay() {
		if (_mission == null) {
			return;
		}

		string name = _mission.Name;
		if (!_launch.FromGame) {
			if (_playInGame.Start(_launch.InstallRoot, name, out string? notStarted)) {
				// Still changed after the prompt let Play through: Discard was chosen, and the game plays the saved files.
				_reloadAfterPlay = _saving.Dirty;
				_window.Minimize();
			} else {
				Console.Error.WriteLine($"Cannot play {name}: {notStarted}");
				_saving.Tell($"Cannot play {name}: {notStarted}");
			}

			return;
		}

		string? failure;
		string? script;
		try {
			Directory.CreateDirectory(_playCache);
			ClearMissionCache(_playCache);
			script = MissionFileLaunch.Write(_launch.ShellContent(), _launch.InstallRoot, name, _playCache, ownWorld: true, out failure);
		} catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
			script = null;
			failure = ex.Message;
		}

		if (script == null) {
			Console.Error.WriteLine($"Cannot play {name}: {failure}");
			_saving.Tell($"Cannot play {name}: {failure}");
			return;
		}

		_play = new EditorPlay(name, new ShellLaunch(script, _installData));
		Close(EditorExit.Play);
	}

	// The game Play started has exited with <exitCode>: the editor comes back, opening the mission's saved files again if
	// the changes Discard dropped are still shown, and says so if the game did not play it (its command line ends a
	// mission it played with 0, however the mission ended).
	private void EndPlayInGame(int exitCode) {
		_window.Raise();
		if (_reloadAfterPlay && _mission != null) {
			QueueLoad(_mission.Name);
		}

		_reloadAfterPlay = false;
		if (exitCode != 0) {
			_saving.Tell($"The game ended with code {exitCode}, so it did not play the mission to its end. Its log, "
				+ @"herculan.log in %LOCALAPPDATA%\Herculan, says why.");
		}
	}

	// The herc, flyer or structure group the selection is, or a member of, which a Toolbox add can join, by its row-16 GUID
	// in the layout. The player's squad is offered so the Toolbox can say why it takes no herc.
	private static short? JoinableGroup(EditorMission mission) {
		var index = mission.Index;
		var group = mission.Selection.Current switch {
			GroupSelection selected => index.Group(selected.Guid),
			ObjectSelection selected => index.Plan.Object(new ObjectKey(selected.Row, selected.Guid)) is { } planned
				? index.Group(planned.Group)
				: null,
			_ => null
		};
		return group switch {
			{ Kind: MissionUnitKind.Base, IsSquad: false } or { Kind: MissionUnitKind.Mech or MissionUnitKind.Flyer } => group.Guid,
			_ => null
		};
	}

	// Why an entry of <kind> cannot join <group>, by its row-16 GUID: the WhyNotJoin of the placer that kind's add goes
	// through, which its Add refuses by, so the Toolbox's checkbox and the add agree.
	private static string? WhyNotJoin(EditorMission mission, short group, ToolboxKind kind) => kind switch {
		ToolboxKind.Herc => mission.Hercs.WhyNotJoin(group),
		ToolboxKind.Flyer => mission.Flyers.WhyNotJoin(group),
		ToolboxKind.Vehicle => mission.Vehicles.WhyNotJoin(group),
		ToolboxKind.Base => "A base starts a group of its own.",
		_ => mission.Placer.WhyNotJoin(group)
	};

	// The delete, the group delete, the machine fit, the base pad, the add in a structure group's place, the squadmate add or remove, the
	// theater or time of day, the route edit, the trigger-area edit, the order edit, the action or timer edit, the objective
	// or text edit, the finished route or area, the unused-position removal and the Undo or Redo the frame's panels, menus and keys asked for; none while
	// a drag is under way.
	private void RunEditRequests(EditorMission mission) {
		var deleting = mission.Deleter.TakeRequest();
		var step = _pendingStep;
		_pendingStep = null;
		if (mission.Gizmo.Dragging) {
			return;
		}

		if (deleting != null && mission.Deleter.Delete(deleting) is { } reason) {
			Console.Error.WriteLine($"Cannot delete {(mission.Index.KeyOf(deleting) is { } key ? mission.Names.Object(key).Full : "the object")}: {reason}");
		}

		mission.GroupDeleter.RunRequest();
		mission.Members.RunRequest();
		mission.Fitter.RunRequest();
		mission.Types.RunRequest();
		mission.StartingConditions.RunRequest();
		mission.RadarSettings.RunRequest();
		mission.Headings.RunRequest();
		mission.Pads.RunRequest();
		mission.Formations.RunRequest();
		mission.Placer.RunRequest();
		mission.Hercs.RunRequest();
		mission.Flyers.RunRequest();
		mission.Squad.RunRequest();
		mission.Theater.RunRequest();
		mission.PlayableIn.RunRequest();
		mission.Routes.RunRequest();
		mission.Areas.RunRequest();
		mission.Orders.RunRequest();
		mission.Actions.RunRequest();
		mission.Timers.RunRequest();
		mission.Conditions.RunRequest();
		mission.TableEditor.RunRequest();
		mission.Presence.RunRequest();
		mission.Objectives.RunRequest();
		mission.Texts.RunRequest();
		mission.Debrief.RunRequest();
		mission.Placing.RunRequest();
		mission.AreaPlacing.RunRequest();
		mission.UnusedPoints.RunRequest();

		if (step == true) {
			mission.Mover.Undo();
		} else if (step == false) {
			mission.Mover.Redo();
		}
	}

	// Stands what a Toolbox drag would add on the ground under the pointer, from the drag as the last frame's
	// panels left it; not while the pointer is over a panel, where letting go adds nothing.
	private void UpdateDropPreview(EditorMission mission) {
		var drag = _toolbox.Drag;
		var ground = drag != null && !_toolbox.DragOverPanel && _mouse != null
			? mission.Picker.GroundAt(_camera, _padPointer.PositionOf(_mouse), Viewport)
			: null;
		_dropPreview?.Update(drag, ground);
	}

	// Adds what the toolbox asked for on the ground under the pointer, or at the middle of the view, else under the
	// camera when the middle of the view is sky.
	private void AddFromToolbox(ToolboxRequest request) {
		if (_mission == null || _mission.Gizmo.Dragging) {
			return;
		}

		var picker = _mission.Picker;
		var ground = request.AtPointer
			? picker.GroundAt(_camera, _mouse?.Position ?? Viewport / 2f, Viewport)
			: picker.GroundAt(_camera, Viewport / 2f, Viewport)
				?? new Numerics.Vec3i((int)_editorCamera.WorldPosition.X, (int)_editorCamera.WorldPosition.Y, 0);
		_toolbox.Status = ground is not { } at ? "There is no ground under the pointer."
			: request.Kind switch {
				ToolboxKind.Herc => _mission.Hercs.Add(request.TypeIndex, request.Side, at.X, at.Y, request.Group),
				ToolboxKind.Flyer => _mission.Flyers.Add(request.TypeIndex, request.Side, at.X, at.Y, request.Group),
				ToolboxKind.Vehicle => _mission.Vehicles.Add(request.TypeIndex, request.Side, at.X, at.Y, request.Group),
				ToolboxKind.Base => _mission.Placer.AddBase(BaseTemplates.All[request.TypeIndex], request.Side, at.X, at.Y),
				_ => _mission.Placer.Add(request.TypeIndex, request.Side, at.X, at.Y, request.Group)
			};
	}

	// The mission either browser asked to open since the last frame, with the browser that asked.
	private LoadRequest? TakeLoadRequest() =>
		_volBrowser.TakeRequest() is { } archived ? new LoadRequest(archived, _volBrowser)
		: _customBrowser.TakeRequest() is { } custom ? new LoadRequest(custom, _customBrowser)
		: null;

	// Opens <browser> in place of the other mission list.
	private void OpenBrowser(MissionBrowser browser) {
		(browser == _volBrowser ? _customBrowser : _volBrowser).Close();
		browser.Open();
	}

	// Generates the handoff for the mission the request names, an archive's or one saved into the install, into the scratch
	// directory, holds the mission as its .MSN, swaps the editor over to it and remembers it for the next run. On failure
	// the open mission stays, and the browser that asked says why.
	private void LoadMission(LoadRequest request) {
		if (_gl == null || _renderer == null) {
			return;
		}

		string name = request.File;
		EditorMission opened;
		try {
			ClearMissionCache();
			if (MissionOpener.Open(_launch.ShellContent(), _launch.InstallRoot, name, _missionCache, out var source,
					out var squad, out string? failure) is not { } generated) {
				request.From.Failure = failure;
				Console.Error.WriteLine($"Cannot load {name}: {failure}");
				return;
			}

			opened = EditorMission.Open(_launch.SimulatorContent, generated, Path.GetFileNameWithoutExtension(name), _installData,
				source!, squad!, _launch.ShellContent, _cybridWeapons, _zones, _presets);
		} catch (Exception ex) when (ex is MissingHandoffFileException or InvalidDataException or IOException
				or UnauthorizedAccessException) {
			request.From.Failure = ex.Message;
			Console.Error.WriteLine($"Cannot load {name}: {ex.Message}");
			return;
		}

		SwapTo(_gl, _renderer, opened);
		_saving.Mission = opened;
		request.From.Close();
		Remember(name);
	}

	// Builds the new mission File > New mission asked for (NewMission, in HercWorks.Core), generates its handoff into the
	// scratch directory as a VOL load does, from the .MSN held in memory, and swaps the editor over to it, unsaved. On
	// failure the open mission stays, and the dialog says why.
	private void CreateMission(NewMissionRequest request) {
		if (_gl == null || _renderer == null) {
			return;
		}

		var zone = request.Zone;
		var (msn, text) = NewMission.Build(request.Theater, zone.Zone, request.Night, zone.Width, zone.Height);
		EditorMission opened;
		try {
			ClearMissionCache();
			if (MissionOpener.OpenNew(_launch.ShellContent(), _launch.InstallRoot, request.Name,
					new MissionFileTransformer().Write(msn) ?? throw new InvalidDataException("The new mission does not write."),
					new MissionStringFileTransformer().Write(text), _missionCache, out var source, out var squad,
					out string? failure) is not { } generated) {
				Fail(failure);
				return;
			}

			opened = EditorMission.Open(_launch.SimulatorContent, generated, request.Name, _installData, source!, squad!,
				_launch.ShellContent, _cybridWeapons, _zones, _presets);
		} catch (Exception ex) when (ex is MissingHandoffFileException or InvalidDataException or IOException
				or UnauthorizedAccessException) {
			Fail(ex.Message);
			return;
		}

		SwapTo(_gl, _renderer, opened);
		_saving.OpenNew(opened);
		_newMission.Close();

		void Fail(string? reason) {
			_newMission.Creating = null;
			_newMission.Failure = reason;
			Console.Error.WriteLine($"Cannot create {request.Name}: {reason}");
		}
	}

	// Puts <opened> in the editor in place of the open mission, its drawing uploaded and the camera where its scene starts.
	private void SwapTo(GL gl, SceneRenderer renderer, EditorMission opened) {
		_drawables?.Dispose();
		_mission = opened;
		Follow(opened);
		_drawables = Upload(gl, renderer, opened);
		_editorCamera.ResetTo(opened.Scene.Camera.Position, opened.Scene.Camera.Heading);
		ReportLoaded(opened);
	}

	// Removes the last generated handoff from <directory>, the open's scratch directory unless named, since the shell's
	// writer does not truncate (ShellWorkingFiles.WriteMissionFile) and a shorter mission would otherwise keep the longer
	// one's tail.
	private void ClearMissionCache(string? directory = null) {
		foreach (string file in new[] {
				MissionLoader.ScriptFileName, MissionLoader.TextFileName, MissionLoader.PlayerFileName,
				MissionLoader.CountersFileName }) {
			string path = CaseInsensitivePath.Combine(directory ?? _missionCache, file);
			if (File.Exists(path)) {
				File.Delete(path);
			}
		}
	}

	private static void ReportLoaded(EditorMission opened) {
		Console.WriteLine(
			$"Mission {opened.Name}: zone {opened.Zone}, theater {opened.Theater.Theater}. " +
			$"Placed {opened.Index.Objects.Count} objects, {opened.Index.Objects.Count(placed => placed.Model == null)} without a model.");
	}

	// Draws the main menu bar and returns its height, which is what the rest of the frame's overlays
	// hang below.
	private float BuildMenuBar() {
		if (!ImGui.BeginMainMenuBar()) {
			return 0f;
		}

		if (ImGui.BeginMenu("File")) {
			bool idle = _pendingLoad == null && _pendingNew == null && _mission?.Gizmo.Dragging != true;
			if (ImGui.MenuItem("New mission...", null, false, idle)) {
				_saving.Leave("starting a new mission", _newMission.Open);
			}

			if (ImGui.MenuItem("Load from VOL", null, false, _pendingLoad == null)) {
				OpenBrowser(_volBrowser);
			}

			if (ImGui.MenuItem("Load custom mission", null, false, _pendingLoad == null)) {
				OpenBrowser(_customBrowser);
			}

			ImGui.Separator();
			bool saveable = _saving.WhyNoSave == null && _mission?.Gizmo.Dragging != true;
			if (ImGui.MenuItem("Save", "Ctrl+S", false, saveable)) {
				_saving.Save();
			}

			WhyNoSave();
			if (ImGui.MenuItem("Save As...", null, false, saveable)) {
				_saving.SaveAs();
			}

			WhyNoSave();
			if (ImGui.MenuItem("Export script.dat...", null, false, saveable)) {
				_saving.Export();
			}

			WhyNoSave();
			ImGui.Separator();

			// Outside the game there is no game to go back to.
			if (ImGui.MenuItem("Back to game", null, false, _launch.FromGame)) {
				_saving.Leave("returning to the game", () => Close(EditorExit.BackToGame));
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("Edit", _mission != null)) {
			bool idle = !_mission!.Gizmo.Dragging && !_mission.PlacingAny;
			if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _mission.Mover.CanUndo && idle)) {
				_pendingStep = true;
			}

			if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _mission.Mover.CanRedo && idle)) {
				_pendingStep = false;
			}

			ImGui.Separator();
			if (ImGui.MenuItem("Mission Settings")) {
				_mission.Settings.Open();
			}

			int unused = _mission.UnusedPoints.Count;
			if (ImGui.MenuItem($"Remove unused positions ({unused})", null, false, unused > 0 && idle)) {
				_mission.UnusedPoints.Request();
			}

			if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
				ImGui.SetTooltip("Removes every position no group, object, waypoint, trigger area, order or win/lose condition "
					+ "stands on. Positions picked by a random table are not removed.");
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
			if (ImGui.MenuItem("Play Area", null, ref box)) {
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

		string? whyNoPlay = WhyNoPlay;
		if (ImGui.MenuItem("Play", null, false, whyNoPlay == null)) {
			_saving.SaveFirst("playing it", StartPlay);
		}

		if (whyNoPlay != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(whyNoPlay);
		}

		if (ImGui.MenuItem("Help")) {
			_help.Open();
		}

		float height = ImGui.GetWindowSize().Y;
		ImGui.EndMainMenuBar();
		return height;
	}

	// Says, over a File menu item that saves, why it is disabled.
	private void WhyNoSave() {
		if (_saving.WhyNoSave is { } why && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(why);
		}
	}
}
