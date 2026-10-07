using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// When the MFD's two status screens, F1 and F5, repaint, and what they show between repaints. In the
/// original a status screen is pixels left in the cockpit raster by its last paint, so what the player
/// reads is that paint's subject, values and scramble state until the next one; <see cref="Status"/> and
/// <see cref="Target"/> are those paints. docs/retail/formats/mfd.md, "The subject", owns the evidence.
///
/// <para><b>When a screen paints.</b> <c>MfdDisplay_Update</c> (<c>00446328</c>) runs the current status
/// screen's update — the subject parked again, then <c>MfdStatusScreen_Update</c> (<c>0043b210</c>) and its
/// paint — only once the coarse clock is past the display's refresh tick (<c>+0x100</c>), and then sets
/// that tick <see cref="RefreshTicks"/> ahead. One tick serves both screens. Besides that the display's
/// full repaint paints the current screen without parking or touching the scramble: <c>MfdDisplay_SetMode</c>
/// parks the new mode's subject and asks for one, as do the sensor dropout ending, a squadmate's
/// transmission leaving the screen, and every frame of a view change (the view manager's <c>+0x1c</c>). The
/// host reports the last three as the first frame the screen updates after not doing so, and as
/// <c>viewChanging</c>.</para>
///
/// <para><b>The scramble.</b> SELECT on either screen writes 100 to that screen's <c>+0xc</c>
/// (<see cref="Scramble"/>). Its next update turns that into the flag <c>+0x34</c> with a deadline
/// <see cref="ScrambleTicks"/> ahead, and the first update past the deadline clears it, so a press scrambles
/// exactly one refresh's paint. See <see cref="MfdStatusSubject.Scrambled"/> for what it hides.</para>
///
/// <para><b>The latch.</b> Each paint (<c>MfdStatusScreen_Paint</c>, <c>0043a5a0</c>) first latches the parked
/// subject at <c>+0x3e</c>, holding a lost one that is <see cref="SimObject.Neutralised"/> for
/// <see cref="DeadHoldTicks"/>. The hold flag's stale arming is reproduced (KNOWN_ISSUES.md).</para>
/// </summary>
public sealed class MfdStatusRefresh {
	/// <summary>How far ahead <c>MfdDisplay_Update</c> sets the next refresh, in coarse ticks — <c>0x1e</c>.</summary>
	public const int RefreshTicks = 0x1e;

	/// <summary>How long a scramble lasts, in coarse ticks — <c>MfdStatusScreen_Update</c>'s <c>0x1e</c>.</summary>
	public const int ScrambleTicks = 0x1e;

	/// <summary>How long a paint holds a dead subject the screen has lost, in coarse ticks.</summary>
	public const int DeadHoldTicks = 300;

	private readonly Screen _status = new();
	private readonly Screen _target = new();

	/// <summary>The display's refresh tick, <c>+0x100</c>. The display is allocated zeroed.</summary>
	private long _nextRefresh;

	/// <summary>The subject parked in the screens' shared state block, <c>display+0xb9</c>.</summary>
	private SimObject? _parked;

	private MfdMode? _mode;
	private bool _updatedLastFrame;

	/// <summary>The F1 screen as its last paint left it.</summary>
	public MfdStatusSubject Status => _status.Painted;

	/// <summary>The F5 screen as its last paint left it.</summary>
	public MfdStatusSubject Target => _target.Painted;

	/// <summary>
	/// <c>MfdButton_OnClick</c>'s SELECT case writing 100 to the current screen's <c>+0xc</c>, after its own
	/// action. Nothing for a screen that is not a status screen.
	/// </summary>
	public void Scramble(MfdMode mode) {
		if (ScreenFor(mode) is { } screen) {
			screen.ScrambleRequested = true;
		}
	}

	/// <summary>One frame of the display's status-screen work.</summary>
	/// <param name="mode">The display's mode this frame, after anything that set it.</param>
	/// <param name="updating">
	/// Whether <c>MfdDisplay_Update</c> reaches the current screen this frame: the cockpit view up, the
	/// display on screen, armed and past its dropout, and no transmission over it.
	/// </param>
	/// <param name="viewChanging">Whether a view change is under way — the view manager's <c>+0x1c</c>.</param>
	/// <param name="coarseTicks">The coarse clock.</param>
	/// <param name="rosterEntry">F1's subject: the squad roster's current entry.</param>
	/// <param name="selection">F5's subject: the current selection, <c>CockpitView+0x210</c>.</param>
	/// <param name="read">Reads one subject as the paint does; null reads as <see cref="MfdStatusSubject.None"/>.</param>
	public void Update(MfdMode mode, bool updating, bool viewChanging, long coarseTicks, SimObject? rosterEntry,
			SimObject? selection, Func<SimObject?, MfdStatusSubject> read) {
		ArgumentNullException.ThrowIfNull(read);

		bool modeChanged = mode != _mode;
		_mode = mode;
		var screen = ScreenFor(mode);
		if (screen == null) {
			_updatedLastFrame = false;
			return;
		}

		// MfdDisplay_SetMode parks the new screen's subject whether or not the display is updating.
		if (modeChanged) {
			_parked = mode == MfdMode.Status ? rosterEntry : selection;
		}

		bool repaint = modeChanged || !_updatedLastFrame || viewChanging;
		_updatedLastFrame = updating;
		if (!updating) {
			return;
		}

		if (repaint) {
			Paint(screen, coarseTicks, read);
		}

		if (_nextRefresh >= coarseTicks) {
			return;
		}

		_nextRefresh = coarseTicks + RefreshTicks;
		_parked = mode == MfdMode.Status ? rosterEntry : selection;

		// MfdStatusScreen_Update.
		if (screen.ScrambleRequested) {
			screen.ScrambleRequested = false;
			screen.Scrambled = true;
			screen.ScrambleDeadline = coarseTicks + ScrambleTicks;
		} else if (screen.Scrambled && screen.ScrambleDeadline < coarseTicks) {
			screen.Scrambled = false;
		}

		Paint(screen, coarseTicks, read);
	}

	private void Paint(Screen screen, long coarseTicks, Func<SimObject?, MfdStatusSubject> read) {
		if (screen.Latched == null || _parked != null) {
			screen.Latched = _parked;
		} else if (screen.Latched.Neutralised) {
			if (!screen.HoldArmed) {
				screen.HoldArmed = true;
				screen.HoldDeadline = coarseTicks + DeadHoldTicks;
			} else if (screen.HoldDeadline < coarseTicks) {
				screen.HoldArmed = false;
				screen.Latched = null;
			}
		} else {
			screen.Latched = null;
		}

		var painted = screen.Latched == null ? MfdStatusSubject.None : read(screen.Latched);
		screen.Painted = painted with { Scrambled = screen.Scrambled && painted.Present };
	}

	private Screen? ScreenFor(MfdMode mode) => mode switch {
		MfdMode.Status => _status,
		MfdMode.TargetStatus => _target,
		_ => null,
	};

	/// <summary>One <c>MfdStatusScreen_Ctor</c> object's own state.</summary>
	private sealed class Screen {
		/// <summary><c>+0xc</c> holding 100.</summary>
		public bool ScrambleRequested;

		/// <summary><c>+0x34</c>.</summary>
		public bool Scrambled;

		/// <summary><c>+0x35</c>.</summary>
		public long ScrambleDeadline;

		/// <summary><c>+0x3e</c>.</summary>
		public SimObject? Latched;

		/// <summary><c>+0x39</c>.</summary>
		public bool HoldArmed;

		/// <summary><c>+0x3a</c>.</summary>
		public long HoldDeadline;

		public MfdStatusSubject Painted = MfdStatusSubject.None;
	}
}
