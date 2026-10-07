using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The mount's side of its cockpit weapon row — the gauge-state pushes (<c>WeaponMount_PushEnergyGaugeState</c>,
/// <c>0040f288</c>; <c>WeaponMount_PushAmmoGaugeState</c>, <c>0040f330</c>), the energy class's charge-bar
/// slider, and the pod row's button.
/// </summary>
public sealed partial class WeaponMount {
	/// <summary>
	/// <c>+0x34</c> and <c>+0x3c</c>, byte 1 of the <c>+0x33</c> and <c>+0x3b</c> flag blocks — which
	/// way <see cref="PushGaugeState"/> moves the power level. The pool turn shuffles them as it does
	/// <see cref="FiringSustained"/>'s byte 0, so a write that sets both holds for one turn.
	/// </summary>
	private bool _powerToGauge;

	private bool _powerToGaugeThisTick;

	/// <summary>The charge bar's slider range, <c>0..0x400</c> — the same 0-1024 the LED bar it carries reads in.</summary>
	public const int ChargeBarRange = 0x400;

	/// <summary>
	/// The charge bar's slider position, gauge <c>+0xc6</c>, 0..<see cref="ChargeBarRange"/>,
	/// or null for a mount with no energy gauge. It lives on the gauge in the original; it is kept here
	/// for the reason <see cref="PodButton"/> is.
	/// </summary>
	private int? _chargeBarPosition;

	/// <summary>
	/// The on/off button on this mount's cockpit row — the pod gauge's own byte at <c>gauge+0xc2</c>,
	/// which <c>TogglePodGauge_OnClick</c> (<c>004419fc</c>) XORs when the row is pressed.
	///
	/// <para><b>It lives on the gauge in the original, not on the mount</b>, and the pod's tick copies
	/// it across each frame. It is kept here because the cockpit's rows are rebuilt from sim state
	/// every frame rather than being objects that persist, so the mount is the only thing on the
	/// press's side of the frame that outlives it. Only the two pods with a
	/// <see cref="HasPodButton"/> gauge class ever have it moved.</para>
	/// </summary>
	public bool PodButton { get; internal set; }

	/// <summary>
	/// Whether this row's press has anything to toggle: <c>CockpitView_CreatePodGauge</c>
	/// (<c>004321d4</c>) builds a <c>TogglePodGauge</c> for the ECM pod and a <c>TurboPodGauge</c>
	/// derived from it for the Turbo pod, and the plain <c>PodGauge</c> — whose click sets the repaint
	/// byte and returns — for the other three. See docs/retail/simulation/equipment-pods.md.
	/// </summary>
	public bool HasPodButton => Kind == WeaponMountKind.Pod
		&& WeaponId is MechPods.EcmWeaponId or MechPods.TurboPodWeaponId;

	/// <summary>
	/// The range the Turbo row's LED bar is built with — <c>TurboPodGauge_Ctor</c>'s literal
	/// <c>0x9c4</c>. It is larger than the tank, so a full pod fills four-fifths of its bar, the same
	/// way an energy weapon's does.
	/// </summary>
	public const int TurboMeterRange = 0x9c4;

	/// <summary>Rounds remaining, as the ammunition gauge prints them — <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>)'s <c>+0x7d &gt;&gt; 8</c>.</summary>
	public int Rounds => Charge >> 8;

	/// <summary>
	/// The charge bar's value, over the 0-1024 range its LED bar was built with —
	/// <c>WeaponMount_PushEnergyGaugeState</c> (<c>0040f288</c>)'s <c>(charge &lt;&lt; 10) / 1200</c>.
	/// </summary>
	public int ChargeMeterValue => (Charge << 10) / EnergyChargeScale;

	/// <summary>
	/// What the ammunition gauge's own printed count does between the shot and the next: the mount
	/// keeps two figures, the true round count at <c>+0x7b</c> which a shot drops instantly, and a
	/// display figure at <c>+0x7d</c> in 256ths which chases it down at
	/// <see cref="AmmoGaugeDecayRate"/> a tick. That is what makes the cockpit's round counter roll
	/// rather than jump, and it is why an ammunition mount's two "charge" fields disagree for a
	/// moment after every shot.
	///
	/// <para><b>Moved, deliberately.</b> The original does this inside
	/// <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>), the gauge-state push, which runs per
	/// frame and only for the machine whose cockpit is on screen. It is per-tick state driven by
	/// <see cref="SimMath.IntegrateRateOverTick"/>, so it belongs on the tick; the visible result for
	/// the piloted machine is the same, and an AI machine's unread display figure now decays too.</para>
	/// </summary>
	private void AmmoGaugeDecayTick() {
		if (Kind != WeaponMountKind.Ammunition) {
			return;
		}

		int floor = ChargeTarget << 8;
		if (floor < Charge) {
			Charge -= (short)SimMath.IntegrateRateOverTick(AmmoGaugeDecayRate);
		}

		if (Charge < floor) {
			Charge = floor;
		}
	}

	/// <summary>How fast the printed round count chases the real one — <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>)'s literal 250 per 125 ms.</summary>
	public const short AmmoGaugeDecayRate = 0xfa;

	/// <summary>
	/// <c>WeaponMount_CreateEnergyGauge</c> (<c>0040e0e0</c>), energy and ELF vtable <c>+0x64</c>: the
	/// charge bar's slider is seeded with the raw charge target, through
	/// <c>SliderWidget_SetValueH</c>'s clamp. Every other class's gauge has no slider.
	/// </summary>
	internal void BuildGauge() {
		if (IsEnergyClass) {
			_chargeBarPosition = Math.Clamp((int)ChargeTarget, 0, ChargeBarRange);
		}
	}

	/// <summary>
	/// The power-level half of <c>WeaponMount_PushEnergyGaugeState</c> (<c>0040f288</c>), energy and
	/// ELF vtable <c>+0x50</c>, on a mount with a gauge. With the hand-off set — for one pool turn
	/// after the constructor or <see cref="AdjustPower"/> — the charge target goes out to the slider
	/// as <c>(target &lt;&lt; 10) / 1200</c>; otherwise the slider comes back as the charge target,
	/// <see cref="ChargeTargetForBarPosition"/>. The round trip loses up to two units, so a power level
	/// settles just under what set it: 960 goes out as 819 and comes back as 959. See
	/// docs/retail/simulation/weapon-firing.md#the-charge-bar.
	///
	/// <para>The rest of the push — the bar's fill and the row's flags — is display state the cockpit
	/// rows read straight off the mount (<see cref="ChargeMeterValue"/>).</para>
	/// </summary>
	internal void PushGaugeState() {
		if (_chargeBarPosition is not { } position) {
			return;
		}

		if (_powerToGauge) {
			// EnergyWeaponGauge_SetState hands a changed position to SliderWidget_SetValueH, which clamps it
			// and commits it back through EnergyWeaponGauge_OnChildClick into the same field.
			_chargeBarPosition = Math.Clamp(((int)ChargeTarget << 10) / EnergyChargeScale,
				0, ChargeBarRange);
		} else {
			ChargeTarget = ChargeTargetForBarPosition(position);
		}
	}

	/// <summary>
	/// The charge bar's slider committed at <paramref name="position"/>, under
	/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/>. Retail's path, which no press
	/// reaches there: <c>EnergyWeaponGauge_OnChildClick</c> (<c>00440ef0</c>) clamps the position to
	/// 0..<see cref="ChargeBarRange"/> into the gauge's state block, and the next
	/// <see cref="PushGaugeState"/> with the hand-off clear reads it back as the charge target. A key
	/// press whose hand-off is still pending wins over it; docs/retail/simulation/weapon-firing.md#the-charge-bar.
	/// </summary>
	/// <returns>Whether this mount has a charge bar to take it — an energy or ELF mount still working.</returns>
	internal bool SetPowerFromChargeBar(int position) {
		if (!IsEnergyClass || Disabled || _chargeBarPosition == null) {
			return false;
		}

		_chargeBarPosition = Math.Clamp(position, 0, ChargeBarRange);
		return true;
	}

	/// <summary>
	/// The charge target a charge-bar position reads back as: clamped to the slider's range as
	/// <c>EnergyWeaponGauge_OnChildClick</c> clamps it, then <c>position * 1200 &gt;&gt; 10</c>.
	/// </summary>
	internal static short ChargeTargetForBarPosition(int position) =>
		(short)(Math.Clamp(position, 0, ChargeBarRange) * EnergyChargeScale >> 10);
}
