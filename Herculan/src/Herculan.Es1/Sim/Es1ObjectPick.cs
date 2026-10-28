namespace Herculan.Es1.Sim;

/// <summary>
/// The pixel pick, <c>terrain.cpp</c>'s globals <c>ObjPick_Pending</c> (<c>ES1DBSIM 000afa45</c>), <c>ObjPick_Point</c>
/// (<c>000afa48</c>) and <c>ObjPick_Object</c> (<c>000afa50</c>), with <c>ObjPick_Arm</c> (<c>00020d28</c>),
/// <c>ObjPick_IsIdle</c> (<c>00020d50</c>) and <c>ObjPick_GetObject</c> (<c>00020d48</c>)
/// (docs/es1/simulation/terrain.md#the-pixel-pick-and-the-occlusion-probe). The gunsight's click arms it, the next 3D
/// pass of the frame's objects answers it (<see cref="Resolve"/>), and the gunsight's update reads the answer.
///
/// <para>Retail answers the pick by writing 1 at the point before the object list's draw and keeping each object whose
/// draw changes that pixel, so <c>ObjPick_Object</c> ends as the last object painted over it, and puts the pixel back
/// after. This engine draws the zone's shapes and the HERCs with a depth test, so the pass that answers it reads back
/// which object the GPU drew at the point instead, the one shown there: this engine's departure, the user's choice
/// (docs/herculan/plan-es1-first-mission.md, decision 16). Nothing is written into the page, so
/// <c>ObjPick_SavedPixel</c> (<c>000afa90</c>) has no counterpart.</para>
/// </summary>
public sealed class Es1ObjectPick {
	/// <summary><c>ObjPick_Pending</c>: set by <see cref="Arm"/>, cleared by the pass that answers it.</summary>
	public bool Pending { get; private set; }

	/// <summary><c>ObjPick_Point</c>: the point in the 320 × 200 screen's pixels, of the page the 3D pass draws on.</summary>
	public (int X, int Y) Point { get; private set; }

	/// <summary>
	/// <c>ObjPick_Object</c>: the object the last answering pass found at the point, a HERC (<see cref="Es1Mech"/>), a
	/// flyer (<see cref="Es1Flyer"/>) or a zone shape (its <see cref="Es1ShapeObject"/>), or null for none; 0 in the image.
	/// </summary>
	public object? Object { get; private set; }

	/// <summary><c>ObjPick_Arm</c>(point): the point kept and the pick pending.</summary>
	public void Arm(int x, int y) {
		Point = (x, y);
		Pending = true;
	}

	/// <summary><c>ObjPick_IsIdle</c>: true while the pick is not pending.</summary>
	public bool IsIdle => !Pending;

	/// <summary><c>ObjPick_GetObject</c>: <see cref="Object"/>.</summary>
	public object? GetObject() => Object;

	/// <summary>
	/// <c>ObjList_DrawFrameObjects</c>' part (<c>00021008</c>), run by the 3D pass after its objects are drawn: with the
	/// pick pending, <see cref="Object"/> becomes what <paramref name="objectAt"/> answers for the point, null where the
	/// pass drew no object there, and the pick goes idle. Not pending, nothing changes.
	/// </summary>
	public void Resolve(Func<int, int, object?> objectAt) {
		if (!Pending) {
			return;
		}

		Object = objectAt(Point.X, Point.Y);
		Pending = false;
	}
}
