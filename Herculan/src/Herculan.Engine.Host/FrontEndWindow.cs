using Herculan.Engine.Host.Settings;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
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

	private readonly HostSession _session;
	private readonly EngineWindow _window;
	private readonly HostMenuBar _menuBar;
	private readonly string _imguiFontPath;
	private readonly string? _screenshotPath;
	private ScaledImGui? _imgui;
	private int _framesRendered;

	// Set by the Settings menu's restart, which closes the window once the frame's ImGui is drawn.
	private bool _restartPending;

	/// <param name="restartRequested">
	/// The Settings menu's restart, run as it is asked for; the window then closes once the frame is drawn.
	/// </param>
	public FrontEndWindow(HostSession session, string title, string? screenshotPath, Action restartRequested) {
		_imguiFontPath = session.ImGuiFontPath;
		_screenshotPath = screenshotPath;
		_session = session;
		_window = new EngineWindow(title, placement: session.WindowPlacement);
		if (session.SavePrintScreens) {
			PrintScreenFiles.Attach(_window, session);
		}
		_menuBar = new HostMenuBar(session.Localization, new TweaksMenu(TweakSettings.Current, session.Localization),
			new SettingsWindow(session, () => RequestRestart(restartRequested)));
		_window.View.FocusChanged += focused => {
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

	/// <summary>Whether the window has the focus. It starts set, until the first time the window loses it.</summary>
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
		Mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
		Keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
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

	/// <summary>
	/// VSHELL's Display_ToggleFullScreen (00407085). Retail sets an exclusive 640x480 8-bit display mode with the window's frame pushed
	/// off the screen, confines the pointer to the screen and centres it; going back releases DirectDraw,
	/// which restores the desktop's mode, and centres the window. Here full screen covers the monitor at
	/// its current mode (EngineWindow.ToggleFullScreen), with the canvas scaled into it as it is in a
	/// window — a divergence the user chose, so that no display mode changes. The pointer is confined and
	/// centred as retail's is.
	/// </summary>
	public void ToggleFullScreen() => _window.ToggleFullScreen(Mouse);

	/// <summary>[Esc] for the menu bar: closes whichever of its windows is open, else hides an empty bar, else raises it.</summary>
	public void MenuBarEscape() {
		if (!_menuBar.BackOut()) {
			_menuBar.Show();
		}
	}

	public void BeginFrame(double delta) => _imgui?.Update((float)delta);

	/// <summary>The menu bar and its windows over whatever the frame drew, never in a <c>--screenshot</c> capture.</summary>
	public void DrawMenuBar() {
		if (_imgui == null) {
			return;
		}

		if (_screenshotPath == null) {
			_menuBar.Draw(_window.View.Native?.Win32?.Hwnd ?? 0, _window.FullScreen);
		}

		_imgui.Render();

		if (_restartPending) {
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

	/// <summary>Runs the window until it closes, and leaves where it was for the next window to open at.</summary>
	public void Run() {
		_window.Run();
		_session.WindowPlacement = _window.Placement;
		_menuBar.Settings.Dispose();
	}

	public void Dispose() => _window.Dispose();

	// Settings asks from inside its ImGui window, and the close disposes the ImGui context, so the close waits for
	// DrawMenuBar to finish the frame.
	private void RequestRestart(Action restartRequested) {
		restartRequested();
		_restartPending = true;
	}
}
