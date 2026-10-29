using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// The herc, flyer, ground vehicle, structure or base a Toolbox drag would add, drawn at the ground point under the pointer
/// while the drag is over the view: built as the add builds it (<see cref="HercPlacer.Ghost"/>, <see cref="FlyerPlacer.Ghost"/>,
/// <see cref="VehiclePlacer.Ghost"/>, <see cref="StructurePlacer.Ghost"/>) and drawn through <see cref="MissionDrawables"/>,
/// but not listed in the index or the picker, so nothing selects or levels it. It stands where the add's object will stand
/// on a point at Z 0 (<see cref="GroundStanding.Standing"/>, <see cref="GroundStanding.Settled"/>): a herc at its ride
/// height above the ground as it is, a flyer at 5,000 units, a ground vehicle or a structure on the ground before the add
/// levels it. A base is one ghost per member, each where <see cref="StructurePlacer.BasePlaces"/> stands it, so the whole
/// base jumps a ground tile at a time; the preview leaves the ground alone, painting no pad and levelling nothing.
/// </summary>
internal sealed class DropPreview {
	private readonly EditorMission _mission;
	private readonly MissionDrawables _drawables;
	private readonly List<(SceneObject Ghost, int Slot)> _ghosts = [];
	private ToolboxDrag? _for;
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
			_ghosts.Clear();
			if (drag is { } wanted) {
				Build(wanted);
			}
		}

		if (_ghosts.Count == 0) {
			return;
		}

		if (ground is not { } at) {
			Hide();
			return;
		}

		var terrain = _mission.Scene.World.Terrain;
		var places = _for is { Kind: ToolboxKind.Base } dragged
			? _mission.Placer.BasePlaces(BaseTemplates.All[dragged.TypeIndex], at.X, at.Y)
			: null;
		foreach (var (ghost, slot) in _ghosts) {
			var (x, y) = places != null ? (places[slot].Position.X, places[slot].Position.Y) : (at.X, at.Y);
			if (places != null) {
				ghost.Object.Heading = places[slot].Heading;
			}

			ghost.Object.Position = GroundStanding.Settled(terrain, ghost.Object, GroundStanding.Standing(terrain, ghost, x, y, 0));
			if (_shown) {
				_drawables.Move(ghost);
			} else {
				_drawables.Add(ghost);
			}
		}

		_shown = true;
	}

	// The ghosts of what <wanted> adds: one, or for a base one per member slot whose type the install has.
	private void Build(ToolboxDrag wanted) {
		if (wanted.Kind == ToolboxKind.Base) {
			var types = BaseTemplates.All[wanted.TypeIndex].Types;
			for (int slot = 0; slot < types.Count; slot++) {
				if (_mission.Placer.Ghost(types[slot], wanted.Side) is { } member) {
					_ghosts.Add((member, slot));
				}
			}

			return;
		}

		var ghost = wanted.Kind switch {
			ToolboxKind.Herc => _mission.Hercs.Ghost(wanted.TypeIndex, wanted.Side),
			ToolboxKind.Flyer => _mission.Flyers.Ghost(wanted.TypeIndex, wanted.Side),
			ToolboxKind.Vehicle => _mission.Vehicles.Ghost(wanted.TypeIndex, wanted.Side),
			_ => _mission.Placer.Ghost(wanted.TypeIndex, wanted.Side)
		};
		if (ghost != null) {
			_ghosts.Add((ghost, 0));
		}
	}

	private void Hide() {
		if (_shown) {
			foreach (var (ghost, _) in _ghosts) {
				_drawables.Remove(ghost);
			}
		}

		_shown = false;
	}
}
