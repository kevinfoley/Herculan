using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A mount's turn at the Master Energy Pool — vtable slot <c>0x34</c>, <c>WeaponMount_ChargeCapacitor</c>
/// (<c>0040f00c</c>) for the energy classes and <c>TurboPod_ChargeTick</c> (<c>0040f0d0</c>) for the Turbo
/// Pod — and the two controls that move what it charges toward: the idle wake and the power-level keys.
/// </summary>
public sealed partial class WeaponMount {
	/// <summary>
	/// What an energy mount's capacitor holds at spawn, and the charge level it asks for while idle
	/// — <c>WeaponMount_CtorEnergy</c> (<c>0040e074</c>)'s <c>Q10Multiply(820, 1200)</c>, a literal pair that does not vary by
	/// weapon. Both <c>+0x7b</c> (the target) and <c>+0x7d</c> (the level) start here, so an energy
	/// weapon powers up already charged.
	/// </summary>
	public static readonly short EnergyCapacitorFull = (short)SimMath.Q10Multiply(0x334, EnergyChargeScale);

	/// <summary>
	/// The denominator the charge bar is drawn against — <c>WeaponMount_PushEnergyGaugeState</c> (<c>0040f288</c>) pushes
	/// <c>(charge &lt;&lt; 10) / 1200</c> to a widget whose LED bar has a range of 1024. It is not
	/// the capacitor's own capacity, which is why a fully charged weapon reads four-fifths of a bar
	/// rather than a full one: 960 out of 1200.
	///
	/// <para>What fills the last fifth is the <b>power-level keys</b> — see
	/// <see cref="AdjustPower"/>. <c>WeaponMount_DemandFullCharge</c> (<c>0040f4f0</c>) does the same
	/// thing in one step and was the obvious candidate, but its only caller (<c>WeaponMounts_DemandFullChargeOnArmed_Dead</c> (<c>00410d50</c>),
	/// "raise the armed mount to full and clear everyone else's mid-charge flag") has no reference of
	/// any kind anywhere in the image — neither a call nor a stored address — so nothing in the
	/// retail build ever reaches it.</para>
	/// </summary>
	public const short EnergyChargeScale = 0x4b0;

	/// <summary>How much an energy mount draws per tick when it is the one being served — <c>+0x7f</c>, 20 until its component is past half damage (see <see cref="ConditionChanged"/>).</summary>
	public const short EnergyChargeRate = 0x14;

	/// <summary>
	/// The charge level an idle energy mount asks for — <c>WeaponMount_WakeCapacitor</c> (<c>0040f4d8</c>)'s literal <c>0x334</c>,
	/// the same 820 <see cref="EnergyCapacitorFull"/> is derived from. A mount with a shot demanded
	/// of it raises its target to <see cref="EnergyChargeScale"/> instead.
	/// </summary>
	public const short EnergyIdleTarget = 0x334;

	/// <summary>
	/// What a mount whose turn has passed bleeds back into the pool each tick, once some other mount
	/// has declared itself mid-charge — <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>)'s floor of -5 on a negative deficit.
	/// </summary>
	public const short EnergyBleedBack = 5;

	/// <summary>
	/// Catalog id 25, <c>PLAS</c> — the one weapon <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>) singles out by id. Its
	/// capacitor deficit counts double and only half of what it draws is stored, so it costs twice
	/// the pool for the same charge.
	/// </summary>
	public const int HalfEfficiencyWeaponId = 0x19;

	/// <summary>
	/// The step one press of the power-level keys moves an energy mount's charge target —
	/// <c>WeaponMount_AdjustPowerLevel</c> (<c>0040f48c</c>)'s literal <c>0x50</c>, clamped to 0..<see cref="EnergyChargeScale"/>.
	/// </summary>
	public const short EnergyPowerStep = 0x50;

	/// <summary>
	/// <c>pod+0x81</c>, the Turbo Pod's engaged flag, and the only thing
	/// <see cref="MechObject.TurboSpeedBonus"/> is gated on. Raised by <see cref="EngageTurbo"/> and
	/// dropped either by the row's button going off or by the tank running dry.
	/// </summary>
	public bool TurboEngaged { get; internal set; }

	/// <summary>What <c>TurboPod_Ctor</c> fills the tank to, and the ceiling the pool refills it to.</summary>
	public const int TurboChargeFull = 2000;

	/// <summary>
	/// The charge <c>TurboPod_Engage</c> (<c>0040f09c</c>) demands before it will engage — so a pod
	/// that has just run itself dry cannot be switched straight back on.
	/// </summary>
	public const int TurboEngageCharge = 600;

	/// <summary>What an engaged Turbo Pod spends per tick — <c>TurboPod_ChargeTick</c>'s <c>0x23</c>.</summary>
	public const int TurboSpendRate = 0x23;

	/// <summary>And what it buys back per tick out of the weapons' leftover pool budget.</summary>
	public const short TurboRefillRate = 0x14;

	/// <summary>
	/// <c>TurboPod_Engage</c> (<c>0040f09c</c>): engage if the pod is idle and holds more than
	/// <see cref="TurboEngageCharge"/>. Both the player's row button and the two AI sites that sprint
	/// — the flee behaviour and a long drive under a standing squad order — come through here, which
	/// is why an AI machine engages one at all despite never ticking its pods.
	/// </summary>
	/// <param name="world">Optional, and only so the engage tone has somewhere to go.</param>
	/// <param name="audible">
	/// Whether to sound it. The original gates the sound on the pod having a cockpit gauge
	/// (<c>+0x79</c>), which no AI machine's pod has.
	/// </param>
	/// <returns>Whether the pod engaged just now.</returns>
	internal bool EngageTurbo(SimWorld? world = null, bool audible = false) {
		if (TurboEngaged || Charge <= TurboEngageCharge) {
			return false;
		}

		TurboEngaged = true;
		if (audible) {
			world?.Sounds?.Play(SoundId.Throttle);
		}

		return true;
	}

	/// <summary>Whether the pool arbitration treats this mount as half-efficient — <c>PLAS</c> alone.</summary>
	public bool HalfEfficiency => WeaponId == HalfEfficiencyWeaponId;

	/// <summary>
	/// Vtable slot <c>0x3c</c>, <c>WeaponMount_WakeCapacitor</c> (<c>0040f4d8</c>): put an energy mount's charge target back to its
	/// idle level. Only the energy class implements it — the other two have a no-op in that slot.
	///
	/// <para>It does not set the charge bar's hand-off, so on a mount with a gauge the next
	/// <see cref="PushGaugeState"/> reads the slider back over the idle level.</para>
	/// </summary>
	internal void WakeCapacitor() {
		if (IsEnergyClass && !Disabled) {
			ChargeTarget = EnergyIdleTarget;
		}
	}

	/// <summary>
	/// The priority this mount reports to the arbitration — <c>WeaponMount_GetEnergyPriority</c>
	/// (<c>0040f504</c>) for an energy mount, a flat zero for every other class
	/// (<c>WeaponMount_GetEnergyPriorityZero</c> (<c>004111e2</c>)). A mount already mid-charge reports 10000 and so is always served first,
	/// which is how one weapon finishes charging before another starts.
	/// </summary>
	public short EnergyPriority => Kind switch {
		WeaponMountKind.Energy or WeaponMountKind.Elf => Charging ? (short)10000 : ChargeTarget,
		_ => 0,
	};

	/// <summary>
	/// This mount's turn at the Master Energy Pool — vtable slot <c>0x34</c>. An ammunition mount's
	/// override (<c>WeaponMount_RefireTick</c>, <c>0040ef94</c>) hands the budget straight back; an energy mount runs
	/// <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>):
	///
	/// <list type="number">
	/// <item>The deficit is the mount's target (or zero, once another mount has claimed the tick)
	/// minus its current level, doubled for <c>PLAS</c>.</item>
	/// <item>A positive deficit takes <c>min(charge rate, budget, deficit)</c> — so a mount can be
	/// starved by an empty pool as easily as by its own rate.</item>
	/// <item>A deficit of zero or less clears the mid-charge flag and gives back up to
	/// <see cref="EnergyBleedBack"/> a tick, which is what "targeting zero" means: the capacitor
	/// drains into the pool for someone else to use.</item>
	/// <item>Half of what <c>PLAS</c> draws is thrown away rather than stored.</item>
	/// </list>
	/// </summary>
	/// <param name="budget">What is left of the pool this tick.</param>
	/// <param name="yieldToOther">Whether some earlier mount has already declared itself mid-charge.</param>
	/// <returns>The budget with this mount's draw removed — negative draws put charge back.</returns>
	internal short ChargeTick(short budget, bool yieldToOther) {
		if (Kind == WeaponMountKind.Pod) {
			return WeaponId == MechPods.TurboPodWeaponId ? TurboChargeTick(budget) : budget;
		}

		if (Disabled) {
			return budget;
		}

		// ElfMount_SpinUpAndChargeTick's own half, which runs before it falls through into the
		// energy class's slot below. Only the ELF vtable has it.
		if (Kind == WeaponMountKind.Elf) {
			SpinUpTick();
		}

		// WeaponMount_RefireTick — the refire countdown. It is the whole of an ammunition mount's turn
		// at the pool (that function *is* its vtable slot 0x34) and the first thing the energy class's
		// own slot does, so a mount's cooldown runs on the same pass that charges it and a destroyed
		// mount's does not run at all.
		if (IsEnergyClass || Kind == WeaponMountKind.Ammunition) {
			SimMath.CountdownTimerTick(ref _refireTimer);

			// WeaponMount_AndFlagBlocks (0040f881): +0x33 &= +0x3b, then +0x3b is cleared. Byte 0 is FiringSustained, which
			// the ELF readiness test reads; byte 1 is the charge bar's hand-off, which PushGaugeState reads.
			_firedSinceShuffle &= _firedThisTick;
			_firedThisTick = false;
			_powerToGauge &= _powerToGaugeThisTick;
			_powerToGaugeThisTick = false;

			MuzzleFlashTick();
		}

		if (!IsEnergyClass) {
			AmmoGaugeDecayTick();
			return budget;
		}

		short deficit = (short)((yieldToOther ? 0 : ChargeTarget) - Charge);
		if (HalfEfficiency) {
			deficit *= 2;
		}

		short draw;
		if (deficit < 1) {
			Charging = false;
			draw = Math.Max(deficit, (short)-EnergyBleedBack);
		} else {
			draw = Math.Min(Math.Min(ChargeRate, budget), deficit);
		}

		Charge += draw;
		if (HalfEfficiency) {
			Charge -= draw >> 1;
		}

		return (short)(budget - draw);
	}

	/// <summary>
	/// <c>TurboPod_ChargeTick</c> (<c>0040f0d0</c>) — the Turbo Pod's own <c>+0x34</c> override, and
	/// the only pod turn at the Master Energy Pool that costs anything. Every other pod inherits
	/// <c>WeaponMount_RefireTick</c> there and hands the budget straight back.
	///
	/// <para>Spending and refilling are gated differently: an engaged pod burns
	/// <see cref="TurboSpendRate"/> a tick whether or not the mount is destroyed, and cuts out when
	/// that empties it, but only a live mount buys any back. So shooting the hardpoint a Turbo Pod
	/// sits on leaves the pilot whatever is in the tank and no more.</para>
	/// </summary>
	private short TurboChargeTick(short budget) {
		if (TurboEngaged) {
			Charge -= TurboSpendRate;
			if (Charge < 1) {
				TurboEngaged = false;
				Charge = 0;
			}
		}

		if (Disabled) {
			return budget;
		}

		short deficit = (short)(TurboChargeFull - Charge);
		if (deficit < 1) {
			return budget;
		}

		short draw = Math.Min(Math.Min(TurboRefillRate, budget), deficit);
		Charge += draw;
		return (short)(budget - draw);
	}

	/// <summary>
	/// Vtable slot <c>0x38</c>, <c>WeaponMount_AdjustPowerLevel</c> (<c>0040f48c</c>) — the power-level control, which the manual does not
	/// mention, on
	/// <c>[-]</c>/<c>[=]</c> and the numeric keypad's <c>[-]</c>/<c>[+]</c>. Moves this mount's charge
	/// target by <see cref="EnergyPowerStep"/>, clamped to zero and <see cref="EnergyChargeScale"/>.
	/// Only the energy class implements it; the other two have a no-op in that slot.
	///
	/// <para>What it changes depends on which shape the weapon's threshold pair has — see
	/// <see cref="ShotCost"/>. A laser is unaffected in everything but its bar: its threshold and its
	/// cost are both fixed. A charge-up weapon's target <i>is</i> its shot strength, and turning it
	/// down is what makes one fire sooner and hit softer.</para>
	///
	/// <para>The new target goes out to the charge bar on the next <see cref="PushGaugeState"/> and
	/// comes back from it a pool turn later, up to two units low.</para>
	/// </summary>
	/// <param name="raise">True for the two "up" keys.</param>
	internal void AdjustPower(bool raise) {
		if (!IsEnergyClass) {
			return;
		}

		ChargeTarget += raise ? EnergyPowerStep : (short)-EnergyPowerStep;
		ChargeTarget = Math.Clamp(ChargeTarget, (short)0, EnergyChargeScale);
		_powerToGauge = true;
		_powerToGaugeThisTick = true;
	}
}
