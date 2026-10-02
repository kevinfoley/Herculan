using HercWorks.Core.Data.File.Gau;

namespace Herculan.Engine.Content;

/// <summary>
/// The manual's <b>Rotation Indicator</b>: the sliding bar across the top of the front-window HUD
/// that shows how far the turret is turned off centre, green while it is centred and yellow once it
/// is not.
///
/// <para>Reverse-engineered from <c>Gau_RovingGunsightWidget</c> (<c>0043c7d8</c>), which builds it
/// as one of the gunsight complex's children, its class constructor <c>HudRotationIndicator_Ctor</c> (<c>0043b438</c>), the shared
/// slide-bar base <c>HudSlideBar_CtorBase</c> (<c>0043b378</c>) and the paint at <c>HudRotationIndicator_Paint</c> (<c>0043b4a4</c>).</para>
///
/// <para><b>Where it comes from.</b> Not a <c>.GAU</c> rect of its own. The complex reads the rect
/// at offset 1104 — the heading tape's box, <see cref="HHeadingTape"/> — and derives
/// this bar from it with literals in the constructor: <c>+15, -10</c> from the rect's top-left, 90
/// wide and 4 tall. Every retail file puts that rect at <c>100,y - 220,y+17</c>, so the bar lands
/// horizontally centred on the 320-wide HUD.</para>
///
/// <para><b>What it shows.</b> <c>Player_PerFrameCockpitUpdate</c> (<c>0041b130</c>) hands the
/// gunsight complex the machine's heading, twist angle and pitch angle each frame, and the complex
/// forwards each one's <i>delta</i> to a child. This is the twist angle's child, so its value tracks
/// <c>mech+0x298</c> exactly, clamped to the <see cref="Limit"/> its constructor is given — wider
/// than the 14000 twist limit every player chassis states, so the bar never actually reaches its
/// end stops.</para>
///
/// <para><b>Units.</b> Device pixels, the 640-wide space the sprite banks and the rest of the
/// cockpit art live in: the loader shifts the whole <c>.GAU</c> block by the video mode's coordinate
/// shift, and the constructor's own literals are shifted the same way where they are applied.</para>
/// </summary>
public readonly struct RotationIndicator {
	/// <summary>The sprite bank all three frames come from.</summary>
	public const string SpriteBank = "HUD";

	/// <summary>Bank frame 11 — the 182x10 track the bar slides along.</summary>
	public const int TrackFrame = 11;

	/// <summary>Bank frame 13 — the 62x4 bar, in the colour it wears while the turret is centred.</summary>
	public const int CenteredFrame = 13;

	/// <summary>Bank frame 12 — the same bar in its off-centre colour.</summary>
	public const int OffCenterFrame = 12;

	/// <summary>
	/// The travel the bar's span maps onto, <c>±0x38e3</c> (about 80°) — the pair
	/// <c>Gau_RovingGunsightWidget</c> hands the bar's <c>SetLimits</c> slot. It is wider than any
	/// player chassis' twist limit, so the ends of the track are never reached.
	/// </summary>
	public const short Limit = 0x38e3;

	/// <summary>
	/// How far off centre the turret has to be before the bar changes colour: the paint's own
	/// <c>|value| > 299</c>, about 1.6°, which is small enough that any deliberate movement trips it.
	/// </summary>
	public const short CenteredTolerance = 299;

	private RotationIndicator(int trackX, int trackY, int barY, int span) {
		TrackX = trackX;
		TrackY = trackY;
		BarY = barY;
		Span = span;
	}

	/// <summary>Left edge of the track sprite, device pixels.</summary>
	public int TrackX { get; }

	/// <summary>Top edge of the track sprite — the bar's own row less the paint's two-unit lift.</summary>
	public int TrackY { get; }

	/// <summary>Top edge of the sliding bar, device pixels.</summary>
	public int BarY { get; }

	/// <summary>The bar's travel, the constructor's 90-unit width in device pixels.</summary>
	public int Span { get; }

	/// <summary>
	/// This herc's rotation indicator, or null when its <c>.GAU</c> has no gunsight rect or the HUD
	/// bank is missing its frames — either way there is nothing to place.
	/// </summary>
	public static RotationIndicator? From(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.HeadingTape is not { } widget
			|| art.Sprites?.Sprite(SpriteBank, TrackFrame) is null
			|| art.Sprites?.Sprite(SpriteBank, CenteredFrame) is null) {
			return null;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;
		int barX = (widget.Origin.X + BarOffsetX) * scale;
		int barY = (widget.Origin.Y + BarOffsetY) * scale;

		return new RotationIndicator(barX, barY - (TrackLift * scale), barY, BarSpan * scale);
	}

	/// <summary>
	/// Where the bar's left edge sits for a twist angle: the value mapped across the track, then the
	/// same <c>15</c> units backed off again that were added to reach the track's own left edge. The
	/// bar sprite is 31 units wide, so that lands it centred on the point rather than hung off it —
	/// one unit shy of exactly centred, which is the original's arithmetic and not a rounding slip
	/// here. Its own half-pixel nudge on negative values is kept for the same reason.
	/// </summary>
	public int BarLeftFor(short twist) {
		int clamped = Math.Clamp((int)twist, -Limit, Limit);
		int left = TrackX + ((clamped + Limit) * Span / (Limit * 2));
		if (clamped < 0) {
			left++;
		}

		return left - (BarOffsetX * (int)CockpitArt.GauToPixelScale);
	}

	/// <summary>Which of the two bar frames a twist angle wears.</summary>
	public static int FrameFor(short twist) =>
		twist > CenteredTolerance || twist < -CenteredTolerance ? OffCenterFrame : CenteredFrame;

	/// <summary>The constructor's own offsets from the gunsight rect's top-left, in <c>.GAU</c> units.</summary>
	private const int BarOffsetX = 15;

	private const int BarOffsetY = -10;

	/// <summary>The bar's travel in <c>.GAU</c> units, and how far above it the track sprite sits.</summary>
	private const int BarSpan = 90;

	private const int TrackLift = 2;
}
