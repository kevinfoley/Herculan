using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where the pilot and squad channel's message box sits and how its line is placed in it — the
/// geometry and colour half of <see cref="SquadMessagePort"/>, the way
/// <see cref="MessageTickerLayout"/> is the computer ticker's.
///
/// <para>Unlike the computer's fixed box with a scrolling line, this one is <b>sized to its text and
/// centered on the screen</b>: the paint sets the box to the composed line's width plus
/// <see cref="HorizontalPadding"/> on each side and centers it on the screen's midline every time
/// it draws. Only the vertical half of the authored rect (<see cref="GAUFile.PilotMessagePort"/>)
/// is used.</para>
///
/// <para><b>The colors are the speaker's, not the port's.</b> A message from a squadmate fills with
/// that pilot's own comm-box color, <see cref="HudColorTable.PilotColorId"/>, and is framed in the
/// palette entry <i>one below</i> the fill — raw palette arithmetic on the resolved index, not a
/// second <c>COLORS.DAT</c> id. A message with no squadmate behind it falls back to the computer's
/// own black and red, through <see cref="NoSpeakerFillColorId"/>.</para>
///
/// <para>Ports the box half of <c>PilotMessagePort_Speak</c> (<c>00435d9c</c>); see
/// docs/retail/simulation/cockpit-messages.md#its-box. The training port paints a different box,
/// <see cref="TrainingMessageLayout"/>.</para>
/// </summary>
/// <param name="Top">Device-pixel top edge, from the <c>.GAU</c>.</param>
/// <param name="Bottom">Device-pixel bottom edge, from the same rect.</param>
public readonly record struct PilotMessageBoxLayout(int Top, int Bottom) {
	/// <summary>
	/// Device pixels the box extends past its text on each side — the paint's
	/// <c>10 &lt;&lt; XCoordShift</c> off the left edge and <c>0x14 &lt;&lt; XCoordShift</c> added to
	/// the width, which is the same margin twice.
	/// </summary>
	public const int HorizontalPadding = 10 * MessageTickerLayout.CoordScale;

	/// <summary>
	/// The font the line is written in — <c>ColorSchemePanels[2]</c>, <c>CPRED</c>. The same font the
	/// ticker uses, so the text is red on both, and it is the font that makes it red: an
	/// <c>.HFN</c> is a single-colour stencil.
	/// </summary>
	public const string Font = MessageTickerLayout.Font;

	/// <summary>
	/// What fills the box when the message has no squadmate behind it — <c>COLORS.DAT</c> id 19,
	/// black, the computer's own background.
	/// </summary>
	public const int NoSpeakerFillColorId = MessageTickerLayout.BackgroundColorId;

	/// <summary>And its frame in that case — id 9, red.</summary>
	public const int NoSpeakerBorderColorId = MessageTickerLayout.BorderColorId;

	/// <summary>Separator between the speaker's name and their line, from <c>PilotMessagePort_ComposeLine</c> (<c>00435d0c</c>).</summary>
	public const string NameSeparator = ": ";

	/// <summary>
	/// How much of the message text is copied in after the name — the composer's <c>strncat</c>
	/// length. Nothing retail ships comes close to it.
	/// </summary>
	public const int MaxTextLength = 0x4a;

	/// <summary>Box height in device pixels.</summary>
	public int Height => Bottom - Top;

	/// <summary>
	/// This herc's box, or null when its <c>.GAU</c> carries no rect for one. Every retail file
	/// authors it as <c>0,y - 320,y+10</c>; the width is discarded, so only <paramref name="hud"/>'s
	/// y pair reaches the screen.
	/// </summary>
	public static PilotMessageBoxLayout? From(CockpitArt? hud) {
		if (hud?.Gau.PilotMessagePort is not { } rect) {
			return null;
		}

		const int s = MessageTickerLayout.CoordScale;
		int top = rect.Origin.Y * s;
		int bottom = (rect.Origin.Y + rect.Size.Height) * s;
		return bottom > top ? new PilotMessageBoxLayout(top, bottom) : null;
	}

	/// <summary>
	/// Device-pixel left edge for a line <paramref name="textWidth"/> wide, on a screen
	/// <paramref name="screenWidth"/> device pixels across — the paint's
	/// <c>(screen / 2) - (width / 2) - padding</c>.
	/// </summary>
	public static int Left(int screenWidth, int textWidth) =>
		(screenWidth >> 1) - (textWidth >> 1) - HorizontalPadding;

	/// <summary>Right edge: the left edge plus the text and both margins.</summary>
	public static int Right(int screenWidth, int textWidth) =>
		Left(screenWidth, textWidth) + textWidth + HorizontalPadding * 2;

	/// <summary>Device-pixel x of the line's first glyph — centred, with no margin of its own.</summary>
	public static int TextLeft(int screenWidth, int textWidth) =>
		(screenWidth >> 1) - (textWidth >> 1);

	/// <summary>
	/// Device-pixel top of the glyph row. The paint anchors it at
	/// <c>bottom - ((height - inkHeight) &gt;&gt; 1)</c> and the glyph blitter subtracts
	/// <see cref="HudFont.InkHeight"/> back off — so, unlike the ticker, it is the <i>ink</i> that is
	/// centred in the box here and not the cell.
	/// </summary>
	public int TextTop(HudFont font) => Bottom - ((Height - font.InkHeight) >> 1) - font.InkHeight;
}
