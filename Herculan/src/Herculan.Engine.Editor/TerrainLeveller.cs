using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Editor;

/// <summary>
/// Keeps the ground levelled under the structures where they now stand. The simulator levels it once, at spawn
/// (<see cref="HeightGrid.FlattenStructureFootprints"/>, docs/retail/simulation/terrain-heightmap.md#structure-footprints--the-flattening-pass),
/// from every structure's footprint and every base pad's layout; after a move, <see cref="Relevel"/> puts the
/// heightmap back as the zone loaded it and runs the same pass over the footprints as they now stand, so the ground a
/// structure leaves returns to the heightmap and the ground it reaches is what mission start would make of it.
/// Running the pass again is the editor's own; retail never re-levels.
///
/// <para>What stands on the ground is settled again where it changed: a structure on the ground under it, as the
/// spawn pass re-settles them, and a walking mech where the editor draws it (<see cref="PointFollowers.Settled"/>).
/// The terrain mesh is rebuilt over the cells that changed (<see cref="Changed"/>) and the overlays laid on it again.
/// A moved pad's paint is not redone.</para>
/// </summary>
internal sealed class TerrainLeveller {
	private readonly HeightGrid _terrain;
	private readonly MissionModels _models;
	private readonly MissionIndex _index;
	private readonly IReadOnlyList<SceneObject> _objects;
	private readonly TerrainMaterialTable _materials;
	private readonly MissionOverlay _overlay;
	private readonly Action<SceneObject, Vec3i, int> _place;

	// The raw heights the terrain mesh was last built from, and where the footprints stood when the ground was last
	// levelled.
	private readonly byte[] _meshed;
	private int[] _levelledFor;

	/// <param name="place">Stands an object at a position and heading, for whatever draws and picks it.</param>
	public TerrainLeveller(MissionScene scene, MissionIndex index, TerrainMaterialTable materials, MissionOverlay overlay,
			Action<SceneObject, Vec3i, int> place) {
		_terrain = scene.World.Terrain;
		_models = scene.Models;
		_index = index;
		_objects = scene.Objects;
		_materials = materials;
		_overlay = overlay;
		_place = place;

		_meshed = new byte[_terrain.Width * _terrain.Height];
		for (int y = 0; y < _terrain.Height; y++) {
			for (int x = 0; x < _terrain.Width; x++) {
				_meshed[_terrain.CellIndex(x, y)] = _terrain.RawHeightAt(x, y);
			}
		}

		_levelledFor = Footprints();
	}

	/// <summary>
	/// Raised with the run of <see cref="MissionModels.TerrainMesh"/>'s vertices a re-levelling rebuilt, for whatever
	/// uploads it.
	/// </summary>
	public event Action<int, int>? Changed;

	/// <summary>Levels the ground again if a structure or a pad has moved since it was last levelled.</summary>
	public void Relevel() {
		int[] footprints = Footprints();
		if (footprints.AsSpan().SequenceEqual(_levelledFor)) {
			return;
		}

		_levelledFor = footprints;
		_terrain.RestoreUnlevelledHeights();
		foreach (var sceneObject in _objects) {
			if (sceneObject.Object is BaseObject structure) {
				_terrain.MarkStructureFootprint(structure.Position.X, structure.Position.Y, structure.ShapeRadius);
			}
		}

		foreach (var pad in _index.BasePads) {
			_terrain.MarkFormationPad(pad.Anchor.X, pad.Anchor.Y, _materials[pad.Layout.MaterialIndex].BlockShift,
				pad.Layout.Dimension, pad.Layout.Map);
		}

		_terrain.FlattenStructureFootprints();

		// The corners that moved. A cell is drawn from its own corner and the three east and north of it, so the cells
		// to build again reach one further west and south.
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		for (int y = 0; y < _terrain.Height; y++) {
			for (int x = 0; x < _terrain.Width; x++) {
				int cell = _terrain.CellIndex(x, y);
				byte height = _terrain.RawHeightAt(x, y);
				if (height == _meshed[cell]) {
					continue;
				}

				_meshed[cell] = height;
				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		if (maxX < 0) {
			return;
		}

		Settle();
		var (first, count) = TerrainMeshBuilder.Rebuild(_models.TerrainMesh, _terrain, _models.TerrainBank,
			_models.TerrainShading, minX - 1, minY - 1, maxX, maxY);
		_overlay.Redrape();
		Changed?.Invoke(first, count);
	}

	// Stands each structure and walking mech on the ground as it now is.
	private void Settle() {
		foreach (var sceneObject in _objects) {
			var position = sceneObject.Object.Position;
			var settled = sceneObject.Object switch {
				BaseObject => new Vec3i(position.X, position.Y, _terrain.HeightAtWorld(position.X, position.Y)),
				MechObject mech => PointFollowers.Settled(_terrain, mech, position),
				_ => position
			};

			if (settled != position) {
				_place(sceneObject, settled, sceneObject.Object.Heading);
			}
		}
	}

	// Where every input to the flattening pass stands: each structure, and each pad's anchor.
	private int[] Footprints() {
		var footprints = new List<int>();
		foreach (var sceneObject in _objects) {
			if (sceneObject.Object is BaseObject structure) {
				footprints.Add(structure.Position.X);
				footprints.Add(structure.Position.Y);
			}
		}

		foreach (var pad in _index.BasePads) {
			footprints.Add(pad.Anchor.X);
			footprints.Add(pad.Anchor.Y);
		}

		return footprints.ToArray();
	}
}
