using Herculan.Engine.Numerics;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Sim;

/// <summary>
/// A flat shape lying on the ground — DBSIM's <c>FlatObj</c> class (vtable <c>FlatObj_Vtable</c>,
/// <c>0049a262</c>), out of the 150-entry pool at <c>g_FlatObjPool</c>
/// (docs/retail/simulation/ground-shapes.md).
///
/// <para>Three things lay one, each choosing a root of the theater's flat set
/// (<see cref="World.TheaterDescriptor.FlatSetName"/>): a HERC, whose shadows these are — one per
/// entry of its chassis' part list for as long as it lives, moved under that part every tick
/// (<see cref="MechObject.Shadows"/>); an impact effect whose <c>EXPLOS.DAT</c> row asks for one,
/// which no retail row does (<see cref="ImpactEffect"/>); and a drop pod that carried a group, at its
/// landing site for the rest of the mission (<see cref="MeteorObject"/>).</para>
///
/// <para>It has no tick. What it does it does as it is drawn: <c>FlatObj_Draw</c> (<c>0040991c</c>)
/// sits it on the ground first — <see cref="ConformToTerrain"/> — and that is where its height, pitch
/// and roll come from. Whoever placed it set only X, Y and the heading. Like the other effect classes
/// it is not in the object list, so nothing can target or shoot it.</para>
/// </summary>
public sealed class GroundShape {
	/// <param name="shapeIndex">Which root of the flat set it draws.</param>
	/// <param name="shapeRadius">That root's own bounding radius — see <see cref="ShapeRadius"/>.</param>
	/// <param name="position">Where it is placed.</param>
	internal GroundShape(int shapeIndex, int shapeRadius, Vec3i position) {
		ShapeIndex = shapeIndex;
		ShapeRadius = shapeRadius;
		Position = position;
	}

	/// <summary>Which root of the flat set it draws, fixed at construction.</summary>
	public int ShapeIndex { get; }

	/// <summary>
	/// The root's own bounding radius, <c>shape+8</c> through <c>SimObject_GetShapeRadius</c>
	/// (<c>0046b80c</c>, the class's vtable <c>+0x10</c>). It is how far out the draw's terrain conform
	/// probes; the draw pass files the shape by it too.
	/// </summary>
	public int ShapeRadius { get; }

	/// <summary>
	/// Where it sits, <c>obj+0x26</c>. X and Y are its placer's; Z is whatever the last
	/// <see cref="ConformToTerrain"/> made it, or the placer's until one has run.
	/// </summary>
	public Vec3i Position { get; internal set; }

	/// <summary>The euler Z at <c>obj+0x10</c>. A HERC's shapes take the machine's own every tick; the rest keep 0.</summary>
	public int Heading { get; internal set; }

	/// <summary>The euler X at <c>obj+0x0c</c>, written only by <see cref="ConformToTerrain"/>.</summary>
	public short Pitch { get; private set; }

	/// <summary>The euler Y at <c>obj+0x0e</c>, written only by <see cref="ConformToTerrain"/>.</summary>
	public short Roll { get; private set; }

	/// <summary>
	/// The cell of the shape's sequence 0 it shows — the shape instance's own frame counter. Only an
	/// impact effect's shape steps it, in step with its own flipbook; everything else stays on cell 0.
	/// </summary>
	public int Frame { get; internal set; }

	/// <summary>Its frame, for a renderer: the euler triple with the position in the translation.</summary>
	public Transform3 WorldFrame {
		get {
			var frame = Transform3.FromEuler(Pitch, Roll, (short)Heading);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>
	/// The first thing <c>FlatObj_Draw</c> (<c>0040991c</c>) does: <c>SimObject_ConformToTerrain</c>
	/// (<c>004029d8</c>) at <see cref="ShapeRadius"/> — see <see cref="TerrainConform"/>. The result
	/// stays on the shape, and the next conform starts its probes from it.
	///
	/// <para>A draw-time call, and so the host's: the original runs it only on shapes the frame
	/// submits (<see cref="DrawRange"/>), which nothing in the simulation can observe.</para>
	/// </summary>
	public void ConformToTerrain(HeightGrid terrain) {
		var (pitch, roll, z) = TerrainConform.Apply(terrain, WorldFrame, ShapeRadius);
		Pitch = pitch;
		Roll = roll;
		Position = new Vec3i(Position.X, Position.Y, z);
	}

	/// <summary>
	/// <c>g_FlatObjPool</c>'s size — <c>Pool_Init(pool, 150, 0x41)</c> in <c>FlatObj_LoadResources</c>
	/// (<c>004097a8</c>). A spawn into a full pool gets nothing; see <see cref="SimWorld.SpawnGroundShape"/>.
	/// </summary>
	public const int PoolSize = 150;

	/// <summary>
	/// How near the camera a shape has to be to be drawn — <c>Scene_SubmitFrameObjects</c>
	/// (<c>0042841c</c>) submits it only under 30000 by <c>Math_DistanceBetweenPoints</c>, the
	/// <see cref="Vec3i.ApproxDistanceTo"/> measure.
	/// </summary>
	public const int DrawRange = 30000;

	/// <summary>
	/// The Z a HERC's shapes are built at, <c>Mech_Constructor</c>'s literal (<c>00415efb</c>), so a
	/// shape its machine has not yet placed is out of <see cref="DrawRange"/> of anything.
	/// </summary>
	public const int HiddenDepth = -100000;

	/// <summary>The root an impact effect lays — <c>Explosion_Construct</c>'s <c>g_FlatShapes[1]</c>.</summary>
	public const int ImpactShapeIndex = 1;

	/// <summary>The root a drop pod leaves at its landing site — <c>Meteor_Tick</c>'s <c>g_FlatShapes[3]</c>.</summary>
	public const int DropPodShapeIndex = 3;
}
