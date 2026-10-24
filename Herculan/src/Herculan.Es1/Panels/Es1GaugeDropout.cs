using Herculan.Engine.Numerics;

namespace Herculan.Es1.Panels;

/// <summary>
/// The gauge base's sensor dropout, its <c>+0x68</c>–<c>+0x75</c> (docs/es1/simulation/cockpit-widgets.md#sensor-dropout):
/// while the feed's condition is not 0 the display goes dark and comes back in bursts. All 0 from the zero-filled heap,
/// so the first tick schedules a burst.
/// </summary>
public sealed class Es1GaugeDropout {
	/// <summary>The clock counts a second, the unit of the gap before a burst.</summary>
	public const int CountsPerSecond = 60;

	/// <summary>The condition that holds the display dark with no burst.</summary>
	public const short DarkCondition = 3;

	/// <summary>The repaint byte's value at each change: painted on one frame, copied to the screen on the next.</summary>
	public const byte RepaintPasses = 2;

	/// <summary><c>+0x68</c>, the condition, 0 intact to 4 destroyed.</summary>
	public short Condition { get; set; }

	/// <summary><c>+0x6a</c>, the burst's length in clock counts.</summary>
	public short BurstLength { get; private set; }

	/// <summary><c>+0x6c</c>, the clock count the burst starts after.</summary>
	public uint BurstStart { get; private set; }

	/// <summary><c>+0x70</c>, the clock count the next flip comes after.</summary>
	public uint NextFlip { get; private set; }

	/// <summary><c>+0x74</c>, set while the display is dark.</summary>
	public bool Dark { get; private set; }

	/// <summary><c>+0x75</c>, the repaint byte, set to <see cref="RepaintPasses"/> at each change and by the gauge's own writers.</summary>
	public byte Repaint { get; set; }

	/// <summary>
	/// <c>PanelGauge_TickDropout</c> (<c>ES1DBSIM 00031400</c>)(gauge) at the clock <paramref name="now"/>: at
	/// <see cref="DarkCondition"/> dark and nothing more; otherwise, while the clock is past <see cref="BurstStart"/> and
	/// short of its end, each time it passes <see cref="NextFlip"/> the state flipped, the repaint byte set and the next
	/// flip 1 to 30 counts on; once the clock reaches the end the next burst scheduled
	/// (<see cref="ScheduleDropout"/>), dark for a condition of 2 or more and shown below, and the repaint byte set. The
	/// comparisons are unsigned, as retail's.
	/// </summary>
	/// <param name="random"><c>Math_RandomNext</c>'s state at <c>000b5c48</c>, of which the low word is read.</param>
	public void Tick(uint now, SimRandom random) {
		if (Condition == DarkCondition) {
			Dark = true;
			return;
		}

		if (BurstStart >= now) {
			return;
		}

		if (now < BurstStart + (uint)BurstLength) {
			if (NextFlip < now) {
				NextFlip = (uint)((ushort)random.Next() % 30) + now + 1;
				Dark = !Dark;
				Repaint = RepaintPasses;
				// PLACEHOLDER: Sound_Play(0x1c) when the flip goes dark; no plan step ports ES1's sound yet.
			}

			return;
		}

		ScheduleDropout(now, random);
		Dark = Condition >= 2;
		Repaint = RepaintPasses;
	}

	/// <summary>
	/// <c>PanelGauge_ScheduleDropout</c> (<c>000313a4</c>)(gauge): the burst's start and its first flip at the clock + 2
	/// to 7 seconds, and its length 1 to 240 counts, from two draws' low words.
	/// </summary>
	public void ScheduleDropout(uint now, SimRandom random) {
		uint start = (uint)(((ushort)random.Next() % 6 + 2) * CountsPerSecond) + now;
		NextFlip = start;
		BurstStart = start;
		BurstLength = (short)((ushort)random.Next() % 240 + 1);
	}
}
