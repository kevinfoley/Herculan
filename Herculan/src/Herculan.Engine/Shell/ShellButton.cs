namespace Herculan.Engine.Shell;

/// <summary>One frame of one sprite bank — what a widget constructor is handed for each of its states.</summary>
public readonly record struct ShellSprite(string Bank, int Frame);

/// <summary>
/// One of the shell's framed buttons: a rect, two sprites, and a caption centred on whichever sprite
/// is showing.
///
/// <para><b>Two, though the constructor takes three.</b> <c>ESButtonBitmap_Ctor</c> (<c>00409d14</c>)
/// stores three frame pointers at <c>+0x51</c>, <c>+0x55</c> and <c>+0x59</c>, and both of the
/// class's paints (<c>ESButtonBitmap_Paint</c>, <c>0040a05d</c>, and its subclass's <c>ESRadioButton_Paint</c>, <c>0040a26d</c>) pick between the first two on
/// the lit flag and never read the third. So a button has an unlit and a lit face and nothing
/// else.</para>
///
/// <para>Each is a bank <i>and</i> a frame rather than an index into one bank, which the tab strip's
/// leftmost button needs: its two faces come from <c>dba\online.dba</c> while the eight tabs' come
/// from <see cref="ShellArt.ButtonBank"/>.</para>
///
/// <para>A button's art is blitted at its rect's top-left at the sprite's own size rather than
/// stretched to the rect — the same rule the cockpit's widgets follow, for the same reason: the rect
/// is the hit and layout box, the sprite is the art.</para>
///
/// <para><b>The face is whatever the last repaint saw.</b> <see cref="Lit"/> is the widget's own lit
/// flag at <c>+0x45</c>, which a press, a tab handler's latch and a release all write, and
/// <see cref="ShowsLit"/> is what the paint read from it the last time it ran. They differ because
/// <c>ESButtonBitmap_HandleEvent</c> (<c>00409df2</c>) zeroes the flag on a left release without
/// repainting, which is what leaves the active tab drawn lit after the click that latched it
/// (docs/retail/shell/screen-layout.md#which-widget-a-click-reaches).</para>
/// </summary>
public sealed class ShellButton {
	public ShellButton(int id, ShellRect rect, ShellSprite unlit, ShellSprite lit,
			string? caption = null, string fontName = ShellArt.ButtonFont) {
		Id = id;
		Rect = rect;
		UnlitFace = unlit;
		LitFace = lit;
		Caption = caption;
		FontName = fontName;
	}

	/// <summary>Caller-assigned identity — see <see cref="ShellScreen.MenuButtonId"/> for the frame's own ids.</summary>
	public int Id { get; }

	/// <summary>The button's box in canvas pixels, both corners inclusive.</summary>
	public ShellRect Rect { get; }

	/// <summary>Sprite drawn at rest.</summary>
	public ShellSprite UnlitFace { get; }

	/// <summary>Sprite drawn while lit — pressed, or latched as the tab that is up.</summary>
	public ShellSprite LitFace { get; }

	/// <summary>The button's text, or null for an icon button that carries none.</summary>
	public string? Caption { get; set; }

	/// <summary>Which font the caption is drawn in — in this format the font is the colour.</summary>
	public string FontName { get; }

	/// <summary>
	/// Whether the button takes clicks. It does not change what is drawn: this class has no disabled
	/// face, so a gated button in the original looks exactly like an idle one and only stops
	/// responding.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>The lit flag, <c>+0x45</c>.</summary>
	public bool Lit { get; set; }

	/// <summary>Whether the face on screen is the lit one — <see cref="Lit"/> as of the last <see cref="Repaint"/>.</summary>
	public bool ShowsLit { get; private set; }

	/// <summary>The class's paint, <c>ESButtonBitmap_Paint</c> (<c>0040a05d</c>): picks the face from the lit flag as it now stands.</summary>
	public void Repaint() => ShowsLit = Lit;

	/// <summary>Which face is on screen.</summary>
	public ShellSprite Sprite() => ShowsLit ? LitFace : UnlitFace;

	/// <summary>
	/// Whether the caption takes the pressed nudge: the same paint moves it down only while the button is
	/// both lit and enabled.
	/// </summary>
	public bool CaptionNudged => ShowsLit && Enabled;
}
