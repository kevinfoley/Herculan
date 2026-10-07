namespace Herculan.Engine.View;

/// <summary>
/// The screen of the cockpit view manager's view 4, in the 640x480 mode's device pixels: the 3D rect
/// <c>CockpitViewManager_Ctor</c> (<c>00429660</c>) sets as the default, and the caption
/// <c>ViewChain_DrawCaption</c> (<c>0045e1ec</c>) paints under it. See docs/retail/simulation/external-views.md, "What the external view
/// shows".
/// </summary>
public static class ExternalViewLayout {
	/// <summary>Rows of the 3D view: <c>(0xc3 &lt;&lt; 1)</c>, the rest of the screen below it.</summary>
	public const int ViewRows = 0xc3 << 1;

	/// <summary>The projection centre's row: <c>0x5a &lt;&lt; 1</c> down the view. Its column is the middle.</summary>
	public const int CentreRow = 0x5a << 1;

	/// <summary>The screen's height, whose last row the caption is centred against.</summary>
	public const int ScreenRows = 480;

	/// <summary>Where the VIEW run starts: <c>DAT_0049f056</c>, 50, shifted.</summary>
	public const int ViewCaptionX = 50 << 1;

	/// <summary>Where the CONTROL run starts: <c>DAT_0049f058</c>, 215, shifted.</summary>
	public const int ControlCaptionX = 215 << 1;

	/// <summary>
	/// The caption's font, <c>DAT_004d1eb0</c> — the green 6x8 face.
	/// </summary>
	public const string CaptionFont = "GREEN6X8";

	/// <summary><c>STRINGS0.STR</c> group 36: <c>VIEW: </c>, <c>CONTROL: </c>, <c>CAMERA</c>, <c>HERC</c>.</summary>
	public const int CaptionGroup = 36;

	/// <summary><c>STRINGS0.STR</c> group 17, the name the caption gives the player: <c>YOU</c>.</summary>
	public const int PlayerNameGroup = 17;

	/// <summary>
	/// The row the caption's glyphs stand on — <c>HudFont_DrawString</c>'s y, which a glyph hangs its
	/// ink height above. The cell is centred between the 3D view's last row and the screen's, and kept
	/// clear of the screen's own last two.
	/// </summary>
	public static int CaptionBaseline(int cellHeight) {
		int viewBottom = ViewRows - 1;
		int top = ((ScreenRows - 1 - viewBottom) >> 1) + viewBottom - (cellHeight >> 1);
		if (ScreenRows - 2 - cellHeight < top) {
			top = ScreenRows - 2 - cellHeight;
		}

		return top + cellHeight;
	}
}
