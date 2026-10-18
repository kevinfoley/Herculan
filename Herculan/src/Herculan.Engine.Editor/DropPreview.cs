using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// The herc, flyer, ground vehicle or structure a Toolbox drag would add, drawn at the ground point under the pointer while
/// the drag is over the view: built as the add builds it (<see cref="HercPlacer.Ghost"/>, <see cref="FlyerPlacer.Ghost"/>,
/// <see cref="VehiclePlacer.Ghost"/>, <see cref="StructurePlacer.Ghost"/>) and drawn through <see cref="MissionDrawables"/>,
/// but not listed in the index or the picker, so nothing selects or levels it. It stands where the add's object will stand
/// on a point at Z 0 (<see cref="PointFollowers.Standing"/>, <see cref="PointFollowers.Settled"/>): a herc at its ride
/// height above the ground as it is, a flyer at 5,000 units, a ground vehicle or a structure on the ground before the add
/// levels it.
/// </summary>
internal sealed class DropPreview {
	private readonly EditorMission _mission;
	private readonly MissionDrawables _drawables;
	private ToolboxDrag? _for;
	private SceneObject? _ghost;
	private bool _shown;

	public DropPreview(EditorMission mission, MissionDrawables drawables) {
		_mission = mission;
		_drawables = drawables;
	}

	/// <summary>
	/// Draws what <paramref name="drag"/> would add on <paramref name="ground"/>, or nothing when either is null: no drag,
	/// or the pointer over a panel or the sky.
	/// </summary>
	public void Update(ToolboxDrag? drag, Vec3i? ground) {
		if (drag != _for) {
			Hide();
			_for = drag;
			_ghost = drag is { } wanted
				? wanted.Kind switch {
					ToolboxKind.Herc => _mission.Hercs.Ghost(wanted.TypeIndex, wanted.Side),
					ToolboxKind.Flyer => _mission.Flyers.Ghost(wanted.TypeIndex, wanted.Side),
					ToolboxKind.Vehicle => _mission.Vehicles.Ghost(wanted.TypeIndex, wanted.Side),
					_ => _mission.Placer.Ghost(wanted.TypeIndex, wanted.Side)
				}
				: null;
		}

		if (_ghost == null) {
			return;
		}

		if (ground is not { } at) {
			Hide();
			return;
		}

		var terrain = _mission.Scene.World.Terrain;
		_ghost.Object.Position = PointFollowers.Settled(terrain, _ghost.Object, PointFollowers.Standing(terrain, _ghost, at.X, at.Y, 0));
		if (_shown) {
			_drawables.Move(_ghost);
		} else {
			_drawables.Add(_ghost);
			_shown = true;
		}
	}

	private void Hide() {
		if (_shown && _ghost != null) {
			_drawables.Remove(_ghost);
		}

		_shown = false;
	}
}
