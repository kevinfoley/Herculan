using Herculan.Engine.Host.Settings;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Engine.Shell;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host;

/// <summary>
/// A front end's window and what this engine adds to it: HERCULAN's own menu bar and its windows, the Settings
/// menu's restart, the <c>--screenshot</c> capture, the focus, and the pointer in framebuffer pixels. None of it is
/// the game's, so another game's front end reuses it as it stands; what the game does with the keys, the focus and
/// the pointer stays with the caller.
/// </summary>
sealed class FrontEndWindow : IDisposable {
	/// <summary>
	/// Frames to let pass before <c>--screenshot</c> fires. The shell has nothing to settle — no
	/// simulation, no streaming — but the window manager can hand back a stale or part-sized
	/// framebuffer for the first frame or two, so the capture waits the same short beat the mission
	/// host waits.
	/// </summary>
	private const int ScreenshotFrame = 5;

	private readonly EngineWindow _window;
	private readonly HostMenuBar _menuBar;
	private readonly string _imguiFontPath;
	private readonly string? _screenshotPath;
	private readonly GamepadHostButtons _padButtons = new();
	private readonly GamepadPointer _padPointer = new();
	private IInputContext? _input;
	private ScaledImGui? _imgui;
	private ImGuiGamepad? _padNav;
	private int _framesRendered;

	// Set by the Settings menu's restart and the Editor item, either of which closes the window once the frame's ImGui
	// is drawn.
	private bool _closePending;

	/// <param name="window">The window the front end's turn runs in.</param>
	/// <param name="restartRequested">
	/// The Settings menu's restart, run as it is asked for; the window then closes once the frame is drawn.
	/// </param>
	/// <param name="editorRequested">
	/// The Editor item's switch to the mission editor, run as it is asked for; the window then closes once the frame
	/// is drawn.
	/// </param>
	public FrontEndWindow(HostSession session, HostWindow window, string title, string? screenshotPath,
			Action restartRequested, Action editorRequested) {
		_imguiFontPath = session.ImGuiFontPath;
		_screenshotPath = screenshotPath;
		_window = new EngineWindow(window, title);
		_menuBar = new HostMenuBar(session.Localization, new TweaksMenu(TweakSettings.Current, session.Localization),
			new SettingsWindow(session, () => RequestClose(restartRequested)),
			new ControllerWindow(session.Localization, () => ShellWorkingFiles.DataDirectory(session.InstallRoot)),
			openEditor: () => RequestClose(editorRequested));
		_window.FocusChanged += focused => {
			HasFocus = focused;
			FocusChanged?.Invoke(focused);
		};
	}

	public event Action<GL, IInputContext>? Load {
		add => _window.Load += value;
		remove => _window.Load -= value;
	}

	public event Action<double>? Update {
		add => _window.Update += value;
		remove => _window.Update -= value;
	}

	public event Action<double, GL>? Render {
		add => _window.Render += value;
		remove => _window.Render -= value;
	}

	public event Action? Closing {
		add => _window.Closing += value;
		remove => _window.Closing -= value;
	}

	/// <summary>Raised when the window gains or loses the focus, after <see cref="HasFocus"/> has taken it.</summary>
	public event Action<bool>? FocusChanged;

	/// <summary>
	/// Whether the window has the focus. It starts set, until the first time the window loses it, which a window that
	/// lost it during the load hears as the turn starts.
	/// </summary>
	public bool HasFocus { get; private set; } = true;

	public IMouse? Mouse { get; private set; }

	public IKeyboard? Keyboard { get; private set; }

	public bool FullScreen => _window.FullScreen;

	public Vector2D<int> FramebufferSize => _window.FramebufferSize;

	/// <summary>Whether the pointer is over the menu bar or one of its windows.</summary>
	public bool ImGuiWantsMouse => _imgui != null && ImGui.GetIO().WantCaptureMouse;

	/// <summary>Whether a field of the menu bar's windows is being typed into.</summary>
	public bool ImGuiWantsKeyboard => _imgui != null && ImGui.GetIO().WantCaptureKeyboard;

	/// <summary>Takes the window's devices once its input context exists, and builds ImGui on its GL context.</summary>
	public void Attach(GL gl, IInputContext input) {
		_imgui = new ScaledImGui(gl, _window, input, _imguiFontPath);
		_padNav = new ImGuiGamepad(input);
		_imgui.GamepadFeed = _padNav.Feed;
		Mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
		Keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
		_input = input;
		_menuBar.Controller.Attach(input);
	}

	/// <summary>
	/// The pointer in framebuffer pixels. The pointer reports window-client pixels while a canvas is placed in
	/// framebuffer pixels, which differ on a scaled display — the same correction the mission host makes.
	/// </summary>
	public (float X, float Y) PointerIn(IMouse mouse, Vector2D<int> framebuffer) {
		var client = _window.ClientSize;
		return (mouse.Position.X * framebuffer.X / Math.Max(client.X, 1),
			mouse.Position.Y * framebuffer.Y / Math.Max(client.Y, 1));
	}

	/// <summary>Whether a gamepad's right trigger is pulled as the left button (<see cref="GamepadPointer"/>).</summary>
	public bool PadClickHeld => _padPointer.ClickHeld;

	/// <summary>Whether a gamepad button is held as a skip key (<see cref="GamepadPointer"/>).</summary>
	public bool PadSkipHeld => _padPointer.SkipHeld;

	/// <summary>
	/// Moves the pointer by a gamepad's left stick and reads its right trigger and its buttons, once an update before the
	/// buttons are polled (<see cref="GamepadPointer"/>). The pad stands down without the focus, in a <c>--screenshot</c>
	/// capture, and while the menu bar or one of its windows is up, where ImGui's own gamepad navigation has it.
	/// </summary>
	public void UpdatePadPointer(double delta) {
		if (_input != null) {
			_padPointer.Update(_input, Mouse, _window.ClientSize, delta,
				active: HasFocus && _screenshotPath == null && !_menuBar.Up);
		}
	}

	/// <summary>
	/// VSHELL's Display_ToggleFullScreen (00407085). Retail sets an exclusive 640x480 8-bit display mode with the window's frame pushed
	/// off the screen, confines the pointer to the screen and centres it; going back releases DirectDraw,
	/// which restores the desktop's mode, and centres the window. Here full screen covers the monitor at
	/// its current mode (EngineWindow.ToggleFullScreen), with the canvas scaled into it as it is in a
	/// window — a divergence the user chose, so that no display mode changes. The pointer is confined and
	/// centred as retail's is.
	/// </summary>
	public void ToggleFullScreen() => _window.ToggleFullScreen(Mouse);

	/// <inheritdoc cref="EngineWindow.StartFullScreen"/>
	public void StartFullScreen(bool fullScreen) => _window.StartFullScreen(fullScreen, Mouse);

	/// <summary>A menu-bar key press: closes whichever of its windows is open, else hides an empty bar, else raises it.</summary>
	public void MenuBarEscape() {
		if (!_menuBar.BackOut()) {
			_menuBar.Show();
		}
	}

	/// <summary>Whether the menu bar or one of its windows is up.</summary>
	public bool MenuBarUp => _menuBar.Up;

	/// <summary>
	/// Raises the first start's <see cref="WelcomeDialog"/>, unless it has been dismissed before or this run is a
	/// <c>--screenshot</c> capture, which draws no menu bar windows.
	/// </summary>
	public void OfferWelcome() {
		if (_screenshotPath == null) {
			_menuBar.Welcome.Offer();
		}
	}

	/// <summary>
	/// The top of a frame: ImGui's, and a gamepad's Back, which raises the menu bar or backs out of it as [Shift+Esc]
	/// does (<see cref="GamepadHostButtons"/>), and puts ImGui's navigation on the bar it leaves up. The front end has no
	/// pause, so Start does nothing here.
	/// </summary>
	public void BeginFrame(double delta) {
		if (_padNav != null) {
			_padNav.Suspended = _menuBar.Controller.Listening;
		}

		_imgui?.Update((float)delta);
		if (_input != null && _screenshotPath == null) {
			_padButtons.Update(_input);
			if (_padButtons.BackPressed) {
				_menuBar.PadBack();
			}
		}
	}

	/// <summary>The menu bar and its windows over whatever the frame drew, never in a <c>--screenshot</c> capture.</summary>
	public void DrawMenuBar() {
		if (_imgui == null) {
			return;
		}

		if (_screenshotPath == null) {
			_menuBar.Draw(_window.View.Native?.Win32?.Hwnd ?? 0, _window.FullScreen);
		}

		_imgui.Render();

		if (_closePending) {
			_window.Close();
		}
	}

	/// <summary>Counts a frame drawn, and on the one <c>--screenshot</c> waits for captures it and closes the window.</summary>
	public void FrameDrawn(GL gl) {
		_framesRendered++;
		if (_screenshotPath != null && _framesRendered == ScreenshotFrame) {
			var framebuffer = _window.FramebufferSize;
			Screenshot.Capture(gl, framebuffer.X, framebuffer.Y, _screenshotPath);
			_window.Close();
		}
	}

	public void Close() => _window.Close();

	public void DisposeImGui() {
		_imgui?.Dispose();
		_imgui = null;
	}

	/// <summary>Runs the front end's turn until it ends.</summary>
	public void Run() {
		_window.Run();
		_menuBar.Settings.Dispose();
	}

	public void Dispose() => _window.Dispose();

	// Settings and the Editor item ask from inside an ImGui window, and the close disposes the ImGui context, so the
	// close waits for DrawMenuBar to finish the frame.
	private void RequestClose(Action requested) {
		requested();
		_closePending = true;
	}
}
