using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;
using Herculan.Engine.Settings;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Render;

/// <summary>
/// Draws one panel's cockpit-art quad — at its own native aspect ratio, never stretched — plus, for
/// the center panel only, the herc's HUD widgets over it, positioned from its own <c>.GAU</c> and
/// drawn in the game's own sprite art and fonts. See docs/formats/cockpit-hud-widgets.md and
/// docs/engine/planning.md's Milestone 8.
///
/// <para>Orthographic, own minimal shader (position/UV/color, no lighting) — same precedent as
/// <see cref="WireframeRenderer"/> using its own shader rather than forcing 2D content through
/// <see cref="SceneRenderer"/>'s lit-3D layout. Disables depth test and enables alpha blending for
/// its own draw, then restores both — the 3D pass (<see cref="SceneRenderer"/>) assumes depth test is
/// always on, and nothing else in the engine uses blending.</para>
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
/// </summary>
public sealed class Overlay2DRenderer : IDisposable {
	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly GpuOverlayMesh _mesh;
	private readonly List<Overlay2DVertex> _vertices = new();

	public Overlay2DRenderer(GL gl) {
		_gl = gl;
		_shader = ShaderProgram.Load(gl, "Overlay2D.glsl");
		_mesh = new GpuOverlayMesh(gl);
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
		_gl.Viewport(viewportX, viewportY, (uint)Math.Max(viewportWidth, 1), (uint)Math.Max(viewportHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		// Fit by height, preserving the art's native aspect ratio — never stretched. When the panel is
		// narrower than the art (the common case: each of three side-by-side panels is much taller
		// than 4:3), the quad is wider than the viewport and GL's own clipping crops its left/right
		// edges symmetrically, no explicit UV cropping needed. When the panel is wider than the art
		// (an ultrawide window), the quad is narrower than the viewport and sits centered, leaving the
		// live 3D view visible at the flanks instead of stretching the cockpit art to cover them.
		float scale = viewportHeight / (float)cockpitTextureHeight;
		float quadWidth = cockpitTextureWidth * scale;
		float quadX0 = (viewportWidth - quadWidth) / 2f;

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(viewportWidth, viewportHeight));

		// Before the canopy, so the canopy art covers it: the HUD's target box is the one thing on the
		// front window that goes *behind* the cockpit frame. See AddTargetBox for why — it is the only
		// gunsight child that drops back into the view's own render context, whose clip block is the
		// herc's canopy cutout, while every other widget draws through the full-canvas context.
		if (hud?.Sprites is { } boxSprites && spriteTexture != null
			&& !(hudState ?? CockpitHudState.Default).Dropout.GunsightDark) {
			_vertices.Clear();
			AddTargetBoxLayer(hud.Gau, boxSprites, hudState ?? CockpitHudState.Default, scale, quadX0);
			if (_vertices.Count > 0) {
				_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
				_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
			}
		}

		_vertices.Clear();
		AddCockpitQuad(quadX0, quadWidth, viewportHeight, mirrorHorizontally);
		_shader.SetSamplerTexture("uTexture", cockpitTexture.Handle, 0);
		_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));

		// Second bind, second draw: the sprites live in their own atlas. Widget positions are authored
		// in the cockpit texture's own native pixel space (after CockpitArt.GauToPixelScale), so the
		// same uniform scale and horizontal offset the canopy quad uses keeps them aligned to the
		// console art regardless of panel aspect ratio.
		// Flat-coloured gauge fills ride along in the same batch — Overlay2DVertex carries the
		// textured/flat choice per vertex, so they ignore whatever texture happens to be bound.
		if (hud != null && spriteTexture != null) {
			_vertices.Clear();
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

					_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
					_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
					_vertices.Clear();
					DrawMfdNavMapTerrain(navMap, mapTexture, left, top, right, bottom, centre, scale,
						viewportX, viewportY, viewportHeight);
					_vertices.Clear();
				}

				// The MISSILE CAM's world the same way, between the screen's flood and the cross over it.
				// The scene pass leaves its own GL state behind, so the overlay's is put back after it.
				void DrawMissileView(float left, float top, float right, float bottom, Camera view) {
					if (missileView == null) {
						return;
					}

					_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
					_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
					_vertices.Clear();

					int x0 = (int)MathF.Floor(left);
					int y0 = (int)MathF.Floor(viewportHeight - bottom);
					int width = Math.Max((int)MathF.Ceiling(right) - x0, 0);
					int height = Math.Max((int)MathF.Ceiling(viewportHeight - top) - y0, 0);
					if (width == 0 || height == 0) {
						return;
					}

					missileView(viewportX + x0, viewportY + y0, width, height, view);

					_gl.Viewport(viewportX, viewportY, (uint)Math.Max(viewportWidth, 1), (uint)Math.Max(viewportHeight, 1));
					_gl.Disable(EnableCap.DepthTest);
					_gl.Enable(EnableCap.Blend);
					_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
					_shader.Use();
					_shader.SetVector2("uViewportSize", new Vector2(viewportWidth, viewportHeight));
				}

				AddWidgets(hud, sprites, scale, quadX0, hudState ?? CockpitHudState.Default, DrawNavMapTerrain,
					DrawMissileView);
			}

			if (_vertices.Count > 0) {
				_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
				_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
			}
		}

		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// Draws the Heads-Down Display's background — <c>(herc).HB1</c> — filling the given viewport
	/// edge to edge horizontally.
	///
	/// <para>The art itself is placed exactly as <see cref="Draw"/> places a cockpit panel: fit by
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
	public void DrawHeadsDown(int viewportX, int viewportY, int viewportWidth, int viewportHeight,
			GpuTexture texture, int textureWidth, int textureHeight,
			CockpitArt? hud = null, GpuTexture? spriteTexture = null, CockpitHudState? hudState = null,
			GpuTexture? mapTexture = null) {
		_gl.Viewport(viewportX, viewportY, (uint)Math.Max(viewportWidth, 1), (uint)Math.Max(viewportHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		float scale = viewportHeight / (float)textureHeight;
		float quadWidth = textureWidth * scale;
		float quadX0 = (viewportWidth - quadWidth) / 2f;
		float quadX1 = quadX0 + quadWidth;

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(viewportWidth, viewportHeight));

		_vertices.Clear();

		// Texel centres, so nearest sampling lands on the outermost column and not on whatever the
		// clamp boundary rounds to.
		float leftEdgeU = 0.5f / textureWidth;
		float rightEdgeU = 1f - 0.5f / textureWidth;

		if (quadX0 > 0f) {
			AddTexturedQuad(0f, 0f, quadX0, viewportHeight, leftEdgeU, 0f, leftEdgeU, 1f);
		}

		if (quadX1 < viewportWidth) {
			AddTexturedQuad(quadX1, 0f, viewportWidth, viewportHeight, rightEdgeU, 0f, rightEdgeU, 1f);
		}

		AddTexturedQuad(quadX0, 0f, quadX1, viewportHeight, 0f, 0f, 1f, 1f);

		_shader.SetSamplerTexture("uTexture", texture.Handle, 0);
		_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));

		// Second bind, second draw, exactly as Draw layers the console instruments over .HB0: the
		// display's own sprites and glyphs live in the shared HUD atlas, not in the canopy texture.
		if (hud?.HeadsDownLayout is { } layout && hud.Sprites is { } sprites && spriteTexture != null) {
			_vertices.Clear();
			AddHeadsDown(hud, layout, sprites, scale, quadX0, hudState ?? CockpitHudState.Default);

			if (_vertices.Count > 0) {
				_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
				_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
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
			DrawHddMap(hud, mapLayout, mapSprites, spriteTexture, mapTexture, mapState, view,
				scale, quadX0, viewportX, viewportY, viewportHeight);
		}

		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// The Heads-Down Display's live content, built the way <c>HddDisplay_Ctor</c> (<c>00448cc8</c>) and its two page
	/// constructors build it — see <see cref="HddLayout"/> for where every rect and frame index comes
	/// from, and docs/formats/cockpit-views.md for the pan that reaches this view.
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
	/// <see cref="DrawHddMap"/>.</para>
	///
	/// <para>The page's content goes down before the widgets rather than after, which is the reverse
	/// of the display's own paint loop. That loop can afford the other order because a page only
	/// floods its screen on a full repaint; here every frame is a full repaint, and XMIT and CANCEL
	/// sit inside the screen rect, so painting the page last would erase them. The command display's
	/// map is the exception and is drawn after all of this by <see cref="DrawHddMap"/>: it owns a
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
			AddFilledRect(Dx(rect.X0), Dy(rect.Y0), Dx(rect.X1 + 1), Dy(rect.Y1 + 1), color);

		void Blit(string bank, int frame, float left, float top) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var r = sprite.Rect;
			float drawn = sprite.Scale;
			AddTexturedQuad(Dx(left), Dy(top), Dx(left + sprite.Width * drawn), Dy(top + sprite.Height * drawn),
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
			AddTexturedQuad(Dx(x0), Dy(y0), Dx(x1), Dy(y1),
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
				(x0, y0, x1, y1, color) => AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
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
	/// <see cref="DrawHddMap"/>. What this leaves behind it is the flood its render target sits on.</para>
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
			// left edge, three device pixels down from the row's top.
			if (selected) {
				blit(HddLayout.Bank,
					available ? HddLayout.OrderHighlightFrame : HddLayout.OrderHighlightUnavailableFrame,
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
	/// and the doll still tints the rest — docs/formats/heads-down-display.md#damage-detail--page-1.</para>
	///
	/// <para>The paper doll is the subject chassis's own <c>.PDG</c> view for the category — front for structural,
	/// rear for internal — blitted at the screen rect's top-left plus that view's own origin, which is
	/// the paint's own arithmetic rather than a centring rule. Every category draws the weapon icons
	/// over it, placed by the <c>.PDG</c>'s hardpoint list — see <see cref="AddHddWeaponIcons"/>.</para>
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

			AddHddWeaponIcons(hud, sprites, view, bank, doll, dollView, paperDoll.Hardpoints, hardpoints,
				readings, dollLeft, dollTop, blit, fillRect);

			// One tint per row, in the row's own order — the structural view's first two rows share a
			// rect (both cockpit halves) and the second of them draws nothing, which is why the reading
			// comes from PaperDollDamage.TintReading rather than from the row's printed number. Drawn
			// after the icons, as the original's per-row pass is, so a worn limb's tint runs under an
			// icon wherever the doll's art holds the limb's colour.
			if (regions != null && readings != null) {
				for (int i = 0; i < regions.Length; i++) {
					AddPaperDollTint(hud, sprites, bank, dollView, regions[i],
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
	private void DrawHddMap(CockpitArt hud, HddLayout layout, HudSpriteSheet sprites,
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

		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(scissorX, scissorY, (uint)scissorW, (uint)scissorH);

		// Viewport-local device pixels, which is the space HddMapView projects into.
		float Mx(float x) => Dx(region.X0 + x);
		float My(float y) => Dy(region.Y0 + y);
		float Px(int worldX) => Mx(view.ToScreenX(worldX));
		float Py(int worldY) => My(view.ToScreenY(worldY));

		if (mapTexture != null && state.Command.Raster is { } raster) {
			_vertices.Clear();
			AddTexturedQuad(Px(raster.WorldX0), Py(raster.WorldY1), Px(raster.WorldX1), Py(raster.WorldY0),
				0f, 0f, 1f, 1f);
			_shader.SetSamplerTexture("uTexture", mapTexture.Handle, 0);
			_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
		}

		_vertices.Clear();

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
				AddFilledRect(x, My(0), x + 1f, My(region.Height), gridColor);
			}

			for (int worldY = FloorToPitch(view.CentreY - halfY);
					worldY <= view.CentreY + halfY; worldY += HddMap.GridPitch) {
				float y = Py(worldY);
				AddFilledRect(Mx(0), y, Mx(region.Width), y + 1f, gridColor);
			}
		}

		// The mission border: the block-1 bounding box the screen keeps in its own +0x160 rect, drawn
		// through the brush mode Raster_FillRect answers by walking the four edges as lines rather
		// than by filling the interior.
		var bounds = view.Bounds;
		if (!bounds.IsEmpty && hud.LogicalColor(HddMap.BorderColorId) is { } borderColor) {
			AddRectOutline(Px(bounds.MinX), Py(bounds.MaxY), Px(bounds.MaxX), Py(bounds.MinY),
				1f, borderColor);
		}

		var markers = state.Command.Plotted;
		for (int i = 0; i < markers.Count; i++) {
			// The selected pilot's marker blinks on the display's own half-second toggle — see
			// "The selected pilot's marker" in docs/formats/heads-down-display.md.
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
				AddLine(Px(from.WorldX), Py(from.WorldY),
					Px(markers[chosen].WorldX), Py(markers[chosen].WorldY), linkColor);
			} else if (state.Command.ChosenPoint is { } point) {
				AddLine(Px(from.WorldX), Py(from.WorldY), Px(point.X), Py(point.Y), linkColor);
			}
		}

		if (_vertices.Count > 0) {
			_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
			_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
		}

		_gl.Disable(EnableCap.ScissorTest);

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
					AddFilledRect(centerX - half, centerY - half, centerX + half, centerY + half, boxColor);
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
		AddTexturedQuad(x, y, x + icon.Width, y + icon.Height, rect.U0, rect.V0, rect.U1, rect.V1);

		// The order's chosen unit is boxed, two pixels proud of the icon on every side.
		if (selected && hud.LogicalColor(HddMap.ChosenUnitColorId) is { } outline) {
			AddRectOutline(x - 2f, y - 2f, x + icon.Width + 2f, y + icon.Height + 2f, 1f, outline);
		}
	}

	/// <summary>A one-pixel line between two arbitrary points, as a quad along its own normal.</summary>
	private void AddLine(float x0, float y0, float x1, float y1, Vector3 color) {
		float dx = x1 - x0;
		float dy = y1 - y0;
		float length = MathF.Sqrt(dx * dx + dy * dy);
		if (length < 0.5f) {
			return;
		}

		float nx = -dy / length * 0.5f;
		float ny = dx / length * 0.5f;
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 + nx, y0 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 + nx, y1 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 - nx, y1 - ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 + nx, y0 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 - nx, y1 - ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 - nx, y0 - ny), color));
	}

	private delegate void HddLabelWriter(string font, string alternateFont, string text, int hotkeyIndex,
		HddLayout.Rect rect, bool centered, float marginX);

	/// <summary>The cockpit-art quad at its native aspect ratio, positioned by <see cref="Draw"/>'s fit-by-height math.</summary>
	private void AddCockpitQuad(float quadX0, float quadWidth, int viewportHeight, bool mirror) {
		float u0 = mirror ? 1f : 0f;
		float u1 = mirror ? 0f : 1f;
		AddTexturedQuad(quadX0, 0, quadX0 + quadWidth, viewportHeight, u0, 0f, u1, 1f);
	}

	/// <summary>
	/// Draws the Master Energy Pool meter's LED bar: the unfilled remainder across the whole box,
	/// then the filled span as the original's one-pixel vertical pinstripe of two near-identical
	/// shades (see <see cref="HudColorTable.GaugeFillEvenId"/>).
	///
	/// <para>Geometry is the <c>.GAU</c> energy-meter rect at offset 564, which
	/// <c>EnergyPoolGauge_Ctor</c> (<c>00444d5c</c>) copies verbatim into the bar object before
	/// handing it to <c>LedBarGraph_Ctor</c> with range <c>0x400</c>. The bar fills along x in both
	/// class variants: <c>LedBarGraph_CtorBase</c> takes its start/end from the rect's x0/x1
	/// (<c>param_2[0]</c>/<c>param_2[2]</c>) and the pinstripe walk strides columns.</para>
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

		AddFilledRect(Px(left), Py(top), Px(right), Py(bottom), remainder);

		// Columns are stepped in the .GAU's own coordinate space, which is what the original strides
		// over — the x2 scale to cockpit pixels happens inside Px, so the stripe stays one source
		// pixel wide regardless of panel size.
		int filledTo = left + (int)MathF.Round(meter.Size.Width * Math.Clamp(fillFraction, 0f, 1f));
		for (int x = left; x < filledTo; x++) {
			AddFilledRect(Px(x), Py(top), Px(x + 1), Py(bottom), (x & 1) == 0 ? fillEven : fillOdd);
		}
	}

	private void AddFilledRect(float x0, float y0, float x1, float y1, Vector3 color) {
		var a = new Overlay2DVertex(new Vector2(x0, y0), color);
		var b = new Overlay2DVertex(new Vector2(x1, y0), color);
		var c = new Overlay2DVertex(new Vector2(x1, y1), color);
		var d = new Overlay2DVertex(new Vector2(x0, y1), color);
		_vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
		_vertices.Add(a); _vertices.Add(c); _vertices.Add(d);
	}
	/// <summary>
	/// Places each widget's sprite and text. A sprite is drawn at its own native pixel size anchored
	/// to the widget's top-left, not stretched to the widget rect: the two disagree by a few pixels in
	/// real data (a weapon plate is 116x18 against a 110x12 rect) because the <c>.GAU</c> rect is the
	/// widget's hit/layout box, not its art's extent, and stretching to match visibly softens art
	/// authored for exact pixels.
	/// </summary>
	private void AddWidgets(CockpitArt hud, HudSpriteSheet sprites, float scale, float quadX0,
			CockpitHudState state, NavMapTerrainWriter? drawNavMapTerrain = null,
			MissileViewWriter? drawMissileView = null) {
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
			AddTexturedQuad(left, top, left + sprite.Width * drawn, top + sprite.Height * drawn,
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
			AddTexturedQuad(Dx(x0), Dy(deviceTop), Dx(x1), Dy(deviceTop + sprite.Height * sprite.Scale),
				r.U0 + (r.U1 - r.U0) * ((x0 - deviceLeft) / width), r.V0,
				r.U1 - (r.U1 - r.U0) * ((deviceLeft + width - x1) / width), r.V1);
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

			AddTexturedQuad(pivot, pivot + right, pivot + right + down, pivot + down,
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
				AddFilledTriangle(At(bottom), At(left), At(top), color);
				AddFilledTriangle(At(bottom), At(top), At(right), color);
			} else {
				var (tip, baseA, baseB) =
					geometry.Arrow(WaypointIndicator.PointsRight(subject.BearingError));
				AddFilledTriangle(At(tip), At(baseA), At(baseB), color);
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
				AddFilledRect(Px(gauX0), Py(gauY0), Px(gauX1), Py(gauY1), fill);
			}

			// The .GAU rect's Origin + Size is the file's own inclusive second corner, so scaling both
			// corners gives the device-pixel rect Label_SetRect takes. See HudFont.Place.
			var (textX, textY) = font.Place(text,
				(int)(gauX0 * S), (int)(gauY0 * S), (int)(gauX1 * S), (int)(gauY1 * S),
				LabelAlign.Center);
			DrawText(fontName, text, textX, textY);
		}

		AddMfd(hud, state, BlitDevice, BlitRotatedDevice, DrawRun,
			(x0, y0, x1, y1, color) => AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color),
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

		AddWeaponRows(gau, state, hud.WeaponBarColors, hud.LogicalColor(PodPlateColorId),
			BlitDevice, DrawText,
			(x0, y0, x1, y1, color) => AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		AddShieldReadouts(gau, state, hud.GaugeColors?.Remainder, DrawTextCentered);
		AddConsoleButtons(gau, hud.Strings, state, BlitDevice, DrawTextCentered);

		// The reticle is a point, not a rect — the only widget in the file that is — so its sprite
		// centers on it rather than hanging off a top-left corner. Which of the bank's three frames it
		// wears is the gunsight's on-target state: child 4's paint (Gunsight_ReticlePaint, 0043b7e0) draws frame 0 with
		// nothing selected or the selection off the sight, frame 2 once the target projects within
		// TargetBox.OnTargetTolerance of this very point, and frame 1 when it also has missile lock.
		if (gunsight && gau.Reticle is { } reticle) {
			int frame = ReticleFrame(gau, state);
			if (sprites.Sprite(TargetBox.SpriteBank, frame) is { } crosshair) {
				Blit(TargetBox.SpriteBank, frame,
					Px(reticle.Origin.X) - crosshair.Width * scale / 2f,
					Py(reticle.Origin.Y) - crosshair.Height * scale / 2f);
			}
		}

		// Over the reticle, because the gunsight complex paints its children in construction order and
		// the target box is child 5 to the reticle's child 4.
		if (gunsight) {
			AddTargetIndicator(gau, sprites, hud.TargetArrowColors, state,
				(bank, frame, left, top, flipX, flipY) => BlitFlipped(bank, frame, Dx(left), Dy(top), flipX, flipY),
				(a, b, c, color) => AddFilledTriangle(
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
			AddAutoTrackLegend(gau, hud.Strings, state, BlitDevice, DrawTextCentered);
			AddGunsightReadouts(gau, sprites, state, DrawText);
		}

		// And last of all, after every child, the floating scanner repeater — the gunsight's paint
		// calls it once the child loop is done.
		if (gunsight) {
			AddHudScanner(hud, state, BlitDevice,
				(x0, y0, x1, y1, color) => AddFilledRect(Dx(x0), Dy(y0), Dx(x1), Dy(y1), color));
		}

		// The message port is the view's own last child, constructed after every gauge, so its box
		// goes over whatever it overlaps rather than under it.
		AddMessageTicker(hud, sprites, state.Message, Dx, Dy, scale);

		// And the second port of the same class, built from the rect immediately before the ticker's.
		AddPilotMessage(hud, sprites, state.PilotMessage, Dx, Dy, scale);
		AddTrainingMessage(hud, sprites, state.TrainingMessage, Dx, Dy, scale);
	}

	/// <summary>
	/// The training port's block — the instructor's sentences wrapped into white lines, left-aligned
	/// in a black box with a red frame, the box as wide as the widest line and centred on the screen.
	/// <see cref="TrainingMessageLayout"/> carries the rules.
	/// </summary>
	private void AddTrainingMessage(CockpitArt hud, HudSpriteSheet sprites, TrainingMessageBox? message,
			Func<float, float> dx, Func<float, float> dy, float scale) {
		if (message is not { Lines.Count: > 0 } block
			|| TrainingMessageLayout.From(hud) is not { } box
			|| sprites.Font(TrainingMessageLayout.Font) is not { } font) {
			return;
		}

		const int screen = CockpitViewGeometry.ViewWidth;
		int textWidth = block.Widest >= 0 ? font.Measure(block.Lines[block.Widest]) : 0;
		int left = TrainingMessageLayout.Left(screen, textWidth);
		int right = TrainingMessageLayout.Right(screen, textWidth);
		int bottom = box.Top + TrainingMessageLayout.Height(block.Lines.Count);

		if (hud.LogicalColor(TrainingMessageLayout.FillColorId) is { } fill) {
			AddFilledRect(dx(left), dy(box.Top), dx(right), dy(bottom), fill);
		}

		if (hud.LogicalColor(TrainingMessageLayout.BorderColorId) is { } frame) {
			AddRectOutline(dx(left), dy(box.Top), dx(right), dy(bottom), scale, frame);
		}

		for (int i = 0; i < block.Lines.Count; i++) {
			float pen = TrainingMessageLayout.TextLeft(screen, textWidth);
			float top = box.LineTop(i, font);

			foreach (char c in block.Lines[i]) {
				if (font.GlyphIndex(c) is { } glyph
					&& sprites.Sprite(TrainingMessageLayout.Font, glyph) is { Width: > 0, Height: > 0 } cell) {
					var r = cell.Rect;
					AddTexturedQuad(dx(pen), dy(top), dx(pen + cell.Width), dy(top + cell.Height),
						r.U0, r.V0, r.U1, r.V1);
				}

				pen += font.Width(c);
			}
		}
	}

	/// <summary>
	/// The pilot and squad channel's line — a box in the speaking squadmate's own colour, framed one
	/// palette entry below it, with the composed <c>NAME: message</c> centred in it.
	/// <see cref="PilotMessageBoxLayout"/> carries the geometry and the colour rule and says where
	/// both come from.
	///
	/// <para>No horizontal clip, unlike the ticker: this box is built <i>around</i> its text rather
	/// than the text scrolled through it, so nothing can overhang an edge.</para>
	/// </summary>
	private void AddPilotMessage(CockpitArt hud, HudSpriteSheet sprites, PilotMessageLine? line,
			Func<float, float> dx, Func<float, float> dy, float scale) {
		if (line is not { Text.Length: > 0 } message
			|| PilotMessageBoxLayout.From(hud) is not { } box
			|| sprites.Font(PilotMessageBoxLayout.Font) is not { } font) {
			return;
		}

		const int screen = CockpitViewGeometry.ViewWidth;
		int textWidth = font.Measure(message.Text);
		int left = PilotMessageBoxLayout.Left(screen, textWidth);
		int right = PilotMessageBoxLayout.Right(screen, textWidth);

		// A squadmate's slot resolves to their own comm-box colour and the frame is the palette entry
		// one below it — index arithmetic on the resolved index, not a second logical id. Anything
		// without a squadmate behind it takes the computer's black and red instead.
		Vector3? fill, border;
		if (message.Slot >= 0
			&& hud.Colors?.PaletteIndex(HudColorTable.PilotColorId(message.Slot)) is { } index) {
			fill = hud.PaletteEntry(index);
			border = hud.PaletteEntry(index - 1);
		} else {
			fill = hud.LogicalColor(PilotMessageBoxLayout.NoSpeakerFillColorId);
			border = hud.LogicalColor(PilotMessageBoxLayout.NoSpeakerBorderColorId);
		}

		if (fill is { } background) {
			AddFilledRect(dx(left), dy(box.Top), dx(right), dy(box.Bottom), background);
		}

		if (border is { } frame) {
			AddRectOutline(dx(left), dy(box.Top), dx(right), dy(box.Bottom), scale, frame);
		}

		float pen = PilotMessageBoxLayout.TextLeft(screen, textWidth);
		float top = box.TextTop(font);

		foreach (char c in message.Text) {
			if (font.GlyphIndex(c) is { } glyph
				&& sprites.Sprite(PilotMessageBoxLayout.Font, glyph) is { Width: > 0, Height: > 0 } cell) {
				var r = cell.Rect;
				AddTexturedQuad(dx(pen), dy(top), dx(pen + cell.Width), dy(top + cell.Height),
					r.U0, r.V0, r.U1, r.V1);
			}

			pen += font.Width(c);
		}
	}

	/// <summary>
	/// The cockpit computer's message ticker — a black box with a red frame, and one line of red text
	/// scrolling right to left inside it. <see cref="Content.MessagePort"/> decides what is in it and
	/// for how long; <see cref="MessageTickerLayout"/> says where it is and where the line sits.
	///
	/// <para>The text is clipped horizontally, per glyph, against the box's inset edges — the
	/// original narrows its live clip rect between drawing the frame and drawing the line
	/// (<c>MessageTicker_Paint</c>, <c>00436cec</c>), which is what makes the marquee slide under the frame instead of past it.
	/// Clipping the geometry rather than setting a GL scissor keeps the whole panel one batch, and a
	/// horizontal trim is all that is needed: the glyph row is centred in a box taller than it.</para>
	/// </summary>
	private void AddMessageTicker(CockpitArt hud, HudSpriteSheet sprites, in MessageTicker ticker,
			Func<float, float> dx, Func<float, float> dy, float scale) {
		if (!ticker.HasText
			|| MessageTickerLayout.From(hud) is not { } box
			|| sprites.Font(MessageTickerLayout.Font) is not { } font) {
			return;
		}

		if (hud.LogicalColor(MessageTickerLayout.BackgroundColorId) is { } background) {
			AddFilledRect(dx(box.Left), dy(box.Top), dx(box.Right), dy(box.Bottom), background);
		}

		if (hud.LogicalColor(MessageTickerLayout.BorderColorId) is { } border) {
			AddRectOutline(dx(box.Left), dy(box.Top), dx(box.Right), dy(box.Bottom), scale, border);
		}

		if (!ticker.Visible) {
			return;
		}

		string text = ticker.Text!;
		float pen = box.TextLeft(ticker, font.Measure(text));
		float top = box.TextTop(font);

		foreach (char c in text) {
			if (font.GlyphIndex(c) is not { } glyph) {
				continue;
			}

			float width = font.Width(c);
			if (pen + width > box.ClipLeft && pen < box.ClipRight
				&& sprites.Sprite(MessageTickerLayout.Font, glyph) is { Width: > 0, Height: > 0 } cell) {
				float left = Math.Max(pen, box.ClipLeft);
				float right = Math.Min(pen + cell.Width, box.ClipRight);
				var r = cell.Rect;

				// Trim the UVs by the same fraction the quad was trimmed by, so a half-clipped glyph
				// shows half of itself rather than a squeezed whole one.
				float u0 = r.U0 + (r.U1 - r.U0) * ((left - pen) / cell.Width);
				float u1 = r.U1 - (r.U1 - r.U0) * ((pen + cell.Width - right) / cell.Width);
				AddTexturedQuad(dx(left), dy(top), dx(right), dy(top + cell.Height), u0, r.V0, u1, r.V1);
			}

			pen += width;
		}
	}

	/// <summary>
	/// A one-device-pixel frame round a rect, drawn as four filled edges — the fill brush's style 4,
	/// which <c>Raster_FillRect</c> (<c>004865f8</c>) implements as four line draws round the rect it is handed.
	/// </summary>
	private void AddRectOutline(float x0, float y0, float x1, float y1, float scale, Vector3 color) {
		float thickness = Math.Max(scale, 1f);
		AddFilledRect(x0, y0, x1, y0 + thickness, color);
		AddFilledRect(x0, y1 - thickness, x1, y1, color);
		AddFilledRect(x0, y0, x0 + thickness, y1, color);
		AddFilledRect(x1 - thickness, y0, x1, y1, color);
	}

	/// <summary>
	/// The front window's floating scanner repeater (<c>HudScanner_Paint</c>, <c>0043f2b0</c>) — see
	/// <see cref="Content.HudScanner"/> for what it is and why it is here rather than with the MFD.
	/// It draws only while the MFD is showing something other than its own screen.
	/// </summary>
	private static void AddHudScanner(CockpitArt hud, CockpitHudState state,
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
			AddCircleOutline(centerX, centerY, half, outline, fillRect);

			// The turret arc: two lines from the centre out to the rim, 45 degrees either side of the
			// twist. The endpoint is the point (0, -half) rotated, so both reach exactly the rim.
			for (int side = -1; side <= 1; side += 2) {
				short angle = unchecked((short)(state.TorsoTwist + side * HudScanner.ArcHalfAngle));
				float cos = SimTrig.Cos(angle) / 16384f;
				float sin = SimTrig.Sin(angle) / 16384f;
				AddLine(centerX, centerY, centerX + half * sin, centerY - half * cos, outline, fillRect);
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
				AddFilledCircle(blipX, blipY, HudScanner.BlipOutlineRadius, ring, fillRect);
			}

			AddFilledCircle(blipX, blipY, HudScanner.BlipCoreRadius, color, fillRect);
		}

		if (scanner.TargetContact >= 0 && scanner.TargetContact < contacts.Count) {
			var target = contacts[scanner.TargetContact];
			blitDevice(MfdScanner.Bank, MfdScanner.TargetBracketFrame,
				centerX + target.X / worldPerPixel - HudScanner.TargetBracketOffset * S,
				centerY + target.Y / worldPerPixel - HudScanner.TargetBracketOffset * S);
		}
	}

	/// <summary>
	/// A one-device-pixel line, stamped a pixel at a time by Bresenham — the same approach
	/// <see cref="AddCircleOutline"/> takes, and for the same reason: the original rasterizes it
	/// (<c>Raster_DrawLine</c>) rather than drawing a quad, and a quad thin enough to match would
	/// alias differently.
	/// </summary>
	private static void AddLine(float x0, float y0, float x1, float y1, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		int px = (int)MathF.Round(x0), py = (int)MathF.Round(y0);
		int qx = (int)MathF.Round(x1), qy = (int)MathF.Round(y1);
		int dx = Math.Abs(qx - px), dy = -Math.Abs(qy - py);
		int stepX = px < qx ? 1 : -1, stepY = py < qy ? 1 : -1;
		int error = dx + dy;

		while (true) {
			fillRect(px, py, px + 1, py + 1, color);
			if (px == qx && py == qy) {
				return;
			}

			int doubled = error * 2;
			if (doubled >= dy) {
				error += dy;
				px += stepX;
			}

			if (doubled <= dx) {
				error += dx;
				py += stepY;
			}
		}
	}

	/// <summary>
	/// A filled disc, one row of spans at a time — the original's general ellipse rasterizer
	/// (<c>Raster_DrawEllipse</c>, <c>00488070</c>) with the brush in fill mode, which is how the repeater draws a blip: a
	/// radius-2 disc in black with a radius-1 one in the contact's colour inside it.
	/// </summary>
	private static void AddFilledCircle(float centerX, float centerY, int radius, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		for (int dy = -radius; dy <= radius; dy++) {
			int dx = (int)MathF.Round(MathF.Sqrt(radius * radius - dy * dy));
			fillRect(centerX - dx, centerY + dy, centerX + dx + 1, centerY + dy + 1, color);
		}
	}

	/// <summary>
	/// Which of the <c>HUD</c> bank's three reticle frames the crosshair wears, from child 4's paint
	/// (<c>Gunsight_ReticlePaint</c>, <c>0043b7e0</c>): frame 0 unless the selected target projects within
	/// <see cref="TargetBox.OnTargetTolerance"/> of the reticle point on both axes, then frame 2, or
	/// frame 1 when the armed missile mount also has lock. This is the cockpit's "on target"
	/// indication — the box is suppressed over exactly the same span, so the two never overlap.
	/// </summary>
	private static int ReticleFrame(GAUFile gau, CockpitHudState state) {
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
	/// in clip <b>mode 2</b>, the region-list mode. The transparent-sprite blitter
	/// (<c>Bitmap_BlitTransparent</c>, <c>00488cec</c>) tests for exactly that mode and sends every pixel run it emits through
	/// the clipped span writer instead of the plain one, so a sprite drawn in that context is cut to
	/// the canopy opening scanline by scanline — following the A-pillars, not a rectangle.</para>
	///
	/// <para><b>Child 5 is the only widget that opts into it</b>: its paint calls
	/// <c>Cockpit_PopRenderContext</c> before the box and <c>Cockpit_PushCanvasContext</c> after, dropping out of the canvas
	/// context for those blits alone. The reticle, the heading tape, the rotation indicator, the
	/// readouts and the off-screen arrow all stay in the canvas context and are never cut. Reproduced
	/// here by draw order: this batch goes down before the canopy quad, whose art is opaque everywhere
	/// but the cutout (see <see cref="CockpitClipRegions"/>, which is the same region data), so the
	/// frame covers it exactly where the original's clip block would have.</para>
	/// </summary>
	private void AddTargetBoxLayer(GAUFile gau, HudSpriteSheet sprites, CockpitHudState state,
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
			AddTexturedQuad(x, y, x + sprite.Width * drawn, y + sprite.Height * drawn,
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
	private static void AddTargetIndicator(GAUFile gau, HudSpriteSheet sprites,
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

	/// <summary>One flat-coloured triangle — the only thing on the HUD that is neither a sprite nor a rect.</summary>
	private void AddFilledTriangle(Vector2 a, Vector2 b, Vector2 c, Vector3 color) {
		_vertices.Add(new Overlay2DVertex(a, color));
		_vertices.Add(new Overlay2DVertex(b, color));
		_vertices.Add(new Overlay2DVertex(c, color));
	}

	/// <summary>
	/// One row per fitted hardpoint, built the way <c>WeaponGauge_Ctor</c> (<c>0044080c</c>) and its
	/// select-gadget child (<c>ChainedWeaponSelectGadget_Ctor</c> (<c>00442488</c>), painted by <c>WeaponSelectGadget_Paint</c> (<c>004426c0</c>)) build it:
	///
	/// <list type="bullet">
	/// <item>the row plate from <c>PWEAPONS</c> — frame 0 selected, frame 1 not — blitted
	/// <see cref="RowPlateBezel"/> device pixels up and left of the <c>.GAU</c> rect;</item>
	/// <item>the hardpoint's state box from <c>PWEAPONS</c> frames 4 and 5 (6x14) at the rect's
	/// <c>+12</c> device offset — the constructor's own <c>+6</c> GAU literal. It is drawn only for a
	/// mount that is armed or in the current fire group, lit (frame 4) when the mount could fire and
	/// dark (frame 5) when it could not, which is why a pod's row never has one;</item>
	/// <item>the slot number at <c>+6</c> device, then the weapon's name in the label rect the
	/// constructor puts at <c>+11..+35</c> GAU. Colour is the font: <c>WHITE</c> for the selected
	/// row, <c>GRAY</c> for the rest.</item>
	/// <item>and, on a pod row whose button is on, that label rect flooded green first — see
	/// <see cref="PodPlateColorId"/>.</item>
	/// <item>the value field past the name, at <c>+0x24..+0x35</c> GAU — a round count for an
	/// ammunition mount (<c>AmmoWeaponGauge_Paint</c> (<c>004411b4</c>) prints <c>itoa(rounds)</c> there) and an LED charge bar
	/// for an energy one (<c>WeaponSliderGadget_Paint</c> (<c>00442b38</c>) paints one across the same span). A pod has neither: its
	/// own constructor widens the name label across both fields instead.</item>
	/// </list>
	///
	/// <para>Under all of it, <c>WPN_DMG</c> frame 0 fills the plate's hole — <c>WeaponSelectGadget_PaintUnderlay</c> (<c>00442394</c>)'s
	/// underlay, a flat plate in the row's own background colour. The bank's other nine frames are the
	/// row's sensor-dropout wipe: while <see cref="WeaponRowState.DropoutHidden"/> every paint but the
	/// plate's bezel holds back, and the wipe's frame stands in the hole instead.</para>
	/// </summary>
	private static void AddWeaponRows(GAUFile gau, CockpitHudState state,
			(Vector3 FillEven, Vector3 FillOdd)? barColors, Vector3? podPlate,
			Action<string, int, float, float> blit, Func<string, string, float, float, float> drawText,
			Action<float, float, float, float, Vector3> fillRect) {
		if (gau.Weapons is not { } weapons) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		int slots = Math.Min(gau.WeaponListTotal, weapons.Length);
		for (int i = 0; i < slots; i++) {
			var rect = weapons[i];
			var row = i < state.Weapons.Count ? state.Weapons[i] : WeaponRowState.Empty;
			if (!row.Powered) {
				continue;
			}

			// The row's own font, which the slot number always wears: WHITE for the selected row and
			// GRAY for the rest. A pod row's name is the exception — see nameFont.
			string font = row.Selected ? "WHITE" : "GRAY";

			// A pod row's own paint (PodGauge_Paint, 0044171c) re-dresses the name label, and only the name label:
			// it reaches the widget at gauge+0xca and never touches the slot number, which
			// WeaponSelectGadget_Paint draws in the row font above. The label's font comes from the
			// button rather than from the selection, which a pod never has — gray while the button is
			// off, dark while it is on, over the green plate below.
			string nameFont = row.PodButton ? "DARK" : font;
			float left = rect.Origin.X * S;
			float top = rect.Origin.Y * S;

			// WeaponSelectGadget_Paint draws the plate whatever the dropout is doing, and nothing else of
			// the row while it holds the row back; the wipe's last frame fills the hole meanwhile.
			if (row.DropoutHidden) {
				if (row.DropoutFrame is { } wipe) {
					blit(SensorDropout.RowBank, wipe, left, top);
				}

				blit("PWEAPONS", row.Selected ? 0 : 1, left - RowPlateBezel, top - RowPlateBezel);
				continue;
			}

			blit(SensorDropout.RowBank, RowUnderlayFrame, left, top);
			blit("PWEAPONS", row.Selected ? 0 : 1, left - RowPlateBezel, top - RowPlateBezel);
			if (row.Selected || row.InGroup) {
				blit("PWEAPONS", row.Ready ? ReadyStateFrame : UnreadyStateFrame, left + 12, top);
			}

			// The button's other half: the same paint swaps the name label's background from the plate
			// colour it was seeded with to COLORS.DAT id 12, so the whole label rect floods green under
			// the dark ink. The rect is the pod gauge's own, which is why the Turbo Pod's plate is the
			// short one — see PodLabelLeft.
			if (row.PodButton && podPlate is { } plate) {
				bool turbo = row.ChargeBar;
				float x0 = left + (turbo ? TurboPodLabelLeft : PodLabelLeft) * S;
				float x1 = left + (turbo ? TurboPodLabelRight : PodLabelRight) * S;
				float y0 = top + (turbo ? TurboPodLabelTop : PodLabelTop) * S;
				fillRect(x0, y0, x1 + 1, top + PodLabelBottom * S + 1, plate);
			}

			drawText(font, (i + 1).ToString(), left + 6, top);
			if (row.Name is { Length: > 0 } name) {
				drawText(nameFont, name, left + 22, top);
			}

			switch (row.Kind) {
				case WeaponMountKind.Ammunition:
					drawText(font, row.Rounds.ToString(), left + ValueFieldLeft * S, top);
					break;

				// The ELF class keeps the energy class's gauge slot (+0x50), so it prints the same bar,
				// and the Turbo Pod's gauge adds one of its own over the same span — see
				// WeaponRowState.ChargeBar, which is what says whether this row has one.
				case not WeaponMountKind.Ammunition
					when row.ChargeBar && barColors is var (fillEven, fillOdd):
					AddChargeBar(row.ChargeMeter,
						left + ValueFieldLeft * S, top + ChargeBarTop * S,
						(ValueFieldRight - ValueFieldLeft) * S,
						(ChargeBarBottom - ChargeBarTop) * S, fillEven, fillOdd, fillRect);
					break;
			}
		}
	}

	/// <summary>
	/// How far up and left of the <c>.GAU</c> rect the row plate is blitted, in device pixels.
	///
	/// <para>The plate is not a filled plate: <c>PWEAPONS</c> frames 0 and 1 are 116x18 with a 112x14
	/// hole of palette index 0 punched out of the middle, so all the art carries is a two-pixel bezel
	/// and the row's interior is the console bitmap showing through. Offsetting by the bezel width
	/// lands that hole with its top-left corner exactly on the rect, which is where the hardpoint
	/// state box (14 device pixels tall, drawn at the rect's own <c>y</c>) and an engaged pod's green
	/// plate both have to sit for the row to close around them.</para>
	/// </summary>
	private const int RowPlateBezel = 2;

	/// <summary>The <see cref="SensorDropout.RowBank"/> frame every row is underlaid with — 112x14, the plate's hole exactly.</summary>
	private const int RowUnderlayFrame = 0;

	/// <summary><c>PWEAPONS</c> frame for a mount that could fire this instant.</summary>
	private const int ReadyStateFrame = 4;

	/// <summary>And for one that could not — out of ammunition, still charging, or inside its refire delay.</summary>
	private const int UnreadyStateFrame = 5;

	/// <summary>
	/// Where a weapon row's value field starts and ends, in <c>.GAU</c> units from the row's own
	/// left edge — the ammunition gauge's <c>+0x24..+0x35</c> label rect (<c>AmmoWeaponGauge_Ctor</c>, <c>00440f78</c>), which
	/// is also the span the energy gauge hands its LED bar (<c>WeaponSliderGadget_Ctor</c>, <c>00442950</c>).
	/// </summary>
	internal const int ValueFieldLeft = 0x24;

	internal const int ValueFieldRight = 0x35;

	/// <summary>
	/// A pod row's name label, in <c>.GAU</c> units from the row's own left edge and top. It is the
	/// weapon-name label widened over the value field as well, because a pod has nothing to print
	/// there — <c>PodGauge_Ctor</c> (<c>00441524</c>) builds it at <c>x0+11 .. x0+53</c>,
	/// <c>y0 .. y0+5</c>. Both edges are inclusive: the flood covers <c>x1</c> and <c>y1</c> too.
	/// </summary>
	private const int PodLabelLeft = 0xb;

	private const int PodLabelRight = 0x35;

	private const int PodLabelTop = 0;

	private const int PodLabelBottom = 5;

	/// <summary>
	/// And the Turbo Pod's, which is the one pod label that has to share the row with a value field.
	/// <c>TurboPodGauge_Ctor</c> (<c>00441a34</c>) moves the left edge out to <c>x0+6</c>, pulls the
	/// right edge in to two device pixels short of the charge bar, and drops the top edge a unit — so
	/// its plate is shorter and narrower than every other pod's, and sits clear of the bar.
	///
	/// <para>The label's <i>text</i> does not move with it: every row on the panel, this one included,
	/// prints its name at the same <c>+22</c> device pixels.</para>
	/// </summary>
	private const int TurboPodLabelLeft = 6;

	private const int TurboPodLabelRight = 0x22;

	private const int TurboPodLabelTop = 1;

	/// <summary>
	/// What a pod row's name label is flooded with while the pod's button is on — <c>COLORS.DAT</c>
	/// id 12, the green the paper doll and the scanner's hostile structures also wear. Off, the label
	/// keeps the raw palette index <c>0x2e</c> its constructor seeded, which is the row plate's own
	/// background and therefore invisible.
	/// </summary>
	private const int PodPlateColorId = 12;

	/// <summary>
	/// The charge bar's top and bottom edges, in <c>.GAU</c> units below the row's own top. The
	/// energy gauge builds the bar's rect as the value field at <c>y0..y0+5</c>
	/// (<c>EnergyWeaponGauge_Ctor</c>, <c>00440a68</c>), and <c>WeaponSliderGadget_Ctor</c> (<c>00442950</c>) then drops the top edge by one more unit — so
	/// the bar is a touch shorter than the row and sits clear of the plate's upper bezel.
	/// </summary>
	private const int ChargeBarTop = 1;

	private const int ChargeBarBottom = 5;

	/// <summary>
	/// An energy mount's capacitor bar — the same one-pixel pinstripe of two near-identical shades
	/// <see cref="AddGaugeFills"/> paints for the Master Energy Pool, since both are the same
	/// <c>LEDBarGraph</c> class.
	///
	/// <para>Unlike the pool's meter, the unfilled remainder is left alone: the weapon row's bar sits
	/// on the plate art rather than on its own box, and <c>LedBarGraph_PaintToValue</c>'s remainder
	/// colour for this instance is the row background it was built with.</para>
	/// </summary>
	/// <param name="meterValue">The bar's value over its own 0-1024 range.</param>
	private static void AddChargeBar(int meterValue, float left, float top, float width, float height,
			Vector3 fillEven, Vector3 fillOdd, Action<float, float, float, float, Vector3> fillRect) {
		const int Range = 0x400;
		int columns = (int)MathF.Round(width);
		int filled = (int)(Math.Clamp(meterValue, 0, Range) * (long)columns / Range);

		for (int x = 0; x < filled; x++) {
			fillRect(left + x, top, left + x + 1, top + height, (x & 1) == 0 ? fillEven : fillOdd);
		}
	}

	/// <summary>
	/// The shield meter's two numeric readouts, centred in the <c>.GAU</c> label rects at 664 and 680.
	/// <c>ShieldsGauge_Ctor</c> (<c>004434fc</c>) builds them with the <c>WHITE</c> font, and
	/// <c>ShieldsGauge_UpdateReadouts</c> (<c>00444a68</c>) fills them with <c>balance * 200 &gt;&gt; 10</c> and its complement — so an
	/// even fore/aft split reads 100 and 100 out of a 200-point pool.
	///
	/// <para>The meter bodies themselves are not drawn here. They are painted into the canopy art in
	/// palette indices 66-71 and lit by <see cref="CockpitPalette.InstallShieldRamp"/>.</para>
	/// </summary>
	private static void AddShieldReadouts(GAUFile gau, CockpitHudState state, Vector3? background,
			Action<string, string, int, int, int, int, Vector3?> drawCentered) {
		if (gau.ShieldDisplay is not { } shields) {
			return;
		}

		void Label(string text, PixelPoint origin, PixelSize size) =>
			drawCentered("WHITE", text, origin.X, origin.Y, origin.X + size.Width, origin.Y + size.Height,
				background);

		Label(state.ShieldFront.ToString(), shields.FrontLabel, shields.FrontLabelSize);
		Label(state.ShieldRear.ToString(), shields.RearLabel, shields.RearLabelSize);
	}

	/// <summary>
	/// The multi-function display, built the way <c>MfdDisplay_Ctor</c> (<c>00445218</c>) builds it —
	/// see <see cref="MfdLayout"/> for where every rect comes from and
	/// docs/formats/cockpit-hud-widgets.md for the panel it sits in.
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
	/// method off one <see cref="MfdStatusSubject"/> — the machine being flown for F1, the current
	/// selection for F5 — SCANNER plots live contacts, and FLASH COMM lists the string table's order
	/// rows. NAV MAP draws the terrain raster turned to the machine's heading, through
	/// <paramref name="drawNavMapTerrain"/>, and its centre cross; MISSILE CAM draws whatever its
	/// screen last painted, the world from the round going through <paramref name="drawMissileView"/>.</para>
	/// </summary>
	private static void AddMfd(CockpitArt hud, CockpitHudState state,
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
	private delegate void NavMapTerrainWriter(float x0, float y0, float x1, float y1,
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
	/// the time it reaches <see cref="Draw"/> — and the camera to draw the world from.
	/// </summary>
	private delegate void MissileViewWriter(float x0, float y0, float x1, float y1, Camera view);

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
					AddCircleOutline(centreX, centreY, MfdMissileCam.RingRadius(hud.Gau), mark, fillRect);
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
	/// the screen's own offscreen render target, as <see cref="DrawHddMap"/> uses it.
	/// </summary>
	private void DrawMfdNavMapTerrain(MfdNavMapState navMap, GpuTexture mapTexture,
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

		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(scissorX, scissorY, (uint)scissorW, (uint)scissorH);

		AddTexturedQuad(Corner(raster.WorldX0, raster.WorldY1), Corner(raster.WorldX1, raster.WorldY1),
			Corner(raster.WorldX1, raster.WorldY0), Corner(raster.WorldX0, raster.WorldY0), 0f, 0f, 1f, 1f);
		_shader.SetSamplerTexture("uTexture", mapTexture.Handle, 0);
		_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));

		_gl.Disable(EnableCap.ScissorTest);
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
	/// then tinted region by region (see <see cref="AddPaperDollTint"/>), or a flat silhouette from the
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
		if (!subject.Identified) {
			Label(1, strings?.Text(MfdLayout.UnknownNameGroup, 0), MfdLayout.UnknownNameFont);
			return;
		}

		Label(1, subject.Name, subject.Hostile ? MfdLayout.HostileNameFont : MfdLayout.FriendlyNameFont);
		Label(2, strings?.Text(MfdLayout.StatusLabelGroup, 0), MfdLayout.StatusLabelFonts[2]);
		Label(3, strings?.Text(MfdLayout.ConditionGroup, subject.Condition), MfdLayout.StatusLabelFonts[3]);
		if (subject.Hostile) { // F5 TARGET screen, show distance.
			if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowTargetDistanceInMeters)) {
				Label(4, MfdLayout.DistanceReadout(strings, MfdScanner.WorldUnitsToMetres(subject.Distance)),
					MfdLayout.StatusLabelFonts[4]);
			} else { // Vanilla functionality is to show the distance in engine units (6mm / unit)
				Label(4, MfdLayout.DistanceReadout(strings, subject.Distance), MfdLayout.StatusLabelFonts[4]);
			}
		} else { // F1 STATUS screen, show hull integrity.
			Label(4, MfdLayout.IntegrityReadout(subject.Damage), MfdLayout.StatusLabelFonts[4]);
		}

		switch (subject.SilhouetteKind) {
			// The paper doll blits at the viewport's top-left plus the .PDG view's own origin plus a
			// fixed (0x11, 2) device nudge — the paint's own arithmetic, not a centring rule. The view's
			// origin is authored in the 320-wide space like every other .PDG coordinate.
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
						AddPaperDollTint(hud, dollSprites, subject.SilhouetteBank,
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
	/// The damage detail's weapon icons (<c>HddDamageScreen_BlitWeaponIcons</c>, <c>00451db8</c>) and the
	/// recolour <c>HddDamageScreen_Update</c> gives them, which is what makes them blue beside a green
	/// doll on two categories and green beside a blue one on the third. Every palette comparison is
	/// against the pixel the screen already holds, so the walks below composite the icons over the doll
	/// to answer it.
	///
	/// <list type="bullet">
	/// <item><b>Structural and internal.</b> The icons are blitted over the doll, then every
	/// <see cref="PaperDollDamage.OkColorId"/> pixel inside each icon's rect goes
	/// <see cref="PaperDollDamage.WeaponIconColorId"/> — doll pixels the rect covers included.</item>
	/// <item><b>Weapons.</b> The doll goes blue first: its view rect's id-12 pixels, then each region
	/// authored in another colour, over that region's rect. The icons are blitted over it and each is
	/// tinted from its hardpoint's reading, the green-to-grey ladder a doll region takes, so an intact
	/// weapon stays green. The doll under them is blue by then and takes no part.</item>
	/// </list>
	/// </summary>
	private static void AddHddWeaponIcons(CockpitArt hud, HudSpriteSheet sprites, HddDamageView view,
			string dollBank, PaperDollGraphic.ViewEntry doll, int dollView, PaperDollGraphic.HardpointEntry[]? entries,
			IReadOnlyList<DamageHardpoint?> hardpoints, IReadOnlyList<short>? readings, float dollLeft,
			float dollTop, Action<string, int, float, float> blit,
			Action<float, float, float, float, Vector3> fillRect) {
		const int S = (int)CockpitArt.GauToPixelScale;
		const string Bank = PaperDollDamage.WeaponIconBank;

		if (hud.Colors is not { } colors
			|| colors.PaletteIndex(PaperDollDamage.OkColorId) is not { } green
			|| hud.LogicalColor(PaperDollDamage.WeaponIconColorId) is not { } blue) {
			return;
		}

		var dollArt = sprites.Indexed(dollBank, dollView);

		if (view == HddDamageView.Weapons && dollArt is { } art) {
			int ArtAt(int x, int y) =>
				x >= 0 && y >= 0 && x < art.Width && y < art.Height ? art.Pixels[y * art.Width + x] : -1;

			AddIndexedRecolor(0, 0, doll.Size.X * S, doll.Size.Y * S, ArtAt, green, blue, dollLeft, dollTop,
				fillRect);
			foreach (var region in doll.Regions ?? Array.Empty<PaperDollGraphic.ViewRegion>()) {
				if (colors.PaletteIndex(region.ColorId) is { } key && key != green) {
					AddIndexedRecolor(region.TopLeft.X * S, region.TopLeft.Y * S, region.BottomRight.X * S + 1,
						region.BottomRight.Y * S + 1, ArtAt, key, blue, dollLeft, dollTop, fillRect);
				}
			}
		}

		// PaperDoll_BuildWeaponIcons: .PDG entry n places the icon of .GL slot n, and an empty slot, or
		// a weapon with no icon, places none.
		var icons = new List<(PaperDollDamage.WeaponIcon Icon, int Slot, HudSpriteSheet.IndexedFrame Art)>();
		if (hud.WeaponIconSizes is { } sizes) {
			for (int slot = 0; slot < (entries?.Length ?? 0); slot++) {
				if (slot < hardpoints.Count && hardpoints[slot] is { } hardpoint
					&& PaperDollDamage.PlaceWeaponIcon(entries![slot], hardpoint.Icon, sizes, S) is { } icon
					&& sprites.Indexed(Bank, icon.Frame) is { } iconArt) {
					blit(Bank, icon.Frame, dollLeft + icon.X, dollTop + icon.Y);
					icons.Add((icon, slot, iconArt));
				}
			}
		}

		// The topmost icon pixel at a doll-relative point, the last blitted winning; -1 where no icon
		// has one.
		int IconAt(int x, int y) {
			for (int i = icons.Count - 1; i >= 0; i--) {
				var (icon, _, iconArt) = icons[i];
				int u = x - icon.X, v = y - icon.Y;
				if (u >= 0 && v >= 0 && u < iconArt.Width && v < iconArt.Height
					&& iconArt.Pixels[v * iconArt.Width + u] is var pixel and not 0) {
					return pixel;
				}
			}

			return -1;
		}

		foreach (var (icon, slot, _) in icons) {
			int x1 = icon.X + icon.Width, y1 = icon.Y + icon.Height;

			if (view != HddDamageView.Weapons) {
				int ScreenAt(int x, int y) => IconAt(x, y) is var pixel and >= 0 ? pixel
					: dollArt is { } art && x >= 0 && y >= 0 && x < art.Width && y < art.Height
						? art.Pixels[y * art.Width + x]
						: -1;
				AddIndexedRecolor(icon.X, icon.Y, x1, y1, ScreenAt, green, blue, dollLeft, dollTop, fillRect);
				continue;
			}

			if (readings == null || PaperDollDamage.WeaponRowReading(slot, readings) is not { } reading) {
				continue;
			}

			int tintId = PaperDollDamage.TintColorId(PaperDollDamage.State(reading));
			if (colors.PaletteIndex(tintId) != green && hud.LogicalColor(tintId) is { } tint) {
				AddIndexedRecolor(icon.X, icon.Y, x1, y1, IconAt, green, tint, dollLeft, dollTop, fillRect);
			}
		}
	}

	/// <summary>
	/// <c>PaperDoll_RecolorRect</c>'s walk over an inclusive rect relative to the doll's origin: every
	/// point whose palette index <paramref name="indexAt"/> reports as <paramref name="key"/> is filled
	/// with <paramref name="tint"/>, as merged horizontal runs. An icon can sit left of or above the
	/// origin, so <paramref name="indexAt"/> answers for any point and -1 for one holding nothing.
	/// </summary>
	private static void AddIndexedRecolor(int x0, int y0, int x1, int y1, Func<int, int, int> indexAt, int key,
			Vector3 tint, float left, float top, Action<float, float, float, float, Vector3> fillRect) {
		for (int y = y0; y <= y1; y++) {
			int run = int.MinValue;
			for (int x = x0; x <= x1 + 1; x++) {
				if (x <= x1 && indexAt(x, y) == key) {
					run = run == int.MinValue ? x : run;
				} else if (run != int.MinValue) {
					fillRect(left + run, top + y, left + x, top + y + 1, tint);
					run = int.MinValue;
				}
			}
		}
	}

	/// <summary>
	/// One paper-doll region repainted at its current damage — <c>PaperDoll_RecolorRect</c> in its mode 0 arm,
	/// the only arm the retail <c>.PDG</c> files reach. It walks the region's rect a pixel at a time
	/// and rewrites just the pixels still holding the colour the art drew that body part in, which is
	/// why the outlines and rivets over a limb survive the recolour.
	///
	/// <para>Both colours are palette indices: the region's own is the <c>COLORS.DAT</c> id the file
	/// carries, resolved at load in the original (<c>PaperDoll_Load</c>) and here at draw time, and
	/// the tint is <see cref="PaperDollDamage.TintColorId"/>'s. When the two agree the original skips
	/// the walk outright, and so does this — an undamaged region is already the colour it should
	/// be.</para>
	///
	/// <para>The rect is the file's inclusive corner pair, doubled and its far corner nudged one
	/// further, because the loader shifts every <c>.PDG</c> coordinate by the video mode's
	/// <c>X/YCoordShift</c> and adds that <c>+1</c> in the 640-wide mode — so the region covers the
	/// full 2x2 device footprint of each source pixel. Matching pixels go out as merged horizontal
	/// runs rather than one quad each.</para>
	/// </summary>
	private static void AddPaperDollTint(CockpitArt hud, HudSpriteSheet sprites, string bank, int frame,
			PaperDollGraphic.ViewRegion region, int? reading, float dollLeft, float dollTop,
			Action<float, float, float, float, Vector3> fillRect) {
		const int S = (int)CockpitArt.GauToPixelScale;

		if (reading is not { } damage
			|| hud.Colors?.PaletteIndex(region.ColorId) is not { } key
			|| sprites.Indexed(bank, frame) is not { } art) {
			return;
		}

		int tintId = PaperDollDamage.TintColorId(PaperDollDamage.State(damage));
		if (hud.Colors.PaletteIndex(tintId) == key || hud.LogicalColor(tintId) is not { } tint) {
			return;
		}

		int x0 = Math.Max(region.TopLeft.X * S, 0);
		int y0 = Math.Max(region.TopLeft.Y * S, 0);
		int x1 = Math.Min(region.BottomRight.X * S + 1, art.Width - 1);
		int y1 = Math.Min(region.BottomRight.Y * S + 1, art.Height - 1);

		for (int y = y0; y <= y1; y++) {
			int row = y * art.Width;
			int run = -1;
			for (int px = x0; px <= x1 + 1; px++) {
				bool match = px <= x1 && art.Pixels[row + px] == key;
				if (match) {
					run = run < 0 ? px : run;
				} else if (run >= 0) {
					fillRect(dollLeft + run, dollTop + y, dollLeft + px, dollTop + y + 1, tint);
					run = -1;
				}
			}
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

		int plate = state.PressedWidget == CockpitWidgetId.Mfd(MfdLayout.TransmitButton)
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
	/// <see cref="AddWidgets"/>. The <b>ring</b> appears only
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
			AddCircleOutline(centerX, centerY, MfdScanner.PassiveRingRange / worldPerPixel, ring, fillRect);
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
	/// A one-device-pixel circle outline, stamped a pixel at a time by the midpoint algorithm. The
	/// original rasterizes it through its general ellipse routine (<c>Raster_DrawEllipse</c>, <c>00488070</c>) with the brush
	/// in outline mode; this reproduces the same aliased ring without a second drawing primitive. The
	/// scanner's passive-range ring and the MISSILE CAM's sight ring are its two uses.
	/// </summary>
	private static void AddCircleOutline(float centerX, float centerY, int radius, Vector3 color,
			Action<float, float, float, float, Vector3> fillRect) {
		if (radius <= 0) {
			return;
		}

		void Plot(int dx, int dy) =>
			fillRect(centerX + dx, centerY + dy, centerX + dx + 1, centerY + dy + 1, color);

		int px = radius;
		int py = 0;
		int error = 1 - radius;
		while (px >= py) {
			Plot(px, py); Plot(py, px); Plot(-py, px); Plot(-px, py);
			Plot(-px, -py); Plot(-py, -px); Plot(py, -px); Plot(px, -py);
			py++;
			if (error < 0) {
				error += 2 * py + 1;
			} else {
				px--;
				error += 2 * (py - px) + 1;
			}
		}
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
	private delegate float MfdTextWriter(string font, string alternateFont, string text,
		int hotkeyIndex, float deviceLeft, float deviceTop);

	/// <summary>
	/// The three console buttons: a <c>PWEAPONS</c> plate with a caption centred on it.
	///
	/// <para>The plate is not canopy art — <c>ConsoleButton_Paint</c> (<c>00442c88</c>) blits it per
	/// frame from <c>PWEAPONS</c> frames 2 and 3, indexed <c>bank[2 + state]</c>, at the widget's
	/// own rect. Frame 2 is the unlit plate (solid palette index 34, the blue the retail screenshot
	/// shows at RGB (77,77,182)) and frame 3 the lit one (index 14, green). Both are 50x16 against
	/// a 48x14 rect, the same one-pixel overhang the weapon-row plates have, and all three buttons 
	/// are that same 24x7 GAU size in every retail file.</para>
	///
	/// <para>The chain button's caption is its count in Roman numerals, read from DBSIM's own
	/// three-entry table at <c>0049c71c</c> ("I", "II", "III") — a literal table in <c>.rdata</c>,
	/// unrelated to the string file. LINK and TRACK are not fixed: <c>ConsoleButton_Paint</c>
	/// (<c>00442c88</c>) reads them out of <c>ConsoleButtonCaptions</c> (<c>004d13d0</c>), the <c>.bss</c> array
	/// <c>SimStrings_LoadAll</c> fills from <c>STRINGS0.STR</c> group <see cref="CaptionGroup"/>,
	/// indexed by the widget's own kind field (1 = LINK, 2 = TRACK) — see
	/// docs/formats/str-strings.md.</para>
	/// </summary>
	private static void AddConsoleButtons(GAUFile gau, StringFile? strings, CockpitHudState state,
			Action<string, int, float, float> blit,
			Action<string, string, int, int, int, int, Vector3?> drawCentered) {
		const float S = CockpitArt.GauToPixelScale;

		void Button(string? text, WidgetBase? widget, bool lit = false) {
			if (widget == null) {
				return;
			}

			blit("PWEAPONS", lit ? 3 : 2, widget.Origin.X * S, widget.Origin.Y * S);
			if (text is { Length: > 0 }) {
				drawCentered("WHITE", text, widget.Origin.X, widget.Origin.Y,
					widget.Origin.X + widget.Size.Width, widget.Origin.Y + widget.Size.Height, null);
			}
		}

		// Firing chain and LINK light only while held; TRACK latches. ConsoleButton_Paint
		// (00442c88) takes the first two from the shared press byte and the third from its own flag.
		bool Held(ConsoleButton which) => state.PressedWidget == CockpitWidgetId.Console(which);

		Button(new string('I', Math.Clamp(state.ChainGroup + 1, 1, 3)), gau.ChainButton,
			Held(ConsoleButton.Chain));
		Button(strings?.Text(CaptionGroup, 1), gau.LinkButton, Held(ConsoleButton.Link));
		Button(strings?.Text(CaptionGroup, 2), gau.AutoTrackButton, state.AutoTrack);
	}

	/// <summary>
	/// <c>STRINGS0.STR</c> group 4: the console button captions — index 0 is <c>"I"</c> (unused; the
	/// chain button gets its numerals from <see cref="AddConsoleButtons"/>'s own table instead), 1 is
	/// <c>"LINK"</c>, 2 is <c>"TRACK"</c>, 3 is empty.
	/// </summary>
	private const int CaptionGroup = 4;

	/// <summary>
	/// The manual's <b>ATT</b> legend: while the TRACK button is latched, <c>Gunsight_Paint</c>
	/// (<c>0043d5c8</c>) and <c>Gunsight_UpdateAndPaint</c> (<c>0043d6dc</c>) blit <c>HUD</c> frame
	/// <see cref="AutoTrackLegendPlateFrame"/> at the legend rect's top-left and centre the text in
	/// that rect in <c>DARK</c>. Nothing is drawn while it is off. See
	/// docs/formats/cockpit-gunsight-hud.md#the-att-legend.
	/// </summary>
	private static void AddAutoTrackLegend(GAUFile gau, StringFile? strings, CockpitHudState state,
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
	private static void AddGunsightReadouts(GAUFile gau, HudSpriteSheet sprites, CockpitHudState state,
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

	/// <summary>
	/// Height of one comm-box label. <c>HddGauge_LoadPilotFrames</c> states only the y each label
	/// starts at — 8, 32, 48, 64 and 80 device pixels down the box — and the four in the body are 16
	/// apart, so each row is given that pitch less a two-pixel gap. The glyphs themselves are placed
	/// by <see cref="HudFont.Place"/>, which centres them in whatever rect it is handed.
	/// </summary>
	private const int PilotLabelHeight = 14;

	/// <summary>The gap between a caption and its value: the constructor's own <c>2 &lt;&lt; XCoordShift</c>, four device pixels wide.</summary>
	private const float ReadoutGap = 2 * CockpitArt.GauToPixelScale;


	/// <summary>
	/// One line of panel text: the font it is set in, what it says, the rect it is placed in and how
	/// it sits in that rect. Every panel but the preferences one centres with no margin.
	/// </summary>
	private readonly record struct PanelLine(string Font, string Text, AlertPanelLayout.Rect Rect,
		LabelAlign Align = LabelAlign.Center, int MarginX = 0);

	/// <summary>
	/// One panel button: its caption, its rect, and the plate frame and caption font its current
	/// widget state selects. Three of the four panels take those last two from the shared
	/// <see cref="AlertPanelLayout.ButtonBank"/> via <see cref="SharedButton"/>; the preferences panel
	/// blits its own bank instead.
	/// </summary>
	private readonly record struct PanelButton(string Caption, AlertPanelLayout.Rect Rect,
		string Bank, int Frame, string Font);

	/// <summary>A button drawn from the bank the alert-panel family shares, in one of its two states.</summary>
	private static PanelButton SharedButton(string caption, AlertPanelLayout.Rect rect, bool pressed) =>
		new(caption, rect, AlertPanelLayout.ButtonBank, AlertPanelLayout.ButtonFrame(pressed),
			AlertPanelLayout.CaptionFont(pressed));

	/// <summary>
	/// The [F11] objectives panel, drawn over the whole window — <c>obj_alrt</c>
	/// (<c>ObjectivesPanel_Ctor</c>, <c>0045751c</c>) and its paint (<c>ObjectivesPanel_Paint</c>,
	/// <c>00457b58</c>), in that paint's own order: the background plate at the panel's origin, the
	/// title, the objective lines, then the one widget the panel owns.
	/// </summary>
	public void DrawObjectivesPanel(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, ObjectivesPanel panel) {
		ArgumentNullException.ThrowIfNull(panel);
		ArgumentNullException.ThrowIfNull(sprites);

		var lines = new List<PanelLine>(panel.Lines.Count);
		for (int i = 0; i < panel.Lines.Count; i++) {
			lines.Add(new PanelLine(ObjectiveLineFont, panel.Lines[i], ObjectivesPanelLayout.Line(i)));
		}

		DrawAlertPanel(windowWidth, windowHeight, spriteTexture, sprites,
			ObjectivesPanelLayout.Place(windowWidth, windowHeight),
			ObjectivesPanel.BackgroundBank, 0,
			panel.Title, ObjectivesPanelLayout.Title(Measure(sprites, AlertPanelLayout.TitleFont, panel.Title)),
			lines,
			new[] { SharedButton(panel.ButtonCaption, ObjectivesPanelLayout.Button, panel.ButtonPressed) });
	}

	/// <summary>
	/// The mission-status alert — <c>gnl_alrt</c> (<c>StatusAlertPanel_Ctor</c>, <c>00455934</c>) and
	/// its paint (<c>StatusAlertPanel_Paint</c>, <c>00456068</c>) — or the pause panel, which shares
	/// that same paint and differs only in its plate and its rects. Same shape as the objectives
	/// panel and the same draw; what differs is a green body font instead of a yellow one, and one or
	/// two buttons instead of exactly one.
	///
	/// <para><b>A one-line body starts on the second row</b>, not the first — the paint's own
	/// <c>lineCount == 1</c> test, which centres a short message in the block instead of hanging it
	/// from the top. <see cref="StatusAlertPanelLayout.FirstRowFor"/>. The pause panel never reaches
	/// it: its two statuses carry no body text at all.</para>
	/// </summary>
	public void DrawStatusAlertPanel(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, StatusAlertPanel panel) {
		ArgumentNullException.ThrowIfNull(panel);
		ArgumentNullException.ThrowIfNull(sprites);

		bool pause = panel.Variant == AlertPanelVariant.Pause;
		int firstRow = StatusAlertPanelLayout.FirstRowFor(panel.Body.Count);
		var lines = new List<PanelLine>(panel.Body.Count);
		for (int i = 0; i < panel.Body.Count; i++) {
			lines.Add(new PanelLine(StatusAlertPanelLayout.BodyFont, panel.Body[i],
				StatusAlertPanelLayout.BodyRow(firstRow + i)));
		}

		var buttons = new PanelButton[panel.Buttons.Count];
		for (int i = 0; i < buttons.Length; i++) {
			buttons[i] = SharedButton(panel.Buttons[i],
				StatusAlertPanel.ButtonRect(panel.Variant, i, buttons.Length),
				panel.PressedButton == i);
		}

		int titleWidth = Measure(sprites, AlertPanelLayout.TitleFont, panel.Title);

		DrawAlertPanel(windowWidth, windowHeight, spriteTexture, sprites,
			panel.Place(windowWidth, windowHeight),
			pause ? PausePanelLayout.PlateBank : StatusAlertPanelLayout.PlateBank,
			pause ? PausePanelLayout.PlateFrame : StatusAlertPanelLayout.PlateFrame,
			panel.Title,
			pause ? PausePanelLayout.Title(titleWidth) : StatusAlertPanelLayout.Title(titleWidth),
			lines, buttons);
	}

	/// <summary>
	/// The [F12] preferences panel — <c>PreferencesPanel_Ctor</c> (<c>004566c4</c>), drawn in its
	/// paint's own order: the plate at the panel's origin, the title, the nine value readouts, then
	/// the eleven buttons.
	///
	/// <para>Two things set it apart from the rest of the family. Its buttons come from its own
	/// <c>PRF_ALRT</c> bank rather than the shared <c>ALERT</c> one, four frames that differ only in
	/// border colour, and the nine option buttons sit in widget state 3 while CONTROLS and DONE sit in
	/// state 0 — which is the whole of why the bottom pair's border reads brighter than the rows
	/// above. And its readouts are left-aligned with a four-pixel inset where every other label on
	/// every other panel is centred. With no sound device the first four rows grey to state 2, the
	/// same way the controls panel's do with no stick.</para>
	///
	/// <para><c>Label_SetText</c> would also flood each readout's rect with <c>COLORS.DAT</c> id 19
	/// first, that being the label's <c>+0x1d</c> background colour. Nothing is drawn for it here: the
	/// plate already paints a black box behind each readout that is larger than the rect on both axes,
	/// so the fill lands entirely inside its own background.</para>
	/// </summary>
	public void DrawPreferencesPanel(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, PreferencesPanel panel) {
		ArgumentNullException.ThrowIfNull(panel);
		ArgumentNullException.ThrowIfNull(sprites);

		var values = new List<PanelLine>(panel.Values.Count);
		for (int i = 0; i < panel.Values.Count; i++) {
			values.Add(new PanelLine(PreferencesPanelLayout.ValueFont, panel.Values[i],
				PreferencesPanelLayout.Value(i), LabelAlign.Left, PreferencesPanelLayout.ValueMarginX));
		}

		var buttons = new PanelButton[panel.Captions.Count];
		for (int i = 0; i < buttons.Length; i++) {
			int state = panel.RowState(i);
			buttons[i] = new PanelButton(panel.Captions[i], PreferencesPanelLayout.Button(i),
				PreferencesPanelLayout.PlateBank, PreferencesPanelLayout.ButtonFrame(i, state),
				PanelButtonFont(state));
		}

		DrawAlertPanel(windowWidth, windowHeight, spriteTexture, sprites,
			PreferencesPanelLayout.Place(windowWidth, windowHeight),
			PreferencesPanelLayout.PlateBank, PreferencesPanelLayout.PlateFrame,
			panel.Title,
			PreferencesPanelLayout.Title(Measure(sprites, AlertPanelLayout.TitleFont, panel.Title)),
			values, buttons);
	}

	/// <summary>
	/// The CONTROLS panel — <c>ControlsPanel_Ctor</c> (<c>00457d1c</c>), drawn in its paint's own
	/// order (<c>ControlsPanel_Paint</c> (<c>00458c68</c>)): the plate at the panel's origin, the title, the fourteen buttons,
	/// the twelve value readouts, then the OPTIONS caption. The option rows go down with the readouts,
	/// both being <see cref="ControlsPanelLayout.ValueFont"/> labels the paint refreshes together.
	///
	/// <para>Its buttons come from its own <c>CTL_ALRT</c> bank in three sizes, each with a four-frame
	/// state set of its own, so every button carries a frame base as well as a state — see
	/// <see cref="ControlsPanelLayout.ButtonFrame"/>. A greyed row is state 2, which is how the panel
	/// reads with no stick attached.</para>
	///
	/// <para>The OPTIONS caption is the one label on this panel with no background colour of its own,
	/// which is why its rect can overlap the list box's top border without erasing it; every other
	/// label's fill lands inside a black well the plate already paints.</para>
	/// </summary>
	public void DrawControlsPanel(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, ControlsPanel panel) {
		ArgumentNullException.ThrowIfNull(panel);
		ArgumentNullException.ThrowIfNull(sprites);

		var lines = new List<PanelLine>(
			panel.Values.Count + ControlsPanelLayout.OptionRowCount + 1);

		for (int i = 0; i < panel.Values.Count; i++) {
			lines.Add(new PanelLine(ControlsPanelLayout.ValueFont, panel.Values[i],
				ControlsPanelLayout.Value(i), LabelAlign.Left, ControlsPanelLayout.LabelMarginX));
		}

		var optionRows = panel.OptionRows();
		for (int i = 0; i < optionRows.Count; i++) {
			lines.Add(new PanelLine(ControlsPanelLayout.ValueFont, optionRows[i],
				ControlsPanelLayout.OptionRow(i), LabelAlign.Center, ControlsPanelLayout.LabelMarginX));
		}

		lines.Add(new PanelLine(AlertPanelLayout.TitleFont, panel.OptionsCaption,
			ControlsPanelLayout.OptionsTitle(), LabelAlign.Center, ControlsPanelLayout.LabelMarginX));

		var buttons = new PanelButton[panel.Captions.Count];
		for (int i = 0; i < buttons.Length; i++) {
			int state = panel.RowState(i);
			buttons[i] = new PanelButton(panel.Captions[i], ControlsPanelLayout.Button(i),
				ControlsPanelLayout.PlateBank, ControlsPanelLayout.ButtonFrame(i, state),
				PanelButtonFont(state));
		}

		DrawAlertPanel(windowWidth, windowHeight, spriteTexture, sprites,
			ControlsPanelLayout.Place(windowWidth, windowHeight),
			ControlsPanelLayout.PlateBank, ControlsPanelLayout.PlateFrame,
			panel.Title,
			ControlsPanelLayout.Title(Measure(sprites, AlertPanelLayout.TitleFont, panel.Title)),
			lines, buttons);
	}

	/// <summary>
	/// The external view's caption, <c>ViewChain_DrawCaption</c> (<c>0045e1ec</c>): two runs of <see cref="ExternalViewLayout.CaptionFont"/>
	/// on one row below the 3D view, placed on a 640x480 screen centred in the window the way the
	/// modal panels are. The paint floods the row with colour 19 first, which is the black the rest of
	/// the band is already, so nothing is drawn for it here.
	/// </summary>
	public void DrawExternalViewCaption(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, string view, string control) {
		ArgumentNullException.ThrowIfNull(spriteTexture);
		ArgumentNullException.ThrowIfNull(sprites);

		if (sprites.Font(ExternalViewLayout.CaptionFont) is not { } font) {
			return;
		}

		var place = AlertPanelLayout.Placement.CreateAt(windowWidth, windowHeight, 0, 0);

		_gl.Viewport(0, 0, (uint)Math.Max(windowWidth, 1), (uint)Math.Max(windowHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(windowWidth, windowHeight));
		_vertices.Clear();

		int top = ExternalViewLayout.CaptionBaseline(font.CellHeight) - font.InkHeight;
		Run(view, ExternalViewLayout.ViewCaptionX);
		Run(control, ExternalViewLayout.ControlCaptionX);

		if (_vertices.Count > 0) {
			_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
			_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
		}

		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);

		// HudFont_DrawString: glyph after glyph from the run's left edge, each advancing by its own width.
		void Run(string text, float left) {
			float pen = left;
			foreach (char c in text) {
				if (font.GlyphIndex(c) is { } glyph
						&& sprites.Sprite(ExternalViewLayout.CaptionFont, glyph) is { Width: > 0, Height: > 0 } sprite) {
					var (x0, y0) = place.ToWindow(pen, top);
					var (x1, y1) = place.ToWindow(pen + sprite.Width * sprite.Scale, top + sprite.Height * sprite.Scale);
					var r = sprite.Rect;
					AddTexturedQuad(x0, y0, x1, y1, r.U0, r.V0, r.U1, r.V1);
				}

				pen += font.Width(c);
			}
		}
	}

	/// <summary>
	/// The caption font for a panel button in widget state 0-3 — <c>PanelButton_Ctor</c>'s own
	/// four-entry table at <c>+0x40</c>: ACTIVE at rest, PUSHED held, INACTIVE disabled, and ACTIVE
	/// again for the fourth state. The controls panel is the only one this engine draws that reaches
	/// the third entry.
	/// </summary>
	private static string PanelButtonFont(int state) => state switch {
		1 => AlertPanelLayout.ButtonPressedFont,
		2 => AlertPanelLayout.DisabledButtonFont,
		_ => AlertPanelLayout.ButtonFont,
	};

	/// <summary>
	/// One modal alert panel, drawn over the whole window in the order every panel of the family
	/// paints itself: the plate at the panel's own origin, the title, the body rows, then each
	/// button — its plate for the state it is in, and its caption over that.
	///
	/// <para>It is its own viewport and its own draw rather than a layer on a cockpit panel: the
	/// original's panels are screen-space modals centred on the 640x480 screen, not something
	/// anchored to a piece of canopy art, and they have to sit over all three cockpit panels and the
	/// heads-down view at once. The <see cref="AlertPanelLayout.Placement"/> the caller passes is the
	/// transform, and the input path takes the same one so a click cannot drift off its button.</para>
	///
	/// <para>Everything comes out of the cockpit's shared atlas — the plates are banks in it and the
	/// fonts are glyph runs in it — so a whole panel is one bind and one draw.</para>
	/// </summary>
	private void DrawAlertPanel(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, AlertPanelLayout.Placement place, string plateBank, int plateFrame,
			string title, AlertPanelLayout.Rect titleRect,
			IReadOnlyList<PanelLine> lines, IReadOnlyList<PanelButton> buttons) {
		ArgumentNullException.ThrowIfNull(spriteTexture);

		_gl.Viewport(0, 0, (uint)Math.Max(windowWidth, 1), (uint)Math.Max(windowHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(windowWidth, windowHeight));
		_vertices.Clear();

		Blit(plateBank, plateFrame, 0, 0);
		Label(AlertPanelLayout.TitleFont, title, titleRect, LabelAlign.Center);

		foreach (var line in lines) {
			Label(line.Font, line.Text, line.Rect, line.Align, line.MarginX);
		}

		// A button's plate art is larger than its widget rect on both axes and is blitted at the
		// rect's origin, so it overhangs to the right and below — the original's own placement.
		foreach (var button in buttons) {
			Blit(button.Bank, button.Frame, button.Rect.X0, button.Rect.Y0);
			Label(button.Font, button.Caption, button.Rect, LabelAlign.Center);
		}

		if (_vertices.Count > 0) {
			_shader.SetSamplerTexture("uTexture", spriteTexture.Handle, 0);
			_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
		}

		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);

		void Blit(string bank, int frame, float left, float top) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var (x0, y0) = place.ToWindow(left, top);
			var (x1, y1) = place.ToWindow(left + sprite.Width * sprite.Scale, top + sprite.Height * sprite.Scale);
			var r = sprite.Rect;
			AddTexturedQuad(x0, y0, x1, y1, r.U0, r.V0, r.U1, r.V1);
		}

		// Titles and button captions are centred with no margin; only the preferences panel's value
		// readouts pass anything else, and they pass left with the constructor's own 4px inset.
		void Label(string fontName, string text, AlertPanelLayout.Rect rect, LabelAlign align,
				int marginX = 0) {
			if (text.Length == 0 || sprites.Font(fontName) is not { } font) {
				return;
			}

			var (textX, textY) = font.Place(text, rect.X0, rect.Y0, rect.X1, rect.Y1, align, marginX);
			float pen = textX;
			foreach (char c in text) {
				if (font.GlyphIndex(c) is { } glyph) {
					Blit(fontName, glyph, pen, textY);
					pen += font.Width(c);
				}
			}
		}
	}

	private static int Measure(HudSpriteSheet sprites, string fontName, string text) =>
		sprites.Font(fontName)?.Measure(text) ?? 0;

	/// <summary>
	/// The objectives panel's line font, <c>DAT_004d1ea0</c> — the cockpit yellow the retail
	/// screenshot shows. Both panels construct their button captions in it too, and neither is ever
	/// drawn in it: a button's paint overwrites the label's font from its own state table every time.
	/// </summary>
	private const string ObjectiveLineFont = "CPYLW";

	private void AddTexturedQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1) {
		var a = new Overlay2DVertex(new Vector2(x0, y0), new Vector2(u0, v0));
		var b = new Overlay2DVertex(new Vector2(x1, y0), new Vector2(u1, v0));
		var c = new Overlay2DVertex(new Vector2(x1, y1), new Vector2(u1, v1));
		var d = new Overlay2DVertex(new Vector2(x0, y1), new Vector2(u0, v1));
		_vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
		_vertices.Add(a); _vertices.Add(c); _vertices.Add(d);
	}

	/// <summary>
	/// The same quad with its four corners given explicitly, for art that is rotated rather than
	/// axis-aligned. Corners run top-left, top-right, bottom-right, bottom-left in the <i>source</i>
	/// bitmap's own order, so the UVs pair with them regardless of where the rotation puts them.
	/// </summary>
	private void AddTexturedQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d,
			float u0, float v0, float u1, float v1) {
		var va = new Overlay2DVertex(a, new Vector2(u0, v0));
		var vb = new Overlay2DVertex(b, new Vector2(u1, v0));
		var vc = new Overlay2DVertex(c, new Vector2(u1, v1));
		var vd = new Overlay2DVertex(d, new Vector2(u0, v1));
		_vertices.Add(va); _vertices.Add(vb); _vertices.Add(vc);
		_vertices.Add(va); _vertices.Add(vc); _vertices.Add(vd);
	}

	public void Dispose() {
		_shader.Dispose();
		_mesh.Dispose();
	}
}
