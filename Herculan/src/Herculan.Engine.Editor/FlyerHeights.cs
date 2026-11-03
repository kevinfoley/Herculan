using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>A position a flyer reads its height from, by its row-6 GUID, where it stands.</summary>
internal readonly record struct FlyerPosition(short Point, int X, int Y, int Z);

/// <summary>
/// Keeps each flyer as high above the ground as it was when the mission's terrain changes, replaced
/// (<see cref="TerrainChanger"/>) or sculpted (<see cref="TerrainSculptor"/>). Only a flyer reads its height from its position
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5); everything else stands on whatever ground is under
/// it. So every position a flyer stands on of its own, and every position a flyer group stands on, its own or its route
/// start's, is raised or lowered by how much the ground under it rose or fell, a position at Z 0 counted at the
/// <see cref="FlyerObject.DefaultHoverHeight"/> a flyer flies at there. This is the editor's own choice, not retail's: retail
/// has no terrain to change. A position whose coordinates cannot be written (<see cref="MissionDocument.SetPoint"/>) keeps
/// its height.
/// </summary>
internal sealed class FlyerHeights {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;

	public FlyerHeights(MissionDocument source, MissionIndex index) {
		_source = source;
		_index = index;
	}

	/// <summary>The positions a flyer reads its height from, where the layout has them now; one no record carries is left out.</summary>
	public IReadOnlyList<FlyerPosition> Read() => Points()
		.Select(point => (Point: point, At: _source.Layout.Entry<MapPoint22>(point)?.Fields))
		.Where(flyer => flyer.At != null)
		.Select(flyer => new FlyerPosition(flyer.Point, flyer.At!.X, flyer.At.Y, flyer.At.Z))
		.ToList();

	/// <summary>
	/// Raises each of <paramref name="positions"/> by <paramref name="rise"/> of it, world units, moving it
	/// <paramref name="east"/> and <paramref name="north"/> as it goes, inside a <see cref="MissionDocument.Change"/>; one whose
	/// rise is 0 is left as it is. Z 0 is where a flyer flies at its default height, and a raised one that lands on 0 is
	/// written 1 above it. Answers how many were written; each that could not be is left at its height, with the reason added
	/// to <paramref name="skipped"/>.
	/// </summary>
	public int Raise(IEnumerable<FlyerPosition> positions, int east, int north, Func<FlyerPosition, int> rise, List<string> skipped) {
		int raised = 0;
		foreach (var flyer in positions) {
			int by = rise(flyer);
			if (by == 0) {
				continue;
			}

			int z = (flyer.Z == 0 ? FlyerObject.DefaultHoverHeight : flyer.Z) + by;
			try {
				_source.SetPoint(flyer.Point, flyer.X + east, flyer.Y + north, z == 0 ? 1 : z);
				raised++;
			} catch (InvalidOperationException ex) {
				skipped.Add(ex.Message);
			}
		}

		return raised;
	}

	// The row-6 GUIDs of the positions a flyer reads its height from: each flyer's own, and each flyer group's own or its
	// route start's.
	private HashSet<short> Points() {
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
}
