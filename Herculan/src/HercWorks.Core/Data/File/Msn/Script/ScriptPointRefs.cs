namespace HercWorks.Core.Data.File.Msn.Script;

/// <summary>
/// How to give a field of a <see cref="ScriptDat"/> that names a block-1 point a point of its own without moving anything
/// else that names the same point. The mission editor places a drag's preview this way in the <c>script.dat</c> generated
/// from the <c>.MSN</c>, which the drag's commit then writes (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>Every other block names block 1 by array position, so a point appended at the end shifts nothing. Block 1's
/// extent is also the mission box and frames the heads-down map
/// (docs/retail/simulation/heads-down-display.md#the-maps-frame-of-reference), so a point placed outside it grows
/// both, and a point moved in place can shrink them.</para>
/// </summary>
public static class ScriptPointRefs {
	// How many fields name block-1 point <point>: route waypoints, a trigger area's first corner or centre and a box's
	// second corner, the three rosters' positions, an order's point, a group's point and an objective's point. Every
	// record counts, read or not, so the count never misses a reader.
	private static int Count(ScriptDat script, int point) {
		int count = 0;
		foreach (var route in script.WaypointGroups) {
			count += route.Waypoints.Count(reference => reference == point);
		}

		foreach (var area in script.TriggerAreas) {
			count += Is(area.PointRef, point);
			if (area.Shape == 0) {
				count += Is(area.SecondPointOrRadius, point);
			}
		}

		count += script.Mechs.Sum(record => Is(record.PositionRef, point));
		count += script.Flyers.Sum(record => Is(record.PositionRef, point));
		count += script.Bases.Sum(record => Is(record.PositionRef, point));
		count += script.Orders.Sum(record => Is(record.PointRef, point));
		count += script.Groups.Sum(record => Is(record.PositionRef, point));
		count += script.Objectives.Sum(record => Is(record.PointRef, point));
		return count;
	}

	/// <summary>
	/// The ref <paramref name="moving"/> fields naming <paramref name="reference"/> should hold to stand together at
	/// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>): the same ref, its point rewritten, when
	/// nothing else names that point; otherwise a point appended to block 1. Throws
	/// <see cref="InvalidOperationException"/> when block 1 is already as long as a ref can index.
	/// </summary>
	public static short Place(ScriptDat script, short reference, int x, int y, int z, int moving = 1) {
		if (reference >= 0 && reference < script.Coordinates.Length && Count(script, reference) == moving) {
			var point = script.Coordinates[reference];
			point.X = x;
			point.Y = y;
			point.Z = z;
			return reference;
		}

		if (script.Coordinates.Length >= short.MaxValue) {
			throw new InvalidOperationException($"Block 1 already holds {script.Coordinates.Length} points, as many as a ref can name.");
		}

		script.Coordinates = [.. script.Coordinates, new ScriptCoordinate { X = x, Y = y, Z = z }];
		return (short)(script.Coordinates.Length - 1);
	}

	private static int Is(short reference, int point) => reference == point ? 1 : 0;
}
