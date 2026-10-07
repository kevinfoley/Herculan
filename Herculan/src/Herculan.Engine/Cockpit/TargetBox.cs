using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The front-window HUD's <b>target indicator</b>: the green box drawn over whatever is selected,
/// and the arrow that replaces it when the selection is off to one side.
///
/// <para>Reverse-engineered from child 5 of the roving-gunsight complex — constructed by
/// <c>Gau_RovingGunsightWidget</c> (<c>0043c7d8</c>) through <c>Gunsight_TargetIndicator_Ctor</c> (<c>0043b928</c>) and painted by
/// <c>Gunsight_TargetIndicatorPaint</c> (<c>0043b950</c>). The complex feeds it a 38-byte state block once a frame
/// (<c>Gunsight_SetValues</c>, <c>0043d98c</c>) that <c>Gunsight_UpdateAndPaint</c> (<c>0043d6dc</c>) has just filled from
/// <c>CockpitView+0x26c</c>..<c>+0x27e</c> — the selected object, its world aim point, and the
/// component the Targeting Pod has picked out of it.</para>
///
/// <para><b>Everything is measured from the reticle point</b>, the <c>.GAU</c>'s offset-1136 point
/// (<see cref="HercWorks.Core.Data.File.Gau.HReticle"/>), not from the middle of the view — and
/// that point is also the projection centre the herc's <c>.VUE</c> states, so a target dead ahead
/// projects exactly onto it.</para>
///
/// <para><b>Two states, never both.</b> Inside <see cref="OnTargetTolerance"/> of the reticle the box
/// is not drawn at all: the crosshair sprite itself changes frame instead (child 4,
/// <c>Gunsight_ReticlePaint</c> (<c>0043b7e0</c>)), which is what "on target" looks like. Outside it, the box is drawn. The
/// arrow is a separate test on the same frame — it appears whenever the target does not project
/// inside <see cref="HercWorks.Core.Data.File.Gau.HGunsightArea"/>, which every retail file places
/// well inside the canopy's window opening so the arrow never lands on the cockpit frame.</para>
/// </summary>
public static class TargetBox {
	/// <summary>The bank every piece of the indicator comes from.</summary>
	public const string SpriteBank = "HUD";

	/// <summary>
	/// First of the four frames drawn for an unlocked target: <c>+0</c> the 23x23 pip that sits on
	/// the target itself, <c>+1</c> the 12x12 corner bracket drawn four times (flipped into each
	/// corner), <c>+2</c> the 7x1 tick outside the box's left and right edges, and <c>+3</c> the 1x7
	/// tick outside its top and bottom. The paint reaches them as
	/// <c>bank[base]</c>..<c>bank[base + 3]</c>.
	/// </summary>
	public const int UnlockedFirstFrame = 3;

	/// <summary>
	/// The same four in the colour a locked target wears, chosen when <c>mech+0x9b</c>
	/// (<see cref="Herculan.Engine.Sim.MechObject.LockAcquired"/>) is set. Its pip is 21x21 and its
	/// left/right tick 8x1; the corners and vertical tick are the same sizes as the unlocked set.
	/// </summary>
	public const int LockedFirstFrame = 7;

	/// <summary>Offsets of the four pieces from <see cref="UnlockedFirstFrame"/> / <see cref="LockedFirstFrame"/>.</summary>
	public const int PipFrame = 0;

	/// <inheritdoc cref="PipFrame"/>
	public const int CornerFrame = 1;

	/// <inheritdoc cref="PipFrame"/>
	public const int HorizontalTickFrame = 2;

	/// <inheritdoc cref="PipFrame"/>
	public const int VerticalTickFrame = 3;

	/// <summary>
	/// How close to the reticle a target has to project before the box gives way to the crosshair's
	/// own on-target frame, in device pixels on each axis independently — the paint's own
	/// <c>5 &lt;&lt; VideoMode_X/YCoordShift</c>.
	/// </summary>
	public const int OnTargetTolerance = 5 * (int)CockpitArt.GauToPixelScale;

	/// <summary>
	/// Smallest and largest the box is allowed to get, in device pixels. <b>Not</b> coordinate-shifted
	/// — the paint compares against literal 25 and 75 whatever the video mode, which is the original's
	/// own arithmetic and is transcribed rather than corrected. The minimum is exactly two 12-pixel
	/// corner brackets plus one pixel, so at its smallest the box closes into an unbroken frame.
	/// </summary>
	public const int MinimumSize = 25;

	/// <inheritdoc cref="MinimumSize"/>
	public const int MaximumSize = 75;

	/// <summary>
	/// The arrow's length and full width in device pixels — the paint builds its triangle as
	/// <c>(0,0)</c>, <c>(±(6 &lt;&lt; shift) / 2, -(10 &lt;&lt; shift))</c> and rotates it so the apex
	/// points at the target. Both use the <i>vertical</i> coordinate shift on both axes, which is the
	/// original's own quirk and has no effect in any retail video mode.
	/// </summary>
	public const int ArrowLength = 10 * (int)CockpitArt.GauToPixelScale;

	/// <inheritdoc cref="ArrowLength"/>
	public const int ArrowHalfWidth = 6 * (int)CockpitArt.GauToPixelScale / 2;

	/// <summary>
	/// <c>COLORS.DAT</c> id the arrow is filled with — 12, which resolves to palette 14, green. It is
	/// a flat-filled polygon rather than a sprite, the only piece of the indicator that is.
	/// </summary>
	public const int ArrowColorId = 12;

	/// <summary>And the id it wears once the target is locked — 9, palette 10, red.</summary>
	public const int ArrowLockedColorId = 9;

	/// <summary>
	/// Where the paint puts a target that is <i>behind</i> the eye, so the arrow still has a direction
	/// to point in: it throws away the projection and re-projects the synthetic view-space point
	/// <c>(±10000, 1024, 0)</c>, whose sign is that of the real point's own view-space x. With the
	/// focal length that lands 5000 device pixels to one side of the reticle, on its own row — so the
	/// arrow points straight left or straight right and nowhere else.
	/// </summary>
	public const int BehindOffsetX = 5000;

	/// <summary>The first of the four frames for a target in the given lock state.</summary>
	public static int FirstFrameFor(bool locked) => locked ? LockedFirstFrame : UnlockedFirstFrame;

	/// <summary>The colour id the arrow is filled with in the given lock state.</summary>
	public static int ArrowColorFor(bool locked) => locked ? ArrowLockedColorId : ArrowColorId;

	/// <summary>
	/// The box's device-pixel rect about a target that projects to <paramref name="screenY"/> at
	/// <paramref name="screenX"/>.
	///
	/// <para>Its half-height is the target's own shape radius, halved, projected: <c>(radius &gt;&gt; 1)
	/// * focal / distance</c> — where the focal length is <see cref="Render.Camera.FocalLengthPixels"/>,
	/// the same power of two the rasterizer projects everything else with. The height is then clamped
	/// into <see cref="MinimumSize"/>..<see cref="MaximumSize"/> by moving both edges toward or away
	/// from each other, and the width is taken from the clamped height, so the box is square give or
	/// take the odd pixel integer halving leaves behind.</para>
	/// </summary>
	/// <param name="shapeRadius">The target's <see cref="Herculan.Engine.Sim.SimObject.ShapeRadius"/>, world units.</param>
	/// <param name="distance">Eye to aim point, world units. Zero or less yields the minimum box.</param>
	public static (int X0, int Y0, int X1, int Y1) Bounds(
			float screenX, float screenY, int shapeRadius, int distance) {
		int centerX = (int)screenX;
		int centerY = (int)screenY;
		int halfHeight = distance > 0
			? (int)((shapeRadius >> 1) * Render.Camera.FocalLengthPixels / distance)
			: 0;

		int top = centerY - halfHeight;
		int bottom = centerY + halfHeight;
		int height = bottom - top;

		if (height < MinimumSize) {
			int pad = (MinimumSize - height) / 2;
			top -= pad;
			bottom += pad;
			height = MinimumSize;
		} else if (height > MaximumSize) {
			int trim = (height - MaximumSize) / 2;
			top += trim;
			bottom -= trim;
			height = MaximumSize;
		}

		return (centerX - height / 2, top, centerX + height / 2, bottom);
	}
}
