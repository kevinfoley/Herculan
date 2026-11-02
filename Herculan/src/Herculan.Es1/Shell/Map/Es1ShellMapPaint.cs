using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// <c>ShellMap_Paint</c> (<c>00009ca7</c>), slot <c>+4</c> of <c>ShellMapVtable</c>, and its steps
/// (docs/es1/shell/map.md#a-paint), each drawn on the display's draw page. Each draw is cut to the map's rect, both
/// edges included, as the viewport's clip mode 1 cuts it. The clear and the grid set the viewport's translation flag
/// <c>+0x20c</c> and draw at view points about its centre (<see cref="Centred"/>); the other steps clear it and draw at
/// screen points (<see cref="OnScreen"/>). <c>Map_PushContext</c> (<c>00008f65</c>) and <c>Map_PopContext</c>
/// (<c>00008f86</c>), which install the viewport around a paint and put the kept context back, are these canvases here,
/// made for each paint.
///
/// <para>The slow paint's waits (<see cref="Es1ShellMap.SlowPaint"/>) spin on <c>ShellMap_Ticks</c> in retail; here the
/// shell's work stands at each (<see cref="Es1ShellWait"/>), so what is drawn on the visible page so far shows.</para>
/// </summary>
public static class Es1ShellMapPaint {
	/// <summary>The clear's color, <c>0x36</c>.</summary>
	public const byte ClearColor = 0x36;

	/// <summary>The grid's pen, <c>0x37</c>.</summary>
	public const byte GridColor = 0x37;

	/// <summary>The bounds' outline and a side-1 base's pixel, <c>0x35</c>.</summary>
	public const byte BoundsColor = 0x35;

	/// <summary>The nav path's pen and brush, <c>0x38</c>.</summary>
	public const byte PathColor = 0x38;

	/// <summary>The pixel of a base on another side and of a squad slot, <c>0x32</c>.</summary>
	public const byte IconColor = 0x32;

	/// <summary>The callout's frame and pointer, <c>0x3b</c>.</summary>
	public const byte CalloutColor = 0x3b;

	/// <summary>The one entry of the zone's shapes' ramp, the <c>int16</c> <c>0x38</c> at <c>0007e330</c>.</summary>
	public const short ShapeRampColor = 0x38;

	/// <summary>The intro's last state, in which the lines fill the left side and the callout is drawn.</summary>
	private const short LastState = 0x13;

	/// <summary>
	/// <c>ShellMap_Paint</c>: the camera installed (<see cref="Es1ShellMap.InstallCamera"/>), then the clear, the zone's
	/// shapes, the grid and bounds, the nav path, the bases, the nav markers, the squad, the objective lines and the
	/// callout.
	/// </summary>
	public static IEnumerable<Es1ShellWait> Paint(Es1ShellMap map, Es1VideoMemory display) {
		map.InstallCamera();
		var centred = Centred(map, display);
		foreach (var wait in Clear(map, centred)) {
			yield return wait;
		}

		foreach (var wait in PaintShapes(map, display)) {
			yield return wait;
		}

		foreach (var wait in PaintGrid(map, centred)) {
			yield return wait;
		}

		var screen = OnScreen(map, display);
		PaintPath(map, screen);
		PaintBases(map, screen);
		PaintNavMarkers(map, screen);
		PaintSquad(map, screen);
		CurrentLine(map, screen);
		PaintCallout(map, screen);
	}

	/// <summary>
	/// <c>ShellMap_ResetViewAndClear</c> (<c>00009c64</c>), ES2's, which <c>Shell_DrawScreen</c> runs when it draws the war
	/// map's window (docs/es1/shell/map.md#the-tick): the camera at the full view, its pan and the gadgets' rects as they
	/// were; <see cref="Clear"/> in the viewport; and <see cref="Es1ShellMap.RedrawDue"/> set.
	/// </summary>
	public static IEnumerable<Es1ShellWait> ResetViewAndClear(Es1ShellMap map, Es1VideoMemory display) {
		(map.CameraX, map.CameraY, map.Altitude) = map.FullView;
		foreach (var wait in Clear(map, Centred(map, display))) {
			yield return wait;
		}

		map.RedrawDue = true;
	}

	/// <summary>The viewport with its translation flag set: a point is the view point about its centre, <see cref="Es1ShellMap.ViewportCentre"/> its origin.</summary>
	private static Es1PanelCanvas Centred(Es1ShellMap map, Es1VideoMemory display) => new(display, map.Rect, map.ViewportCentre);

	/// <summary>The viewport with its translation flag clear: a point is the screen's.</summary>
	private static Es1PanelCanvas OnScreen(Es1ShellMap map, Es1VideoMemory display) => new(display, map.Rect, (map.Rect.X0, map.Rect.Y0));

	/// <summary>
	/// <c>ShellMap_Clear</c> (<c>0000aa3c</c>): <c>Raster_FillRect</c> under the brush {0, <see cref="ClearColor"/>} of the
	/// rect from the viewport's centre <c>+0x220</c>/<c>+0x224</c> to that plus the width and height, the whole viewport;
	/// in the slow paint, then <see cref="Es1ShellMapBnd.ClearWait"/> ticks.
	/// </summary>
	private static IEnumerable<Es1ShellWait> Clear(Es1ShellMap map, Es1PanelCanvas canvas) {
		var (left, top) = map.ViewportCentre;
		canvas.FillRect(left, top, unchecked(left + map.Width), unchecked(top + map.Height), ClearColor);
		if (map.SlowPaint) {
			foreach (var wait in WaitTicks(map, map.Bnd.ClearWait)) {
				yield return wait;
			}
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintShapes</c> (<c>0000aff0</c>) (docs/es1/shell/map.md#the-shapes): with
	/// <see cref="Es1ShellMap.DrawShapes"/>, ramp <see cref="Es1ShellMapBnd.ShapeRamp"/> made the one entry
	/// <see cref="ShapeRampColor"/>; then each zone object drawn through <see cref="Es1ShellShapes.RenderObject"/>, its
	/// instance's sequence-0 frame 1 for the draw when its shape's sequence 0 has frames and every frame 0 otherwise; in the
	/// slow paint, then <see cref="Es1ShellMapBnd.ShapesWait"/> ticks. An object whose shape index is past the theater's
	/// shapes, which retail reads past its array, is passed over, an engine guard.
	/// </summary>
	private static IEnumerable<Es1ShellWait> PaintShapes(Es1ShellMap map, Es1VideoMemory display) {
		var renderer = new Es1ShellShapes(map, display);
		if (map.DrawShapes) {
			renderer.SetColourRamp(map.Bnd.ShapeRamp, [ShapeRampColor]);
		}

		foreach (var obj in map.Objects) {
			if (!map.DrawShapes) {
				// PLACEHOLDER: the 2D map's object, dba\mapshp's first bitmap through Bitmap_BlitRotatedUnpacked (0005048c),
				// turned by the object's yaw; retail's SHELLMAP.BND never takes this branch.
				continue;
			}

			if (map.Shapes is not { } shapes || obj.ShapeIndex < 0 || obj.ShapeIndex >= shapes.Count) {
				continue;
			}

			var shape = shapes[obj.ShapeIndex];
			var frames = new short[shape.SequenceList?.Length ?? 0];
			if (frames.Length > 0 && shape.SequenceList![0] != 0) {
				frames[0] = 1;
			}

			renderer.RenderObject(shape, obj, frames);
		}

		if (map.SlowPaint) {
			foreach (var wait in WaitTicks(map, map.Bnd.ShapesWait)) {
				yield return wait;
			}
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintGrid</c> (<c>0000ab58</c>) (docs/es1/shell/map.md#a-paint), at view points: the spacing
	/// <see cref="Es1ShellMap.GridStep"/> the absolute <i>x</i> distance between the view points of the world origin and of
	/// (<see cref="Es1ShellMapBnd.GridSpacing"/>, the same, 0); with <i>o</i> the origin's view point, <i>s</i> the
	/// spacing and <i>L</i>, <i>T</i> the viewport's centre, the vertical lines in <see cref="GridColor"/> from <i>x</i> =
	/// <i>o.x</i> − ((<i>o.x</i> − <i>L</i>) / <i>s</i> + 1) × <i>s</i> while below −<i>L</i>, each from (<i>x</i>,
	/// −<i>T</i>) to (<i>x</i>, <i>T</i>), then the horizontal lines from <i>y</i> = <i>o.y</i> − ((<i>o.y</i> −
	/// <i>T</i>) / <i>s</i> + 1) × <i>s</i> while below −<i>T</i>, each from (−<i>L</i>, <i>y</i>) to (<i>L</i>,
	/// <i>y</i>), the slow paint waiting <see cref="Es1ShellMapBnd.GridLineWait"/> ticks after each; outside the slow paint,
	/// the bounds' outline, <c>Raster_FillRect</c> under the brush {4, <see cref="BoundsColor"/>} on the view points of the
	/// low and high corners at height 0. With a spacing of 0, where retail divides by 0, no line is drawn, an engine guard.
	/// </summary>
	private static IEnumerable<Es1ShellWait> PaintGrid(Es1ShellMap map, Es1PanelCanvas canvas) {
		int spacing = map.Bnd.GridSpacing;
		var origin = map.ViewPoint(default);
		var far = map.ViewPoint(new Es1MapPoint(spacing, spacing, 0));
		int step = unchecked(far.X - origin.X);
		map.GridStep = step < 0 ? unchecked(-step) : step;

		int s = map.GridStep;
		var (left, top) = map.ViewportCentre;
		if (s != 0) {
			unchecked {
				int x = origin.X - ((origin.X - left) / s + 1) * s;
				int y = origin.Y - ((origin.Y - top) / s + 1) * s;
				for (; x < -left; x += s) {
					canvas.DrawLine(x, -top, x, top, GridColor);
					foreach (var wait in GridLineWait(map)) {
						yield return wait;
					}
				}

				for (; y < -top; y += s) {
					canvas.DrawLine(-left, y, left, y, GridColor);
					foreach (var wait in GridLineWait(map)) {
						yield return wait;
					}
				}
			}
		}

		if (!map.SlowPaint) {
			var low = map.ViewPoint(new Es1MapPoint(map.MinX, map.MinY, 0));
			var high = map.ViewPoint(new Es1MapPoint(map.MaxX, map.MaxY, 0));
			canvas.OutlineRect(low.X, low.Y, high.X, high.Y, BoundsColor);
		}

		static IEnumerable<Es1ShellWait> GridLineWait(Es1ShellMap map) =>
			map.SlowPaint ? WaitTicks(map, map.Bnd.GridLineWait) : [];
	}

	/// <summary>
	/// <c>ShellMap_PaintPath</c> (<c>0000b4ec</c>) (docs/es1/shell/map.md#the-path): with a nav path and
	/// <see cref="Es1ShellMap.RevealedPath"/> not 0, under the pen {0, <see cref="PathColor"/>}: short of the path's
	/// waypoint count, a line from the last revealed ref's point to the camera's position at height 0, without the pan,
	/// and a filled ellipse under the brush {0, <see cref="PathColor"/>} on the box <see cref="Es1ShellMapBnd.PenRadius"/>
	/// pixels each way around that position; otherwise lines through the refs' points in order. A revealed count past the
	/// path's refs, which retail reads past them, takes the point of ref −1 here, an engine guard.
	/// </summary>
	private static void PaintPath(Es1ShellMap map, Es1PanelCanvas canvas) {
		if (map.NavPath is not { } path || map.RevealedPath == 0) {
			return;
		}

		if (map.RevealedPath != (ushort)path.Length) {
			int last = map.RevealedPath - 1;
			var from = map.Project(map.PointAt(last < path.Length ? path[last] : -1));
			var pen = map.Project(new Es1MapPoint(map.CameraX, map.CameraY, 0));
			canvas.DrawLine(from.X, from.Y, pen.X, pen.Y, PathColor);
			int radius = map.Bnd.PenRadius;
			canvas.DrawEllipse(unchecked(new Es1Rect(pen.X - radius, pen.Y - radius, pen.X + radius, pen.Y + radius)), PathColor, outline: false);
			return;
		}

		var previous = map.Project(map.PointAt(path.Length > 0 ? path[0] : -1));
		for (int i = 1; i < path.Length && i < map.RevealedPath; i++) {
			var next = map.Project(map.PointAt(path[i]));
			canvas.DrawLine(previous.X, previous.Y, next.X, next.Y, PathColor);
			previous = next;
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintBases</c> (<c>0000b840</c>) (docs/es1/shell/map.md#the-icons): each base whose side is not negative,
	/// and not 1 while <see cref="Es1ShellMap.HidesSide1Bases"/> is set, as <see cref="PaintIcon"/> of the frame its type
	/// and side pick at its point: the types 5, 6 and 8 the small base's frame, world size, size and pixel threshold, the
	/// others the base's; side 1 the side-1 frame and <see cref="BoundsColor"/>, any other side the other frame and
	/// <see cref="IconColor"/>.
	/// </summary>
	private static void PaintBases(Es1ShellMap map, Es1PanelCanvas canvas) {
		var bnd = map.Bnd;
		foreach (var structure in map.Bases) {
			if (structure.Side < 0 || (map.HidesSide1Bases && structure.Side == 1)) {
				continue;
			}

			bool sideOne = structure.Side == 1;
			byte color = sideOne ? BoundsColor : IconColor;
			if (structure.Type is 5 or 6 or 8) {
				PaintIcon(map, canvas, Icon(map, sideOne ? bnd.Side1SmallBaseFrame : bnd.SmallBaseFrame), color,
					bnd.SmallBasePixelBelow, bnd.SmallBaseGadgetSize, bnd.SmallBaseWorldSize, map.PointAt(structure.PointRef));
			} else {
				PaintIcon(map, canvas, Icon(map, sideOne ? bnd.Side1BaseFrame : bnd.BaseFrame), color,
					bnd.BasePixelBelow, bnd.BaseGadgetSize, bnd.BaseWorldSize, map.PointAt(structure.PointRef));
			}
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintIcon</c> (<c>0000b9cb</c>)(map, <paramref name="bitmap"/>, <paramref name="color"/>,
	/// <paramref name="below"/>, <paramref name="size"/>, <paramref name="worldSize"/>, <paramref name="point"/>)
	/// (docs/es1/shell/map.md#the-icons): with <i>p</i> the point's <see cref="Es1ShellMap.Project"/> and <i>z</i> =
	/// (world size &lt;&lt; <see cref="Es1ShellMap.FocalShift"/>) / the altitude, a <i>z</i> below <paramref name="below"/>
	/// is one pixel of the color at <i>p</i> through <c>Gfx_PlotPixel</c>; a <i>z</i> below the bitmap's rows draws it
	/// scaled to <i>z</i> by <i>z</i> (<c>Bitmap_BlitScaled</c>) at <i>p</i> less <i>z</i> &gt;&gt; 1; otherwise it is
	/// drawn as it is (<c>Bitmap_Blit</c>) at <i>p</i> less <paramref name="size"/> &gt;&gt; 1. An altitude of 0, where
	/// retail divides by 0, is taken as 1, and a missing frame, which retail reads through, draws no bitmap, engine guards.
	/// </summary>
	private static void PaintIcon(Es1ShellMap map, Es1PanelCanvas canvas, DynamixBitmap? bitmap, byte color, short below,
			short size, int worldSize, Es1MapPoint point) {
		var at = map.Project(point);
		int z = unchecked(worldSize << Es1ShellMap.FocalShift) / (map.Altitude == 0 ? 1 : map.Altitude);
		if (z < below) {
			canvas.Plot(at.X, at.Y, color);
		} else if (bitmap == null) {
			return;
		} else if (z < bitmap.Rows) {
			int half = z >> 1;
			canvas.BlitScaled(bitmap, z, z, 0, at.X - half, at.Y - half);
		} else {
			canvas.Blit(bitmap, at.X - (size >> 1), at.Y - (size >> 1));
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintNavMarkers</c> (<c>0000bb1c</c>): with a nav path and <see cref="Es1ShellMap.RevealedMarkers"/>
	/// not 0, the path's points after the first, at most the frames from <see cref="Es1ShellMapBnd.FirstMarkerFrame"/> to
	/// <see cref="Es1ShellMapBnd.LastMarkerFrame"/>, the waypoint count less 1 and the revealed count, each frame in turn
	/// drawn as it is at its point less half of <see cref="Es1ShellMapBnd.MarkerSize"/>.
	/// </summary>
	private static void PaintNavMarkers(Es1ShellMap map, Es1PanelCanvas canvas) {
		if (map.NavPath is not { } path || map.RevealedMarkers == 0) {
			return;
		}

		var bnd = map.Bnd;
		int count = Math.Min(path.Length - 1, bnd.LastMarkerFrame - bnd.FirstMarkerFrame + 1);
		for (int i = 0; i < count && i < map.RevealedMarkers; i++) {
			var at = map.Project(map.PointAt(path[i + 1]));
			int half = bnd.MarkerSize >> 1;
			canvas.Blit(Icon(map, bnd.FirstMarkerFrame + i), at.X - half, at.Y - half);
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintSquad</c> (<c>0000bc47</c>): with <see cref="Es1ShellMap.RevealedSquad"/> not 0, the squad's slots in
	/// order while fewer than that many have been drawn, each whose member ref is not −1 as <see cref="PaintIcon"/> of
	/// <see cref="Es1ShellMapBnd.PlayerFrame"/> for ref 0 and <see cref="Es1ShellMapBnd.SquadmateFrame"/> for any other, in
	/// <see cref="IconColor"/> with the squad's world size, size and pixel threshold, at the slot's position. The pen
	/// {0, <see cref="CalloutColor"/>} it installs first draws nothing, and the mech record it addresses for each slot is
	/// not read.
	/// </summary>
	private static void PaintSquad(Es1ShellMap map, Es1PanelCanvas canvas) {
		if (map.RevealedSquad == 0 || map.Squad is not { } squad) {
			return;
		}

		var bnd = map.Bnd;
		int shown = 0;
		for (int slot = 0; slot < Es1ShellMap.SquadSlots && shown < map.RevealedSquad; slot++) {
			short member = squad.MemberRefs[slot];
			if (member == -1) {
				continue;
			}

			var frame = Icon(map, member == 0 ? bnd.PlayerFrame : bnd.SquadmateFrame);
			PaintIcon(map, canvas, frame, IconColor, bnd.SquadPixelBelow, bnd.SquadGadgetSize, bnd.SquadWorldSize,
				map.SquadPositions[slot]);
			shown++;
		}
	}

	/// <summary>
	/// <c>ShellMap_CurrentLine</c> (<c>0000bd83</c>) (docs/es1/shell/map.md#the-lines), by the intro's state through
	/// <c>ShellMap_CurrentLineCases</c> (<c>0007e334</c>): in 7 and <c>0xd</c> with the line below the count, the top label
	/// gets the line, kept in <see cref="Es1ShellMap.KeptLine"/>, or for an empty line the kept one or nothing; in
	/// <c>0xe</c>, nothing for a line below the count that is not empty, otherwise the kept one or nothing; in
	/// <c>0x13</c>, each line that is not empty into the next of the <see cref="Es1ShellMap.ObjectiveLabels"/>. Retail's
	/// last loop has no bound at the labels' count and writes past them; here a line past the last label is passed over,
	/// an engine guard.
	/// </summary>
	private static void CurrentLine(Es1ShellMap map, Es1PanelCanvas canvas) {
		var lines = map.ObjectiveLines;
		switch (map.IntroState) {
			case 7 or 0xd:
				if (map.IntroLine < lines.Count) {
					string line = lines[map.IntroLine];
					if (line.Length != 0) {
						map.KeptLine = line;
					} else {
						line = map.KeptLine ?? "";
					}

					map.TopLabel.SetText(canvas, line);
				}

				break;
			case 0xe:
				bool showing = map.IntroLine < lines.Count && lines[map.IntroLine].Length != 0;
				map.TopLabel.SetText(canvas, showing ? "" : map.KeptLine ?? "");
				break;
			case LastState:
				int label = 0;
				foreach (string text in lines) {
					if (text.Length != 0 && label < map.ObjectiveLabels.Count) {
						map.ObjectiveLabels[label++].SetText(canvas, text);
					}
				}

				break;
		}
	}

	/// <summary>
	/// <c>ShellMap_PaintCallout</c> (<c>0000bf1b</c>) (docs/es1/shell/map.md#picking-a-gadget): in the intro's last state with
	/// a gadget selected, its text set in <see cref="Es1ShellMap.CalloutLabel"/>, which fills its box first; the label's
	/// rect framed under the brush {4, <see cref="CalloutColor"/>}; and the pointer <see cref="Es1ShellMap.CalloutPointer"/>
	/// filled under the brush {0, <see cref="CalloutColor"/>} (<c>Raster_DrawPolygonDispatch</c>(3, pointer)). A selected
	/// index past the gadgets, which retail reads past its array, draws nothing, an engine guard.
	/// </summary>
	private static void PaintCallout(Es1ShellMap map, Es1PanelCanvas canvas) {
		if (map.IntroState != LastState || map.SelectedGadget == -1) {
			return;
		}

		if (map.SelectedGadget < 0 || map.SelectedGadget >= map.Gadgets.Count) {
			return;
		}

		var label = map.CalloutLabel;
		label.SetText(canvas, map.Gadgets[map.SelectedGadget].Text);
		canvas.OutlineRect(label.Rect.X0, label.Rect.Y0, label.Rect.X1, label.Rect.Y1, CalloutColor);
		canvas.FillPolygon(map.CalloutPointer, CalloutColor);
	}

	/// <summary>
	/// Frame <paramref name="frame"/> of <c>dba\icons</c> (<see cref="Es1ShellMapResources.Icons"/>); none without the array
	/// or for a frame past it, which retail reads past its array, an engine guard.
	/// </summary>
	private static DynamixBitmap? Icon(Es1ShellMap map, int frame) =>
		map.Resources?.Icons?.Images is { } images && frame >= 0 && frame < images.Length ? images[frame] : null;

	/// <summary>
	/// A slow-paint wait: until <c>ShellMap_Ticks</c>, an unsigned count, reaches its value now plus
	/// <paramref name="ticks"/>. Without the map's resources there is no clock and nothing waits.
	/// </summary>
	internal static IEnumerable<Es1ShellWait> WaitTicks(Es1ShellMap map, int ticks) {
		if (map.Resources is not { } clock) {
			yield break;
		}

		uint deadline = unchecked((uint)(clock.Ticks + ticks));
		if ((uint)clock.Ticks < deadline) {
			yield return Es1ShellWait.For(() => (uint)clock.Ticks >= deadline);
		}
	}
}
