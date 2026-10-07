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

		// Viewport-local device pixels, which is the space HddMapView projects into.
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
				_overlay.AddFilledRect(x, My(0), x + 1f, My(region.Height), gridColor);
			}

			for (int worldY = FloorToPitch(view.CentreY - halfY);
					worldY <= view.CentreY + halfY; worldY += HddMap.GridPitch) {
				float y = Py(worldY);
				_overlay.AddFilledRect(Mx(0), y, Mx(region.Width), y + 1f, gridColor);
			}
		}

		// The mission border: the block-1 bounding box the screen keeps in its own +0x160 rect, drawn
		// through the brush mode Raster_FillRect answers by walking the four edges as lines rather
		// than by filling the interior.
		var bounds = view.Bounds;
		if (!bounds.IsEmpty && hud.LogicalColor(HddMap.BorderColorId) is { } borderColor) {
			_overlay.AddRectOutline(Px(bounds.MinX), Py(bounds.MaxY), Px(bounds.MaxX), Py(bounds.MinY),
				1f, borderColor);
		}

		var markers = state.Command.Plotted;
		for (int i = 0; i < markers.Count; i++) {
			// The selected pilot's marker blinks on the display's own half-second toggle — see
			// "The selected pilot's marker" in docs/retail/formats/heads-down-display.md.
			if (!state.Command.Blink && markers[i].PilotSlot == state.Command.SelectedPilot
				&& state.Command.SelectedPilot >= 0) {
				continue;
			}

			AddHddMarker(hud, sprites, markers[i], view, Px, Py, selected: i == state.Command.ChosenUnit);
		}

		// The link the manual describes: a line from the selected pilot to whatever the armed order
		// has been pointed at, in that pilot's own colour.
		int slot = state.Command.SelectedPilot;
		if (slot >= 0 && hud.LogicalColor(HudColorTable.PilotColorId(slot)) is { } linkColor
			&& PilotMarker(markers, slot) is { } from) {
			int chosen = state.Command.ChosenUnit;
			if (chosen >= 0 && chosen < markers.Count) {
				_overlay.AddLine(Px(from.WorldX), Py(from.WorldY),
					Px(markers[chosen].WorldX), Py(markers[chosen].WorldY), linkColor);
			} else if (state.Command.ChosenPoint is { } point) {
				_overlay.AddLine(Px(from.WorldX), Py(from.WorldY), Px(point.X), Py(point.Y), linkColor);
			}
		}

		if (_overlay.VertexCount > 0) {
			_overlay.Submit(spriteTexture);
		}

		_overlay.ClearScissor();

		static int FloorToPitch(int world) =>
			(int)Math.Floor(world / (double)HddMap.GridPitch) * HddMap.GridPitch;
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
	/// back by half the marker's size so it lands on the object. A <see cref="HddMapMarker.Ranged"/>
	/// one first works out its apparent size from its distance to the map centre and draws a filled
	/// box of that size instead whenever the icon would be the bigger of the two.
	/// </summary>
	private void AddHddMarker(CockpitArt hud, HudSpriteSheet sprites, HddMapMarker marker,
			HddMapView view, Func<int, float> px, Func<int, float> py, bool selected) {
		float centerX = px(marker.WorldX);
		float centerY = py(marker.WorldY);
		var sprite = sprites.Sprite(HddMap.IconBank, marker.Frame);

		if (marker.Ranged) {
			// The distance the size divides by is measured in three dimensions with the zoom standing
			// in for height, which is the original's own vector: (x - centreX, y - centreY, -scale).
			double dx = marker.WorldX - (double)view.CentreX;
			double dy = marker.WorldY - (double)view.CentreY;
			double distance = Math.Max(Math.Sqrt(dx * dx + dy * dy + (double)view.Scale * view.Scale), 1);
			int apparent = (int)Math.Min(
				((long)HddMap.MarkerSizeReference << HddMap.MarkerSizeShift) / distance, int.MaxValue);

			if (sprite is not { Height: > 0 } || apparent < sprite.Value.Height) {
				if (hud.LogicalColor(marker.ColorId) is { } boxColor) {
					float half = apparent / 2f;
					_overlay.AddFilledRect(centerX - half, centerY - half, centerX + half, centerY + half, boxColor);
				}

				return;
			}
		}

		if (sprite is not { Width: > 0, Height: > 0 } icon) {
			return;
		}

		float x = centerX - marker.Size / 2f + marker.NudgeX;
		float y = centerY - marker.Size / 2f + marker.NudgeY;
		var rect = icon.Rect;
		_overlay.AddTexturedQuad(x, y, x + icon.Width, y + icon.Height, rect.U0, rect.V0, rect.U1, rect.V1);

		// The order's chosen unit is boxed, two pixels proud of the icon on every side.
		if (selected && hud.LogicalColor(HddMap.ChosenUnitColorId) is { } outline) {
			_overlay.AddRectOutline(x - 2f, y - 2f, x + icon.Width + 2f, y + icon.Height + 2f, 1f, outline);
		}
	}
}
