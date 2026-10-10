namespace Herculan.Engine.Shell;

/// <summary>
/// One alarm of VSHELL's WinTimer (<c>g_WinTimer</c>, <c>005ddbd4</c>), which a widget installs to be sent a
/// tick, event <c>0x200</c>, at a fixed interval. <c>WinTimer_InstallAlarm</c> (<c>0046a0b0</c>) stores a
/// delay and a period; <c>Timer_Tick</c> (<c>0046a1c8</c>), once a pass of the shell's loop
/// (<c>Shell_PumpEvents</c>), takes the <c>GetTickCount</c> time since its own last run off the time left,
/// and once that is down to 0 posts one tick and reloads it from the period. A pass that finds it overdue
/// posts one tick however late it is, and the overshoot is dropped, so ticks come at least a period apart.
/// A period of 0, which <c>Timer_Tick</c> removes on its first pass whether or not it has expired, is not
/// modelled. See docs/retail/shell/widgets.md#the-widget-that-takes-a-click-decides-what-it-does.
/// </summary>
public sealed class ShellAlarm {
	/// <summary><c>WinTimer_Ctor</c> (<c>0046a070</c>)'s <c>+0x145</c>: when the timer last ran.</summary>
	private long _lastTick;

	private long _remaining;
	private int _period;

	/// <param name="now">The <c>GetTickCount</c> the timer is built at, in milliseconds.</param>
	public ShellAlarm(long now) => _lastTick = now;

	/// <summary>Whether the alarm is installed.</summary>
	public bool Installed { get; private set; }

	/// <summary><c>WinTimer_InstallAlarm</c>: the first tick <paramref name="delay"/> ms on, then one each <paramref name="period"/>.</summary>
	public void Install(int delay, int period) {
		Installed = true;
		_remaining = delay;
		_period = period;
	}

	/// <summary><c>WinTimer_RemoveAlarms</c> (<c>0046a138</c>).</summary>
	public void Remove() => Installed = false;

	/// <summary>
	/// <c>Timer_Tick</c>'s pass at <paramref name="now"/>: whether the alarm ticks. Run it every pass of the
	/// loop, installed or not, since the timer measures from its own last run.
	/// </summary>
	public bool Tick(long now) {
		long elapsed = now - _lastTick;
		_lastTick = now;
		if (!Installed) {
			return false;
		}

		_remaining -= elapsed;
		if (_remaining >= 1) {
			return false;
		}

		_remaining = _period;
		return true;
	}
}
