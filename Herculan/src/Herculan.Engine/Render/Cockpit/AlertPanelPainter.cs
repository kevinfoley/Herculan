using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The modal alert-panel family drawn over the whole window — objectives, mission status and pause,
/// preferences and controls — each through <see cref="DrawAlertPanel"/>.
/// </summary>
public sealed class AlertPanelPainter {
	private readonly Overlay2DRenderer _overlay;

	public AlertPanelPainter(Overlay2DRenderer overlay) {
		_overlay = overlay;
	}

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
				panel.ShowsPressed(i));
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

		_overlay.Begin(0, 0, windowWidth, windowHeight);
		_overlay.Clear();

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

		if (_overlay.VertexCount > 0) {
			_overlay.Submit(spriteTexture);
		}

		_overlay.End();

		void Blit(string bank, int frame, float left, float top) {
			if (sprites.Sprite(bank, frame) is not { } sprite || sprite.Width <= 0 || sprite.Height <= 0) {
				return;
			}

			var (x0, y0) = place.ToWindow(left, top);
			var (x1, y1) = place.ToWindow(left + sprite.Width * sprite.Scale, top + sprite.Height * sprite.Scale);
			var r = sprite.Rect;
			_overlay.AddTexturedQuad(x0, y0, x1, y1, r.U0, r.V0, r.U1, r.V1);
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
}
