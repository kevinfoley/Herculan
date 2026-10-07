using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// Where the simulator's input comes from: the window's keyboard and mouse, or a replaying tape's, and
/// whether the debug UI has taken either. Every handler reads <see cref="Keyboard"/> and
/// <see cref="Pointer"/> rather than a device, so a replay drives exactly the code a player does.
/// </summary>
sealed class SimulatorInput : IPointerDevice {
	private readonly EngineWindow _window;
	private readonly TapePlayback _tape;

	public SimulatorInput(EngineWindow window, TapePlayback tape, CockpitInput cockpitInput) {
		_window = window;
		_tape = tape;
		Cockpit = cockpitInput;
	}

	/// <summary>
	/// What every key handler reads: the window's own keyboard, or a replaying tape's keystrokes. <see cref="LiveKeys"/>
	/// stays the window's throughout, because a replay still listens to it for [Ctrl+E], for -D's any-key abort and
	/// for the menu bar.
	/// </summary>
	public IKeyState? Keyboard { get; private set; }

	public LiveKeys? LiveKeys { get; private set; }

	public IMouse? Mouse { get; private set; }

	/// <summary>The cockpit's click queue, which both the live mouse and a tape's mouse events feed.</summary>
	public CockpitInput Cockpit { get; }

	/// <summary>The debug UI, once the window has a GL context to build it on.</summary>
	public ScaledImGui? ImGui { get; set; }

	/// <summary>
	/// A live mouse event, in framebuffer pixels, as it went into <see cref="Cockpit"/>, with the framebuffer's
	/// size: what a recording puts on its tape.
	/// </summary>
	public event Action<float, float, CockpitMouseButtons, int, int>? MouseQueued;

	public bool ImGuiHasKeyboard => ImGui != null && ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;

	public bool ImGuiWantsMouse => ImGui != null && ImGuiNET.ImGui.GetIO().WantCaptureMouse;

	/// <summary>
	/// Whether the debug UI is typing and the game's keys should go dead. Never during a replay: the tape's
	/// keystrokes are not the player's, and the player typing into the debug panel must not lose them.
	/// </summary>
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
		// frame later (docs/retail/formats/cockpit-input.md §3-4).
		if (input.Mice.Count > 0) {
			Mouse = input.Mice[0];

			// The pointer reports window-client pixels while the cockpit is placed in framebuffer pixels,
			// which differ on a high-DPI display. Rescaling here is the same correction the original makes
			// for the same reason — Mouse_RecomputeScale (0048078c) converts client coordinates into the
			// game's own space before any listener sees them (§2).
			void Queue(IMouse m, CockpitMouseButtons buttons) {
				if (_tape.Playing) {
					return;
				}

				var client = _window.ClientSize;
				var framebuffer = _window.FramebufferSize;
				float x = m.Position.X * framebuffer.X / Math.Max(client.X, 1);
				float y = m.Position.Y * framebuffer.Y / Math.Max(client.Y, 1);
				Cockpit.Enqueue(x, y, buttons);
				MouseQueued?.Invoke(x, y, buttons, framebuffer.X, framebuffer.Y);
			}

			// The mask is built from the event's own button rather than read back off the device, so it
			// does not depend on whether Silk.NET updates the held state before or after it raises.
			Mouse.MouseMove += (m, _) => Queue(m, ButtonsHeld(m));
			Mouse.MouseDown += (m, button) => Queue(m, ButtonsHeld(m) | ButtonFlag(button));
			Mouse.MouseUp += (m, button) => Queue(m, ButtonsHeld(m) & ~ButtonFlag(button));
		}
	}

	/// <summary>Hands the keyboard back to the player once a tape has stopped — -p's hand-over.</summary>
	public void TakeLiveKeys() => Keyboard = LiveKeys;

	/// <summary>
	/// Where the pointer is and what it holds, in framebuffer pixels: the live mouse, or during a replay
	/// wherever the tape's own mouse events have left it.
	/// </summary>
	public (float X, float Y, CockpitMouseButtons Buttons) Pointer() {
		if (_tape.Playing) {
			return _tape.Pointer;
		}

		if (Mouse == null) {
			return (float.NaN, float.NaN, CockpitMouseButtons.None);
		}

		var client = _window.ClientSize;
		var framebuffer = _window.FramebufferSize;
		return (Mouse.Position.X * framebuffer.X / Math.Max(client.X, 1),
			Mouse.Position.Y * framebuffer.Y / Math.Max(client.Y, 1),
			ButtonsHeld(Mouse));
	}

	/// <summary>
	/// Puts the pointer on a framebuffer pixel — <c>Mouse_WarpCursorToPoint</c>'s (<c>004807d0</c>) conversion back to
	/// client coordinates and its <c>SetCursorPos</c>, which the original gates on a live mouse device. During a replay
	/// the tape owns the pointer and the window's own mouse is left alone, which is a divergence from retail: the
	/// original's replay warps too, then sets the position from each mouse event the tape carries, and this
	/// engine's tapes carry a position with every press.
	/// </summary>
	public void WarpPointer(float x, float y) {
		if (_tape.Playing || Mouse == null || float.IsNaN(x) || float.IsNaN(y)) {
			return;
		}

		var client = _window.ClientSize;
		var framebuffer = _window.FramebufferSize;
		Mouse.Position = new System.Numerics.Vector2(
			x * client.X / Math.Max(framebuffer.X, 1),
			y * client.Y / Math.Max(framebuffer.Y, 1));
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
