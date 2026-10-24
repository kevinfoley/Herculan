using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Simulator;

/// <summary>The simulator's input off the window's own devices, or a replaying tape's.</summary>
sealed class SimulatorInput : ISimulatorInput {
	private readonly EngineWindow _window;
	private readonly TapePlayback _tape;
	private readonly Func<FrameFit> _fit;

	// Whether a warped pointer is drawn by this engine rather than left to the system: under gamescope, which shows its
	// cursor only for a mouse's own moves (GamepadPointer.UnderGamescope).
	private static readonly bool DrawsWarpedPointer = GamepadPointer.UnderGamescope();

	// Where the last warp put the window's cursor, in window coordinates, while DrawsWarpedPointer; null otherwise.
	private System.Numerics.Vector2? _warpedTo;

	/// <param name="fit">Where the mission's frame sits in the window this frame, which the pointer is placed in.</param>
	public SimulatorInput(EngineWindow window, TapePlayback tape, CockpitInput cockpitInput, Func<FrameFit> fit) {
		_window = window;
		_tape = tape;
		Cockpit = cockpitInput;
		_fit = fit;
	}

	/// <inheritdoc/>
	public IKeyState? Keyboard { get; private set; }

	/// <inheritdoc cref="ISimulatorInput.LiveKeys"/>
	public LiveKeys? LiveKeys { get; private set; }

	IKeyState? ISimulatorInput.LiveKeys => LiveKeys;

	/// <inheritdoc/>
	public IMouse? Mouse { get; private set; }

	/// <inheritdoc/>
	public CockpitInput Cockpit { get; }

	/// <summary>The debug UI, once the window has a GL context to build it on.</summary>
	public ScaledImGui? ImGui { get; set; }

	/// <summary>
	/// A live mouse event, in pixels of the mission's frame, as it went into <see cref="Cockpit"/>, with the frame's
	/// size: what a recording puts on its tape.
	/// </summary>
	public event Action<float, float, CockpitMouseButtons, int, int>? MouseQueued;

	/// <inheritdoc/>
	public bool ImGuiHasKeyboard => ImGui != null && ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;

	/// <inheritdoc/>
	public bool ImGuiWantsMouse => ImGui != null && ImGuiNET.ImGui.GetIO().WantCaptureMouse;

	/// <inheritdoc/>
	public bool KeyboardCapturedByImGui => !_tape.Playing && ImGuiHasKeyboard;

	/// <summary>Takes the window's devices, once its input context exists.</summary>
	public void Attach(IInputContext input) {
		LiveKeys = input.Keyboards.Count > 0 ? new LiveKeys(input.Keyboards[0]) : null;
		Keyboard = _tape.Keys ?? (IKeyState?)LiveKeys;
		if (LiveKeys != null) {
			_tape.ListenTo(LiveKeys);
		}

		// Mouse events are queued here and nowhere else: everything that decides what a click means runs
		// once per frame in Update, out of CockpitInput.Drain. That is the original's own split — its
		// listener callback pushes a record and returns, and CockpitMouse_ProcessQueue does the work a
		// frame later (docs/retail/simulation/cockpit-input.md §3-4).
		if (input.Mice.Count > 0) {
			Mouse = input.Mice[0];

			// The pointer reports window-client pixels while the cockpit is placed in pixels of the mission's
			// frame, which differ on a high-DPI display and at 640x480 (the high-resolution tweak off). Rescaling
			// here is the same correction the original makes for the same reason — Mouse_RecomputeScale (0048078c)
			// converts client coordinates into the game's own space before any listener sees them (§2).
			void Queue(IMouse m, CockpitMouseButtons buttons) {
				if (_tape.Playing) {
					return;
				}

				var fit = _fit();
				var (x, y) = ToFrame(m.Position, fit);
				Cockpit.Enqueue(x, y, buttons);
				MouseQueued?.Invoke(x, y, buttons, fit.Frame.X, fit.Frame.Y);
			}

			// The mask is built from the event's own button rather than read back off the device, so it
			// does not depend on whether Silk.NET updates the held state before or after it raises.
			Mouse.MouseMove += (m, _) => Queue(m, ButtonsHeld(m));
			Mouse.MouseDown += (m, button) => Queue(m, ButtonsHeld(m) | ButtonFlag(button));
			Mouse.MouseUp += (m, button) => Queue(m, ButtonsHeld(m) & ~ButtonFlag(button));
		}
	}

	/// <inheritdoc/>
	public void TakeLiveKeys() => Keyboard = LiveKeys;

	/// <summary>
	/// Where the pointer is and what it holds, in pixels of the mission's frame: the live mouse, or during a replay
	/// wherever the tape's own mouse events have left it.
	/// </summary>
	public (float X, float Y, CockpitMouseButtons Buttons) Pointer() {
		if (_tape.Playing) {
			return _tape.Pointer;
		}

		if (Mouse == null) {
			return (float.NaN, float.NaN, CockpitMouseButtons.None);
		}

		var (x, y) = ToFrame(Mouse.Position, _fit());
		return (x, y, ButtonsHeld(Mouse));
	}

	/// <summary>
	/// Puts the pointer on a pixel of the mission's frame — <c>Mouse_WarpCursorToPoint</c>'s (<c>004807d0</c>) conversion back to
	/// client coordinates and its <c>SetCursorPos</c>, which the original gates on a live mouse device. During a replay
	/// the tape owns the pointer and the window's own mouse is left alone, which is a divergence from retail: the
	/// original's replay warps too, then sets the position from each mouse event the tape carries, and this
	/// engine's tapes carry a position with every press.
	/// </summary>
	public void WarpPointer(float x, float y) {
		if (_tape.Playing || Mouse == null || float.IsNaN(x) || float.IsNaN(y)) {
			return;
		}

		var fit = _fit();
		var (windowX, windowY) = fit.ToWindow(x, y);
		var client = _window.ClientSize;
		Mouse.Position = new System.Numerics.Vector2(
			windowX * client.X / Math.Max(fit.Window.X, 1),
			windowY * client.Y / Math.Max(fit.Window.Y, 1));
		if (DrawsWarpedPointer) {
			_warpedTo = Mouse.Position;
		}
	}

	/// <summary>
	/// Where <see cref="WarpPointer"/> last put the cursor, in window coordinates, where the system will not show it there
	/// and this engine has to draw it: under gamescope, the Steam Deck's Game Mode, whose cursor stays hidden while a
	/// controller is in use, so a panel's focus moved by the pad would leave the pointer unseen on its button. Null
	/// elsewhere, and once the mouse has moved the cursor since, which gamescope then shows itself.
	/// </summary>
	public System.Numerics.Vector2? WarpedPointer =>
		_warpedTo is { } warped && Mouse != null && System.Numerics.Vector2.Distance(Mouse.Position, warped) <= 0.5f
			? warped
			: null;

	/// <summary>A window-client position as a pixel of the mission's frame, through the framebuffer's pixels.</summary>
	private (float X, float Y) ToFrame(System.Numerics.Vector2 position, FrameFit fit) {
		var client = _window.ClientSize;
		return fit.ToFrame(position.X * fit.Window.X / Math.Max(client.X, 1),
			position.Y * fit.Window.Y / Math.Max(client.Y, 1));
	}

	/// <summary>Every mouse button currently held, as the cockpit's own flag pair.</summary>
	private static CockpitMouseButtons ButtonsHeld(IMouse mouse) =>
		(mouse.IsButtonPressed(MouseButton.Left) ? CockpitMouseButtons.Left : CockpitMouseButtons.None)
		| (mouse.IsButtonPressed(MouseButton.Right) ? CockpitMouseButtons.Right : CockpitMouseButtons.None);

	/// <summary>One button as its flag. Anything but left and right is <see cref="CockpitMouseButtons.None"/> — the original watches only those two.</summary>
	private static CockpitMouseButtons ButtonFlag(MouseButton button) => button switch {
		MouseButton.Left => CockpitMouseButtons.Left,
		MouseButton.Right => CockpitMouseButtons.Right,
		_ => CockpitMouseButtons.None,
	};
}
