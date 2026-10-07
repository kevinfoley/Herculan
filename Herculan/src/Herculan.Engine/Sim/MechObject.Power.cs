using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The reactor and the Master Energy Pool — <c>Mech_PerTickSystemsUpdate</c>'s (<c>0041aa5c</c>)
/// opening five statements, which are the whole of DBSIM's power model.
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// Reactor output for an undamaged HERC with no Energy Pod, in pool units per unit time —
	/// <c>Mech_ComputeReactorRate</c>'s bare <c>0x14</c>. It is a literal, not a per-type stat: every
	/// machine in the fleet, from the STINGRAY to the APOCALYPSE, runs the same reactor.
	/// </summary>
	public const short BaseReactorOutputRate = 0x14;

	/// <summary>
	/// The pool's ceiling, and the value a HERC powers up holding — <c>Mech_Constructor</c> writes
	/// 10000 to <c>mech+0x292</c> and the tick clamps back to it. Also the denominator the cockpit
	/// meter's fill fraction is taken against.
	/// </summary>
	public const short EnergyPoolMax = 10000;

	/// <summary>
	/// The slice of the pool nothing may spend. The tick offers consumers <c>pool - 500</c> and puts
	/// <c>leftover + 500</c> back, so 500 units are held out of every arbitration and the pool
	/// settles at that floor rather than at zero under sustained demand.
	///
	/// <para>It is a floor, not a battery: when the pool is already below it the budget goes
	/// negative, which the weapon mounts read as "give charge back" — a drained machine pulls energy
	/// out of its own capacitors to hold the reserve.</para>
	/// </summary>
	public const short EnergyPoolReserve = 500;

	/// <summary>
	/// How badly the reactor itself is hurt. <c>Mech_ComponentDamageWrite</c> (<c>00417de4</c>) sets
	/// one of two latching flags off the damage on sub-piece 5 of the 22-entry dependent array. The
	/// pair cuts movement speed; the reactor rate reads it too, but only before either can be set —
	/// see <see cref="ReactorOutputRate"/> and docs/retail/simulation/reactor-energy-pool.md#reactor-damage-flags.
	/// </summary>
	public enum ReactorCondition {
		/// <summary>Sub-piece 5 at 50% damage or less. No penalty.</summary>
		Intact,

		/// <summary><c>mech+0xaa</c>, set past 50% damage. The rate term it selects is <c>Q10(600)</c>, about 59%.</summary>
		Degraded,

		/// <summary><c>mech+0xab</c>, set at 75% damage or worse. The rate term it selects is <c>Q10(200)</c>, about 20%.</summary>
		Critical,
	}

	/// <summary>
	/// This machine's reactor output rate, computed once when its loadout was configured.
	///
	/// <para><b>It is not recomputed.</b> <c>Mech_ComputeReactorRate</c> has exactly one reference in
	/// the whole of DBSIM — the tail of <c>Mech_ConfigureLoadout</c>, only reached on spawn —
	/// so every damage term it reads is sampled at that one moment and the number stands for the rest
	/// of the mission. A HERC whose reactor is shot to pieces mid-fight keeps generating exactly what
	/// it generated when it rolled out. The damage terms are ported anyway, because they are what the
	/// original computes; at that one call they always read a pristine machine — see
	/// <see cref="ConfigureLoadout"/>.</para>
	/// </summary>
	public short ReactorOutputRate { get; private set; }

	/// <summary>
	/// The Master Energy Pool, <c>mech+0x292</c> — the manual's "advanced capacitor" that the reactor
	/// fills and the weapons and shields draw down. Ranges 0 to <see cref="EnergyPoolMax"/>, starts
	/// full.
	/// </summary>
	public short EnergyPool { get; private set; } = EnergyPoolMax;

	/// <summary>This machine's shield array — the pool's sink once the weapon mounts have taken their share.</summary>
	public ShieldCharge Shields { get; private set; } = new(0);

	/// <summary>
	/// <c>mech+0xb0</c> - the shields-down alert latch, and the one alert latch that re-arms. The hit
	/// test raises it below <see cref="ShieldsDownAlertCharge"/> and plays alert <c>0x15</c>;
	/// <see cref="PowerTick"/> releases it above <see cref="ShieldsDownAlertClearCharge"/>. Only the
	/// player's own machine touches it, which is why a HERC on the MFD TARGET screen (F5) never reads
	/// SHIELDS DN; a flyer or structure there can, through its damage band. The two thresholds and what the gap between them buys are in
	/// docs/retail/simulation/component-damage.md, "What the endpoint announces".
	/// </summary>
	public bool ShieldsDownAlert { get; internal set; }

	/// <summary>The charge the latch trips below - the original's own literal.</summary>
	public const int ShieldsDownAlertCharge = 500;

	/// <summary>The charge the latch re-arms above, <c>0x5dc</c> - the original's own literal.</summary>
	public const int ShieldsDownAlertClearCharge = 1500;

	/// <summary>
	/// This machine's weapon mounts, <c>mech+0x202</c> — built from its own hardpoint list and the fit
	/// the mission gave it. <see cref="WeaponMounts.Empty"/> when either was unavailable.
	/// </summary>
	public WeaponMounts Weapons { get; private set; } = WeaponMounts.Empty;

	/// <summary>The pods this machine's fit gave it. Two of them feed the numbers on this page.</summary>
	public MechPods Pods { get; private set; }

	/// <summary>
	/// The pool as a fraction of full, Q10 over 0-1024 — what
	/// <c>Player_PerFrameCockpitUpdate</c> computes as <c>(pool &lt;&lt; 10) / 10000</c> and hands the
	/// cockpit's energy meter. The meter's own LED bar is built with a range of <c>0x400</c>, so this
	/// is the number it fills against.
	/// </summary>
	public int EnergyPoolFraction => ((int)EnergyPool << 10) / EnergyPoolMax;

	/// <summary>
	/// <c>Mech_ConfigureLoadout</c> (<c>004175dc</c>), in its own order: build the weapon mounts from
	/// the chassis' hardpoint list, give each fitted weapon its own hit spheres and damage record
	/// (<see cref="FitWeaponsToModels"/>), clear the occupancy of every component left with no
	/// internal, file the pods out of the finished mount list, size the shield array and fill it, then
	/// work out the reactor rate.
	///
	/// <para><b>Every damage term is sampled from the machine's own condition, here and now.</b> The
	/// original takes no arguments for them either — it reads the shield generator's dependent and
	/// each pod's component straight off <c>mech+0x206</c>. Its one call, in
	/// <c>DBSim_SpawnMissionObjects</c> (<c>004258a2</c>), comes before either way a machine spawns
	/// damaged — a roster record's starting condition and a squad machine's carried-over one — so
	/// those terms always read a pristine machine and every HERC powers up with its chassis' full
	/// array and reactor rate.</para>
	///
	/// <para>Only the reactor rate is a one-off. Shield capacity is recomputed on every component
	/// write — see <see cref="ShieldCapacity"/>.</para>
	/// </summary>
	public void ConfigureLoadout() {
		Weapons = WeaponMounts.Build(_hardpoints, Loadout, _weapons, _weaponModelCellCount);
		FitWeaponsToModels();
		_damage?.ClearUnoccupied();
		Pods = MechPods.FromLoadout(Weapons);

		short generator = (short)(_damage?.DependentPercent(ShieldGeneratorDependent) ?? 0);

		Shields = new ShieldCharge(Type.ShieldCapacity);
		Shields.SetMax(ShieldCapacity(Type.ShieldCapacity, generator, Pods.ShieldPod,
			MechPods.DamageOf(Pods.ShieldPodMount, _damage)));
		Shields.RefillToBalance();

		ReactorOutputRate = ReactorRate(Pods.EnergyPod,
			MechPods.DamageOf(Pods.EnergyPodMount, _damage), Reactor);
		EnergyPool = EnergyPoolMax;
	}

	/// <summary>
	/// <c>Mech_ConfigureLoadout</c>'s per-mount step. Every retail <c>.COL</c> names the mount
	/// components with no spheres and every <c>.DMG</c> gives them a placeholder piece, so a weapon is
	/// only shootable, and only has armour, because it brings both: its template's sphere set goes
	/// into the mount's cluster about the weapon's mount point (<see cref="CollisionModel.ReplaceCluster"/>,
	/// moved by <c>WeaponMount_MuzzleOffset</c>, which is <see cref="WeaponMount.MountPointOffset"/>),
	/// and its template's piece and internal maximum into the damage record
	/// (<see cref="ComponentDamage.FitWeapon"/>). The component anchors are taken afterwards, as
	/// <c>Collision_CollectComponentAnchors</c> is, so a mount's stands at its weapon's spheres.
	/// See docs/retail/formats/collision-spheres.md#a-fitted-weapon-brings-its-own-spheres.
	/// </summary>
	private void FitWeaponsToModels() {
		foreach (var mount in Weapons.Mounts) {
			if (mount.Template is not { } template) {
				continue;
			}

			short component = (short)(WeaponMounts.FirstMountComponent + mount.LoadoutSlot);
			_collision = CollisionModel.ReplaceCluster(_collision, component,
				template.Cluster.Spheres ?? Array.Empty<ColliderSphere>(), mount.MountPointOffset);
			_damage?.FitWeapon(component, template.Piece, mount.LoadoutSlot, template.InternalMaximum);
		}

		_componentAnchors = null;
	}

	/// <summary>
	/// The pods' own <c>+0x50</c> tick — <c>EcmPod_Tick</c> (<c>0040f184</c>) and
	/// <c>TurboPod_Tick</c> (<c>0040f1f0</c>), the two overrides that read their cockpit row's button.
	/// The other three pods inherit <c>Pod_TickBase</c>, which refreshes the row's display and touches
	/// no pod field, so there is nothing here for them.
	///
	/// <para><b>It runs on the player's machine only.</b> The original's one call site is inside
	/// <c>WeaponMounts_PerFrameUpdate</c> (<c>00410b40</c>), whose own one caller is
	/// <c>Player_PerFrameCockpitUpdate</c> — an AI machine's pods are never ticked at all. That is why
	/// a squadmate fitted with a Turbo Pod never engages it from here, and why an AI machine's ECM
	/// follows its radar mode instead (see <see cref="JammerTick"/>): neither has a row to press.</para>
	///
	/// <list type="bullet">
	/// <item><b>ECM</b> copies the button into <see cref="EcmEnabled"/> and announces the change. It
	/// posts rather than replaces, so unlike the radar and auto-track toggles a run of quick presses
	/// reads the whole sequence out.</item>
	/// <item><b>Turbo</b> engages on a button that is on over <see cref="WeaponMount.TurboEngageCharge"/>
	/// of charge, cuts out when the tank empties, and <b>clears its own button</b> whenever it is not
	/// engaged — so a row pressed with an empty tank lights for one frame and goes out again.</item>
	/// </list>
	/// </summary>
	/// <param name="world">Optional, and only so the engage tone and the messages have somewhere to go.</param>
	public void PodTick(SimWorld? world = null) {
		if (!LocallyPiloted) {
			return;
		}

		if (Pods.EcmMount is { } ecm && EcmEnabled != ecm.PodButton) {
			EcmEnabled = ecm.PodButton;
			world?.Sounds?.Say(EcmEnabled
				? Content.SystemMessages.JammingEngaged
				: Content.SystemMessages.JammingDisabled);
		}

		if (Pods.TurboPodMount is not { } turbo) {
			return;
		}

		if (!turbo.PodButton) {
			turbo.TurboEngaged = false;
			return;
		}

		if (!turbo.TurboEngaged) {
			turbo.EngageTurbo(world, audible: true);
		} else if (turbo.Charge < 1) {
			turbo.TurboEngaged = false;
			turbo.Charge = 0;
		}

		turbo.PodButton = turbo.TurboEngaged;
	}

	/// <summary>
	/// <c>Mech_LocomotionTick</c>'s Turbo Pod term (<c>00416b64</c>): what an engaged pod adds to the
	/// speed the pilot asked for, as a Q10 fraction of the machine's own top speed in the direction it
	/// is already travelling.
	///
	/// <para><b>Maximal at full health.</b> It is <see cref="DamageScale"/> — the curve the Shield and
	/// Energy pods share — taken against a literal 1000 rather than 1024, so a pristine pod is worth
	/// about 98% of top speed and a badly chewed one about 20%, with nothing at all past 225/256
	/// damage. Omitting it is therefore not neutral on an undamaged machine.</para>
	///
	/// <para>A stationary machine gets nothing: the original's gate is <c>speed != 0</c>, on the speed
	/// carried over from the previous tick, so the pod accelerates a machine that is already moving
	/// rather than starting one.</para>
	/// </summary>
	/// <returns>The term to add to desired speed, in the same units, or zero when no pod applies.</returns>
	private short TurboSpeedBonus() {
		if (Pods.TurboPodMount is not { TurboEngaged: true } turbo) {
			return 0;
		}

		int scale = Speed != 0 && DamageScale(MechPods.DamageOf(turbo, _damage)) is { } curve
			? SimMath.Q10Multiply(curve, TurboSpeedScale)
			: 0;

		return (short)SimMath.Q10Multiply(scale, Speed < 1 ? Type.MaxReverse : Type.MaxForward);
	}

	/// <summary>
	/// What the Turbo Pod's damage curve is taken against — the literal <c>1000</c> in
	/// <c>Mech_LocomotionTick</c>, one Q10 unit short of the 1024 that would make a pristine pod
	/// worth a whole second top speed.
	/// </summary>
	public const short TurboSpeedScale = 1000;

	/// <summary>
	/// <c>Mech_ComputeReactorRate</c> (<c>00417d08</c>) — the reactor's output rate.
	///
	/// <para>A flat <see cref="BaseReactorOutputRate"/>, replaced outright (not scaled) by a much
	/// smaller figure when either reactor-damage flag is up, plus an Energy Pod's contribution. The
	/// pod's term is worth a second <see cref="BaseReactorOutputRate"/> at full — it <i>doubles</i>
	/// reactor output — and falls away in five steps as the pod itself is chewed up, contributing
	/// nothing at all once it passes 88% damage. The manual sells the Energy Pod as doubling the
	/// pool's <i>capacity</i>; the code doubles its <i>recharge rate</i> and leaves
	/// <see cref="EnergyPoolMax"/> alone, so on that point the manual is wrong and its own
	/// afterthought — "a modest increase in the Pool recharge rate" — is the real effect.</para>
	/// </summary>
	/// <param name="energyPod">Whether slot 3 is filled.</param>
	/// <param name="podDamage">The pod's component damage, Q8 over 0-256 with 0 pristine.</param>
	/// <param name="reactor">Which of the two reactor-damage flags, if either, is up.</param>
	public static short ReactorRate(bool energyPod, short podDamage, ReactorCondition reactor) {
		short rate = reactor switch {
			ReactorCondition.Critical => (short)SimMath.Q10Multiply(200, BaseReactorOutputRate),
			ReactorCondition.Degraded => (short)SimMath.Q10Multiply(600, BaseReactorOutputRate),
			_ => BaseReactorOutputRate,
		};

		if (energyPod && DamageScale(podDamage) is { } scale) {
			rate += (short)SimMath.Q10Multiply(scale, BaseReactorOutputRate);
		}

		return rate;
	}

	/// <summary>
	/// <c>Mech_ComputeShieldCapacity</c> (<c>00417bec</c>) — the shield array's total capacity, the
	/// sibling of <see cref="ReactorRate"/> and built the same way.
	///
	/// <para>Damage to the shield generator scales the base capacity down: past 50% it drops in five
	/// steps to half. A Shield Pod then adds up to a second full base capacity on top — the manual's
	/// "doubles the effective size of your shield reserves", exactly — degrading with the pod's own
	/// damage on the same five-step curve the Energy Pod uses. The pod's share is a fraction of the
	/// <i>undamaged</i> base, not of the generator-reduced figure, which is the original's own
	/// arithmetic and means a battered machine still gets the full pod bonus.</para>
	/// </summary>
	/// <param name="generatorDamage">
	/// Dependent sub-piece 4 — the shield generator — as a Q8 fraction, 0 pristine. The original
	/// spells this reading out inline rather than going through <c>Component_ReadDamagePercent</c>.
	/// </param>
	public static short ShieldCapacity(short baseCapacity, short generatorDamage, bool shieldPod,
			short podDamage) {
		short capacity = baseCapacity;

		// Generator wear, on its own curve: 25-damage steps against a 102/1024 penalty each, so the
		// worst case is 514/1024 of base rather than zero.
		if (generatorDamage > 0x7f) {
			capacity = (short)SimMath.Q10Multiply(baseCapacity,
				((generatorDamage - 0x80) / 0x19) * -0x66 + 0x400);
		}

		if (shieldPod && DamageScale(podDamage) is { } scale) {
			capacity += (short)SimMath.Q10Multiply(scale, baseCapacity);
		}

		return capacity;
	}

	/// <summary>
	/// The five-step curve both pods degrade on, shared verbatim between
	/// <c>Mech_ComputeReactorRate</c> and <c>Mech_ComputeShieldCapacity</c>:
	/// <c>1024 - 204 * (damage / 51)</c>, Q10, gated off entirely at 225 damage out of 256. Null
	/// means the pod is too far gone to contribute — which is not the same as a scale of zero, and
	/// the original's gate is why the last step is 208/1024 rather than a smooth fade to nothing.
	/// </summary>
	private static short? DamageScale(short damage) =>
		damage < 0xe1 ? (short)((damage / 0x33) * -0xcc + 0x400) : null;

	/// <summary>
	/// The reactor/energy half of <c>Mech_PerTickSystemsUpdate</c> — its first five statements, in
	/// order, and the only place the pool moves.
	///
	/// <list type="number">
	/// <item>The reactor's rate is integrated over this tick and added to the pool.</item>
	/// <item>The weapon mounts are offered <c>pool - 500</c> and hand back what they did not take.</item>
	/// <item>The shields are offered that remainder and hand back what <i>they</i> did not take.</item>
	/// <item>The reserve is added back and the result becomes the new pool, <b>overwriting</b> it —
	/// consumption is not a subtraction, it is that the pool is rebuilt from whatever survived the
	/// pass.</item>
	/// <item>The pool is clamped to 0..<see cref="EnergyPoolMax"/>.</item>
	/// <item>On the player's own machine, <see cref="ShieldsDownAlert"/> is released once the array
	/// holds more than <see cref="ShieldsDownAlertClearCharge"/>.</item>
	/// </list>
	///
	/// <para>The ordering is the answer to the manual's claim that energy goes to "movement, shields,
	/// and weapons, in that order". It is weapons and then shields, and movement takes nothing at all
	/// — the locomotion tick never reads the pool. What the manual gets right is the consequence:
	/// because weapons are served first and the shields only ever get the leftovers, a machine firing
	/// hard stops replenishing its shields long before it stops shooting.</para>
	///
	/// <para>All of it is 16-bit, and deliberately so — the original's adds are word-sized and wrap.
	/// Nothing in the real ranges gets anywhere near an overflow, but the arithmetic is kept faithful
	/// rather than widened.</para>
	/// </summary>
	private void PowerTick(SimWorld world) {
		EnergyPool = unchecked((short)(EnergyPool + SimMath.IntegrateRateOverTick(ReactorOutputRate)));

		short budget = unchecked((short)(EnergyPool - EnergyPoolReserve));
		budget = ChargeWeapons(budget, world);
		budget = Shields.RechargeTick(budget);

		EnergyPool = unchecked((short)(budget + EnergyPoolReserve));
		EnergyPool = Math.Clamp(EnergyPool, (short)0, EnergyPoolMax);

		// The shields-down latch re-arms here, where the original clears it, and on the same guard.
		if (LocallyPiloted && Shields.Total > ShieldsDownAlertClearCharge) {
			ShieldsDownAlert = false;
		}
	}

	/// <summary>
	/// <b>Not a ported mechanic — a test seam.</b> Empties the Master Energy Pool down to
	/// <see cref="EnergyPoolReserve"/>, the floor the tick holds back and the lowest a running
	/// machine settles at. The host's debug panel is the only caller.
	///
	/// <para>Nothing in the engine spends the pool hard enough to reach the floor yet — energy
	/// weapons are a later milestone and the shields alone draw less than the reactor makes — so this
	/// is how the refill is watched: from 500 the pool climbs back at the reactor's rate, minus
	/// whatever the shields are still taking.</para>
	/// </summary>
	public void DrainEnergyPoolForTest() => EnergyPool = EnergyPoolReserve;

	/// <summary>
	/// The weapon mounts' claim on the pool — vtable slot 0 of the mount-manager object at
	/// <c>mech+0x202</c>, which is <c>WeaponMounts_ArbitrateEnergy</c> (<c>004107e4</c>) for every machine. See
	/// <see cref="WeaponMounts.ChargeTick"/> for the arbitration itself.
	///
	/// <para>The armed mount is served first, and only a locally-piloted machine has one: the local
	/// manager passes its own selection, the remote manager passes -1 and goes straight to the
	/// priority ranking.</para>
	/// </summary>
	private short ChargeWeapons(short budget, SimWorld world) =>
		Weapons.ChargeTick(budget, IsPlayer ? Weapons.Selected : WeaponMounts.NoSelection, this, world);
}
