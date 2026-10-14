using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Platform;

/// <summary>
/// The first connected gamepad as a mouse: its left stick moves the pointer, its right trigger is held as the left
/// button and its left trigger as the right, and its other buttons are read as a skip, so a front end or the mission
/// editor can be worked without a mouse (docs/herculan/herculan-key-bindings.md, "Gamepad in the front end"). This
/// engine's own; retail has no gamepad. The stick moves the window's own cursor, so whatever reads the mouse sees the
/// pad's moves, and the mouse still works alongside it; each host decides what the held buttons reach.
/// </summary>
public sealed class GamepadPointer {
	// Where the stick starts counting, as a fraction of its travel, measured round the circle rather than per axis so a
	// diagonal is not slower than a straight push.
	private const float StickDeadzone = 0.2f;

	// How fast a stick pushed all the way moves the pointer, in window heights a second; the shell's canvas is a fixed
	// 4:3 scaled into the window, so this is the same pace across it at any window size. The push is squared on its way
	// to a speed, so a light push places the pointer finely.
	private const float FullSpeed = 1.2f;

	// The longest update the stick is integrated over, so a stall does not throw the pointer across the window.
	private const double LongestStep = 0.1;

	// How far a trigger has to be pulled to press, and let back to release, as fractions of its pull: apart, so a
	// trigger held near one point does not chatter.
	private const float PressPull = 0.5f;
	private const float ReleasePull = 0.25f;

	// GLFW's standard gamepad layout: the left and right triggers.
	private const int LeftTrigger = 0;
	private const int RightTrigger = 1;

	// Where the pad last put the pointer, in window coordinates, kept to a fraction of a pixel: the window rounds the
	// cursor to whole pixels, which would stall a slow push. Null until the pad first moves it, or once the mouse has
	// moved it since.
	private Vector2? _placed;

	// Set while the pad is stood down, and kept until every button and both triggers are let go, so that a button still
	// held from working the menu bar does not click or skip once the bar is down.
	private bool _waitForRelease;

	/// <summary>Whether the right trigger is pulled as the left button.</summary>
	public bool ClickHeld { get; private set; }

	/// <summary>Whether the left trigger is pulled as the right button.</summary>
	public bool RightClickHeld { get; private set; }

	/// <summary>
	/// Whether any button is held but Back, which raises a host's menu bar and so skips nothing, as [Shift+Esc] skips
	/// nothing.
	/// </summary>
	public bool SkipHeld { get; private set; }

	/// <summary>
	/// Reads the pad, once an update before anything asks. While <paramref name="active"/> is clear, such as while the
	/// menu bar's own gamepad navigation has the pad, the pointer stays where it is and the triggers count as let go.
	/// <paramref name="confine"/> keeps the pointer inside <paramref name="client"/>; a host that has grabbed the cursor
	/// for mouse-look clears it, so the stick can turn the view as far as it likes.
	/// </summary>
	public void Update(IInputContext input, IMouse? mouse, Vector2D<int> client, double delta, bool active, bool confine = true) {
		var pad = active ? input.Gamepads.FirstOrDefault(gamepad => gamepad.IsConnected) : null;
		if (pad == null) {
			ClickHeld = false;
			RightClickHeld = false;
			SkipHeld = false;
			_waitForRelease = !active;
			return;
		}

		// Silk's own deadzone is off, as the joystick layer turns it off: the stick's band is this class's own.
		pad.Deadzone = new Deadzone(0f, DeadzoneMethod.Traditional);

		float rightPull = Pull(pad, RightTrigger);
		float leftPull = Pull(pad, LeftTrigger);
		bool skip = pad.Buttons.Any(button => button.Pressed && button.Name != ButtonName.Back);

		if (_waitForRelease) {
			_waitForRelease = pad.Buttons.Any(button => button.Pressed) || rightPull >= ReleasePull || leftPull >= ReleasePull;
		}

		ClickHeld = !_waitForRelease && Held(ClickHeld, rightPull);
		RightClickHeld = !_waitForRelease && Held(RightClickHeld, leftPull);
		SkipHeld = !_waitForRelease && skip;

		if (mouse != null && pad.Thumbsticks.Count > 0) {
			var stick = pad.Thumbsticks[0];
			Move(mouse, new Vector2(stick.X, stick.Y), client, Math.Min(delta, LongestStep), confine);
		}
	}

	// GLFW reads a trigger at rest as -1 and pulled all the way as +1.
	private static float Pull(IGamepad pad, int trigger) =>
		trigger < pad.Triggers.Count ? (pad.Triggers[trigger].Position + 1f) / 2f : 0f;

	private static bool Held(bool wasHeld, float pull) => wasHeld ? pull >= ReleasePull : pull >= PressPull;

	private void Move(IMouse mouse, Vector2 stick, Vector2D<int> client, double delta, bool confine) {
		float push = stick.Length();
		if (push <= StickDeadzone || client.X <= 0 || client.Y <= 0) {
			return;
		}

		float amount = Math.Min((push - StickDeadzone) / (1f - StickDeadzone), 1f);
		var step = stick / push * (amount * amount * FullSpeed * client.Y * (float)delta);

		// A pointer more than a pixel from where the pad left it was moved by the mouse, and the pad carries on from there.
		var from = _placed is { } placed && Vector2.Distance(placed, mouse.Position) <= 1f ? placed : mouse.Position;
		var to = from + step;
		if (confine) {
			to = Vector2.Clamp(to, Vector2.Zero, new Vector2(client.X - 1, client.Y - 1));
		}

		mouse.Position = to;
		_placed = to;
	}
}
