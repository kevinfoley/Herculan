namespace Herculan.Engine.Cockpit;

/// <summary>
/// The front window's <c>TIME:</c> readout — <c>HudClock_Tick</c> (<c>0043dcac</c>), the gunsight complex's clock
/// gadget, called from <c>Gunsight_Paint</c> (<c>0043d5c8</c>) and <c>Gunsight_UpdateAndPaint</c>
/// (<c>0043d6dc</c>) just before the four readout labels are re-texted.
///
/// <para><b>It is not a duration counter</b> but four ASCII digits carried by hand: minutes 00-99,
/// and past <c>99:59</c> the whole of it resets to <c>00:00</c>. The digits start at <c>99:59</c>
/// with the deadline zeroed, so the first paint shows <c>00:00</c>.</para>
///
/// <para>The clock is wall time, not simulation time: one displayed second is
/// <see cref="TicksPerSecond"/> coarse ticks, 960 ms, so it runs about 4% fast, and a gap between
/// two paints longer than that still advances the display by one second. See
/// docs/retail/simulation/cockpit-gunsight-hud.md#speed-and-time-readouts.</para>
/// </summary>
public sealed class MissionClock {
	/// <summary>Coarse ticks per displayed second — <c>HudClock_Tick</c>'s <c>+ 0x3c</c>.</summary>
	private const double TicksPerSecond = 0x3c;

	/// <summary><c>Time_GetCoarseTicks</c>' unit: <c>GetTickCount() >> 4</c>, so 16 ms.</summary>
	private const double CoarseTickSeconds = 0.016;

	private char _minuteTens = '9';
	private char _minuteOnes = '9';
	private char _secondTens = '5';
	private char _secondOnes = '9';

	private double _ticks;
	private double _due;

	/// <summary>
	/// The label text, <c>004d1868</c>. Empty until the first <see cref="Advance"/>, matching the
	/// <c>.bss</c> buffer the original has not written yet — the readout is blank for the frames
	/// before the gunsight's first paint.
	/// </summary>
	public string Text { get; private set; } = string.Empty;

	/// <summary>
	/// Runs the gadget for one painted frame. Call it only on the frames the front window's gunsight
	/// is actually painted: the original's clock is driven from that paint and from nowhere else, so
	/// it stalls for as long as the pilot is in a view that has no gunsight in it.
	/// </summary>
	public void Advance(double deltaSeconds) {
		_ticks += deltaSeconds / CoarseTickSeconds;
		if (_due >= _ticks) {
			return;
		}

		_due = _ticks + TicksPerSecond;
		Carry();
		Text = $"{_minuteTens}{_minuteOnes}:{_secondTens}{_secondOnes}";
	}

	/// <summary>
	/// <c>HudClock_Tick</c>'s carry chain, digit for digit. The seconds tens roll at <c>'5'</c> and
	/// everything else at <c>'9'</c>; the wrap test is on the minutes, and it fires before the ones
	/// digit is normalised, which is why it reads against <c>':'</c> — the character <c>'9' + 1</c>
	/// has already become.
	/// </summary>
	private void Carry() {
		if (++_secondOnes <= '9') {
			return;
		}

		_secondOnes = '0';
		if (++_secondTens <= '5') {
			return;
		}

		_secondTens = '0';
		if (++_minuteOnes >= ':' && _minuteTens == '9') {
			_secondOnes = '0';
			_secondTens = '0';
			_minuteOnes = '0';
			_minuteTens = '0';
		} else if (_minuteOnes > '9') {
			_minuteOnes = '0';
			_minuteTens++;
		}
	}
}
