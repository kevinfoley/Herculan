namespace Herculan.Es1.Input;

/// <summary>
/// The keyboard's output block (<c>ES1DBSIM 000b188c</c>) <c>Input_BuildKeyboardAxes</c> writes: two axis pairs, the
/// first steering and throttle and the second twist and pitch, as dwords, then the <c>Space</c> and <c>Enter</c> bytes
/// and two zero bytes, the four source buttons (docs/es1/simulation/input.md#setting-up--input_buildsourcetable-0004c95c).
/// </summary>
/// <param name="FirstX"><c>+0</c>.</param>
/// <param name="FirstY"><c>+4</c>.</param>
/// <param name="SecondX"><c>+8</c>.</param>
/// <param name="SecondY"><c>+0xc</c>.</param>
/// <param name="Space"><c>+0x10</c>: <c>Space</c> held.</param>
/// <param name="Enter"><c>+0x11</c>: <c>Enter</c> held.</param>
public readonly record struct Es1KeyboardAxesBlock(int FirstX, int FirstY, int SecondX, int SecondY, bool Space, bool Enter) {
	/// <summary>The source button <paramref name="index"/>, 0–3: <c>Space</c>, <c>Enter</c>, then the two zero bytes.</summary>
	public byte Button(int index) => index switch {
		0 => (byte)(Space ? 1 : 0),
		1 => (byte)(Enter ? 1 : 0),
		_ => 0,
	};
}

/// <summary>
/// <c>Input_BuildKeyboardAxes</c> (<c>0004c818</c>) and the table <c>Input_InitKeyboardAxisTable</c> (<c>0004da7c</c>)
/// writes (docs/es1/simulation/input.md#the-keyboard-axes--input_buildkeyboardaxes-0004c818; ES2's
/// docs/retail/simulation/joystick-input.md#the-keyboard).
/// </summary>
public static class Es1KeyboardAxes {
	/// <summary><c>KeyboardAxisDeltas</c> (<c>000b1874</c>), keys 0–11: keypad 7, 8, 9, 4, 6, 1, 2, 3, then <c>M</c>, <c>J</c>, <c>K</c>, <c>I</c>.</summary>
	private static readonly (sbyte Dx, sbyte Dy)[] Deltas = [
		(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1),
		(0, -1), (-1, 0), (1, 0), (0, 1),
	];

	/// <summary><c>KeyboardAxisDiagonalFlags</c> (<c>0009588c</c>), keys 0–7: keypad 7, 9, 1 and 3.</summary>
	private static readonly bool[] Diagonal = [true, false, true, false, false, true, false, true];

	private const int FirstPairKeys = 8, AxisKeys = 12, SpaceKey = 12, EnterKey = 13;

	/// <summary>A key's entry is shifted left 6, a quarter of <c>0x100</c>, or 8, the whole of it.</summary>
	private const int QuarterShift = 6, FullShift = 8;

	/// <summary>
	/// One build: each held key of 0–11 is marked seen and adds its entry, a quarter, or the whole while
	/// <see cref="Es1AxisKeys.AltHeld"/> is set and, for keys 0–7, whenever <paramref name="slow"/> is false. Keys 0–7
	/// add into the first pair, a held diagonal zeroing it, adding its own and ending the group; the first held of keys
	/// 8–11 adds into the second and ends the loop. Then <c>Space</c> and <c>Enter</c> held, and every key both held and
	/// release-pending has its held and seen bytes cleared, so a key pressed and released between two builds counts
	/// for one.
	///
	/// <para>With <paramref name="aimAtStickRate"/>, the core's faster keyboard aiming tweak
	/// (<see cref="Engine.Settings.TweakSettingDefinitions.FasterKeyboardAiming"/>, plan decision 32,
	/// docs/herculan/plan-es1-first-mission.md#decisions), every key that aims the turret adds the whole of its entry, as
	/// it does with <c>Alt</c> held: keys 8–11, and keys 0–7 when <paramref name="firstPairAims"/>, the input build moving
	/// the first pair onto the second. The whole of <c>0x100</c> is the travel a stick pushed all the way reaches under the
	/// squared response every device is built with (docs/es1/simulation/joystick.md#the-record). This engine's own; off,
	/// the build is retail's.</para>
	/// </summary>
	internal static Es1KeyboardAxesBlock Build(Es1AxisKeys keys, bool slow, bool aimAtStickRate = false, bool firstPairAims = false) {
		byte[] held = keys.Held, seen = keys.Seen, releasePending = keys.ReleasePending;
		int firstX = 0, firstY = 0, secondX = 0, secondY = 0;
		for (int key = 0; key < AxisKeys; key++) {
			if (held[key] == 0) {
				continue;
			}

			bool aims = key >= FirstPairKeys || firstPairAims;
			int shift = keys.AltHeld != 0 || (key < FirstPairKeys && !slow) || (aimAtStickRate && aims) ? FullShift : QuarterShift;
			seen[key] = 1;
			var (dx, dy) = Deltas[key];
			if (key >= FirstPairKeys) {
				secondX += dx << shift;
				secondY += dy << shift;
				break;
			}

			if (Diagonal[key]) {
				firstX = 0;
				firstY = 0;
			}

			firstX += dx << shift;
			firstY += dy << shift;
			if (Diagonal[key]) {
				key = FirstPairKeys - 1;
			}
		}

		var block = new Es1KeyboardAxesBlock(firstX, firstY, secondX, secondY, held[SpaceKey] != 0, held[EnterKey] != 0);
		for (int key = 0; key < Es1AxisKeys.KeyCount; key++) {
			if (held[key] != 0 && releasePending[key] != 0) {
				seen[key] = 0;
				held[key] = 0;
			}
		}

		return block;
	}
}
