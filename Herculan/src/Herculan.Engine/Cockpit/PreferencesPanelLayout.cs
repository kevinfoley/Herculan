namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where every piece of the [F12] preferences panel sits — <c>ctl_alrt</c>'s neighbour
/// <c>PreferencesPanel_Ctor</c> (<c>004566c4</c>), the fourth member of the alert-panel family and
/// the only one that does not centre itself. Its constructor calls <c>AlertPanel_SetRect</c>
/// (<c>00454ef8</c>) with an explicit origin, so the panel is a strip pinned along the bottom of the
/// 640x480 screen with the camera circling the player's machine above it.
///
/// <para>Every number here is an authored 320-wide coordinate doubled, the same relationship
/// <see cref="ObjectivesPanelLayout"/>'s are: the constructor writes each as
/// <c>value &lt;&lt; VideoMode_?CoordShift</c>. Rects are panel-local — the panel's own top-left is
/// (0, 0), which is where the plate is blitted — and <see cref="AlertPanelLayout"/> owns the rect
/// type and the window transform.</para>
///
/// <para>The panel is two columns of option rows plus a bottom pair: eleven buttons and nine value
/// readouts, the last two buttons (CONTROLS and DONE) having no value of their own. The rects come
/// from nine parallel <c>short[]</c> tables in <c>.rdata</c> — <c>0049e224</c> onwards for the
/// buttons' x/y/width/height and <c>0049e292</c> onwards for the readouts' — and every entry in a
/// width or height table holds the same value, so the two sizes below stand for all of them.</para>
/// </summary>
public static class PreferencesPanelLayout {
	/// <summary>The panel's declared width, <c>0x13f</c> doubled.</summary>
	public const int Width = 638;

	/// <summary>The panel's declared height, <c>0x59</c> doubled.</summary>
	public const int Height = 178;

	/// <summary>
	/// The panel's left edge on the screen, given rather than derived: the constructor's own
	/// <c>0 &lt;&lt; XCoordShift</c>. The plate is 630 wide against the panel's 638, so the eight
	/// columns of slack sit at the right-hand end and nothing draws in them.
	/// </summary>
	public const int ScreenLeft = 0;

	/// <summary>
	/// The panel's top edge on the screen, <c>0x96</c> doubled — 300 rows down a 480-row screen, which
	/// puts its bottom edge two rows short of the last.
	/// </summary>
	public const int ScreenTop = 300;

	/// <summary>
	/// The projection centre's row in the 3D rect <c>PreferencesPanel_Raise</c> (<c>0045cfd4</c>) sets for as long as
	/// the panel is up, <c>0x32</c> doubled. The rect spans the screen's width and runs down to
	/// <see cref="ScreenTop"/>, and the centre's column is the middle. See
	/// docs/retail/simulation/preferences.md#preferences-and-controls-dbsimexe.
	/// </summary>
	public const int OrbitViewCentreRow = 0x32 << 1;

	/// <summary>The plate, <c>hba\PRF_ALRT.HBA</c> frame 0. 630x170.</summary>
	public const string PlateBank = "PRF_ALRT";

	/// <inheritdoc cref="PlateBank"/>
	public const int PlateFrame = 0;

	/// <summary>
	/// Every button's plate art is four frames of the panel's own bank starting here — one per widget
	/// state, identical but for the border colour — see
	/// <see cref="AlertPanelLayout.ButtonBank"/> for which panels use which.
	///
	/// <para>The constructor writes <see cref="AlertPanelLayout.WidgetState.Option"/> into all eleven
	/// buttons and then puts the last two back to <see cref="AlertPanelLayout.WidgetState.Rest"/>,
	/// which is the only thing that distinguishes CONTROLS and DONE from the rows above them.</para>
	/// </summary>
	public const int ButtonFirstFrame = 1;

	/// <summary>The title bar's height — the band the title is vertically centred in.</summary>
	public const int TitleHeight = 16;

	/// <summary>How many buttons the panel builds. The last two are <see cref="ControlsButton"/> and <see cref="DoneButton"/>.</summary>
	public const int ButtonCount = 11;

	/// <summary>
	/// How many rows from the top the panel greys when no sound device came up — MUSIC, SOUNDS, PILOT
	/// MESSAGE and COMPUTER MESSAGE, which <c>PreferencesPanel_Run</c> puts into widget state 2 by
	/// index before it enters its modal loop.
	/// </summary>
	public const int SoundRowCount = 4;

	/// <summary>How many of those carry a value readout beside them.</summary>
	public const int OptionCount = 9;

	/// <summary>Index of the CONTROLS button, which raises the controls panel (<c>ctl_alrt</c>).</summary>
	public const int ControlsButton = 9;

	/// <summary>
	/// Index of the DONE button, and the panel's cancel widget — the constructor sets <c>+0x2fb</c> to
	/// the last widget it built, so [Esc] presses this one.
	/// </summary>
	public const int DoneButton = 10;

	/// <summary>Every button's size: <c>0x42</c> and <c>9</c> doubled. The art is 133x22 and overhangs it by 1x4.</summary>
	public const int ButtonWidth = 132;

	/// <inheritdoc cref="ButtonWidth"/>
	public const int ButtonHeight = 18;

	/// <summary>Every value readout's size: <c>0x3f</c> and <c>7</c> doubled.</summary>
	public const int ValueWidth = 126;

	/// <inheritdoc cref="ValueWidth"/>
	public const int ValueHeight = 14;

	/// <summary>The readouts' font, <c>DAT_004d1eb0</c> — the sixth of the seven the cockpit font init loads.</summary>
	public const string ValueFont = "GREEN6X8";

	/// <summary>
	/// The readouts are left-aligned with this margin, the constructor's <c>2 &lt;&lt; XCoordShift</c>
	/// in <c>DAT_004d1f7a</c>. Button captions are centred with no margin at all.
	/// </summary>
	public const int ValueMarginX = 4;

	/// <summary>Button left edges, <c>DAT_0049e224</c> doubled: two columns and the offset bottom pair.</summary>
	private static readonly int[] ButtonX =
		{ 18, 18, 18, 18, 18, 312, 312, 312, 312, 312, 450 };

	/// <summary>Button top edges, <c>DAT_0049e23a</c> doubled. The rows are 24 apart.</summary>
	private static readonly int[] ButtonY =
		{ 36, 60, 84, 108, 132, 36, 60, 84, 108, 142, 142 };

	/// <summary>Readout left edges, <c>DAT_0049e292</c> doubled.</summary>
	private static readonly int[] ValueX = { 158, 158, 158, 158, 158, 452, 452, 452, 452 };

	/// <summary>
	/// Readout top edges, <c>DAT_0049e2a4</c> doubled — each two rows below its button's, which is
	/// what centres the shorter readout against the taller button.
	/// </summary>
	private static readonly int[] ValueY = { 38, 62, 86, 110, 134, 38, 62, 86, 110 };

	/// <summary>The rect of button <paramref name="index"/>.</summary>
	public static AlertPanelLayout.Rect Button(int index) {
		int i = Math.Clamp(index, 0, ButtonCount - 1);
		return new AlertPanelLayout.Rect(ButtonX[i], ButtonY[i],
			ButtonX[i] + ButtonWidth, ButtonY[i] + ButtonHeight);
	}

	/// <summary>The rect of option <paramref name="index"/>'s value readout.</summary>
	public static AlertPanelLayout.Rect Value(int index) {
		int i = Math.Clamp(index, 0, OptionCount - 1);
		return new AlertPanelLayout.Rect(ValueX[i], ValueY[i],
			ValueX[i] + ValueWidth, ValueY[i] + ValueHeight);
	}

	/// <summary>The plate frame button <paramref name="index"/> draws in widget state <paramref name="state"/>.</summary>
	public static int ButtonFrame(int index, int state) =>
		ButtonFirstFrame + Math.Clamp(state, 0, 3);

	/// <summary>The title's rect, sized to the measured text and centred in the panel's local width.</summary>
	public static AlertPanelLayout.Rect Title(int measuredWidth) {
		int x0 = (Width - measuredWidth) / 2;
		return new AlertPanelLayout.Rect(x0, 0, x0 + measuredWidth, TitleHeight);
	}

	/// <summary>Where the panel lands in a window of the given size.</summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		AlertPanelLayout.Placement.CreateAt(windowWidth, windowHeight, ScreenLeft, ScreenTop);
}
