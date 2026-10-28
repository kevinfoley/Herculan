using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Editor;

/// <summary>
/// Keeps the ground levelled under the structures, and the base pads painted, where they now stand. The simulator
/// paints each pad (<see cref="HeightGrid.PaintFormationPad"/>) and levels the ground
/// (<see cref="HeightGrid.FlattenStructureFootprints"/>, docs/retail/simulation/terrain-heightmap.md#structure-footprints--the-flattening-pass)
/// once, at spawn, from every structure's footprint and every pad's layout; after a move, <see cref="Relevel"/> puts
/// the heightmap and the cell materials back as the zone loaded them and runs the same two passes over the structures
/// and pads as they now stand, so the ground a structure or pad leaves returns to the zone as loaded and the ground it
/// reaches is what mission start would make of it. Running the passes again is the editor's own; retail never does.
///
/// <para>What stands on the ground is settled again where it changed: a structure on the ground under it, as the
/// spawn pass re-settles them, and a walking mech where the editor draws it (<see cref="GroundStanding.Settled"/>).
/// The terrain mesh is rebuilt over the cells that changed (<see cref="Changed"/>) and the overlays laid on it again.</para>
/// </summary>
internal sealed class TerrainLeveller {
	private readonly HeightGrid _terrain;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly TerrainMaterialTable _materials;
	private readonly MissionOverlay _overlay;
	private readonly Action<SceneObject, Vec3i, int> _place;

	// The raw heights and materials the terrain mesh was last built from, and where the footprints stood when the
	// ground was last levelled.
	private readonly byte[] _meshed;
	private readonly byte[] _meshedMaterials;
	private int[] _levelledFor;

	/// <param name="place">Stands an object at a position and heading, for whatever draws and picks it.</param>
	public TerrainLeveller(MissionScene scene, MissionIndex index, TerrainMaterialTable materials, MissionOverlay overlay,
			Action<SceneObject, Vec3i, int> place) {
		_terrain = scene.World.Terrain;
		_scene = scene;
		_index = index;
		_materials = materials;
		_overlay = overlay;
		_place = place;

		_meshed = new byte[_terrain.Width * _terrain.Height];
		_meshedMaterials = new byte[_meshed.Length];
		for (int y = 0; y < _terrain.Height; y++) {
			for (int x = 0; x < _terrain.Width; x++) {
				_meshed[_terrain.CellIndex(x, y)] = _terrain.RawHeightAt(x, y);
				_meshedMaterials[_terrain.CellIndex(x, y)] = (byte)_terrain.MaterialIndexAt(x, y);
			}
		}

		_levelledFor = Footprints();
	}

	/// <summary>
	/// Raised with the run of <see cref="MissionModels.TerrainMesh"/>'s vertices a re-levelling rebuilt, for whatever
	/// uploads it.
	/// </summary>
	public event Action<int, int>? Changed;

	/// <summary>Levels and paints the ground again if a structure or a pad has moved, or a structure changed type, since it was last levelled.</summary>
	public void Relevel() {
		int[] footprints = Footprints();
		if (footprints.AsSpan().SequenceEqual(_levelledFor)) {
			return;
		}

		_levelledFor = footprints;
		_terrain.RestoreUnlevelledHeights();
		_terrain.RestoreUnpaintedMaterials();
		foreach (var sceneObject in _index.Objects) {
			if (sceneObject.Object is BaseObject structure) {
				_terrain.MarkStructureFootprint(structure.Position.X, structure.Position.Y, structure.ShapeRadius);
			}
		}

		// In the mission's order, as the spawn pass paints them, so where two tiles overlap the same one wins.
		foreach (var pad in _index.BasePads) {
			_terrain.PaintFormationPad(pad.Anchor.X, pad.Anchor.Y, pad.Layout.MaterialIndex,
				_materials[pad.Layout.MaterialIndex].BlockShift, pad.Layout.Dimension, pad.Layout.Map);
		}

		_terrain.FlattenStructureFootprints();

		// The cells to build again: those with a corner that moved, and those repainted. A cell is drawn from its own
		// corner and the three east and north of it, so a moved corner reaches the cells one west and south of it too;
		// a cell's texture is its own material's alone.
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		bool heightsChanged = false;
		for (int y = 0; y < _terrain.Height; y++) {
			for (int x = 0; x < _terrain.Width; x++) {
				int cell = _terrain.CellIndex(x, y);
				byte height = _terrain.RawHeightAt(x, y);
				byte material = (byte)_terrain.MaterialIndexAt(x, y);
				bool moved = height != _meshed[cell];
				if (!moved && material == _meshedMaterials[cell]) {
					continue;
				}

				_meshed[cell] = height;
				_meshedMaterials[cell] = material;
				heightsChanged |= moved;
				minX = Math.Min(minX, moved ? x - 1 : x);
				minY = Math.Min(minY, moved ? y - 1 : y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		if (maxX < 0) {
			return;
		}

		if (heightsChanged) {
			Settle();
			_overlay.Redrape();
		}

		// The scene's models as they stand: a change of theater builds the mesh again in its bank and shading.
		var models = _scene.Models;
		var (first, count) = TerrainMeshBuilder.Rebuild(models.TerrainMesh, _terrain, models.TerrainBank,
			models.TerrainShading, minX, minY, maxX, maxY);
		Changed?.Invoke(first, count);
	}

	// Stands each structure and walking mech on the ground as it now is.
	private void Settle() {
		foreach (var sceneObject in _index.Objects) {
			var position = sceneObject.Object.Position;
			var settled = sceneObject.Object switch {
				BaseObject => new Vec3i(position.X, position.Y, _terrain.HeightAtWorld(position.X, position.Y)),
				MechObject mech => GroundStanding.Settled(_terrain, mech, position),
				_ => position
			};

			if (settled != position) {
				_place(sceneObject, settled, sceneObject.Object.Heading);
			}
		}
	}

	// Where every input to the flattening pass stands, and how much ground it levels: each structure with its shape radius,
	// which a change of type changes in place, and each pad's anchor.
	private int[] Footprints() {
		var footprints = new List<int>();
		foreach (var sceneObject in _index.Objects) {
			if (sceneObject.Object is BaseObject structure) {
				footprints.Add(structure.Position.X);
				footprints.Add(structure.Position.Y);
				footprints.Add(structure.ShapeRadius);
			}
		}

		foreach (var pad in _index.BasePads) {
			footprints.Add(pad.Anchor.X);
			footprints.Add(pad.Anchor.Y);
		}

		return footprints.ToArray();
	}
}
