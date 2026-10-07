using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Engine.Shell;

/// <summary>
/// Drawing the map — vtable slot 0, <c>ShellMap_Paint</c>, and its passes: the relief, the grid and
/// bounds, the nav path, the bases, the nav markers and the squad, each projected through the
/// camera the paint settles on.
/// </summary>
public sealed partial class ShellMap {
	/// <summary><c>DAT_00471c3c</c>, the world distance between grid lines.</summary>
	private const int GridSpacing = 200000;

	private const byte ClearColor = 0x10;
	private const byte GridColor = 0x0f;
	private const byte BoundsColor = 10;
	private const byte PathColor = 0x0e;
	private const byte FriendlyColor = 10;
	private const byte HostileColor = 0x21;

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
}
