using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Herculan.Engine.Platform;

/// <summary>
/// A Silk.NET window with an OpenGL context and an input context — the engine's platform surface,
/// meant to be driven by a thin front-end host rather than assuming it is the only thing running a
/// loop (see docs/herculan/planning.md, "Engine internal architecture").
///
/// <para>It owns no scene state and does no drawing of its own: it raises <see cref="Load"/> once
/// the GL and input contexts exist, <see cref="Update"/> for simulation, and <see cref="Render"/>
/// for drawing, and leaves clearing and depth state to whatever renders (see
/// <see cref="Render.SceneRenderer"/>). That separation is what lets a second host — a mission
/// editor, a screenshot tool — reuse the same engine libraries without inheriting a game loop's
/// assumptions.</para>
/// </summary>
public sealed class EngineWindow : IDisposable {
	private readonly IWindow _window;
	private GL? _gl;
	private IInputContext? _input;

	/// <summary>Raised once, after the GL and input contexts are created.</summary>
	public event Action<GL, IInputContext>? Load;

	/// <summary>Raised each frame before <see cref="Render"/>, with the elapsed seconds.</summary>
	public event Action<double>? Update;

	/// <summary>Raised each frame to draw, with the elapsed seconds and the GL context.</summary>
	public event Action<double, GL>? Render;

	/// <summary>Raised when the window is closing, while the GL context is still active.</summary>
	public event Action? Closing;

	/// <param name="width">The client width on a 100% display, scaled to the display the window opens on.</param>
	/// <param name="height">The client height on a 100% display, scaled likewise.</param>
	/// <param name="placement">
	/// Where an earlier window was when it closed, so the next one opens there at the same size and maximized if
	/// it was; null for the default size at the default place.
	/// </param>
	public EngineWindow(string title = "HERCULAN Engine", int width = 1280, int height = 960,
			WindowPlacement? placement = null) {
		_restoredPosition = placement?.Position;
		_restoredSize = placement?.Size ?? new Vector2D<int>(width, height);
		_scaleOnShow = placement == null;
		_maximizeOnShow = placement is { Maximized: true };
		var options = WindowOptions.Default with {
			Size = _restoredSize,
			Position = placement?.Position ?? WindowOptions.Default.Position,
			// Created hidden and shown by OnLoad once its icon is set: Alt+Tab keeps the icon it read when it first saw
			// the window. Maximized after showing rather than here, since a hidden window's maximize is reported as
			// a plain resize and would be taken for the restored size.
			IsVisible = false,
			Title = title,
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

		_window = Window.Create(options);
		_window.Load += OnLoad;
		_window.Update += OnUpdate;
		_window.Render += OnRender;
		_window.Closing += OnClosing;
		_window.Resize += OnResize;
		_window.Move += OnMove;
	}

	// The window's size and place while it is neither maximized, minimized nor full screen: what it goes back to,
	// and what the next window opens at.
	private Vector2D<int> _restoredSize;
	private Vector2D<int>? _restoredPosition;
	private readonly bool _maximizeOnShow;
	private readonly bool _scaleOnShow;
	private bool _maximizedBeforeFullScreen;
	private bool _maximizedAtClose;

	/// <summary>
	/// Where the window was when it closed, for the next window to open at: its restored size and place, and whether
	/// it was maximized. Full screen counts as the windowed state it was entered from.
	/// </summary>
	public WindowPlacement Placement => new(_restoredPosition, _restoredSize, _maximizedAtClose);
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

	/// <summary>
	/// The underlying view, for integrations that need it directly — e.g. ImGui's
	/// <c>ImGuiController</c>, which hooks resize itself rather than going through
	/// <see cref="Render"/>/<see cref="Update"/>.
	/// </summary>
	public IView View => _window;

	/// <summary>Window title, so a host can show live diagnostics without owning the window type.</summary>
	public string Title {
		get => _window.Title;
		set => _window.Title = value;
	}

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

		_printScreen?.Dispose();
		_printScreen = null;

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
	private PrintScreenCapture? _printScreen;
	private ExecutableIcon? _executableIcon;

	/// <summary>
	/// Raised with each frame [PrtScn] captures while full screen, as it goes on the clipboard: its width, height and
	/// pixels as <see cref="PrintScreenCapture"/>'s constructor describes them. Subscribed before going full screen.
	/// </summary>
	public event Action<int, int, byte[]>? PrintScreenCaptured;

	/// <summary>
	/// <see cref="ToggleFullScreen()"/> with the pointer confined to the window and centred in it on the way
	/// into full screen, as both retail toggles do it, and released on the way out. VSHELL's
	/// <c>Display_ToggleFullScreen</c> (<c>00407085</c>) and DBSIM's <c>Video_ToggleFullscreen</c>
	/// (<c>004666c4</c>) each <c>ClipCursor</c> the pointer to the new display mode's screen and
	/// <c>SetCursorPos</c> it to the middle (docs/retail/shell/screen-layout.md, "Full screen asks first";
	/// docs/retail/formats/cockpit-input.md, "The two system buttons").
	/// </summary>
	/// <param name="pointer">The window's mouse, or null when it has none.</param>
	public void ToggleFullScreen(IMouse? pointer) {
		ToggleFullScreen();
		if (pointer?.Cursor is { } cursor) {
			cursor.IsConfined = FullScreen;
		}

		if (FullScreen && pointer != null) {
			var client = ClientSize;
			pointer.Position = new System.Numerics.Vector2(client.X / 2, client.Y / 2);
		}
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

	public void Run() => _window.Run();

	public void Close() => _window.Close();

	private void OnLoad() {
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
		}

		_window.IsVisible = true;
		if (_maximizeOnShow) {
			_window.WindowState = WindowState.Maximized;
		}

		_gl = _window.CreateOpenGL();
		_input = _window.CreateInput();
		Load?.Invoke(_gl, _input);
	}

	private void OnUpdate(double deltaSeconds) => Update?.Invoke(deltaSeconds);

	private void OnRender(double deltaSeconds) {
		if (_gl != null) {
			Render?.Invoke(deltaSeconds, _gl);
			_printScreen?.CaptureIfRequested(_gl, FramebufferSize.X, FramebufferSize.Y);
		}
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

	private void OnClosing() {
		_maximizedAtClose = FullScreen ? _maximizedBeforeFullScreen : Shape().Maximized;
		Closing?.Invoke();
		_printScreen?.Dispose();
		_printScreen = null;
		_input?.Dispose();
		_input = null;
	}

	public void Dispose() {
		_printScreen?.Dispose();
		_input?.Dispose();
		_gl?.Dispose();
		_window.Dispose();
		// After the window, which uses the icons until it is destroyed.
		_executableIcon?.Dispose();
	}
}

/// <summary>Where a window sat, for the next one to open at (<see cref="EngineWindow.Placement"/>).</summary>
/// <param name="Position">The client area's top-left in screen coordinates, or null when it never moved from where it opened.</param>
/// <param name="Size">The client area's size when neither maximized nor full screen.</param>
/// <param name="Maximized">Whether it was maximized.</param>
public readonly record struct WindowPlacement(Vector2D<int>? Position, Vector2D<int> Size, bool Maximized);
