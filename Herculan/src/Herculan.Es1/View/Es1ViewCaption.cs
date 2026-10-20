using Herculan.Engine.Content;

namespace Herculan.Es1.View;

/// <summary>
/// What <c>ViewChain_DrawCaption</c> (<c>ES1DBSIM 00058b68</c>) draws with (docs/es1/simulation/main-loop.md#the-views--viewchain_apply-0005894c):
/// the font <c>000bd556</c>, <c>dfn\green6x8</c>, the sixth of the seven <c>Widget_LoadStylesAndFonts</c> loads into
/// <c>000bd542</c>–<c>000bd55a</c> (docs/es1/formats/styles-dat.md); and <c>str\STRINGS0.STR</c>'s group 32, the strings
/// <c>000bd4c4</c>[0–3] <c>VIEW: </c>, <c>CONTROL: </c>, <c>CAMERA</c> and <c>HERC</c>, and group 14, <c>000bd3e0</c>'s
/// <c>YOU</c> (docs/es1/formats/str-strings.md#the-groups).
/// </summary>
public sealed class Es1ViewCaption {
	/// <summary>The two runs' x, the words <c>00096c3c</c> and <c>00096c3e</c> of the image.</summary>
	public const int ViewX = 50, ControlX = 215;

	/// <summary>The band's brush colour, <c>0x3f</c>.</summary>
	public const byte BandColor = 0x3f;

	/// <summary>The groups of <c>STRINGS0.STR</c> the caption reads.</summary>
	private const int CaptionGroup = 32, YouGroup = 14;

	/// <summary>The caption's strings and font, as loaded.</summary>
	public Es1ViewCaption(HudFont font, string view, string control, string camera, string herc, string you) {
		Font = font;
		ViewText = view;
		ControlText = control;
		CameraText = camera;
		HercText = herc;
		YouText = you;
	}

	public HudFont Font { get; }

	/// <summary><c>000bd4c4</c>[0], <c>VIEW: </c>.</summary>
	public string ViewText { get; }

	/// <summary><c>000bd4c4</c>[1], <c>CONTROL: </c>.</summary>
	public string ControlText { get; }

	/// <summary><c>000bd4c4</c>[2], <c>CAMERA</c>, while <c>InputDrivesCamera</c> is set.</summary>
	public string CameraText { get; }

	/// <summary><c>000bd4c4</c>[3], <c>HERC</c>, while it is clear.</summary>
	public string HercText { get; }

	/// <summary><c>000bd3e0</c>, <c>YOU</c>, the name for the player.</summary>
	public string YouText { get; }

	/// <summary>The font and the strings from <paramref name="content"/>, or null when any is missing, so nothing is drawn.</summary>
	public static Es1ViewCaption? Load(GameContent content) {
		if (HudFont.Load(content, "GREEN6X8", "dfn") is not { } font
				|| SimStrings.LoadUntranslated(content, SimStrings.SimulatorStrings) is not { } strings
				|| strings.Text(CaptionGroup, 0) is not { } view
				|| strings.Text(CaptionGroup, 1) is not { } control
				|| strings.Text(CaptionGroup, 2) is not { } camera
				|| strings.Text(CaptionGroup, 3) is not { } herc
				|| strings.Text(YouGroup, 0) is not { } you) {
			return null;
		}

		return new Es1ViewCaption(font, view, control, camera, herc, you);
	}

	/// <summary>
	/// <c>ViewChain_DrawCaption</c>'s drawing on the draw page, unclipped and untranslated: the band's top row
	/// halfway between <paramref name="rectBottom"/> (<c>ExternalView_GetRect</c>'s <c>+0xc</c>) and row 199 less half
	/// the font's cell height, and at most 198 less that height; the band, columns 0 to 319 and the cell height's rows
	/// below its top row, filled with <see cref="BandColor"/>; then on its bottom row <see cref="ViewText"/> and
	/// <paramref name="name"/> at <see cref="ViewX"/>, and <see cref="ControlText"/> and <see cref="CameraText"/> or
	/// <see cref="HercText"/> at <see cref="ControlX"/>.
	/// </summary>
	public void Draw(Es1VideoMemory display, int rectBottom, string name, bool cameraDriven) {
		int height = Font.CellHeight;
		int top = (short)(((199 - rectBottom) >> 1) + rectBottom) - (height >> 1);
		if (0xc6 - height < top) {
			top = 0xc6 - height;
		}

		int bottom = top + height;
		display.FillRect(0, top, 319, bottom, BandColor);
		DrawString(display, ViewText + name, ViewX, bottom);
		DrawString(display, ControlText + (cameraDriven ? CameraText : HercText), ControlX, bottom);
	}

	/// <summary>
	/// <c>HudFont_DrawString</c>: glyph after glyph from <paramref name="x"/>, each advancing by its width, each blitted
	/// by <c>HudFont_DrawGlyph</c> (<c>00069d64</c>) with its top row at <paramref name="y"/> less the font's ink height
	/// (docs/es1/simulation/cockpit-widgets.md), its index-0 pixels left as they were. PLACEHOLDER: the underline
	/// <c>HudFont_DrawGlyph</c> draws along the pen's row while the installed render context's <c>+0x230</c> is set is not
	/// drawn; what that byte holds in the 3D render context is unread.
	/// </summary>
	private void DrawString(Es1VideoMemory display, string text, int x, int y) {
		int pen = x;
		foreach (char c in text) {
			if (Font.GlyphIndex(c) is not { } index) {
				continue;
			}

			var glyph = Font.Glyphs[index];
			display.BlitTransparent(glyph.ImageData ?? [], glyph.Cols, glyph.Rows, pen, y - Font.InkHeight);
			pen += glyph.Cols;
		}
	}
}
