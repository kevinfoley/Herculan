using Herculan.Es1.Sim;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's frame-time scale (docs/es1/shell/startup.md#the-frame-time-scale): a countdown the PC timer runs down 60
/// times a second, which each pass of the frame loop reads and reloads, giving the pass's length in Q8 tenths of a
/// second. Its readers, the functions from <c>00011b63</c> to <c>00011d9f</c>, are later modules' (startup.md's Open).
/// </summary>
public sealed class Es1ShellTime {
	/// <summary><c>ShellTime_ScaleQ8</c>'s floor and ceiling.</summary>
	private const short MinimumScale = 0x40, MaximumScale = 0x1c2;

	private bool _registered;

	/// <summary>The word <c>0009ae10</c>: the PC timer's interrupts a second, 120.</summary>
	public short TicksPerSecond { get; private set; }

	/// <summary>The word <c>0009ae14</c>, the countdown's reload.</summary>
	public short Reload { get; private set; }

	/// <summary>The word <c>0009ae16</c>, the countdown <see cref="Tick"/> runs down.</summary>
	public short Countdown { get; private set; }

	/// <summary>The words <c>000aa7a6</c> and <c>000aa7a8</c>, the two scales' units, a tenth of <see cref="TicksPerSecond"/>.</summary>
	public short Unit { get; private set; }

	/// <inheritdoc cref="Unit"/>
	public short Unit2 { get; private set; }

	/// <summary>The word <c>000aa7aa</c>: the ticks the last pass took.</summary>
	public short Elapsed { get; private set; }

	/// <summary>The word <c>000aa7a2</c>: the last pass's length, Q8 with 1.0 = 0.1 s, held to <c>0x40</c>–<c>0x1c2</c>.</summary>
	public short Scale { get; private set; }

	/// <summary>The word <c>000aa7a4</c>: <see cref="Scale"/> over <see cref="Unit2"/>.</summary>
	public short Scale2 { get; private set; }

	/// <summary>The dword <c>0007e6fc</c>, which <see cref="Tick"/> counts up and <c>Anm_Play</c> reads.</summary>
	public int AnmClock { get; private set; }

	/// <summary>
	/// <c>ShellTime_Init</c> (<c>000119fc</c>): both scales 1.0 (<c>0x100</c>), and the first time, with
	/// <paramref name="timer"/> the timer GL slot 0 holds, <see cref="Tick"/> registered at a count of 2 and the
	/// interrupts a second derived from the divisor as the simulator derives them (<see cref="Es1SimClock.InterruptsPerSecond"/>),
	/// with a tenth of them for the units and the elapsed count, and the countdown at the full count.
	/// </summary>
	public void Init(Es1SimClock timer) {
		Scale = 0x100;
		Scale2 = 0x100;
		if (_registered) {
			return;
		}

		_registered = true;
		timer.AddCallback(2, Tick);
		TicksPerSecond = Es1SimClock.InterruptsPerSecond;
		short tenth = (short)(TicksPerSecond / 10);
		Reload = TicksPerSecond;
		Countdown = TicksPerSecond;
		Unit = tenth;
		Unit2 = tenth;
		Elapsed = tenth;
	}

	/// <summary><c>ShellTime_Tick</c> (<c>000119e0</c>), every second interrupt: the countdown down by 1 while it is not 0, and <see cref="AnmClock"/> up by 1.</summary>
	public void Tick() {
		if (Countdown != 0) {
			Countdown--;
		}

		AnmClock++;
	}

	/// <summary>
	/// <c>ShellTime_UpdateFrameScale</c> (<c>00011b16</c>), the frame loop's last step: the ticks since the last pass,
	/// reload less countdown, into <see cref="Elapsed"/>; both scales from it; the countdown reloaded.
	/// </summary>
	public void UpdateFrameScale() {
		Elapsed = (short)(Reload - Countdown);
		Scale = ScaleQ8(Elapsed, Unit);
		Scale2 = ScaleQ8(Elapsed, Unit2);
		Countdown = Reload;
	}

	/// <summary><c>ShellTime_ScaleQ8</c> (<c>00011ac7</c>)(a, b): (a &lt;&lt; 8) / b, a signed division, its low 16 bits held to <c>0x40</c>–<c>0x1c2</c>.</summary>
	public static short ScaleQ8(short a, short b) {
		short value = (short)((a << 8) / b);
		return value > MaximumScale ? MaximumScale : value < MinimumScale ? MinimumScale : value;
	}
}
