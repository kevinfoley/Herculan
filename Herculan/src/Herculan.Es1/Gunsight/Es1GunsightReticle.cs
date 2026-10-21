using Herculan.Engine.Numerics;
using Herculan.Es1.View;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// A gunsight child on <c>GunsightChild_CtorWithFields</c> (<c>ES1DBSIM 00033ed8</c>): the rect holder with the dwords 3,
/// 6, <c>0xa0</c> and <c>0x3a</c> at <c>+0x14</c>–<c>+0x20</c>, the reticle's arms and centre, which the target box reads
/// its corners' length from and the click surface keeps the cursor in (docs/es1/simulation/cockpit-gunsight-hud.md#the-reticle).
/// </summary>
public abstract class Es1GunsightFieldChild : Es1GunsightChild {
	protected Es1GunsightFieldChild(Es1Cockpit cockpit, Es1Rect rect) : base(cockpit, rect) {
	}

	/// <summary><c>+0x14</c>, the inner end of the reticle's arms.</summary>
	public int Inner { get; set; } = 3;

	/// <summary><c>+0x18</c>, their outer end, and the target box's corners' length.</summary>
	public int Outer { get; set; } = 6;

	/// <summary><c>+0x1c</c>, the reticle's centre's <i>x</i>, or the cursor's.</summary>
	public int CenterX { get; set; } = 0xa0;

	/// <summary><c>+0x20</c>, its <i>y</i>.</summary>
	public int CenterY { get; set; } = 0x3a;
}

/// <summary>
/// The reticle, <c>Gunsight_Reticle_Ctor</c> (<c>00033f1c</c>): a <c>0x24</c>-byte child on
/// <c>HUDCrosshairGunsightVtable</c> (<c>00093776</c>), a cross of four lines with a gap at its middle
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-reticle).
/// </summary>
public sealed class Es1GunsightReticle : Es1GunsightFieldChild {
	/// <summary>The pen's colour.</summary>
	public const byte Color = 0x38;

	public Es1GunsightReticle(Es1Cockpit cockpit, Es1Rect rect) : base(cockpit, rect) {
	}

	/// <summary>
	/// <c>Gunsight_ReticlePaint</c> (<c>00033f64</c>), in the canvas push with the pen {0, <see cref="Color"/>}: with
	/// (<i>cx</i>, <i>cy</i>) the centre, <i>a</i> <see cref="Es1GunsightFieldChild.Inner"/> and <i>b</i>
	/// <see cref="Es1GunsightFieldChild.Outer"/>, the lines (<i>cx</i> + <i>a</i>, <i>cy</i>)–(<i>cx</i> + <i>b</i>,
	/// <i>cy</i>), (<i>cx</i> − <i>b</i>, <i>cy</i>)–(<i>cx</i> − <i>a</i>, <i>cy</i>), (<i>cx</i>, <i>cy</i> −
	/// <i>b</i>)–(<i>cx</i>, <i>cy</i> − <i>a</i>) and (<i>cx</i>, <i>cy</i> + <i>a</i>)–(<i>cx</i>, <i>cy</i> + <i>b</i>).
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.DrawLine(CenterX + Inner, CenterY, CenterX + Outer, CenterY, Color);
		canvas.DrawLine(CenterX - Outer, CenterY, CenterX - Inner, CenterY, Color);
		canvas.DrawLine(CenterX, CenterY - Outer, CenterX, CenterY - Inner, Color);
		canvas.DrawLine(CenterX, CenterY + Inner, CenterX, CenterY + Outer, Color);
	}
}

/// <summary>
/// The target box, <c>Gunsight_TargetIndicator_Ctor</c> (<c>0003403c</c>): a <c>0x38</c>-byte child on
/// <c>HUDLockingGunsightVtable</c> (<c>0009378a</c>) with the box's rect at <c>+0x28</c>, which the builder points at the
/// gunsight's state block (docs/es1/simulation/cockpit-gunsight-hud.md#the-target-box).
/// </summary>
public class Es1GunsightTargetIndicator : Es1GunsightFieldChild {
	/// <summary>The box's height is held between these, in rows.</summary>
	public const int MinHeight = 25, MaxHeight = 75;

	/// <summary>The colours by the target's side: 1 for side 0, <see cref="OtherSideColor"/> for any other.</summary>
	public const byte SideZeroColor = 1, OtherSideColor = 0x35;

	/// <summary>The least view depth either point may have, the main camera's near plane.</summary>
	public const int NearDepth = Es1Camera.NearPlane;

	public Es1GunsightTargetIndicator(Es1Cockpit cockpit, Es1Rect rect) : base(cockpit, rect) {
	}

	/// <summary><c>+0x24</c>, the gunsight's state block.</summary>
	public Es1GunsightState? State { get; set; }

	/// <summary><c>+0x28</c>–<c>+0x34</c>, the box as the paint last placed it.</summary>
	public Es1Rect Box { get; protected set; }

	/// <summary>
	/// <c>Gunsight_TargetIndicatorPaint</c> (<c>000340c0</c>) (docs/es1/simulation/cockpit-gunsight-hud.md#the-target-box):
	/// while the block has a selection (offset 6) and its byte <c>0x24</c>, with <i>r</i> the radius and (<i>x</i>,
	/// <i>y</i>, <i>z</i>) the position, the points (<i>x</i>, <i>y</i>, <i>z</i> − <i>r</i>/2) and (<i>x</i>, <i>y</i>,
	/// <i>z</i> − <i>r</i>/2 + 2<i>r</i>) through the installed view (<see cref="Es1ViewProjection"/>), nothing when either's
	/// depth is below <see cref="NearDepth"/>; the rows moved apart or together to hold their distance <i>h</i> between
	/// <see cref="MinHeight"/> and <see cref="MaxHeight"/>; the box the upper point's <i>x</i> ± <i>h</i>/2 across and its
	/// row to the lower point's down; and the corners in the side's colour, drawn through the scene's render context
	/// (<see cref="Es1SceneCanvas"/>): an L of two lines at each without lock (offset <c>0x25</c> clear), a filled triangle,
	/// the inner half of the corner's square, at each with it.
	///
	/// <para>The identity model transform retail installs around the projection, saving and putting back the one installed,
	/// and the pen and brush it saves and restores, are this port's arguments rather than state. With no view installed yet,
	/// before the first 3D pass, or a target with no group, whose side retail reads through, it draws nothing: engine
	/// guards.</para>
	/// </summary>
	public override void Paint() {
		if (State is not { Selection: { } target, SelectionArmed: true } block || Cockpit.Projection is not { } view
				|| target.Group is not { } group) {
			return;
		}

		int r = block.SelectionRadius;
		var p = block.SelectionPosition;
		int low = p.Z - (r >> 1);
		var first = view.ModelToView(new Vec3i(p.X, p.Y, low));
		var second = view.ModelToView(new Vec3i(p.X, p.Y, low + r * 2));
		if (first.Y < NearDepth || second.Y < NearDepth) {
			return;
		}

		var lower = view.ProjectToScreen(Es1ViewProjection.PerspectiveDivide(first));
		var upper = view.ProjectToScreen(Es1ViewProjection.PerspectiveDivide(second));
		int lowerRow = lower.Y, upperRow = upper.Y;
		int h = Math.Abs(lowerRow - upperRow);
		if (h < MinHeight) {
			int d = (short)((MinHeight - h) >> 1);
			upperRow -= d;
			lowerRow += d;
			h = MinHeight;
		} else if (h > MaxHeight) {
			int d = (short)((h - MaxHeight) >> 1);
			upperRow += d;
			lowerRow -= d;
			h = MaxHeight;
		}

		Box = new Es1Rect(upper.X - (h >> 1), upperRow, upper.X + (h >> 1), lowerRow);
		var (x0, y0, x1, y1) = (Box.X0, Box.Y0, Box.X1, Box.Y1);
		int across = CornerLength(x1 - x0), down = CornerLength(y1 - y0);
		byte color = group.Side == 0 ? SideZeroColor : OtherSideColor;
		var scene = Cockpit.SceneCanvas();
		if (!block.LockLamp) {
			scene.DrawLine(x0, y0, x0, y0 + down, color);
			scene.DrawLine(x0, y0, x0 + across, y0, color);
			scene.DrawLine(x1 - across, y0, x1, y0, color);
			scene.DrawLine(x1, y0, x1, y0 + down, color);
			scene.DrawLine(x1, y1 - down, x1, y1, color);
			scene.DrawLine(x1 - across, y1, x1, y1, color);
			scene.DrawLine(x0, y1 - down, x0, y1, color);
			scene.DrawLine(x0, y1, x0 + across, y1, color);
		} else {
			scene.FillPolygon([(x0, y0 + down), (x0 + across, y0), (x0 + across, y0 + down)], color);
			scene.FillPolygon([(x1 - across, y0 + down), (x1 - across, y0), (x1, y0 + down)], color);
			scene.FillPolygon([(x1 - across, y1), (x1 - across, y1 - down), (x1, y1 - down)], color);
			scene.FillPolygon([(x0, y1 - down), (x0 + across, y1 - down), (x0 + across, y1)], color);
		}

		// A side's corner length: Outer (+0x18) when the side is longer than that, half the side otherwise.
		int CornerLength(int side) => side <= Outer ? side >> 1 : Outer;
	}
}

/// <summary>
/// The unused child, <c>Gunsight_UnusedChild_Ctor</c> (<c>00034710</c>): the target box's object on
/// <c>HUDPipperVtable</c> (<c>00093802</c>), whose paint, <c>Gunsight_UnusedChild_Paint</c> (<c>00034755</c>), is three
/// <c>NOP</c>s and a return (docs/es1/simulation/cockpit-gunsight-hud.md#what-the-box-is-drawn-through).
/// </summary>
public sealed class Es1GunsightUnusedChild : Es1GunsightTargetIndicator {
	public Es1GunsightUnusedChild(Es1Cockpit cockpit, Es1Rect rect) : base(cockpit, rect) {
	}

	/// <summary><c>Gunsight_UnusedChild_Paint</c>: nothing.</summary>
	public override void Paint() {
	}
}
