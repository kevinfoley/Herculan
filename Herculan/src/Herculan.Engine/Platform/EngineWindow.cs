using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Herculan.Engine.Platform;

/// <summary>
/// One turn in a <see cref="HostWindow"/>, with its own input context — the engine's platform surface, meant to be
/// driven by a thin front-end host rather than assuming it is the only thing running a loop (see
/// docs/herculan/planning.md, "Engine internal architecture"). A host that runs one turn after another hands each the
/// same <see cref="HostWindow"/>, and the window stays on screen between them; one that runs alone makes a window of
/// its own, which closes with it.
///
/// <para>It owns no scene state and does no drawing of its own: it raises <see cref="Load"/> once the turn starts,
/// with the GL context and the turn's input context, <see cref="Update"/> for simulation, and <see cref="Render"/>
/// for drawing, and leaves clearing and depth state to whatever renders (see <see cref="Render.SceneRenderer"/>).
/// That separation is what lets a second host — a mission editor, a screenshot tool — reuse the same engine
/// libraries without inheriting a game loop's assumptions.</para>
/// </summary>
public sealed class EngineWindow : IDisposable {
	private readonly HostWindow _host;
	private readonly bool _ownsHost;
	private string _title;
	private IInputContext? _input;

	/// <summary>Raised once, as the turn starts, with the GL context and the turn's input context.</summary>
	public event Action<GL, IInputContext>? Load;

	/// <summary>Raised each frame before <see cref="Render"/>, with the elapsed seconds.</summary>
	public event Action<double>? Update;

	/// <summary>Raised each frame to draw, with the elapsed seconds and the GL context.</summary>
	public event Action<double, GL>? Render;

	/// <summary>
	/// Raised once as the turn ends, by <see cref="Close"/> or the window closing, while the GL context is still
	/// active: everything the turn made on it is freed here, since the context outlives the turn.
	/// </summary>
	public event Action? Closing;

	/// <summary>Raised when the window gains or loses the focus during the turn.</summary>
	public event Action<bool>? FocusChanged;

	/// <summary>A turn in a window of its own, made for it and closed with it.</summary>
	/// <inheritdoc cref="HostWindow(int, int, WindowPlacement?, bool)"/>
	public EngineWindow(string title = "HERCULAN Engine", int width = 1280, int height = 960,
			WindowPlacement? placement = null, bool maximized = false)
		: this(new HostWindow(width, height, placement, maximized), title, ownsHost: true) {
	}

	/// <summary>A turn in <paramref name="host"/>, which stays open after it.</summary>
	public EngineWindow(HostWindow host, string title) : this(host, title, ownsHost: false) {
	}

	private EngineWindow(HostWindow host, string title, bool ownsHost) {
		_host = host;
		_title = title;
		_ownsHost = ownsHost;
	}

	/// <summary>The window this turn runs in.</summary>
	public HostWindow Host => _host;

	/// <inheritdoc cref="HostWindow.Placement"/>
	public WindowPlacement Placement => _host.Placement;

	/// <inheritdoc cref="HostWindow.FramebufferSize"/>
	public Vector2D<int> FramebufferSize => _host.FramebufferSize;

	/// <inheritdoc cref="HostWindow.ClientSize"/>
	public Vector2D<int> ClientSize => _host.ClientSize;

	/// <inheritdoc cref="HostWindow.ContentScale"/>
	public float ContentScale => _host.ContentScale;

	/// <inheritdoc cref="HostWindow.ClientScale"/>
	public float? ClientScale => _host.ClientScale;

	/// <inheritdoc cref="HostWindow.View"/>
	public IView View => _host.View;

	/// <inheritdoc cref="HostWindow.FullScreen"/>
	public bool FullScreen => _host.FullScreen;

	/// <summary>The window title while this turn runs, so a host can show live diagnostics without owning the window type.</summary>
	public string Title {
		get => _title;
		set {
			_title = value;
			_host.SetTitle(this, value);
		}
	}

	/// <summary>
	/// Asked when the player closes the window while this turn runs, before anything ends; false keeps the window and
	/// the turn open, as though it had not been closed. It is asked between frames, outside any ImGui frame, and only
	/// where the window can refuse a close (GLFW's, <see cref="HostWindow"/>); null lets every close through.
	/// </summary>
	public Func<bool>? CloseRequested { get; set; }

	/// <summary>Whether the turn has ended (<see cref="Closing"/>).</summary>
	internal bool Ended { get; private set; }

	/// <inheritdoc cref="HostWindow.ToggleFullScreen"/>
	public void ToggleFullScreen() => _host.ToggleFullScreen();

	/// <summary>
	/// <see cref="ToggleFullScreen()"/> with the pointer confined to the window and centred in it on the way
	/// into full screen, as both retail toggles do it, and released on the way out. VSHELL's
	/// <c>Display_ToggleFullScreen</c> (<c>00407085</c>) and DBSIM's <c>Video_ToggleFullscreen</c>
	/// (<c>004666c4</c>) each <c>ClipCursor</c> the pointer to the new display mode's screen and
	/// <c>SetCursorPos</c> it to the middle (docs/retail/shell/main-menu.md, "Full screen asks first";
	/// docs/retail/simulation/cockpit-input.md, "The two system buttons").
	/// </summary>
	/// <param name="pointer">The window's mouse, or null when it has none.</param>
	public void ToggleFullScreen(IMouse? pointer) {
		_host.ToggleFullScreen();
		if (pointer?.Cursor is { } cursor) {
			cursor.IsConfined = FullScreen;
		}

		if (FullScreen) {
			Centre(pointer);
		}
	}

	/// <summary>
	/// The start of a turn's own full-screen toggle, which in retail each executable makes on a window that has just
	/// opened windowed: <see cref="ToggleFullScreen(IMouse?)"/> when the window is not already as asked. A window the
	/// turn before left full screen stays so, with the pointer confined and centred again as the toggle would.
	/// </summary>
	/// <param name="fullScreen">Whether the turn starts full screen.</param>
	/// <param name="pointer">The window's mouse, or null when it has none.</param>
	public void StartFullScreen(bool fullScreen, IMouse? pointer) {
		if (fullScreen != FullScreen) {
			ToggleFullScreen(pointer);
		} else if (fullScreen) {
			if (pointer?.Cursor is { } cursor) {
				cursor.IsConfined = true;
			}

			Centre(pointer);
		}
	}

	private void Centre(IMouse? pointer) {
		if (pointer != null) {
			var client = ClientSize;
			pointer.Position = new System.Numerics.Vector2(client.X / 2, client.Y / 2);
		}
	}

	/// <inheritdoc cref="HostWindow.PresentBlack"/>
	public void PresentBlack() => _host.PresentBlack();

	/// <summary>Runs the turn until it ends; see <see cref="HostWindow"/>.</summary>
	public void Run() => _host.RunTurn(this);

	/// <summary>
	/// Ends the turn: <see cref="Closing"/> is raised at once, and <see cref="Run"/> returns before the next frame. The
	/// window itself stays open for the next turn, unless it is this turn's own.
	/// </summary>
	public void Close() => End();

	internal void Begin(GL gl, IInputContext input) {
		_input = input;
		Load?.Invoke(gl, input);
	}

	internal void RaiseUpdate(double deltaSeconds) {
		// Closed from here, on the main thread, as if the player had closed it.
		if (TerminationSignal.Requested) {
			End();
			return;
		}

		Update?.Invoke(deltaSeconds);
	}

	internal void RaiseRender(double deltaSeconds, GL gl) => Render?.Invoke(deltaSeconds, gl);

	internal void RaiseFocusChanged(bool focused) => FocusChanged?.Invoke(focused);

	internal void End() {
		if (Ended) {
			return;
		}

		Ended = true;
		Closing?.Invoke();
		if (_input != null) {
			// A pointer grabbed for looking is let go, and one hidden for a gamepad's own pointer (GamepadPointer) shown,
			// so the next turn does not inherit either; a full-screen confinement stays with the window.
			foreach (var mouse in _input.Mice) {
				if (mouse.Cursor.CursorMode is CursorMode.Disabled or CursorMode.Hidden) {
					mouse.Cursor.CursorMode = CursorMode.Normal;
				}
			}

			_input.Dispose();
			_input = null;
		}
	}

	public void Dispose() {
		if (_ownsHost) {
			_host.Dispose();
		}
	}
}
