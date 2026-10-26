using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Anim;
using Herculan.Es1.Input;
using Herculan.Es1.View;
using Herculan.Es1.WeaponRows;

namespace Herculan.Es1.Sim;

/// <summary>
/// The machine's mount manager at <c>+0x198</c> (docs/es1/simulation/weapon-mounts.md#the-manager): the mount array with
/// its holes, and for the player's machine the three chains and the armed mount. Built by
/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0000c31c</c>) and, for the local manager, <c>WeaponMounts_CtorLocal</c>
/// (<c>0000c808</c>).
/// </summary>
public sealed class Es1WeaponMounts {
	/// <summary>The weapon id that builds no mount and leaves the slot empty: one past the 17, and what a negative id becomes.</summary>
	public const short NoWeapon = 17;

	/// <summary>The projectile index meaning no record, which <c>ECM</c> carries.</summary>
	public const short NoProjectile = 20;

	/// <summary>The projectile index meaning a rocket record looked up by the hardpoint's ammunition type.</summary>
	public const short RocketByAmmunition = 21;

	/// <summary>The ECM's weapon id, which no chain takes.</summary>
	public const short EcmWeapon = 13;

	/// <summary>The value <c>+0x1d</c> holds with no mount armed.</summary>
	public const int NoneArmed = 0xff;

	private Es1WeaponMounts(Es1WeaponMount?[] mounts, bool local) {
		Mounts = mounts;
		IsLocal = local;
		Chains = local ? [new bool[mounts.Length], new bool[mounts.Length], new bool[mounts.Length]] : [];
	}

	/// <summary><c>+0x00</c> and <c>+0x04</c>, a mount per hardpoint record, null where none was built.</summary>
	public IReadOnlyList<Es1WeaponMount?> Mounts { get; }

	/// <summary>Whether this is the local manager (vptr <c>0009158a</c>), the player's; otherwise the base (<c>000912f2</c>).</summary>
	public bool IsLocal { get; }

	/// <summary><c>+0x1f</c>, <c>+0x25</c>, <c>+0x2b</c>: per chain, whether each mount is in it. Empty for the base manager.</summary>
	public IReadOnlyList<bool[]> Chains { get; }

	/// <summary><c>+0x1c</c>, the current chain.</summary>
	public int CurrentChain { get; private set; }

	/// <summary><c>+0x1d</c>, the armed mount, <see cref="NoneArmed"/> for none.</summary>
	public int Armed { get; private set; } = NoneArmed;

	/// <summary><c>+0x16</c>, the chain-changed byte.</summary>
	public bool ChainChanged { get; private set; }

	/// <summary>
	/// <c>+0x14</c>, TRACK's latch, which the turret's tracking and <c>Player_PerFrameCockpitUpdate</c> read
	/// (docs/es1/simulation/weapon-mounts.md#the-manager), and <see cref="PerFrameUpdate"/> copies from the console's
	/// <c>TRACK</c> each frame.
	/// </summary>
	public bool TrackLatch { get; private set; }

	/// <summary><c>+0x18</c>, single fire, which arming a row sets.</summary>
	public bool SingleFire { get; private set; }

	/// <summary><c>+0x31</c>, the range to the selected target the per-frame pass stores.</summary>
	public int Range { get; private set; }

	/// <summary>
	/// <c>+0x06</c>, the five <c>int16</c> missile-lock flags, one per rocket subtype, which <see cref="MountIsReady"/> reads.
	/// PLACEHOLDER: their writer, the lock of <c>Mech_PerTickSystemsUpdate</c>, is plan step 7c's; until then they stay
	/// clear, so a launcher of subtype 0, 1, 2 or 4 never answers ready.
	/// </summary>
	public short[] MissileLocks { get; } = new short[5];

	/// <summary>
	/// The electro-optical launch's hold on the chain, <c>maybe_WeaponChain_EoHold</c> (<c>000b5bc6</c>) and
	/// <c>maybe_WeaponChain_EoStepPending</c> (<c>000b5bc8</c>), two globals in retail, which the simulation hands the
	/// player's manager. A manager with none reads both as clear and writes neither.
	/// </summary>
	public Es1ControlsState? ChainHold { get; set; }

	/// <summary><see cref="Armed"/> as the signed byte retail reads, −1 for none.</summary>
	private int ArmedIndex => (sbyte)Armed;

	/// <summary>
	/// <c>WeaponMounts_BuildGauges</c> (<c>0000c9b0</c>)(manager), from <c>Sim_StartMission</c>'s step 13: each mount's
	/// gauge factory (<see cref="Es1WeaponMount.CreateGauge"/>) on <paramref name="cockpit"/>, <c>CockpitViewInstance</c>.
	/// </summary>
	public void BuildGauges(Es1Cockpit cockpit) {
		foreach (var mount in Mounts) {
			mount?.CreateGauge(cockpit);
		}
	}

	/// <summary>
	/// <c>WeaponMounts_SelectByGauge</c> (<c>0000ca70</c>)(manager, <paramref name="gauge"/>): the first mount whose gauge
	/// match passes; false when none does or it cannot be armed (<see cref="Es1WeaponMount.Selectable"/> clear);
	/// otherwise it is armed (<see cref="SetSelection"/>), single fire set, and true.
	/// </summary>
	public bool SelectByGauge(Es1WeaponGauge gauge) {
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is { } mount && mount.MatchesGauge(gauge)) {
				if (!mount.Selectable) {
					return false;
				}

				SetSelection(i);
				SingleFire = true;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>WeaponMounts_SetSelection</c> (<c>0000cb00</c>)(manager, <paramref name="index"/>): when the index is not the
	/// armed one, a linked mount whose hardpoint's partner offset is negative hands it to its partner, it is stored, and
	/// the electro-optical launch's pending step (<see cref="Es1ControlsState.MissileStepPending"/>) is cleared.
	/// </summary>
	public void SetSelection(int index) {
		if (index == ArmedIndex) {
			return;
		}

		if (Mounts[index] is { Linked: true } mount && (sbyte)mount.Hardpoint.LinkPartnerOffset is < 0 and var offset) {
			index += offset;
		}

		Armed = (byte)index;
		if (ChainHold != null) {
			ChainHold.MissileStepPending = false;
		}
	}

	/// <summary>
	/// <c>WeaponMounts_StepSelection</c> (<c>0000cb54</c>)(manager, <paramref name="step"/>): with a mount armed, from it by
	/// the step, wrapping below 0 to the last mount and past the last to 0, to the first mount that can be armed, is
	/// unlinked or the first half of a pair (a positive partner offset), and is in the current chain, or back to the armed
	/// one; then <see cref="SetSelection"/>.
	/// </summary>
	public void StepSelection(int step) {
		int start = ArmedIndex;
		if (start < 0) {
			return;
		}

		int index = start;
		bool found;
		do {
			index += step;
			if (index < 0) {
				index = Mounts.Count - 1;
			} else if (index >= Mounts.Count) {
				index = 0;
			}

			found = Mounts[index] is { Selectable: true } mount
				&& (!mount.Linked || (sbyte)mount.Hardpoint.LinkPartnerOffset > 0)
				&& Chains[CurrentChain][index];
		} while (!found && index != start);

		SetSelection(index);
	}

	/// <summary>
	/// <c>WeaponMounts_SetChain</c> (<c>0000cf10</c>)(manager, <paramref name="chain"/>): when the chain is not the current
	/// one, it is stored, the selection stepped when the armed mount is not in it, and the chain handed to the console
	/// <paramref name="console"/>, <c>CockpitViewInstance</c>'s <c>+0x1dd</c> (<see cref="Es1ConsoleButtons.SetChainNumber"/>).
	/// </summary>
	public void SetChain(short chain, Es1ConsoleButtons? console) {
		if (chain == CurrentChain) {
			return;
		}

		CurrentChain = (sbyte)chain;
		if (ArmedIndex >= 0 && !Chains[CurrentChain][ArmedIndex]) {
			StepSelection(1);
		}

		console?.SetChainNumber(chain);
	}

	/// <summary><c>WeaponMounts_XorChainBit</c> (<c>0000d180</c>)(manager, <paramref name="index"/>): the mount's entry in the current chain flipped.</summary>
	public void XorChainBit(int index) => Chains[CurrentChain][index] = !Chains[CurrentChain][index];

	/// <summary>
	/// <c>WeaponMounts_ToggleChainMember</c> (<c>0000d5e4</c>)(manager, <paramref name="row"/>): the first mount whose
	/// hardpoint's row (signed byte <c>+7</c>) is the row and whose template's <c>int32</c> <c>+0x30</c> is above 0 has its
	/// chain entry flipped and, when its energy priority is 0, its wake slot called; true, or false when none matches.
	/// </summary>
	public bool ToggleChainMember(short row) {
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is { } mount && (sbyte)mount.Hardpoint.FireChainNumber == row && mount.Template.Range > 0) {
				XorChainBit(i);
				if (mount.EnergyPriority == 0) {
					mount.Wake();
				}

				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>WeaponMounts_ToggleLink</c> (<c>0000d41c</c>)(manager): with a mount armed, the link flipped on it and on the
	/// mount its hardpoint's partner offset names, when the offset is not 0, the partner exists and the two are the same
	/// weapon (the templates' <c>+0x56</c>, the weapon id). A partner index outside the array, which retail reads past
	/// and no retail <c>.GL</c> holds, is no partner, an engine guard.
	/// </summary>
	public void ToggleLink() {
		if (ArmedIndex < 0 || Mounts[ArmedIndex] is not { } armed) {
			return;
		}

		int offset = (sbyte)armed.Hardpoint.LinkPartnerOffset;
		int partner = ArmedIndex + offset;
		if (offset != 0 && partner >= 0 && partner < Mounts.Count && Mounts[partner] is { } other
				&& other.WeaponId == armed.WeaponId) {
			armed.Linked = !armed.Linked;
			other.Linked = !other.Linked;
		}
	}

	/// <summary><c>WeaponMounts_SetTrackLatch</c> (<c>0000d408</c>)(manager): <see cref="TrackLatch"/> set.</summary>
	public void SetTrackLatch() => TrackLatch = true;

	/// <summary>
	/// <c>WeaponMounts_PerFrameUpdate</c> (<c>0000cf78</c>)(manager, <paramref name="range"/>), from
	/// <c>Player_PerFrameCockpitUpdate</c> (docs/es1/simulation/weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78):
	/// with the electro-optical launch's step pending and its missile no longer flown, the selection stepped on and single
	/// fire cleared; the range stored and <see cref="AdvanceToReady"/>; then the console's block (<see cref="Es1ConsoleButtons.GetStateBlock"/>): with the chain-changed
	/// byte set it is cleared and the manager's chain put in the block, otherwise <see cref="SetChain"/> takes the
	/// block's; the block's tracking byte sets <see cref="TrackLatch"/> or clears it; its link byte runs
	/// <see cref="ToggleLink"/>; the link byte cleared and the block written back
	/// (<see cref="Es1ConsoleButtons.SetStateBlock"/>). Then for each mount, armed when it is the armed one, or when it is
	/// linked, neither destroyed nor unselectable, and its partner is the armed one; a linked mount that is destroyed or
	/// unselectable has its and its partner's link flipped and hands the selection to its partner; then its gauge push
	/// (<see cref="Es1WeaponMount.PushGaugeState"/>) with whether it is ready (<see cref="MountIsReady"/>), in the current
	/// chain and armed.
	/// </summary>
	/// <param name="console"><c>CockpitViewInstance</c>'s <c>+0x1dd</c>; with none, which retail always has, the block's part is skipped.</param>
	public void PerFrameUpdate(int range, Es1ConsoleButtons? console, Es1SimClock clock) {
		// Retail calls it on the player's local manager alone; a base manager has no chains, an engine guard.
		if (!IsLocal) {
			return;
		}

		if (ChainHold is { MissileStepPending: true, MissileFlown: false }) {
			StepSelection(1);
			SingleFire = false;
		}

		Range = range;
		AdvanceToReady();

		if (console != null) {
			var block = console.GetStateBlock();
			if (!ChainChanged) {
				SetChain(block.Chain, console);
			} else {
				ChainChanged = false;
				block.Chain = (short)CurrentChain;
			}

			if (block.Tracking) {
				SetTrackLatch();
			} else {
				TrackLatch = false;
			}

			if (block.Link) {
				ToggleLink();
			}

			block.Link = false;
			console.SetStateBlock(block);
		}

		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is not { } mount) {
				continue;
			}

			bool armed = i == ArmedIndex;
			if (mount.Linked) {
				int partner = i + (sbyte)mount.Hardpoint.LinkPartnerOffset;
				if (!mount.Destroyed && mount.Selectable) {
					if (partner == ArmedIndex) {
						armed = true;
					}
				} else if (partner >= 0 && partner < Mounts.Count && Mounts[partner] is { } other) {
					// A partner outside the array or missing, which retail writes through and no retail .GL holds, is
					// skipped, an engine guard.
					mount.Linked = !mount.Linked;
					other.Linked = !other.Linked;
					SetSelection(partner);
				}
			}

			mount.PushGaugeState(MountIsReady(mount), Chains[CurrentChain][i], armed, clock);
		}
	}

	/// <summary>
	/// <c>WeaponMounts_MountIsReady</c> (<c>0000cdb0</c>)(manager, <paramref name="mount"/>)
	/// (docs/es1/simulation/weapon-mounts.md#readiness): the mount's ready slot (<see cref="Es1WeaponMount.CanFire"/>);
	/// then, with a range stored, <see cref="Es1WeaponMount.RangeAllows"/>; then its ammunition subtype is
	/// <see cref="Es1WeaponMount.NotALauncher"/>, the electro-optical 3, or one whose <see cref="MissileLocks"/> flag is
	/// set. A subtype past the five flags, which no retail record has, is not ready, an engine guard.
	/// </summary>
	public bool MountIsReady(Es1WeaponMount mount) {
		if (!mount.CanFire() || (Range != 0 && !mount.RangeAllows(Range))) {
			return false;
		}

		short subtype = mount.AmmunitionSubtype(out _);
		return subtype == Es1WeaponMount.NotALauncher || subtype == Projectiles.Es1Rocket.ElectroOpticalSubtype
			|| (subtype >= 0 && subtype < MissileLocks.Length && MissileLocks[subtype] != 0);
	}

	/// <summary>
	/// <c>WeaponMounts_ChainReady</c> (<c>0000ce1c</c>)(manager, <paramref name="mount"/>): ready without asking while single
	/// fire or the electro-optical hold (<see cref="Es1ControlsState.MissileFlown"/>) is set, otherwise
	/// <see cref="MountIsReady"/>. No mount, a hole the selection never rests on, is not ready, an engine guard.
	/// </summary>
	private bool ChainReady(Es1WeaponMount? mount) =>
		SingleFire || ChainHold?.MissileFlown == true || (mount != null && MountIsReady(mount));

	/// <summary>
	/// <c>WeaponMounts_AdvanceToReady</c> (<c>0000ce54</c>)(manager) (docs/es1/simulation/weapon-mounts.md#readiness): with a
	/// mount armed that <see cref="ChainReady"/> refuses, the selection stepped by +1 until a mount passes, which clears
	/// single fire; when the steps come back to the start, or pass the mount count by one, the original is put back.
	/// </summary>
	private void AdvanceToReady() {
		int start = ArmedIndex;
		if (start < 0 || ChainReady(Mounts[start])) {
			return;
		}

		int steps = 0;
		do {
			StepSelection(1);
			if (ChainReady(Mounts[ArmedIndex])) {
				SingleFire = false;
				return;
			}

			if (ArmedIndex == start) {
				break;
			}
		} while (steps++ <= Mounts.Count);

		Armed = (byte)start;
		SetSelection(start);
	}

	/// <summary>
	/// <c>WeaponMounts_FireTrigger</c> (<c>0000d2bc</c>)(manager, <paramref name="machine"/>, <paramref name="input"/>), from
	/// <c>Sim_PollPlayerInput</c> with the camera not driven
	/// (docs/es1/simulation/weapon-mounts.md#firing--weaponmounts_firetrigger-0000d2bc): with a mount armed, it and, when
	/// linked, the partner its hardpoint's offset names must both answer ready (<see cref="Es1WeaponMount.CanFire"/>) and
	/// then both take the trigger (<see cref="Es1WeaponMount.TriggerHeld"/>); then the armed mount fires
	/// (<see cref="Es1WeaponMount.Fire"/>) with <see cref="Es1SimGlobals.FreeShots"/>, single fire is cleared when it no
	/// longer answers ready, an electro-optical launcher (subtype 3) sets <see cref="Es1ControlsState.MissileStepPending"/>
	/// and <see cref="Es1ControlsState.MissileFlown"/>, and the partner fires the same way, single fire again cleared on the
	/// armed mount's answer. A partner outside the array, which retail reads past and no retail <c>.GL</c> holds, is none,
	/// an engine guard.
	/// </summary>
	public void FireTrigger(Es1SimGlobals globals, Es1Mech machine, Es1PlayerInputBlock input) {
		if (ArmedIndex < 0 || Mounts[ArmedIndex] is not { } armed) {
			return;
		}

		bool freeShot = globals.FreeShots;
		int at = ArmedIndex + (sbyte)armed.Hardpoint.LinkPartnerOffset;
		var partner = armed.Linked && at >= 0 && at < Mounts.Count ? Mounts[at] : null;
		if (!armed.CanFire() || (partner != null && !partner.CanFire())) {
			return;
		}

		var latches = ChainHold?.Latches;
		if (!armed.TriggerHeld(input, latches) || (partner != null && !partner.TriggerHeld(input, latches))) {
			return;
		}

		armed.Fire(freeShot, machine, globals);
		if (!armed.CanFire()) {
			SingleFire = false;
		}

		if (armed.AmmunitionSubtype(out _) == Projectiles.Es1Rocket.ElectroOpticalSubtype && ChainHold != null) {
			ChainHold.MissileStepPending = true;
			ChainHold.MissileFlown = true;
		}

		if (partner != null) {
			partner.Fire(freeShot, machine, globals);
			if (!armed.CanFire()) {
				SingleFire = false;
			}
		}
	}

	/// <summary>
	/// The manager's energy slot, (manager, <paramref name="budget"/>), the weapons' share of the systems tick's pool
	/// (docs/es1/simulation/weapon-mounts.md#the-manager): the local manager's
	/// <c>WeaponMounts_ArbitrateEnergyWithSelection</c> (<c>0000c994</c>) serves the armed mount first, the base's
	/// <c>WeaponMounts_ArbitrateEnergyNoSelection</c> (<c>0000c5bc</c>) none, through <see cref="ArbitrateEnergy"/>.
	/// Answers the budget left.
	/// </summary>
	public short EnergyTurn(short budget, Es1SimGlobals globals) =>
		ArbitrateEnergy(IsLocal ? ArmedIndex : -1, budget, globals);

	/// <summary>
	/// <c>WeaponMounts_ArbitrateEnergy</c> (<c>0000cc2c</c>)(manager, <paramref name="selection"/>, <paramref name="budget"/>)
	/// (docs/es1/simulation/weapon-mounts.md#energy--weaponmounts_arbitrateenergy-0000cc2c): up to the mount count of
	/// turns, each serving the selection while it is not negative and not yet served, otherwise the unserved mount whose
	/// energy priority (<see cref="Es1WeaponMount.EnergyPriority"/>) is highest, the first of equals; a served mount's pool
	/// turn (<see cref="Es1WeaponMount.PoolTurn"/>) is told whether a mount served before it ended its turn mid-charge
	/// and takes the budget, its answer the budget for the next; the walk stops at the first turn with no mount. With
	/// <see cref="Es1SimGlobals.FreeShots"/> the budget it was given is answered instead.
	/// </summary>
	public short ArbitrateEnergy(int selection, short budget, Es1SimGlobals globals) {
		short given = budget;
		var served = new bool[Mounts.Count];
		bool midChargeSeen = false;
		for (int turn = 0; turn < Mounts.Count; turn++) {
			Es1WeaponMount? mount = null;
			int pick = -1;
			if (selection >= 0 && selection < Mounts.Count && !served[selection]) {
				pick = selection;
				mount = Mounts[selection];
			} else {
				for (int i = 0; i < Mounts.Count; i++) {
					if (!served[i] && Mounts[i] is { } candidate
							&& (mount == null || candidate.EnergyPriority > mount.EnergyPriority)) {
						mount = candidate;
						pick = i;
					}
				}
			}

			if (mount == null) {
				break;
			}

			served[pick] = true;
			budget = mount.PoolTurn(midChargeSeen, budget, globals.Clock);
			if (mount.MidCharge) {
				midChargeSeen = true;
			}
		}

		return globals.FreeShots ? given : budget;
	}

	/// <summary>
	/// The factory and, when <paramref name="local"/>, <c>WeaponMounts_CtorLocal</c>'s chains: for each hardpoint record in
	/// file order the weapon and ammunition type its <c>+0x17</c> slot indexes in <paramref name="fit"/> and
	/// <paramref name="ammunition"/>, and the class the weapon id picks
	/// (docs/es1/formats/weapons-dat-sim.md#the-projectile-index). <paramref name="modelFor"/> gives the weapon model for a
	/// template and mounting code.
	/// </summary>
	public static Es1WeaponMounts Build(bool local, GunLayout? hardpoints, Func<int, short> fit, Func<int, short> ammunition,
			Es1Weapons weapons, Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor,
			Func<int, AnimationThread> addThread) {
		var records = hardpoints?.Hardpoints ?? [];
		var mounts = new Es1WeaponMount?[records.Length];
		for (int i = 0; i < records.Length; i++) {
			var hardpoint = records[i];
			short weapon = fit(hardpoint.LoadoutSlot);
			if (weapon < 0) {
				weapon = NoWeapon;
			}

			if (weapon >= NoWeapon || weapon >= weapons.Templates.Length) {
				continue;
			}

			var template = weapons.Templates[weapon];
			var kind = weapon switch {
				3 => Es1MountKind.Elf,
				4 or 6 or 7 or 8 or 12 or 14 or 15 => Es1MountKind.Energy,
				5 => Es1MountKind.Grenade,
				13 => Es1MountKind.Ecm,
				_ => Es1MountKind.Ammunition,
			};

			var projectile = kind == Es1MountKind.Ecm ? null : Projectile(weapons, template.ProjDatIndex, ammunition(hardpoint.LoadoutSlot));
			var model = hardpoint.MountingCode < Es1MechType.InvisibleMounting ? modelFor(template, hardpoint.MountingCode) : null;
			mounts[i] = new Es1WeaponMount(kind, weapon, hardpoint, template, projectile, model, addThread);
		}

		var manager = new Es1WeaponMounts(mounts, local);
		if (local) {
			manager.BuildChains();
		}

		return manager;
	}

	/// <summary>
	/// The projectile record the template's index names: none for 20, <c>Proj_LookupRecord</c>(ammunition type, rocket) for
	/// 21 with 5 read as 0, otherwise <c>Proj_LookupRecordByIndex</c>; null for an index past the 19 records.
	/// </summary>
	private static ProjectileData.Projectile? Projectile(Es1Weapons weapons, short index, short ammunition) {
		if (index == NoProjectile) {
			return null;
		}

		if (index == RocketByAmmunition) {
			short subtype = ammunition == 5 ? (short)0 : ammunition;
			return Projectiles.Es1ProjectilePool.LookupRecord(weapons.Projectiles, subtype, Projectiles.Es1Rocket.RecordType);
		}

		return index >= 0 && index < weapons.Projectiles.Length ? weapons.Projectiles[index] : null;
	}

	/// <summary>
	/// <c>WeaponMounts_CtorLocal</c>'s chains: no mount armed, every mount but the ECM in chain I alone and the first of them
	/// armed, then <c>WeaponMounts_SetChain</c>(0) and the chain-changed byte set. The manager is a fresh zeroed allocation,
	/// so its current chain is already 0 and the set changes nothing.
	/// </summary>
	private void BuildChains() {
		Armed = NoneArmed;
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is not { } mount || mount.WeaponId == EcmWeapon) {
				continue;
			}

			Chains[0][i] = true;
			if (Armed == NoneArmed) {
				Armed = i;
			}
		}

		CurrentChain = 0;
		ChainChanged = true;
	}
}
