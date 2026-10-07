namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where the pause panel's pieces sit — <c>PausePanel_Ctor</c> (<c>004561c0</c>), the small sibling
/// of the status alert. [P] raises it as <c>PAUSE</c> and [Ctrl+Q] as <c>EXIT EARTHSIEGE?</c>.
///
/// <para>It is the same panel class as <see cref="StatusAlertPanelLayout"/> in everything but size
/// and arrangement: the same base, the same modal loop, the same
/// <see cref="StatusAlertPanel.StringsFileName"/> read the same three ways, and a vtable whose six
/// entries are identical. What differs is that it is 178x68 rather than 444x218, it blits
/// <c>GNL_ALRT.HBA</c> frame 1 rather than frame 0, its two buttons are <b>stacked</b> rather than
/// side by side, and it builds no body labels at all. See
/// docs/retail/simulation/alert-panels.md.</para>
/// </summary>
public static class PausePanelLayout {
	/// <summary>The panel's declared width, <c>0x59</c> doubled.</summary>
	public const int Width = 178;

	/// <summary>
	/// The panel's declared height, <c>0x22</c> doubled. The plate is 181x70 — <b>larger</b> than the
	/// declared size on both axes, where the other two panels' plates are smaller than theirs.
	/// </summary>
	public const int Height = 68;

	/// <summary>The plate, <c>GNL_ALRT.HBA</c> frame 1 — the status alert's bank, its second frame.</summary>
	public const string PlateBank = StatusAlertPanelLayout.PlateBank;

	/// <inheritdoc cref="PlateBank"/>
	public const int PlateFrame = 1;

	/// <summary>The title bar's height — the band the title is vertically centred in.</summary>
	public const int TitleHeight = 16;

	/// <summary>Every button's left edge and size: <c>0xe</c>, <c>0x3d</c> and <c>9</c> doubled.</summary>
	public const int ButtonX = 28;

	/// <inheritdoc cref="ButtonX"/>
	public const int ButtonWidth = 122;

	/// <inheritdoc cref="ButtonX"/>
	public const int ButtonHeight = 18;

	/// <summary>The one button's top edge when the status has only one.</summary>
	public const int SingleButtonY = 24;

	/// <summary>
	/// The two buttons' top edges when it has two. They share one x and differ only in y, which is
	/// what makes this panel's pair stack instead of sitting side by side.
	/// </summary>
	public static readonly int[] PairButtonY = { 16, 42 };

	/// <summary>
	/// The rect of button <paramref name="index"/> of <paramref name="buttonCount"/>.
	///
	/// <para><b>The three button y-origins are scaled by the horizontal shift</b>, not the vertical
	/// one — the constructor's own slip, and the only place in the family where an axis is crossed.
	/// It costs nothing: DBSIM's two coordinate shifts are equal in both video modes it supports, so
	/// the numbers come out the same either way.</para>
	/// </summary>
	public static AlertPanelLayout.Rect Button(int index, int buttonCount) {
		int y0 = buttonCount <= 1 ? SingleButtonY : PairButtonY[Math.Clamp(index, 0, 1)];
		return new AlertPanelLayout.Rect(ButtonX, y0, ButtonX + ButtonWidth, y0 + ButtonHeight);
	}

	/// <summary>The title's rect, sized to the measured text and centred in the panel's local width.</summary>
	public static AlertPanelLayout.Rect Title(int measuredWidth) {
		int x0 = (Width - measuredWidth) / 2;
		return new AlertPanelLayout.Rect(x0, 0, x0 + measuredWidth, TitleHeight);
	}

	/// <summary>Where the panel lands in a window of the given size.</summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		AlertPanelLayout.Placement.Create(windowWidth, windowHeight, Width, Height);
}
