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
/// <para>Only a flyer reads its height from its position (docs/retail/formats/script-dat.md#placement--the-actual-rule,
/// rule 5); everything else stands on whatever ground is under it. So the replacement keeps each flyer as high above the
/// ground as it was: every position a flyer stands on of its own, and every position a flyer group stands on, is raised or
/// lowered by how much the ground under it rose or fell, where it stands before and after the move, a position at Z 0 counted at the 5000 units a flyer flies at
/// there. This is the editor's own choice, not retail's: retail has no terrain to replace. A position whose coordinates
/// cannot be written (<see cref="MissionDocument.SetPoint"/>) keeps its height, and <see cref="Note"/> names it.</para>
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
		var flyers = FlyerPoints()
			.Select(point => (Point: point, At: _source.Layout.Entry<MapPoint22>(point)?.Fields))
			.Where(flyer => flyer.At != null)
			.Select(flyer => (flyer.Point, flyer.At!.X, flyer.At.Y, flyer.At.Z))
			.ToList();
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

				foreach (var (point, x, y, z) in flyers) {
					int rise = after.HeightAtWorld(x + dx, y + dy) - before.HeightAtWorld(x, y);
					if (rise == 0) {
						continue;
					}

					// Z 0 is where a flyer flies at its default height; a raised one that lands on 0 is written 1 above it.
					int raisedZ = (z == 0 ? FlyerObject.DefaultHoverHeight : z) + rise;
					try {
						_source.SetPoint(point, x + dx, y + dy, raisedZ == 0 ? 1 : raisedZ);
						raised++;
					} catch (InvalidOperationException ex) {
						skipped.Add(ex.Message);
					}
				}
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

	// The row-6 GUIDs of the positions a flyer reads its height from: each flyer's own, and each flyer group's own or its
	// route start's.
	private HashSet<short> FlyerPoints() {
		var points = new HashSet<short>();
		foreach (var entry in _source.Layout.Entries<FlyerRosterEntry102>()) {
			if (entry.Fields.PositionRef != -1) {
				points.Add(entry.Fields.PositionRef);
			}
		}

		foreach (var group in _index.Groups.Where(group => group.Kind == MissionUnitKind.Flyer)) {
			if (group.OwnPoint != -1) {
				points.Add(group.OwnPoint);
			} else {
				points.UnionWith(group.StartPoints);
			}
		}

		return points;
	}

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
