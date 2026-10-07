using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where every piece of the [F11] objectives panel sits, in the 640x480 device pixels the original
/// draws its screen in. <c>ObjectivesPanel_Ctor</c> writes the whole block out once into <c>.bss</c>
/// (<c>DAT_004d1f84</c>..<c>DAT_004d1fa4</c>) as <c>value &lt;&lt; VideoMode_?CoordShift</c>, so
/// every number here is an authored 320-wide coordinate doubled — the same relationship
/// <see cref="CockpitArt.GauToPixelScale"/> is. See docs/retail/simulation/alert-panels.md.
///
/// <para>Rects are panel-local: the panel's own top-left is (0, 0), which is where the background
/// plate is blitted and the space every label's rect is in. The screen, the rect type and the
/// window transform are <see cref="AlertPanelLayout"/>'s, shared with the other panel this engine
/// draws.</para>
/// </summary>
public static class ObjectivesPanelLayout {
	/// <summary>The panel's declared width, <c>0x13b</c> doubled. Also the plate's own width.</summary>
	public const int Width = 630;

	/// <summary>
	/// The panel's declared height, <c>0x8b</c> doubled — and <b>not</b> the plate's, which is 230.
	/// The extra 48 rows are what the panel is centred by and nothing draws in them, which is why the
	/// plate sits 24 rows above the middle of the screen rather than on it.
	/// </summary>
	public const int Height = 278;

	/// <summary>The background plate's height, from <c>hba\OBJ_ALRT.HBA</c> frame 0.</summary>
	public const int PlateHeight = 230;

	/// <summary>How many objective lines the panel builds labels for. An eighth entry is dropped.</summary>
	public const int LineCount = 7;

	/// <summary>The title bar's height — the band the title is vertically centred in.</summary>
	public const int TitleHeight = 16;

	/// <summary>First line's top edge.</summary>
	public const int FirstLineY = 34;

	/// <summary>A line label's height, and the pitch between them. Both are 10 doubled.</summary>
	public const int LineHeight = 20;

	/// <summary>The panel's left edge on the screen.</summary>
	public const int ScreenLeft = (AlertPanelLayout.ScreenWidth - Width) / 2;

	/// <summary>The RETURN button, the panel's only widget. Its plate art overhangs it by 4x2px.</summary>
	public static readonly AlertPanelLayout.Rect Button = new(254, 186, 254 + 120, 186 + 20);

	/// <summary>
	/// The rect the <paramref name="index"/>'th objective line is centred in.
	///
	/// <para><b>Its horizontal extent is the panel's screen rect, not its local one</b> — the
	/// constructor takes x from <c>panel+0x04</c> and <c>panel+0x0c</c>, the absolute pair, while
	/// every other rect it builds uses the local one. The label is centred, so the effect is that the
	/// objective text sits <see cref="ScreenLeft"/> pixels right of the panel's centre line while the
	/// title and the button sit on it. Reproduced rather than corrected: it is what the original
	/// draws, and the retail screenshot shows the offset.</para>
	/// </summary>
	public static AlertPanelLayout.Rect Line(int index) =>
		new(ScreenLeft, FirstLineY + LineHeight * index,
			ScreenLeft + Width, FirstLineY + LineHeight * index + LineHeight);

	/// <summary>
	/// The title's rect, which is sized to the measured text and then centred in the panel's local
	/// width — the constructor's only measurement.
	/// </summary>
	public static AlertPanelLayout.Rect Title(int measuredWidth) {
		int x0 = (Width - measuredWidth) / 2;
		return new AlertPanelLayout.Rect(x0, 0, x0 + measuredWidth, TitleHeight);
	}

	/// <summary>Where the panel lands in a window of the given size.</summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		AlertPanelLayout.Placement.Create(windowWidth, windowHeight, Width, Height);
}
