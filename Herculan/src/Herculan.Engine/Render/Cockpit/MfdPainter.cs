using Herculan.Engine.Cockpit;
using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The multi-function display, painted into the front panel's batch — see <see cref="AddMfd"/>.
/// </summary>
internal sealed class MfdPainter {
	private readonly Overlay2DRenderer _overlay;

	public MfdPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
	}

	/// <summary>
	/// The multi-function display, built the way <c>MfdDisplay_Ctor</c> (<c>00445218</c>) builds it —
	/// see <see cref="MfdLayout"/> for where every rect comes from and
	/// docs/retail/simulation/cockpit-hud-widgets.md for the panel it sits in.
	///
	/// <list type="number">
	/// <item>the screen background, whichever <c>MFD</c> frame the current mode selects — see
	/// <see cref="MfdLayout.BackgroundFrame"/>, and note that frame 0 is never one of them — at the
	/// panel rect inset 18 GAU units from the left; its 196x122 art fills that inset region exactly;</item>
	/// <item>the F1-F6 mode column down the strip the inset left free, each button lit (frame 4) for
	/// the current screen and unlit (frame 3) otherwise, captioned "F1".."F6" — the original composes
	/// those from its own <c>"Fx"</c> literal rather than storing six strings;</item>
	/// <item>the aux buttons the current screen shows, from the 6x13 visibility table — SELECT on the
	/// two status screens, XMIT on FLASH COMM, PASS/ACTIVE/RANGE/TARGET on the scanner, none on the
	/// nav map or missile cam;</item>
	/// <item>the screen title, and then whichever screen's own content is implemented.</item>
	/// </list>
	///
	/// <para>A lit button captions in <c>DARK</c> and an unlit one in <c>WHITE</c>, which is
	/// <c>MfdButton_Repaint</c> (<c>004474e4</c>)'s own choice: it picks <c>ColorSchemePanels[12]</c> when the button's
	/// <c>+0x40</c> lit flag is set and <c>[10]</c> when it is clear.</para>
	///
	/// <para><b>Each screen draws its own content.</b> STATUS and TARGET STATUS share one
	/// method off one <see cref="MfdStatusSubject"/> — the squad roster's current entry for F1, the
	/// current selection for F5 — SCANNER plots live contacts, and FLASH COMM lists the string table's order
	/// rows. NAV MAP draws the terrain raster turned to the machine's heading, through
	/// <paramref name="drawNavMapTerrain"/>, and its centre cross; MISSILE CAM draws whatever its
	/// screen last painted, the world from the round going through <paramref name="drawMissileView"/>.</para>
	/// </summary>
	internal static void AddMfd(CockpitArt hud, CockpitHudState state,
			Action<string, int, float, float> blitDevice,
			Action<string, int, float, float, short> blitRotatedDevice,
			MfdTextWriter drawText,
			Action<float, float, float, float, Vector3> fillRect,
			NavMapTerrainWriter? drawNavMapTerrain = null,
			MissileViewWriter? drawMissileView = null) {
		if (hud.Sprites is not { } sprites || MfdLayout.InsetOrigin(hud.Gau) is not { } inset) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		float insetX = inset.X * S;
		float insetY = inset.Y * S;
		var strings = hud.Strings;

		// Device-pixel positions measured from the inset origin, which is the space every rect in
		// MfdLayout is expressed in once scaled.
		float X(int gau) => insetX + gau * S;
		float Y(int gau) => insetY + gau * S;

		// Places one label in a device-pixel rect through the shared Label_SetRect/Label_SetText rule
		// — see HudFont.Place, which is where that pair's arithmetic lives.
		void DrawLabel(string font, string text, float x0, float y0, float x1, float y1,
				LabelAlign align, float marginX = 0f) {
			DrawHotkeyLabel(font, string.Empty, text, -1, x0, y0, x1, y1, align, marginX);
		}

		// The same placement with one character re-fonted — the form the FLASH COMM rows are built in.
		void DrawHotkeyLabel(string font, string alternateFont, string text, int hotkeyIndex,
				float x0, float y0, float x1, float y1, LabelAlign align, float marginX = 0f) {
			if (sprites.Font(font) is not { } metrics) {
				return;
			}

			var (textX, textY) = metrics.Place(text, (int)x0, (int)y0, (int)x1, (int)y1,
				align, (int)marginX);
			drawText(font, alternateFont, text, hotkeyIndex, textX, textY);
		}

		if (MfdLayout.BackgroundFrame(state.Mfd) is { } background) {
			blitDevice(MfdLayout.Bank, background, insetX, insetY);
		}

		// The nav map owns the whole inset region and its paint (MfdMapScreen_Paint) floods that rect with
		// COLORS.DAT id 19 — black, the same id the gauge remainder resolves through — before
		// blitting terrain into it. That flood is why the repaint blits no chrome for this mode at
		// all, and it goes down before the buttons and title for the same reason the original paints
		// them after. It is also all that shows where the turned raster does not reach.
		if (state.Mfd == MfdMode.NavMap
			&& hud.GaugeColors?.Remainder is { } mapBackground
			&& sprites.Sprite(MfdLayout.Bank, 0) is { } screen) {
			fillRect(insetX, insetY, insetX + screen.Width, insetY + screen.Height, mapBackground);
		}

		// Which buttons this mode shows, where they sit and which are lit all come from
		// CockpitWidgets, so the same answers drive the click regions — see that type. Only the
		// caption's own right and bottom bounds are taken from the table directly: those are a text
		// layout box, not the button's extent, and the original measures them to the rect's inclusive
		// GAU edge rather than to the last pixel its sprite covers.
		foreach (var widget in CockpitWidgets.VisibleMfdButtons(hud, state)) {
			int i = widget.Id.Index;
			var button = MfdLayout.Buttons[i];

			blitDevice(MfdLayout.Bank, widget.Lit ? button.LitFrame : button.UnlitFrame, widget.X0, widget.Y0);
			if (MfdLayout.Caption(strings, i) is { Length: > 0 } caption) {
				// The plate follows Lit, the caption follows Selected. MfdButton_Repaint is the only
				// thing that re-fonts a caption, and it reads the button's own selection flag (+0x40),
				// not the press byte — so a held button lights its plate with its text unchanged.
				DrawLabel(widget.Selected ? "DARK" : "WHITE", caption,
					widget.X0, widget.Y0, X(button.X1), Y(button.Y1), LabelAlign.Center);
			}
		}

		// The sensor dropout returns from MfdDisplay_Update before the screen, the transmission and the
		// title, and the wipe's sequencer blits its frames at the display's own rect — the inset. The
		// last one stays up for the whole dark spell, since nothing paints over it.
		if (state.Dropout.MfdHidden) {
			if (state.Dropout.MfdFrame is { } wipe) {
				blitDevice(SensorDropout.MfdBank, wipe, insetX, insetY);
			}

			return;
		}

		switch (state.Mfd) {
			case MfdMode.Status:
				AddMfdStatusScreen(hud, state.StatusSubject, blitDevice, DrawLabel, fillRect, X, Y);
				break;
			case MfdMode.TargetStatus:
				AddMfdStatusScreen(hud, state.TargetSubject, blitDevice, DrawLabel, fillRect, X, Y);
				break;
			case MfdMode.FlashComm:
				AddMfdFlashComm(hud, state, strings, blitDevice, DrawHotkeyLabel, insetX, insetY);
				break;
			// While the display is powering up, the dish's animation stands where the screen would be.
			// Its frames are the dish's own size and opaque, so nothing of the screen needs drawing under.
			case MfdMode.Scanner when state.MfdPowerUpFrame is { } powerUpFrame:
				blitDevice(CockpitPowerUp.MfdBank, powerUpFrame,
					X(MfdScanner.DiscOrigin.X), Y(MfdScanner.DiscOrigin.Y));
				break;
			case MfdMode.Scanner:
				AddMfdScanner(hud, state.Scanner, state.TorsoTwist,
					blitDevice, blitRotatedDevice, fillRect, DrawLabel, X, Y);
				break;
			case MfdMode.NavMap:
				// Its background is flooded above, before the buttons and title go down over it. A
				// transmission skips the screen's paint altogether, so the relief is not drawn under it.
				if (state.Transmission is null && sprites.Sprite(MfdLayout.Bank, 0) is { } mapScreen
					&& MfdLayout.ScreenCentre(hud.Gau) is { } mapCentre) {
					AddMfdNavMap(hud, state.NavMap, drawNavMapTerrain, fillRect,
						insetX, insetY, mapScreen.Width, mapScreen.Height, mapCentre);
				}

				break;
			case MfdMode.MissileCam:
				AddMfdMissileCam(hud, state.MissileCam, fillRect, DrawLabel, drawMissileView, insetX, insetY, X, Y);
				break;
		}

		// A squadmate answering takes the whole screen: MfdDisplay_Update floods the inset and blits
		// the transmitting comm box's own frame there before it ever reaches the current screen's
		// update slot, so the order list is simply not drawn while a reply is coming in.
		// The power-up returns from MfdDisplay_Update before the transmission is drawn, too, and the
		// update draws none while the display has switched itself to the missile camera.
		bool transmissionShown = false;
		if (state.Transmission is { } transmission && state.MfdPowerUpFrame is null && !state.MissileCamHolding) {
			AddMfdTransmission(hud, transmission, blitDevice, DrawLabel, fillRect, insetX, insetY, X, Y);
			transmissionShown = true;
		}

		// The title goes down last, after the screen has painted — the repaint's own order, so a
		// screen that draws into the header strip cannot cover its own caption. Left-aligned, not
		// centred: the title passes alignment 1 where the button captions pass 2, and retail's own
		// "STATUS" starts 44 device pixels from the panel's left edge, which is this rect's left edge.
		//
		// A transmission has no title at all. The flood covers the header strip along with the rest
		// of the screen, and the branch that draws the pilot jumps past the title refresh as well as
		// past the screen update — the caption comes back with the full repaint the display queues
		// when the transmission ends. A display still powering up never reaches that branch, so it
		// keeps its title.
		if (!transmissionShown && MfdLayout.Title(strings, state.Mfd) is { Length: > 0 } title) {
			DrawLabel("WHITE", title,
				X(MfdLayout.TitleRect.X0), Y(MfdLayout.TitleRect.Y0),
				X(MfdLayout.TitleRect.X1), Y(MfdLayout.TitleRect.Y1), LabelAlign.Left);
		}
	}

	/// <summary>
	/// Receives the NAV MAP's inset rect and centre in device pixels, and the map state to turn and
	/// blit the terrain raster from — see <see cref="DrawMfdNavMapTerrain"/>.
	/// </summary>
	internal delegate void NavMapTerrainWriter(float x0, float y0, float x1, float y1,
		Vector2 centre, MfdNavMapState navMap);

	/// <summary>
	/// F3's NAV MAP, <c>MfdMapScreen_Paint</c>: the terrain raster, then the cross. The flood under
	/// both is <see cref="AddMfd"/>'s. See <see cref="MfdNavMap"/>.
	/// </summary>
	private static void AddMfdNavMap(CockpitArt hud, MfdNavMapState navMap,
			NavMapTerrainWriter? drawTerrain, Action<float, float, float, float, Vector3> fillRect,
			float insetX, float insetY, int width, int height, (int X, int Y) centre) {
		float centreX = insetX + centre.X;
		float centreY = insetY + centre.Y;

		if (navMap.Raster != null) {
			drawTerrain?.Invoke(insetX, insetY, insetX + width, insetY + height,
				new Vector2(centreX, centreY), navMap);
		}

		// Two one-pixel lines through the centre, inclusive of their end points.
		if (hud.LogicalColor(MfdNavMap.CrossColorId) is { } crossColor) {
			float arm = MfdNavMap.CrossArm * CockpitArt.GauToPixelScale;
			fillRect(centreX - arm, centreY, centreX + arm + 1f, centreY + 1f, crossColor);
			fillRect(centreX, centreY - arm, centreX + 1f, centreY + arm + 1f, crossColor);
		}
	}

	/// <summary>
	/// Receives the MISSILE CAM's screen rect — device pixels in <see cref="AddMfd"/>, panel pixels by
	/// the time it reaches <see cref="CanopyPanelPainter.Draw"/> — and the camera to draw the world from.
	/// </summary>
	internal delegate void MissileViewWriter(float x0, float y0, float x1, float y1, Camera view);

	/// <summary>
	/// F6's MISSILE CAM, as <see cref="MfdMissileCamScreen"/> last painted it: a flash, the world from
	/// the round under its cross and ring, or the labels. See <see cref="MfdMissileCam"/>.
	///
	/// <para>The floods take the screen chrome's 196x122 extent, as the display's other floods do here;
	/// the view's own flood stops one column short of the inset rect's right edge in the original.</para>
	/// </summary>
	private static void AddMfdMissileCam(CockpitArt hud, MfdMissileCamState cam,
			Action<float, float, float, float, Vector3> fillRect, MfdLabelWriter drawLabel,
			MissileViewWriter? drawView, float insetX, float insetY, Func<int, float> x, Func<int, float> y) {
		if (hud.Sprites?.Sprite(MfdLayout.Bank, MfdLayout.ScreenFrame) is not { } screen) {
			return;
		}

		float right = insetX + screen.Width;
		float bottom = insetY + screen.Height;

		switch (cam.Picture) {
			case MfdMissileCamPicture.Flash:
				if (hud.LogicalColor(cam.FlashColorId) is { } flash) {
					fillRect(insetX, insetY, right, bottom, flash);
				}

				break;

			case MfdMissileCamPicture.View:
				if (MfdLayout.ScreenCentre(hud.Gau) is not { } centre) {
					break;
				}

				if (hud.LogicalColor(MfdMissileCam.BackgroundColorId) is { } background) {
					fillRect(insetX, insetY, right - 1f, bottom, background);
				}

				drawView?.Invoke(insetX, insetY, right, bottom,
					MfdMissileCam.CameraFor(cam.View, screen.Width, screen.Height, centre));

				// Two Raster_DrawLine calls through the projection centre, the full width and the full
				// height of the context, then a one-pixel Raster_DrawEllipse ring about it.
				if (hud.LogicalColor(MfdMissileCam.MarkColorId) is { } mark) {
					float centreX = insetX + centre.X;
					float centreY = insetY + centre.Y;
					fillRect(centreX - centre.X, centreY, centreX + centre.X + 1f, centreY + 1f, mark);
					fillRect(centreX, centreY - centre.Y, centreX + 1f, centreY + centre.Y + 1f, mark);
					RasterPrimitives.AddCircleOutline(centreX, centreY, MfdMissileCam.RingRadius(hud.Gau), mark, fillRect);
				}

				break;

			case MfdMissileCamPicture.Labels:
				if (hud.PaletteEntry(MfdLayout.ScreenFillPaletteIndex) is { } flood) {
					fillRect(insetX, insetY, right, bottom, flood);
				}

				var strings = hud.Strings;

				// Label_SetText fills a label's plate before its text when the label carries a colour.
				void Label(int label, int entry, string font, int plateColorId = -1) {
					var rect = MfdMissileCam.LabelRects[label];
					if (plateColorId >= 0 && hud.LogicalColor(plateColorId) is { } plate) {
						fillRect(x(rect.X0), y(rect.Y0), x(rect.X1 + 1), y(rect.Y1 + 1), plate);
					}

					if (strings?.Text(MfdMissileCam.StringGroup, entry) is { Length: > 0 } text) {
						drawLabel(font, text, x(rect.X0), y(rect.Y0), x(rect.X1), y(rect.Y1), LabelAlign.Center, 0f);
					}
				}

				if (cam.ReadyToLaunch) {
					Label(MfdMissileCam.ReadyToLabel, MfdMissileCam.ReadyToEntry, MfdMissileCam.LabelFont);
					Label(MfdMissileCam.LaunchLabel, MfdMissileCam.LaunchEntry, MfdMissileCam.LabelFont);
				} else {
					Label(MfdMissileCam.LaunchLabel, MfdMissileCam.NoneEntry, MfdMissileCam.LabelFont);
				}

				if (cam.Locked) {
					Label(MfdMissileCam.LockLabel, MfdMissileCam.LockEntry, MfdMissileCam.LockFont,
						cam.LockBlinkLit ? MfdMissileCam.LockLitPlateColorId : MfdMissileCam.MarkColorId);
				} else {
					Label(MfdMissileCam.LockLabel, MfdMissileCam.LockEntry, MfdMissileCam.LockIdleFont,
						MfdMissileCam.LockIdlePlateColorId);
				}

				break;
		}
	}

	/// <summary>
	/// <c>HddMap_DrawTerrain</c> as the NAV MAP calls it: the raster's four corners projected about the
	/// machine at <see cref="MfdNavMap.Scale"/>, turned about the map centre by the heading with
	/// <c>Math_Rotate2DPoint</c>'s matrix, and clipped to the inset by a GL scissor — the analogue of
	/// the screen's own offscreen render target, as <see cref="HddMapPainter.DrawHddMap"/> uses it.
	/// </summary>
	internal void DrawMfdNavMapTerrain(MfdNavMapState navMap, GpuTexture mapTexture,
			float left, float top, float right, float bottom, Vector2 centre, float scale,
			int viewportX, int viewportY, int viewportHeight) {
		if (navMap.Raster is not { } raster) {
			return;
		}

		int scissorX = viewportX + (int)MathF.Floor(left);
		int scissorY = viewportY + (int)MathF.Floor(viewportHeight - bottom);
		int scissorW = Math.Max((int)MathF.Ceiling(right - left), 0);
		int scissorH = Math.Max((int)MathF.Ceiling(bottom - top), 0);
		if (scissorW == 0 || scissorH == 0) {
			return;
		}

		// A positive binary angle turns clockwise on a screen whose y runs down, which with the
		// heading's own sense puts the nose up.
		float cos = SimTrig.Cos(navMap.Heading) / 16384f;
		float sin = SimTrig.Sin(navMap.Heading) / 16384f;
		Vector2 Corner(int worldX, int worldY) {
			var p = MfdNavMap.Project(navMap, worldX, worldY) * scale;
			return centre + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
		}

		_overlay.SetScissor(scissorX, scissorY, scissorW, scissorH);

		_overlay.AddTexturedQuad(Corner(raster.WorldX0, raster.WorldY1), Corner(raster.WorldX1, raster.WorldY1),
			Corner(raster.WorldX1, raster.WorldY0), Corner(raster.WorldX0, raster.WorldY0), 0f, 0f, 1f, 1f);
		_overlay.Submit(mapTexture);

		_overlay.ClearScissor();
	}

	/// <summary>
	/// The status screen shared by F1 and F5 (<c>0043a2e0</c>): five stacked labels down the left of
	/// the screen and the subject's damage diagram in a viewport whose left edge is the labels' right
	/// edge. One method for both keys, because there is one screen class for both in the original too
	/// — F1 and F5 differ only in <paramref name="subject"/>.
	///
	/// <list type="number">
	/// <item><c>ID:</c> for the player's own machine, <c>TARGET:</c> for anything else;</item>
	/// <item>the subject's name, in green for one of ours and red for a Cybrid — the paint's own font
	/// override, read from the subject's mission group and not from the label's constructor;</item>
	/// <item><c>STATUS:</c>, always;</item>
	/// <item>its condition, from <see cref="MfdLayout.ConditionGroup"/>;</item>
	/// <item>a structural-integrity percentage for one of ours, its range for a hostile.</item>
	/// </list>
	///
	/// <para>The viewport holds a machine's own paper doll — <c>hba\&lt;HERC&gt;.HBA</c> frame 2, the
	/// compact third view of the three its <c>.PDG</c> describes — placed by that view's origin and
	/// then tinted region by region (see <see cref="PaperDollPainter.AddPaperDollTint"/>), or a flat silhouette from the
	/// <c>BASES</c>, <c>VEHICLES</c> or <c>FLYERS</c> bank centred in the viewport.</para>
	/// </summary>
	private static void AddMfdStatusScreen(CockpitArt hud, MfdStatusSubject subject,
			Action<string, int, float, float> blitDevice, MfdLabelWriter drawLabel,
			Action<float, float, float, float, Vector3> fillRect,
			Func<int, float> x, Func<int, float> y) {
		const float S = CockpitArt.GauToPixelScale;
		var strings = hud.Strings;

		void Label(int index, string? text, string font) {
			if (text is { Length: > 0 }) {
				drawLabel(font, text,
					x(MfdLayout.StatusLabelX), y(MfdLayout.StatusLabelY[index]),
					x(MfdLayout.WireframeRect.X0),
					y(MfdLayout.StatusLabelY[index] + MfdLayout.StatusLabelHeight),
					LabelAlign.Left, 0f);
			}
		}

		// No subject at all: the caption reads TARGET:, the name reads NONE in the unknown font, and
		// the other three labels are cleared. The paint writes literal empty strings into them.
		if (!subject.Present) {
			Label(0, strings?.Text(MfdLayout.IdentLabelGroup, MfdLayout.IdentTargetEntry), "WHITE");
			Label(1, strings?.Text(MfdLayout.NoTargetNameGroup, 0), MfdLayout.UnknownNameFont);
			return;
		}

		Label(0, strings?.Text(MfdLayout.IdentLabelGroup,
			subject.Own ? MfdLayout.IdentSelfEntry : MfdLayout.IdentTargetEntry), "WHITE");

		// A class the screen's switch does not recognise stops here too, name and all — the paint
		// leaves the status labels holding whatever they last said rather than clearing them.
		//
		// A scramble (MfdStatusSubject.Scrambled) swaps the name, the condition and the integrity or range for
		// runs of X, on every class but the empty one.
		bool scrambled = subject.Scrambled;
		if (!subject.Identified) {
			Label(1, scrambled ? MfdLayout.ScrambledName : strings?.Text(MfdLayout.UnknownNameGroup, 0),
				MfdLayout.UnknownNameFont);
			return;
		}

		Label(1, scrambled ? MfdLayout.ScrambledName : subject.Name,
			subject.Hostile ? MfdLayout.HostileNameFont : MfdLayout.FriendlyNameFont);
		Label(2, strings?.Text(MfdLayout.StatusLabelGroup, 0), MfdLayout.StatusLabelFonts[2]);
		Label(3, scrambled ? MfdLayout.ScrambledCondition : strings?.Text(MfdLayout.ConditionGroup, subject.Condition),
			MfdLayout.StatusLabelFonts[3]);
		if (subject.Hostile && scrambled) {
			Label(4, MfdLayout.DistanceReadout(strings, MfdLayout.ScrambledRange), MfdLayout.StatusLabelFonts[4]);
		} else if (!subject.Hostile && scrambled) {
			Label(4, MfdLayout.ScrambledIntegrity, MfdLayout.StatusLabelFonts[4]);
		} else if (subject.Hostile) { // The range, already in the units it prints in (MfdStatusSubject.Range).
			Label(4, MfdLayout.DistanceReadout(strings, subject.Distance), MfdLayout.StatusLabelFonts[4]);
		} else { // A friendly: the hull integrity.
			Label(4, MfdLayout.IntegrityReadout(subject.Damage), MfdLayout.StatusLabelFonts[4]);
		}

		switch (subject.SilhouetteKind) {
			// The paper doll blits at the viewport's top-left plus the .PDG view's own origin plus a
			// fixed (0x11, 2) device nudge — the paint's own arithmetic, not a centring rule. The view's
			// origin is authored in the 320-wide space like every other .PDG coordinate.
			//
			// A scramble skips the doll's blit and the pod highlight. The paint's tint pass still runs, but in
			// mode 0 it recolours only raster pixels holding a region's own colour, and with no doll blitted
			// the viewport holds the paint's 0x11 flood. Every retail view-2 region is mode 0 in COLORS.DAT id
			// 12 or 15 (palette 14 or 13), which the flood is neither as an index nor as an id (palette 24),
			// so it draws nothing either.
			case MfdSilhouetteKind.PaperDoll when subject.Scrambled:
				break;
			case MfdSilhouetteKind.PaperDoll
				when hud.PaperDollFor(subject.PaperDollName)?.Entries is { } views
					&& MfdLayout.WireframeViewIndex < views.Length
					&& views[MfdLayout.WireframeViewIndex] is { } view
					&& subject.SilhouetteBank != null:
				float dollLeft =
					x(MfdLayout.WireframeRect.X0) + view.Origin.X * S + MfdLayout.WireframeArtOffset.X;
				float dollTop =
					y(MfdLayout.WireframeRect.Y0) + view.Origin.Y * S + MfdLayout.WireframeArtOffset.Y;
				blitDevice(subject.SilhouetteBank, MfdLayout.WireframeViewIndex, dollLeft, dollTop);

				// Then the damage over it, region by region. The compact view merges components — one
				// torso region over both cockpit halves, one limb region over each three-deep stack —
				// so what a region reads is not one component's number; see
				// PaperDollDamage.StatusRegionReading.
				if (subject.Readings is { } readings && view.Regions is { } regions
					&& hud.Sprites is { } dollSprites) {
					foreach (var region in regions) {
						PaperDollPainter.AddPaperDollTint(hud, dollSprites, subject.SilhouetteBank,
							MfdLayout.WireframeViewIndex, region,
							PaperDollDamage.StatusRegionReading(region.Index, subject.FlyerVariant, readings),
							dollLeft, dollTop, fillRect);
					}
				}

				// And last, over the tints: the Targeting Pod's component highlight. The paint stops at
				// the first region that holds the component, directly or through the merge mapping, and
				// fills its rect one device pixel proud on each side.
				if (subject.Hostile && subject.HighlightComponent >= 0 && view.Regions is { } highlightRegions
					&& hud.LogicalColor(MfdLayout.ComponentHighlightColorId) is { } highlight) {
					int alias = MfdLayout.ComponentHighlightRegion(subject.HighlightComponent);
					foreach (var region in highlightRegions) {
						if (region.Index != subject.HighlightComponent && region.Index != alias) {
							continue;
						}

						fillRect(
							dollLeft + region.TopLeft.X * S - 1, dollTop + region.TopLeft.Y * S - 1,
							dollLeft + region.BottomRight.X * S + 2, dollTop + region.BottomRight.Y * S + 2,
							highlight);
						break;
					}
				}

				break;

			// A flat silhouette is centred in the viewport instead, by its own frame size — the paint
			// computes ((x1 - x0) - width) / 2 on both axes.
			case MfdSilhouetteKind.Silhouette
				when subject.SilhouetteBank != null
					&& hud.Sprites?.Sprite(subject.SilhouetteBank, subject.SilhouetteFrame) is { } art:
				float width = art.Width * art.Scale;
				float height = art.Height * art.Scale;
				blitDevice(subject.SilhouetteBank, subject.SilhouetteFrame,
					x(MfdLayout.WireframeRect.X0)
						+ (x(MfdLayout.WireframeRect.X1) - x(MfdLayout.WireframeRect.X0) - width) / 2f,
					y(MfdLayout.WireframeRect.Y0)
						+ (y(MfdLayout.WireframeRect.Y1) - y(MfdLayout.WireframeRect.Y0) - height) / 2f);
				break;
		}
	}

	/// <summary>
	/// FLASH COMM's order list (<c>MfdFlashCommScreen_Paint</c> (<c>0043f7a4</c>)): six evenly stacked rows spanning almost the whole
	/// screen, 7 GAU units apart, each naming one of the squad orders in
	/// <see cref="MfdLayout.OrderGroup"/>.
	///
	/// <para>Three things vary per row and all three come from <see cref="MfdFlashCommScreen"/>. The
	/// <b>verb</b> is the row index, or that index plus 3 when the row has been toggled, so rows 4 and
	/// 5 read <c>EMCON</c> and <c>HOLD YOUR FIRE</c> once those orders have been given. The <b>font</b>
	/// is <c>CPYLW</c> for the row the cursor is on, <c>CPOFF</c> for one the squad cannot take and
	/// <c>CPGREEN</c> otherwise. And <b>one character</b> of every row is redrawn in <c>CPRED</c>, at
	/// the index the order's own attribute byte names — the key that gives that order, stored beside
	/// the string rather than in the code, exactly as the [F7] command display's list does it.</para>
	///
	/// <para>The plate under the selected row is <c>MFD</c> frame 11, or 12 while XMIT is held: the
	/// frame index is <c>11 + </c> the XMIT button's own press byte, which is why pressing the button
	/// lights the row too. It is a hollow rounded rect and goes down <i>after</i> the text, which is
	/// the paint's own order.</para>
	/// </summary>
	private static void AddMfdFlashComm(CockpitArt hud, CockpitHudState state, StringFile? strings,
			Action<string, int, float, float> blitDevice, MfdHotkeyLabelWriter drawLabel,
			float insetX, float insetY) {
		var rows = MfdLayout.FlashCommRows;
		var orders = strings?.Group(MfdLayout.OrderGroup);
		var page = state.FlashComm;

		if (orders == null) {
			return;
		}

		for (int i = 0; i < MfdLayout.FlashCommRowCount; i++) {
			int verb = page.Verb(i);
			if (verb < 0 || verb >= orders.Count || orders[verb].Text is not { Length: > 0 } text) {
				continue;
			}

			bool selected = i == page.SelectedRow;
			string font = selected ? MfdLayout.FlashCommSelectedFont
				: page.CanTake(i) ? MfdLayout.FlashCommFont
				: MfdLayout.FlashCommUnavailableFont;

			// Unlike the command display's list, the alternate is passed on every row: the page
			// re-fonts the whole line rather than dropping the hotkey, so even the selected yellow row
			// keeps its red letter.
			int hotkey = orders[verb].Attributes is { Length: > 0 } attributes ? attributes[0] : -1;

			float top = insetY + rows.Y0 + i * rows.RowHeight;
			drawLabel(font, MfdLayout.FlashCommHotkeyFont, text, hotkey,
				insetX + rows.X0, top, insetX + rows.X1, top + rows.RowHeight,
				LabelAlign.Left, MfdLayout.FlashCommTextMarginX);
		}

		int plate = state.ShowsPressed(CockpitWidgetId.Mfd(MfdLayout.TransmitButton))
			? MfdLayout.FlashCommRowPlatePressedFrame
			: MfdLayout.FlashCommRowPlateFrame;

		blitDevice(MfdLayout.Bank, plate,
			insetX + rows.X0, insetY + rows.Y0 + page.SelectedRow * rows.RowHeight);
	}

	/// <summary>
	/// A squadmate answering on the radio (<c>MfdDisplay_Update</c>, <c>00446328</c>): the inset is
	/// flooded, one frame of that pilot's video is blitted at <see cref="MfdLayout.TransmissionOrigin"/>
	/// plus the frame's own <c>.OFS</c> offset, and — only while the picture is up rather than the
	/// static either side of it — their name goes in the message label on their own comm-box colour.
	///
	/// <para>This runs whichever of the six screens is selected. The update draws it before it ever
	/// reaches the current screen's update slot and jumps past that slot afterwards, so a transmission
	/// replaces the screen rather than sitting on it.</para>
	///
	/// <para>The frame is blitted doubled, as every <c>dba</c>-only bank is, but the offset pair is
	/// added raw — see <see cref="SquadTransmission.OffsetX"/>.</para>
	/// </summary>
	private static void AddMfdTransmission(CockpitArt hud, SquadTransmission transmission,
			Action<string, int, float, float> blitDevice, MfdLabelWriter drawLabel,
			Action<float, float, float, float, Vector3> fillRect,
			float insetX, float insetY, Func<int, float> x, Func<int, float> y) {
		const float S = CockpitArt.GauToPixelScale;

		// The flood is the inset rect the display carries at +0xeb, whose extent is the screen
		// chrome's own frame — and a sprite's Width/Height are already device pixels, so the GAU
		// doubling must not be applied to them a second time.
		if (hud.Sprites?.Sprite(MfdLayout.Bank, MfdLayout.ScreenFrame) is { } screen
			&& hud.PaletteEntry(MfdLayout.ScreenFillPaletteIndex) is { } fill) {
			fillRect(insetX, insetY, insetX + screen.Width, insetY + screen.Height, fill);
		}

		blitDevice(transmission.Bank, transmission.Frame,
			insetX + MfdLayout.TransmissionOrigin.X * S + transmission.OffsetX,
			insetY + MfdLayout.TransmissionOrigin.Y * S + transmission.OffsetY);

		if (!transmission.ShowName || transmission.Name is not { Length: > 0 } name) {
			return;
		}

		var label = MfdLayout.MessageRect;
		if (hud.LogicalColor(transmission.NameColorId) is { } plate) {
			fillRect(x(label.X0), y(label.Y0), x(label.X1 + 1), y(label.Y1 + 1), plate);
		}

		drawLabel(MfdLayout.MessageFont, name,
			x(label.X0), y(label.Y0), x(label.X1), y(label.Y1), LabelAlign.Center, 0f);
	}

	/// <summary>
	/// The SCANNER screen (<c>MfdRadarScreen_Paint</c>, <c>0043eecc</c>), in the original's own paint order: the dish rect is
	/// flooded, the turret wedge goes down, the dish art covers it everywhere but its transparent
	/// interior, then the passive-range ring, the 12-o'clock reference line, the player marker, the
	/// contacts, the target bracket and finally the four corner readouts.
	///
	/// <para>Two of those are worth spelling out. The <b>wedge</b> is one sprite rotated about its own
	/// corner, which sits on the plot centre — see <c>BlitRotatedDevice</c> inside
	/// <see cref="CanopyPanelPainter.AddWidgets"/>. The <b>ring</b> appears only
	/// while the machine is passive and only on the 1200 m setting, because the paint tests both the
	/// mode and <c>140000 &lt; range</c> before drawing it.</para>
	///
	/// <para>Contacts are plotted by integer division of their world-unit offset, exactly as the
	/// original does it — the plot is quantised to whole device pixels, which is why blips visibly
	/// snap rather than slide at the longest range.</para>
	/// </summary>
	private static void AddMfdScanner(CockpitArt hud, MfdScannerState scanner, short torsoTwist,
			Action<string, int, float, float> blitDevice,
			Action<string, int, float, float, short> blitRotatedDevice,
			Action<float, float, float, float, Vector3> fillRect,
			MfdLabelWriter drawLabel,
			Func<int, float> x, Func<int, float> y) {
		const float S = CockpitArt.GauToPixelScale;
		var strings = hud.Strings;
		var background = hud.PaletteEntry(MfdScanner.BackgroundPaletteIndex);

		float discLeft = x(MfdScanner.DiscOrigin.X);
		float discTop = y(MfdScanner.DiscOrigin.Y);
		float centerX = x(MfdScanner.Center.X);
		float centerY = y(MfdScanner.Center.Y);

		// The flood covers the dish art's own extent — the constructor builds that rect from the
		// frame's size, not from a stated one.
		if (hud.Sprites?.Sprite(MfdScanner.Bank, MfdScanner.DiscFrame) is { } dish && background is { } fill) {
			fillRect(discLeft, discTop, discLeft + dish.Width * dish.Scale,
				discTop + dish.Height * dish.Scale, fill);
		}

		blitRotatedDevice(MfdScanner.Bank, MfdScanner.WedgeFrame, centerX, centerY,
			unchecked((short)(torsoTwist + MfdScanner.WedgeAngleOffset)));
		blitDevice(MfdScanner.Bank, MfdScanner.DiscFrame, discLeft, discTop);

		int worldPerPixel = scanner.WorldUnitsPerPixel;
		if (scanner.Passive && MfdScanner.PassiveRingRange < scanner.Range
			&& hud.LogicalColor(MfdScanner.PassiveRingColorId) is { } ring) {
			RasterPrimitives.AddCircleOutline(centerX, centerY, MfdScanner.PassiveRingRange / worldPerPixel, ring, fillRect);
		}

		blitDevice(MfdScanner.Bank, MfdScanner.ReferenceLineFrame,
			x(MfdScanner.ReferenceLineOrigin.X), y(MfdScanner.ReferenceLineOrigin.Y));
		blitDevice(MfdScanner.Bank, MfdScanner.PlayerMarkerFrame,
			centerX - MfdScanner.PlayerMarkerOffsetX * S, centerY);

		var contacts = scanner.Plotted;
		for (int i = 0; i < contacts.Count; i++) {
			if (hud.LogicalColor(contacts[i].ColorId) is not { } color) {
				continue;
			}

			float blipX = centerX + contacts[i].X / worldPerPixel;
			float blipY = centerY + contacts[i].Y / worldPerPixel;
			fillRect(blipX, blipY, blipX + MfdScanner.BlipSize, blipY + MfdScanner.BlipSize, color);
		}

		if (scanner.TargetContact >= 0 && scanner.TargetContact < contacts.Count) {
			var target = contacts[scanner.TargetContact];
			blitDevice(MfdScanner.Bank, MfdScanner.TargetBracketFrame,
				centerX + target.X / worldPerPixel - MfdScanner.TargetBracketOffset * S,
				centerY + target.Y / worldPerPixel - MfdScanner.TargetBracketOffset * S);
		}

		// Each readout paints its own background before its text — the label objects carry background
		// id 0x11, the same colour the dish's own corners are, which is what keeps the four boxes
		// invisible against it.
		void Readout((int X0, int Y0, int X1, int Y1) rect, string? text, LabelAlign align) {
			if (text is not { Length: > 0 }) {
				return;
			}

			if (background is { } labelFill) {
				fillRect(x(rect.X0), y(rect.Y0), x(rect.X1), y(rect.Y1), labelFill);
			}

			drawLabel(MfdScanner.ReadoutFont, text,
				x(rect.X0), y(rect.Y0), x(rect.X1), y(rect.Y1), align, 0f);
		}

		Readout(MfdScanner.TargetCaptionRect, strings?.Text(MfdScanner.TargetCaptionGroup, 0), LabelAlign.Left);
		Readout(MfdScanner.RangeValueRect,
			MfdScanner.Readout(MfdScanner.WorldUnitsToMetres(scanner.Range)), LabelAlign.Right);
		Readout(MfdScanner.RangeCaptionRect, strings?.Text(MfdScanner.RangeCaptionGroup, 0), LabelAlign.Left);
		Readout(MfdScanner.TargetValueRect,
			MfdScanner.Readout(scanner.TargetRangeMetres), LabelAlign.Right);
	}

	/// <summary>
	/// Places one MFD label: a font, its text, the device-pixel rect it sits in, how it anchors
	/// horizontally, and how far its text is indented from the anchoring edge. Vertical centring is
	/// unconditional — see the implementation inside <see cref="AddMfd"/> for why.
	/// </summary>
	private delegate void MfdLabelWriter(string font, string text,
		float x0, float y0, float x1, float y1, LabelAlign align, float marginX);

	/// <summary>
	/// <see cref="MfdLabelWriter"/> with the alternate font one character is re-drawn in, and that
	/// character's index. Only the FLASH COMM rows need it.
	/// </summary>
	private delegate void MfdHotkeyLabelWriter(string font, string alternateFont, string text,
		int hotkeyIndex, float x0, float y0, float x1, float y1, LabelAlign align, float marginX);

	/// <summary>One run of glyphs, with the character at <c>hotkeyIndex</c> in an alternate font.</summary>
	internal delegate float MfdTextWriter(string font, string alternateFont, string text,
		int hotkeyIndex, float deviceLeft, float deviceTop);
}
