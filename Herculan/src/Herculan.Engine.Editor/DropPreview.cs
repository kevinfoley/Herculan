using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The structure a Toolbox drag would add, drawn on the ground under the pointer while the drag is over the view: built
/// as the add builds it (<see cref="StructurePlacer.Ghost"/>) and drawn through <see cref="MissionDrawables"/>, but not
/// listed in the index or the picker, so nothing selects or levels it. It stands on the ground as it is, before the add
/// levels the ground under it.
/// </summary>
internal sealed class DropPreview {
	private readonly EditorMission _mission;
	private readonly MissionDrawables _drawables;
	private (int TypeIndex, MissionSide Side)? _for;
	private SceneObject? _ghost;
	private bool _shown;

	public DropPreview(EditorMission mission, MissionDrawables drawables) {
		_mission = mission;
		_drawables = drawables;
	}

	/// <summary>
	/// Draws the structure <paramref name="drag"/> names on <paramref name="ground"/>, or nothing when either is null:
	/// no drag, or the pointer over a panel or the sky.
	/// </summary>
	public void Update((int TypeIndex, MissionSide Side)? drag, Vec3i? ground) {
		if (drag != _for) {
			Hide();
			_for = drag;
			_ghost = drag is { } wanted ? _mission.Placer.Ghost(wanted.TypeIndex, wanted.Side) : null;
		}

		if (_ghost == null) {
			return;
		}

		if (ground is not { } at) {
			Hide();
			return;
		}

		var terrain = _mission.Scene.World.Terrain;
		_ghost.Object.Position = new Vec3i(at.X, at.Y, terrain.HeightAtWorld(at.X, at.Y));
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
