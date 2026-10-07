namespace Herculan.Engine.Shell;

/// <summary>
/// The six-frame sequence that first brings the main menu up — an <c>esanim2.cpp</c> widget over the
/// whole window (<c>StartupAnimWidget</c> (<c>0048d0c0</c>), class <c>ESAnim2_Ctor</c> (<c>0040c85c</c>), event handler <c>ESAnim2_HandleEvent</c> (<c>0040c8b3</c>)) that
/// <c>MainMenu_BuildScreen</c> fills with <c>dbm\bay2a_80</c> to <c>bay2a_84</c>, the last twice, and the
/// startup shows once its movies are done. See docs/retail/shell/screen-layout.md#the-main-menu.
///
/// <para>Shown, it paints frame 0 and installs a 500 ms alarm; each tick advances <c>+0x6d</c>, wrapping
/// at the frame count, paints that frame and runs the builder's handler, <c>004311b8</c>, which plays the
/// switch sound on its first run (<c>DAT_00473608</c>) and, once <c>+0x6d</c> reaches 5, hides the widget
/// and shows the menu, once only (<c>DAT_00473604</c>). So the menu comes up 2.5 s after the first frame,
/// half a second after the backdrop reaches its last image.</para>
/// </summary>
public sealed class ShellStartupSequence {
	/// <summary>The frames, in the order the builder adds them (<c>ESAnim2_AddFrame</c> (<c>0040ca06</c>)).</summary>
	public static readonly string[] FrameNames = { "BAY2A_80", "BAY2A_81", "BAY2A_82", "BAY2A_83", "BAY2A_84", "BAY2A_84" };

	/// <summary>The alarm's delay and period, <c>WinTimer_InstallAlarm</c>'s 500 and 500.</summary>
	public const int TickMilliseconds = 500;

	/// <summary>The frame at which the handler hides the widget and shows the menu.</summary>
	private const int MenuFrame = 5;

	private long _clock;
	private bool _switchPlayed;

	/// <summary><c>+0x6d</c>, the frame on screen.</summary>
	public int Frame { get; private set; }

	/// <summary>Whether the widget is up — shown and not yet hidden by its handler.</summary>
	public bool IsUp { get; private set; }

	/// <summary><c>DAT_00473604</c>: the handler has hidden the widget and shown the menu.</summary>
	public bool Done { get; private set; }

	/// <summary>The show: frame 0 on screen and the alarm installed at <paramref name="now"/>.</summary>
	public void Show(long now) {
		IsUp = true;
		Frame = 0;
		_clock = now;
	}

	/// <summary>
	/// Every alarm tick due by <paramref name="now"/>, each advancing the frame and running the handler,
	/// which calls <paramref name="playSwitch"/> on its first run. Returns whether the frame changed.
	/// </summary>
	public bool Advance(long now, Action playSwitch) {
		bool changed = false;
		while (IsUp && now - _clock >= TickMilliseconds) {
			_clock += TickMilliseconds;
			Frame = Frame + 1 >= FrameNames.Length ? 0 : Frame + 1;
			changed = true;

			if (!_switchPlayed) {
				playSwitch();
				_switchPlayed = true;
			}

			if (Frame == MenuFrame) {
				Done = true;
				IsUp = false;
			}
		}

		return changed;
	}
}
