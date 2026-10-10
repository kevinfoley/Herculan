using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The 0x2a-byte sprite sequencer the cockpit plays its short animations through
/// (<c>SpriteSequence_Start</c>, <c>00471d04</c>, and <c>SpriteSequence_Step</c>, <c>00471d7c</c>) — here
/// the sensor dropout's wipes. It runs on the coarse clock from the tick it was started on; its layout
/// and the rules below are docs/retail/simulation/cockpit-hud-widgets.md's.
/// </summary>
public sealed class SpriteSequence {
	/// <summary>One frame of a sequence: which bank frame, and for how many coarse ticks it holds.</summary>
	public readonly record struct Entry(int Frame, int Hold);

	private IReadOnlyList<Entry> _entries = Array.Empty<Entry>();
	private int _state;
	private int _index;
	private long _start;
	private long _holds;
	private long _end;

	/// <summary>Whether the sequencer is anywhere but idle — state 1 playing or state 2 just ended.</summary>
	public bool Busy => _state != 0;

	/// <summary>The bank frame the sequencer last blitted, or null before it has ever started.</summary>
	public int? Frame { get; private set; }

	/// <summary>
	/// Starts <paramref name="entries"/> from its first frame: the start and the first frame's hold are
	/// stamped, the state goes to playing, and frame 0 is blitted.
	/// </summary>
	public void Start(IReadOnlyList<Entry> entries, long coarseTicks) {
		_entries = entries;
		_index = 0;
		_start = coarseTicks;
		_holds = entries.Count > 0 ? entries[0].Hold : 0;
		_end = _start + _holds;
		_state = 1;
		Frame = entries.Count > 0 ? entries[0].Frame : Frame;
	}

	/// <summary>
	/// One step. Playing, once the current hold has run out it advances past every frame whose hold
	/// has, and landing on the last frame ends it (state 2) with that frame blitted. A step in state 2
	/// drops the sequencer back to idle and answers false, which is how its owner learns it is over; a
	/// playing step answers true.
	/// </summary>
	public bool Step(long coarseTicks) {
		if (_state != 1) {
			_state = 0;
			return false;
		}

		if (_end < coarseTicks) {
			do {
				_index++;
				if (_entries.Count - 1 <= _index) {
					_state = 2;
					break;
				}

				_holds += _entries[_index].Hold;
			} while (_start + _holds < coarseTicks);

			_end = _start + _holds;
			if (_index < _entries.Count) {
				Frame = _entries[_index].Frame;
			}
		}

		return true;
	}
}

/// <summary>
/// One display's sensor dropout — the two-state toggle on the <c>PanelGauge</c> base
/// (<c>PanelGauge_TickDropout</c>, <c>00438bc0</c>) that blanks the display for random spells while the
/// player's sensor array is damaged. Every display that has one ticks it from its own update with the
/// array's condition, and only while that condition is nonzero; the ranges each display draws its
/// spells from, and the wipe it plays between states, are its own. See
/// docs/retail/simulation/cockpit-hud-widgets.md#sensor-dropout.
/// </summary>
public sealed class SensorDropout {
	/// <summary>
	/// The range a spell's length is drawn from, in coarse ticks, for the dark state and for the shown
	/// one.
	/// </summary>
	public readonly record struct Ranges(int DarkMin, int DarkMax, int ShownMin, int ShownMax);

	/// <summary>The sequencer a display plays at each change, and the sequence it plays each way.</summary>
	public readonly record struct Wipe(SpriteSequence Sequencer,
		IReadOnlyList<SpriteSequence.Entry> GoDark, IReadOnlyList<SpriteSequence.Entry> ComeBack);

	/// <summary>
	/// Which dependent of the player's machine the displays read — entry 2 of the internal table,
	/// <c>SENSOR ARRAY</c>. <c>Player_DependentCondition</c> (<c>004342e0</c>) is called with it by every
	/// display that drops out.
	/// </summary>
	public static readonly int SensorArraySlot = HercInternals.SensorArray.Id;

	private readonly Func<int, Ranges> _ranges;
	private readonly Func<int, Wipe?> _wipe;
	private SpriteSequence? _sequencer;
	private long? _deadline;
	private int? _frame;

	private SensorDropout(Func<int, Ranges> ranges, Func<int, Wipe?> wipe) {
		_ranges = ranges;
		_wipe = wipe;
	}

	/// <summary>The condition the display was last handed, 0 intact to 4 destroyed — <c>+0x74</c>.</summary>
	public int Condition { get; private set; }

	/// <summary>Whether the display is in its dark state — <c>+0x76</c>.</summary>
	public bool Dark { get; private set; }

	/// <summary>The dark state as it stood before this frame's toggle — <c>+0x77</c>.</summary>
	public bool WasDark { get; private set; }

	/// <summary>
	/// Whether the display's content is held back this frame: dark, or with a wipe still running either
	/// way. Every display with a wipe tests exactly this pair before painting its content.
	/// </summary>
	public bool Hidden => Dark || _sequencer is { Busy: true };

	/// <summary>
	/// The frame of the display's wipe bank that stands over it while it is <see cref="Hidden"/>, or
	/// null for a display with no wipe or one that is showing. A finished wipe's last frame stays up for
	/// the whole dark spell: the display stops painting and nothing covers it.
	/// </summary>
	public int? Frame => Hidden ? _frame : null;

	/// <summary>
	/// Hands the display this frame's sensor condition and, when it is nonzero, runs the toggle once.
	/// A spell's length is drawn on entering the state; once it has passed, a display without a wipe
	/// flips at once and one with a wipe plays it and flips when it ends.
	/// </summary>
	public void Tick(int condition, long coarseTicks, SimRandom random) {
		Condition = condition;
		if (condition == 0) {
			return;
		}

		var ranges = _ranges(condition);
		var wipe = _wipe(condition);
		_sequencer = wipe?.Sequencer;
		WasDark = Dark;

		if (_deadline is not { } deadline) {
			_deadline = coarseTicks + (Dark
				? RollDuration(random, ranges.DarkMin, ranges.DarkMax)
				: RollDuration(random, ranges.ShownMin, ranges.ShownMax));
			return;
		}

		if (deadline >= coarseTicks) {
			return;
		}

		if (wipe is not { } playing) {
			Flip();
			return;
		}

		var sequencer = playing.Sequencer;
		if (!sequencer.Busy) {
			sequencer.Start(Dark ? playing.ComeBack : playing.GoDark, coarseTicks);
		} else if (!sequencer.Step(coarseTicks)) {
			Flip();
		}

		_frame = sequencer.Frame;
	}

	private void Flip() {
		Dark = !Dark;
		_deadline = null;
	}

	/// <summary>
	/// <c>PanelGauge_RollDuration</c> (<c>00438d6c</c>): <c>(next &amp; 0xffff) % (hi - lo) + lo</c> on the
	/// presentation generator, and <paramref name="min"/> without a draw when the two bounds agree.
	/// </summary>
	private static int RollDuration(SimRandom random, int min, int max) =>
		max == min ? min : (random.Next() & 0xffff) % (max - min) + min;

	/// <summary>
	/// The front-window HUD's (<c>Gunsight_UpdateAndPaint</c>): ranges indexed by condition from the four
	/// short tables at <c>0049be48</c>-<c>0049be66</c>, and no wipe.
	/// </summary>
	public static SensorDropout ForGunsight() =>
		new(condition => new Ranges(GunsightDarkMin[condition], GunsightDarkMax[condition],
			GunsightShownMin[condition], GunsightShownMax[condition]), _ => null);

	private static readonly int[] GunsightDarkMin = { 30, 30, 60, 90, 120 };
	private static readonly int[] GunsightDarkMax = { 60, 120, 120, 160, 200 };
	private static readonly int[] GunsightShownMin = { 360, 180, 180, 180, 120 };
	private static readonly int[] GunsightShownMax = { 1800, 900, 360, 360, 300 };

	/// <summary>
	/// A weapon row's (<c>WeaponGauge_Ctor</c>): 120-360 dark and 180-1800 shown whatever the condition,
	/// and a sequencer of its own over <c>WPN_DMG</c> — frames 1 to 9 going dark, 9 to 1 coming back,
	/// each held one tick.
	/// </summary>
	public static SensorDropout ForWeaponRow() {
		var wipe = new Wipe(new SpriteSequence(), RowGoDark, RowComeBack);
		return new(_ => new Ranges(120, 360, 180, 1800), _ => wipe);
	}

	/// <summary>The bank a weapon row's wipe plays — <c>WeaponGauge_Ctor</c>'s second load.</summary>
	public const string RowBank = "WPN_DMG";

	private static readonly SpriteSequence.Entry[] RowGoDark =
		Enumerable.Range(1, 9).Select(frame => new SpriteSequence.Entry(frame, 1)).ToArray();

	private static readonly SpriteSequence.Entry[] RowComeBack = RowGoDark.Reverse().ToArray();

	/// <summary>
	/// The MFD's (<c>MfdDisplay_SetDropoutRanges</c>, <c>00446db4</c>): the condition picks one of three
	/// sets through <c>[0, 1, 2, 2, 2]</c>, and the set picks the ranges and one of three sequencers
	/// over <c>MFD_DMG</c>. Each sequencer's sequence set holds one sequence, so the set's two sequence
	/// numbers are both refused and it plays the sequence it was built with both ways — frames 4, 0 for
	/// condition 1, and 4, 5, 6 for conditions 2 to 4, each held four ticks.
	/// </summary>
	public static SensorDropout ForMfd() {
		var wipes = MfdSequences
			.Select(frames => new Wipe(new SpriteSequence(), frames, frames))
			.ToArray();
		return new(
			condition => MfdRanges[MfdSetByCondition[condition]],
			condition => wipes[MfdSetByCondition[condition]]);
	}

	/// <summary>The bank the MFD's wipes play.</summary>
	public const string MfdBank = "MFD_DMG";

	private static readonly int[] MfdSetByCondition = { 0, 1, 2, 2, 2 };

	private static readonly Ranges[] MfdRanges = {
		new(30, 60, 360, 1800),
		new(30, 120, 180, 900),
		new(60, 120, 180, 360),
	};

	private static readonly SpriteSequence.Entry[][] MfdSequences = {
		new SpriteSequence.Entry[] { new(0, 4), new(4, 4), new(0, 4) },
		new SpriteSequence.Entry[] { new(4, 4), new(0, 4) },
		new SpriteSequence.Entry[] { new(4, 4), new(5, 4), new(6, 4) },
	};

	/// <summary>
	/// The Heads-Down Display's: the <c>PanelGauge_Ctor</c> defaults, 180-360 both ways, and no wipe.
	/// </summary>
	public static SensorDropout ForHeadsDown() => new(_ => new Ranges(180, 360, 180, 360), _ => null);

	/// <summary>
	/// The sensor array's condition, 0 intact to 4 destroyed — <c>Player_DependentCondition(2)</c>:
	/// the dependent's own damage over its maximum, bucketed through <c>Damage_ToConditionState</c>.
	/// </summary>
	public static int SensorCondition(MechObject pilot) =>
		MfdStatusSubject.ConditionFromDamage(pilot.Damage?.DependentPercent(SensorArraySlot) ?? 0);
}

/// <summary>
/// The cockpit's displays' dropouts, one per display, ticked once a frame in the order the original's
/// updates run them. Which displays tick when follows each display's own update: the HUD and the
/// Heads-Down Display while the cockpit is up, the MFD once its power-up is over, and a weapon row once
/// the power-up has armed it and only while the forward console is on screen.
/// </summary>
public sealed class CockpitDropouts {
	private readonly SensorDropout[] _rows =
		Enumerable.Range(0, CockpitPowerUp.RowCount).Select(_ => SensorDropout.ForWeaponRow()).ToArray();

	/// <summary>The front-window HUD's.</summary>
	public SensorDropout Gunsight { get; } = SensorDropout.ForGunsight();

	/// <summary>The MFD's.</summary>
	public SensorDropout Mfd { get; } = SensorDropout.ForMfd();

	/// <summary>The Heads-Down Display's.</summary>
	public SensorDropout HeadsDown { get; } = SensorDropout.ForHeadsDown();

	/// <summary>Weapon row <paramref name="row"/>'s, or null past the ten rows.</summary>
	public SensorDropout? Row(int row) => row >= 0 && row < _rows.Length ? _rows[row] : null;

	/// <summary>One frame's ticks.</summary>
	/// <param name="condition">The sensor array's condition — <see cref="SensorDropout.SensorCondition"/>.</param>
	/// <param name="cockpitUp">Whether the cockpit is showing at all, which the external view is not.</param>
	/// <param name="consoleOnScreen">Whether the forward console's rows are on screen.</param>
	/// <param name="mfdUpdating">Whether the MFD's update reaches its dropout this frame.</param>
	/// <param name="rowTicking">Whether weapon row <c>n</c>'s gauge exists and has been armed.</param>
	public void Tick(int condition, long coarseTicks, SimRandom random, bool cockpitUp, bool consoleOnScreen,
			bool mfdUpdating, Func<int, bool> rowTicking) {
		if (!cockpitUp) {
			return;
		}

		Gunsight.Tick(condition, coarseTicks, random);
		for (int row = 0; row < _rows.Length; row++) {
			if (consoleOnScreen && rowTicking(row)) {
				_rows[row].Tick(condition, coarseTicks, random);
			}
		}

		if (mfdUpdating && consoleOnScreen) {
			Mfd.Tick(condition, coarseTicks, random);
		}

		HeadsDown.Tick(condition, coarseTicks, random);
	}

	/// <summary>What the renderer needs of the three displays that are not weapon rows.</summary>
	public CockpitDropoutState Snapshot => new(Gunsight.Dark, Mfd.Hidden, Mfd.Frame, HeadsDown.Dark);
}

/// <summary>
/// The dropout as the renderer sees it.
/// </summary>
/// <param name="GunsightDark">The front-window HUD skips its whole paint.</param>
/// <param name="MfdHidden">The MFD paints none of its screen, its transmission or its title.</param>
/// <param name="MfdFrame">The <see cref="SensorDropout.MfdBank"/> frame standing over the MFD's screen.</param>
/// <param name="HeadsDownDark">
/// The Heads-Down Display's pages blank — the map and the damage screen flood and draw nothing, and
/// every order goes to the unavailable font.
/// </param>
public readonly record struct CockpitDropoutState(bool GunsightDark, bool MfdHidden, int? MfdFrame,
	bool HeadsDownDark);
