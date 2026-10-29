using System.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The disc painted on the ground under each unit the viewport draws, in its team's colour at half strength: red for a
/// Cybrid, blue for a human. A unit is a HERC, a flyer or a ground vehicle; a structure gets none. The disc is as wide as
/// the unit's drawn model (<see cref="SimObject.ShapeRadius"/>) and lies on the terrain under the unit's position, a
/// flyer's too (<see cref="GroundDiscs"/>).
/// </summary>
internal static class TeamDiscs {
	private static readonly Vector4 CybridColor = new(1f, 0.1f, 0.1f, 0.5f);
	private static readonly Vector4 HumanColor = new(0.15f, 0.35f, 1f, 0.5f);

	/// <summary>Fills <paramref name="discs"/> with one disc under each unit of <paramref name="shown"/>.</summary>
	public static void Fill(GroundDiscs discs, IEnumerable<SceneObject> shown) {
		discs.Items.Clear();
		foreach (var sceneObject in shown) {
			var unit = sceneObject.Object;
			if (unit is not (MechObject or FlyerObject or BaseObject { Class: BaseObject.StructureClass.GroundVehicle })
					|| unit.ShapeRadius <= 0) {
				continue;
			}

			var at = WorldScale.ToRender(unit.Position);
			discs.Items.Add(new GroundDisc(new Vector2(at.X, at.Z), WorldScale.DistanceToRender(unit.ShapeRadius),
				unit.Side == MissionSide.Cybrid ? CybridColor : HumanColor));
		}
	}
}
