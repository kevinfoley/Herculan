using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where the cockpit's message ticker sits and how its one line is placed in it — the geometry half
/// of <see cref="MessagePort"/>, whose own state has no pixels in it. See
/// docs/retail/simulation/cockpit-messages.md, "The ticker".
///
/// <para><b>The box is the herc's, not the game's.</b> Its rect is the last field of the
/// <c>.GAU</c> (<see cref="GAUFile.MessageTicker"/>), which is why the ticker sits low in the RAZOR's
/// canopy and high in everyone else's. The constructor coordinate-shifts it into device pixels the way
/// it shifts every other <c>.GAU</c> rect, so everything here is in the 640-wide space the sprites
/// and <c>.HFN</c> glyphs are authored in.</para>
///
/// <para>All measurements are in the <b>forward view's</b> art space and so ride the heads-down pan
/// with the rest of the front panel: the port is a child of that view and pans out of sight with it,
/// exactly as it does in the original's shared canvas.</para>
/// </summary>
/// <param name="Left">Device-pixel left edge of the box.</param>
/// <param name="Top">Device-pixel top edge.</param>
/// <param name="Right">Device-pixel right edge, exclusive of the border the fill draws on it.</param>
/// <param name="Bottom">Device-pixel bottom edge.</param>
public readonly record struct MessageTickerLayout(int Left, int Top, int Right, int Bottom) {
	/// <summary>
	/// Device pixels the text is clipped in from each side, leaving the box's border and a margin
	/// clear — <c>MessageTicker_Paint</c> (<c>00436cec</c>) narrows the live clip rect by <c>3 &lt;&lt; XCoordShift</c> on both
	/// edges after it has drawn the fill and before it draws the glyphs, so the line slides under the
	/// frame rather than over it.
	/// </summary>
	public const int TextInset = 3 * CoordScale;

	/// <summary>
	/// Colour the box is flooded with before the text goes in — <c>COLORS.DAT</c> id 19, black. The
	/// erase (<c>MessagePort_Erase</c>, <c>00436fd0</c>) fills the same rect with the same colour and nothing else, which is
	/// what identifies it as the background rather than as part of the frame.
	/// </summary>
	public const int BackgroundColorId = 19;

	/// <summary>
	/// And the one-pixel frame around it — id 9, red. The paint installs a second brush of style 4
	/// over the same rect, and style 4 is four line draws round its edges (<c>Raster_FillRect</c>, <c>004865f8</c>).
	/// </summary>
	public const int BorderColorId = 9;

	/// <summary>
	/// The font the line is written in — <c>ColorSchemePanels[2]</c>, which
	/// <see cref="MfdLayout.HostileNameFont"/> also names. Red on black: this is the computer warning
	/// the player is meant to catch out of the corner of an eye.
	/// </summary>
	public const string Font = "CPRED";

	/// <summary>
	/// Device pixels one authored <c>.GAU</c> unit becomes — the original's
	/// <c>1 &lt;&lt; VideoMode_XCoordShift</c>, which every rect and every distance here goes through.
	/// </summary>
	public const int CoordScale = 1 << CockpitViewGeometry.CoordShift;

	/// <summary>Box width in device pixels.</summary>
	public int Width => Right - Left;

	/// <summary>Box height in device pixels.</summary>
	public int Height => Bottom - Top;

	/// <summary>Left edge text is clipped to.</summary>
	public int ClipLeft => Left + TextInset;

	/// <summary>Right edge text is clipped to.</summary>
	public int ClipRight => Right - TextInset;

	/// <summary>
	/// This herc's ticker box, or null when its <c>.GAU</c> carries no rect for one.
	/// </summary>
	public static MessageTickerLayout? From(CockpitArt? hud) {
		if (hud?.Gau.MessageTicker is not { } rect) {
			return null;
		}

		const int s = CoordScale;
		int left = rect.Origin.X * s;
		int top = rect.Origin.Y * s;
		int right = (rect.Origin.X + rect.Size.Width) * s;
		int bottom = (rect.Origin.Y + rect.Size.Height) * s;
		return right > left && bottom > top ? new MessageTickerLayout(left, top, right, bottom) : null;
	}

	/// <summary>
	/// Device-pixel x of the line's first glyph — the scrolling marquee's own position,
	/// <c>MessageTicker_ScrollText</c> (<c>00436f70</c>)'s <c>right - 0x23 &lt;&lt; XCoordShift * elapsed / 0x3c</c>. It starts at the
	/// box's right edge and travels left at about 73 device pixels a second, and there is no wrap: a line
	/// that outlives its own width simply leaves.
	///
	/// <para>The one message that does not scroll is centred on its measured width instead, with no
	/// trailing-advance trim — see <see cref="MessageTicker.Centered"/>.</para>
	/// </summary>
	public int TextLeft(in MessageTicker ticker, int textWidth) => ticker.Centered
		? ((Width - textWidth) >> 1) + Left
		: Right - (int)(ScrollPixelsPerInterval * ticker.ScrollTicks / MessagePort.TicksPerTimingUnit);

	/// <summary>
	/// Device-pixel top of the glyph row. <c>MessageTicker_Paint</c> (<c>00436cec</c>) anchors at
	/// <c>((height - cellHeight) &gt;&gt; 1) + inkHeight + 1</c> and the glyph blitter subtracts
	/// <c>inkHeight</c> back off, so what survives is the cell centred in the box and nudged one pixel
	/// down — not <see cref="HudFont.Place"/>'s rule, which centres <see cref="HudFont.InkHeight"/>
	/// instead and is what the <i>labels</i> everywhere else in the cockpit use.
	/// </summary>
	public int TextTop(HudFont font) => Top + ((Height - font.CellHeight) >> 1) + 1;

	private const int ScrollPixelsPerInterval = MessagePort.ScrollUnitsPerInterval * CoordScale;
}
