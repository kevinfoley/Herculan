using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The mission's own terrain (<see cref="ZoneFiles"/>), which Save writes beside its <c>.MSN</c>
/// (<see cref="SavedMissions.Write"/>) and Export Mission Bundle into its bundle, or none when it plays on the archive's
/// zone; and replacing it from a terrain image (<see cref="TerrainImages"/>) as one step of <see cref="PointMover"/>'s
/// undo history, as the Terrain window asks (<see cref="MissionTerrainWindow"/>).
///
/// <para>A replacement plays as the zone the mission already has of its own, or else as the highest number no zone of the
/// install has (<see cref="ZoneCatalog.FreeZone"/>), which it writes as the mission's zone, header word 1
/// (<see cref="MissionDocument.SetHeaderWord"/>). The scene's terrain is swapped in place
/// (<see cref="MissionScene.ReplaceTerrain"/>), painted and levelled for the structures and pads where they stand, and
/// everything is stood on it again. <see cref="WorldChanged"/> then tells whatever has the scene uploaded to upload it
/// again. Undo swaps the terrain the mission had back in.</para>
///
/// <para>Every terrain starts at the world's origin, so one of another size would leave the mission where it was laid out
/// on the old one, kilometers from the new one's center. The replacement moves every position by half the difference in
/// size instead, so the mission keeps its place about the terrain's center: every row-6 record, its condition's variants
/// and the templates a random table copies from included. A position that is the sum of two others (unused in retail) is
/// left to its sum, which moves twice as far; <see cref="Note"/> counts them. This is the editor's own choice.</para>
///
/// <para>The replacement keeps each flyer as high above the ground as it was (<see cref="FlyerHeights"/>), measuring the
/// ground where it stands before and after the move. A position whose coordinates cannot be written keeps its height, and
/// <see cref="Note"/> names it.</para>
///
/// <para>A mission on an archive zone is given a terrain of its own, a copy of that zone, before it is sculpted
/// (<see cref="CopyZone"/>), and a sculpting stroke builds that terrain again from the scene's (<see cref="TakeSculpted"/>).</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since replacing the
/// terrain replaces what the frame draws with.</para>
/// </summary>
internal sealed class TerrainChanger {
	private const int PointRow = 6;

	private readonly GameContent _content;
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly MissionOverlay _overlay;
	private readonly TerrainMaterialTable _materials;
	private readonly int _detailLevel;
	private readonly Func<short> _freeZone;
	private readonly FlyerHeights _flyers;
	private ZoneFiles? _requested;

	/// <param name="content">DBSIM's mount, which holds the archive's zones.</param>
	/// <param name="detailLevel">The terrain detail setting the mission's terrain was loaded at (<see cref="TerrainDetail"/>).</param>
	/// <param name="freeZone">The number a mission's own terrain is played as when it has none yet (<see cref="ZoneCatalog.FreeZone"/>).</param>
	public TerrainChanger(GameContent content, MissionDocument source, MissionScene scene, MissionIndex index, PointMover mover,
			SceneSync sync, MissionOverlay overlay, TerrainMaterialTable materials, int detailLevel, Func<short> freeZone) {
		_content = content;
		_source = source;
		_scene = scene;
		_index = index;
		_mover = mover;
		_sync = sync;
		_overlay = overlay;
		_materials = materials;
		_detailLevel = detailLevel;
		_freeZone = freeZone;
		_flyers = new FlyerHeights(source, index);
	}

	/// <summary>Raised once the scene's terrain is replaced or put back, for whatever has it uploaded.</summary>
	public event Action? WorldChanged;

	/// <summary>
	/// Raised once a replacement, or its undo or redo, is made, with how far every position moved east and north (0 for a
	/// terrain of the same size) and the terrain it replaced, for whatever keeps a place of its own on the ground.
	/// </summary>
	public event Action<int, int, HeightGrid>? Replaced;

	/// <summary>The mission's own terrain, or null when it plays on the archive's zone <see cref="Zone"/>.</summary>
	public ZoneFiles? OwnZone { get; private set; }

	/// <summary>The zone the mission is played in: the launch's header word (<see cref="MissionDocument.LaunchHeader"/>).</summary>
	public short Zone => _source.LaunchHeader[ZoneCatalog.ZoneWord];

	/// <summary>The cell size shift and height scale of the terrain the mission has (<see cref="HeightGrid"/>).</summary>
	public (int CellSizeShift, int HeightScale) Scale => (_scene.World.Terrain.CellSizeShift, _scene.World.Terrain.HeightScale);

	/// <summary>Why the terrain cannot be replaced, or null when it can.</summary>
	public string? WhyFixed =>
		_source.Generation.Header[ZoneCatalog.ZoneWord] != null ? "The launch sets the zone." : null;

	/// <summary>Why the last replacement asked for could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>What the last replacement did to the mission's positions, or null.</summary>
	public string? Note { get; private set; }

	/// <summary>Takes <paramref name="zone"/> as the terrain the mission was opened with, as no step of the undo history.</summary>
	public void Load(ZoneFiles? zone) => OwnZone = zone;

	/// <summary>Asks for the mission's terrain to be <paramref name="zone"/>.</summary>
	public void Request(ZoneFiles zone) => _requested = zone;

	/// <summary>Makes the replacement asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } zone) {
			_requested = null;
			Note = null;
			Failure = Replace(zone);
		}
	}

	/// <summary>
	/// The mission's heightmap as the zone loads it, before any structure levels it: its own terrain's heights, or the
	/// archive zone's image, as rows of the width in image order, row 0 north. Throws <see cref="InvalidDataException"/> or
	/// <see cref="FileNotFoundException"/> for an archive zone that does not read.
	/// </summary>
	public (int Width, int Height, byte[] Heights) Heightmap() {
		if (OwnZone is { } own) {
			return (own.Side, own.Side, own.Heights.ToArray());
		}

		var image = TerrainZoneLoader.ReadHeightmapImage(_content, Zone);
		return (image.Cols, image.Rows, image.ImageData!);
	}

	private string? Replace(ZoneFiles zone) {
		if (WhyFixed is { } reason) {
			return reason;
		}

		short number = OwnZone != null ? Zone : _freeZone();
		var before = _scene.World.Terrain;
		var after = TerrainZoneLoader.FromZone(zone, _materials, new SimRandom(), detailLevel: _detailLevel);
		var previous = OwnZone;
		int dx = (int)((after.WorldWidth - before.WorldWidth) / 2);
		int dy = (int)((after.WorldHeight - before.WorldHeight) / 2);

		// Read before the move writes them.
		var flyers = _flyers.Read();
		Swap(after, zone);

		var skipped = new List<string>();
		int raised = 0, sums = 0;
		MsnEdit change;
		try {
			change = _source.Change(() => {
				if (number != Zone) {
					_source.SetHeaderWord(ZoneCatalog.ZoneWord, number);
				}

				if (dx != 0 || dy != 0) {
					foreach (short guid in (_source.Mission.Points ?? []).Select(record => record.GUID).Distinct().ToList()) {
						_source.RewriteEvery<MapPoint22>(PointRow, guid, record => {
							if (record.SumFlag != 0) {
								sums++;
								return;
							}

							record.X += dx;
							record.Y += dy;
						});
					}
				}

				raised = _flyers.Raise(flyers, dx, dy,
					flyer => after.HeightAtWorld(flyer.X + dx, flyer.Y + dy) - before.HeightAtWorld(flyer.X, flyer.Y), skipped);
			});
		} catch (InvalidOperationException ex) {
			Swap(before, previous);
			_sync.Sync();
			return ex.Message;
		}

		_sync.Sync();
		_mover.Record(new TerrainEdit(this, change, (before, previous), (after, zone), dx, dy));
		Replaced?.Invoke(dx, dy, before);
		var notes = new List<string>();
		if (dx != 0 || dy != 0) {
			notes.Add($"Every position moved {Meters(Math.Abs(dx))} {(dx < 0 ? "west" : "east")} and {Meters(Math.Abs(dy))} "
				+ $"{(dy < 0 ? "south" : "north")}, keeping its place about the terrain's center.");
		}

		if (sums > 0) {
			notes.Add($"Positions that are the sum of two others moved twice as far: {sums}.");
		}

		if (raised > 0) {
			notes.Add($"Flyer positions moved up or down with the ground under them: {raised}.");
		}

		if (skipped.Count > 0) {
			notes.Add($"Flyer positions left at their height: {string.Join(" ", skipped)}");
		}

		Note = notes.Count > 0 ? string.Join(" ", notes) : null;
		return null;
	}

	private static string Meters(int worldUnits) =>
		(worldUnits / Render.WorldScale.WorldUnitsPerMeter).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + " m";

	/// <summary>
	/// Gives a mission that plays on an archive zone a terrain of its own, a copy of that zone's heightmap at the terrain's
	/// cell size and height scale, played as the highest number no zone of the install has, as a replacement is, as one step
	/// of the undo history. The scene's terrain is the same heights, so it stays. Answers why it could not be made, or null;
	/// a mission with a terrain of its own already is left as it is.
	/// </summary>
	public string? CopyZone() {
		if (WhyFixed is { } reason) {
			return reason;
		}

		if (OwnZone != null) {
			return null;
		}

		ZoneFiles zone;
		try {
			var (_, _, heights) = Heightmap();
			var (shift, scale) = Scale;
			zone = ZoneFiles.Create(heights, shift, scale);
		} catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException) {
			return ex.Message;
		}

		short number = _freeZone();
		MsnEdit change;
		try {
			change = _sync.Run(() => _source.Change(() => _source.SetHeaderWord(ZoneCatalog.ZoneWord, number)));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		OwnZone = zone;
		_mover.Record(new OwnZoneEdit(this, change, zone));
		return null;
	}

	/// <summary>
	/// Builds the mission's own terrain again from the scene's heights as the zone loads them
	/// (<see cref="HeightGrid.LoadedHeightAt"/>), once a sculpting stroke has written them, and answers it. The mission has a
	/// terrain of its own already (<see cref="CopyZone"/>), whose cell size and height scale the scene's are.
	/// </summary>
	internal ZoneFiles TakeSculpted() {
		var terrain = _scene.World.Terrain;
		int side = terrain.Width;
		var heights = new byte[side * terrain.Height];

		// Image order: row 0 is the north edge, the grid's highest cell row (TerrainZoneLoader).
		for (int y = 0; y < terrain.Height; y++) {
			int row = (terrain.Height - 1 - y) * side;
			for (int x = 0; x < side; x++) {
				heights[row + x] = terrain.LoadedHeightAt(x, y);
			}
		}

		OwnZone = ZoneFiles.Create(heights, terrain.CellSizeShift, terrain.HeightScale);
		return OwnZone;
	}

	/// <summary>Puts <paramref name="zone"/> back as the mission's own terrain, for a sculpting stroke's undo or redo.</summary>
	internal void PutSculpted(ZoneFiles zone) => OwnZone = zone;

	// Puts <terrain> in the scene and <zone> as the mission's own, levels the ground and stands everything on it again.
	private void Swap(HeightGrid terrain, ZoneFiles? zone) {
		OwnZone = zone;
		var modelled = _index.Objects.Concat(_sync.Built).Distinct(ReferenceEqualityComparer.Instance).Cast<SceneObject>().ToList();
		_scene.ReplaceTerrain(terrain, _index.Objects, _index.BasePads, modelled);

		// Stood on the new ground in place: told to whatever picks them.
		foreach (var placed in _index.Objects) {
			_sync.Place(placed, placed.Object.Position, placed.Object.Heading);
		}

		_mover.TakeTerrain();
		_overlay.Redrape();
		WorldChanged?.Invoke();
	}

	// Puts the mission's zone number and its own terrain back as the copy of an archive zone found or left them, and syncs.
	private sealed record OwnZoneEdit(TerrainChanger Changer, MsnEdit Change, ZoneFiles Zone) : IEditorEdit {
		public void Undo() {
			Changer._sync.Undo(Change);
			Changer.OwnZone = null;
		}

		public void Redo() {
			Changer._sync.Redo(Change);
			Changer.OwnZone = Zone;
		}
	}

	// Puts the terrain and the .MSN back as the step found or left them, and syncs; the step moved every position <East>
	// and <North>.
	private sealed record TerrainEdit(TerrainChanger Changer, MsnEdit Change, (HeightGrid Terrain, ZoneFiles? Zone) Before,
			(HeightGrid Terrain, ZoneFiles? Zone) After, int East, int North) : IEditorEdit {
		public void Undo() {
			Changer._source.Undo(Change);
			Changer.Swap(Before.Terrain, Before.Zone);
			Changer._sync.Sync();
			Changer.Replaced?.Invoke(-East, -North, After.Terrain);
		}

		public void Redo() {
			Changer._source.Redo(Change);
			Changer.Swap(After.Terrain, After.Zone);
			Changer._sync.Sync();
			Changer.Replaced?.Invoke(East, North, Before.Terrain);
		}
	}
}
