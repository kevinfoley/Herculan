using Herculan.Engine.Cockpit;
using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// Draws one panel's cockpit-art quad — at its own native aspect ratio, never stretched — plus, for
/// the center panel only, the herc's HUD widgets over it, positioned from its own <c>.GAU</c> and
/// drawn in the game's own sprite art and fonts. See docs/retail/formats/cockpit-hud-widgets.md and
/// docs/herculan/planning.md's Milestone 8.
///
/// <para>Widgets draw the game's own <c>.HBA</c> sprite art (see <see cref="HudSpriteSheet"/>),
/// positioned by their <c>.GAU</c> rects scaled by <see cref="CockpitArt.GauToPixelScale"/>. That
/// costs a second texture bind per panel — the canopy quad and the sprite quads come from different
/// textures — so the two go out as separate draws against the same shader and vertex format.</para>
///
/// <para>Widgets whose art is not yet identified draw nothing rather than a placeholder shape: their
/// bezels are already painted into the canopy art, so an empty overlay reads as correct where a green
/// rectangle read as unfinished. Everything that is drawn picks its frame from live state in
/// <see cref="CockpitHudState"/> the way the original's own repaint does — a button's lit plate, the
/// MFD's per-mode background, the throttle knob's height, the reticle's on-target frame.</para>
///
/// <para><see cref="AddWidgets"/> lays the panel down in paint order and hands the gunsight, the
/// console gauges, the MFD and the message ports to <see cref="GunsightPainter"/>,
/// <see cref="ConsoleGaugePainter"/>, <see cref="MfdPainter"/> and <see cref="MessagePortPainter"/>.</para>
/// </summary>
public sealed class CanopyPanelPainter {
	private readonly Overlay2DRenderer _overlay;
	private readonly GunsightPainter _gunsight;
	private readonly MessagePortPainter _messages;
	private readonly MfdPainter _mfd;

	public CanopyPanelPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
		_gunsight = new GunsightPainter(overlay);
		_messages = new MessagePortPainter(overlay);
		_mfd = new MfdPainter(overlay);
	}

	/// <summary>
	/// Draws one panel into the given viewport sub-rect: the cockpit-art quad at its own native aspect
	/// ratio (never stretched), then (when <paramref name="hud"/> is non-null) that herc's HUD widgets
	/// on top, aligned to the same transform.
	/// </summary>
	/// <param name="mirrorHorizontally">
	/// True for the left panel, which reuses the same side (<c>.HB2</c>) texture as the right panel
	/// with its UVs flipped — see <see cref="CockpitArt"/>'s doc comment; there is no separate
	/// mirrored asset.
	/// </param>
	/// <param name="hud">
	/// The cockpit whose HUD widgets to overlay, or null to draw the cockpit-art quad alone — pass
	/// null for the side panels, since the console instruments physically live in the front view only.
	/// </param>
	/// <param name="spriteTexture">
	/// The texture <paramref name="hud"/>'s sprite atlas was uploaded to. Required alongside the
	/// atlas: the sheet supplies UVs and pixel sizes, the texture supplies the pixels.
	/// </param>
	/// <param name="mapTexture">
	/// The mission's terrain raster (<see cref="HddMapRaster"/>), which the MFD's NAV MAP draws under
	/// its cross, or null to leave that screen on its flood. The same texture the Heads-Down Display's
	/// command display takes.
	/// </param>
	/// <param name="missileView">
	/// Draws the world into the MFD's MISSILE CAM screen: handed the screen's rect in framebuffer
	/// pixels (x, y from the bottom left, width, height) and the camera to draw it from, and expected
	/// to leave the scissor off. Null leaves the screen on its flood under the cross.
	/// </param>
	public void Draw(int viewportX, int viewportY, int viewportWidth, int viewportHeight,
			GpuTexture cockpitTexture, int cockpitTextureWidth, int cockpitTextureHeight,
			bool mirrorHorizontally, CockpitArt? hud, GpuTexture? spriteTexture = null,
			CockpitHudState? hudState = null, GpuTexture? mapTexture = null,
			Action<int, int, int, int, Camera>? missileView = null) {
		_overlay.Begin(viewportX, viewportY, viewportWidth, viewportHeight);

		// Fit by height, preserving the art's native aspect ratio — never stretched. When the panel is
		// narrower than the art (the common case: each of three side-by-side panels is much taller
		// than 4:3), the quad is wider than the viewport and GL's own clipping crops its left/right
		// edges symmetrically, no explicit UV cropping needed. When the panel is wider than the art
		// (an ultrawide window), the quad is narrower than the viewport and sits centered, leaving the
		// live 3D view visible at the flanks instead of stretching the cockpit art to cover them.
		float scale = viewportHeight / (float)cockpitTextureHeight;
		float quadWidth = cockpitTextureWidth * scale;
		float quadX0 = (viewportWidth - quadWidth) / 2f;

		// Before the canopy, so the canopy art covers it: the HUD's target box is the one thing on the
		// front window that goes *behind* the cockpit frame. See GunsightPainter.AddTargetBoxLayer for why — it is the only
		// gunsight child that drops back into the view's own render context, whose clip block is the
		// herc's canopy cutout, while every other widget draws through the full-canvas context.
		if (hud?.Sprites is { } boxSprites && spriteTexture != null
			&& !(hudState ?? CockpitHudState.Default).Dropout.GunsightDark) {
			_overlay.Clear();
			_gunsight.AddTargetBoxLayer(hud.Gau, boxSprites, hudState ?? CockpitHudState.Default, scale, quadX0);
			if (_overlay.VertexCount > 0) {
				_overlay.Submit(spriteTexture);
			}
		}

		_overlay.Clear();
		AddCockpitQuad(quadX0, quadWidth, viewportHeight, mirrorHorizontally);
		_overlay.Submit(cockpitTexture);

		// Second bind, second draw: the sprites live in their own atlas. Widget positions are authored
		// in the cockpit texture's own native pixel space (after CockpitArt.GauToPixelScale), so the
		// same uniform scale and horizontal offset the canopy quad uses keeps them aligned to the
		// console art regardless of panel aspect ratio.
		// Flat-coloured gauge fills ride along in the same batch — Overlay2DVertex carries the
		// textured/flat choice per vertex, so they ignore whatever texture happens to be bound.
		if (hud != null && spriteTexture != null) {
			_overlay.Clear();
			AddGaugeFills(hud, scale, quadX0,
				fillFraction: (hudState ?? CockpitHudState.Default).EnergyFraction / 1024f);
			if (hud.Sprites is { } sprites) {
				// The NAV MAP's relief lives in its own texture, so the batch is flushed around it where
				// the MFD reaches it — which keeps the flood under it and the cross and title over it,
				// in the paint's own order.
				void DrawNavMapTerrain(float left, float top, float right, float bottom,
						Vector2 centre, MfdNavMapState navMap) {
					if (mapTexture == null) {
						return;
					}

					_overlay.Submit(spriteTexture);
					_overlay.Clear();
					_mfd.DrawMfdNavMapTerrain(navMap, mapTexture, left, top, right, bottom, centre, scale,
						viewportX, viewportY, viewportHeight);
					_overlay.Clear();
				}

				// The MISSILE CAM's world the same way, between the screen's flood and the cross over it.
				// The scene pass leaves its own GL state behind, so the overlay's is put back after it.
				void DrawMissileView(float left, float top, float right, float bottom, Camera view) {
					if (missileView == null) {
						return;
					}

					_overlay.Submit(spriteTexture);
					_overlay.Clear();

					int x0 = (int)MathF.Floor(left);
					int y0 = (int)MathF.Floor(viewportHeight - bottom);
					int width = Math.Max((int)MathF.Ceiling(right) - x0, 0);
					int height = Math.Max((int)MathF.Ceiling(viewportHeight - top) - y0, 0);
					if (width == 0 || height == 0) {
						return;
					}

					missileView(viewportX + x0, viewportY + y0, width, height, view);

					_overlay.Begin(viewportX, viewportY, viewportWidth, viewportHeight);
				}

				AddWidgets(hud, sprites, scale, quadX0, hudState ?? CockpitHudState.Default, DrawNavMapTerrain,
					DrawMissileView);
			}

			if (_overlay.VertexCount > 0) {
				_overlay.Submit(spriteTexture);
			}
		}

		_overlay.End();
	}

	/// <summary>The cockpit-art quad at its native aspect ratio, positioned by <see cref="Draw"/>'s fit-by-height math.</summary>
	private void AddCockpitQuad(float quadX0, float quadWidth, int viewportHeight, bool mirror) {
		float u0 = mirror ? 1f : 0f;
		float u1 = mirror ? 0f : 1f;
		_overlay.AddTexturedQuad(quadX0, 0, quadX0 + quadWidth, viewportHeight, u0, 0f, u1, 1f);
	}

	/// <summary>
	/// Draws the Master Energy Pool meter's LED bar: the unfilled remainder across the whole box,
	/// then the filled span as the original's one-pixel vertical pinstripe of two near-identical
	/// shades (see <see cref="HudColorTable.GaugeFillEvenId"/>).
	///
	/// <para>Geometry is the <c>.GAU</c> energy-meter rect at offset 564, which
	/// <c>EnergyPoolGauge_Ctor</c> (<c>00444d5c</c>) copies verbatim into the bar object before
	/// handing it to <c>LedBarGraph_Ctor</c> with range <c>0x400</c>, so the bar is the horizontal
	/// variant and fills along x — see docs/retail/formats/cockpit-hud-widgets.md, "LED gauges".</para>
	///
	/// <para>Nothing is drawn at <c>ShieldDisplay</c>: that widget is <c>ShieldsGauge</c>, a
	/// different class with its own nested-box geometry, not an LED bar.</para>
	///
	/// <para><paramref name="fillFraction"/> is the piloted machine's Master Energy Pool, over the
	/// same 0-1024 range the widget's bar was built with — see
	/// <c>Herculan.Engine.Sim.MechObject.EnergyPoolFraction</c>. The bar's fill <i>direction</i> is
	/// still assumed rather than read: the original derives it from the sign of its precomputed span,
	/// and every retail rect authors x0 left of x1, so it fills left to right here.</para>
	/// </summary>
	private void AddGaugeFills(CockpitArt hud, float scale, float quadX0, float fillFraction) {
		if (hud.GaugeColors is not var (fillEven, fillOdd, remainder)
			|| hud.Gau.EnergyMeter is not { } meter
			|| meter.Size.Width <= 0 || meter.Size.Height <= 0) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		float Px(int gauX) => quadX0 + gauX * S * scale;
		float Py(int gauY) => gauY * S * scale;

		int left = meter.Origin.X;
		int right = left + meter.Size.Width;
		int top = meter.Origin.Y;
		int bottom = top + meter.Size.Height;

		_overlay.AddFilledRect(Px(left), Py(top), Px(right), Py(bottom), remainder);

		// Columns are stepped in the .GAU's own coordinate space, which is what the original strides
		// over — the x2 scale to cockpit pixels happens inside Px, so the stripe stays one source
		// pixel wide regardless of panel size.
		int filledTo = left + (int)MathF.Round(meter.Size.Width * Math.Clamp(fillFraction, 0f, 1f));
		for (int x = left; x < filledTo; x++) {
			_overlay.AddFilledRect(Px(x), Py(top), Px(x + 1), Py(bottom), (x & 1) == 0 ? fillEven : fillOdd);
		}
	}

	/// <summary>
	/// Places each widget's sprite and text. A sprite is drawn at its own native pixel size anchored
	/// to the widget's top-left, not stretched to the widget rect: the two disagree by a few pixels in
	/// real data (a weapon plate is 116x18 against a 110x12 rect) because the <c>.GAU</c> rect is the
	/// widget's hit/layout box, not its art's extent, and stretching to match visibly softens art
	/// authored for exact pixels.
	/// </summary>
	private void AddWidgets(CockpitArt hud, HudSpriteSheet sprites, float scale, float quadX0,
			CockpitHudState state, MfdPainter.NavMapTerrainWriter? drawNavMapTerrain = null,
			MfdPainter.MissileViewWriter? drawMissileView = null) {
		const float S = CockpitArt.GauToPixelScale;
		var gau = hud.Gau;
		float Px(int gauX) => quadX0 + gauX * S * scale;
		float Py(int gauY) => gauY * S * scale;

		// Device pixels: the 640-wide space the .GAU x2 scale maps into, which is also the space the
		// sprite banks and .HFN glyphs are authored in, and the space the original's own widget code
		// works in once VideoMode_X/YCoordShift has been applied.
		float Dx(float deviceX) => quadX0 + deviceX * scale;
		float Dy(float deviceY) => deviceY * scale;

		void Blit(string bank, int frame, float left, float top) =>
			BlitFlipped(bank, frame, left, top, flipX: false, flipY: false);

		// Mirroring is how the target box gets its four corners out of one 12x12 bracket sprite - the
		// original passes the blitter a 0-3 flip mode for exactly that.
		void BlitFlipped(string bank, int frame, float left, float top, bool flipX, bool flipY) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			// A bank that only ships at 320-wide reports Scale 2, so its frames still cover the cockpit
			// pixels they were authored for - the original doubles the same banks the same way.
			var r = sprite.Rect;
			float drawn = scale * sprite.Scale;
			_overlay.AddTexturedQuad(left, top, left + sprite.Width * drawn, top + sprite.Height * drawn,
				flipX ? r.U1 : r.U0, flipY ? r.V1 : r.V0,
				flipX ? r.U0 : r.U1, flipY ? r.V0 : r.V1);
		}

		void BlitDevice(string bank, int frame, float deviceLeft, float deviceTop) =>
			Blit(bank, frame, Dx(deviceLeft), Dy(deviceTop));

		// The same blit trimmed to a span of device columns, for the one thing on the front window that is
		// drawn wider than the box it belongs in: the heading tape's strip. The original installs a clip
		// rect round the pair (HudHeadingTape_Paint narrows the canvas context to the tape's own rect and
		// restores it after), so the frames hanging out either end are cut at the window's edges rather
		// than painted over the canopy. Trimming the quad and its UVs by the same fraction keeps the whole
		// cockpit one batch, exactly as the Heads-Down Display's own clipped blit does.
		void BlitDeviceClippedX(string bank, int frame, float deviceLeft, float deviceTop,
				float clipLeft, float clipRight) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			float width = sprite.Width * sprite.Scale;
			float x0 = Math.Max(deviceLeft, clipLeft), x1 = Math.Min(deviceLeft + width, clipRight);
			if (x1 <= x0) {
				return;
			}

			var r = sprite.Rect;
			_overlay.AddTexturedQuad(Dx(x0), Dy(deviceTop), Dx(x1), Dy(deviceTop + sprite.Height * sprite.Scale),
				r.U0 + (r.U1 - r.U0) * ((x0 - deviceLeft) / width), r.V0,
				r.U1 - (r.U1 - r.U0) * ((deviceLeft + width - x1) / width), r.V1);
		}

		// The same trimmed to a span of device rows, for the altitude scale's tick strip, which its paint
		// draws through a clip rect the height of the scale.
		void BlitDeviceClippedY(string bank, int frame, float deviceLeft, float deviceTop,
				float clipTop, float clipBottom) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			float height = sprite.Height * sprite.Scale;
			float y0 = Math.Max(deviceTop, clipTop), y1 = Math.Min(deviceTop + height, clipBottom);
			if (y1 <= y0) {
				return;
			}

			var r = sprite.Rect;
			_overlay.AddTexturedQuad(Dx(deviceLeft), Dy(y0), Dx(deviceLeft + sprite.Width * sprite.Scale), Dy(y1),
				r.U0, r.V0 + (r.V1 - r.V0) * ((y0 - deviceTop) / height),
				r.U1, r.V1 - (r.V1 - r.V0) * ((deviceTop + height - y1) / height));
		}

		// A sprite rotated about its own top-left corner rather than blitted axis-aligned — the MFD
		// scanner's turret wedge is the one thing on the cockpit drawn this way. The pivot is the
		// corner and not the centre because Bitmap_BlitRotatedScaled (00488a8c) builds its destination
		// quad as (0,0),(w,0),(w,h),(0,h), rotates each corner, and only then translates by the
		// caller's position; the wedge sprite is authored as a quarter disc filling the quadrant right
		// and below that corner for exactly this reason.
		void BlitRotatedDevice(string bank, int frame, float pivotDeviceX, float pivotDeviceY, short angle) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var r = sprite.Rect;
			float drawn = scale * sprite.Scale;
			float width = sprite.Width * drawn;
			float height = sprite.Height * drawn;

			// Math_Rotate2DPoint's own matrix, at Q14: a positive binary angle turns the quad clockwise
			// on a screen whose y runs down.
			float cos = SimTrig.Cos(angle) / 16384f;
			float sin = SimTrig.Sin(angle) / 16384f;
			var right = new Vector2(cos, sin) * width;
			var down = new Vector2(-sin, cos) * height;
			var pivot = new Vector2(Dx(pivotDeviceX), Dy(pivotDeviceY));

			_overlay.AddTexturedQuad(pivot, pivot + right, pivot + right + down, pivot + down,
				r.U0, r.V0, r.U1, r.V1);
		}

		// Draws one run of glyphs left to right from a device-pixel top-left and reports where the run
		// ended — the original chains its readouts by measuring the previous one the same way. The
		// character at hotkeyIndex is drawn in an alternate font, which is all Label_SetTextWithHotkey (00438aac) does over
		// a plain Label_SetText: it measures the prefix, then redraws that one glyph in the label's
		// own +0x21 alternate. Pass -1 for a plain run.
		float DrawRun(string fontName, string alternateFont, string text, int hotkeyIndex,
				float deviceLeft, float deviceTop) {
			if (sprites.Font(fontName) is not { } font) {
				return deviceLeft;
			}

			float pen = deviceLeft;
			for (int i = 0; i < text.Length; i++) {
				string face = i == hotkeyIndex && sprites.Font(alternateFont) != null ? alternateFont : fontName;
				var metrics = sprites.Font(face)!;
				if (metrics.GlyphIndex(text[i]) is { } glyph) {
					BlitDevice(face, glyph, pen, deviceTop);
					pen += metrics.Width(text[i]);
				}
			}

			return pen;
		}

		float DrawText(string fontName, string text, float deviceLeft, float deviceTop) =>
			DrawRun(fontName, string.Empty, text, -1, deviceLeft, deviceTop);

		// One waypoint indicator: the diamond while its subject is inside the tape's span, an arrow
		// parked past whichever end it is beyond otherwise, and — for the route indicator alone — the
		// WAYPOINT line under the compass. The caption's prefix is a string-table entry, so a file
		// that has not got it leaves the line off rather than printing a bare number.
		void DrawWaypointMark(WaypointIndicator geometry, WaypointMark? mark) {
			if (mark is not { } subject || hud.LogicalColor(subject.ColorId) is not { } color) {
				return;
			}

			Vector2 At((int X, int Y) point) => new(Dx(point.X), Dy(point.Y));

			if (WaypointIndicator.OnTape(subject.BearingError)) {
				var (bottom, left, top, right) = geometry.Diamond(subject.BearingError);
				_overlay.AddFilledTriangle(At(bottom), At(left), At(top), color);
				_overlay.AddFilledTriangle(At(bottom), At(top), At(right), color);
			} else {
				var (tip, baseA, baseB) =
					geometry.Arrow(WaypointIndicator.PointsRight(subject.BearingError));
				_overlay.AddFilledTriangle(At(tip), At(baseA), At(baseB), color);
			}

			if (subject.Number == WaypointMark.NoCaption
				|| hud.Strings?.Text(WaypointIndicator.CaptionGroup, WaypointIndicator.CaptionIndex)
					is not { } prefix) {
				return;
			}

			var (x0, y0, x1, y1) = geometry.LabelRect;
			DrawTextCentered(WaypointIndicator.LabelFont, subject.Caption(prefix), x0, y0, x1, y1);
		}

		// A label paints its own background before its text: the constructors write a background colour
		// id into the label object's field 0x1d — 0x2e for a weapon row, DAT_004d3c26 (colour id 19,
		// black) for the shield readouts — which is why retail's "100" sits on solid black rather than
		// on the bezel art under it.
		void DrawTextCentered(string fontName, string text, int gauX0, int gauY0, int gauX1, int gauY1,
				Vector3? background = null) {
			if (sprites.Font(fontName) is not { } font) {
				return;
			}

			if (background is { } fill) {
				_overlay.AddFilledRect(Px(gauX0), Py(gauY0), Px(gauX1), Py(gauY1), fill);
			}

			// The .GAU rect's Origin + Size is the file's own inclusive second corner, so scaling both
			// corners gives the device-pixel rect Label_SetRect takes. See HudFont.Place.
			var (textX, textY) = font.Place(text,
				(int)(gauX0 * S), (int)(gauY0 * S), (int)(gauX1 * S), (int)(gauY1 * S),
				LabelAlign.Center);
			DrawText(fontName, text, textX, textY);
		}

		MfdPainter.AddMfd(hud, state, BlitDevice, BlitRotatedDevice, DrawRun,
			(x0, y0, x1, y1, color) => _overlay.AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color),
			drawNavMapTerrain is null ? null : (x0, y0, x1, y1, centre, navMap) => drawNavMapTerrain(
				Dx(x0), Dy(y0), Dx(x1), Dy(y1), new Vector2(Dx(centre.X), Dy(centre.Y)), navMap),
			drawMissileView is null ? null : (x0, y0, x1, y1, view) => drawMissileView(
				Dx(x0), Dy(y0), Dx(x1), Dy(y1), view));

		// The front-window HUD — the gunsight complex and everything its paint draws after its children
		// — skips its whole paint while the sensor dropout has it dark.
		bool gunsight = !state.Dropout.GunsightDark;

		// The heading tape. The whole compass — ticks, degree labels and all — is the hudhtick bank's
		// art laid end to end, and the heading picks which slice of it shows: two consecutive frames a
		// rect-width apart, clipped to the rect, sliding through it as the machine turns. See HeadingTape.
		if (gunsight && HeadingTape.From(hud) is { } tape) {
			var (frame, next, scrollX) = tape.Slice(state.Heading);
			BlitDeviceClippedX(HeadingTape.SpriteBank, frame, scrollX, tape.Top, tape.Left, tape.Left + tape.Width);
			BlitDeviceClippedX(HeadingTape.SpriteBank, next, scrollX + tape.Width, tape.Top,
				tape.Left, tape.Left + tape.Width);
		}

		// The Rotation Indicator, above the heading tape: a fixed track with a bar sliding along it at
		// the turret's twist angle, in one of two colours depending on whether the turret is centred.
		// Its geometry is derived from the heading tape's rect rather than read from the file — see
		// RotationIndicator.
		if (gunsight && RotationIndicator.From(hud) is { } rotation) {
			BlitDevice(RotationIndicator.SpriteBank, RotationIndicator.TrackFrame,
				rotation.TrackX, rotation.TrackY);
			BlitDevice(RotationIndicator.SpriteBank, RotationIndicator.FrameFor(state.TorsoTwist),
				rotation.BarLeftFor(state.TorsoTwist), rotation.BarY);
		}

		// The throttle slider: frame 1 is the knob, riding the track at the setting's own height, and
		// frame 0 the 2px tick the gauge parks beside the track's centre — ThrottleGauge_Ctor captures
		// that tick's position once, at the knob's neutral height, and never moves it again. Its x
		// nudge is the .GAU's own offset-1072 int, the one field of the block the loader does not
		// pre-scale, which is why it is shifted here instead.
		if (ThrottleTrack.From(hud) is { } track) {
			BlitDevice(ThrottleTrack.SpriteBank, ThrottleTrack.KnobFrame,
				track.Left, track.KnobTopFor(state.Throttle));
			BlitDevice(ThrottleTrack.SpriteBank, ThrottleTrack.TickFrame,
				track.Left + track.TickOffsetX, track.KnobTopFor(0));
		}

		ConsoleGaugePainter.AddWeaponRows(gau, state, hud.WeaponBarColors, hud.LogicalColor(ConsoleGaugePainter.PodPlateColorId),
			BlitDevice, DrawText,
			(x0, y0, x1, y1, color) => _overlay.AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		ConsoleGaugePainter.AddShieldReadouts(gau, state, hud.GaugeColors?.Remainder, DrawTextCentered);
		ConsoleGaugePainter.AddConsoleButtons(gau, hud.Strings, state, BlitDevice, DrawTextCentered);

		// The reticle is a point, not a rect — the only widget in the file that is — so its sprite
		// centers on it rather than hanging off a top-left corner. Which of the bank's three frames it
		// wears is the gunsight's on-target state: child 4's paint (Gunsight_ReticlePaint, 0043b7e0) draws frame 0 with
		// nothing selected or the selection off the sight, frame 2 once the target projects within
		// TargetBox.OnTargetTolerance of this very point, and frame 1 when it also has missile lock.
		if (gunsight && gau.Reticle is { } reticle) {
			int frame = GunsightPainter.ReticleFrame(gau, state);
			if (sprites.Sprite(TargetBox.SpriteBank, frame) is { } crosshair) {
				Blit(TargetBox.SpriteBank, frame,
					Px(reticle.Origin.X) - crosshair.Width * scale / 2f,
					Py(reticle.Origin.Y) - crosshair.Height * scale / 2f);
			}
		}

		// Over the reticle, because the gunsight complex paints its children in construction order and
		// the target box is child 5 to the reticle's child 4.
		if (gunsight) {
			GunsightPainter.AddTargetIndicator(gau, sprites, hud.TargetArrowColors, state,
				(bank, frame, left, top, flipX, flipY) => BlitFlipped(bank, frame, Dx(left), Dy(top), flipX, flipY),
				(a, b, c, color) => _overlay.AddFilledTriangle(
					new Vector2(Dx(a.X), Dy(a.Y)),
					new Vector2(Dx(b.X), Dy(b.Y)),
					new Vector2(Dx(c.X), Dy(c.Y)), color));
		}

		// Children 7 and 8, in construction order: the nav marker's indicator and the route's. Both
		// hang off the heading tape's rect and are filled polygons rather than sprites — see
		// WaypointIndicator, which owns all of the arithmetic.
		if (gunsight && WaypointIndicator.From(hud) is { } waypoints) {
			DrawWaypointMark(waypoints, state.NavMarker);
			DrawWaypointMark(waypoints, state.RouteWaypoint);
		}

		// Once every child has painted, the ATT legend while the tracker is on, then the speed and
		// time readouts — the order both gunsight paints draw them in.
		if (gunsight) {
			GunsightPainter.AddAutoTrackLegend(gau, hud.Strings, state, BlitDevice, DrawTextCentered);
			GunsightPainter.AddGunsightReadouts(gau, sprites, state, DrawText);
		}

		// And last of all, after every child, the floating scanner repeater — the gunsight's paint
		// calls it once the child loop is done.
		if (gunsight) {
			GunsightPainter.AddHudScanner(hud, state, BlitDevice,
				(x0, y0, x1, y1, color) => _overlay.AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		}

		// Then the RAZOR's altitude scale, which both gunsight paints call straight after the repeater.
		if (gunsight && state.Altitude is { } altitude && AltitudeScale.From(hud) is { } altitudeScale) {
			GunsightPainter.AddAltitudeScale(hud, altitudeScale, altitude, BlitDevice, BlitDeviceClippedY,
				(x0, y0, x1, y1, color) => _overlay.AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		}

		// The message port is the view's own last child, constructed after every gauge, so its box
		// goes over whatever it overlaps rather than under it.
		_messages.AddMessageTicker(hud, sprites, state.Message, Dx, Dy, scale);

		// And the second port of the same class, built from the rect immediately before the ticker's.
		_messages.AddPilotMessage(hud, sprites, state.PilotMessage, Dx, Dy, scale);
		_messages.AddTrainingMessage(hud, sprites, state.TrainingMessage, Dx, Dy, scale);
	}
}
