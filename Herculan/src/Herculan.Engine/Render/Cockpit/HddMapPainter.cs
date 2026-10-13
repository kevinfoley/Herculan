using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The Heads-Down Display command display's tactical map — see <see cref="DrawHddMap"/>.
/// </summary>
internal sealed class HddMapPainter {
	private readonly Overlay2DRenderer _overlay;

	public HddMapPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
	}

	/// <summary>
	/// The command display's map, in the order <c>HddCommandScreen_DrawMap</c> (<c>0044e30c</c>) draws it: the terrain raster, the
	/// 1200-metre grid, the mission border, then every marker.
	///
	/// <para>The whole thing is clipped by a GL scissor set to the map viewport. That is the direct
	/// analogue of the original's arrangement, which renders into a <c>0x239</c>-byte offscreen target
	/// sized to the viewport and blits the result — the raster covers the entire mission box, so
	/// without the clip a zoomed-in map would paint over the order column and the console around
	/// it.</para>
	/// </summary>
	internal void DrawHddMap(CockpitArt hud, HddLayout layout, HudSpriteSheet sprites,
			GpuTexture spriteTexture, GpuTexture? mapTexture, CockpitHudState state, HddMapView view,
			float scale, float quadX0, int viewportX, int viewportY, int viewportHeight) {
		var region = layout.MapViewport;
		float Dx(float x) => quadX0 + x * scale;
		float Dy(float y) => y * scale;

		// Scissor coordinates are framebuffer pixels with the origin at the bottom-left, which is the
		// frame the viewport was set in; the overlay's own pixel space runs the other way down.
		float left = Dx(region.X0);
		float right = Dx(region.X1 + 1);
		float top = Dy(region.Y0);
		float bottom = Dy(region.Y1 + 1);
		int scissorX = viewportX + (int)MathF.Floor(left);
		int scissorY = viewportY + (int)MathF.Floor(viewportHeight - bottom);
		int scissorW = Math.Max((int)MathF.Ceiling(right - left), 0);
		int scissorH = Math.Max((int)MathF.Ceiling(bottom - top), 0);
		if (scissorW == 0 || scissorH == 0) {
			return;
		}

		_overlay.SetScissor(scissorX, scissorY, scissorW, scissorH);

		// Viewport-local device pixels, which is the space HddMapView projects into. Every size on the map is in
		// device pixels too — an icon, a marker's offsets, a line's one pixel — so each is multiplied by `scale`
		// as the positions are, or the map shrinks against the console as the window grows.
		float Mx(float x) => Dx(region.X0 + x);
		float My(float y) => Dy(region.Y0 + y);
		float Px(int worldX) => Mx(view.ToScreenX(worldX));
		float Py(int worldY) => My(view.ToScreenY(worldY));

		if (mapTexture != null && state.Command.Raster is { } raster) {
			_overlay.Clear();
			_overlay.AddTexturedQuad(Px(raster.WorldX0), Py(raster.WorldY1), Px(raster.WorldX1), Py(raster.WorldY0),
				0f, 0f, 1f, 1f);
			_overlay.Submit(mapTexture);
		}

		_overlay.Clear();

		// One device pixel, which a line on the map is.
		float pixel = Math.Max(scale, 1f);

		// The grid: lines every HddMap.GridPitch world units either side of the world origin, walked
		// out from it until they leave the viewport. The original steps in projected pixels and
		// divides by sixteen; stepping in world units and projecting each line is the same set of
		// lines without the accumulated rounding.
		if (hud.LogicalColor(HddMap.GridColorId) is { } gridColor) {
			int halfX = view.HalfWorldWidth + HddMap.GridPitch;
			int halfY = view.HalfWorldHeight + HddMap.GridPitch;
			for (int worldX = FloorToPitch(view.CentreX - halfX);
					worldX <= view.CentreX + halfX; worldX += HddMap.GridPitch) {
				float x = Px(worldX);
				_overlay.AddFilledRect(x, My(0), x + pixel, My(region.Height), gridColor);
			}

			for (int worldY = FloorToPitch(view.CentreY - halfY);
					worldY <= view.CentreY + halfY; worldY += HddMap.GridPitch) {
				float y = Py(worldY);
				_overlay.AddFilledRect(Mx(0), y, Mx(region.Width), y + pixel, gridColor);
			}
		}

		// The mission border: the block-1 bounding box the screen keeps in its own +0x160 rect, drawn
		// through the brush mode Raster_FillRect answers by walking the four edges as lines rather
		// than by filling the interior.
		var bounds = view.Bounds;
		if (!bounds.IsEmpty && hud.LogicalColor(HddMap.BorderColorId) is { } borderColor) {
			_overlay.AddRectOutline(Px(bounds.MinX), Py(bounds.MaxY), Px(bounds.MaxX), Py(bounds.MinY),
				scale, borderColor);
		}

		var markers = state.Command.Plotted;
		for (int i = 0; i < markers.Count; i++) {
			// The selected pilot's marker blinks on the display's own half-second toggle — see
			// "The selected pilot's marker" in docs/retail/simulation/heads-down-display.md.
			if (!state.Command.Blink && markers[i].PilotSlot == state.Command.SelectedPilot
				&& state.Command.SelectedPilot >= 0) {
				continue;
			}

			AddHddMarker(hud, sprites, markers[i], view, Px, Py, scale);
		}

		// The link the manual describes: a line from the selected pilot to whatever the order being built has been
		// pointed at, in that pilot's own colour, or to the last order's pick in the palette entry one below it —
		// HddCommandScreen_DrawMarker's HudColorTable_Get(slot) - 1.
		int slot = state.Command.SelectedPilot;
		if (slot >= 0 && PilotMarker(markers, slot) is { } from && state.Command.LinkTarget is { } target
			&& LinkColor(hud, slot, state.Command.LinkSaved) is { } linkColor) {
			_overlay.AddLine(Px(from.WorldX), Py(from.WorldY), Px(target.X), Py(target.Y), linkColor, pixel);
		}

		// The order's chosen unit is boxed after every marker, by HddCommandScreen_DrawMap itself: a frame
		// two pixels out from the marker's drawn half-size on the top and left and one on the bottom and right,
		// centred on the object rather than on the icon. A unit's marker has no drawn half-size, so its box is
		// a four-pixel square sitting on the object. It is placed in the viewport, not the world, so once the
		// marker stops being drawn the box stays put on screen while the map moves under it.
		if (state.Command.ChosenBox is { } box && hud.LogicalColor(HddMap.ChosenUnitColorId) is { } outline) {
			float cx = Mx(box.X);
			float cy = My(box.Y);
			_overlay.AddRectOutline(cx - (box.Half + 2) * scale, cy - (box.Half + 2) * scale,
				cx + (box.Half + 2) * scale, cy + (box.Half + 2) * scale, scale, outline);
		}

		if (_overlay.VertexCount > 0) {
			_overlay.Submit(spriteTexture);
		}

		_overlay.ClearScissor();

		static int FloorToPitch(int world) =>
			(int)Math.Floor(world / (double)HddMap.GridPitch) * HddMap.GridPitch;
	}

	private static Vector3? LinkColor(CockpitArt hud, int slot, bool saved) {
		int id = HudColorTable.PilotColorId(slot);
		if (!saved) {
			return hud.LogicalColor(id);
		}

		return hud.Colors?.PaletteIndex(id) is { } index ? hud.PaletteEntry(index - 1) : null;
	}

	/// <summary>The marker belonging to squad slot <paramref name="slot"/>, or null.</summary>
	private static HddMapMarker? PilotMarker(IReadOnlyList<HddMapMarker> markers, int slot) {
		foreach (var marker in markers) {
			if (marker.PilotSlot == slot) {
				return marker;
			}
		}

		return null;
	}

	/// <summary>
	/// One map marker — <c>HddMarker_Paint</c> (<c>0044f194</c>). An icon is blitted with its own rotation nudge, offset
	/// back by half the marker's size so it lands on the object. A <see cref="HddMapMarker.Ranged"/> one draws a
	/// box of its <see cref="HddMap.Apparent"/> size instead whenever the icon would be the bigger of the two: filled in
	/// id 16, then framed in the marker's own colour. A radar mast is then ringed at
	/// <see cref="HddMap.RadarRingRange"/>, about a point a quarter of its drawn size up and left of the object —
	/// the original's offset, which puts a large structure's ring on its icon's corner.
	/// </summary>
	private void AddHddMarker(CockpitArt hud, HudSpriteSheet sprites, HddMapMarker marker,
			HddMapView view, Func<int, float> px, Func<int, float> py, float scale) {
		float centerX = px(marker.WorldX);
		float centerY = py(marker.WorldY);
		var sprite = sprites.Sprite(HddMap.IconBank, marker.Frame);
		var color = MarkerColor(hud, marker);
		int half = HddMap.DrawnHalf(marker, view, HddMap.IconHeight(sprite));

		if (HddMap.BoxedAt(marker, view, HddMap.IconHeight(sprite)) is { } apparent) {
			int boxHalf = apparent >> 1;
			float x0 = centerX - boxHalf * scale;
			float y0 = centerY - boxHalf * scale;
			float x1 = centerX + (boxHalf + 1) * scale;
			float y1 = centerY + (boxHalf + 1) * scale;
			if (hud.LogicalColor(HddMap.ChosenUnitColorId) is { } fill) {
				_overlay.AddFilledRect(x0, y0, x1, y1, fill);
			}

			if (color is { } frame) {
				_overlay.AddRectOutline(x0, y0, x1, y1, scale, frame);
			}
		} else if (sprite is { Width: > 0, Height: > 0 } icon) {
			float x = centerX + (marker.NudgeX - marker.Size / 2f) * scale;
			float y = centerY + (marker.NudgeY - marker.Size / 2f) * scale;
			var rect = icon.Rect;
			_overlay.AddTexturedQuad(x, y, x + icon.Width * icon.Scale * scale, y + icon.Height * icon.Scale * scale,
				rect.U0, rect.V0, rect.U1, rect.V1);
		}

		if (marker.RadarRing && color is { } ringColor) {
			AddRing(centerX - (half >> 1) * scale, centerY - (half >> 1) * scale,
				((long)HddMap.RadarRingRange << HddMapView.ScaleShift) / view.Scale * scale, scale, ringColor);
		}
	}


	private static Vector3? MarkerColor(CockpitArt hud, HddMapMarker marker) =>
		marker.PaletteIndex >= 0 ? hud.PaletteEntry(marker.PaletteIndex) : hud.LogicalColor(marker.ColorId);

	/// <summary>A one-device-pixel circle — <c>Raster_DrawEllipse</c> under brush style 4, as segments.</summary>
	private void AddRing(float cx, float cy, float radius, float scale, Vector3 color) {
		if (radius < 1f) {
			return;
		}

		int segments = Math.Clamp((int)(radius / 4f), 24, 256);
		float previousX = cx + radius;
		float previousY = cy;
		for (int i = 1; i <= segments; i++) {
			float angle = i * MathF.Tau / segments;
			float x = cx + radius * MathF.Cos(angle);
			float y = cy + radius * MathF.Sin(angle);
			_overlay.AddLine(previousX, previousY, x, y, color, Math.Max(scale, 1f));
			previousX = x;
			previousY = y;
		}
	}
}
