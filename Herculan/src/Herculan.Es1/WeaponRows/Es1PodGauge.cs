using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// The ECM's row, <c>PodGauge</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-pod-row): a number button and the word
/// <c>ECM</c>, which a click on the number or the row's number key switches on and off, the word's plate changing color
/// as it does; on <c>PodGaugeVtable</c> (<c>00093ace</c>), whose set-state slot <c>+0x14</c> does nothing.
/// </summary>
public sealed class Es1PodGauge : Es1WeaponGauge {
	/// <summary>The name's plate while the switch is on.</summary>
	public const byte SwitchedOnPlate = 0x38;

	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly string? _offline;

	/// <summary>
	/// <c>PodGauge_Ctor</c> (<c>00037810</c>)(0, <paramref name="mount"/>, <paramref name="row"/>,
	/// <paramref name="cockpit"/>, block), the block the pod block at 340 with the row's rect <paramref name="rect"/> in
	/// its first 16 bytes: with (<i>x</i>, <i>y</i>) the row's top-left, child 0 the plain number button on the block's
	/// rect at <c>+0x10</c> moved by it and registered on the cockpit's root; the name the first 12 bytes of
	/// <paramref name="ecm"/>, whatever the mount; the label's rect the block's rect at <c>+0x20</c> moved by it, and the
	/// label in <c>cpred</c> centered over it with the fill color <see cref="Es1WeaponGauge.DarkPlate"/>; and the state
	/// block cleared.
	/// </summary>
	/// <param name="ecm"><c>STRINGS0.STR</c> group 3's one string, <c>ECM</c> (<c>000bd2c8</c>).</param>
	/// <param name="offline"><c>STRINGS0.STR</c> group 2's <c>OFFLINE</c> (<c>000bd2c4</c>).</param>
	internal Es1PodGauge(Es1Cockpit cockpit, Es1WeaponMount mount, short row, Es1Rect rect, Es1GauPodRowBlock block,
			IReadOnlyList<HudFont?> fonts, IReadOnlyList<DynamixBitmap>? pushButtons, string? ecm, string? offline)
		: base(cockpit, mount, rect) {
		_fonts = fonts;
		_offline = offline;
		SelectButton = new Es1WeaponSelectButton(this, row, Es1Rect.From(block.SelectButton).Offset(rect.X0, rect.Y0), fonts,
			pushButtons);
		cockpit.Root.RegisterClickable(SelectButton);
		Name = Truncate(ecm);
		NameLabel = new Es1Label(Font(fonts, Es1CockpitViews.CpRed)) { FillColor = DarkPlate };
		NameLabel.SetRect(Es1Label.Centered, Es1Rect.From(block.Label).Offset(rect.X0, rect.Y0));
	}

	/// <summary><c>+0x14</c>, child 0, the number button.</summary>
	public Es1WeaponSelectButton SelectButton { get; }

	/// <summary><c>+0x8d</c>, the name's label, its rect <c>+0x91</c>.</summary>
	public Es1Label NameLabel { get; }

	/// <summary><c>+0x8b</c>, the state block.</summary>
	public Es1PodRowState State { get; private set; }

	/// <summary><c>PodGauge_GetStateBlock</c> (<c>00037b50</c>): a copy of the state block.</summary>
	public Es1PodRowState GetStateBlock() => State;

	/// <summary>
	/// <c>PodGauge_SetStateBlock</c> (<c>00037b14</c>)(gauge, <paramref name="block"/>): when either byte differs, both
	/// copied and the repaint counter set to 2.
	/// </summary>
	public void SetStateBlock(Es1PodRowState block) {
		if (block != State) {
			State = block;
			RepaintCounter = 2;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>PodGauge_OnChildClick</c> (<c>00037b5c</c>)(gauge, value, <paramref name="child"/>): for the
	/// number button, whatever the value, the switch flipped and the repaint counter set to 2.
	/// </summary>
	public override void OnChildClick(short value, Es1Widget child) {
		if (ReferenceEquals(child, SelectButton)) {
			State = State with { Switch = !State.Switch };
			RepaintCounter = 2;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>PodGauge_Paint</c> (<c>0003799c</c>): the number button's paint; then, when
	/// <c>CockpitView_RectCornerVisible</c> passes the label's rect and the button is not destroyed (state 2), the label
	/// written in <c>cpylw</c>: with the destroyed byte clear, the name on a plate of <see cref="SwitchedOnPlate"/> while
	/// the switch is on and <see cref="Es1WeaponGauge.DarkPlate"/> while it is off; with it set, <c>OFFLINE</c> on
	/// <see cref="Es1WeaponGauge.DarkPlate"/>, the label centered again from the button's top-left to its own far corner,
	/// and the button's state 2.
	/// </summary>
	public override void Paint() {
		SelectButton.Paint();
		if (!Cockpit.RectCornerVisible(NameLabel.Rect) || SelectButton.State == Es1Widget.Hidden) {
			return;
		}

		string? text = Name;
		byte plate;
		if (!State.Destroyed) {
			plate = State.Switch ? SwitchedOnPlate : DarkPlate;
		} else {
			plate = DarkPlate;
			text = _offline;
			var button = SelectButton.Rect;
			NameLabel.SetRect(Es1Label.Centered, NameLabel.Rect with { X0 = button.X0, Y0 = button.Y0 });
			SelectButton.State = Es1Widget.Hidden;
		}

		NameLabel.Font = Font(_fonts, Es1CockpitViews.CpYellow);
		NameLabel.FillColor = plate;
		NameLabel.SetText(Cockpit.PushCanvasContext(), text);
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>PodGauge_Update</c> (<c>00037abc</c>): at a repaint counter of 2 the paint, at 1 the label's
	/// rect copied from the page on the screen to the page drawn, each lowering it by 1; at 0 the number button's
	/// per-frame slot.
	/// </summary>
	public override void Update() {
		if (RepaintCounter == 2) {
			Paint();
			RepaintCounter--;
		} else if (RepaintCounter == 1) {
			Cockpit.DrawToCockpit(NameLabel.Rect, 0);
			RepaintCounter--;
		} else if (RepaintCounter == 0) {
			SelectButton.Tick();
		}
	}
}
