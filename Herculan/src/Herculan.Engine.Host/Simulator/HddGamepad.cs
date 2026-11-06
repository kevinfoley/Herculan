using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// A gamepad on the Heads-Down Display (docs/herculan/herculan-key-bindings.md, "The Heads-Down Display on a gamepad"):
/// the stick and triggers the Controller window's Mouse emulation names work the pointer as they do in the front end
/// (<see cref="GamepadPointer"/>), and the other stick holds the page's arrow keys, with the keyboard's own auto-repeat.
/// While it has them, and after it until both sticks are centred and both triggers let go, the mission's stick reads
/// them at rest (<see cref="JoystickSource.PadAxesMuted"/>), so the HERC neither steers, aims nor fires from them. This
/// engine's own; retail has no gamepad.
/// </summary>
sealed class HddGamepad {
	// How far the arrow stick has to be pushed to hold an arrow, and let back to release it, as fractions of its
	// travel round the circle: apart, so a stick held near one point does not chatter.
	private const float ArrowPress = 0.5f;
	private const float ArrowRelease = 0.35f;

	// How near the centre a stick, and how little pulled a trigger, counts as let go for handing the axes back.
	private const float StickAtRest = 0.2f;
	private const float TriggerAtRest = 0.25f;

	// GLFW's standard gamepad layout: the left and right sticks, and triggers.
	private const int LeftStick = 0;
	private const int RightStick = 1;

	// Each page's four arrows, up, down, left and right, as CockpitKeyboard reads them.
	private static readonly KeyAction[] CommandArrows = {
		KeyAction.CommandScrollUp, KeyAction.CommandScrollDown, KeyAction.CommandScrollLeft, KeyAction.CommandScrollRight,
	};
	private static readonly KeyAction[] DamageArrows = {
		KeyAction.DamageSystemsUp, KeyAction.DamageSystemsDown, KeyAction.DamageHercLeft, KeyAction.DamageHercRight,
	};

	private readonly KeyRepeat _repeat = new(KeyRepeat.SystemTypematic());

	// Which of the four arrows the stick holds, or -1; whether the arrows wait for the stick to come back to the centre,
	// as they do from the moment the display takes the pad, so a stick already steering does not scroll at once.
	private int _arrow = -1;
	private bool _arrowsWaitForCentre;

	private bool _wasActive;
	private bool _awaitingRest;

	/// <summary>The pad as the mouse.</summary>
	public GamepadPointer Pointer { get; } = new();

	/// <summary>The triggers held as mouse buttons, which every mouse event carries (<see cref="SimulatorInput.Pad"/>).</summary>
	public CockpitMouseButtons Buttons { get; private set; }

	/// <summary>Whether the mission's stick reads the sticks and triggers at rest.</summary>
	public bool HoldsAxes => _wasActive || _awaitingRest;

	/// <summary>
	/// Reads the pad, once an update after the host's keys are cleared and before the frame reads them.
	/// <paramref name="active"/> is whether the display has the pad: the view down on it with nothing else holding the
	/// input. <paramref name="damagePage"/> picks the damage detail's arrows over the command display's.
	/// </summary>
	public void Update(IInputContext input, SimulatorInput simulatorInput, Vector2D<int> client, double delta,
			bool active, bool damagePage, KeyMap keyMap, GamepadMouseSettings settings) {
		var pad = input.Gamepads.FirstOrDefault(gamepad => gamepad.IsConnected);
		if (active && !_wasActive) {
			// A trigger already pulled to fire, or a stick already steering, does nothing here until it is let go.
			Pointer.WaitForRelease();
			_arrowsWaitForCentre = true;
		}

		_awaitingRest = (_wasActive && !active || _awaitingRest) && pad != null && !AtRest(pad);
		_wasActive = active;

		var mouse = simulatorInput.Mouse;
		Pointer.Settings = settings;
		var before = mouse == null ? default : Pointer.PositionOf(mouse);
		Pointer.Update(input, mouse, client, delta, active && pad != null);

		var buttons = (Pointer.ClickHeld ? CockpitMouseButtons.Left : CockpitMouseButtons.None)
			| (Pointer.RightClickHeld ? CockpitMouseButtons.Right : CockpitMouseButtons.None);
		bool moved = mouse != null && Pointer.PositionOf(mouse) != before;
		if (buttons != Buttons || active && moved) {
			Buttons = buttons;
			simulatorInput.QueuePointer();
		}

		HoldArrow(pad, active, damagePage ? DamageArrows : CommandArrows, settings, keyMap, simulatorInput.LiveKeys, delta);
	}

	// The stick Mouse emulation leaves free holds the arrow it leans toward most, with the auto-repeat the keyboard's
	// own arrow has, so holding it scrolls the map or steps the list as holding the key does.
	private void HoldArrow(IGamepad? pad, bool active, KeyAction[] arrows, GamepadMouseSettings settings, KeyMap keyMap,
			LiveKeys? keys, double delta) {
		int stickIndex = settings.CursorStick == GamepadSide.Right ? LeftStick : RightStick;
		var stick = pad != null && stickIndex < pad.Thumbsticks.Count
			? new Vector2(pad.Thumbsticks[stickIndex].X, pad.Thumbsticks[stickIndex].Y)
			: Vector2.Zero;

		if (!active || keys == null) {
			_arrow = -1;
		} else if (_arrowsWaitForCentre) {
			_arrowsWaitForCentre = stick.Length() >= ArrowRelease;
		} else {
			_arrow = ArrowOf(stick, _arrow);
		}

		Key? held = _arrow >= 0 ? keyMap.KeyOf(arrows[_arrow]) : null;
		_repeat.Advance(key => key == held, delta);
		if (held is { } key && keys != null) {
			keys.HeldByHost.Add(key);
			if (_repeat.Repeated == key) {
				keys.RepeatedByHost.Add(key);
			}
		}
	}

	// Up, down, left or right (0-3) for a push past ArrowPress, whichever of its axes is the larger; the arrow already
	// held while the push stays past ArrowRelease; none otherwise. GLFW reads a stick pushed up as negative Y.
	private static int ArrowOf(Vector2 stick, int held) {
		float push = stick.Length();
		if (push < ArrowRelease) {
			return -1;
		}

		if (push < ArrowPress) {
			return held;
		}

		return MathF.Abs(stick.Y) >= MathF.Abs(stick.X)
			? stick.Y < 0 ? 0 : 1
			: stick.X < 0 ? 2 : 3;
	}

	private static bool AtRest(IGamepad pad) =>
		pad.Thumbsticks.All(stick => new Vector2(stick.X, stick.Y).Length() < StickAtRest)
		&& pad.Triggers.All(trigger => (trigger.Position + 1f) / 2f < TriggerAtRest);
}
