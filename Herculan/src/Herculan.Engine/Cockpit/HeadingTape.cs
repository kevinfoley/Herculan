using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using HercWorks.Core.Data.File.Gau;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The manual's <b>Heading Indicator</b>: the compass strip across the top of the front-window HUD,
/// child 1 of the roving-gunsight complex. Its geometry and the slice of strip an angle selects —
/// <c>HudHeadingTape_Recompute</c> (<c>0043b5dc</c>), <c>HudHeadingTape_SetHeading</c>
/// (<c>0043b654</c>) and the paint at <c>0043b6dc</c>.
///
/// <para>The whole compass is art: the <c>hudhtick</c> bank is one turn of strip cut into frames, and
/// the angle picks which slice shows. <see cref="Slice"/> negates the heading it is given, because
/// that is what the gunsight does before the call. Both are
/// docs/retail/formats/cockpit-gunsight-hud.md's heading-tape section, which also owns why the sign is there.</para>
///
/// <para>The rect is the <c>.GAU</c>'s offset 1104, <see cref="HHeadingTape"/> — the one
/// <see cref="RotationIndicator"/> and <see cref="WaypointIndicator"/> hang off too. Units are device
/// pixels, like <see cref="RotationIndicator"/>. The wind-up that drives this at power-up is
/// <see cref="HeadingTapeSweep"/>.</para>
/// </summary>
public readonly struct HeadingTape {
	/// <summary>The sprite bank the strip is cut from, loaded by the tape's own constructor.</summary>
	public const string SpriteBank = "HUDHTICK";

	private HeadingTape(int left, int top, int width, int frames) {
		Left = left;
		Top = top;
		Width = width;
		Frames = frames;
	}

	/// <summary>Left edge of the tape's rect, device pixels — the window's own left edge.</summary>
	public int Left { get; }

	/// <summary>Its top edge, the row both frames are blitted at.</summary>
	public int Top { get; }

	/// <summary>
	/// The rect's width, device pixels. It is both the clip window and the step between the two
	/// blitted frames, which is what makes a frame's worth of strip one window's worth of turn.
	/// </summary>
	public int Width { get; }

	/// <summary>How many frames the bank holds — the strip's length in windows, and so a full turn.</summary>
	public int Frames { get; }

	/// <summary>
	/// This herc's tape, or null when its <c>.GAU</c> has no gunsight rect or the bank did not load —
	/// either way there is no strip to slide.
	/// </summary>
	public static HeadingTape? From(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.HeadingTape is not { } widget || art.Sprites is not { } sprites) {
			return null;
		}

		int frames = sprites.FrameCount(SpriteBank);
		if (frames <= 0 || widget.Size.Width <= 0) {
			return null;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;
		return new HeadingTape(widget.Origin.X * scale, widget.Origin.Y * scale,
			widget.Size.Width * scale, frames);
	}

	/// <summary>
	/// Which slice of the strip a heading shows: the frame under the window's left edge, the one after
	/// it, and the device x the first is blitted at — the second goes <see cref="Width"/> further right,
	/// both clipped to <see cref="Left"/>..<c>Left + Width</c>. The heading is negated and then taken
	/// unsigned, so the binary angle's whole range maps onto the strip once and the frame after the last
	/// wraps to the first.
	/// </summary>
	public (int Frame, int Next, int ScrollX) Slice(short heading) {
		int total = SimMath.Q16Multiply((ushort)-heading, Frames * Width);
		int frame = total / Width;
		int offset = total % Width;
		int next = frame + 1 == Frames ? 0 : frame + 1;

		return (frame, next, Left - offset);
	}
}
