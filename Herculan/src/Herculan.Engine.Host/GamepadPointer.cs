using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Host;

/// <summary>
/// The first connected gamepad as a front end's mouse: its left stick moves the pointer, its right trigger is held as
/// the left button (<see cref="PolledButtons.AlsoLeft"/>), and its buttons skip as a skip key does
/// (<see cref="PolledButtons.AlsoSkip"/>), so the shell can be worked without a mouse
/// (docs/herculan/herculan-key-bindings.md, "Gamepad in the front end"). This engine's own; retail has no gamepad. The
/// stick moves the window's own cursor, so whatever reads the mouse sees the pad's moves, and the mouse still works
/// alongside it.
/// </summary>
sealed class GamepadPointer {
	// Where the stick starts counting, as a fraction of its travel, measured round the circle rather than per axis so a
	// diagonal is not slower than a straight push.
	private const float StickDeadzone = 0.2f;

	// How fast a stick pushed all the way moves the pointer, in window heights a second; the canvas is a fixed 4:3 scaled
	// into the window, so this is the same pace across the shell at any window size. The push is squared on its way to
	// a speed, so a light push places the pointer finely.
	private const float FullSpeed = 1.2f;

	// The longest update the stick is integrated over, so a stall does not throw the pointer across the window.
	private const double LongestStep = 0.1;

	// How far the trigger has to be pulled to press, and let back to release, as fractions of its pull: apart, so a
	// trigger held near one point does not chatter.
	private const float PressPull = 0.5f;
	private const float ReleasePull = 0.25f;

	// Where the pad last put the pointer, in window coordinates, kept to a fraction of a pixel: the window rounds the
	// cursor to whole pixels, which would stall a slow push. Null until the pad first moves it, or once the mouse has
	// moved it since.
	private Vector2? _placed;

	// Set while the pad is stood down, and kept until every button and the trigger are let go, so that a button still
	// held from working the menu bar does not click or skip once the bar is down.
	private bool _waitForRelease;

	/// <summary>Whether the trigger is pulled as the left button.</summary>
	public bool ClickHeld { get; private set; }

	/// <summary>
	/// Whether any button is held but Back, which raises the menu bar (<see cref="GamepadHostButtons"/>) and so skips
	/// nothing, as [Shift+Esc] skips nothing.
	/// </summary>
	public bool SkipHeld { get; private set; }

	/// <summary>
	/// Reads the pad, once an update before anything asks. While <paramref name="active"/> is clear, such as while the
	/// menu bar's own gamepad navigation has the pad, the pointer stays where it is and the trigger counts as let go.
	/// </summary>
	public void Update(IInputContext input, IMouse? mouse, Vector2D<int> client, double delta, bool active) {
		var pad = active ? ControllerDevice.Connected(input).FirstOrDefault(device => device.IsGamepad) : null;
		if (pad == null) {
			ClickHeld = false;
			SkipHeld = false;
			_waitForRelease = !active;
			return;
		}

		// GLFW reads a trigger at rest as -1 and pulled all the way as +1.
		float pull = (pad.Axis(5) + 1f) / 2f;
		bool skip = false;
		for (int i = 0; i < pad.ButtonCount; i++) {
			skip |= i != ControllerDevice.GamepadBack && pad.Button(i);
		}

		if (_waitForRelease) {
			_waitForRelease = skip || pad.Button(ControllerDevice.GamepadBack) || pull >= ReleasePull;
		}

		ClickHeld = !_waitForRelease && (ClickHeld ? pull >= ReleasePull : pull >= PressPull);
		SkipHeld = !_waitForRelease && skip;

		if (mouse != null) {
			Move(mouse, new Vector2(pad.Axis(0), pad.Axis(1)), client, Math.Min(delta, LongestStep));
		}
	}

	private void Move(IMouse mouse, Vector2 stick, Vector2D<int> client, double delta) {
		float push = stick.Length();
		if (push <= StickDeadzone || client.X <= 0 || client.Y <= 0) {
			return;
		}

		float amount = Math.Min((push - StickDeadzone) / (1f - StickDeadzone), 1f);
		var step = stick / push * (amount * amount * FullSpeed * client.Y * (float)delta);

		// A pointer more than a pixel from where the pad left it was moved by the mouse, and the pad carries on from there.
		var from = _placed is { } placed && Vector2.Distance(placed, mouse.Position) <= 1f ? placed : mouse.Position;
		var to = Vector2.Clamp(from + step, Vector2.Zero, new Vector2(client.X - 1, client.Y - 1));
		mouse.Position = to;
		_placed = to;
	}
}
