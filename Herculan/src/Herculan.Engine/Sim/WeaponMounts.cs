using HercWorks.Core.Data.File.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// A machine's weapon-mount manager, <c>mech+0x202</c> — the object
/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0040fff8</c>) builds and <c>WeaponMounts_CtorLocal</c> (<c>004104ec</c>) extends
/// for a locally-piloted machine. It owns the mounts, which one is armed, the three fire groups, and
/// the mounts' claim on the Master Energy Pool.
///
/// <para>The two classes in the original differ only in what a remote machine does not need: the
/// remote manager (<c>00499338</c>) is the bare mount array, the local one (<c>00499238</c>) adds
/// the selection and the groups. Both are modelled here as one type — a machine with no player in it
/// simply never has its selection read.</para>
/// </summary>
public sealed class WeaponMounts {
	/// <summary>How many fire groups the chain button cycles through — <c>WeaponMounts_CtorLocal</c> (<c>004104ec</c>) allocates exactly three.</summary>
	public const int GroupCount = 3;

	/// <summary><see cref="Selected"/> when the machine has nothing armed — the constructor's <c>0xff</c>.</summary>
	public const int NoSelection = -1;

	private readonly WeaponMount?[] _slots;
	private readonly bool[][] _groups;

	private WeaponMounts(WeaponMount?[] slots) {
		_slots = slots;
		_groups = new bool[GroupCount][];
		for (int group = 0; group < GroupCount; group++) {
			_groups[group] = new bool[slots.Length];
		}
	}

	/// <summary>A machine with no hardpoint list, no fit, or no readable weapon tables.</summary>
	public static WeaponMounts Empty { get; } = new(Array.Empty<WeaponMount?>());

	/// <summary>
	/// Every hardpoint the chassis has, in <c>.GL</c> file order, with an empty one left as a null
	/// hole rather than closed up. The holes matter: the armed-weapon index, the fire-group arrays
	/// and <see cref="WeaponMount.LinkPartnerOffset"/> are all positions in this array, so compacting
	/// it would silently renumber them.
	/// </summary>
	public IReadOnlyList<WeaponMount?> Slots => _slots;

	/// <summary>The fitted mounts, in <c>.GL</c> hardpoint order — see <see cref="WeaponMount.MountIndex"/>.</summary>
	public IEnumerable<WeaponMount> Mounts => _slots.Where(m => m != null)!;

	/// <summary>
	/// The armed mount's index, or <see cref="NoSelection"/>. <c>WeaponMounts_CtorLocal</c> (<c>004104ec</c>) starts it at the
	/// first mount that is not a pod, so a machine powers up with its first real weapon selected —
	/// which is a hardpoint-order first, not a cockpit-row first.
	/// </summary>
	public int Selected { get; private set; } = NoSelection;

	/// <summary>Which fire group the chain button has selected — <c>manager+0x1c</c>, zero at power-up.</summary>
	public int Group { get; private set; }

	/// <summary>
	/// <c>manager+0x18</c>. Set when the player armed a weapon by hand, cleared when the selection was
	/// stepped or advanced by the chain. While it is set, <see cref="PerFrameUpdate"/> leaves the
	/// selection alone however unready that weapon is — the manual's "select a weapon to single-fire";
	/// the original clears it again on the shot, and the chain resumes.
	/// </summary>
	public bool SingleFire { get; private set; }

	/// <summary>
	/// <c>manager+0x31</c> — the distance from the machine to its selected target, in world units,
	/// refreshed once a frame by <see cref="PerFrameUpdate"/>. <b>Zero means no target</b>, and
	/// <see cref="CanFireNow"/> skips its range gate entirely in that case.
	/// </summary>
	public int TargetRange { get; private set; }

	/// <summary>
	/// <c>manager+0x14</c>, the TRACK button's latch — Automatic Turret Tracking. The console button
	/// sets and clears it (<c>WeaponMounts_SetTrackLatch</c> (<c>00410f04</c>) and <c>WeaponMounts_PerFrameUpdate</c>'s own else branch), which is
	/// why TRACK is the one console button that stays lit, and so does the [T] command.
	///
	/// <para>Read by <see cref="MechObject.TorsoTick"/>, which flies the turret at the selected
	/// target while it is set. Both centring commands clear it — see
	/// <see cref="MechObject.LatchCenterTorso"/>.</para>
	/// </summary>
	public bool AutoTrack { get; set; }

	/// <summary>
	/// <c>manager+0x0a</c> — one flag per missile subtype: <b>has this class of launcher achieved
	/// lock on the machine's current target</b>. Five entries, indexed by
	/// <see cref="WeaponMount.AmmoType"/>.
	///
	/// <para>Read by <c>Mech_MissileLockState</c> (mech vtable <c>+0x6c</c>). <c>Rocket_Fire</c> uses
	/// it to decide whether the round it is launching gets a target attached, and
	/// <c>WeaponMounts_MountIsReady</c> uses it to light a missile row's ready box.</para>
	///
	/// <para>Written by <see cref="MechObject.MissileLockTick"/>, which clears all five at the top of
	/// every tick for every machine and sets the ones whose lock timer has run out. Nothing else
	/// touches it.</para>
	/// </summary>
	public IReadOnlyList<bool> MissileLock => _missileLock;

	private readonly bool[] _missileLock = new bool[WeaponMount.NotAMissile];

	/// <summary>Clears every lock flag — the first thing <see cref="MechObject.MissileLockTick"/> does.</summary>
	internal void ClearMissileLock() => Array.Clear(_missileLock);

	/// <summary>Latches lock for one missile subtype.</summary>
	internal void SetMissileLock(int missileType) {
		if (missileType >= 0 && missileType < _missileLock.Length) {
			_missileLock[missileType] = true;
		}
	}

	/// <summary>Whether the given missile subtype currently holds lock.</summary>
	public bool Locked(int missileType) =>
		missileType >= 0 && missileType < _missileLock.Length && _missileLock[missileType];

	/// <summary>Whether any missile subtype holds lock — what a non-launcher's ready lamp reads.</summary>
	public bool AnyLocked => _missileLock.Any(locked => locked);

	/// <summary>
	/// <c>WeaponMounts_RoundsByMissileType</c> (<c>0040fbdc</c>) — rounds carried, totalled per missile subtype across every fitted mount.
	/// A mount that is not a launcher reports <see cref="WeaponMount.NotAMissile"/> and is skipped, so
	/// an entry is non-zero only where the machine actually has that class of missile <i>and has
	/// rounds left for it</i>. That is what decides which lock timers run at all.
	/// </summary>
	/// <param name="rounds">
	/// Filled with one total per subtype; must be at least <see cref="WeaponMount.NotAMissile"/> long.
	/// Zeroed first, as the original's own <c>memset</c> does.
	/// </param>
	public void RoundsByMissileType(short[] rounds) {
		Array.Clear(rounds);

		foreach (var mount in Mounts) {
			short type = mount.AmmoType;
			if (type != WeaponMount.NotAMissile && type >= 0 && type < rounds.Length) {
				rounds[type] += mount.AmmoRounds;
			}
		}
	}

	/// <summary>
	/// Whether cockpit weapon row <paramref name="mountIndex"/> draws as armed. The armed mount does,
	/// and so does the other half of a linked pair when its partner is the armed one — which is what
	/// makes linking visible: both rows light together. <c>WeaponMounts_PerFrameUpdate</c> computes exactly this and
	/// pushes it to each row's gauge.
	/// </summary>
	/// <summary>
	/// <c>Mech_ConvergeGunsOnRange</c>'s loop — every mount toed in on the range the turret is aiming
	/// at. Run from <see cref="MechObject.TorsoPitchTick"/>, so it happens for the player and the AI
	/// alike; see docs/simulation/weapon-firing.md ("Gun convergence").
	/// </summary>
	internal void ConvergeOnRange(MechObject owner, int range) {
		foreach (var mount in Mounts) {
			mount.ConvergeOnRange(owner, range);
		}
	}

	public bool IsArmedRow(int mountIndex) {
		if (mountIndex == Selected) {
			return true;
		}

		return _slots.ElementAtOrDefault(mountIndex) is { Linked: true } mount
			&& mountIndex + mount.LinkPartnerOffset == Selected;
	}

	/// <summary>
	/// The mount this one is link-fired with, or null when it is unlinked or its partner slot is
	/// empty. The pairing itself is the chassis': <see cref="WeaponMount.LinkPartnerOffset"/> is the
	/// <c>.GL</c> record's own signed neighbour offset.
	/// </summary>
	public WeaponMount? PartnerOf(WeaponMount mount) {
		int partner = mount.MountIndex + mount.LinkPartnerOffset;
		return mount.LinkPartnerOffset != 0 && partner >= 0 && partner < _slots.Length
			? _slots[partner]
			: null;
	}

	/// <summary>
	/// Whether the currently armed mount could be linked at all — what the LINK button and <c>[L]</c>
	/// need before they do anything. <c>WeaponMounts_ToggleLink</c> (<c>00410f14</c>)'s own two conditions: the armed mount has a
	/// partner hardpoint, and that hardpoint carries the same weapon. The manual states the same rule
	/// from the other side: "any two identical weapons mounted symmetrically on the HERC".
	/// </summary>
	public bool CanLink =>
		_slots.ElementAtOrDefault(Selected) is { } armed
		&& PartnerOf(armed) is { } partner
		&& partner.WeaponId == armed.WeaponId;

	/// <summary>Whether <paramref name="mountIndex"/> is in the currently selected fire group.</summary>
	public bool InCurrentGroup(int mountIndex) =>
		Group >= 0 && Group < _groups.Length && mountIndex >= 0 && mountIndex < _groups[Group].Length
			&& _groups[Group][mountIndex];

	/// <summary>The mount that owns cockpit weapon row <paramref name="gaugeSlot"/>, or null when none does.</summary>
	public WeaponMount? BySlot(int gaugeSlot) => Mounts.FirstOrDefault(m => m.GaugeSlot == gaugeSlot);

	/// <summary>
	/// <c>WeaponMounts_MountForHardpointSlot</c> (<c>00410670</c>) — the mount a damage component names, or null when that component is not a
	/// mount or the fit left its hardpoint empty.
	///
	/// <para>The match is on <see cref="WeaponMount.LoadoutSlot"/>, not on a position in this array:
	/// <c>Mech_ConfigureLoadout</c> registers each mount's collision and damage records under
	/// <c>LoadoutSlot + </c><see cref="FirstMountComponent"/>, and this is that mapping read
	/// backwards. A chassis whose hardpoints are listed in a different order from its fit slots — and
	/// they routinely are — would give the wrong mount if the array index were used instead.</para>
	/// </summary>
	public WeaponMount? ByComponent(int componentIndex) =>
		Mounts.FirstOrDefault(m => m.LoadoutSlot == componentIndex - FirstMountComponent);

	/// <summary>
	/// The damage component the machine's first weapon mount occupies. Components
	/// <see cref="FirstMountComponent"/> upward are the mounts; everything below is structure.
	/// </summary>
	public const int FirstMountComponent = 19;

	/// <summary>
	/// <c>MechLoadout_ConstructWeaponMounts</c> followed by <c>WeaponMounts_CtorLocal</c> (<c>004104ec</c>): walk the chassis'
	/// hardpoint list in file order, resolve each record's fit slot into a weapon id and a mount, and
	/// then work out the fire groups and the initial selection.
	///
	/// <para>The group pass is simple and worth stating plainly, because the cockpit's state boxes
	/// depend on it: <b>every non-pod mount goes into group I and nothing goes into II or III.</b>
	/// The constructor writes <c>group == wanted</c> into all three arrays with <c>wanted</c> fixed
	/// at 0 for a weapon and -1 for a pod, so the other two groups start empty and only the chain
	/// controls ever put anything in them.</para>
	/// </summary>
	/// <param name="hardpoints">The chassis' own <c>gl\&lt;HERC&gt;.GL</c>, or null if it has none.</param>
	/// <param name="loadout">The fit, with its two parallel arrays addressed by hardpoint slot.</param>
	/// <param name="catalog">The weapon tables, or null when they could not be read.</param>
	/// <param name="modelCellCount">
	/// How many cells a weapon model's muzzle-flash flipbook has, by its
	/// <c>dts\MECHWPNS.DTS</c> shape index — see <see cref="WeaponMount.FlashCellCount"/>. Null on
	/// a host with no model library, which leaves every mount flashless and every ELF firing on the
	/// press.
	/// </param>
	public static WeaponMounts Build(GunLayout? hardpoints, MechLoadout loadout, WeaponCatalog? catalog,
			Func<int, int>? modelCellCount = null) {
		if (hardpoints?.Hardpoints is not { } records || catalog == null) {
			return Empty;
		}

		int total = Math.Min(hardpoints.TotalGuns, records.Length);
		var manager = new WeaponMounts(new WeaponMount?[total]);

		for (int i = 0; i < total; i++) {
			var record = records[i];

			// A negative id is clamped to zero and a zero id builds nothing, so the slot stays a hole
			// in the array rather than shifting everything after it up.
			int weaponId = Math.Max(loadout.WeaponAt(record.LoadoutSlot), 0);
			if (weaponId == WeaponMount.EmptyWeaponId
				|| WeaponCatalog.Kind(weaponId) == WeaponMountKind.None) {
				continue;
			}

			manager._slots[i] = new WeaponMount(
				i, record, weaponId, loadout.SecondaryAt(record.LoadoutSlot), catalog, modelCellCount);
		}

		foreach (var mount in manager.Mounts) {
			if (mount.Kind == WeaponMountKind.Pod) {
				continue;
			}

			manager._groups[0][mount.MountIndex] = true;
			if (manager.Selected == NoSelection) {
				manager.Selected = mount.MountIndex;
			}
		}

		return manager;
	}

	/// <summary>
	/// A press on cockpit weapon row <paramref name="gaugeSlot"/>, dispatched the way the original
	/// dispatches it: <b>by the row's gauge class</b>, not by a switch on what the mount is.
	///
	/// <list type="bullet">
	/// <item>An energy or ammunition row (<c>EnergyWeaponGauge_OnChildClick</c> (<c>00440ef0</c>) / <c>AmmoWeaponGauge_OnChildClick</c> (<c>004414b4</c>)) branches on the
	/// mouse-button bit its <c>GetValue</c> slot handed it: the left button arms the mount, the right
	/// button toggles its membership of the current fire chain.</item>
	/// <item>An ECM or Turbo row (<c>TogglePodGauge_OnClick</c>, <c>004419fc</c>) flips its button
	/// byte. <b>It never looks at which button pressed it</b> — the slot takes only the gauge and the
	/// child — so a right press toggles the pod exactly as a left one does, and neither arms nor
	/// chains anything.</item>
	/// <item>A Shield, Targeting or Energy pod row (<c>PodGauge_OnClick</c>, <c>00441988</c>) marks
	/// itself for repaint and returns. It clicks, it sounds, and nothing changes.</item>
	/// </list>
	///
	/// <para>Both the mouse and the number keys arrive here. <c>CockpitWidgets_HandleCommand</c>
	/// (<c>00432bc8</c>) answers codes <c>0x02</c>-<c>0x0b</c> by calling
	/// <c>WeaponMounts_SelectByGauge</c> on the gauge <i>and</i> pressing its select gadget with the
	/// left-button bit, so a number key runs the arm twice over on a weapon row and reaches the pod
	/// toggle on a pod row. <c>[Alt]</c> and a number is a different command bank entirely
	/// (<c>0x202</c>-<c>0x20b</c>), answered by the weapon manager rather than by the gauge, which is
	/// why it chains and never toggles a pod.</para>
	/// </summary>
	/// <param name="gaugeSlot">The row pressed, zero-based — the number it prints minus one.</param>
	/// <param name="rightButton">Whether the right button completed the press.</param>
	/// <returns>Whether the press changed anything.</returns>
	public bool PressRow(int gaugeSlot, bool rightButton = false) {
		if (BySlot(gaugeSlot) is { Kind: WeaponMountKind.Pod } pod) {
			if (!pod.HasPodButton) {
				return false;
			}

			pod.PodButton = !pod.PodButton;
			return true;
		}

		return rightButton ? ToggleChain(gaugeSlot) : SelectBySlot(gaugeSlot);
	}

	/// <summary>
	/// Arm the mount that owns cockpit weapon row <paramref name="gaugeSlot"/> — what a left click on
	/// that row and the matching number key both do. The original reaches this by two different
	/// routes that meet in <c>WeaponMounts_SelectByGauge</c> (<c>004106ac</c>): a click goes through the row gadget
	/// (<c>EnergyWeaponGauge_OnChildClick</c> (<c>00440ef0</c>)/<c>AmmoWeaponGauge_OnChildClick</c> (<c>004414b4</c>) → <c>CockpitView_ArmWeaponByGauge</c> (<c>00432a50</c>)), and a number key indexes the
	/// cockpit's own ten-gauge array at <c>CockpitViewInstance+0x70</c> and presses that gauge's
	/// select gadget.
	/// </summary>
	/// <returns>Whether anything was armed.</returns>
	public bool SelectBySlot(int gaugeSlot) =>
		BySlot(gaugeSlot) is { } mount && Select(mount.MountIndex);

	/// <summary>
	/// <c>WeaponMounts_SelectByGauge</c> (<c>004106ac</c>): arm one mount. Refused for a mount that is not
	/// <see cref="WeaponMount.Selectable"/> — a pod, or a weapon out of ammunition — which is why
	/// clicking a pod's row does nothing. A successful arm sets <see cref="SingleFire"/>.
	/// </summary>
	public bool Select(int mountIndex) {
		if (_slots.ElementAtOrDefault(mountIndex) is not { Selectable: true }) {
			return false;
		}

		SetSelected(mountIndex);
		SingleFire = true;
		return true;
	}

	/// <summary>
	/// <c>WeaponMounts_SetSelection</c> (<c>00410708</c>): the one place <see cref="Selected"/> is written.
	///
	/// <para>It normalises a linked pair to its first half — arming the right-hand weapon of a linked
	/// pair arms the left-hand one instead (the partner offset is negative on the second half). That
	/// is what keeps a pair's two rows agreeing about which of them is the armed one.</para>
	/// </summary>
	private void SetSelected(int mountIndex) {
		if (mountIndex == Selected) {
			return;
		}

		if (_slots.ElementAtOrDefault(mountIndex) is { Linked: true } mount && mount.LinkPartnerOffset < 0) {
			mountIndex += mount.LinkPartnerOffset;
		}

		Selected = mountIndex;
		ElectroOpticalFired = false;
	}

	/// <summary>
	/// <c>DAT_004d25ac</c> — the armed mount launched an electro-optical round and the selection has
	/// not moved since. Set by <see cref="FireTick"/> beside <see cref="SimWorld.MissileFlown"/> and
	/// cleared by any change of selection; while it is set and no round is being flown,
	/// <see cref="PerFrameUpdate"/> steps the selection on. See docs/simulation/weapon-firing.md.
	/// </summary>
	public bool ElectroOpticalFired { get; private set; }

	/// <summary>
	/// <c>WeaponMounts_StepSelection</c> (<c>0041074c</c>): step the armed mount one place through the current fire chain —
	/// <c>[W]</c> forward, <c>[Alt]+[W]</c> back. Wraps, and skips anything that is not selectable,
	/// not in the current chain, or the second half of a linked pair (a pair is armed by its first
	/// half only). Stepping clears <see cref="SingleFire"/>: the chain has the selection again.
	/// </summary>
	public void CycleSelection(int direction) {
		StepSelection(direction);

		// The original clears the flag at the key handler rather than inside the step, which is why
		// the two internal callers below — the chain switch and the per-frame advance — leave it
		// alone. Only the player stepping the weapon by hand gives the chain the selection back.
		SingleFire = false;
	}

	private void StepSelection(int direction) {
		if (Selected < 0 || _slots.Length == 0) {
			return;
		}

		int index = Selected;
		bool found = false;
		do {
			index += direction;
			if (index < 0) {
				index = _slots.Length - 1;
			} else if (index >= _slots.Length) {
				index = 0;
			}

			if (_slots[index] is { Selectable: true } mount
				&& (!mount.Linked || mount.LinkPartnerOffset > 0)) {
				found = InCurrentGroup(index);
			}
		} while (!found && index != Selected);

		SetSelected(index);
	}

	/// <summary>
	/// <c>WeaponMounts_ToggleChainMember</c> (<c>004110ac</c>) with <c>WeaponMounts_XorChainBit</c> (<c>00410cd0</c>): add or remove the mount on cockpit weapon row
	/// <paramref name="gaugeSlot"/> from the <i>current</i> fire chain — a right click on the row, or
	/// <c>[Alt]</c> and its number key. It toggles membership rather than setting it, and it does not
	/// arm anything.
	///
	/// <para>A hardpoint whose weapon has no range is skipped outright, so the pods on the panel
	/// cannot be chained — see <see cref="WeaponMount.Range"/>.</para>
	/// </summary>
	/// <returns>Whether a mount was found to toggle.</returns>
	public bool ToggleChain(int gaugeSlot) {
		var mount = Mounts.FirstOrDefault(m => m.GaugeSlot == gaugeSlot && m.Range > 0);
		if (mount == null || Group < 0 || Group >= _groups.Length) {
			return false;
		}

		_groups[Group][mount.MountIndex] = !_groups[Group][mount.MountIndex];

		// The original follows the toggle by asking the mount for its arbitration priority and, when
		// that is zero, calling its "wake up" slot. Only an energy mount implements either, and only
		// one whose charge target had been zeroed is changed by it.
		if (mount.EnergyPriority == 0) {
			mount.WakeCapacitor();
		}

		return true;
	}

	/// <summary>
	/// <c>WeaponMounts_ToggleLink</c> (<c>00410f14</c>): toggle link fire on the armed mount and its partner — the LINK button and
	/// <c>[L]</c>. Both halves flip together and only when <see cref="CanLink"/> holds, so a weapon
	/// with no symmetric twin, or one whose opposite hardpoint carries something else, simply cannot
	/// be linked.
	///
	/// <para>In the original one press runs this three times — once from the button's own click
	/// handler, once from the manager's next per-frame pass reading the button's latch byte, and once
	/// more as that pass writes the byte back and the widget notices it changed. Three flips of one
	/// bit is one flip, which is why it works; the net effect is what is reproduced here.</para>
	/// </summary>
	/// <returns>Whether the link state changed.</returns>
	public bool ToggleLink() {
		if (_slots.ElementAtOrDefault(Selected) is not { } armed
			|| PartnerOf(armed) is not { } partner
			|| partner.WeaponId != armed.WeaponId) {
			return false;
		}

		armed.Linked = !armed.Linked;
		partner.Linked = !partner.Linked;
		return true;
	}

	/// <summary>
	/// <c>WeaponMounts_SetChain</c> (<c>00410ae4</c>): switch the chain the console's chain button names. Switching to a chain
	/// the armed weapon is not in steps the selection to one that is.
	/// </summary>
	public void SetGroup(int group) {
		if (group == Group || group < 0 || group >= _groups.Length) {
			return;
		}

		Group = group;
		if (Selected >= 0 && !InCurrentGroup(Selected)) {
			StepSelection(1);
		}
	}

	/// <summary>
	/// The manager's own per-frame pass, <c>WeaponMounts_PerFrameUpdate</c> (<c>00410b40</c>) and
	/// <c>WeaponMounts_AdvanceToReady</c> (<c>00410a3c</c>), minus the input block it reads (the host
	/// drives those directly) and the auto-fire it performs.
	///
	/// <list type="bullet">
	/// <item><b>The chain advances the armed weapon.</b> Unless <see cref="SingleFire"/> is set, a
	/// mount that could not fire hands the selection to the next one in the chain that could. This is
	/// what makes a chain a chain.</item>
	/// <item><b>A destroyed mount breaks its link.</b> A linked pair whose half is destroyed or out
	/// of ammunition unlinks both halves and hands the selection to the partner.</item>
	/// </list>
	///
	/// <para>The range comes in first, before the advance, exactly as the original orders it: a
	/// weapon that cannot reach the selected target is not ready, so the chain steps past it and its
	/// row lights red. The armed weapon still fires — <see cref="FireTick"/> asks the mount alone
	/// and never consults this.</para>
	///
	/// <para><b>An electro-optical round holds the chain.</b> While one is being flown
	/// (<paramref name="missileFlown"/>) the advance does not run at all — <c>WeaponMounts_ChainReady</c>
	/// (<c>00410a04</c>) answers ready without asking, as it does for <see cref="SingleFire"/>. Once
	/// the flight is over, <see cref="ElectroOpticalFired"/> steps the selection on by one and gives
	/// the chain the selection back, before anything else in the pass.</para>
	/// </summary>
	/// <param name="targetRange">
	/// Distance to the selected target in world units, or zero when nothing is selected — see
	/// <see cref="TargetRange"/>.
	/// </param>
	/// <param name="missileFlown"><see cref="SimWorld.MissileFlown"/>.</param>
	public void PerFrameUpdate(int targetRange = 0, bool missileFlown = false) {
		if (ElectroOpticalFired && !missileFlown) {
			StepSelection(1);
			SingleFire = false;
		}

		TargetRange = targetRange;
		foreach (var mount in Mounts.ToList()) {
			if (!mount.Linked || PartnerOf(mount) is not { } partner) {
				continue;
			}

			if (mount.Disabled || !mount.Selectable) {
				mount.Linked = false;
				partner.Linked = false;
				SetSelected(partner.MountIndex);
			}
		}

		if (Selected < 0 || SingleFire || missileFlown || CanFireNow(Selected)) {
			return;
		}

		int armed = Selected;
		for (int steps = 0; steps <= _slots.Length; steps++) {
			StepSelection(1);
			if (CanFireNow(Selected)) {
				return;
			}

			if (Selected == armed) {
				break;
			}
		}

		Selected = armed;
	}

	/// <summary>
	/// <c>WeaponMounts_MountIsReady</c> (<c>00410970</c>): the mount's own readiness, gated on the
	/// selected target's range, and for a linked pair, both halves'. One predicate serves two
	/// purposes — it is the flag a row's state box is lit green or red by
	/// (<see cref="Herculan.Engine.Content.WeaponRowState.Ready"/>) and the test
	/// <see cref="PerFrameUpdate"/> steps the chain past.
	///
	/// <para>The range gate is <see cref="WeaponMount.RangeAllows"/> against
	/// <see cref="TargetRange"/>, and it is skipped outright when that is zero — which is what the
	/// original does with no target selected, and what stops every row going red when nothing is
	/// locked.</para>
	///
	/// <para><b>Not ported:</b> the original's third gate, which requires a launcher's subtype to hold
	/// missile lock (<see cref="MissileLock"/>) before its row counts as ready. See
	/// docs/simulation/weapon-mounts.md.</para>
	/// </summary>
	/// <param name="followLink">
	/// The original's fourth argument. Clear on the outer call and set on the recursion into the link
	/// partner, so a pair is tested once each way round rather than endlessly.
	/// </param>
	public bool CanFireNow(int mountIndex, bool followLink = false) {
		if (_slots.ElementAtOrDefault(mountIndex) is not { } mount || !mount.CanFire) {
			return false;
		}

		if (TargetRange != 0 && !mount.RangeAllows(TargetRange)) {
			return false;
		}

		if (followLink || !mount.Linked || PartnerOf(mount) is not { } partner) {
			return true;
		}

		return CanFireNow(partner.MountIndex, followLink: true);
	}

	/// <summary>
	/// <c>WeaponMounts_FireTrigger</c> (<c>00410dbc</c>) — the manager's fire entry, and the whole of what pulling the trigger does.
	///
	/// <para>Its order is worth keeping: <b>both halves of a linked pair are tested before either
	/// fires</b>, so a pair whose second half is still charging does not fire its first half alone.
	/// The same goes for the trigger itself, which is asked of each mount separately (vtable
	/// <c>+0x30</c>) even though every mount answers from the same device byte.</para>
	///
	/// <para>Firing clears <see cref="SingleFire"/> the moment the armed mount is no longer ready,
	/// which is what "once you fire, the current firing chain will resume" means in the manual: a
	/// weapon armed by hand keeps the selection until its shot leaves it unready, and then the chain
	/// takes over again on the next <see cref="PerFrameUpdate"/>.</para>
	///
	/// <para>When the armed mount's <see cref="WeaponMount.AmmoType"/> is
	/// <see cref="Rocket.PlayerFlownSubtype"/> — it has just launched an electro-optical round — the
	/// shot raises <see cref="ElectroOpticalFired"/> and <see cref="SimWorld.MissileFlown"/>, so the
	/// controls go to the round from the next tick. Only the locally piloted machine's trigger reaches
	/// this in the original, which is what the owner test stands for here.</para>
	/// </summary>
	/// <param name="owner">The machine firing, which the shot's geometry and the raycast both need.</param>
	/// <param name="world">The world the shot is resolved against.</param>
	/// <param name="triggerHeld">
	/// The device's fire byte — see <see cref="MechControls.Fire"/>. It reaches each mount's own
	/// trigger slot rather than being tested here, because an ELF's slot answers a spin-up rather
	/// than the byte: see <see cref="WeaponMount.TriggerHeld"/>.
	/// </param>
	/// <returns>Whether anything fired.</returns>
	public bool FireTick(MechObject owner, SimWorld world, bool triggerHeld) {
		if (_slots.ElementAtOrDefault(Selected) is not { } armed) {
			return false;
		}

		var partner = armed.Linked ? PartnerOf(armed) : null;

		if (!armed.CanFire || (partner != null && !partner.CanFire)) {
			return false;
		}

		// Vtable +0x30, asked of the armed mount and only then of its partner — the original's own
		// short circuit, which matters because the ELF's slot has side effects: a partner that is
		// never asked never spins up. Every other class hands the byte straight back.
		if (!armed.TriggerHeld(triggerHeld) || (partner != null && !partner.TriggerHeld(triggerHeld))) {
			return false;
		}

		// The free-shot flag off UnlimitedAmmoFlag (004a9edc). The original builds it from the globals alone, because
		// the only caller of this is the player's own trigger poll; here the trigger path runs for
		// every machine, so the owner is tested for what that caller guarantees.
		bool freeShot = owner.LocallyPiloted && world.UnlimitedAmmunition;

		armed.Fire(owner, world, freeShot);

		if (owner.LocallyPiloted && armed.AmmoType == Rocket.PlayerFlownSubtype) {
			ElectroOpticalFired = true;
			world.MissileFlown = true;
		}

		partner?.Fire(owner, world, freeShot);

		// The original re-tests the armed mount after each of the two shots, and the partner's shot
		// cannot change the armed mount's readiness, so the two tests are one.
		if (!armed.CanFire) {
			SingleFire = false;
		}

		return true;
	}

	/// <summary>
	/// <c>WeaponMounts_HandleCommand</c>'s <c>0x0c</c>/<c>0x0d</c>/<c>0x4a</c>/<c>0x4e</c> cases — the
	/// <c>[-]</c> and <c>[=]</c> keys and the keypad's <c>[-]</c> and <c>[+]</c>, which reach the armed
	/// mount's vtable <c>+0x38</c> with the "up" half of the pair as a flag. Only an energy mount has
	/// anything in that slot; see <see cref="WeaponMount.AdjustPower"/> for what it does.
	/// </summary>
	/// <returns>Whether a mount took the command.</returns>
	public bool AdjustPower(bool raise) {
		if (_slots.ElementAtOrDefault(Selected) is not { } armed) {
			return false;
		}

		armed.AdjustPower(raise);
		return true;
	}

	/// <summary>
	/// A drag released on weapon row <paramref name="gaugeSlot"/>'s charge bar, under
	/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/> — see
	/// <see cref="WeaponMount.SetPowerFromChargeBar"/>. Unlike the keys, which reach only the armed
	/// mount, the bar sets the power level of whichever row it belongs to.
	/// </summary>
	/// <returns>Whether a mount took it.</returns>
	public bool SetPowerFromChargeBar(int gaugeSlot, int position) =>
		BySlot(gaugeSlot)?.SetPowerFromChargeBar(position) ?? false;

	/// <summary>
	/// The mounts' claim on the Master Energy Pool — vtable slot 0 of the manager,
	/// <c>WeaponMounts_ArbitrateEnergy</c> (<c>004107e4</c>), called from <c>Mech_PerTickSystemsUpdate</c> between the reactor's
	/// contribution and the shields'.
	///
	/// <para>Mounts are served one at a time, and the order is not the mount order:</para>
	/// <list type="number">
	/// <item>The armed mount goes first, before the ranking is consulted at all. An AI machine
	/// passes -1 for it and starts straight at the ranking.</item>
	/// <item>Every pass after that picks the highest-priority mount not yet served — priority being
	/// <see cref="WeaponMount.EnergyPriority"/>, which a mount mid-charge reports as 10000 so that it
	/// finishes before anything else starts.</item>
	/// <item>Once any mount reports itself mid-charge, every mount served after it this tick is told
	/// to target zero instead and bleeds its own capacitor back into the pool.</item>
	/// </list>
	///
	/// <para><b>The burst follow-up fires from here</b>, not from the trigger: a mount served this
	/// pass that reports <see cref="WeaponMount.AutoFireDue"/> is dispatched again immediately, with
	/// no trigger read and no readiness test. Only the charge-up gun branch ever arms that, and only
	/// on <c>EMP2</c>, so this is the second of that weapon's two volleys and nothing else.</para>
	///
	/// <para><b>The energy half of the unlimited-ammunition cheat is here</b>, and it is a refund
	/// rather than a suspension: the mounts are served and charged exactly as they otherwise would
	/// be, and then the budget the pool gets back is the one it came in with, so a player machine
	/// with the cheat set never spends reactor energy on its guns. See
	/// <see cref="SimWorld.UnlimitedAmmunition"/>.</para>
	/// </summary>
	/// <param name="budget">The pool less its reserve.</param>
	/// <param name="selected">
	/// The armed mount to serve first, or <see cref="NoSelection"/> to go straight to the ranking.
	/// </param>
	/// <param name="owner">The machine these mounts belong to, for the follow-up shot's geometry.</param>
	/// <param name="world">The world a follow-up shot is resolved against.</param>
	/// <returns>What the mounts did not take.</returns>
	public short ChargeTick(short budget, int selected, MechObject owner, SimWorld world) {
		if (_slots.Length == 0) {
			return budget;
		}

		bool freeShot = owner.LocallyPiloted && world.UnlimitedAmmunition;
		short unspent = budget;

		var served = new bool[_slots.Length];
		bool yieldToOther = false;

		for (int pass = 0; pass < _slots.Length; pass++) {
			WeaponMount? next;

			if (selected >= 0 && selected < served.Length && !served[selected]) {
				next = _slots[selected];
			} else {
				next = null;
				for (int i = 0; i < _slots.Length; i++) {
					if (_slots[i] is { } candidate && !served[i]
						&& (next == null || candidate.EnergyPriority > next.EnergyPriority)) {
						next = candidate;
					}
				}
			}

			if (next == null) {
				break;
			}

			served[next.MountIndex] = true;
			budget = next.ChargeTick(budget, yieldToOther);

			if (next.AutoFireDue) {
				next.Fire(owner, world, freeShot);
			}

			yieldToOther |= next.Charging;
		}

		return freeShot ? unspent : budget;
	}
}
