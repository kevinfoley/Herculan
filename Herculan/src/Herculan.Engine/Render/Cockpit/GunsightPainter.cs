using Herculan.Engine.Cockpit;
using System.Numerics;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The front window's gunsight complex — the reticle, the target box and its off-screen arrow, the ATT
/// legend, the speed and time readouts, the floating scanner repeater and the RAZOR's altitude scale —
/// in the order <see cref="CanopyPanelPainter.AddWidgets"/> paints them.
/// </summary>
internal sealed class GunsightPainter {
	private readonly Overlay2DRenderer _overlay;

	public GunsightPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
	}

	/// <summary>
	/// The front window's floating scanner repeater (<c>HudScanner_Paint</c>, <c>0043f2b0</c>) — see
	/// <see cref="HudScanner"/> for what it is and why it is here rather than with the MFD.
	/// It draws only while the MFD is showing something other than its own screen.
	/// </summary>
	internal static void AddHudScanner(CockpitArt hud, CockpitHudState state,
			Action<string, int, float, float> blitDevice,
			Action<float, float, float, float, Vector3> fillRect) {
		if (state.Mfd == MfdMode.Scanner || HudScanner.Origin(hud.Gau) is not { } origin) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		int half = HudScanner.HalfSizeDevice;
		float centerX = origin.X * S + half;
		float centerY = origin.Y * S + half;

		if (hud.LogicalColor(HudScanner.OutlineColorId) is { } outline) {
			RasterPrimitives.AddCircleOutline(centerX, centerY, half, outline, fillRect);

			// The turret arc: two lines from the centre out to the rim, 45 degrees either side of the
			// twist. The endpoint is the point (0, -half) rotated, so both reach exactly the rim.
			for (int side = -1; side <= 1; side += 2) {
				short angle = unchecked((short)(state.TorsoTwist + side * HudScanner.ArcHalfAngle));
				float cos = SimTrig.Cos(angle) / 16384f;
				float sin = SimTrig.Sin(angle) / 16384f;
				RasterPrimitives.AddLine(centerX, centerY, centerX + half * sin, centerY - half * cos, outline, fillRect);
			}
		}

		blitDevice(MfdScanner.Bank, MfdScanner.PlayerMarkerFrame,
			centerX - HudScanner.PlayerMarkerOffsetX * S, centerY);

		var scanner = state.Scanner;
		int worldPerPixel = HudScanner.WorldUnitsPerPixel(scanner.Range);
		var contacts = scanner.Plotted;
		var blipOutline = hud.LogicalColor(HudScanner.BlipOutlineColorId);

		for (int i = 0; i < contacts.Count; i++) {
			if (hud.LogicalColor(contacts[i].ColorId) is not { } color) {
				continue;
			}

			float blipX = centerX + contacts[i].X / worldPerPixel;
			float blipY = centerY + contacts[i].Y / worldPerPixel;
			if (blipOutline is { } ring) {
				RasterPrimitives.AddFilledCircle(blipX, blipY, HudScanner.BlipOutlineRadius, ring, fillRect);
			}

			RasterPrimitives.AddFilledCircle(blipX, blipY, HudScanner.BlipCoreRadius, color, fillRect);
		}

		if (scanner.TargetContact >= 0 && scanner.TargetContact < contacts.Count) {
			var target = contacts[scanner.TargetContact];
			blitDevice(MfdScanner.Bank, MfdScanner.TargetBracketFrame,
				centerX + target.X / worldPerPixel - HudScanner.TargetBracketOffset * S,
				centerY + target.Y / worldPerPixel - HudScanner.TargetBracketOffset * S);
		}
	}

	/// <summary>
	/// The RAZOR's altitude scale (<c>Gunsight_PaintAltitudeScale</c>, <c>0043dd70</c>), in the paint's
	/// own order — see <see cref="AltitudeScale"/>. The column's sides are vertical lines and its solid
	/// part a fill, both inclusive of their end rows and columns as <c>Raster_DrawLine</c> and
	/// <c>Raster_FillRect</c> are; the fill goes over the row the sides end on.
	/// </summary>
	internal static void AddAltitudeScale(CockpitArt hud, AltitudeScale scale, AltitudeReading reading,
			Action<string, int, float, float> blitDevice,
			Action<string, int, float, float, float, float> blitDeviceClippedY,
			Action<float, float, float, float, Vector3> fillRect) {
		blitDevice(AltitudeScale.SpriteBank, AltitudeScale.HeadFrame, scale.X, scale.Y);
		blitDevice(AltitudeScale.SpriteBank, AltitudeScale.FootFrame, scale.X, scale.Foot);

		int ground = scale.RowFor(reading.Ground, reading);
		if (hud.PaletteEntry(AltitudeScale.SideColorIndex) is { } side) {
			fillRect(scale.ColumnLeft, scale.Top, scale.ColumnLeft + 1, ground + 1, side);
			fillRect(scale.ColumnRight, scale.Top, scale.ColumnRight + 1, ground + 1, side);
		}

		if (hud.PaletteEntry(AltitudeScale.FillColorIndex) is { } fill) {
			fillRect(scale.ColumnLeft, ground, scale.ColumnRight + 1, scale.Foot + 1, fill);
		}

		var (markerX, markerY) = scale.MarkerAt(reading);
		blitDevice(AltitudeScale.SpriteBank, AltitudeScale.MarkerFrame, markerX, markerY);

		// Retail's tape blits never reach the screen — see AltitudeScale.TapeFrame. Read every frame, so
		// toggling the tweak shows or hides it at once.
		if (!TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowAltitudeTape)) {
			return;
		}

		int tape = scale.TapeRow(reading);
		blitDeviceClippedY(AltitudeScale.SpriteBank, AltitudeScale.TapeFrame, scale.TapeLeft, tape,
			scale.Top, scale.Foot + 1);
		blitDeviceClippedY(AltitudeScale.SpriteBank, AltitudeScale.TapeFrame, scale.TapeLeft, tape - scale.TapeHeight,
			scale.Top, scale.Foot + 1);
	}

	/// <summary>
	/// Which of the <c>HUD</c> bank's three reticle frames the crosshair wears, from child 4's paint
	/// (<c>Gunsight_ReticlePaint</c>, <c>0043b7e0</c>): frame 0 unless the selected target projects within
	/// <see cref="TargetBox.OnTargetTolerance"/> of the reticle point on both axes, then frame 2, or
	/// frame 1 when the armed missile mount also has lock. This is the cockpit's "on target"
	/// indication — the box is suppressed over exactly the same span, so the two never overlap.
	/// </summary>
	internal static int ReticleFrame(GAUFile gau, CockpitHudState state) {
		if (gau.Reticle is not { } reticle || state.Target is not { InFront: true } target) {
			return 0;
		}

		const float S = CockpitArt.GauToPixelScale;
		bool onTarget = MathF.Abs(target.ScreenX - reticle.Origin.X * S) < TargetBox.OnTargetTolerance
			&& MathF.Abs(target.ScreenY - reticle.Origin.Y * S) < TargetBox.OnTargetTolerance;

		return onTarget ? target.Locked ? 1 : 2 : 0;
	}

	/// <summary>
	/// Where the selected target sits on the canopy, and where the indicator is measured from — the
	/// two points both halves of child 5's paint (<c>Gunsight_TargetIndicatorPaint</c>, <c>0043b950</c>) work off. Null when nothing is
	/// selected or the herc's <c>.GAU</c> has no reticle point.
	/// </summary>
	private static (TargetIndicator Target, Vector2 Origin, Vector2 Point)? TargetPoint(
			GAUFile gau, CockpitHudState state) {
		if (state.Target is not { } target || gau.Reticle is not { } reticle) {
			return null;
		}

		const float S = CockpitArt.GauToPixelScale;
		var origin = new Vector2(reticle.Origin.X * S, reticle.Origin.Y * S);

		// A target behind the eye keeps no usable projection, so the original throws it away and
		// re-projects a synthetic point straight out to one side on the reticle's own row: the arrow
		// then points level left or level right, and no box is drawn.
		var point = target.InFront
			? new Vector2(target.ScreenX, target.ScreenY)
			: new Vector2(
				origin.X + (target.BehindToLeft ? -TargetBox.BehindOffsetX : TargetBox.BehindOffsetX),
				origin.Y);

		return (target, origin, point);
	}

	/// <summary>
	/// The target box, emitted as its own batch so it can be drawn <b>under</b> the canopy art. It is
	/// the one HUD element the cockpit frame covers, and that is a real distinction in the original
	/// rather than a layering choice here.
	///
	/// <para>Every widget the cockpit paints goes through a render context whose clip block decides
	/// what it may touch. <c>Gau_BuildCockpitWidgets</c> (<c>00431bf8</c>) builds one covering the
	/// whole cockpit canvas in the plain single-rect clip mode and stores it at
	/// <c>CockpitViewInstance+4</c>; <c>Cockpit_PushCanvasContext</c> (<c>004311e0</c>) installs it and <c>Cockpit_PopRenderContext</c> (<c>00431210</c>) restores
	/// whatever was there before. The context underneath is the one
	/// <c>CockpitView_ApplyViewState</c> (<c>00429e60</c>) loaded the current view's own
	/// <c>0x204</c>-byte clip block into — the herc's <c>.HD</c>/<c>.ED</c> canopy cutout — putting it
	/// in clip <b>mode 2</b>, the region-list mode, in which a bitmap is cut to the canopy opening
	/// scanline by scanline — following the A-pillars, not a rectangle (see
	/// docs/retail/simulation/hud-target-indicator.md, "Why the box goes behind the cockpit frame").</para>
	///
	/// <para><b>Child 5 is the only widget that opts into it</b>: its paint calls
	/// <c>Cockpit_PopRenderContext</c> before the box and <c>Cockpit_PushCanvasContext</c> after, dropping out of the canvas
	/// context for those blits alone. The reticle, the heading tape, the rotation indicator, the
	/// readouts and the off-screen arrow all stay in the canvas context and are never cut. Reproduced
	/// here by draw order: this batch goes down before the canopy quad, whose art is opaque everywhere
	/// but the cutout (see <see cref="CockpitClipRegions"/>, which is the same region data), so the
	/// frame covers it exactly where the original's clip block would have.</para>
	/// </summary>
	internal void AddTargetBoxLayer(GAUFile gau, HudSpriteSheet sprites, CockpitHudState state,
			float scale, float quadX0) {
		if (TargetPoint(gau, state) is not var (target, origin, point)
			|| !target.InFront
			|| (MathF.Abs(point.X - origin.X) <= TargetBox.OnTargetTolerance
				&& MathF.Abs(point.Y - origin.Y) <= TargetBox.OnTargetTolerance)) {
			return;
		}

		AddTargetBox(sprites, target, point, (bank, frame, left, top, flipX, flipY) => {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var r = sprite.Rect;
			float drawn = scale * sprite.Scale;
			float x = quadX0 + left * scale;
			float y = top * scale;
			_overlay.AddTexturedQuad(x, y, x + sprite.Width * drawn, y + sprite.Height * drawn,
				flipX ? r.U1 : r.U0, flipY ? r.V1 : r.V0,
				flipX ? r.U0 : r.U1, flipY ? r.V0 : r.V1);
		});
	}

	/// <summary>
	/// The off-screen half of child 5's paint: the arrow, drawn whenever the target does not land
	/// inside the <c>.GAU</c>'s gunsight area, where the line from the reticle out to it crosses that
	/// rect's border. Unlike the box this stays in the canvas context, so it is never cut by the
	/// canopy — it does not need to be, since the area is well inside the window opening.
	/// </summary>
	internal static void AddTargetIndicator(GAUFile gau, HudSpriteSheet sprites,
			(Vector3 Unlocked, Vector3 Locked)? arrowColors, CockpitHudState state,
			Action<string, int, float, float, bool, bool> blit,
			Action<Vector2, Vector2, Vector2, Vector3> triangle) {
		if (TargetPoint(gau, state) is not var (target, origin, point)
			|| gau.GunsightArea is not { } area
			|| arrowColors is not var (unlocked, locked)) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		float areaX0 = area.Origin.X * S;
		float areaY0 = area.Origin.Y * S;
		float areaX1 = (area.Origin.X + area.Size.Width) * S;
		float areaY1 = (area.Origin.Y + area.Size.Height) * S;
		bool inside = target.InFront
			&& point.X >= areaX0 && point.X <= areaX1
			&& point.Y >= areaY0 && point.Y <= areaY1;

		if (!inside) {
			AddTargetArrow(origin, point, areaX0, areaY0, areaX1, areaY1,
				target.Locked ? locked : unlocked, triangle);
		}
	}

	/// <summary>
	/// The box itself: the pip centred on the target, four corner brackets at the corners of
	/// <see cref="TargetBox.Bounds"/> — one sprite mirrored into each — and four ticks stood off the
	/// box's edges but lined up on the <i>target's</i> own row and column rather than the box's centre.
	///
	/// <para>The corner brackets and the ticks are the half a Targeting Pod suppresses once it has
	/// singled out a component of the target, leaving the bare pip —
	/// <see cref="TargetIndicator.ComponentTargeted"/>. Both retail reference captures show the full
	/// box, which is what a machine with no pod gets.</para>
	/// </summary>
	private static void AddTargetBox(HudSpriteSheet sprites, TargetIndicator target, Vector2 point,
			Action<string, int, float, float, bool, bool> blit) {
		int first = TargetBox.FirstFrameFor(target.Locked);
		if (sprites.Sprite(TargetBox.SpriteBank, first + TargetBox.PipFrame) is not { } pip
			|| sprites.Sprite(TargetBox.SpriteBank, first + TargetBox.CornerFrame) is not { } corner) {
			return;
		}

		void Draw(int frame, float left, float top, bool flipX = false, bool flipY = false) =>
			blit(TargetBox.SpriteBank, first + frame, left, top, flipX, flipY);

		Draw(TargetBox.PipFrame, point.X - pip.Width / 2f, point.Y - pip.Height / 2f);

		if (target.ComponentTargeted) {
			return;
		}

		var (x0, y0, x1, y1) = TargetBox.Bounds(point.X, point.Y, target.ShapeRadius, target.Distance);
		Draw(TargetBox.CornerFrame, x0, y0);
		Draw(TargetBox.CornerFrame, x1 - corner.Width, y0, flipX: true);
		Draw(TargetBox.CornerFrame, x0, y1 - corner.Height, flipY: true);
		Draw(TargetBox.CornerFrame, x1 - corner.Width, y1 - corner.Height, flipX: true, flipY: true);

		if (sprites.Sprite(TargetBox.SpriteBank, first + TargetBox.VerticalTickFrame) is { } vertical) {
			Draw(TargetBox.VerticalTickFrame, point.X, y1);
			Draw(TargetBox.VerticalTickFrame, point.X, y0 - vertical.Height);
		}

		if (sprites.Sprite(TargetBox.SpriteBank, first + TargetBox.HorizontalTickFrame) is { } horizontal) {
			Draw(TargetBox.HorizontalTickFrame, x1, point.Y);
			Draw(TargetBox.HorizontalTickFrame, x0 - horizontal.Width, point.Y);
		}
	}

	/// <summary>
	/// The off-screen arrow: a flat triangle whose apex sits where the ray from the reticle out to the
	/// target leaves the gunsight area, pointing along that ray.
	///
	/// <para>The crossing is found the way the original finds it — take the vertical border the target
	/// is on and solve for y; if that lands outside the rect, take the horizontal border instead and
	/// solve for x. The apex goes on the crossing and the base <see cref="TargetBox.ArrowLength"/>
	/// back down the ray, <see cref="TargetBox.ArrowHalfWidth"/> to either side. The original builds
	/// the same triangle about the origin and rotates it by the crossing's own bearing less a quarter
	/// turn, which comes to the same thing.</para>
	/// </summary>
	private static void AddTargetArrow(Vector2 origin, Vector2 point,
			float areaX0, float areaY0, float areaX1, float areaY1, Vector3 color,
			Action<Vector2, Vector2, Vector2, Vector3> triangle) {
		// The original nudges a zero component to one rather than special-casing the divide.
		float dx = point.X - origin.X;
		float dy = point.Y - origin.Y;
		if (dx == 0f) {
			dx = 1f;
		}

		if (dy == 0f) {
			dy = 1f;
		}

		float offsetX = (point.X < origin.X ? areaX0 : areaX1) - origin.X;
		float crossingY = offsetX * dy / dx + origin.Y;
		float offsetY;

		if (crossingY > areaY0 && crossingY < areaY1) {
			offsetY = crossingY - origin.Y;
		} else {
			offsetY = (crossingY < origin.Y ? areaY0 : areaY1) - origin.Y;
			offsetX = offsetY * dx / dy;
		}

		var apex = new Vector2(origin.X + offsetX, origin.Y + offsetY);
		if (apex == origin) {
			return;
		}

		var direction = Vector2.Normalize(new Vector2(offsetX, offsetY));
		var perpendicular = new Vector2(-direction.Y, direction.X);
		var back = apex - direction * TargetBox.ArrowLength;

		triangle(back + perpendicular * TargetBox.ArrowHalfWidth, apex,
			back - perpendicular * TargetBox.ArrowHalfWidth, color);
	}

	/// <summary>
	/// The manual's <b>ATT</b> legend: while the TRACK button is latched, <c>Gunsight_Paint</c>
	/// (<c>0043d5c8</c>) and <c>Gunsight_UpdateAndPaint</c> (<c>0043d6dc</c>) blit <c>HUD</c> frame
	/// <see cref="AutoTrackLegendPlateFrame"/> at the legend rect's top-left and centre the text in
	/// that rect in <c>DARK</c>. Nothing is drawn while it is off. See
	/// docs/retail/simulation/cockpit-gunsight-hud.md#the-att-legend.
	/// </summary>
	internal static void AddAutoTrackLegend(GAUFile gau, StringFile? strings, CockpitHudState state,
			Action<string, int, float, float> blit,
			Action<string, string, int, int, int, int, Vector3?> drawCentered) {
		if (!state.AutoTrack || gau.AutoTrackLegend is not { } legend) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		blit(RotationIndicator.SpriteBank, AutoTrackLegendPlateFrame, legend.Origin.X * S, legend.Origin.Y * S);
		if (strings?.Text(WaypointIndicator.CaptionGroup, AutoTrackLegendIndex) is { Length: > 0 } text) {
			drawCentered(AutoTrackLegendFont, text, legend.Origin.X, legend.Origin.Y,
				legend.Origin.X + legend.Size.Width, legend.Origin.Y + legend.Size.Height, null);
		}
	}

	/// <summary><c>HUD</c> bank frame 14, the 50x16 plate behind the legend.</summary>
	private const int AutoTrackLegendPlateFrame = 14;

	/// <summary><c>STRINGS0.STR</c> group 37 entry 0, <c>"ATT"</c> — the entry before the waypoint caption's.</summary>
	private const int AutoTrackLegendIndex = 0;

	/// <summary><c>ColorSchemePanels[12]</c>, the font the gunsight constructor gives both its labels.</summary>
	private const string AutoTrackLegendFont = "DARK";

	/// <summary>
	/// The speed and mission-time readouts under the reticle, laid out as
	/// <c>Gau_RovingGunsightWidget</c> (<c>0043c7d8</c>) lays them out from the two anchor points in
	/// the gunsight complex's own <c>.GAU</c> block:
	///
	/// <list type="bullet">
	/// <item>the anchor at 1128/1132 is the <b>left</b> edge of "SPEED:"; the value follows two GAU
	/// pixels past the caption's measured width;</item>
	/// <item>the anchor at 1120/1124 is the <b>right</b> edge of the time field, whose left edge is
	/// that minus the measured width of "00000" — a five-digit reservation — with the "TIME:" caption
	/// right-aligned two GAU pixels before it.</item>
	/// </list>
	///
	/// <para>Captions use <c>HUD2</c> and values <c>HUD3</c>, the constructor's own font choices
	/// (<c>0049b0ec</c> and <c>0049b0f0</c>, entries 16 and 17 of <c>ColorSchemePanels</c>). That is
	/// where the retail screenshot's pale yellow-green captions and cyan values come from: they are
	/// theater palette indices 73 and 74, not colours the widget picks.</para>
	/// </summary>
	internal static void AddGunsightReadouts(GAUFile gau, HudSpriteSheet sprites, CockpitHudState state,
			Func<string, string, float, float, float> drawText) {
		if (gau.RemainderBeforeReticle is not { Length: >= 16 } anchors
			|| sprites.Font("HUD2") is not { } captions
			|| sprites.Font("HUD3") is not { } values) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		int Anchor(int offset) => BitConverter.ToInt32(anchors, offset);

		float timeRight = Anchor(0) * S;
		float speedLeft = Anchor(8) * S;
		float row = Anchor(12) * S;

		float speedEnd = drawText("HUD2", SpeedCaption, speedLeft, row);
		drawText("HUD3", $"{state.SpeedKph} K/H", speedEnd + ReadoutGap, row);

		float timeLeft = timeRight - values.Measure(TimeFieldReservation);
		drawText("HUD3", state.MissionTime, timeLeft, row);
		drawText("HUD2", TimeCaption, timeLeft - ReadoutGap - captions.Measure(TimeCaption), row);
	}

	/// <summary>The two captions, and the five-digit field the time value is right-anchored through.</summary>
	private const string SpeedCaption = "SPEED:";

	private const string TimeCaption = "TIME:";

	private const string TimeFieldReservation = "00000";

	/// <summary>The gap between a caption and its value: the constructor's own <c>2 &lt;&lt; XCoordShift</c>, four device pixels wide.</summary>
	private const float ReadoutGap = 2 * CockpitArt.GauToPixelScale;
}
