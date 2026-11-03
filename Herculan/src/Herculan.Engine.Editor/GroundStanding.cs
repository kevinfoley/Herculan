using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Editor;

/// <summary>Where the editor stands an object on the ground: for a point of its own, and once the simulator's first move has run.</summary>
internal static class GroundStanding {
	/// <summary>
	/// Where the simulator stands <paramref name="sceneObject"/> for a point of its own at (<paramref name="x"/>,
	/// <paramref name="y"/>, <paramref name="z"/>): <paramref name="z"/> is read for a flyer alone
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5).
	/// </summary>
	public static Vec3i Standing(HeightGrid terrain, SceneObject sceneObject, int x, int y, int z) => sceneObject.Object switch {
		FlyerObject => new Vec3i(x, y, z == 0 ? FlyerObject.DefaultHoverHeight : z),
		MechObject mech => new Vec3i(x, y, MissionScene.MechSpawnHeight(terrain, mech, new Vec3i(x, y, 0))),
		_ => new Vec3i(x, y, terrain.HeightAtWorld(x, y))
	};

	/// <summary>
	/// Where the editor draws <paramref name="simObject"/>, spawned at <paramref name="spawn"/>: where it stands once the
	/// simulator's first move has run. A walking mech spawned by its group has the height of the ground under the group's
	/// point, one height for every member, and its move (<c>Mech_MovementTick</c>, <c>0041a360</c>) sets its Z to the ground
	/// under the machine itself plus its ride height every tick, moving or not; drawn at the spawn height, a member away
	/// from the point on a slope would float or sit underground. A flyer spawns at an absolute height, 5,000 units for a
	/// point at Z 0, which is below the ground over most terrain; its move (<c>Flyer_MovementTick</c>, <c>004218c4</c>)
	/// lifts it to <see cref="FlyerObject.GroundClearance"/> above the ground, so it is drawn no lower than that, unless
	/// <paramref name="asleep"/>: a flyer whose group's first order is sleep never runs a move
	/// (docs/retail/simulation/ai-flyers.md#the-seven-states) and stays at its spawn height. A RAZOR's move does neither,
	/// so it keeps the spawn height, as does anything else. The editor's choice of what to draw, not a retail placement:
	/// nothing writes the drawn height into the file, whose points hold no height for a mech and the spawn height for a
	/// flyer (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5).
	/// </summary>
	public static Vec3i Settled(HeightGrid terrain, SimObject simObject, Vec3i spawn, bool asleep = false) => simObject switch {
		MechObject { Type.IsFlyer: false } mech => new Vec3i(spawn.X, spawn.Y, terrain.HeightAtWorld(spawn.X, spawn.Y) + mech.Type.RideHeight),
		FlyerObject when !asleep => new Vec3i(spawn.X, spawn.Y, Flown(terrain, spawn)),
		_ => spawn
	};

	/// <summary>
	/// The height a flyer spawned at <paramref name="spawn"/> flies at once its first move has run: <paramref name="spawn"/>'s
	/// own, or <see cref="FlyerObject.GroundClearance"/> above the ground under it when that is higher (<see cref="Settled"/>).
	/// </summary>
	public static int Flown(HeightGrid terrain, Vec3i spawn) =>
		Math.Max(spawn.Z, terrain.HeightAtWorld(spawn.X, spawn.Y) + FlyerObject.GroundClearance);
}
