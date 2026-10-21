using System.Numerics;
using Herculan.Engine.Input;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Platform;

/// <summary>
/// The first connected gamepad as a mouse: one stick moves the pointer, one trigger is held as the left button and the
/// other as the right, as <see cref="Settings"/> chooses, and its other buttons are read as a skip, so a front end or the
/// mission editor can be worked without a mouse (docs/herculan/herculan-key-bindings.md, "Gamepad in the front end").
/// This engine's own; retail has no gamepad. The stick moves the window's own cursor, so whatever reads the mouse sees the
/// pad's moves, and the mouse still works alongside it; each host decides what the held buttons reach.
///
/// <para><b>Where the cursor cannot be moved, or would move unseen, the pad moves a pointer of its own.</b> Wayland lets no client move a
/// visible pointer, and GLFW drops the warp there without an error Silk passes on, so each warp is read back. Once
/// <see cref="RefusedWarps"/> in a row are seen not to land, for the rest of the process a stick push hides the window's
/// cursor and puts the pointer at <see cref="Override"/>, which a host reads in place of the mouse's position
/// (<see cref="PositionOf"/>) and ImGui draws its own arrow at (<see cref="ScaledImGui.PointerOverride"/>); moving the
/// mouse brings the cursor back where the mouse is. Under gamescope, the Steam Deck's Game Mode, the same holds from the
/// start: the warps land, but gamescope draws the cursor itself and keeps it hidden while a controller is in use, so the
/// pointer would move unseen (<see cref="UnderGamescope"/>). Elsewhere the pointer stays the system's arrow, as retail's
/// is. A grabbed cursor (<see cref="CursorMode.Disabled"/>) is warped as before on every platform, since GLFW then moves only
/// its own virtual position, so mouse-look by stick needs no pointer of its own.</para>
/// </summary>
public sealed class GamepadPointer {
	// Where the stick starts counting, as a fraction of its travel, measured round the circle rather than per axis so a
	// diagonal is not slower than a straight push.
	private const float StickDeadzone = 0.2f;

	// How fast a stick pushed all the way moves the pointer at the default CursorSpeed, in window heights a second; the
	// shell's canvas is a fixed 4:3 scaled into the window, so this is the same pace across it at any window size. The
	// push is squared on its way to a speed, so a light push places the pointer finely.
	private const float FullSpeed = 1.2f;

	// The longest update the stick is integrated over, so a stall does not throw the pointer across the window.
	private const double LongestStep = 0.1;

	// How far a trigger has to be pulled to press, and let back to release, as fractions of its pull: apart, so a
	// trigger held near one point does not chatter.
	private const float PressPull = 0.5f;
	private const float ReleasePull = 0.25f;

	// How far the cursor read back after a warp may sit from where it was put and still count as moved: the window keeps
	// it in whole pixels, which rounds a fractional target by up to one each way.
	private const float WarpTolerance = 1.5f;

	/// <summary>
	/// How many warps in a row have to miss before the platform counts as refusing them: one can miss because the mouse
	/// moved between the warp and the read, and a platform that refuses them misses every one.
	/// </summary>
	public const int RefusedWarps = 3;

	// GLFW's standard gamepad layout: the left and right triggers, and sticks.
	private const int LeftTrigger = 0;
	private const int RightTrigger = 1;
	private const int LeftStick = 0;
	private const int RightStick = 1;

	// Whether the pad moves a pointer of its own rather than the window's cursor: from the start under gamescope, or once
	// RefusedWarps warps in a row have been read back somewhere else; kept for the process, since it is the platform's,
	// so the front end and the editor share it. _warpMisses counts the run; _announced is set once the log has said why.
	private static string? _ownPointerReason = UnderGamescope()
		? "gamescope shows the cursor only for a mouse's own moves"
		: null;
	private static bool _announced;
	private int _warpMisses;

	// Where the pad last put the pointer, in window coordinates, kept to a fraction of a pixel: the window rounds the
	// cursor to whole pixels, which would stall a slow push. Null until the pad first moves it, or once the mouse has
	// moved it since.
	private Vector2? _placed;

	// Whether the pointer is at _placed rather than at the window's cursor, which could not follow it; and the cursor as
	// last read meanwhile, so a move of the mouse hands the pointer back.
	private bool _owned;
	private Vector2 _cursorSeen;

	// Whether this class hid the window's cursor, so it shows it again only then.
	private bool _hidCursor;

	// Set while the pad is stood down, and kept until every button and both triggers are let go, so that a button still
	// held from working the menu bar does not click or skip once the bar is down.
	private bool _waitForRelease;

	/// <summary>Which stick moves the pointer and how fast, and which trigger is the left button; the player's, from the Controller window.</summary>
	public GamepadMouseSettings Settings { get; set; } = new();

	/// <summary>Whether the left button's trigger is pulled.</summary>
	public bool ClickHeld { get; private set; }

	/// <summary>Whether the right button's trigger is pulled.</summary>
	public bool RightClickHeld { get; private set; }

	/// <summary>
	/// Whether any button is held but Back, which raises a host's menu bar and so skips nothing, as [Shift+Esc] skips
	/// nothing.
	/// </summary>
	public bool SkipHeld { get; private set; }

	/// <summary>
	/// Where the pad has put the pointer, in window coordinates, while the window's cursor could not follow it and is
	/// hidden; null while the pointer is the cursor.
	/// </summary>
	public Vector2? Override => _owned ? _placed : null;

	/// <summary>The pointer in window coordinates: <see cref="Override"/>, or else where <paramref name="mouse"/> is.</summary>
	public Vector2 PositionOf(IMouse mouse) => Override ?? mouse.Position;

	/// <summary>
	/// Holds every button and trigger as let go until each is, as standing down does, for a host whose menus the pad
	/// worked while it stayed active.
	/// </summary>
	public void WaitForRelease() => _waitForRelease = true;

	/// <summary>
	/// Reads the pad, once an update before anything asks. While <paramref name="active"/> is clear, such as while the
	/// menu bar's own gamepad navigation has the pad, the pointer stays where it is and the triggers count as let go.
	/// <paramref name="confine"/> keeps the pointer inside <paramref name="client"/>; a host that has grabbed the cursor
	/// for mouse-look clears it, so the stick can turn the view as far as it likes.
	/// </summary>
	public void Update(IInputContext input, IMouse? mouse, Vector2D<int> client, double delta, bool active, bool confine = true) {
		if (mouse != null) {
			FollowMouse(mouse);
		}

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

		bool rightClicks = Settings.LeftClickTrigger == GamepadSide.Right;
		float clickPull = Pull(pad, rightClicks ? RightTrigger : LeftTrigger);
		float rightClickPull = Pull(pad, rightClicks ? LeftTrigger : RightTrigger);
		bool skip = pad.Buttons.Any(button => button.Pressed && button.Name != ButtonName.Back);

		if (_waitForRelease) {
			_waitForRelease = pad.Buttons.Any(button => button.Pressed) || clickPull >= ReleasePull || rightClickPull >= ReleasePull;
		}

		ClickHeld = !_waitForRelease && Held(ClickHeld, clickPull);
		RightClickHeld = !_waitForRelease && Held(RightClickHeld, rightClickPull);
		SkipHeld = !_waitForRelease && skip;

		int stickIndex = Settings.CursorStick == GamepadSide.Right ? RightStick : LeftStick;
		if (mouse != null && stickIndex < pad.Thumbsticks.Count) {
			var stick = pad.Thumbsticks[stickIndex];
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
		float speed = FullSpeed * Settings.CursorSpeed / GamepadMouseSettings.DefaultCursorSpeed;
		var step = stick / push * (amount * amount * speed * client.Y * (float)delta);

		// A pointer more than a pixel from where the pad left it was moved by the mouse, and the pad carries on from there.
		var from = _owned ? _placed!.Value
			: _placed is { } placed && Vector2.Distance(placed, mouse.Position) <= 1f ? placed : mouse.Position;
		var to = from + step;
		if (confine) {
			to = Vector2.Clamp(to, Vector2.Zero, new Vector2(client.X - 1, client.Y - 1));
		}

		_placed = to;
		bool grabbed = mouse.Cursor.CursorMode == CursorMode.Disabled;
		if (!grabbed && (_owned || _ownPointerReason != null)) {
			Own(mouse);
			return;
		}

		mouse.Position = to;
		if (grabbed) {
			return;
		}

		_warpMisses = Vector2.Distance(mouse.Position, to) > WarpTolerance ? _warpMisses + 1 : 0;
		if (_warpMisses >= RefusedWarps) {
			_ownPointerReason = "the window's cursor cannot be moved on this platform";
			Own(mouse);
		}
	}

	// The pointer taken off the window's cursor, which is hidden where the pad's pointer is drawn instead.
	private void Own(IMouse mouse) {
		if (!_announced) {
			_announced = true;
			Console.WriteLine($"Gamepad pointer: {_ownPointerReason}, so the pad draws a pointer of its own.");
		}

		_owned = true;
		_cursorSeen = mouse.Position;
		if (mouse.Cursor.CursorMode == CursorMode.Normal) {
			mouse.Cursor.CursorMode = CursorMode.Hidden;
			_hidCursor = true;
		}
	}

	/// <summary>
	/// Whether this process runs under gamescope, the compositor of the Steam Deck's Game Mode, which gives the programs it
	/// starts <c>GAMESCOPE_WAYLAND_DISPLAY</c>; the Game Mode session also names it as <c>XDG_CURRENT_DESKTOP</c>.
	/// </summary>
	public static bool UnderGamescope() =>
		OperatingSystem.IsLinux()
		&& (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"))
			|| string.Equals(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase));

	// While the pad has the pointer, a move of the window's cursor is the mouse's, or a grab's warp: either way the
	// pointer goes back to the cursor, shown again if this class hid it.
	private void FollowMouse(IMouse mouse) {
		if (!_owned || Vector2.Distance(mouse.Position, _cursorSeen) <= 0.5f) {
			return;
		}

		_owned = false;
		_placed = null;
		if (_hidCursor && mouse.Cursor.CursorMode == CursorMode.Hidden) {
			mouse.Cursor.CursorMode = CursorMode.Normal;
		}

		_hidCursor = false;
	}
}
