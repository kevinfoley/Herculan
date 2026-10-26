namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Mech_HandleCommand</c> (<c>ES1DBSIM 00011050</c>), slot <c>+0x30</c> of the HERC's table, the codes
/// <c>Sim_DispatchCommand</c> has no case for (docs/es1/simulation/mech.md#the-cockpit): the all-stop, the developer
/// keys' steps and their moves and turns, with no developer gate.
/// </summary>
public static class Es1MechCommands {
	/// <summary>Keypad 5: <c>PlayerMech</c>'s throttle zeroed and <c>+0x8d</c> set.</summary>
	public const ushort Keypad5 = 0x4c;

	/// <summary><c>Mech_ApplyOutnumberedDamage</c> on <c>PlayerMech</c>.</summary>
	public const ushort OutnumberedDamage = 0x619;

	/// <summary><c>Ctrl+Alt+1</c> to <c>Ctrl+Alt+9</c>: the move and turn steps from <see cref="MoveSteps"/> and <see cref="TurnSteps"/>.</summary>
	public const ushort FirstStepCode = 0x602, LastStepCode = 0x60a;

	/// <summary><c>Alt</c> and keypad 6, 4, 8 and 2: the machine moved by the move step along +X, −X, +Y and −Y of its own frame.</summary>
	public const ushort AltKeypad6 = 0x24d, AltKeypad4 = 0x24b, AltKeypad8 = 0x248, AltKeypad2 = 0x250;

	/// <summary><c>Ctrl</c> and keypad 4 and 6: the heading turned by plus and minus the turn step.</summary>
	public const ushort CtrlKeypad4 = 0x44b, CtrlKeypad6 = 0x44d;

	/// <summary>The two codes of <c>Shield_BalanceAdjust</c> (<see cref="Es1Shields.BalanceAdjust"/>) on <c>PlayerMech</c>: <see cref="ShieldCodeB"/> toward the front, <see cref="ShieldCodeA"/> toward the rear.</summary>
	public const ushort ShieldCodeA = 0x1a, ShieldCodeB = 0x1b;

	/// <summary>
	/// The <c>int16</c> move steps at <c>00091990</c>, one per code from <see cref="FirstStepCode"/>, read as
	/// <c>00090d8c</c> plus twice the code.
	/// </summary>
	public static IReadOnlyList<short> MoveSteps { get; } = [500, 1000, 1500, 2000, 3000, 4500, 6000, 7500, 9000];

	/// <summary>The turn steps at <c>000919a4</c>, read as <c>00090da0</c> plus twice the code; the same values.</summary>
	public static IReadOnlyList<short> TurnSteps { get; } = [500, 1000, 1500, 2000, 3000, 4500, 6000, 7500, 9000];

	/// <summary>
	/// <c>Mech_HandleCommand</c>(<paramref name="machine"/>, <paramref name="code"/>): 1 when
	/// <c>WeaponMounts_HandleCommand</c> takes the code or it is one of the 19 at <c>000919d0</c>, after its case; 0 for
	/// any other. The all-stop acts on <paramref name="playerMech"/> (<c>PlayerMech</c>, <c>000bc42a</c>), the moves and
	/// turns on <paramref name="machine"/>; the outnumbered damage writes through <paramref name="globals"/>.
	/// </summary>
	public static short HandleCommand(Es1Mech machine, Es1Mech playerMech, ushort code, Es1DevKeySteps steps,
			Es1SimGlobals globals) {
		// PLACEHOLDER: WeaponMounts_HandleCommand (0000d4b4)(PlayerMech's manager +0x198, code), the weapon keys, takes
		// nothing until plan steps 6 and 7 port the mounts' keys.

		switch (code) {
			case Keypad5:
				playerMech.Throttle = 0;
				playerMech.ThrottleChanged = true;
				return 1;
			case OutnumberedDamage:
				Es1MechDamage.ApplyOutnumberedDamage(playerMech, globals);
				return 1;
			case >= FirstStepCode and <= LastStepCode:
				steps.MoveStep = MoveSteps[code - FirstStepCode];
				steps.TurnStep = TurnSteps[code - FirstStepCode];
				return 1;
			case AltKeypad6:
				MoveAlongFrame(machine, steps.MoveStep, 0);
				return 1;
			case AltKeypad4:
				MoveAlongFrame(machine, (short)-steps.MoveStep, 0);
				return 1;
			case AltKeypad8:
				MoveAlongFrame(machine, 0, steps.MoveStep);
				return 1;
			case AltKeypad2:
				MoveAlongFrame(machine, 0, (short)-steps.MoveStep);
				return 1;
			case CtrlKeypad4:
				Turn(machine, steps.TurnStep);
				return 1;
			case CtrlKeypad6:
				Turn(machine, (short)-steps.TurnStep);
				return 1;
			case ShieldCodeA or ShieldCodeB:
				playerMech.Shields.BalanceAdjust(code == ShieldCodeB);
				return 1;
			default:
				return 0;
		}
	}

	/// <summary>
	/// <c>Context_GetTransform</c> (<c>000016e8</c>) of the machine and <c>Transform_ApplyToShortPoint</c>
	/// (<c>0005e858</c>) of (<paramref name="x"/>, <paramref name="y"/>, 0), stored as the position.
	/// </summary>
	private static void MoveAlongFrame(Es1Mech machine, short x, short y) =>
		machine.Position = machine.Frame.TransformPoint(x, y, 0);

	/// <summary>The heading <c>+0xa</c> turned by <paramref name="step"/>, wrapping, and <c>+0x2c</c> cleared.</summary>
	private static void Turn(Es1Mech machine, short step) {
		var rotation = machine.Rotation;
		machine.Rotation = (rotation.X, rotation.Y, (short)(rotation.Z + step));
		machine.RotationWord = 0;
	}
}

/// <summary>
/// The developer keys' steps, <c>DevKeys_MoveStep</c> (<c>ES1DBSIM 000af6f6</c>) and <c>DevKeys_TurnStep</c>
/// (<c>000af6f8</c>), which <c>Mech_ResetMissionGlobals</c> (<c>00010e10</c>), subsystem phase 2, loads from the words
/// <c>00091996</c> and <c>000919aa</c>, the fourth of each table (docs/es1/simulation/mech.md#the-subsystem).
/// </summary>
public sealed class Es1DevKeySteps {
	/// <summary><c>Mech_ResetMissionGlobals</c>'s values: 2000 each.</summary>
	public Es1DevKeySteps() {
		MoveStep = Es1MechCommands.MoveSteps[3];
		TurnStep = Es1MechCommands.TurnSteps[3];
	}

	public short MoveStep { get; set; }

	public short TurnStep { get; set; }
}
