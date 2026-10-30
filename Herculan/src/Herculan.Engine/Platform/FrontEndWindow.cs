using Herculan.Engine.Input;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Platform;

/// <summary>
/// A front end's window and what this engine adds to it: the game's menu bar and its windows (<see cref="IFrontEndMenus"/>),
/// ImGui on the window's GL context, the gamepad's pointer and its ImGui navigation, the <c>--screenshot</c> capture, the
/// focus, and the pointer in framebuffer pixels. None of it is the game's retail front end, so each game's front end
/// reuses it as it stands with its own menus; what the game does with the keys, the focus and the pointer stays with the
/// caller.
/// </summary>
public sealed class FrontEndWindow : IDisposable {
	/// <summary>
	/// Frames to let pass before <c>--screenshot</c> fires. A front end has nothing to settle — no
	/// simulation, no streaming — but the window manager can hand back a stale or part-sized
	/// framebuffer for the first frame or two, so the capture waits the same short beat the mission
	/// host waits.
	/// </summary>
	private const int ScreenshotFrame = 5;

	private readonly EngineWindow _window;
	private readonly IFrontEndMenus _menus;
	private readonly string _imguiFontPath;
	private readonly string _imguiItalicFontPath;
	private readonly string? _screenshotPath;
	private readonly GamepadHostButtons _padButtons = new();
	private readonly GamepadPointer _padPointer = new();
	private IInputContext? _input;
	private ScaledImGui? _imgui;
	private ImGuiGamepad? _padNav;
	private int _framesRendered;

	// Whether the bar was up at the last pad update, to see it go down.
	private bool _barWasVisible;

	// Set by CloseAfterFrame, which the menus' CloseRequested and the caller's own requests from inside an ImGui window
	// go through, to close the window once the frame's ImGui is drawn.
	private bool _closePending;

	/// <param name="window">The window the front end's turn runs in.</param>
	/// <param name="imguiFontPath">The regular face ImGui draws the menus in (<see cref="ScaledImGui"/>).</param>
	/// <param name="imguiItalicFontPath">The italic face ImGui draws the menus in.</param>
	/// <param name="dataDirectory">
	/// The folder whose controller map (<see cref="JoystickMapFile"/>) gives the gamepad pointer its starting settings.
	/// </param>
	/// <param name="menus">The game's menu bar and its windows, which the window takes over and disposes once its turn has run.</param>
	public FrontEndWindow(HostWindow window, string title, string? screenshotPath, string imguiFontPath,
			string imguiItalicFontPath, string dataDirectory, IFrontEndMenus menus) {
		_imguiFontPath = imguiFontPath;
		_imguiItalicFontPath = imguiItalicFontPath;
		_screenshotPath = screenshotPath;
		_window = new EngineWindow(window, title);
		_padPointer.Settings = JoystickMapFile.LoadIn(dataDirectory).Mouse;
		_menus = menus;
		_menus.PadPointerSettingsChanged += settings => _padPointer.Settings = settings;
		_menus.CloseRequested += CloseAfterFrame;
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
		_imgui = new ScaledImGui(gl, _window, input, _imguiFontPath, _imguiItalicFontPath) {
			HeldAsMouseButtons = () => (_padPointer.ClickHeld, _padPointer.RightClickHeld),
			PointerOverride = () => _padPointer.Override,
		};
		_padNav = new ImGuiGamepad(input);
		_imgui.GamepadFeed = _padNav.Feed;
		Mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
		Keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
		_input = input;
		_menus.Attach(_imgui, input);
	}

	/// <summary>
	/// The pointer in framebuffer pixels. The pointer reports window-client pixels while a canvas is placed in
	/// framebuffer pixels, which differ on a scaled display — the same correction the mission host makes. It is the
	/// gamepad's own pointer where the window's cursor could not follow the pad (<see cref="GamepadPointer.Override"/>).
	/// </summary>
	public (float X, float Y) PointerIn(IMouse mouse, Vector2D<int> framebuffer) {
		var client = _window.ClientSize;
		var position = _padPointer.PositionOf(mouse);
		return (position.X * framebuffer.X / Math.Max(client.X, 1), position.Y * framebuffer.Y / Math.Max(client.Y, 1));
	}

	/// <summary>Whether a gamepad's trigger is pulled as the left button (<see cref="GamepadPointer"/>).</summary>
	public bool PadClickHeld => _padPointer.ClickHeld;

	/// <summary>Whether a gamepad's other trigger is pulled as the right button (<see cref="GamepadPointer"/>).</summary>
	public bool PadRightClickHeld => _padPointer.RightClickHeld;

	/// <summary>
	/// Whether a gamepad button is held as a skip key (<see cref="GamepadPointer"/>); never while the bar is up, where the
	/// buttons work the bar.
	/// </summary>
	public bool PadSkipHeld => _padPointer.SkipHeld && !_menus.Visible;

	/// <summary>Whether a gamepad's A is held (<see cref="GamepadPointer.AHeld"/>); never while the bar is up, as <see cref="PadSkipHeld"/>.</summary>
	public bool PadAHeld => _padPointer.AHeld && !_menus.Visible;

	/// <summary>Whether a gamepad's B is held (<see cref="GamepadPointer.BHeld"/>); never while the bar is up, as <see cref="PadSkipHeld"/>.</summary>
	public bool PadBHeld => _padPointer.BHeld && !_menus.Visible;

	/// <summary>
	/// Moves the pointer by a gamepad's stick and reads its triggers and its buttons, once an update before the
	/// buttons are polled (<see cref="GamepadPointer"/>). The pad stands down without the focus, in a <c>--screenshot</c>
	/// capture, and while one of the menu bar's windows is up, where ImGui's own gamepad navigation has it. While the bar
	/// alone is up the stick and the triggers go on working the pointer, the triggers reaching ImGui as the mouse's
	/// buttons, so the bar can be clicked or hidden by a click outside it; the bar going down then waits, as a window
	/// closing does, for the pad to be let go.
	/// </summary>
	public void UpdatePadPointer(double delta) {
		if (_input == null) {
			return;
		}

		if (_barWasVisible && !_menus.Visible) {
			_padPointer.WaitForRelease();
		}

		_barWasVisible = _menus.Visible;
		_padPointer.Update(_input, Mouse, _window.ClientSize, delta,
			active: HasFocus && _screenshotPath == null && !_menus.PanelOpen);
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
		if (!_menus.BackOut()) {
			_menus.Show();
		}
	}

	/// <summary>Whether the menu bar or one of its windows is up.</summary>
	public bool MenuBarUp => _menus.Visible || _menus.PanelOpen;

	/// <summary>
	/// Raises what the menus offer at the start (<see cref="IFrontEndMenus.OfferWelcome"/>), never in a <c>--screenshot</c>
	/// capture, which draws no menu bar windows.
	/// </summary>
	public void OfferWelcome() {
		if (_screenshotPath == null) {
			_menus.OfferWelcome();
		}
	}

	/// <summary>
	/// Tells the player that <paramref name="message"/>, with <paramref name="detail"/> saying why: over the window while
	/// it is up (<see cref="IFrontEndMenus.ShowError"/>), or without it once the window has closed
	/// (<see cref="IFrontEndMenus.ShowErrorOutsideWindow"/>), as it has for the autosave at the loop's end. A
	/// <c>--screenshot</c> capture, which has nobody to close either, only writes it to standard error.
	/// </summary>
	public void ShowError(string message, string detail) {
		if (_screenshotPath != null) {
			Console.Error.WriteLine($"{message} {detail}");
		} else if (_imgui != null) {
			_menus.ShowError(message, detail);
		} else {
			_menus.ShowErrorOutsideWindow(message, detail);
		}
	}

	/// <summary>
	/// The top of a frame: ImGui's, and a gamepad's Back, which raises the menu bar or backs out of it as [Shift+Esc]
	/// does (<see cref="GamepadHostButtons"/>, <see cref="IFrontEndMenus.PadBack"/>). The front end has no pause, so
	/// Start does nothing here.
	/// </summary>
	public void BeginFrame(double delta) {
		if (_padNav != null) {
			_padNav.Suspended = _menus.PadNavigationSuspended;
		}

		_imgui?.Update((float)delta);
		if (_input != null && _screenshotPath == null) {
			_padButtons.Update(_input);
			if (_padButtons.BackPressed) {
				_menus.PadBack();
			}
		}
	}

	/// <summary>
	/// The menu bar and its windows over whatever the frame drew (<see cref="IFrontEndMenus.Draw"/>), never in a
	/// <c>--screenshot</c> capture.
	/// </summary>
	/// <param name="revealOnHover">
	/// Whether the pointer at the window's top may raise the bar: the caller's say, which the shell withholds while a movie
	/// plays. It does nothing without the focus either, nor in a capture.
	/// </param>
	public void DrawMenuBar(bool revealOnHover) {
		if (_imgui == null) {
			return;
		}

		if (_screenshotPath == null) {
			_menus.Draw(_window.View.Native?.Win32?.Hwnd ?? 0, _window.FullScreen, revealOnHover && HasFocus);
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

	/// <summary>
	/// Closes the window once the frame's ImGui is drawn, for a request made from inside one of the bar's windows, since the
	/// close disposes the ImGui context.
	/// </summary>
	public void CloseAfterFrame() => _closePending = true;

	public void DisposeImGui() {
		_imgui?.Dispose();
		_imgui = null;
	}

	/// <summary>Runs the front end's turn until it ends.</summary>
	public void Run() {
		_window.Run();
		_menus.Dispose();
	}

	public void Dispose() => _window.Dispose();
}
