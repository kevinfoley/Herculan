namespace Herculan.Es1.Input;

/// <summary>
/// The sources the input read chooses from: the keyboard's two pairs and four buttons (the source table's
/// <c>000b1800</c>–<c>000b180c</c> and <c>000b1820</c>–<c>000b182c</c>), and the live block's four stick axes and
/// first four stick buttons (<c>000b1810</c>–<c>000b181c</c>, <c>000b1830</c>–<c>000b183c</c>)
/// (docs/es1/simulation/input.md#setting-up--input_buildsourcetable-0004c95c).
/// </summary>
/// <param name="Keyboard">First X, first Y, second X, second Y.</param>
/// <param name="Stick">X, Y, the third axis, the fourth.</param>
/// <param name="KeyboardButtons"><c>Space</c>, <c>Enter</c> and two zero bytes.</param>
/// <param name="StickButtons">The live block's <c>+0x1d</c>–<c>+0x20</c>.</param>
public readonly record struct Es1InputSources(int[] Keyboard, int[] Stick, byte[] KeyboardButtons, byte[] StickButtons);

/// <summary>
/// The six joystick configurations of preferences option 7, the switch of <c>Input_ReadPlayerInput</c>
/// (<c>ES1DBSIM 0004cb14</c>) that picks a source for each of the four axes and four buttons
/// (docs/es1/simulation/input.md#the-joystick-configurations). A stick source is taken where it is not 0 and the
/// keyboard's otherwise. Retail picks pointers and copies through them at once, so picking the values is the same.
/// </summary>
public static class Es1StickConfigurations {
	private const int KbFirstX = 0, KbFirstY = 1, KbSecondX = 2, KbSecondY = 3;
	private const int StickX = 0, StickY = 1, StickThird = 2, StickFourth = 3;

	/// <summary>
	/// The axes, each cut to <c>int16</c>, and the buttons configuration <paramref name="configuration"/> gives in stick
	/// mode <paramref name="stickMode"/>. <paramref name="thirdAxis"/> is option 9; <paramref name="throttleKeys"/> is
	/// option 10 while no round is flown, which keeps the stick's Y, or the fourth axis, off the throttle.
	/// </summary>
	public static (short[] Axes, byte[] Buttons) Choose(
		byte configuration, byte stickMode, bool thirdAxis, bool throttleKeys, Es1InputSources sources) {
		int[] kb = sources.Keyboard, stick = sources.Stick;
		int[] axes = new int[4];
		byte[] buttons = new byte[4];
		bool turret = stickMode == 2;
		int third = thirdAxis ? stick[StickThird] : 0;
		switch (configuration) {
			case 0:
				axes[Es1PlayerInputBlock.Steering] = kb[KbFirstX];
				axes[Es1PlayerInputBlock.Throttle] = kb[KbFirstY];
				axes[Es1PlayerInputBlock.Twist] = Or(third, kb[KbSecondX]);
				axes[Es1PlayerInputBlock.Pitch] = kb[KbSecondY];
				Array.Copy(sources.KeyboardButtons, buttons, 4);
				break;
			case 1:
			case 3:
			case 4:
			case 5:
				if (turret) {
					axes[Es1PlayerInputBlock.Twist] = Or(stick[StickX], kb[KbSecondX]);
					axes[Es1PlayerInputBlock.Pitch] = Or(stick[StickY], kb[KbSecondY]);
					axes[Es1PlayerInputBlock.Steering] = Or(third, kb[KbFirstX]);
					axes[Es1PlayerInputBlock.Throttle] = kb[KbFirstY];
				} else {
					axes[Es1PlayerInputBlock.Twist] = Or(third, kb[KbSecondX]);
					axes[Es1PlayerInputBlock.Pitch] = kb[KbSecondY];
					axes[Es1PlayerInputBlock.Steering] = Or(stick[StickX], kb[KbFirstX]);
					axes[Es1PlayerInputBlock.Throttle] = throttleKeys ? kb[KbFirstY] : Or(stick[StickY], kb[KbFirstY]);
				}

				// Buttons 0 and 1 from the stick in all four; 2 and 3 from the stick in 3, 4 and 5 only.
				int fromStick = configuration == 1 ? 2 : 4;
				for (int i = 0; i < 4; i++) {
					buttons[i] = i < fromStick ? Or(sources.StickButtons[i], sources.KeyboardButtons[i]) : sources.KeyboardButtons[i];
				}

				break;
			case 2:
				if (turret) {
					axes[Es1PlayerInputBlock.Twist] = Or(stick[StickX], kb[KbSecondX]);
					axes[Es1PlayerInputBlock.Pitch] = Or(stick[StickY], kb[KbSecondY]);
					axes[Es1PlayerInputBlock.Steering] = Or(stick[StickThird], kb[KbFirstX]);
					axes[Es1PlayerInputBlock.Throttle] = throttleKeys ? kb[KbFirstY] : Or(stick[StickFourth], kb[KbFirstY]);
				} else {
					axes[Es1PlayerInputBlock.Twist] = Or(stick[StickThird], kb[KbSecondX]);
					axes[Es1PlayerInputBlock.Pitch] = Or(stick[StickFourth], kb[KbSecondY]);
					axes[Es1PlayerInputBlock.Steering] = Or(stick[StickX], kb[KbFirstX]);
					axes[Es1PlayerInputBlock.Throttle] = throttleKeys ? kb[KbFirstY] : Or(stick[StickY], kb[KbFirstY]);
				}

				for (int i = 0; i < 4; i++) {
					buttons[i] = Or(sources.StickButtons[i], sources.KeyboardButtons[i]);
				}

				break;
			default:
				// PLACEHOLDER: above 5 retail's switch has no case and copies through eight stack slots it never set
				// (ES1DBSIM decompile, Input_ReadPlayerInput's switch); this reads 0 for them. No plan step retires it.
				break;
		}

		return (axes.Select(axis => (short)axis).ToArray(), buttons);
	}

	private static int Or(int stick, int keyboard) => stick != 0 ? stick : keyboard;

	private static byte Or(byte stick, byte keyboard) => stick != 0 ? stick : keyboard;
}
