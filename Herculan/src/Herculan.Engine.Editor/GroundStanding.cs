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
	/// from the point on a slope would float or sit underground. A RAZOR's move does not do this, so it keeps the spawn
	/// height, as does anything that is not a mech. The editor's choice of what to draw, not a retail placement: nothing
	/// writes a mech's drawn height into the file, whose points hold no height for a mech
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5).
	/// </summary>
	public static Vec3i Settled(HeightGrid terrain, SimObject simObject, Vec3i spawn) => simObject is MechObject { Type.IsFlyer: false } mech
		? new Vec3i(spawn.X, spawn.Y, terrain.HeightAtWorld(spawn.X, spawn.Y) + mech.Type.RideHeight)
		: spawn;
}
