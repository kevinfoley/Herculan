using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// A HERC's front/rear shield charge — DBSIM's five-<c>short</c> block at <c>mech+0x222</c>, and the
/// Master Energy Pool's only sink outside the weapon mounts.
///
/// <para><b>There is one pool, not two.</b> <see cref="Max"/> caps <c>Front + Rear</c>, and
/// <see cref="Balance"/> decides how that one total is split; moving the balance moves charge across
/// rather than creating any. That is why the manual can only say power is "redistributed", never
/// added.</para>
///
/// <para>Both absorb paths are ported, and they are separate code in the original as they are here
/// — direct fire (<c>Mech_ShieldAbsorb_DirectFire</c>, <c>00413cc4</c>) as <see cref="AbsorbDirectFire"/> and the explosion one
/// (<c>Mech_ShieldAbsorb_Explosive</c>, <c>00413c68</c>) as <see cref="AbsorbExplosion"/>. Otherwise this type carries the charge,
/// the recharge and the balance, which is what the energy pool tick touches.</para>
///
/// <para>The <c>+0x222</c> field layout, the fleet-wide 3500 capacity and where it is read from, the
/// loadout-time capacity formula and the cockpit readouts' "always sums to 200" trap are all in
/// docs/retail/simulation/damage-system.md, "The shield system". The members below name the constants that
/// document derives.</para>
/// </summary>
public sealed class ShieldCharge {
	/// <summary>
	/// The most a recharge tick may draw from the energy pool, whatever the pool can spare —
	/// <c>Shield_RechargeTick</c>'s own <c>if (4 &lt; request) request = 5</c>. This one constant is
	/// the whole of the manual's "replenishes at a steady rate from the Master Energy Pool": at a
	/// 3500 capacity a shield array rebuilt from empty takes 700 ticks.
	/// </summary>
	public const short RechargePerTick = 5;

	/// <summary>
	/// How far the front facing may slew toward its balance target in one tick. The rear is not
	/// slewed — it takes whatever the new total leaves over — so this is also the rate charge
	/// crosses between facings when the pilot moves the balance.
	/// </summary>
	public const short BalanceSlewPerTick = 0x41;

	/// <summary>
	/// The slew step used instead when the array is over-full (a negative deficit), which is
	/// effectively "snap": far larger than any real capacity, so the front reaches its target in one
	/// tick. Only reachable by <see cref="Max"/> dropping under the charge already held.
	/// </summary>
	public const short OverfullSlewPerTick = 10000;

	/// <summary>The balance value the array powers up at — an even split.</summary>
	public const short BalanceCenter = 0x200;

	/// <summary>The balance range's upper end; 0 is all rear, this is all front.</summary>
	public const short BalanceMax = 0x400;

	/// <summary>
	/// One press of the balance keys, <c>Shield_BalanceAdjust</c>'s <c>±0x66</c> — just short of a
	/// tenth of the range, which is why a forward press from centre reads 119/81. See
	/// docs/retail/formats/cockpit-hud-widgets.md, "Readouts".
	/// </summary>
	public const short BalanceStep = 0x66;

	private short _front;

	/// <summary><c>Shield_Init</c> (<c>00413a90</c>): full, evenly split, at the type's own capacity.</summary>
	public ShieldCharge(short capacity) {
		Max = capacity;
		BaseMax = capacity;
		Balance = BalanceCenter;
		_front = (short)(capacity >> 1);
		Rear = (short)(capacity >> 1);
	}

	/// <summary>Charge held on the front facing (<c>+0x222</c>).</summary>
	public short Front => _front;

	/// <summary>Charge held on the rear facing (<c>+0x224</c>).</summary>
	public short Rear { get; private set; }

	/// <summary>
	/// The front's intended share of the total, Q10 over <c>0..<see cref="BalanceMax"/></c>
	/// (<c>+0x226</c>). The recharge tick reads it every tick, which is why nudging it moves charge
	/// even when the array is already full.
	/// </summary>
	public short Balance { get; private set; } = BalanceCenter;

	/// <summary>
	/// What <see cref="Front"/> plus <see cref="Rear"/> may add up to (<c>+0x228</c>) — one pool
	/// across both facings, raised by a Shield Pod.
	/// </summary>
	public short Max { get; private set; }

	/// <summary>
	/// The type's capacity before any pod (<c>+0x22a</c>). <c>Shield_Init</c> sets it alongside
	/// <see cref="Max"/> and nothing writes it again, and the cockpit readouts divide by <i>this</i>
	/// rather than by <see cref="Max"/> — which is what makes a Shield Pod read as 200/200 instead of
	/// silently renormalising back to 100/100.
	/// </summary>
	public short BaseMax { get; }

	/// <summary>The charge currently held across both facings.</summary>
	public short Total => (short)(_front + Rear);

	/// <summary>
	/// The two numbers the cockpit prints — <b>the balance, not the charge</b>.
	///
	/// <para><c>ShieldsGauge_UpdateReadouts</c> (<c>00444a68</c>) reads the gauge's balance field and
	/// prints <c>balance * 200 &gt;&gt; 10</c> for the front and the literal <i>complement</i>
	/// <c>200 - that</c> for the rear. The pair therefore always sums to 200 by construction, whatever
	/// charge the array is actually holding, and an empty array still reads 100/100 at rest. The
	/// charge is shown by the meter's rings instead (see
	/// <c>CockpitPalette.ShieldFacingCharge</c>), which is why the two halves of the widget can
	/// disagree so completely — dark rings over a confident "100".</para>
	///
	/// <para><paramref name="rounded"/> is <b>not retail</b> — it is the
	/// <see cref="Settings.TweakSettingDefinitions.RoundShieldBalance"/> tweak, which snaps the
	/// front number to the nearest multiple of <see cref="ReadoutStep"/>, so it reads 120/80, 140/60 …
	/// where retail reads 119/81, 139/61 … <see cref="AdjustBalance"/> is the only thing that moves
	/// <see cref="Balance"/>, and its steps fall 0.08 of a point short of 20 each. Rounding to the
	/// nearest point would not be enough: a run of ten steps back from either clamped end drifts 0.78
	/// of a point, so 310 would read 61. Snapping has ten points of margin.</para>
	/// </summary>
	public (int Front, int Rear) Readout(bool rounded) {
		int front = rounded
			? ((Balance * (ReadoutTotal / ReadoutStep) + (BalanceMax >> 1)) >> 10) * ReadoutStep
			: SimMath.Q10Multiply(Balance, ReadoutTotal);
		return (front, ReadoutTotal - front);
	}

	/// <summary>What the two printed numbers always add up to.</summary>
	private const int ReadoutTotal = 200;

	/// <summary>What one press is meant to move the readout by; <see cref="BalanceStep"/> falls just short of it.</summary>
	private const int ReadoutStep = 20;

	/// <summary>
	/// Sets the array's total capacity — <c>Shield_SetMax</c> (<c>00413ab8</c>), the setter
	/// <see cref="MechObject.ShieldCapacity"/>'s result is pushed through at loadout time. Charge
	/// already held is left alone; <see cref="RefillToBalance"/> is what follows it in the original.
	/// </summary>
	public void SetMax(short capacity) => Max = capacity;

	/// <summary>
	/// <c>Shield_RefillToBalance</c> (<c>00413ac8</c>) — fills the array to <see cref="Max"/> and splits it at the current
	/// balance. <c>Mech_ConfigureLoadout</c> calls it right after <see cref="SetMax"/>, so a machine
	/// spawns with full shields at whatever capacity its pods bought it.
	/// </summary>
	public void RefillToBalance() {
		_front = (short)SimMath.Q10Multiply(Balance, Max);
		Rear = (short)(Max - _front);
	}

	/// <summary>
	/// <c>Shield_RechargeTick</c> (<c>00413b38</c>) — the recharge primitive, run once per tick from
	/// the energy pool's own tick with whatever the weapon mounts left unclaimed.
	///
	/// <para>The request is clamped twice: to <see cref="RechargePerTick"/>, and to the deficit, so
	/// the array never overcharges. The granted amount goes into the <i>total</i>; the front is then
	/// slewed toward its balance share at <see cref="BalanceSlewPerTick"/> and the rear takes the
	/// remainder. The slew is why the two facings drift toward the balance over several ticks rather
	/// than snapping, and it runs whether or not anything was granted — a balance nudge on a full
	/// array still moves charge across.</para>
	/// </summary>
	/// <param name="request">Energy on offer this tick, in pool units.</param>
	/// <returns>The unclaimed part of <paramref name="request"/>, handed back to the pool.</returns>
	public short RechargeTick(short request) {
		short deficit = (short)(Max - (_front + Rear));

		// Both per-tick constants go through ScalePerTickStep for the reason its own summary gives.
		// It matters more here than elsewhere: the reactor's income is a *rate*, integrated against
		// the timestep, so leaving the draw as a fixed amount per tick would make the balance between
		// income and demand depend on how fast the engine ticks. Exact, and a no-op, at the vanilla
		// timestep the engine runs.
		short cap = SimMath.ScalePerTickStep(RechargePerTick);

		short granted = request;
		if (granted > cap) {
			granted = cap;
		}
		if (deficit < granted) {
			granted = deficit;
		}

		short newTotal = (short)(_front + Rear + granted);
		short frontTarget = (short)SimMath.Q10Multiply(Balance, newTotal);
		short step = deficit < 0
			? OverfullSlewPerTick
			: SimMath.ScalePerTickStep(BalanceSlewPerTick);

		SimMath.RateLimitedMoveToward(ref _front, frontTarget, step);
		Rear = (short)(newTotal - _front);

		return (short)(request - granted);
	}

	/// <summary>
	/// <b>Not a ported mechanic — a test seam.</b> Empties both facings, leaving <see cref="Max"/>
	/// alone so the whole capacity reads as deficit. The host's debug panel is the only caller: it
	/// makes the refill observable on demand rather than waiting for combat to drain a facing. From
	/// empty the array rebuilds at <see cref="RechargePerTick"/>.
	/// </summary>
	public void Empty() {
		_front = 0;
		Rear = 0;
	}

	/// <summary>
	/// The absorption half of <c>Mech_ShieldAbsorb_DirectFire</c> (<c>00413cc4</c>) — everything that
	/// function does once its geometry has picked a facing.
	///
	/// <para>It is a <b>hard cap, not a threshold</b>: <c>absorbed = min(damage, charge in that
	/// zone)</c>, and both are reduced by it. A hit worth more than the facing holds drains that
	/// facing to zero and carries its excess through to armour in the same hit — the zone does not
	/// have to already be empty for anything to get past it.</para>
	///
	/// <para>Only the struck facing is touched. <see cref="Balance"/> is not consulted and the other
	/// facing is not drawn on, so a machine with everything on its front takes rear hits on nothing
	/// at all.</para>
	/// </summary>
	/// <param name="front">Which facing the hit's bearing selected.</param>
	/// <param name="damage">
	/// The shot's shield damage on the way in, and what is left of it on the way out — zero if the
	/// facing swallowed the whole shot.
	/// </param>
	/// <returns>What the facing absorbed.</returns>
	public short AbsorbDirectFire(bool front, ref short damage) {
		short charge = front ? _front : Rear;
		short absorbed = Math.Min(damage, charge);
		if (absorbed == 0) {
			return 0;
		}

		damage -= absorbed;
		if (front) {
			_front -= absorbed;
		} else {
			Rear -= absorbed;
		}

		return absorbed;
	}

	/// <summary>
	/// <c>Mech_ShieldAbsorb_Explosive</c> (<c>00413c68</c>) — the explosion path's own absorption,
	/// which is a <b>second implementation of the same idea</b> rather than a shared subroutine: the
	/// original writes the hard cap twice, once per damage pathway, and the two do not scale their
	/// input the same way.
	///
	/// <para><b>The two scales are exact inverses.</b> The blast is scaled into charge units by
	/// <c>1000/1024</c> on the way in and the leftover is scaled back by <c>1024/1000</c> on the way
	/// out, so a blast's overflow is its own face value to within a rounding step — the same exchange
	/// rate direct fire has. See docs/retail/simulation/damage-system.md, "Explosive damage".</para>
	///
	/// <para>Unlike <see cref="AbsorbDirectFire"/> nothing here is returned by reference: the caller
	/// wants only what got through, and what the facing absorbed is implied.</para>
	/// </summary>
	/// <param name="front">Which facing the blast's bearing selected.</param>
	/// <param name="damage">The blast's damage figure, before scaling.</param>
	/// <returns>What carried through to structure, or zero for a blast the facing swallowed whole.</returns>
	public short AbsorbExplosion(bool front, short damage) {
		int scaled = SimMath.Q10Multiply(ExplosionDamageScale, damage);
		int charge = (front ? _front : Rear) - scaled;

		if (front) {
			_front = (short)Math.Max(charge, 0);
		} else {
			Rear = (short)Math.Max(charge, 0);
		}

		return charge >= 0
			? (short)0
			: (short)(-charge * ExplosionOverflowScale / ExplosionDamageScale);
	}

	/// <summary>
	/// What a blast's face value is multiplied by to reach charge units — through
	/// <see cref="SimMath.Q10Multiply"/>, so the ratio is <c>1000/1024</c>.
	/// </summary>
	private const int ExplosionDamageScale = 1000;

	/// <summary>
	/// And what the leftover is multiplied by to come back out, over
	/// <see cref="ExplosionDamageScale"/> — <c>1024/1000</c>, the exact inverse.
	/// </summary>
	private const int ExplosionOverflowScale = 0x400;

	/// <summary>
	/// <c>Shield_BalanceAdjust</c> (<c>00413af8</c>) — one press of the manual's <c>[</c> (rear) and
	/// <c>]</c> (forward) keys, or a click on the corresponding half of the cockpit's shield gauge.
	/// Nudges <see cref="Balance"/> by <see cref="BalanceStep"/>, clamped to the range; the charge
	/// itself does not move until the next <see cref="RechargeTick"/>.
	/// </summary>
	/// <param name="towardFront">
	/// True for <c>]</c>. The original passes a direction of 1 for the gauge's first flag byte and 0
	/// for its second, and 1 is the <c>+0x66</c> case — balance is the <i>front's</i> share, so
	/// raising it is forward.
	/// </param>
	public void AdjustBalance(bool towardFront) {
		int balance = Balance + (towardFront ? BalanceStep : -BalanceStep);
		Balance = (short)Math.Clamp(balance, 0, BalanceMax);
	}
}
