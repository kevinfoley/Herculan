namespace Herculan.Engine.Cockpit;

/// <summary>
/// The front window's <c>TIME:</c> readout — <c>HudClock_Tick</c> (<c>0043dcac</c>), the gunsight complex's clock
/// gadget, called from <c>Gunsight_Paint</c> (<c>0043d5c8</c>) and <c>Gunsight_UpdateAndPaint</c>
/// (<c>0043d6dc</c>) just before the four readout labels are re-texted.
///
/// <para><b>It is not a duration counter.</b> DBSIM keeps four ASCII digits in <c>.bss</c> —
/// <c>004d1860</c>/<c>1861</c> the minutes and <c>004d1863</c>/<c>1864</c> the seconds, each pair
/// NUL-terminated in place — and carries them by hand, then <c>strcpy</c>/<c>strcat</c>s them
/// through the <c>":"</c> at <c>004d1866</c> into the label's buffer at <c>004d1868</c>. That is why
/// the field is minutes 00-99 rather than hours: past <c>99:59</c> the whole thing resets to
/// <c>00:00</c> instead of carrying.</para>
///
/// <para><b>The digits start at 99:59.</b> <c>Gau_RovingGunsightWidget</c> (<c>0043c7d8</c>) seeds
/// them that way and zeroes the deadline at <c>004d1888</c>, so the first paint's carry falls
/// straight through the wrap branch and lands on <c>00:00</c>. The buffer at <c>004d1868</c> is
/// therefore never displayed holding <c>99:59</c>.</para>
///
/// <para>The clock is wall time, not simulation time: the deadline is compared against
/// <c>Time_GetCoarseTicks</c> and pushed to <c>now + 0x3c</c> on each carry, so one displayed second
/// is 60 coarse ticks — 960 ms, which is why the retail clock runs about 4% fast. Being an absolute
/// deadline rather than an accumulator, it also never catches up: a gap between two paints longer
/// than 60 ticks still advances the display by exactly one second.</para>
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
