using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Creates, reshapes and deletes trigger areas in a <see cref="MissionDocument"/>, as record edits: an area is a row-9
/// record naming a row-6 point for a circle's centre, or two for a box's opposite corners, with a circle's radius stored in
/// tens of world units (docs/retail/formats/msn-mission-file.md#row-9-field-decode--the-trigger-area-record-dat_0047065e-12-bytesrecord).
/// Moving an area's corner or centre is a point move (the editor's <c>PointMover</c>); this class holds what changes the
/// records themselves.
///
/// <para>A new area is unconditioned, under a new GUID (<see cref="MissionDocument.Append"/>), with the dead words
/// <c>0x02</c> and <c>0x04</c> at <c>-1</c> as all 335 retail records have them (<c>HercWorks.Query values --row 9</c>), and
/// its points are appended at Z 0 (<see cref="RouteEditing.AppendPoint"/>). A box's corners are written in the order they
/// were placed, the first where the drag began: <c>TriggerArea_ContainsPoint</c> (<c>004233a4</c>) takes the smaller and the
/// larger of the two on each axis, so either order catches the same ground
/// (docs/retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358). Every
/// retail record is reached by the generation whether or not an action names it: the generator exports every surviving
/// row-9 record with a GUID (<c>MissionGenerator.ReadAreas</c>, <c>WriteBlock</c>), so a new area reaches block 4 before
/// anything tests it.</para>
///
/// <para>An edit writes the record that supplies the area, its last base record (<see cref="MissionDocument.SetArea"/>).
/// A shape change, and a delete, can leave a row-6 point the area named that no field names any more
/// (<see cref="MissionDocument.CountPointRefs"/>, every record whatever its condition); its records are removed in the same
/// change, as a route edit removes them (<see cref="RouteEditing.RemoveLeftBehind"/>). An area is deleted only while no
/// action names it: a ref to a GUID with no record resolves to <c>-1</c>, and the load stops reading an action's areas at
/// its first <c>-1</c>, so the action would lose that area and every one after it.</para>
/// </summary>
public static class TriggerAreaEditing {
	/// <summary>Row 9's shape for a box between two points.</summary>
	public const short Box = 0;

	/// <summary>
	/// Row 9's shape for a circle, as 178 of the 335 retail records have it; DBSIM reads any shape but 0 as a circle
	/// (<c>TriggerArea_Resolve</c>, <c>00423358</c>).
	/// </summary>
	public const short Circle = 1;

	/// <summary>What <c>TriggerArea_Resolve</c> (<c>00423358</c>) multiplies a circle's stored radius by.</summary>
	public const int RadiusScale = 10;

	private const int AreaRow = 9;
	private const int ActionRow = 10;

	/// <summary>
	/// The stored radius for a radius in world units: rounded to the nearest <see cref="RadiusScale"/>, halves
	/// up, and kept from 1, since a radius of 0 catches nothing, to <see cref="short.MaxValue"/>, since the load sign-extends
	/// the stored word (<c>MOVSX</c> at <c>00423391</c>) and a larger one would read as negative. Retail stores 100 to 20000.
	/// </summary>
	/// <param name="radius">The radius in world units.</param>
	public static short StoredRadius(int radius) =>
		(short)Math.Clamp(((long)Math.Max(radius, 0) + RadiusScale / 2) / RadiusScale, 1, short.MaxValue);

	/// <summary>The radius in world units a stored radius gives.</summary>
	/// <param name="stored">The stored radius.</param>
	public static int Radius(short stored) => stored * RadiusScale;

	/// <summary>Whether a record of a shape is a box: shape 0 is, and any other is a circle.</summary>
	/// <param name="shape">The record's shape.</param>
	public static bool IsBox(short shape) => shape == Box;

	/// <summary>
	/// Appends a box between two corners, as one <see cref="MissionDocument.Change"/>,
	/// and answers its GUID. Throws <see cref="InvalidOperationException"/> for a box with no width
	/// or no depth, which the strict test catches nothing in.
	/// </summary>
	/// <param name="document">The mission document.</param>
	/// <param name="corner">One corner.</param>
	/// <param name="opposite">The opposite corner.</param>
	/// <param name="area">The new area's GUID.</param>
	public static MsnEdit CreateBox(MissionDocument document, (int X, int Y) corner, (int X, int Y) opposite, out short area) {
		if (corner.X == opposite.X || corner.Y == opposite.Y) {
			throw new InvalidOperationException("A box needs width and depth.");
		}

		short created = -1;
		var change = document.Change(() => {
			short first = RouteEditing.AppendPoint(document, corner);
			short second = RouteEditing.AppendPoint(document, opposite);
			created = Append(document, Box, first, second);
		});
		area = created;
		return change;
	}

	/// <summary>
	/// Appends a circle of a radius in world units about a centre, stored as
	/// <see cref="StoredRadius"/> gives it, as one <see cref="MissionDocument.Change"/>, and answers its GUID.
	/// Throws <see cref="InvalidOperationException"/> for a radius of 0 or less.
	/// </summary>
	/// <param name="document">The mission document.</param>
	/// <param name="centre">The circle's centre.</param>
	/// <param name="radius">The radius in world units.</param>
	/// <param name="area">The new area's GUID.</param>
	public static MsnEdit CreateCircle(MissionDocument document, (int X, int Y) centre, int radius, out short area) {
		if (radius <= 0) {
			throw new InvalidOperationException("A circle needs a radius.");
		}

		short created = -1;
		var change = document.Change(() => {
			short point = RouteEditing.AppendPoint(document, centre);
			created = Append(document, Circle, point, StoredRadius(radius));
		});
		area = created;
		return change;
	}

	/// <summary>
	/// Makes an area a box (shape <see cref="Box"/>) or a circle, as one
	/// <see cref="MissionDocument.Change"/>, from where its points stand. The editor's choice of what the
	/// other shape is, not a retail rule: a box becomes the circle about its middle whose radius is half its longer side, and
	/// a circle the square whose corners are its centre less and plus its radius on each axis. The first point is placed as
	/// <see cref="MissionDocument.PlacePoint"/> places one, rewritten in place when the area is all that names it and
	/// appended otherwise; a box's second corner is appended. A point the area no longer names that nothing else names is
	/// removed. Throws <see cref="InvalidOperationException"/> when the area is that shape already, or when a point it names
	/// is not in the mission.
	/// </summary>
	/// <param name="document">The mission document.</param>
	/// <param name="area">The area's GUID.</param>
	/// <param name="shape">The shape to make it: <see cref="Box"/> or a circle.</param>
	public static MsnEdit SetShape(MissionDocument document, short area, short shape) {
		var record = document.AreaRecord(area);
		bool toBox = IsBox(shape);
		if (IsBox(record.Shape) == toBox) {
			throw new InvalidOperationException(toBox ? "It is a box already." : "It is a circle already.");
		}

		short point = record.PointRef;
		var at = RouteEditing.Coordinates(document, point) ?? throw new InvalidOperationException("Its point is not in the mission.");
		if (toBox) {
			int radius = Radius(record.SecondPointOrRadius);
			return document.Change(() => {
				document.PlacePoint(point, 1, at.X - radius, at.Y - radius, 0, out short first);
				short second = RouteEditing.AppendPoint(document, (at.X + radius, at.Y + radius));
				document.SetArea(area, written => {
					written.Shape = Box;
					written.PointRef = first;
					written.SecondPointOrRadius = second;
				});
				RouteEditing.RemoveLeftBehind(document, [point]);
			});
		}

		short opposite = record.SecondPointOrRadius;
		var far = RouteEditing.Coordinates(document, opposite) ?? throw new InvalidOperationException("Its second corner is not in the mission.");
		var middle = ((int)(((long)at.X + far.X) / 2), (int)(((long)at.Y + far.Y) / 2));
		long longer = Math.Max(Math.Abs((long)far.X - at.X), Math.Abs((long)far.Y - at.Y));
		short stored = StoredRadius((int)Math.Min(longer / 2, int.MaxValue));
		return document.Change(() => {
			document.PlacePoint(point, 1, middle.Item1, middle.Item2, 0, out short centre);
			document.SetArea(area, written => {
				written.Shape = Circle;
				written.PointRef = centre;
				written.SecondPointOrRadius = stored;
			});
			RouteEditing.RemoveLeftBehind(document, [point, opposite]);
		});
	}

	/// <summary>
	/// Sets a circle's radius in world units, stored as
	/// <see cref="StoredRadius"/> gives it. Throws <see cref="InvalidOperationException"/> for a box.
	/// </summary>
	/// <param name="document">The mission document.</param>
	/// <param name="area">The circle's GUID.</param>
	/// <param name="radius">The radius in world units.</param>
	public static MsnEdit SetRadius(MissionDocument document, short area, int radius) {
		if (IsBox(document.AreaRecord(area).Shape)) {
			throw new InvalidOperationException("A box has no radius.");
		}

		short stored = StoredRadius(radius);
		return document.SetArea(area, written => written.SecondPointOrRadius = stored);
	}

	/// <summary>The row-10 actions that name an area in any of their eight area slots, every record whatever its condition.</summary>
	/// <param name="document">The mission document.</param>
	/// <param name="area">The area's GUID.</param>
	public static IReadOnlyList<MsnRecordId> Namers(MissionDocument document, short area) {
		var namers = new List<MsnRecordId>();
		if (area == -1) {
			return namers;
		}

		var actions = document.Mission.Actions ?? [];
		for (int i = 0; i < actions.Length; i++) {
			if (actions[i].AreaRefs.Contains(area)) {
				namers.Add(new MsnRecordId(ActionRow, i));
			}
		}

		return namers;
	}

	/// <summary>
	/// Deletes an area: every row-9 record of the GUID, whatever its condition, and the records of
	/// each point those named that is left named by nothing, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> while <see cref="Namers"/> finds an action.
	/// </summary>
	/// <param name="document">The mission document.</param>
	/// <param name="area">The area's GUID.</param>
	public static MsnEdit Delete(MissionDocument document, short area) {
		if (Namers(document, area).Count > 0) {
			throw new InvalidOperationException($"Trigger area {area} is referenced by a trigger.");
		}

		var named = (document.Mission.TriggerAreas ?? []).Where(record => record.GUID == area)
			.SelectMany(record => IsBox(record.Shape) ? new[] { record.PointRef, record.SecondPointOrRadius } : [record.PointRef])
			.ToList();
		return document.Change(() => {
			document.Remove(AreaRow, area);
			RouteEditing.RemoveLeftBehind(document, named);
		});
	}

	private static short Append(MissionDocument document, short shape, short point, short secondOrRadius) {
		document.Append(AreaRow, new TriggerArea12 {
			ConditionRef = -1, Unk04 = -1, Shape = shape, PointRef = point, SecondPointOrRadius = secondOrRadius,
		}, out short area);
		return area;
	}
}
