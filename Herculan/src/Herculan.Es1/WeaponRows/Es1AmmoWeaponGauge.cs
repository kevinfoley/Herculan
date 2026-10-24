using System.Globalization;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// An ammunition weapon's row, <c>AmmoWeaponGauge</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-ammunition-row):
/// the number button with the name and state box, and the round count in yellow on a plate, on
/// <c>ProjectileWeaponGaugeVtable</c> (<c>00093aae</c>).
/// </summary>
public sealed class Es1AmmoWeaponGauge : Es1WeaponRowGauge {
	/// <summary>
	/// <c>AmmoWeaponGauge_Ctor</c> (<c>000373a4</c>)(0, <paramref name="mount"/>, <paramref name="name"/>,
	/// <paramref name="row"/>, <paramref name="cockpit"/>, block), the block the ammunition block at 164 with the row's
	/// rect <paramref name="rect"/> in its first 16 bytes: with (<i>x</i>, <i>y</i>) the row's top-left, child 0 the
	/// chained number button on the block's rect at <c>+0x10</c> moved by it and registered on the cockpit's root; the
	/// name's first 12 bytes; the value rect, the block's rect at <c>+0x20</c> moved by it; the round count's label in
	/// <c>cpylw</c> placed left-aligned over it with zero margins and the fill color <see cref="Es1WeaponGauge.DarkPlate"/>;
	/// and the state block cleared.
	/// </summary>
	internal Es1AmmoWeaponGauge(Es1Cockpit cockpit, Es1WeaponMount mount, string? name, short row, Es1Rect rect,
			Es1GauAmmunitionRowBlock block, IReadOnlyList<HudFont?> fonts, IReadOnlyList<DynamixBitmap>? pushButtons,
			string? offline)
		: base(cockpit, mount, row, rect) {
		SelectButton = new Es1ChainedWeaponSelectButton(this, row, Es1Rect.From(block.SelectButton).Offset(rect.X0, rect.Y0),
			fonts, pushButtons, offline);
		cockpit.Root.RegisterClickable(SelectButton);
		Name = Truncate(name);
		ValueRect = Es1Rect.From(block.RoundCount).Offset(rect.X0, rect.Y0);
		CountLabel = new Es1Label(Font(fonts, Es1CockpitViews.CpYellow)) { FillColor = DarkPlate };
		CountLabel.SetRect(1, ValueRect);
	}

	/// <summary><c>+0xa4</c>, the round count's label.</summary>
	public Es1Label CountLabel { get; }

	/// <summary><c>+0xa8</c>–<c>+0xb4</c>, the value rect.</summary>
	public Es1Rect ValueRect { get; }

	/// <summary>Slot <c>+4</c>, <c>AmmoWeaponGauge_OnChildClick</c> (<c>00037784</c>): the rows' shared case for the number button.</summary>
	public override void OnChildClick(short value, Es1Widget child) {
		if (ReferenceEquals(child, SelectButton)) {
			OnSelectClick(value);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>AmmoWeaponGauge_Paint</c> (<c>00037544</c>): the number button's paint; then, when
	/// <c>CockpitView_RectCornerVisible</c> passes the label's rect, the button is not destroyed (state 2) and the
	/// destroyed byte is clear, the round count written (<see cref="WriteCount"/>).
	/// </summary>
	public override void Paint() {
		SelectButton.Paint();
		if (Cockpit.RectCornerVisible(CountLabel.Rect) && SelectButtonShown && !State.Destroyed) {
			WriteCount();
		}
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>AmmoWeaponGauge_Update</c> (<c>000375e4</c>): the number button's per-frame slot; then at a
	/// repaint counter of 2, while <c>CockpitView_RectCornerVisible</c> passes the label's rect, the count written unless
	/// destroyed and the counter lowered, so it stays at 2 while the label is out of sight; at 1 the label's rect copied
	/// from the page on the screen to the page drawn unless destroyed, and the counter lowered.
	/// </summary>
	public override void Update() {
		SelectButton.Tick();
		if (RepaintCounter == 2) {
			if (Cockpit.RectCornerVisible(CountLabel.Rect)) {
				if (!State.Destroyed) {
					WriteCount();
				}

				RepaintCounter--;
			}
		} else if (RepaintCounter == 1) {
			if (!State.Destroyed) {
				Cockpit.DrawToCockpit(CountLabel.Rect, 0);
			}

			RepaintCounter--;
		}
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>AmmoWeaponGauge_SetState</c> (<c>000376b4</c>)(gauge, <paramref name="block"/>): a new round
	/// count stored and the repaint counter set to 2; a new destroyed byte stored with the number button's state 2 for
	/// set and 0 for clear; the three flags stored; the number button invalidated when a byte changed.
	/// </summary>
	public override void SetState(Es1WeaponRowState block) {
		bool bytes = false;
		if (block.Value != State.Value) {
			State = State with { Value = block.Value };
			RepaintCounter = 2;
		}

		if (block.Destroyed != State.Destroyed) {
			State = State with { Destroyed = block.Destroyed };
			SelectButton.State = block.Destroyed ? Es1Widget.Hidden : Es1Widget.Up;
			bytes = true;
		}

		if (State.Armed != block.Armed || State.InChain != block.InChain || State.Ready != block.Ready) {
			State = State with { Armed = block.Armed, InChain = block.InChain, Ready = block.Ready };
			bytes = true;
		}

		if (bytes) {
			SelectButton.Invalidate();
		}
	}

	/// <summary>
	/// The count's draw: the value rect filled with the brush {0, <see cref="Es1WeaponGauge.DarkPlate"/>}
	/// (<c>Raster_FillRect</c>) and <c>ltoa</c>(the round count, 10) written into the label, which fills the same rect again.
	/// </summary>
	private void WriteCount() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(ValueRect.X0, ValueRect.Y0, ValueRect.X1, ValueRect.Y1, DarkPlate);
		CountLabel.SetText(canvas, State.Value.ToString(CultureInfo.InvariantCulture));
	}
}
