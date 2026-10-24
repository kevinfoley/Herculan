using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// An energy weapon's row, <c>EnergyWeaponGauge</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-energy-row): the
/// number button with the name and state box, and the charge bar <c>+0x18</c>, on <c>EnergyWeaponGaugeVtable</c>
/// (<c>00093a8e</c>).
/// </summary>
public sealed class Es1EnergyWeaponGauge : Es1WeaponRowGauge {
	/// <summary>The largest value the charge bar's click stores at <c>+0x8f</c>.</summary>
	private const int FullPower = 0x400;

	/// <summary>
	/// <c>EnergyWeaponGauge_Ctor</c> (<c>0003700c</c>)(0, <paramref name="mount"/>, <paramref name="name"/>,
	/// <paramref name="row"/>, <paramref name="cockpit"/>, block), the block the energy block at 244 with the row's rect
	/// <paramref name="rect"/> in its first 16 bytes: with (<i>x</i>, <i>y</i>) the row's top-left, child 0 the chained
	/// number button on the block's rect at <c>+0x10</c> moved by it and registered on the cockpit's root; the name's
	/// first 12 bytes; child 1 the charge bar on the block's rect at <c>+0x20</c> moved by it, given the block's dword
	/// <c>+0x3c</c>, and registered; and the state block cleared.
	/// </summary>
	internal Es1EnergyWeaponGauge(Es1Cockpit cockpit, Es1WeaponMount mount, string? name, short row, Es1Rect rect,
			Es1GauEnergyRowBlock block, IReadOnlyList<HudFont?> fonts, IReadOnlyList<DynamixBitmap>? pushButtons,
			string? offline)
		: base(cockpit, mount, row, rect) {
		SelectButton = new Es1ChainedWeaponSelectButton(this, row, Es1Rect.From(block.SelectButton).Offset(rect.X0, rect.Y0),
			fonts, pushButtons, offline);
		cockpit.Root.RegisterClickable(SelectButton);
		Name = Truncate(name);
		ChargeBar = new Es1ChargeBar(block.ChargeBarArgument, this, Es1Rect.From(block.ChargeBar).Offset(rect.X0, rect.Y0),
			pushButtons);
		cockpit.Root.RegisterClickable(ChargeBar);
	}

	/// <summary><c>+0x18</c>, child 1, the charge bar.</summary>
	public Es1ChargeBar ChargeBar { get; }

	/// <summary><c>+0x8a</c>, set while a destroyed byte's change waits for the hit shake to end.</summary>
	public bool DestroyedHeld { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>EnergyWeaponGauge_OnChildClick</c> (<c>000372e4</c>)(gauge, <paramref name="value"/>,
	/// <paramref name="child"/>): for the number button the rows' shared case; for the charge bar the value, held to 0 …
	/// <c>0x400</c>, stored as the state block's <see cref="Es1WeaponRowState.Power"/>.
	/// </summary>
	public override void OnChildClick(short value, Es1Widget child) {
		if (ReferenceEquals(child, SelectButton)) {
			OnSelectClick(value);
		} else if (ReferenceEquals(child, ChargeBar)) {
			int power = value > FullPower - 1 ? FullPower : value < 1 ? 0 : value;
			State = State with { Power = power };
		}
	}

	/// <summary>Slot <c>+8</c>, <c>EnergyWeaponGauge_Paint</c> (<c>00037144</c>): each child's paint.</summary>
	public override void Paint() {
		SelectButton.Paint();
		ChargeBar.Paint();
	}

	/// <summary>Slot <c>+0xc</c>, <c>EnergyWeaponGauge_Update</c> (<c>0003716c</c>): each child's per-frame slot.</summary>
	public override void Update() {
		SelectButton.Tick();
		ChargeBar.Tick();
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>EnergyWeaponGauge_SetState</c> (<c>00037194</c>)(gauge, <paramref name="block"/>): a new
	/// <see cref="Es1WeaponRowState.Power"/> stored and handed to the charge bar's set-value, which moves the knob; a new
	/// <see cref="Es1WeaponRowState.Value"/> stored and made the bar's value; a new destroyed byte, or one held back,
	/// stored with both children's states 2 for set and 0 for clear, unless the hit shake is under way, when it is held
	/// back through <see cref="DestroyedHeld"/>; the three flags stored. The number button is invalidated when a byte
	/// changed and the charge bar when a value did.
	/// </summary>
	public override void SetState(Es1WeaponRowState block) {
		bool bytes = false;
		bool power = block.Power != State.Power;
		if (power) {
			State = State with { Power = block.Power };
			ChargeBar.SetValue((short)block.Power);
		}

		bool value = block.Value != State.Value;
		if (value) {
			State = State with { Value = block.Value };
			ChargeBar.Value = block.Value;
		}

		if (block.Destroyed != State.Destroyed || DestroyedHeld) {
			// CockpitView_GetShakeOffset (000249d4)'s +4 is 0 while no hit shake is armed, and the hit shake is plan step 7,
			// so the change always lands.
			State = State with { Destroyed = block.Destroyed };
			short shown = block.Destroyed ? Es1Widget.Hidden : Es1Widget.Up;
			SelectButton.State = shown;
			ChargeBar.State = shown;
			bytes = true;
			DestroyedHeld = false;
		}

		if (State.Armed != block.Armed || State.InChain != block.InChain || State.Ready != block.Ready) {
			State = State with { Armed = block.Armed, InChain = block.InChain, Ready = block.Ready };
			bytes = true;
		}

		if (bytes) {
			SelectButton.Invalidate();
		}

		if (power || value) {
			ChargeBar.Invalidate();
		}
	}
}
