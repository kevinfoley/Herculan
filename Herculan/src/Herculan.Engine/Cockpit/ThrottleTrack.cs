using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The console's throttle slider, as geometry: where the knob sits for a given throttle setting, and
/// what setting a point on the track means. Reverse-engineered from <c>ThrottleGauge_Ctor</c>
/// (<c>00447b84</c>), its vertical slider child (<c>00447e24</c>) and the shared slider base
/// (<c>004524a8</c> and the value/position pair at <c>00452644</c> / <c>00452628</c>).
///
/// <para><b>The track and the two bars.</b> The <c>.GAU</c> block at offset 1000 is one widget
/// record: the gauge constructor reads ints 4-7 as the slider's track rect and ints 8-15 as
/// <i>two more rects</i> (<see cref="HThrottle.BarCorners"/>), which it hands to the vertical
/// LED-bar constructor (<c>00439344</c>) with ranges <c>+0x400</c> and <c>-0x400</c> — the forward
/// and reverse fill bars either side of centre.</para>
///
/// <para><b>The bars are a cut feature, and not drawing them is faithful.</b> DBSIM does not draw
/// them either: the slider keeps them as private fields, never registers them with the widget tree,
/// and its paint reads them only for their rects, which it unions into the region it invalidates.
/// Their bar-graph draw routines have no callers anywhere in the image. The gauge's <c>+0xb1</c>
/// speed-fraction value is the other half of the same cut feature — the knob shows the throttle
/// asked for, the bars would have shown the speed reached.</para>
///
/// <para><b>Units.</b> Everything here is device pixels, the 640-wide space the rest of the cockpit
/// art and the sprite banks live in — <c>GauThrottle_ApplyCoordShift</c> (<c>004488cc</c>) shifts the whole block left by the video
/// mode's coordinate shift before the gauge ever sees it, which is
/// <see cref="CockpitArt.GauToPixelScale"/>.</para>
///
/// <para><b>Sign.</b> Up the track is forward. The original arrives at that through two sign flips
/// that cancel — the slider's own getter reads the top of the track as its minimum, and the gauge
/// negates what it reads (<c>00447de0</c>, gated on the vertical variant's <c>+0xc1 == 0</c>) — so
/// what this type exposes is the settled convention: positive is forward, and the knob's travel is
/// linear in it.</para>
/// </summary>
public readonly struct ThrottleTrack {
	/// <summary>Full throttle either way, the slider's own <c>±0x400</c> limits.</summary>
	public const short Full = 0x400;

	/// <summary>The sprite bank the track's two frames come from.</summary>
	public const string SpriteBank = "THROTTLE";

	/// <summary>Bank frame 0 — the 2x12 tick the gauge parks beside the track's centre.</summary>
	public const int TickFrame = 0;

	/// <summary>Bank frame 1 — the 28x12 knob that rides the track.</summary>
	public const int KnobFrame = 1;

	/// <summary>
	/// A track from its measurements, all in device pixels. <see cref="From"/> is the normal way in;
	/// this exists for callers that already have the numbers.
	/// </summary>
	public ThrottleTrack(int left, int top, int right, int bottom, int knobHeight, int tickOffsetX) {
		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
		KnobHeight = knobHeight;
		TickOffsetX = tickOffsetX;
	}

	/// <summary>Track left edge, device pixels.</summary>
	public int Left { get; }

	/// <summary>Track top edge, device pixels.</summary>
	public int Top { get; }

	/// <summary>Track right edge, device pixels.</summary>
	public int Right { get; }

	/// <summary>Track bottom edge, device pixels.</summary>
	public int Bottom { get; }

	/// <summary>The knob sprite's height — the slider's own <c>+0x2c</c>, read off bank frame 1.</summary>
	public int KnobHeight { get; }

	/// <summary>
	/// Where the centre tick sits relative to <see cref="Left"/>, in device pixels. The gauge takes
	/// <see cref="HThrottle.TickOffsetX"/> — the one int of the block the loader does not pre-scale —
	/// and shifts it by the video mode's x coordinate shift itself.
	/// </summary>
	public int TickOffsetX { get; }

	/// <summary>How far the knob's bottom edge can travel, in device pixels.</summary>
	public int Travel => Bottom - Top - KnobHeight;

	/// <summary>
	/// The slider's Q16 pixels-per-unit scale, <c>SliderWidget_RecomputeScaleV</c> (<c>00452694</c>)'s <c>+0x20</c>. Zero when the
	/// track is too short to hold the knob, which no retail <c>.GAU</c> is.
	/// </summary>
	public int Scale => Travel > 0 ? Travel * 65536 / (Full * 2) : 0;

	/// <summary>
	/// This herc's throttle track, or null when its <c>.GAU</c> has no throttle block or the knob
	/// sprite is missing — either way there is nothing to place.
	/// </summary>
	public static ThrottleTrack? From(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.Throttle is not { } widget || art.Sprites?.Sprite(SpriteBank, KnobFrame) is not { } knob) {
			return null;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;
		var track = new ThrottleTrack(
			widget.Origin.X * scale, widget.Origin.Y * scale,
			(widget.Origin.X + widget.Size.Width) * scale,
			(widget.Origin.Y + widget.Size.Height) * scale,
			knob.Height,
			widget.TickOffsetX * scale);

		return track.Travel > 0 ? track : null;
	}

	/// <summary>
	/// Where the knob's top edge sits for a throttle setting — <c>SliderWidget_SetValueV</c> (<c>00452644</c>), which places the
	/// knob's <i>bottom</i> and lets the paint derive the top from it.
	/// </summary>
	public int KnobTopFor(int throttle) => KnobBottomFor(throttle) - KnobHeight;

	/// <summary>Where the knob's bottom edge sits for a throttle setting, clamped to the track.</summary>
	public int KnobBottomFor(int throttle) {
		int clamped = Math.Clamp(throttle, -Full, Full);
		return Bottom - (int)(((long)(clamped + Full) * Scale) >> 16);
	}

	/// <summary>
	/// The throttle setting a pointer at <paramref name="deviceY"/> asks for: the drag handler
	/// (<c>SliderWidget_DragToPointV</c>, <c>004525d8</c>) clamps the pointer into the track and puts the knob's bottom edge there,
	/// and the getter reads the setting back off the knob's top.
	///
	/// <para>The pointer lands on the knob's <i>bottom</i>, not its centre, which is the original's
	/// own behaviour: grabbing the knob makes it jump so its lower edge is under the cursor.</para>
	/// </summary>
	public short ThrottleAt(float deviceY) {
		if (Scale <= 0) {
			return 0;
		}

		int knobBottom = (int)Math.Clamp(MathF.Round(deviceY), Top + KnobHeight, Bottom);
		return (short)Math.Clamp(
			Full - (int)(((long)(knobBottom - KnobHeight - Top) << 16) / Scale), -Full, Full);
	}
}
