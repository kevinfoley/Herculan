using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>One base the map draws.</summary>
/// <param name="TypeIndex"><inheritdoc cref="ScriptBaseRecord.TypeIndex"/></param>
/// <param name="Point">The <c>script.dat</c> block-1 point it stands on.</param>
/// <param name="Shown">Block 9's <c>+0x1a</c> as the map's copy holds it: the record's first out-of-action operation, replaced by its type-2 group's <see cref="ScriptGroup.MapShown"/> (docs/retail/shell/mission-map.md#what-it-reads).</param>
public readonly record struct ShellMapBase(int TypeIndex, int Point, int Shown);

/// <summary>
/// The shell's map object, <c>DAT_0046f26c</c> (<c>shellmap.cpp</c>): the picture inside the briefing's
/// <c>Mission Map</c> panel. Built by <c>ShellMap_Constructor</c> (<c>00423f43</c>) each time a mission is
/// loaded, drawn by its vtable slot 0, <c>ShellMap_Paint</c>, moved by the six map buttons through slots
/// <c>+4</c> to <c>+0x18</c>, and introduced by the animation <c>ShellMap_IntroStep</c> runs the first time the
/// briefing comes up. See docs/retail/shell/mission-map.md.
///
/// <para>Everything is in world units until it is projected, and the projection is a plan view:
/// <c>((world - camera) &lt;&lt; 7) / altitude</c> about the viewport's centre, north up.</para>
/// </summary>
public sealed partial class ShellMap {
	/// <summary>The map's rect in the canvas, the literal <c>ShellMap_Build</c> builds it with.</summary>
	public static readonly ShellRect ViewRect = new(0x123, 0x43, 0x249, 0x124);

	/// <summary><c>+0x46</c> and <c>+0x4a</c>: the rect's right minus left and bottom minus top.</summary>
	private static int ViewWidth => ViewRect.X1 - ViewRect.X0;
	private static int ViewHeight => ViewRect.Y1 - ViewRect.Y0;

	/// <summary>The drawing context's <c>+0x220</c> and <c>+0x224</c>, <c>-width &gt;&gt; 1</c> and <c>-height &gt;&gt; 1</c>: the viewport's left and top about its centre.</summary>
	private static int HalfLeft => -ViewWidth >> 1;
	private static int HalfTop => -ViewHeight >> 1;

	private static int CentreX => ViewRect.X0 - HalfLeft;
	private static int CentreY => ViewRect.Y0 - HalfTop;

	/// <summary>The camera's <c>+0x10</c>, 7: the shift the projection scales by before dividing by the altitude.</summary>
	private const int FocalShift = 7;

	/// <summary><c>DAT_00471c1c</c>, the margin <c>ShellMap_LoadScriptDat</c> widens the mission's bounds by.</summary>
	private const int BoundsMargin = 10000;

	/// <summary><c>DAT_00471c20</c>, the further margin the relief, its placement and the camera's limits add.</summary>
	private const int ReliefMargin = 100000;

	/// <summary><c>DAT_00471c34</c>, the margin around the squad the intro zooms to.</summary>
	private const int SquadMargin = 250000;

	/// <summary><c>DAT_00471c24</c> and <c>DAT_00471c26</c>: the largest the relief bitmap may be.</summary>
	private const int ReliefMaxWidth = 640;
	private const int ReliefMaxHeight = 400;

	/// <summary>
	/// The relief's colours: a height is capped below <c>0x80</c>, divided by <c>0x80 / 0x18</c> (five, so
	/// 26 steps), and added to <c>0xd2 - 1</c>.
	/// </summary>
	private const int ReliefHeightCap = 0x80;
	private const int ReliefSteps = 0x18;
	private const int ReliefFirstColor = 0xd2 - 1;

	/// <summary>The camera's altitude before anything sets it, <c>DAT_00471870</c>.</summary>
	private const int StartAltitude = 200000;

	/// <summary><c>DAT_00471864</c> and <c>DAT_00471868</c>: what one zoom button step moves the altitude by, and how low it may go.</summary>
	private const int ZoomStep = 50000;
	private const int MinAltitude = 50000;

	/// <summary><c>DAT_00471858</c>, <c>DAT_0047185c</c> and <c>DAT_0047186c</c>, the pan step's range and the altitude it is scaled over.</summary>
	private const int PanStepFar = 100000;
	private const int PanStepNear = 5000;
	private const int PanScaleAltitude = 1000000;

	private readonly (int X, int Y)[] _points;
	private readonly int[]? _path;
	private readonly ShellMapBase[] _bases;
	private readonly (int X, int Y)[] _squad = new (int, int)[SquadSlots];
	private readonly short[] _squadRefs;
	private readonly ShellSurface? _relief;
	private readonly int _squadCount;
	private readonly int _textCount;

	/// <summary>The twenty member slots of <c>script.dat</c> block 11's record 0, and <c>+0x82</c>'s twenty positions.</summary>
	private const int SquadSlots = 20;

	private ShellMap((int X, int Y)[] points, int[]? path, ShellMapBase[] bases, short[] squadRefs, int squadCount,
			int textCount, ShellSurface? relief) {
		_points = points;
		_path = path;
		_bases = bases;
		_squadRefs = squadRefs;
		_squadCount = squadCount;
		_textCount = textCount;
		_relief = relief;
	}

	/// <summary><c>+0x19f</c>, <c>+0x1a3</c>, <c>+0x1ab</c>, <c>+0x1af</c>: every block-1 point's bounds, widened by <see cref="BoundsMargin"/>.</summary>
	public int MinX { get; private set; }
	public int MinY { get; private set; }
	public int MaxX { get; private set; }
	public int MaxY { get; private set; }

	/// <summary><c>+0x187</c>, <c>+0x18b</c>, <c>+0x18f</c>: the whole mission in view, which is also the altitude limit, <c>+0x3bd</c>.</summary>
	public (int X, int Y, int Z) FullView { get; private set; }

	/// <summary><c>+0x193</c>, <c>+0x197</c>, <c>+0x19b</c>: the squad in view, which the intro zooms to first.</summary>
	public (int X, int Y, int Z) SquadView { get; private set; }

	/// <summary><c>+0x12</c>, <c>+0x16</c>, <c>+0x1a</c>: where the camera is and how high.</summary>
	public int CameraX { get; private set; }
	public int CameraY { get; private set; }
	public int CameraZ { get; private set; } = StartAltitude;

	/// <summary><c>+0x3a</c>, <c>+0x3e</c>: how far the four arrows have panned from the camera, and <c>+0x42</c>, how far one press pans.</summary>
	public int PanX { get; private set; }
	public int PanY { get; private set; }
	public int PanStep { get; private set; } = PanStepFor(StartAltitude);

	/// <summary>
	/// Builds the map for the career's mission from the working <c>script.dat</c>, <c>mission.str</c> and
	/// <c>player.mec</c> (<see cref="ShellWorkingFiles"/>), the <c>data\</c> files the original reads;
	/// <c>data\mforms.dat</c>; and the zone's <c>dat\zone%d.dat</c> and <c>dba\zone%d.dba</c>. Null when
	/// there is no mission file.
	/// </summary>
	public static ShellMap? Load(string installRoot, ShellWorkingFiles working, GameContent content) {
		string? scriptPath = working.Script;
		if (!File.Exists(scriptPath) || new ScriptDatTransformer().Parse(File.ReadAllBytes(scriptPath)) is not { } script) {
			return null;
		}

		string? textPath = working.Text;
		int textCount = File.Exists(textPath) && SimStrings.Parse(File.ReadAllBytes(textPath)) is { GroupCount: > 0 } text
			? text.Group(0).Count : 0;

		// ShellMap_ReadSquadHeader reads data\player.mec's second short, the squad size.
		string? mecPath = working.Player;
		byte[] mec = File.Exists(mecPath) ? File.ReadAllBytes(mecPath) : Array.Empty<byte>();
		int squadCount = mec.Length >= 4 ? BitConverter.ToInt16(mec, 2) : 0;

		string formsPath = Path.Combine(installRoot, MissionLoader.DataFolderName, MechFormationTable.ResourceName);
		var forms = File.Exists(formsPath) ? MechFormationTable.Parse(File.ReadAllBytes(formsPath)) : null;

		int zone = script.ZoneIndex;
		return Build(script, forms, squadCount, textCount, ZoneRelief.Load(content, zone));
	}

	/// <summary>The constructor's reads and derivations, over an already-parsed mission.</summary>
	internal static ShellMap Build(ScriptDat script, MechFormationTable? forms, int squadCount, int textCount, ZoneRelief? zone) {
		var points = script.Coordinates.Select(c => (c.X, c.Y)).ToArray();

		// ShellMap_LoadScriptDat: the nav path is the waypoint group block 11 record 0's first order names.
		var group = script.Groups.Length > 0 ? script.Groups[0] : null;
		var order = group != null && group.OrderRefs[0] >= 0 && group.OrderRefs[0] < script.Orders.Length
			? script.Orders[group.OrderRefs[0]] : null;
		int[]? path = order is { RouteRef: >= 0 } && order.RouteRef < script.WaypointGroups.Length
			? script.WaypointGroups[order.RouteRef].Waypoints.Select(w => (int)w).ToArray() : null;

		// Block 9 kept whole: the map's copy starts each base's shown field from its first out-of-action
		// operation at +0x1a. Every type-2 group then writes its MapShown there (and its side at +0x1c,
		// which nothing here reads) and, from its own point or its route's first, their position.
		var bases = script.Bases.Select(b => new ShellMapBase(b.TypeIndex, b.PositionRef,
			b.CounterOps[0])).ToArray();
		foreach (var owner in script.Groups.Where(g => g.MemberKind == 2)) {
			foreach (short member in owner.MemberRefs) {
				// Retail does not bound the member ref; the upper check is this engine's.
				if (member < 0 || member >= bases.Length) {
					continue;
				}

				int point = bases[member].Point;
				if (owner.PositionRef != -1) {
					point = owner.PositionRef;
				} else if (owner.RouteRef != -1 && owner.RouteRef < script.WaypointGroups.Length
						&& script.WaypointGroups[owner.RouteRef].Waypoints.Length > 0) {
					point = script.WaypointGroups[owner.RouteRef].Waypoints[0];
				}

				bases[member] = bases[member] with { Point = point, Shown = owner.MapShown };
			}
		}

		var refs = group?.MemberRefs ?? Enumerable.Repeat((short)-1, SquadSlots).ToArray();
		var map = new ShellMap(points, path, bases, refs, squadCount, textCount, zone?.BuildRelief(Bounds(points)));
		map.SetBounds();
		map.PlaceSquad(script, group, order, path, forms);
		map.SetFullView();
		map.SetSquadView();
		return map;
	}

	private static (int MinX, int MinY, int MaxX, int MaxY) Bounds((int X, int Y)[] points) {
		if (points.Length == 0) {
			return (0, 0, 0, 0);
		}

		return (points.Min(p => p.X) - BoundsMargin, points.Min(p => p.Y) - BoundsMargin,
			points.Max(p => p.X) + BoundsMargin, points.Max(p => p.Y) + BoundsMargin);
	}

	private void SetBounds() => (MinX, MinY, MaxX, MaxY) = Bounds(_points);

	/// <summary>
	/// <c>ShellMap_ReadSquadHeader</c> (<c>00424db0</c>): each of block 11 record 0's members that names a
	/// block-7 record stands on the group's anchor, and every member after the first is offset by its
	/// <c>mforms.dat</c> slot turned through the group's heading.
	/// </summary>
	private void PlaceSquad(ScriptDat script, ScriptGroup? group, ScriptOrder? order,
			int[]? path, MechFormationTable? forms) {
		if (group == null) {
			return;
		}

		short heading;
		if (group.HeadingRef < 0) {
			heading = path is { Length: >= 2 } && Point(path[0]) is var p0 && Point(path[1]) is var p1
				? unchecked((short)(SimTrig.Atan2(p1.Y - p0.Y, p1.X - p0.X) - 0x4000)) : (short)0;
		} else {
			heading = group.HeadingRef < script.Headings.Length ? script.Headings[group.HeadingRef].Degrees : (short)0;
		}

		(int X, int Y) anchor;
		if (group.PositionRef != -1) {
			anchor = Point(group.PositionRef);
		} else if (path is { Length: > 0 }) {
			anchor = Point(path[0]);
		} else if (order is { PointRef: not -1 }) {
			anchor = Point(order.PointRef);
		} else if (order is { RouteRef: >= 0 } && order.RouteRef < script.WaypointGroups.Length
				&& script.WaypointGroups[order.RouteRef].Waypoints.Length > 0) {
			anchor = Point(script.WaypointGroups[order.RouteRef].Waypoints[0]);
		} else {
			anchor = (0, 0);
		}

		int formation = order?.FormationId ?? -1;
		short cos = SimTrig.Cos(heading);
		short sin = SimTrig.Sin(heading);
		for (int slot = 0; slot < SquadSlots; slot++) {
			short member = _squadRefs[slot];
			if (member == -1 || member >= script.Mechs.Length) {
				continue;
			}

			_squad[slot] = anchor;
			if (slot != 0 && forms?.OffsetFor(formation, slot) is { } offset) {
				// Math_RotateVec2Q14 (00451f94): the offset as two shorts through the heading's matrix, each rounded to Q14.
				short dx = (short)offset.X;
				short dy = (short)offset.Y;
				_squad[slot] = (anchor.X + (short)((dx * cos - dy * sin + 0x2000) >> 14),
					anchor.Y + (short)((dx * sin + dy * cos + 0x2000) >> 14));
			}
		}
	}

	private (int X, int Y) Point(int index) => index >= 0 && index < _points.Length ? _points[index] : (0, 0);

	/// <summary><c>ShellMap_FitBounds</c> (<c>0042524e</c>): the centre of the bounds, and the altitude that fits both their halves in the viewport's.</summary>
	private void SetFullView() {
		int halfX = (MaxX - MinX) >> 1;
		int halfY = (MaxY - MinY) >> 1;
		int z = Math.Max((halfX << FocalShift) / (ViewWidth >> 1), (halfY << FocalShift) / (ViewHeight >> 1));
		FullView = (halfX + MinX, halfY + MinY, z);
	}

	/// <summary>
	/// <c>ShellMap_FitSquad</c> (<c>004252e1</c>): the same over the squad's positions, widened by <see cref="SquadMargin"/>, for
	/// the first <c>+0x80</c> slots — the count <c>player.mec</c> gives — whose member is not <c>-1</c>.
	/// </summary>
	private void SetSquadView() {
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		for (int slot = 0; slot < _squadCount && slot < SquadSlots; slot++) {
			if (_squadRefs[slot] == -1) {
				continue;
			}

			minX = Math.Min(minX, _squad[slot].X);
			minY = Math.Min(minY, _squad[slot].Y);
			maxX = Math.Max(maxX, _squad[slot].X);
			maxY = Math.Max(maxY, _squad[slot].Y);
		}

		unchecked {
			int left = minX - SquadMargin;
			int bottom = minY - SquadMargin;
			int halfX = maxX + SquadMargin - left >> 1;
			int halfY = maxY + SquadMargin - bottom >> 1;
			int z = Math.Max((halfX << FocalShift) / (ViewWidth >> 1), (halfY << FocalShift) / (ViewHeight >> 1));
			SquadView = (halfX + left, bottom + halfY, z);
		}
	}

	/// <summary>
	/// <c>ShellMap_ClampCamera</c> (<c>00427891</c>): holds a camera inside the relief. The altitude is capped at the full view's,
	/// and the centre is kept at least half a viewport's world width inside the bounds widened by
	/// <see cref="ReliefMargin"/> on each side, the far side having the last word.
	/// </summary>
	private (int X, int Y, int Z) Clamp((int X, int Y, int Z) camera) {
		int z = Math.Min(camera.Z, FullView.Z);
		int x = camera.X;
		int y = camera.Y;
		unchecked {
			int halfX = -HalfLeft * z >> FocalShift;
			x = Math.Max(x, halfX + (MinX - ReliefMargin));
			x = Math.Min(x, MaxX + ReliefMargin - halfX);
			int halfY = -HalfTop * z >> FocalShift;
			y = Math.Max(y, halfY + (MinY - ReliefMargin));
			y = Math.Min(y, MaxY + ReliefMargin - halfY);
		}

		return (x, y, z);
	}

	/// <summary><c>Map_PanStepFor</c> (<c>00420195</c>), the camera base's pan step for an altitude.</summary>
	private static int PanStepFor(int altitude) => unchecked(
		((altitude - MinAltitude >> 3) * (PanStepFar - PanStepNear >> 3)) / (PanScaleAltitude - MinAltitude >> 3) * 8
		+ PanStepNear);

	/// <summary><c>ShellMap_PanStepFor</c> (<c>00427ae7</c>), the map's own pan step, scaled over its altitude limit instead.</summary>
	private int MapPanStepFor(int altitude) {
		int range = FullView.Z - MinAltitude;
		return range == 0 ? PanStepNear : unchecked(((altitude - MinAltitude >> 8) * (PanStepFar - PanStepNear)) / range * 0x100
			+ PanStepNear);
	}

	/// <summary>
	/// A map button: the six methods the arrows call, <c>+0xc</c> north, <c>+0x10</c> south, <c>+0x14</c>
	/// west and <c>+0x18</c> east, each of which pans by <see cref="PanStep"/> only when the clamp leaves
	/// the moved centre alone; <c>+4</c>, <c>Map_ZoomIn</c> (<c>004200e4</c>), down one step while that stays above
	/// <see cref="MinAltitude"/>; and <c>+8</c>, <c>ShellMap_ZoomOut</c> (<c>00427946</c>), up one step only when the clamp leaves
	/// the new altitude alone. The two zooms re-derive the pan step, each by its own formula.
	/// </summary>
	public void Press(ShellMissionArrow arrow) {
		switch (arrow) {
			case ShellMissionArrow.MapUp:
				PanY = TryPan(0, PanStep) ? PanY + PanStep : PanY;
				break;
			case ShellMissionArrow.MapDown:
				PanY = TryPan(0, -PanStep) ? PanY - PanStep : PanY;
				break;
			case ShellMissionArrow.MapLeft:
				PanX = TryPan(-PanStep, 0) ? PanX - PanStep : PanX;
				break;
			case ShellMissionArrow.MapRight:
				PanX = TryPan(PanStep, 0) ? PanX + PanStep : PanX;
				break;
			case ShellMissionArrow.MapInward:
				if (MinAltitude <= CameraZ - ZoomStep) {
					CameraZ -= ZoomStep;
					PanStep = PanStepFor(CameraZ);
				}

				break;
			case ShellMissionArrow.MapOutward:
				int raised = CameraZ + ZoomStep;
				if (Clamp((CameraX, CameraY, raised)).Z == raised) {
					CameraZ = raised;
					PanStep = MapPanStepFor(CameraZ);
				}

				break;
		}
	}

	private bool TryPan(int dx, int dy) {
		var moved = (CameraX + PanX + dx, CameraY + PanY + dy, CameraZ);
		var held = Clamp(moved);
		return held.Item1 == moved.Item1 && held.Item2 == moved.Item2;
	}
}
