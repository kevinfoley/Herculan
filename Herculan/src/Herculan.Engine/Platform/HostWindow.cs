using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Herculan.Engine.Platform;

/// <summary>
/// The one native window, and its OpenGL context, that a host's turns run in one after another: the front end, a
/// mission, the mission editor, each an <see cref="EngineWindow"/> that attaches to it for its turn and lets go at the
/// end, so the window never closes between them and the desktop never shows through. It owns what outlives a turn:
/// the window's size and place, full screen, the icon and [PrtScn], and what is on screen between two turns
/// (<see cref="Hold"/>).
///
/// <para>The loop is driven here rather than by Silk's <c>Run</c>, which destroys the window when it returns: a turn
/// runs <see cref="IView.DoEvents"/>, <see cref="IView.DoUpdate"/> and <see cref="IView.DoRender"/> until it ends, and
/// the window stays. Between turns nothing pumps the window but <see cref="PumpEvents"/>, which a host calls between
/// the stages of a load.</para>
/// </summary>
public sealed class HostWindow : IDisposable {
	private readonly IWindow _window;
	private GL? _gl;
	private bool _opened;
	private EngineWindow? _turn;
	private Action<GL, Vector2D<int>>? _hold;

	/// <param name="width">The client width on a 100% display, scaled to the display the window opens on.</param>
	/// <param name="height">The client height on a 100% display, scaled likewise.</param>
	/// <param name="placement">
	/// Where an earlier window was when it closed, so this one opens there at the same size and maximized if it was;
	/// null for the default size at the default place.
	/// </param>
	/// <param name="maximized">Whether a window with no <paramref name="placement"/> opens maximized over its default size.</param>
	public HostWindow(int width = 1280, int height = 960, WindowPlacement? placement = null, bool maximized = false) {
		_restoredPosition = placement?.Position;
		_restoredSize = placement?.Size ?? new Vector2D<int>(width, height);
		_scaleOnShow = placement == null;
		_maximizeOnShow = placement?.Maximized ?? maximized;
		var options = WindowOptions.Default with {
			Size = _restoredSize,
			Position = placement?.Position ?? WindowOptions.Default.Position,
			// Created hidden and shown by OnLoad once its icon is set: Alt+Tab keeps the icon it read when it first saw
			// the window. Maximized after showing rather than here, since a hidden window's maximize is reported as
			// a plain resize and would be taken for the restored size.
			IsVisible = false,
			Title = "HERCULAN Engine",
			// Swaps wait for the display's vertical blank, so a frame never tears and the loop runs at the refresh
			// rate rather than as fast as it can.
			VSync = true,
			// Asked for explicitly rather than relying on the default, since a context without a
			// depth buffer fails silently: depth testing simply does nothing and the scene renders
			// as whatever was drawn last, which is a confusing symptom to chase.
			PreferredDepthBufferBits = 24,
			// What SceneRenderer paints a TSBSPPart's children in the original's order through.
			PreferredStencilBufferBits = 8,
		};

		TerminationSignal.Register();
		_window = Window.Create(options);
		_window.Load += OnLoad;
		_window.Update += delta => _turn?.RaiseUpdate(delta);
		_window.Render += OnRender;
		_window.Closing += OnNativeClosing;
		_window.Resize += OnResize;
		_window.Move += OnMove;
		_window.FocusChanged += focused => _turn?.RaiseFocusChanged(focused);
	}

	// The window's size and place while it is neither maximized, minimized nor full screen: what it goes back to,
	// and what the next window opens at.
	private Vector2D<int> _restoredSize;
	private Vector2D<int>? _restoredPosition;
	private readonly bool _maximizeOnShow;
	private readonly bool _scaleOnShow;
	private bool _maximizedBeforeFullScreen;
	private bool _maximizedAtTurnEnd;

	/// <summary>
	/// Whether the player has closed the window, or a termination signal has arrived (<see cref="TerminationSignal"/>):
	/// the turn running then ends, and no other should start.
	/// </summary>
	public bool Closed { get; private set; }

	/// <summary>
	/// Where the window was when the last turn ended, for the next window to open at: its restored size and place, and
	/// whether it was maximized. Full screen counts as the windowed state it was entered from.
	/// </summary>
	public WindowPlacement Placement => new(_restoredPosition, _restoredSize, _maximizedAtTurnEnd);

	/// <summary>Current framebuffer size in pixels — the viewport a renderer should draw into.</summary>
	public Vector2D<int> FramebufferSize => _window.FramebufferSize;

	/// <summary>
	/// Current client size in window coordinates, which is the space the pointer reports in. Equal to
	/// <see cref="FramebufferSize"/> except on a scaled display, where input has to be converted
	/// between the two.
	/// </summary>
	public Vector2D<int> ClientSize => _window.Size;

	/// <summary>
	/// The display's scale setting as a factor — 2.5 at Windows' 250% — in framebuffer pixels per pixel of a 100%
	/// display: what text and UI given in pixels are multiplied by to read the same on any display. The scale of the
	/// monitor under the window's centre, read from GLFW's monitor rather than its window (Silk binds no
	/// <c>glfwGetWindowContentScale</c>). 1 on a backend other than GLFW.
	/// </summary>
	public unsafe float ContentScale {
		get {
			if (_window.Native?.Glfw is not { } native) {
				return 1f;
			}

			var glfw = Glfw.GetApi();
			var monitor = MonitorUnderWindow(glfw, (WindowHandle*)native);
			if (monitor == null) {
				return 1f;
			}

			glfw.GetMonitorContentScale(monitor, out float scale, out _);
			return scale > 0f ? scale : 1f;
		}
	}

	/// <summary>
	/// <see cref="ContentScale"/> in window coordinates rather than framebuffer pixels: the two are equal on Windows,
	/// where window coordinates are pixels, while on a platform whose window coordinates are already scaled (macOS)
	/// this is 1. Null while the window has no area to measure, as when minimized.
	/// </summary>
	public float? ClientScale {
		get {
			var framebuffer = _window.FramebufferSize;
			var client = _window.Size;
			return framebuffer.X > 0 && client.X > 0 ? ContentScale * client.X / framebuffer.X : null;
		}
	}

	/// <summary>Whether the window has the keyboard focus. True on a backend other than GLFW.</summary>
	public unsafe bool IsFocused => _window.Native?.Glfw is not { } native
		|| Glfw.GetApi().GetWindowAttrib((WindowHandle*)native, WindowAttributeGetter.Focused);

	/// <summary>The underlying view, for integrations that need it directly, such as ImGui's <c>ImGuiController</c>.</summary>
	public IView View => _window;

	/// <summary>
	/// The title bar and taskbar icon for every window this process opens, at as many sizes as there are for the system
	/// to pick from: set once by the host before its first window loads. Empty leaves the platform's default. Unused on
	/// Windows when the executable has an icon of its own (<see cref="ExecutableIcon"/>).
	/// </summary>
	public static Silk.NET.Core.RawImage[] Icons { get; set; } = [];

	/// <summary>Whether <see cref="ToggleFullScreen"/> has the window covering its monitor.</summary>
	public bool FullScreen { get; private set; }

	private Vector2D<int> _windowedSize;

	/// <summary>
	/// Takes the window to full screen or back. Full screen is a borderless window covering the monitor under the
	/// window's centre at that monitor's current resolution, which changes no display mode and which Windows treats
	/// as full screen, taskbar included. It is not <c>glfwSetWindowMonitor</c> with a monitor (nor Silk's
	/// <see cref="WindowState.Fullscreen"/>, which makes that call on the primary monitor): GLFW keeps a window it
	/// gives a monitor topmost, so [Alt+Tab] would switch to another window without showing it. A borderless window
	/// sits in the ordinary order, so losing the focus leaves it full screen behind whatever took it. Back is the size
	/// the window had before, centred on the same monitor, maximized again if it was. While full screen, [PrtScn] is
	/// the window's own (<see cref="PrintScreenCapture"/>). Does nothing on a backend other than GLFW.
	/// </summary>
	public unsafe void ToggleFullScreen() {
		if (_window.Native?.Glfw is not { } native) {
			return;
		}

		var glfw = Glfw.GetApi();
		var handle = (WindowHandle*)native;

		if (!FullScreen) {
			var monitor = MonitorUnderWindow(glfw, handle);
			if (monitor == null) {
				return;
			}

			var mode = glfw.GetVideoMode(monitor);
			glfw.GetMonitorPos(monitor, out int x, out int y);
			_maximizedBeforeFullScreen = glfw.GetWindowAttrib(handle, WindowAttributeGetter.Maximized);
			_fullScreenMonitor = (nint)monitor;
			// Set first, so the resize and move to the monitor are not taken for the restored size and place.
			FullScreen = true;
			// A maximized window keeps its maximized place over any size it is given, so it is restored first.
			if (_maximizedBeforeFullScreen) {
				glfw.RestoreWindow(handle);
			}

			_windowedSize = _window.Size;
			glfw.SetWindowAttrib(handle, WindowAttributeSetter.Decorated, false);
			glfw.SetWindowMonitor(handle, null, x, y, mode->Width, mode->Height, Glfw.DontCare);
			if (OperatingSystem.IsWindows() && _window.Native?.Win32?.Hwnd is { } hwnd) {
				_printScreen = new PrintScreenCapture(hwnd,
					PrintScreenCaptured != null ? (w, h, rgb) => PrintScreenCaptured?.Invoke(w, h, rgb) : null);
			}

			return;
		}

		EndPrintScreen();

		var from = (Silk.NET.GLFW.Monitor*)_fullScreenMonitor;
		glfw.GetMonitorPos(from, out int monitorX, out int monitorY);
		var desktop = glfw.GetVideoMode(from);
		var back = new Vector2D<int>(monitorX + (desktop->Width - _windowedSize.X) / 2,
			monitorY + (desktop->Height - _windowedSize.Y) / 2);
		glfw.SetWindowAttrib(handle, WindowAttributeSetter.Decorated, true);
		glfw.SetWindowMonitor(handle, null, back.X, back.Y, _windowedSize.X, _windowedSize.Y, Glfw.DontCare);
		if (_maximizedBeforeFullScreen) {
			glfw.MaximizeWindow(handle);
		}

		FullScreen = false;
		if (!_maximizedBeforeFullScreen) {
			_restoredPosition = back;
			_restoredSize = _windowedSize;
		}
	}

	private nint _fullScreenMonitor;
	// Both made only on Windows, so null everywhere else; the checks where they are used say so to the compiler.
	private PrintScreenCapture? _printScreen;
	private ExecutableIcon? _executableIcon;

	private void EndPrintScreen() {
		if (OperatingSystem.IsWindows()) {
			_printScreen?.Dispose();
		}
		_printScreen = null;
	}

	/// <summary>
	/// Raised with each frame [PrtScn] captures while full screen, as it goes on the clipboard: its width, height and
	/// pixels as <see cref="PrintScreenCapture"/>'s constructor describes them. Subscribed before going full screen.
	/// </summary>
	public event Action<int, int, byte[]>? PrintScreenCaptured;

	/// <summary>
	/// What is on screen until the next turn draws its first frame: <paramref name="draw"/> over black, or black alone
	/// for null. Drawn now, and again by each <see cref="PumpEvents"/>, so it survives a resize meanwhile. Opens the
	/// window, black, if no turn has yet.
	/// </summary>
	public void Hold(Action<GL, Vector2D<int>>? draw) {
		_hold = draw;
		Present(Open());
	}

	/// <summary>
	/// Handles the window's waiting events between turns, as the original's <c>Win_PumpMessages</c> does between the
	/// stages of a load, and draws the <see cref="Hold"/> frame again. Closing the window meanwhile sets
	/// <see cref="Closed"/>. Nothing before the first turn or <see cref="Hold"/> has opened the window.
	/// </summary>
	public void PumpEvents() {
		if (!_opened || _turn != null) {
			return;
		}

		_window.DoEvents();
		if (_window.IsClosing || TerminationSignal.Requested) {
			Closed = true;
		}

		if (_gl != null && !Closed) {
			Present(_gl);
		}
	}

	/// <summary>Clears the window to black and shows it at once, without changing what <see cref="Hold"/> holds.</summary>
	public void PresentBlack() {
		var gl = Open();
		ClearToBlack(gl);
		_window.GLContext?.SwapBuffers();
	}

	/// <summary>
	/// Runs <paramref name="turn"/> until it ends (<see cref="EngineWindow.Close"/>) or the window is closed: a fresh
	/// input context, the turn's <see cref="EngineWindow.Load"/>, and then a frame at a time. A turn started after the
	/// window has been closed gets no frames, only its <see cref="EngineWindow.Closing"/>.
	/// </summary>
	internal void RunTurn(EngineWindow turn) {
		var gl = Open();
		if (Closed || TerminationSignal.Requested) {
			Closed = true;
			turn.End();
			return;
		}

		_turn = turn;
		_hold = null;
		_window.Title = turn.Title;
		ResetGlState(gl);
		turn.Begin(gl, _window.CreateInput());
		// A turn starts as if the window had just been given the focus, as a newly opened one is; one that lost it
		// during the load hears so before its first frame.
		if (!IsFocused) {
			turn.RaiseFocusChanged(false);
		}

		while (!turn.Ended) {
			_window.DoEvents();
			if (_window.IsClosing) {
				Closed = true;
				turn.End();
			}

			if (turn.Ended) {
				break;
			}

			_window.DoUpdate();
			if (!turn.Ended && !_window.IsClosing) {
				_window.DoRender();
			}
		}

		if (TerminationSignal.Requested) {
			Closed = true;
		}

		_maximizedAtTurnEnd = FullScreen ? _maximizedBeforeFullScreen : Shape().Maximized;
		_turn = null;
	}

	/// <summary>Sets the current turn's title, when it is the one running.</summary>
	internal void SetTitle(EngineWindow turn, string title) {
		if (ReferenceEquals(turn, _turn)) {
			_window.Title = title;
		}
	}

	// Creates the window the first time anything needs it: Silk's Initialize raises Load, which shows it.
	private GL Open() {
		if (!_opened) {
			_opened = true;
			_window.Initialize();
			if (_gl == null) {
				OnLoad();
			}
		}

		return _gl!;
	}

	// The state a turn finds the context in is the state the turn before left, so the switches the hosts' renderers
	// rely on starting as a new context's are put back as one has them.
	private static void ResetGlState(GL gl) {
		gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		gl.UseProgram(0);
		gl.BindVertexArray(0);
		gl.Disable(EnableCap.DepthTest);
		gl.Disable(EnableCap.Blend);
		gl.Disable(EnableCap.CullFace);
		gl.Disable(EnableCap.ScissorTest);
		gl.Disable(EnableCap.StencilTest);
		gl.DepthMask(true);
		gl.ColorMask(true, true, true, true);
		gl.ClearColor(0f, 0f, 0f, 0f);
	}

	private void ClearToBlack(GL gl) {
		var framebuffer = _window.FramebufferSize;
		gl.Disable(EnableCap.ScissorTest);
		gl.Viewport(0, 0, (uint)Math.Max(framebuffer.X, 1), (uint)Math.Max(framebuffer.Y, 1));
		gl.ClearColor(0f, 0f, 0f, 1f);
		gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
	}

	private void Present(GL gl) {
		ClearToBlack(gl);
		var framebuffer = _window.FramebufferSize;
		if (framebuffer.X > 0 && framebuffer.Y > 0) {
			_hold?.Invoke(gl, framebuffer);
		}

		_window.GLContext?.SwapBuffers();
	}

	/// <summary>The monitor whose area holds the window's centre, or the primary one when none does.</summary>
	private static unsafe Silk.NET.GLFW.Monitor* MonitorUnderWindow(Glfw glfw, WindowHandle* handle) {
		glfw.GetWindowPos(handle, out int x, out int y);
		glfw.GetWindowSize(handle, out int width, out int height);
		int centreX = x + width / 2;
		int centreY = y + height / 2;

		var monitors = glfw.GetMonitors(out int count);
		for (int i = 0; i < count; i++) {
			glfw.GetMonitorPos(monitors[i], out int monitorX, out int monitorY);
			var mode = glfw.GetVideoMode(monitors[i]);
			if (centreX >= monitorX && centreX < monitorX + mode->Width
					&& centreY >= monitorY && centreY < monitorY + mode->Height) {
				return monitors[i];
			}
		}

		return glfw.GetPrimaryMonitor();
	}

	/// <summary>
	/// Takes the default size, which is a 100% display's, to the display the hidden window was made on, shrunk where
	/// it would not fit that monitor's work area with its frame, keeping its shape, and centres the window in the work
	/// area. Does nothing on a backend other than GLFW.
	/// </summary>
	private unsafe void ScaleToDisplay() {
		if (_window.Native?.Glfw is not { } native || ClientScale is not { } scale) {
			return;
		}

		var glfw = Glfw.GetApi();
		var handle = (WindowHandle*)native;
		var monitor = MonitorUnderWindow(glfw, handle);
		if (monitor == null) {
			return;
		}

		glfw.GetMonitorWorkarea(monitor, out int areaX, out int areaY, out int areaWidth, out int areaHeight);
		glfw.GetWindowFrameSize(handle, out int left, out int top, out int right, out int bottom);
		var size = _window.Size;
		float fit = MathF.Min(scale, MathF.Min((areaWidth - left - right) / (float)size.X,
			(areaHeight - top - bottom) / (float)size.Y));
		var scaled = new Vector2D<int>((int)MathF.Round(size.X * fit), (int)MathF.Round(size.Y * fit));
		if (scaled.X <= 0 || scaled.Y <= 0) {
			scaled = size;
		}

		var position = new Vector2D<int>(areaX + left + (areaWidth - left - right - scaled.X) / 2,
			areaY + top + (areaHeight - top - bottom - scaled.Y) / 2);
		if (scaled != size) {
			_window.Size = scaled;
		}
		_window.Position = position;
		_restoredSize = scaled;
		_restoredPosition = position;
	}

	/// <summary>
	/// Centres the window in the primary monitor's work area when its top-left lies in no monitor's work area, as a
	/// placement saved before a monitor was unplugged can. Does nothing on a backend other than GLFW.
	/// </summary>
	private unsafe void KeepOnScreen() {
		if (_window.Native?.Glfw is not { } native || _restoredPosition is not { } position) {
			return;
		}

		var glfw = Glfw.GetApi();
		var monitors = glfw.GetMonitors(out int count);
		for (int i = 0; i < count; i++) {
			glfw.GetMonitorWorkarea(monitors[i], out int x, out int y, out int width, out int height);
			if (position.X >= x && position.X < x + width && position.Y >= y && position.Y < y + height) {
				return;
			}
		}

		var primary = glfw.GetPrimaryMonitor();
		if (primary == null) {
			return;
		}

		glfw.GetMonitorWorkarea(primary, out int areaX, out int areaY, out int areaWidth, out int areaHeight);
		glfw.GetWindowFrameSize((WindowHandle*)native, out int left, out int top, out int right, out int bottom);
		var size = _window.Size;
		var centred = new Vector2D<int>(areaX + left + Math.Max(0, (areaWidth - left - right - size.X) / 2),
			areaY + top + Math.Max(0, (areaHeight - top - bottom - size.Y) / 2));
		_window.Position = centred;
		_restoredPosition = centred;
	}

	private void OnLoad() {
		if (_gl != null) {
			return;
		}

		// Set here rather than in the constructor because the native window does not exist until the window loads.
		// On Windows the executable's own icon comes first (ExecutableIcon says why). An icon is cosmetic, so a platform
		// that refuses one (GLFW reports Wayland and macOS as unable) is not an error.
		if (OperatingSystem.IsWindows() && _window.Native?.Win32?.Hwnd is { } hwnd) {
			_executableIcon = ExecutableIcon.TrySet(hwnd);
		}

		if (_executableIcon == null && Icons.Length > 0) {
			try {
				_window.SetWindowIcon(Icons);
			} catch (GlfwException e) {
				Console.Error.WriteLine($"Window icon not set: {e.Message}");
			}
		}

		if (_scaleOnShow) {
			ScaleToDisplay();
		} else {
			KeepOnScreen();
		}

		_window.IsVisible = true;
		if (_maximizeOnShow) {
			_window.WindowState = WindowState.Maximized;
		}

		// Black at once, so the window shows nothing of its own while the first turn loads.
		_gl = _window.CreateOpenGL();
		Present(_gl);
	}

	private void OnRender(double deltaSeconds) {
		if (_gl != null && _turn != null) {
			_turn.RaiseRender(deltaSeconds, _gl);
			if (OperatingSystem.IsWindows()) {
				_printScreen?.CaptureIfRequested(_gl, FramebufferSize.X, FramebufferSize.Y);
			}
		}
	}

	// The player closing the window, or Silk's own close as the window is destroyed: either way no turn runs after.
	private void OnNativeClosing() {
		Closed = true;
		_turn?.End();
	}

	// Whether the window is maximized or minimized, asked of GLFW rather than Silk's WindowState, which is updated
	// by a callback of its own and can still be the old state while a resize to or from maximized is reported.
	private unsafe (bool Maximized, bool Minimized) Shape() {
		if (_window.Native?.Glfw is not { } native) {
			return (_window.WindowState == WindowState.Maximized, _window.WindowState == WindowState.Minimized);
		}

		var glfw = Glfw.GetApi();
		var handle = (WindowHandle*)native;
		return (glfw.GetWindowAttrib(handle, WindowAttributeGetter.Maximized),
			glfw.GetWindowAttrib(handle, WindowAttributeGetter.Iconified));
	}

	private void OnResize(Vector2D<int> size) {
		if (!FullScreen && Shape() is (false, false) && size.X > 0 && size.Y > 0) {
			_restoredSize = size;
		}
	}

	private void OnMove(Vector2D<int> position) {
		if (!FullScreen && Shape() is (false, false)) {
			_restoredPosition = position;
		}
	}

	public void Dispose() {
		EndPrintScreen();
		if (_opened) {
			_window.Reset();
		}
		_gl?.Dispose();
		_window.Dispose();
		// After the window, which uses the icons until it is destroyed.
		if (OperatingSystem.IsWindows()) {
			_executableIcon?.Dispose();
		}
	}
}

/// <summary>Where a window sat, for the next one to open at (<see cref="HostWindow.Placement"/>).</summary>
/// <param name="Position">The client area's top-left in screen coordinates, or null when it never moved from where it opened.</param>
/// <param name="Size">The client area's size when neither maximized nor full screen.</param>
/// <param name="Maximized">Whether it was maximized.</param>
public readonly record struct WindowPlacement(Vector2D<int>? Position, Vector2D<int> Size, bool Maximized);
