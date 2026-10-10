namespace Herculan.Engine.Cockpit;

/// <summary>
/// What DBSIM's modal alert panels have in common: the screen they centre on, the rect type their
/// widgets are laid out in, and the transform that puts one in this engine's window.
///
/// <para>The family is four panel classes built on one base (<c>AlertPanel_CtorBase</c>,
/// <c>00454174</c>), and this engine draws all four: <see cref="ObjectivesPanelLayout"/> ([F11]),
/// <see cref="StatusAlertPanelLayout"/> ([Q] and the mission-end alerts, plus [P] and [Ctrl+Q] at
/// <see cref="PausePanelLayout"/>'s size), <see cref="PreferencesPanelLayout"/> ([F12]) and
/// <see cref="ControlsPanelLayout"/>. They share the fonts and the geometry below; the first two
/// also share the button bank, where the last two carry their own. See
/// docs/retail/simulation/alert-panels.md and docs/retail/simulation/preferences.md.</para>
/// </summary>
public static class AlertPanelLayout {
	/// <summary>The screen a panel centres itself on — <c>AlertPanel_CenterRect</c>'s two globals.</summary>
	public const int ScreenWidth = 640;

	/// <inheritdoc cref="ScreenWidth"/>
	public const int ScreenHeight = 480;

	/// <summary>
	/// The bank the objectives, status-alert and pause panels take their buttons from. Frame 0 is the
	/// button at rest and frame 1 the same box in a different border colour, held; both are 124x22 and
	/// are blitted at the widget rect's origin, so they overhang whatever rect the panel gives them.
	///
	/// <para>The other two panels do not use it: <see cref="PreferencesPanelLayout"/> and
	/// <see cref="ControlsPanelLayout"/> each blit button art out of their own plate bank, four frames
	/// per widget size rather than two.</para>
	/// </summary>
	public const string ButtonBank = "ALERT";

	/// <summary>A panel title's font, <c>DAT_004d1e9c</c>.</summary>
	public const string TitleFont = "TITLE";

	/// <summary>
	/// A panel button's caption font at rest, <c>DAT_004d1ea4</c>. Every panel constructs its button
	/// captions in <c>CPYLW</c> and none is ever drawn in it: <c>PanelButton_Paint</c>
	/// (<c>00454ff8</c>) overwrites the label's font from the button's own four-entry state table
	/// before each draw.
	/// </summary>
	public const string ButtonFont = "ACTIVE";

	/// <summary>And while it is held, <c>DAT_004d1ea8</c>.</summary>
	public const string ButtonPressedFont = "PUSHED";

	/// <summary>
	/// And while it is disabled, <c>DAT_004d1eac</c> — the third of the four entries
	/// <c>PanelButton_Ctor</c> writes. Only the two panels of docs/retail/simulation/preferences.md reach it,
	/// being the only two that grey widgets of their own: a <see cref="ControlsPanel"/> row with no
	/// joystick axis behind it, and <see cref="PreferencesPanel"/>'s first four with no sound device.
	/// </summary>
	public const string DisabledButtonFont = "INACTIVE";

	/// <summary>
	/// A widget's state byte, <c>widget+0x1b</c> — what a <c>PanelButton</c> indexes both its
	/// four-frame plate table (<c>+0x30</c>) and its four-entry caption-font table (<c>+0x40</c>)
	/// with. <c>Widget_HitTestChildren</c> skips a widget in <see cref="Disabled"/> whatever its class
	/// draws. See docs/retail/simulation/cockpit-input.md for the cockpit widgets' own reading of the same byte.
	/// </summary>
	public static class WidgetState {
		/// <summary>At rest. The objectives, status and pause panels' buttons never leave it.</summary>
		public const int Rest = 0;

		/// <summary>Held down.</summary>
		public const int Pressed = 1;

		/// <summary>
		/// Greyed, and skipped by the hit test. Only the two panels of docs/retail/simulation/preferences.md
		/// put a widget of their own here — a controls row with no joystick axis behind it, and the
		/// preferences panel's first four with no sound device.
		/// </summary>
		public const int Disabled = 2;

		/// <summary>
		/// The fourth state, which only those same two panels use: the resting state of an option row,
		/// as against the plain buttons beside it. Its caption font is <see cref="ButtonFont"/>, the
		/// same as <see cref="Rest"/>; only the plate frame differs.
		/// </summary>
		public const int Option = 3;
	}

	/// <summary>Frame of <see cref="ButtonBank"/> for a button in the given state.</summary>
	public static int ButtonFrame(bool pressed) => pressed ? 1 : 0;

	/// <summary>Caption font for a button in the given state.</summary>
	public static string CaptionFont(bool pressed) => pressed ? ButtonPressedFont : ButtonFont;

	/// <summary>A rect in panel-local device pixels, inclusive of its low edges.</summary>
	public readonly record struct Rect(int X0, int Y0, int X1, int Y1) {
		/// <summary>Whether a point is inside — <c>Widget_HitTest</c>'s own rect test.</summary>
		public bool Contains(float x, float y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
	}

	/// <summary>
	/// Where a panel of a given size lands in the window, and how to get back from a window pixel to
	/// a panel one.
	///
	/// <para>The original blits at a fixed screen origin; this engine's window is any size, so the
	/// panel is scaled by the same art-pixels-to-window factor the cockpit uses (fit the 480-row
	/// screen to the window's height) and centred horizontally, which is how the three-panel cockpit
	/// composite is anchored too.</para>
	/// </summary>
	public readonly record struct Placement(float Scale, float OriginX, float OriginY) {
		/// <summary>Places a panel of <paramref name="panelWidth"/> x <paramref name="panelHeight"/> device pixels.</summary>
		public static Placement Create(int windowWidth, int windowHeight, int panelWidth, int panelHeight) =>
			CreateAt(windowWidth, windowHeight, ScreenLeft(panelWidth), ScreenTop(panelHeight));

		/// <summary>
		/// Places a panel whose screen origin its constructor gave outright, rather than one it centred
		/// itself to. <c>AlertPanel_SetRect</c> (<c>00454ef8</c>) is the general form and
		/// <c>AlertPanel_CenterRect</c> (<c>00454f34</c>) the wrapper that centres;
		/// <see cref="PreferencesPanelLayout"/> is the family member that calls the former directly.
		/// </summary>
		public static Placement CreateAt(int windowWidth, int windowHeight, int screenX, int screenY) {
			float scale = Math.Max(windowHeight, 1) / (float)ScreenHeight;
			return new Placement(scale,
				(windowWidth - ScreenWidth * scale) / 2f + screenX * scale,
				screenY * scale);
		}

		/// <summary>The window pixel a panel-local pixel lands on.</summary>
		public (float X, float Y) ToWindow(float panelX, float panelY) =>
			(OriginX + panelX * Scale, OriginY + panelY * Scale);

		/// <summary>The panel-local pixel under a window pixel. Not clamped.</summary>
		public (float X, float Y) ToPanel(float windowX, float windowY) =>
			Scale <= 0f
				? (float.NaN, float.NaN)
				: ((windowX - OriginX) / Scale, (windowY - OriginY) / Scale);
	}

	/// <summary>A panel's left edge on the screen — <c>(ScreenWidth - width) / 2</c>.</summary>
	public static int ScreenLeft(int panelWidth) => (ScreenWidth - panelWidth) / 2;

	/// <summary>A panel's top edge on the screen.</summary>
	public static int ScreenTop(int panelHeight) => (ScreenHeight - panelHeight) / 2;
}
