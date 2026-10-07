using Herculan.Engine.Cockpit;
using Herculan.Engine.View;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// What the sim draws on the 640x480 screen centred in the window outside any panel's art: the
/// external view's caption, and the two system buttons.
/// </summary>
public sealed class ScreenChromePainter {
	private readonly Overlay2DRenderer _overlay;

	public ScreenChromePainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
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

		_overlay.Begin(0, 0, windowWidth, windowHeight);
		_overlay.Clear();

		int top = ExternalViewLayout.CaptionBaseline(font.CellHeight) - font.InkHeight;
		Run(view, ExternalViewLayout.ViewCaptionX);
		Run(control, ExternalViewLayout.ControlCaptionX);

		if (_overlay.VertexCount > 0) {
			_overlay.Submit(spriteTexture);
		}

		_overlay.End();

		// HudFont_DrawString: glyph after glyph from the run's left edge, each advancing by its own width.
		void Run(string text, float left) {
			float pen = left;
			foreach (char c in text) {
				if (font.GlyphIndex(c) is { } glyph
						&& sprites.Sprite(ExternalViewLayout.CaptionFont, glyph) is { Width: > 0, Height: > 0 } sprite) {
					var (x0, y0) = place.ToWindow(pen, top);
					var (x1, y1) = place.ToWindow(pen + sprite.Width * sprite.Scale, top + sprite.Height * sprite.Scale);
					var r = sprite.Rect;
					_overlay.AddTexturedQuad(x0, y0, x1, y1, r.U0, r.V0, r.U1, r.V1);
				}

				pen += font.Width(c);
			}
		}
	}

	/// <summary>
	/// The two system buttons, each blitted at its rect's origin on the 640x480 screen while
	/// <see cref="SystemButtons.Showing"/> — <c>SystemGadget_Paint</c> (<c>00434748</c>)'s state-0 arm. Its
	/// state-3 arm puts back the pixels the blit covered, which here is drawing nothing. The caller
	/// draws this over the cockpit and under any modal panel, as <c>Sim_RenderFrame</c>'s last call.
	/// </summary>
	/// <param name="showing">Which buttons are showing, indexed by <see cref="SystemButton"/>.</param>
	public void DrawSystemButtons(int windowWidth, int windowHeight, GpuTexture spriteTexture,
			HudSpriteSheet sprites, ReadOnlySpan<bool> showing) {
		ArgumentNullException.ThrowIfNull(spriteTexture);
		ArgumentNullException.ThrowIfNull(sprites);

		var place = SystemButtons.Place(windowWidth, windowHeight);
		_overlay.Clear();

		for (int i = 0; i < SystemButtons.Count && i < showing.Length; i++) {
			var button = (SystemButton)i;
			if (!showing[i] || sprites.Sprite(SystemButtons.Bank, SystemButtons.Frame(button)) is not
					{ Width: > 0, Height: > 0 } sprite) {
				continue;
			}

			var rect = SystemButtons.Rect(button);
			var (x0, y0) = place.ToWindow(rect.X0, rect.Y0);
			var (x1, y1) = place.ToWindow(rect.X0 + sprite.Width * sprite.Scale, rect.Y0 + sprite.Height * sprite.Scale);
			var r = sprite.Rect;
			_overlay.AddTexturedQuad(x0, y0, x1, y1, r.U0, r.V0, r.U1, r.V1);
		}

		if (_overlay.VertexCount == 0) {
			return;
		}

		_overlay.Begin(0, 0, windowWidth, windowHeight);
		_overlay.Submit(spriteTexture);

		_overlay.End();
	}
}
