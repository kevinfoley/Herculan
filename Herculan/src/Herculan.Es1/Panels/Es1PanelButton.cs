using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// A panel's button, ES2's <c>AlertSelectGadget</c> on ES1's gadget layout, its table <c>AlertSelectGadgetVtable</c>
/// (<c>ES1DBSIM 00094f96</c>) (docs/es1/simulation/alert-panels.md#the-panels-buttons): drawn as the frame of a style
/// record with its caption in a font, both picked by its state, 0 at rest, 1 pressed and 2 greyed. A mouse press puts
/// it in state 1 and repaints it, as in the cockpit; a key's press through <see cref="Es1AlertPanel.PressWidget"/>
/// leaves its state alone.
/// </summary>
public sealed class Es1PanelButton : Es1Widget {
	/// <summary>The paint counter a repaint sets, so the button is painted on the next two passes of its panel's loop.</summary>
	public const byte RepaintPasses = 2;

	private readonly Es1WidgetStyle?[] _styles;
	private readonly HudFont?[] _fonts;

	/// <summary>
	/// <c>PanelButton_Ctor</c> (<c>000467c8</c>)(0, <paramref name="index"/>, <paramref name="owner"/>,
	/// <paramref name="rect"/>): the widget on the rect, the owner <c>+0x20</c>, the index <c>+0x28</c>, state 0 and drag
	/// flag 0; and the three-entry tables indexed by the state, style records 0, 1 and 2 at <c>+0x3c</c> and the fonts
	/// <c>active</c>, <c>pushed</c> and <c>inactive</c> at <c>+0x48</c>. The caption label is left unset.
	/// </summary>
	public Es1PanelButton(int index, Es1AlertPanel owner, Es1Rect rect) {
		Rect = rect;
		Owner = owner;
		Index = index;
		var widgets = owner.Globals.Widgets;
		_styles = [widgets.Style(0), widgets.Style(1), widgets.Style(2)];
		_fonts = [widgets.Active, widgets.Pushed, widgets.Inactive];
	}

	/// <summary><c>+0x20</c>, the panel.</summary>
	public Es1AlertPanel Owner { get; }

	/// <summary><c>+0x28</c>, the index the owner's caption is asked by.</summary>
	public int Index { get; }

	/// <summary><c>+0x2c</c>, the caption label, which the member sets.</summary>
	public Es1Label? Caption { get; set; }

	/// <summary><c>+0x1b</c>, the paint counter.</summary>
	public byte PaintCounter { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>PanelButton_Paint</c> (<c>000468e0</c>): the cursor hidden; the frame of the style for its
	/// state on its rect; the owner's caption for its index set on the caption label in the font for its state; the
	/// cursor shown.
	/// </summary>
	public override void Paint() {
		var cursor = Owner.Globals.Display.Cursor;
		cursor.Hide();
		Es1CornerFrame.Draw(Owner.Canvas, ForState(_styles), Rect, Owner.Globals.Widgets.Corners);
		string? caption = Owner.GetButtonCaption(Index);
		if (Caption != null) {
			Caption.Font = ForState(_fonts);
			Caption.SetText(Owner.Canvas, caption);
		}

		cursor.Show();
	}

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredAlertCopy</c> (<c>000468b8</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = RepaintPasses;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>PanelButton_OnClick</c> (<c>000469ac</c>)(button, <paramref name="value"/>): the owner's
	/// child click with the flags and the button, the focus moved to the button, and its own invalidate.
	/// PLACEHOLDER: the mixin's click sound, <c>Widget_ClickSound</c> (<c>000314a0</c>), <c>Sound_Play</c>(<c>0x11</c>),
	/// is not played; no plan step ports ES1's sound yet.
	/// </summary>
	public override void OnClick(short value) {
		Owner.OnChildClick(value, this);
		Owner.FocusChild(this);
		Invalidate();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>PanelButton_Tick</c> (<c>0004698c</c>): while the paint counter is not 0, a paint and 1 off
	/// the counter, one a call.
	/// </summary>
	public override void Tick() {
		if (PaintCounter != 0) {
			Paint();
			PaintCounter--;
		}
	}

	/// <summary>Slot <c>+0x14</c>, the button base's value: <c>Widget_ClickButtons</c> (<c>00094b84</c>).</summary>
	public override short GetValue(Es1WidgetGlobals globals) => (short)globals.ClickButtons;

	/// <summary>Slot <c>+0x1c</c>, the button base's drag, which does nothing.</summary>
	public override void DragTo(int x, int y) {
	}

	/// <summary>The table's entry for <see cref="Es1Widget.State"/>; none past the three entries.</summary>
	private T? ForState<T>(T?[] table) where T : class => State >= 0 && State < table.Length ? table[State] : null;
}
