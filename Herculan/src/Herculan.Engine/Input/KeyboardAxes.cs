using Herculan.Engine.Sim;

namespace Herculan.Engine.Input;

/// <summary>
/// The keyboard's two axis pairs — <c>Input_BuildKeyboardAxes</c> (<c>0045a4b0</c>) over the fourteen keys
/// <c>Input_KeyjoyAxisKey</c> (<c>0045a308</c>) holds, each taken from the <see cref="KeyMap"/>. The first pair is
/// steering and throttle, the second twist and pitch, and a binding decides which game axes each reaches
/// (<see cref="JoystickBindings"/>). Each key is worth <see cref="MechControls.KeyboardAxis"/> on the axes its entry
/// names. See docs/retail/simulation/joystick-input.md#the-keyboard.
/// </summary>
public static class KeyboardAxes {
	// Keys 0-7, in the order of the original's wanted-codes list. Each scancode is two keys here, the action's two
	// chords: VkToScancode (004a1104) gives the arrow and editing cluster the keypad's own codes (TapeKeys.KeysOf).
	private static readonly (KeyAction Action, sbyte Dx, sbyte Dy, bool Diagonal)[] FirstPairKeys = {
		(KeyAction.DiagonalForwardLeft, -1, -1, true),
		(KeyAction.ThrottleForward, 0, -1, false),
		(KeyAction.DiagonalForwardRight, 1, -1, true),
		(KeyAction.SteerLeft, -1, 0, false),
		(KeyAction.SteerRight, 1, 0, false),
		(KeyAction.DiagonalBackLeft, -1, 1, true),
		(KeyAction.ThrottleBack, 0, 1, false),
		(KeyAction.DiagonalBackRight, 1, 1, true),
	};

	// Keys 8-13. The last two are the RAZOR's throttle keys.
	private static readonly (KeyAction Action, sbyte Dx, sbyte Dy, bool Razor)[] SecondPairKeys = {
		(KeyAction.TurretDown, 0, -1, false),
		(KeyAction.TurretLeft, -1, 0, false),
		(KeyAction.TurretRight, 1, 0, false),
		(KeyAction.TurretUp, 0, 1, false),
		(KeyAction.RazorThrottleDown, 0, -1, true),
		(KeyAction.RazorThrottleUp, 0, 1, true),
	};

	/// <summary>
	/// The frame's two pairs. Keys 0-7 add up until the first held diagonal, which replaces everything before
	/// it and ends the group; of keys 8-13 only the first held counts.
	/// </summary>
	/// <param name="throttleKeys">
	/// Keypad <c>-</c> and <c>+</c> are axis keys: a RAZOR's, whose second pair's pitch is its throttle.
	/// <c>Input_KeyjoyAxisKey</c> passes them on otherwise.
	/// </param>
	public static PilotAxes Build(IKeyState keys, KeyMap keyMap, bool throttleKeys) {
		int steer = 0, throttle = 0;
		foreach (var (action, dx, dy, diagonal) in FirstPairKeys) {
			if (!keyMap.IsDown(keys, action)) {
				continue;
			}

			if (diagonal) {
				(steer, throttle) = (dx, dy);
				break;
			}

			steer += dx;
			throttle += dy;
		}

		int twist = 0, pitch = 0;
		foreach (var (action, dx, dy, razor) in SecondPairKeys) {
			if (razor && !throttleKeys) {
				continue;
			}

			if (keys.IsKeyPressed(keyMap.KeyOf(action))) {
				(twist, pitch) = (dx, dy);
				break;
			}
		}

		return new PilotAxes(
			(short)(steer * MechControls.KeyboardAxis),
			(short)(throttle * MechControls.KeyboardAxis),
			(short)(twist * MechControls.KeyboardAxis),
			(short)(pitch * MechControls.KeyboardAxis));
	}

	/// <summary>
	/// What a stick pushed all the way delivers, <see cref="JoystickReading.ApplyResponse"/> at
	/// <see cref="JoystickReading.RawFull"/>: 258 with the original's deadzone.
	/// </summary>
	public static readonly short StickFullDeflection = (short)JoystickReading.ApplyResponse(JoystickReading.RawFull);

	/// <summary>
	/// <see cref="Build"/>'s pairs with each key that will aim the turret worth <see cref="StickFullDeflection"/>
	/// rather than <see cref="MechControls.KeyboardAxis"/>, so a held key turns the turret as fast as the stick can
	/// (<see cref="Settings.TweakSettingDefinitions.FasterKeyboardAiming"/>). <b>This engine's own</b>; retail's key
	/// is worth half a stick. The second pair aims the turret in a walker; the first does too when
	/// <paramref name="firstPairAims"/>, the stick having taken the movement pair
	/// (<see cref="JoystickPilotInput.KeyboardAimsTurret"/>).
	/// </summary>
	public static PilotAxes AtStickRate(PilotAxes keys, bool firstPairAims) => new(
		firstPairAims ? AtStickRate(keys.Steer) : keys.Steer,
		firstPairAims ? AtStickRate(keys.Throttle) : keys.Throttle,
		AtStickRate(keys.TorsoTwist),
		AtStickRate(keys.TorsoPitch));

	// Every axis Build makes is 0 or one key's worth either way.
	private static short AtStickRate(short axis) => (short)(Math.Sign(axis) * StickFullDeflection);
}
