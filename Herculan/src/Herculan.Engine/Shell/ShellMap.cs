using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>One base the map draws.</summary>
/// <param name="TypeIndex"><inheritdoc cref="ScriptBaseRecord.TypeIndex"/></param>
/// <param name="Point">The <c>script.dat</c> block-1 point it stands on.</param>
/// <param name="Shown">Block 9's <c>+0x1a</c> as the map's copy holds it: the record's first out-of-action operation, replaced by its type-2 group's <see cref="ScriptGroup.MapShown"/> (docs/shell/mission-map.md#what-it-reads).</param>
public readonly record struct ShellMapBase(int TypeIndex, int Point, int Shown);

/// <summary>
/// The shell's map object, <c>DAT_0046f26c</c> (<c>shellmap.cpp</c>): the picture inside the briefing's
/// <c>Mission Map</c> panel. Built by <c>ShellMap_Constructor</c> (<c>00423f43</c>) each time a mission is
/// loaded, drawn by its vtable slot 0, <c>ShellMap_Paint</c>, moved by the six map buttons through slots
/// <c>+4</c> to <c>+0x18</c>, and introduced by the animation <c>ShellMap_IntroStep</c> runs the first time the
/// briefing comes up. See docs/shell/mission-map.md.
///
/// <para>Everything is in world units until it is projected, and the projection is a plan view:
/// <c>((world - camera) &lt;&lt; 7) / altitude</c> about the viewport's centre, north up.</para>
/// </summary>
public sealed class ShellMap {
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

	/// <summary><c>DAT_00471c3c</c>, the world distance between grid lines.</summary>
	private const int GridSpacing = 200000;

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

	private const byte ClearColor = 0x10;
	private const byte GridColor = 0x0f;
	private const byte BoundsColor = 10;
	private const byte PathColor = 0x0e;
	private const byte FriendlyColor = 10;
	private const byte HostileColor = 0x21;

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

	/// <summary>
	/// The camera a paint draws through, <c>ShellMap_Paint</c>'s opening. Once the intro is over the panned
	/// camera is clamped and the clamp is kept, the pan surviving it; before then the camera is clamped
	/// for the paint alone and the pan is not applied.
	/// </summary>
	private (int X, int Y, int Z) PaintCamera() {
		if (_state == State.Done) {
			var held = Clamp((CameraX + PanX, CameraY + PanY, CameraZ));
			CameraX = held.X - PanX;
			CameraY = held.Y - PanY;
			CameraZ = held.Z;
			return held;
		}

		var clamped = Clamp((CameraX, CameraY, CameraZ));
		return (clamped.X + PanX, clamped.Y + PanY, clamped.Z);
	}

	/// <summary>A world point on the canvas through <paramref name="camera"/>: <c>Map_Project</c> (<c>0041fff2</c>)'s plan-view projection about the viewport's centre.</summary>
	private static (int X, int Y) Project((int X, int Y, int Z) camera, int x, int y) => unchecked(
		(CentreX + ((x - camera.X) << FocalShift) / camera.Z, CentreY - ((y - camera.Y) << FocalShift) / camera.Z));

	/// <summary>
	/// Draws the map, <c>ShellMap_Paint</c>'s passes in order: the viewport cleared to <c>0x10</c>, the relief
	/// stretched over the bounds widened by <see cref="ReliefMargin"/>, the grid and the bounds, the nav
	/// path, the bases, the nav markers and the squad.
	/// </summary>
	public void Paint(ShellSurface surface, ShellMapArt? art, uint now) {
		var camera = PaintCamera();
		var clip = surface.PushClip(ViewRect);

		// The first paint of the intro draws straight to the screen with a wait after each of its first
		// passes and after every grid line; everything before the wait that has not run out is what shows.
		int budget = _slowPaintStart is { } start ? (int)(now - start) : int.MaxValue;

		surface.Fill(ViewRect.X0, ViewRect.Y0, ViewRect.X1, ViewRect.Y1, ClearColor);
		if (budget >= SlowPaintClearWait) {
			PaintRelief(surface, camera);
		}

		if (budget >= SlowPaintClearWait + SlowPaintReliefWait) {
			int lines = PaintGrid(surface, camera, budget - SlowPaintClearWait - SlowPaintReliefWait + 1);
			if (_slowPaintStart == null || budget >= SlowPaintClearWait + SlowPaintReliefWait + lines) {
				PaintPath(surface, camera);
				PaintBases(surface, camera, art);
				PaintNavMarkers(surface, camera, art);
				PaintSquad(surface, camera, art);
			}
		}

		surface.PopClip(clip);
	}

	private void PaintRelief(ShellSurface surface, (int X, int Y, int Z) camera) {
		if (_relief == null) {
			return;
		}

		var topLeft = Project(camera, MinX - ReliefMargin, MaxY + ReliefMargin);
		var bottomRight = Project(camera, MaxX + ReliefMargin, MinY - ReliefMargin);
		surface.StretchBlit(_relief, topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
	}

	/// <summary>
	/// <c>ShellMap_PaintGrid</c> (<c>004258f6</c>): grid lines in <c>0x0f</c> through the projected world origin, one every
	/// <see cref="GridSpacing"/> — the spacing measured on the projection of sixteen of them along x —
	/// rightwards, leftwards, downwards and upwards, across the whole viewport; then, outside the intro's
	/// first paint, the bounds outlined in colour 10. Draws at most <paramref name="limit"/> lines and
	/// returns how many there are.
	/// </summary>
	private int PaintGrid(ShellSurface surface, (int X, int Y, int Z) camera, int limit) {
		int drawn = 0;
		void Line(int x0, int y0, int x1, int y1) {
			if (drawn++ < limit) {
				surface.Line(CentreX + x0, CentreY + y0, CentreX + x1, CentreY + y1, GridColor);
			}
		}

		unchecked {
			int originX = (-camera.X << FocalShift) / camera.Z;
			int originY = -((-camera.Y << FocalShift) / camera.Z);
			int sixteen = Math.Abs(((GridSpacing * 16 - camera.X) << FocalShift) / camera.Z - originX);

			for (int i = 0, x = originX; -x != HalfLeft && x <= -HalfLeft; x = (++i * sixteen >> 4) + originX) {
				Line(x, HalfTop, x, -HalfTop);
			}

			for (int i = -1, x = originX - (sixteen >> 4); HalfLeft < x; x = (--i * sixteen >> 4) + originX) {
				Line(x, HalfTop, x, -HalfTop);
			}

			for (int i = 0, y = originY; -y != HalfTop && y <= -HalfTop; y = (++i * sixteen >> 4) + originY) {
				Line(HalfLeft, y, -HalfLeft, y);
			}

			for (int i = -1, y = originY - (sixteen >> 4); HalfTop < y; y = (--i * sixteen >> 4) + originY) {
				Line(HalfLeft, y, -HalfLeft, y);
			}
		}

		if (_slowPaintStart == null) {
			var low = Project(camera, MinX, MinY);
			var high = Project(camera, MaxX, MaxY);
			surface.Line(low.X, low.Y, high.X, low.Y, BoundsColor);
			surface.Line(low.X, high.Y, low.X, low.Y, BoundsColor);
			surface.Line(high.X, high.Y, low.X, high.Y, BoundsColor);
			surface.Line(high.X, low.Y, high.X, high.Y, BoundsColor);
		}

		return drawn;
	}

	/// <summary>
	/// <c>ShellMap_PaintPath</c> (<c>0042670d</c>): with the whole path revealed, a line in <c>0x0e</c> through its points in
	/// order; part-way through the intro, only the line from the last point reached to the camera's
	/// centre, and a pen of radius <c>DAT_00471c5c</c> at the centre.
	/// </summary>
	private void PaintPath(ShellSurface surface, (int X, int Y, int Z) camera) {
		if (_path == null || _pathShown == 0) {
			return;
		}

		if (_pathShown == _path.Length) {
			var from = Project(camera, Point(_path[0]).X, Point(_path[0]).Y);
			for (int i = 1; i < _path.Length && i < _pathShown; i++) {
				var to = Project(camera, Point(_path[i]).X, Point(_path[i]).Y);
				surface.Line(from.X, from.Y, to.X, to.Y, PathColor);
				from = to;
			}

			return;
		}

		var last = Point(_path[Math.Min(_pathShown, _path.Length) - 1]);
		var start = Project(camera, last.X, last.Y);
		var pen = Project(camera, CameraX, CameraY);
		surface.Line(start.X, start.Y, pen.X, pen.Y, PathColor);
		surface.FillEllipse(pen.X, pen.Y, PenRadius, PenRadius, PathColor);
	}

	private const int PenRadius = 3;

	/// <summary>
	/// <c>ShellMap_PaintBases</c>: each base whose shown field allows it, as the <c>mis_icon.dba</c> frame and
	/// colour its type picks, at its point. A friendly base is shown while the field is non-zero and a
	/// hostile one only while it is 1; types below <c>0x18</c> and <c>0x2d</c>-<c>0x36</c> are friendly.
	/// </summary>
	private void PaintBases(ShellSurface surface, (int X, int Y, int Z) camera, ShellMapArt? art) {
		foreach (var site in _bases) {
			bool hostile = !(site.TypeIndex < 0x18 || (site.TypeIndex > 0x2c && site.TypeIndex < 0x37));
			if (!(hostile ? site.Shown == 1 : site.Shown != 0)) {
				continue;
			}

			var icon = BaseIcon(site.TypeIndex, hostile);
			var at = Point(site.Point);
			PaintIcon(surface, camera, at.X, at.Y, icon, art?.Icon(icon.Frame));
		}
	}

	/// <summary>A base's icon: its frame, colour, world size, drawn size and the size below which it is a single pixel.</summary>
	private readonly record struct MapIcon(int Frame, byte Color, int WorldSize, int DrawnSize, int PixelBelow);

	private static readonly MapIcon FacilityIcon = new(11, FriendlyColor, 50000, 10, 5);
	private static readonly MapIcon EnemyFacilityIcon = new(9, HostileColor, 50000, 10, 5);
	private static readonly MapIcon SmallFriendlyIcon = new(24, FriendlyColor, 50000, 5, 3);
	private static readonly MapIcon SmallHostileIcon = new(25, HostileColor, 50000, 5, 3);

	/// <summary>The type switch in <c>ShellMap_PaintBases</c>, its sizes and frames the shorts at <c>DAT_00471c5e</c> to <c>DAT_00471c82</c>.</summary>
	private static MapIcon BaseIcon(int type, bool hostile) {
		switch (type) {
			case 4 or 5 or 6 or 8 or 9 or 10 or 11 or 13 or 14 or 16 or 18 or 19 or 20:
				return FacilityIcon;
			case 0x1a or (>= 0x1c and <= 0x1e) or (>= 0x20 and <= 0x23) or 0x25 or 0x27 or 0x28:
				return EnemyFacilityIcon;
			case >= 0x2d and <= 0x36:
				return SmallFriendlyIcon;
			case >= 0x37 and <= 0x40:
				return SmallHostileIcon;
			default:
				return hostile ? new MapIcon(8, HostileColor, 50000, 16, 8) : new MapIcon(10, FriendlyColor, 50000, 16, 8);
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintIcon</c> (<c>00426c8c</c>): an icon at a world point. Its world size projected is its size on screen; below
	/// <see cref="MapIcon.PixelBelow"/> it is one pixel of its colour, below the frame's height the frame
	/// scaled to that size and centred, and otherwise the frame as it is, offset by half
	/// <see cref="MapIcon.DrawnSize"/>.
	/// </summary>
	private static void PaintIcon(ShellSurface surface, (int X, int Y, int Z) camera, int x, int y, MapIcon icon,
			DynamixBitmap? frame) {
		var at = Project(camera, x, y);
		int size = unchecked((icon.WorldSize << FocalShift) / camera.Z);
		if (size < icon.PixelBelow) {
			surface.Plot(at.X, at.Y, icon.Color);
		} else if (frame != null && size < frame.Rows) {
			surface.ScaledBlit(frame, at.X - (size >> 1), at.Y - (size >> 1), size, size);
		} else if (frame != null) {
			surface.Blit(frame, at.X - (icon.DrawnSize >> 1), at.Y - (icon.DrawnSize >> 1));
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintNavMarkers</c> (<c>00426d3f</c>): the path's points after the first, as <c>mis_icon.dba</c> frames 14 onward, as
	/// many as are revealed and at most nine, each offset by half of 13.
	/// </summary>
	private void PaintNavMarkers(ShellSurface surface, (int X, int Y, int Z) camera, ShellMapArt? art) {
		if (_path == null || _markersShown == 0) {
			return;
		}

		int count = Math.Min(_path.Length - 1, LastNavFrame - FirstNavFrame + 1);
		for (int i = 0; i < count && i < _markersShown; i++) {
			var point = Point(_path[i + 1]);
			var at = Project(camera, point.X, point.Y);
			if (art?.Icon(FirstNavFrame + i) is { } frame) {
				surface.Blit(frame, at.X - (NavMarkerSize >> 1), at.Y - (NavMarkerSize >> 1));
			}
		}
	}

	private const int FirstNavFrame = 14;
	private const int LastNavFrame = 22;
	private const int NavMarkerSize = 13;

	/// <summary>
	/// <c>ShellMap_PaintSquad</c> (<c>00426e5c</c>): the squad's revealed members, walking the twenty slots and counting only those
	/// whose member is not <c>-1</c>, each as frame 3 when the member is block-7 record 0 and frame 5
	/// otherwise.
	/// </summary>
	private void PaintSquad(ShellSurface surface, (int X, int Y, int Z) camera, ShellMapArt? art) {
		int shown = 0;
		for (int slot = 0; slot < SquadSlots && shown < _squadShown; slot++) {
			short member = _squadRefs[slot];
			if (member == -1) {
				continue;
			}

			int frame = member != 0 ? 5 : 3;
			PaintIcon(surface, camera, _squad[slot].X, _squad[slot].Y, new MapIcon(frame, HostileColor, 100000, 10, 5),
				art?.Icon(frame));
			shown++;
		}
	}

	// ---- The intro, ShellMap_IntroStep -----------------------------------------------------------------

	/// <summary><c>+0x172</c>. Named where the value has one meaning; the rest are the switch's own numbers.</summary>
	private enum State : short {
		Hold = 0,
		ZoomInStart = 1,
		ZoomIn = 2,
		SquadWait = 3,
		SquadNext = 4,
		NextStop = 5,
		PanStart = 6,
		Pan = 7,
		PanEnd = 8,
		LineAfterPan = 9,
		LineStep = 10,
		LineStepB = 11,
		LineTimed = 12,
		Wait = 13,
		WaitE = 14,
		WaitF = 15,
		Leftover = 16,
		ZoomOutStart = 17,
		ZoomOut = 18,
		Done = 19,
	}

	/// <summary><c>DAT_00471c44</c> and <c>DAT_00471c48</c>: how long the full view holds, and how long each zoom takes, in timer ticks.</summary>
	private const int HoldTicks = 60;
	private const int ZoomTicks = 60;

	/// <summary><c>DAT_00471c4c</c>, between squad members, and <c>DAT_00471c50</c>, the pauses around the path.</summary>
	private const int SquadTicks = 15;
	private const int PauseTicks = 15;

	/// <summary><c>DAT_00471c54</c> and <c>DAT_00471c58</c>: a pan takes 15 ticks for every whole 50000 of distance, and 15 more.</summary>
	private const int PanDistanceUnit = 50000;
	private const int PanTicksPerUnit = 15;

	/// <summary><c>DAT_00471c98</c> and <c>DAT_00471c9c</c>, the pause between lines and the time per character; both 0 in the image.</summary>
	private const int LineTicks = 0;
	private const int CharacterTicks = 0;

	/// <summary><c>DAT_00471c2c</c>, <c>DAT_00471c38</c> and <c>DAT_00471c40</c>: the first paint's waits after the clear, after the relief and after each grid line.</summary>
	private const int SlowPaintClearWait = 10;
	private const int SlowPaintReliefWait = 10;

	private State _state = State.Hold;
	private State _next;
	private uint _deadline;
	private uint? _slowPaintStart;
	private bool _skip;
	private bool _finished;
	private int _doneCountdown = 2;
	private int _squadShown;
	private int _pathShown;
	private int _markersShown;
	private int _textIndex;
	private int _stop;
	private int _stopsReached;
	private bool _textFlag;
	private int _panTicks;
	private (int X, int Y, int Z) _from;
	private (int X, int Y, int Z) _to;
	private (int X, int Y, int Z) _step;

	/// <summary>Whether the intro still has frames to run — <c>ShellMap_IntroStep</c>'s return, false once <c>+0x17a</c> is set.</summary>
	public bool IntroRunning => !_finished;

	/// <summary>
	/// A click or <c>Esc</c> or <c>Space</c> during the intro, which <c>ShellMap_SkipIntro</c> (<c>004253ef</c>) answers by jumping
	/// to the closing zoom. It waits until the intro's first paint has finished, as the original's does.
	/// </summary>
	public void Skip() => _skip = true;

	/// <summary>
	/// One pass of <c>ShellMap_IntroStep</c>, the intro's state machine, at <paramref name="now"/> in the
	/// original's timer ticks (<c>GetTickCount() &gt;&gt; 4</c>). The caller paints after every pass.
	/// Returns whether the intro is still running.
	/// </summary>
	public bool Advance(uint now) {
		if (_slowPaintStart is { } start) {
			if (now - start < SlowPaintClearWait + SlowPaintReliefWait + GridLineCount()) {
				return true;
			}

			_slowPaintStart = null;
		}

		if (_skip && _state < State.ZoomOut) {
			_state = State.ZoomOutStart;
		}

		_skip = false;
		Step(now);
		return !_finished;
	}

	private int GridLineCount() {
		var scratch = new ShellSurface(1, 1);
		return PaintGrid(scratch, Clamp((CameraX, CameraY, CameraZ)), 0);
	}

	private void Step(uint now) {
		switch (_state) {
			case State.Hold:
				if (_deadline == 0) {
					_deadline = now + HoldTicks;
					(CameraX, CameraY, CameraZ) = FullView;
					_slowPaintStart = now;
					return;
				}

				if (now < _deadline) {
					return;
				}

				_state = State.ZoomInStart;
				goto case State.ZoomInStart;
			case State.ZoomInStart:
				_deadline += ZoomTicks;
				StartMove(SquadView, ZoomTicks);
				_state = State.ZoomIn;
				goto case State.ZoomIn;
			case State.ZoomIn:
				if (!Move(now, ZoomTicks)) {
					_state = State.SquadNext;
				}

				return;
			case State.SquadWait:
				if (now < _deadline) {
					return;
				}

				_squadShown++;
				goto case State.SquadNext;
			case State.SquadNext:
				if (_squadShown < _squadCount) {
					_deadline = now + SquadTicks;
					_state = State.SquadWait;
				} else {
					_stopsReached = 1;
					_textIndex = 0;
					_stop = 0;
					_next = State.NextStop;
					_deadline = now + PauseTicks;
					_state = State.WaitF;
				}

				return;
			case State.NextStop:
				if (_textCount <= _textIndex) {
					goto case State.PanStart;
				}

				if (_path == null || _stopsReached < _path.Length) {
					_deadline = now + LineTicks;
					_next = State.PanStart;
					_state = State.Wait;
					return;
				}

				_textIndex--;
				_textFlag = true;
				goto case State.Leftover;
			case State.Leftover:
				if (_textIndex < _textCount) {
					if (!_textFlag) {
						_textIndex++;
						_next = State.Leftover;
						_state = State.LineTimed;
						_textFlag = true;
					} else {
						_deadline = now + LineTicks;
						_next = State.Leftover;
						_state = State.WaitE;
						_textFlag = false;
					}
				} else {
					_state = State.NextStop;
				}

				return;
			case State.PanStart:
				_pathShown++;
				_markersShown++;
				_stop++;
				_stopsReached++;
				if (_path == null || _path.Length < _stopsReached) {
					_state = State.ZoomOutStart;
					return;
				}

				var target = Point(_path[Math.Min(_stop, _path.Length - 1)]);
				int distance = SimMath.FastMagnitude3D(CameraX - target.X, CameraY - target.Y, 0);
				_panTicks = (distance / PanDistanceUnit + 1) * PanTicksPerUnit;
				StartMove((target.X, target.Y, CameraZ), _panTicks);
				_deadline = now + (uint)_panTicks;
				_state = State.Pan;
				goto case State.Pan;
			case State.Pan:
				if (Move(now, _panTicks)) {
					return;
				}

				_state = State.PanEnd;
				goto case State.PanEnd;
			case State.PanEnd:
				// With no time per character, a line never outlasts the pan and this waits for nothing.
				goto case State.LineAfterPan;
			case State.LineAfterPan:
				_textIndex++;
				if (_textIndex < _textCount) {
					_deadline = now + LineTicks;
					_next = State.LineStep;
					_state = State.WaitE;
				} else if (_textIndex == _textCount && _path != null && _stopsReached < _path.Length) {
					_textIndex--;
					_state = State.NextStop;
				} else {
					_deadline = now + PauseTicks;
					_next = State.LineStep;
					_state = State.WaitF;
				}

				return;
			case State.LineStep:
				if (_textIndex < _textCount) {
					_next = State.LineStepB;
					_state = State.LineTimed;
					return;
				}

				goto case State.LineStepB;
			case State.LineStepB:
				if (_textIndex < _textCount) {
					_textIndex++;
					if (_textIndex < _textCount - 1) {
						_deadline = now + LineTicks;
						_next = State.NextStop;
						_state = State.WaitE;
					} else {
						_state = State.NextStop;
					}
				} else {
					_state = State.NextStop;
				}

				return;
			case State.LineTimed:
				if (_textIndex < _textCount) {
					_deadline = now + CharacterTicks;
					_state = State.Wait;
				}

				goto case State.Wait;
			case State.Wait:
			case State.WaitE:
			case State.WaitF:
				if (now >= _deadline) {
					_state = _next;
				}

				return;
			case State.ZoomOutStart:
				_squadShown = (byte)_squadCount;
				if (_path != null) {
					_pathShown = (byte)_path.Length;
					_markersShown = (byte)(_pathShown - 1);
				}

				_deadline = now + ZoomTicks;
				StartMove(FullView, ZoomTicks);
				_state = State.ZoomOut;
				goto case State.ZoomOut;
			case State.ZoomOut:
				if (!Move(now, ZoomTicks)) {
					_state = State.Done;
				}

				return;
			case State.Done:
				if (!_finished && --_doneCountdown == 0) {
					_finished = true;
				}

				return;
		}
	}

	/// <summary>A move to <paramref name="to"/> over <paramref name="ticks"/>, stepped by whole-tick fractions computed once, as the originals are.</summary>
	private void StartMove((int X, int Y, int Z) to, int ticks) {
		_from = (CameraX, CameraY, CameraZ);
		_to = to;
		_step = ticks == 0 ? (0, 0, 0) : ((to.X - _from.X) / ticks, (to.Y - _from.Y) / ticks, (to.Z - _from.Z) / ticks);
	}

	/// <summary>Where the move is at <paramref name="now"/>, landing exactly on its target once its deadline passes. Returns whether it is still under way.</summary>
	private bool Move(uint now, int ticks) {
		if (now < _deadline) {
			int elapsed = ticks - (int)(_deadline - now);
			(CameraX, CameraY, CameraZ) = (_from.X + _step.X * elapsed, _from.Y + _step.Y * elapsed, _from.Z + _step.Z * elapsed);
			return true;
		}

		(CameraX, CameraY, CameraZ) = _to;
		return false;
	}
}

/// <summary>The map's icons, <c>dba\mis_icon.dba</c>, which <c>ShellMap_EnsureResourcesLoaded</c> (<c>00423db4</c>) loads.</summary>
public sealed class ShellMapArt {
	private readonly DynamixBitmap[]? _icons;

	private ShellMapArt(DynamixBitmap[]? icons) => _icons = icons;

	public DynamixBitmap? Icon(int frame) => _icons is { } icons && frame >= 0 && frame < icons.Length ? icons[frame] : null;

	public static ShellMapArt Load(GameContent content) => new(ShellArt.ReadBankFrames(content, "MIS_ICON"));
}

/// <summary>
/// One zone's heights as the map reads them: <c>dat\zone%d.dat</c>'s cell shift and <c>dba\zone%d.dba</c>'s
/// first frame, a byte per cell with the bitmap's rows running north to south (<c>HeightGrid_FromBitmap</c> (<c>00428d5b</c>)).
/// </summary>
public sealed class ZoneRelief {
	private readonly byte[] _pixels;

	private ZoneRelief(int widthShift, int cellShift, int rows, byte[] pixels) {
		WidthShift = widthShift;
		CellShift = cellShift;
		Rows = rows;
		_pixels = pixels;
	}

	/// <summary><c>+0xfc</c>, log2 of the bitmap's width, which the relief uses for both sides.</summary>
	public int WidthShift { get; }

	/// <summary><c>+0x104</c>, log2 of a cell's world size.</summary>
	public int CellShift { get; }

	public int Rows { get; }

	private int Size => 1 << WidthShift;

	public static ZoneRelief? Load(GameContent content, int zone) {
		var header = new ZoneDatTransformer().Parse(content.Read("dat", $"ZONE{zone}.DAT"));
		if (header == null || ShellArt.ReadBankFrames(content, $"ZONE{zone}") is not { Length: > 0 } frames
				|| frames[0].ImageData is not { } pixels) {
			return null;
		}

		int widthShift = 0;
		while (1 << widthShift < frames[0].Cols) {
			widthShift++;
		}

		return new ZoneRelief(widthShift, header.CellShift, frames[0].Rows, pixels);
	}

	/// <summary>
	/// The height of cell (<paramref name="x"/>, <paramref name="y"/>): row <paramref name="y"/> counts up
	/// from the bitmap's last row. A read past the grid, which the original makes into the heap, is 0.
	/// </summary>
	private int Height(int x, int y) {
		int row = Rows - 1 - y;
		int at = row * Size + x;
		return x >= 0 && y >= 0 && row >= 0 && at < _pixels.Length ? _pixels[at] : 0;
	}

	/// <summary>
	/// <c>ShellMap_BuildRelief</c> (<c>00426fe0</c>): the cells under the bounds widened by the relief margin, drawn as two banded
	/// triangles each at a whole number of pixels per cell — the most that fits 640 by 400 — into a bitmap
	/// a cell wider and taller than the triangles fill. Each corner's colour is its height's step on the
	/// <c>0xd1</c> ramp; a cell off the grid has all four corners at 0.
	/// </summary>
	internal ShellSurface BuildRelief((int MinX, int MinY, int MaxX, int MaxY) bounds) {
		const int margin = 100000;
		int x0 = bounds.MinX - margin >> CellShift;
		int y0 = bounds.MinY - margin >> CellShift;
		int x1 = bounds.MaxX + margin >> CellShift;
		int y1 = bounds.MaxY + margin >> CellShift;
		int columns = x1 - x0 + 1;
		int rows = y1 - y0 + 1;
		int perCell = Math.Min(640 / columns, 400 / rows);
		var relief = new ShellSurface(columns * perCell, rows * perCell);

		int Color(int height) => Math.Min(height, 0x80 - 1) / (0x80 / 0x18) + 0xd2 - 1;

		int top = 0;
		for (int y = y1; y > y0; y--) {
			int bottom = top + perCell;
			int left = 0;
			for (int x = x0; x < x1; x++) {
				int right = left + perCell;
				int h00 = 0, h01 = 0, h11 = 0, h10 = 0;
				if (y >= 0 && y < Rows && x >= 0 && x < Size) {
					h00 = Color(Height(x, y));
					h01 = Color(Height(x, y + 1));
					h11 = Color(Height(x + 1, y + 1));
					h10 = Color(Height(x + 1, y));
				}

				BandedTriangle(relief, left, bottom, h00, left, top, h01, right, top, h11);
				BandedTriangle(relief, left, bottom, h00, right, top, h11, right, bottom, h10);
				left = right;
			}

			top = bottom;
		}

		return relief;
	}

	/// <summary>
	/// <c>Gfx_BandedTriangle</c> (<c>00457aa8</c>), the 8-bit "Gouraud" triangle: not interpolated per pixel but cut into one flat
	/// band per palette index between its corners' colours. The edge from the highest-coloured corner to
	/// the lowest is divided into one step per index; the band between steps <c>i</c> and <c>i + 1</c> is
	/// filled with the highest colour less <c>i</c>, closed along whichever of the other two edges it
	/// spans. A triangle with two corners on one pixel draws nothing, and one whose corners share a
	/// colour is filled flat.
	/// </summary>
	internal static void BandedTriangle(ShellSurface surface, int ax, int ay, int ac, int bx, int by, int bc, int cx,
			int cy, int cc) {
		if ((ax == bx && ay == by) || (ax == cx && ay == cy) || (cx == bx && cy == by)) {
			return;
		}

		int[] xs = { ax, bx, cx };
		int[] ys = { ay, by, cy };
		int[] cs = { ac, bc, cc };
		int high = Math.Max(Math.Max(ac, bc), cc);
		int low = Math.Min(Math.Min(ac, bc), cc);
		if (high == low) {
			surface.FillConvex(new[] { ax, ay, bx, by, cx, cy }, (byte)high);
			return;
		}

		int hi = 0, lo = 0;
		for (int i = 0; i < 3; i++) {
			if (cs[i] == high) {
				hi = i;
			}

			if (cs[i] == low) {
				lo = i;
			}
		}

		int mid = (hi + lo) switch { 1 => 2, 2 => 1, _ => 0 };
		int midColor = cs[mid];

		int[] Steps(int from, int to, int count) {
			var points = new int[(count + 1) * 2];
			points[0] = xs[from];
			points[1] = ys[from];
			for (int i = 1; i < count; i++) {
				points[i * 2] = (xs[to] - xs[from]) * i / count + xs[from];
				points[i * 2 + 1] = (ys[to] - ys[from]) * i / count + ys[from];
			}

			points[count * 2] = xs[to];
			points[count * 2 + 1] = ys[to];
			return points;
		}

		int total = high - low;
		int upper = high - midColor;
		int lower = midColor - low;
		var main = Steps(hi, lo, total);

		void Band(int[] side, int sideAt, int mainAt, int color) {
			surface.FillConvex(new[] {
				main[mainAt * 2], main[mainAt * 2 + 1], main[(mainAt + 1) * 2], main[(mainAt + 1) * 2 + 1],
				side[(sideAt + 1) * 2], side[(sideAt + 1) * 2 + 1], side[sideAt * 2], side[sideAt * 2 + 1],
			}, (byte)color);
		}

		if (upper == 0) {
			var side = Steps(mid, lo, lower);
			for (int i = 0; i < total; i++) {
				Band(side, i, i, high - i);
			}
		} else if (lower == 0) {
			var side = Steps(hi, mid, upper);
			for (int i = 0; i < total; i++) {
				Band(side, i, i, high - i);
			}
		} else {
			var first = Steps(hi, mid, upper);
			var second = Steps(mid, lo, lower);
			for (int i = 0; i < upper; i++) {
				Band(first, i, i, high - i);
			}

			for (int i = 0; i < lower; i++) {
				Band(second, i, upper + i, midColor - i);
			}
		}
	}
}
