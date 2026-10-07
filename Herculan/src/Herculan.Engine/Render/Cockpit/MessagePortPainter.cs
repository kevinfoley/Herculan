using Herculan.Engine.Cockpit;
using System.Numerics;
using Herculan.Engine.Content;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The front window's message ports: the cockpit computer's ticker, the pilot and squad channel's line,
/// and the training port's block.
/// </summary>
internal sealed class MessagePortPainter {
	private readonly Overlay2DRenderer _overlay;

	public MessagePortPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
	}

	/// <summary>
	/// The training port's block — the instructor's sentences wrapped into white lines, left-aligned
	/// in a black box with a red frame, the box as wide as the widest line and centred on the screen.
	/// <see cref="TrainingMessageLayout"/> carries the rules.
	/// </summary>
	internal void AddTrainingMessage(CockpitArt hud, HudSpriteSheet sprites, TrainingMessageBox? message,
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
			_overlay.AddFilledRect(dx(left), dy(box.Top), dx(right), dy(bottom), fill);
		}

		if (hud.LogicalColor(TrainingMessageLayout.BorderColorId) is { } frame) {
			_overlay.AddRectOutline(dx(left), dy(box.Top), dx(right), dy(bottom), scale, frame);
		}

		for (int i = 0; i < block.Lines.Count; i++) {
			float pen = TrainingMessageLayout.TextLeft(screen, textWidth);
			float top = box.LineTop(i, font);

			foreach (char c in block.Lines[i]) {
				if (font.GlyphIndex(c) is { } glyph
					&& sprites.Sprite(TrainingMessageLayout.Font, glyph) is { Width: > 0, Height: > 0 } cell) {
					var r = cell.Rect;
					_overlay.AddTexturedQuad(dx(pen), dy(top), dx(pen + cell.Width), dy(top + cell.Height),
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
	internal void AddPilotMessage(CockpitArt hud, HudSpriteSheet sprites, PilotMessageLine? line,
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
			_overlay.AddFilledRect(dx(left), dy(box.Top), dx(right), dy(box.Bottom), background);
		}

		if (border is { } frame) {
			_overlay.AddRectOutline(dx(left), dy(box.Top), dx(right), dy(box.Bottom), scale, frame);
		}

		float pen = PilotMessageBoxLayout.TextLeft(screen, textWidth);
		float top = box.TextTop(font);

		foreach (char c in message.Text) {
			if (font.GlyphIndex(c) is { } glyph
				&& sprites.Sprite(PilotMessageBoxLayout.Font, glyph) is { Width: > 0, Height: > 0 } cell) {
				var r = cell.Rect;
				_overlay.AddTexturedQuad(dx(pen), dy(top), dx(pen + cell.Width), dy(top + cell.Height),
					r.U0, r.V0, r.U1, r.V1);
			}

			pen += font.Width(c);
		}
	}

	/// <summary>
	/// The cockpit computer's message ticker — a black box with a red frame, and one line of red text
	/// scrolling right to left inside it. <see cref="MessagePort"/> decides what is in it and
	/// for how long; <see cref="MessageTickerLayout"/> says where it is and where the line sits.
	///
	/// <para>The text is clipped horizontally, per glyph, against the box's inset edges — the
	/// original narrows its live clip rect between drawing the frame and drawing the line
	/// (<c>MessageTicker_Paint</c>, <c>00436cec</c>), which is what makes the marquee slide under the frame instead of past it.
	/// Clipping the geometry rather than setting a GL scissor keeps the whole panel one batch, and a
	/// horizontal trim is all that is needed: the glyph row is centred in a box taller than it.</para>
	/// </summary>
	internal void AddMessageTicker(CockpitArt hud, HudSpriteSheet sprites, in MessageTicker ticker,
			Func<float, float> dx, Func<float, float> dy, float scale) {
		if (!ticker.HasText
			|| MessageTickerLayout.From(hud) is not { } box
			|| sprites.Font(MessageTickerLayout.Font) is not { } font) {
			return;
		}

		if (hud.LogicalColor(MessageTickerLayout.BackgroundColorId) is { } background) {
			_overlay.AddFilledRect(dx(box.Left), dy(box.Top), dx(box.Right), dy(box.Bottom), background);
		}

		if (hud.LogicalColor(MessageTickerLayout.BorderColorId) is { } border) {
			_overlay.AddRectOutline(dx(box.Left), dy(box.Top), dx(box.Right), dy(box.Bottom), scale, border);
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
				_overlay.AddTexturedQuad(dx(left), dy(top), dx(right), dy(top + cell.Height), u0, r.V0, u1, r.V1);
			}

			pen += width;
		}
	}
}
