using Herculan.Engine.Cockpit;
using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Settings;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The Heads-Down Display: the <c>(herc).HB1</c> art and the display's live content over it — see
/// <see cref="Draw"/>. The command display's map is <see cref="HddMapPainter"/>'s.
/// </summary>
public sealed class HeadsDownPainter {
	private readonly Overlay2DRenderer _overlay;
	private readonly HddMapPainter _map;

	public HeadsDownPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
		_map = new HddMapPainter(overlay);
	}

	/// <summary>
	/// Draws the Heads-Down Display's background — <c>(herc).HB1</c> — filling the given viewport
	/// edge to edge horizontally.
	///
	/// <para>The art itself is placed exactly as <see cref="CanopyPanelPainter.Draw"/> places a cockpit panel: fit by
	/// height at its own 4:3 aspect ratio, horizontally centered, never stretched. On a window wider
	/// than 4:3 that leaves a margin on each side, and this fills those margins by stretching the
	/// art's own outermost pixel column outward. That is a Herculan addition with no original behind
	/// it — DBSIM only ever ran at 4:3 and had no margins to fill — chosen because the HDD's art is a
	/// flat console surround whose edge columns are near-uniform, so the stretch reads as the panel
	/// continuing rather than as a smear. The alternative, letting the cleared framebuffer show
	/// through at the flanks, reads as a hole in the cockpit.</para>
	/// </summary>
	/// <param name="hud">
	/// The cockpit whose Heads-Down Display widgets to overlay, or null to draw the <c>.HB1</c> art
	/// alone. Everything drawn comes from <see cref="CockpitArt.HeadsDownLayout"/>, which is the
	/// herc's own <c>.GAU</c> block rather than a hardcoded layout — see <see cref="HddLayout"/>.
	/// </param>
	/// <param name="mapTexture">
	/// The command display's terrain raster (<see cref="HddMapRaster"/>), or null to leave the map
	/// viewport empty. Built once per mission, exactly as the original builds its own bitmap.
	/// </param>
	public void Draw(int viewportX, int viewportY, int viewportWidth, int viewportHeight,
			GpuTexture texture, int textureWidth, int textureHeight,
			CockpitArt? hud = null, GpuTexture? spriteTexture = null, CockpitHudState? hudState = null,
			GpuTexture? mapTexture = null) {
		_overlay.Begin(viewportX, viewportY, viewportWidth, viewportHeight);

		float scale = viewportHeight / (float)textureHeight;
		float quadWidth = textureWidth * scale;
		float quadX0 = (viewportWidth - quadWidth) / 2f;
		float quadX1 = quadX0 + quadWidth;

		_overlay.Clear();

		// Texel centres, so nearest sampling lands on the outermost column and not on whatever the
		// clamp boundary rounds to.
		float leftEdgeU = 0.5f / textureWidth;
		float rightEdgeU = 1f - 0.5f / textureWidth;

		if (quadX0 > 0f) {
			_overlay.AddTexturedQuad(0f, 0f, quadX0, viewportHeight, leftEdgeU, 0f, leftEdgeU, 1f);
		}

		if (quadX1 < viewportWidth) {
			_overlay.AddTexturedQuad(quadX1, 0f, viewportWidth, viewportHeight, rightEdgeU, 0f, rightEdgeU, 1f);
		}

		_overlay.AddTexturedQuad(quadX0, 0f, quadX1, viewportHeight, 0f, 0f, 1f, 1f);

		_overlay.Submit(texture);

		// Second bind, second draw, exactly as CanopyPanelPainter.Draw layers the console instruments over .HB0: the
		// display's own sprites and glyphs live in the shared HUD atlas, not in the canopy texture.
		if (hud?.HeadsDownLayout is { } layout && hud.Sprites is { } sprites && spriteTexture != null) {
			_overlay.Clear();
			AddHeadsDown(hud, layout, sprites, scale, quadX0, hudState ?? CockpitHudState.Default);

			if (_overlay.VertexCount > 0) {
				_overlay.Submit(spriteTexture);
			}
		}

		// The map goes down last, and inside a scissor: it is a window onto a raster far larger than
		// itself, which is what the original's offscreen render target gives it for free, and
		// everything in the window — raster, grid, border, markers — is clipped by the same rect.
		// Last is also the display's own order: HddDisplay_Repaint paints the widgets and then hands
		// over to the current page.
		// HddCommandScreen_DrawMap floods the viewport and stops there while the sensor dropout has the
		// display dark, so the flood AddHddCommandDisplay left is all the map shows.
		if (hud?.HeadsDownLayout is { } mapLayout && hud.Sprites is { } mapSprites && spriteTexture != null
			&& (hudState ?? CockpitHudState.Default) is { Hdd: HddPage.CommandDisplay } mapState
			&& !mapState.Dropout.HeadsDownDark && mapState.Command.View is { } view) {
			_map.DrawHddMap(hud, mapLayout, mapSprites, spriteTexture, mapTexture, mapState, view,
				scale, quadX0, viewportX, viewportY, viewportHeight);
		}

		_overlay.End();
	}

	/// <summary>
	/// The Heads-Down Display's live content, built the way <c>HddDisplay_Ctor</c> (<c>00448cc8</c>) and its two page
	/// constructors build it — see <see cref="HddLayout"/> for where every rect and frame index comes
	/// from, and docs/retail/simulation/cockpit-views.md for the pan that reaches this view.
	///
	/// <list type="number">
	/// <item>the screen area, flooded with colour id 19 the way whichever page owns it floods it;</item>
	/// <item>that page's own content — the map viewport and order list, or the paper doll and
	/// component list;</item>
	/// <item>the widgets the page shows: two page buttons lit for the current page, four arrow
	/// buttons, the map's two magnifiers, and XMIT/CANCEL;</item>
	/// <item>the three squad comm boxes;</item>
	/// <item>the indicator block and the page title, which the display paints after everything
	/// else.</item>
	/// </list>
	///
	/// <para>The command display is drawn by <see cref="AddHddCommandDisplay"/> and
	/// <see cref="HddMapPainter.DrawHddMap"/>.</para>
	///
	/// <para>The page's content goes down before the widgets rather than after, which is the reverse
	/// of the display's own paint loop. That loop can afford the other order because a page only
	/// floods its screen on a full repaint; here every frame is a full repaint, and XMIT and CANCEL
	/// sit inside the screen rect, so painting the page last would erase them. The command display's
	/// map is the exception and is drawn after all of this by <see cref="HddMapPainter.DrawHddMap"/>: it owns a
	/// region nothing else reaches into, and it needs a scissor of its own.</para>
	/// </summary>
	private void AddHeadsDown(CockpitArt hud, HddLayout layout, HudSpriteSheet sprites,
			float scale, float quadX0, CockpitHudState state) {
		var strings = hud.Strings;

		// Device pixels relative to the .HB1 art's top-left, which is the space HddLayout reports in
		// and the space the sprite banks and .HFN glyphs are authored in.
		float Dx(float x) => quadX0 + x * scale;
		float Dy(float y) => y * scale;

		void Fill(HddLayout.Rect rect, Vector3 color) =>
			_overlay.AddFilledRect(Dx(rect.X0), Dy(rect.Y0), Dx(rect.X1 + 1), Dy(rect.Y1 + 1), color);

		void Blit(string bank, int frame, float left, float top) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var r = sprite.Rect;
			float drawn = sprite.Scale;
			_overlay.AddTexturedQuad(Dx(left), Dy(top), Dx(left + sprite.Width * drawn), Dy(top + sprite.Height * drawn),
				r.U0, r.V0, r.U1, r.V1);
		}

		// The same blit trimmed to a rect, for the one thing on this display that does not fit the box
		// it goes in: a comm box's picture. The original does it with a clip rect installed round the
		// blit (CommBox_PushVideoClip, 0044b83c), which is why a portrait taller than its box is cut off at the bezel
		// instead of hanging over it. Trimming the quad and its UVs by the same fraction keeps the
		// whole display one batch, exactly as the message ticker's per-glyph trim does.
		void BlitClipped(string bank, int frame, float left, float top, HddLayout.Rect clip) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			float drawn = sprite.Scale;
			float width = sprite.Width * drawn;
			float height = sprite.Height * drawn;
			float x0 = Math.Max(left, clip.X0), x1 = Math.Min(left + width, clip.X1 + 1);
			float y0 = Math.Max(top, clip.Y0), y1 = Math.Min(top + height, clip.Y1 + 1);
			if (x1 <= x0 || y1 <= y0) {
				return;
			}

			var r = sprite.Rect;
			_overlay.AddTexturedQuad(Dx(x0), Dy(y0), Dx(x1), Dy(y1),
				r.U0 + (r.U1 - r.U0) * ((x0 - left) / width),
				r.V0 + (r.V1 - r.V0) * ((y0 - top) / height),
				r.U1 - (r.U1 - r.U0) * ((left + width - x1) / width),
				r.V1 - (r.V1 - r.V0) * ((top + height - y1) / height));
		}

		// One run of glyphs left to right, with the character at hotkeyIndex drawn in an alternate
		// font — Label_SetTextWithHotkey's (00438aac) own behaviour, and where the order list's red hotkey letters come from.
		// Pass -1 for a plain run.
		void DrawText(string fontName, string alternateFont, string text, int hotkeyIndex,
				float left, float top) {
			if (sprites.Font(fontName) is not { } font) {
				return;
			}

			float pen = left;
			for (int i = 0; i < text.Length; i++) {
				string face = i == hotkeyIndex && sprites.Font(alternateFont) != null ? alternateFont : fontName;
				var metrics = sprites.Font(face)!;
				if (metrics.GlyphIndex(text[i]) is { } glyph) {
					Blit(face, glyph, pen, top);
					pen += metrics.Width(text[i]);
				}
			}
		}

		// Label_SetRect/Label_SetText's shared placement — see HudFont.Place.
		void DrawLabel(string fontName, string alternateFont, string text, int hotkeyIndex,
				HddLayout.Rect rect, bool centered, float marginX = 0f) {
			if (sprites.Font(fontName) is not { } metrics) {
				return;
			}

			var (textX, textY) = metrics.Place(text, rect.X0, rect.Y0, rect.X1, rect.Y1,
				centered ? LabelAlign.Center : LabelAlign.Left, (int)marginX);
			DrawText(fontName, alternateFont, text, hotkeyIndex, textX, textY);
		}

		var background = hud.HeadsDownColors?.Background;
		if (background is { } screenFill) {
			Fill(layout.Screen, screenFill);
		}

		// While the sensor dropout has the display dark the damage screen's update floods its rect and
		// draws nothing else — which the flood above already is, ids 3 and 19 both resolving to palette
		// 16 — and the command display loses its map and greys its orders.
		if (state.Hdd == HddPage.CommandDisplay) {
			AddHddCommandDisplay(hud, layout, sprites, strings, state, background, Blit, Fill, DrawLabel);
		} else if (!state.Dropout.HeadsDownDark) {
			AddHddDamageDetail(hud, layout, sprites, strings, state.HddDamage, state, Blit, Fill, DrawLabel,
				(x0, y0, x1, y1, color) => _overlay.AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		}

		// Visibility, position and lit state come from CockpitWidgets so the click regions agree with
		// what is drawn. The frame check stays here and stays a draw-side concern: a widget with no
		// sprite of its own — the title box, the dead slot — is still clickable in the original's flat
		// list, so CockpitWidgets reports it and only this loop skips it.
		foreach (var clickable in CockpitWidgets.VisibleHddWidgets(hud, state)) {
			// The order rows and the map region come back from the same enumeration so click and paint
			// agree on where they are, but they are not sprite-backed widgets and are drawn elsewhere.
			if (clickable.Id.AsHddWidget is not { } widget) {
				continue;
			}

			int i = clickable.Id.Index;
			if (layout.UnlitFrame(widget) is not { } unlit) {
				continue;
			}

			bool lit = clickable.Lit;
			var rect = layout[widget];
			Blit(HddLayout.Bank, lit ? unlit + 1 : unlit, rect.X0, rect.Y0);

			// A page button captions itself "F7"/"F8" from the shared "Fx" literal, in DARK for the
			// page that is showing and WHITE otherwise — the same pair the MFD's mode buttons use, and
			// keyed on selection rather than on Lit for the same reason (see CockpitWidget.Selected).
			// XMIT and CANCEL take their captions from the string table and their font from
			// ColorSchemePanels[4 + lit].
			if (widget is HddLayout.Widget.PageButton0 or HddLayout.Widget.PageButton1) {
				DrawLabel(clickable.Selected ? "DARK" : HddLayout.TitleFont, string.Empty,
					"F" + (7 + i), -1, rect, centered: true);
			} else if (widget is HddLayout.Widget.Transmit or HddLayout.Widget.Cancel
				&& strings?.Text(HddLayout.ButtonCaptionGroup, i - (int)HddLayout.Widget.Transmit)
					is { Length: > 0 } caption) {
				DrawLabel(HddLayout.TransmitButtonFont, string.Empty, caption, -1,
					layout.TransmitCaptionBox(widget), centered: true);
			}
		}

		// The three squad comm boxes, which belong to the display and not to either page — they are
		// drawn on the damage screen too. An occupied slot gets HddGauge_PaintIdle's flood and five
		// labels; an empty one gets the display's own fill of the box inset one device pixel, and no
		// labels at all.
		//
		// A box with a picture in it takes the other branch of the same dispatch: the two video paints
		// (HddGauge_PaintPilotFrame, HddGauge_PaintStatic) flood the same rect, blit one frame into it
		// and then refresh the name label and nothing else — so the plate stays and the four status
		// lines under it are simply not drawn that frame.
		if (background is { } boxFill) {
			var pilots = state.Command.PilotBoxes;
			var videos = state.PilotVideos;
			for (int i = 0; i < HddLayout.PilotSlotCount; i++) {
				var box = layout[HddLayout.Widget.PilotBox0 + i];
				Fill(box.Inset(1, 1), boxFill);

				if (i >= pilots.Count || !pilots[i].Occupied) {
					continue;
				}

				var pilot = pilots[i];
				var video = videos is not null && i < videos.Count ? videos[i] : null;

				// The frame's own .OFS pair places it inside the box, and is added raw while the frame
				// itself is blitted doubled — the same split the MFD's full-screen copy makes. Static
				// publishes (0, 0) and covers the box on its own.
				if (video is { } picture) {
					BlitClipped(picture.Bank, picture.Frame,
						box.X0 + picture.OffsetX, box.Y0 + picture.OffsetY, box.Inset(1, 1));
				}

				var nameBox = new HddLayout.Rect(box.X0 + 4, box.Y0 + 8, box.X1 - 4, box.Y0 + 8 + PilotLabelHeight);
				if (hud.LogicalColor(HudColorTable.PilotColorId(i)) is { } nameFill) {
					Fill(nameBox, nameFill);
				}

				DrawLabel(HddLayout.PilotNameFont, string.Empty, pilot.Name, -1, nameBox, centered: true);

				if (video is not null) {
					continue;
				}

				PilotLine(box, 32, HddLayout.PilotCaptionFont,
					strings?.Text(HddLayout.PilotCaptionGroup, 0));
				PilotLine(box, 48, HddLayout.PilotNameFont,
					strings?.Text(MfdLayout.ConditionGroup, pilot.ConditionIndex));
				PilotLine(box, 64, HddLayout.PilotCaptionFont,
					strings?.Text(HddLayout.PilotCaptionGroup, 1));
				PilotLine(box, 80, HddLayout.PilotNameFont,
					strings?.Text(HddLayout.PilotOrderGroup, pilot.OrderIndex));

				// The box's sixth label, bottom-left on colour id 15, which HddGauge_LoadPilotFrames (0044a7c0)
				// builds and nothing ever fills, so retail never shows it. Under the tweak it carries the
				// slot number — the key that selects this pilot. Read every frame, so toggling the tweak
				// shows or hides it at once.
				if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowSquadmateNumber)) {
					var slotBox = new HddLayout.Rect(box.X0, box.Y1 - 20, box.X0 + 20, box.Y1);
					if (hud.HeadsDownColors?.Indicator is { } slotFill) {
						Fill(slotBox, slotFill);
					}

					DrawLabel(HddLayout.PilotNameFont, string.Empty, (i + 1).ToString(), -1, slotBox, centered: true);
				}
			}

			// The marker beside the selected box, which is what the herc's own highlight mode 1 fills
			// rather than filling the box itself. The previously selected one goes back to id 13.
			for (int i = 0; i < HddLayout.PilotSlotCount && i < layout.PilotMarkers.Count; i++) {
				int colorId = i == state.Command.SelectedPilot
					? HddLayout.PilotMarkerSelectedColorId
					: HddLayout.PilotMarkerColorId;
				if (hud.LogicalColor(colorId) is { } markerColor) {
					Fill(layout.PilotMarkers[i], markerColor);
				}
			}
		}

		void PilotLine(HddLayout.Rect box, int offsetY, string font, string? text) {
			if (text is { Length: > 0 }) {
				DrawLabel(font, string.Empty, text, -1,
					new HddLayout.Rect(box.X0, box.Y0 + offsetY, box.X1, box.Y0 + offsetY + PilotLabelHeight),
					centered: true);
			}
		}

		// Yellow while the damage screen is inspecting the player, and id 13 once it steps to anyone else
		// — on either page, since the flag belongs to the display.
		if ((state.HddSubject.IndicatorLit
				? hud.HeadsDownColors?.Indicator
				: hud.LogicalColor(HudColorTable.HeadsDownIndicatorUnlitId)) is { } indicator) {
			Fill(layout.Indicator, indicator);
		}

		// Last, after the page has painted — the display's own order, so a page that draws into the
		// header strip cannot cover its own caption. Centred, and on its own black background.
		if (HddLayout.Title(strings, state.Hdd, state.HddDamage) is { Length: > 0 } title) {
			var titleBox = layout[HddLayout.Widget.TitleBox];
			if (background is { } titleFill) {
				Fill(titleBox, titleFill);
			}

			DrawLabel(HddLayout.TitleFont, string.Empty, title, -1, titleBox, centered: true);
		}
	}

	/// <summary>
	/// The command display (<c>HddCommandScreen_Ctor</c>, <c>0044c264</c>): the tactical map on the left and, down the right,
	/// nine rows — a message row and the eight orders you transmit to the selected squadmate.
	///
	/// <para>Orders come from <c>STRINGS0.STR</c> group 0 entries 10-17, and each entry's single
	/// attribute byte is the index of its hotkey character within its own text, which is why DEFEND
	/// POSITION highlights its F and SCAN FOR HOSTILES its C — the manual's own key bindings, stored
	/// beside the strings rather than in the code. The row refresh (<c>HddCommandScreen_RefreshOrders</c>, <c>0044ddec</c>) draws an
	/// available order in <c>CPGREEN</c> with the hotkey in <c>CPRED</c>, an unavailable one wholly in
	/// <c>CPBLUE</c>, and the selected one in <c>CPYLW</c> over the 116x18 plate from the display's own
	/// sprite bank. Availability is one bit for the whole list: selecting a pilot sets all eight bytes
	/// and deselecting clears them, so the list greys out entirely until there is somebody to send
	/// to.</para>
	///
	/// <para>The map itself is drawn separately and last, under a scissor — see
	/// <see cref="HddMapPainter.DrawHddMap"/>. What this leaves behind it is the flood its render target sits on.</para>
	/// </summary>
	private void AddHddCommandDisplay(CockpitArt hud, HddLayout layout, HudSpriteSheet sprites,
			StringFile? strings, CockpitHudState state, Vector3? background,
			Action<string, int, float, float> blit, Action<HddLayout.Rect, Vector3> fill,
			HddLabelWriter drawLabel) {
		if (background is { } mapFill) {
			fill(layout.MapViewport, mapFill);
		}

		var command = state.Command;

		// Row 0 of the nine is the incoming-message row: a centred label on its own colour id 14
		// plate, which the screen writes its "select a pilot" / "select a unit" prompts into.
		var messageRow = layout.OrderRow(0);
		if (hud.LogicalColor(HddLayout.MessageRowColorId) is { } messageFill
			&& command.Message is { Length: > 0 } message) {
			fill(messageRow, messageFill);
			drawLabel(HddLayout.PilotNameFont, string.Empty, message, -1, messageRow, true, 0f);
		}

		var orders = strings?.Group(HddLayout.OrderGroup);
		if (orders == null) {
			return;
		}

		for (int i = 0; i < HddLayout.OrderCount; i++) {
			int entry = HddLayout.FirstCommandOrder + i;
			if (entry >= orders.Count || orders[entry].Text is not { Length: > 0 } text) {
				continue;
			}

			// The row refresh reads the display's dropout byte too: dark, every order takes the
			// unavailable font and none is highlighted.
			var row = layout.OrderRow(i + 1);
			bool dark = state.Dropout.HeadsDownDark;
			bool available = command.OrdersAvailable && !dark;
			bool selected = command.SelectedOrder == (HddOrder)i && !dark;

			// The selected row gets the plate behind its text and a two-pixel bar at the column's own
			// left edge, three device pixels down from the row's top. XMIT's paint redraws the plate a
			// frame on while XMIT shows pressed.
			if (selected) {
				int plate = state.ShowsPressed(CockpitWidgetId.Hdd(HddLayout.Widget.Transmit))
					? HddLayout.OrderHighlightFrame + 1
					: HddLayout.OrderHighlightFrame;
				blit(HddLayout.Bank,
					available ? plate : HddLayout.OrderHighlightUnavailableFrame,
					row.X0, row.Y0);

				if (hud.LogicalColor(HddLayout.SelectedOrderBarColorId) is { } barColor) {
					fill(new HddLayout.Rect(layout.OrderColumn.X0 + 1, row.Y0 + 3,
						layout.OrderColumn.X0 + 1 + HddLayout.SelectedOrderBarWidth, row.Y1), barColor);
				}
			}

			// The hotkey character is only drawn in the alternate font while the order can actually be
			// taken: the refresh passes the alternate through only on that branch.
			int hotkey = available && !selected && orders[entry].Attributes is { Length: > 0 } attributes
				? attributes[0]
				: -1;
			string font = selected ? HddLayout.OrderSelectedFont
				: available ? HddLayout.OrderFont
				: HddLayout.OrderUnavailableFont;

			drawLabel(font, HddLayout.OrderHotkeyFont, text, hotkey, row, false, HddLayout.OrderTextMargin);
		}
	}

	/// <summary>
	/// The damage detail (<c>HddDamageScreen_Ctor</c>, <c>0045079c</c>): the subject's paper doll on the left of the screen and,
	/// down the right, thirteen component rows — a name and a percentage each. The subject is the player,
	/// a squadmate or the target, whichever <see cref="HddDamageSubject"/> the arrows have stepped to.
	///
	/// <para>A row is a <c>.PDG</c> region rather than a table entry: the update walks the view's region
	/// vector in file order and uses each region's id to index both the name group and the readout
	/// buffer. The two orders differ in every retail internal view — a pilotable chassis lists its regions
	/// 0,1,2,5,6,7,8,3,4,9 — so reading the group top to bottom would mislabel the rows. The percentage column's width is
	/// the measured width of the literal "100", which the constructor reserves before placing either
	/// label, so an undamaged component fills its column exactly. Both labels are re-fonted together
	/// from the row's state, giving the manual's green through red plus grey for inoperative — see
	/// <see cref="PaperDollDamage.RowFont"/>.</para>
	///
	/// <para>The rows do not scroll, in the original either: its row offset stays 0 on every path found,
	/// so a view with more regions than <see cref="HddLayout.DamageRowCount"/> labels its first thirteen
	/// and the doll still tints the rest — docs/retail/simulation/heads-down-display.md#damage-detail--page-1.</para>
	///
	/// <para>The paper doll is the subject chassis's own <c>.PDG</c> view for the category — front for structural,
	/// rear for internal — blitted at the screen rect's top-left plus that view's own origin, which is
	/// the paint's own arithmetic rather than a centring rule. Every category draws the weapon icons
	/// over it, placed by the <c>.PDG</c>'s hardpoint list — see <see cref="PaperDollPainter.AddHddWeaponIcons"/>.</para>
	///
	/// <para>The weapons category's row <c>n</c> is <c>.GL</c> slot <c>n</c> — the hardpoint whose icon
	/// <c>.PDG</c> entry <c>n</c> places — so its rows run in slot order, stop at whichever of the
	/// mount array and the <c>.PDG</c> hardpoint list is shorter, and leave an empty hardpoint's row
	/// blank.</para>
	/// </summary>
	private static void AddHddDamageDetail(CockpitArt hud, HddLayout layout, HudSpriteSheet sprites,
			StringFile? strings, HddDamageView view, CockpitHudState state,
			Action<string, int, float, float> blit, Action<HddLayout.Rect, Vector3> fill,
			HddLabelWriter drawLabel, Action<float, float, float, float, Vector3> fillRect) {
		const float S = CockpitArt.GauToPixelScale;

		// Whose herc is being inspected, on its own plate at the screen's bottom-left.
		var subject = state.HddSubject;
		if (subject.Caption is { Length: > 0 } caption) {
			if (hud.LogicalColor(subject.CaptionColorId) is { } plate) {
				fill(layout.DamageFooter, plate);
			}

			drawLabel(subject.CaptionFont, string.Empty, caption, -1, layout.DamageFooter, true, 0f);
		}

		// The target slot with no HERC selected: the caption and one line saying why, and nothing else.
		if (!subject.Subject.Present) {
			if (subject.NoData is { Length: > 0 } noData) {
				if (hud.LogicalColor(HddDamageSubject.TargetPlateColorId) is { } noDataFill) {
					fill(layout.DamageNoSubject, noDataFill);
				}

				drawLabel(HddLayout.NoSubjectFont, string.Empty, noData, -1, layout.DamageNoSubject, true, 0f);
			}

			return;
		}

		// The subject's own chassis: its .PDG places the doll and its bank holds the art.
		var inspected = subject.Subject;
		var readings = inspected.Readings;
		var hardpoints = subject.Hardpoints;
		PaperDollGraphic.ViewRegion[]? regions = null;
		int? iconEntries = null;

		int dollView = HddLayout.PaperDollView(view);
		if (inspected.PaperDollName is { } bank && hud.PaperDollFor(bank) is { Entries: { } views } paperDoll
			&& dollView < views.Length && views[dollView] is { } doll) {
			float dollLeft = layout.Screen.X0 + doll.Origin.X * S;
			float dollTop = layout.Screen.Y0 + doll.Origin.Y * S;
			blit(bank, dollView, dollLeft, dollTop);
			regions = view == HddDamageView.Weapons ? null : doll.Regions;
			iconEntries = paperDoll.Hardpoints?.Length;

			PaperDollPainter.AddHddWeaponIcons(hud, sprites, view, bank, doll, dollView, paperDoll.Hardpoints, hardpoints,
				readings, dollLeft, dollTop, blit, fillRect);

			// One tint per row, in the row's own order — the structural view's first two rows share a
			// rect (both cockpit halves) and the second of them draws nothing, which is why the reading
			// comes from PaperDollDamage.TintReading rather than from the row's printed number. Drawn
			// after the icons, as the original's per-row pass is, so a worn limb's tint runs under an
			// icon wherever the doll's art holds the limb's colour.
			if (regions != null && readings != null) {
				for (int i = 0; i < regions.Length; i++) {
					PaperDollPainter.AddPaperDollTint(hud, sprites, bank, dollView, regions[i],
						PaperDollDamage.TintReading(view, regions, i, inspected.FlyerVariant, readings),
						dollLeft, dollTop, fillRect);
				}
			}
		}

		// One row per .PDG region, in the file's own order, each naming its string by the region's id.
		// The weapons category's rows are the subject's hardpoints by .GL slot, as many as both the
		// mount array and the .PDG's icon list reach.
		var names = HddLayout.ComponentNames(strings, view, inspected.FlyerVariant);
		int rowCount = view == HddDamageView.Weapons
			? Math.Min(hardpoints.Count, iconEntries ?? hardpoints.Count)
			: regions?.Length ?? 0;

		float valueWidth = sprites.Font(HddLayout.DamageRowFont)?.Measure(HddLayout.DamageValueReservation) ?? 0f;

		for (int i = 0; i < HddLayout.DamageRowCount && i < rowCount; i++) {
			string? text;
			int? reading;
			if (view == HddDamageView.Weapons) {
				text = hardpoints[i]?.Name;
				reading = readings != null ? PaperDollDamage.WeaponRowReading(i, readings) : null;
			} else {
				int id = regions![i].Index;
				text = id < names.Count ? names[id].Text : null;
				reading = readings != null ? PaperDollDamage.RowReading(view, id, readings) : null;
			}

			if (text is not { Length: > 0 }) {
				continue;
			}

			// Both labels take the reading's own colour, so a row goes yellow, orange, red and grey
			// together — the re-font HddDamageScreen_Update (00450c54) does off Damage_PickRegionTint's state.
			string font = PaperDollDamage.RowFont(PaperDollDamage.State(reading ?? 0));
			string value = MfdLayout.IntegrityPercent(reading ?? 0).ToString();

			var row = layout.DamageRow(i);
			var nameBox = row with { X1 = row.X1 - (int)valueWidth };
			var valueBox = row with { X0 = nameBox.X1 };

			drawLabel(font, string.Empty, text, -1, nameBox, false, 0f);
			drawLabel(font, string.Empty, value, -1, valueBox, false, 0f);
		}
	}

	/// <summary>
	/// Places one Heads-Down Display label: a font, the alternate its hotkey character is drawn in,
	/// the text, that character's index (-1 for none), the device-pixel rect it sits in, whether it
	/// centres horizontally, and how far its text is indented from the anchoring edge.
	/// </summary>
	private delegate void HddLabelWriter(string font, string alternateFont, string text, int hotkeyIndex,
		HddLayout.Rect rect, bool centered, float marginX);

	/// <summary>
	/// Height of one comm-box label. <c>HddGauge_LoadPilotFrames</c> states only the y each label
	/// starts at — 8, 32, 48, 64 and 80 device pixels down the box — and the four in the body are 16
	/// apart, so each row is given that pitch less a two-pixel gap. The glyphs themselves are placed
	/// by <see cref="HudFont.Place"/>, which centres them in whatever rect it is handed.
	/// </summary>
	private const int PilotLabelHeight = 14;
}
