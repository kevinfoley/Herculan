using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace Herculan.Engine.Shell;

/// <summary>
/// One machine in a hangar bay, as the shell's screens read it — <c>Hangar_BayRecords</c> (<c>00482ac3</c>)'s eight pointers,
/// each to the 122-byte HERC record in the loaded save (docs/retail/formats/save-games.md).
///
/// <para><b>The 66-byte status block is three arrays and one accessor.</b> <c>HercStatus_Get</c>
/// (<c>HercStatus_Get</c>, <c>00411d06</c>) takes a mode and an index and every screen in the shell reads a machine's
/// damage through it, so <see cref="Condition"/> is the whole of what the repair bay needs.
/// <see cref="ShellRepairCategory.ExternalGroup"/> is the one that is not a plain array read: the 13
/// external entries are facets and a group is their mean, taken through the six-group table at
/// <c>0046f8a4</c>.</para>
/// </summary>
public sealed class ShellBayMachine {
	/// <summary>
	/// <c>HercComponentGroupTable</c> (<c>0046f8a4</c>) — which of the 13 external facets each of the
	/// six named groups covers. The six partition all 13 exactly once, which is what makes the group
	/// the granularity the repair bay and the scrap screen price at.
	/// </summary>
	public static readonly int[][] ExternalGroupFacets = {
		new[] { 0, 1 },        // Cockpit: front, rear
		new[] { 2, 4 },        // Left Torso: front, rear
		new[] { 3, 5 },        // Right Torso: front, rear
		new[] { 6 },           // Chassis
		new[] { 7, 9, 11 },    // Left Leg: thigh, calf, foot
		new[] { 8, 10, 12 },   // Right Leg: thigh, calf, foot
	};

	/// <summary>
	/// The four internals <c>Herc_IsFlightworthy</c> (<c>00411681</c>) tests for a machine to count as deployable: both leg
	/// servos, the engine and life support. See <see cref="IsFlightworthy"/>.
	/// </summary>
	private static readonly int[] FlightworthyInternals = { 0, 1, 5, 8 };

	/// <summary>The condition each of those four must be strictly above.</summary>
	private const int FlightworthyFloor = 50;

	/// <summary>A machine is deliverable only at 100% built; anything less is still in the workshop.</summary>
	private const int Complete = 100;

	/// <summary>The mount slots a record carries, whatever its capacity — <c>HercRecord_Ctor</c> (<c>00410d7a</c>) clears ten.</summary>
	private const int MountSlots = 10;

	private readonly short[] _external;
	private readonly short[] _internal;
	private readonly short[] _hardpoint;
	private readonly ShellWeaponUnit?[] _mounts;

	private ShellBayMachine(int chassisType, int mountCapacity, int buildPercent, int buildMissionsLeft, short[] external,
			short[] internals, short overall, short[] hardpoint, ShellWeaponUnit?[] mounts) {
		ChassisType = chassisType;
		MountCapacity = mountCapacity;
		BuildPercent = buildPercent;
		BuildMissionsLeft = buildMissionsLeft;
		_external = external;
		_internal = internals;
		_overall = overall;
		_hardpoint = hardpoint;
		_mounts = mounts;
	}

	/// <summary>
	/// The status block's tenth internal entry, the machine's overall condition: the debrief sets it and
	/// nothing in the shell's screens reads it, so it only travels, into the <c>player.mec</c> export.
	/// </summary>
	private short _overall;

	/// <summary>
	/// <c>HercStatus_Get(block, 1, 9)</c> — the overall-condition slot as it stands, which the debrief copies
	/// into the pilot's condition. Not <see cref="OverallCondition"/>, the mean the shell computes.
	/// </summary>
	public int OverallSlot => _overall;

	/// <summary>
	/// The chassis type, 0-8. The record carries it twice and the two coincide — <c>+0x02</c> is
	/// <c>+0x00</c> through an identity map — and this is the one the stat tables, the cost tables and
	/// the <c>estext.bin</c> name are all indexed by.
	/// </summary>
	public int ChassisType { get; }

	/// <summary>The mount capacity at <c>+0x4c</c>: how many hardpoint rows the repair list offers.</summary>
	public int MountCapacity { get; }

	/// <summary>Build progress at <c>+0x4a</c>. Construction state, not damage.</summary>
	public int BuildPercent { get; private set; }

	/// <summary>
	/// <c>+0x78</c>, the missions of construction left: <c>herc_inf.dat</c>'s build time when the chassis
	/// is ordered, and what <c>Herc_BuildTick</c> (<c>00411086</c>) counts down at each debrief.
	/// </summary>
	public int BuildMissionsLeft { get; private set; }

	/// <summary>
	/// <c>Herc_BuildTick</c> (<c>00411086</c>) — one mission of construction: <see cref="BuildMissionsLeft"/> down
	/// by one and <see cref="BuildPercent"/> recomputed against the chassis's <c>herc_inf.dat</c> build time,
	/// <paramref name="buildTime"/>, as <c>((buildTime - left) * 100) / buildTime</c>.
	/// </summary>
	public void BuildTick(int buildTime) {
		BuildMissionsLeft--;
		BuildPercent = (short)((buildTime - BuildMissionsLeft) * 100 / buildTime);
	}

	/// <summary>
	/// <c>Herc_ReadStatusBlock</c> (<c>00411720</c>) — the 66 bytes <c>results.dat</c> carries for this machine
	/// read straight over the status block: 13 external facets, the nine internals and the overall slot,
	/// then ten hardpoints. Every mount whose hardpoint arrived at 0 is then destroyed, its hardpoint set
	/// back to 100.
	/// </summary>
	public void ReadStatusBlock(byte[] block) {
		int at = 0;
		short Next() {
			short value = at + 2 <= block.Length ? BitConverter.ToInt16(block, at) : (short)0;
			at += 2;
			return value;
		}

		for (int i = 0; i < _external.Length; i++) {
			_external[i] = Next();
		}

		for (int i = 0; i < _internal.Length; i++) {
			_internal[i] = Next();
		}

		_overall = Next();
		for (int i = 0; i < _hardpoint.Length; i++) {
			_hardpoint[i] = Next();
		}

		for (int slot = 0; slot < _mounts.Length; slot++) {
			if (_mounts[slot] != null && Condition(ShellRepairCategory.Hardpoint, slot) == 0) {
				_mounts[slot] = null;
				SetCondition(ShellRepairCategory.Hardpoint, slot, Complete);
			}
		}
	}

	/// <summary>The status block's length in <c>results.dat</c> and in the record, <c>0x42</c>.</summary>
	public const int StatusBlockLength = 0x42;

	/// <summary><c>HercStatus_Set(block, 1, 9, value)</c> — the overall-condition slot.</summary>
	internal void SetOverallSlot(int value) => _overall = (short)value;

	/// <summary>Whether the machine has been delivered — <c>HercList_IsBuilt</c> (<c>00410a64</c>)'s test.</summary>
	public bool IsBuilt => BuildPercent == Complete;

	/// <summary>
	/// <c>Herc_IsFlightworthy</c> (<c>00411681</c>) — whether the machine can be flown: both leg servos, the engine and life
	/// support all above 50. A machine that fails this is still in its bay but does not count towards
	/// the fleet the scrap gate measures.
	/// </summary>
	public bool IsFlightworthy {
		get {
			foreach (int component in FlightworthyInternals) {
				if (Condition(ShellRepairCategory.Internal, component) <= FlightworthyFloor) {
					return false;
				}
			}

			return true;
		}
	}

	/// <summary>
	/// <c>HercStatus_Get(block, category, index)</c>. An index the machine does not have reads 100,
	/// which is what an absent machine reads throughout the repair screen too.
	/// </summary>
	public int Condition(ShellRepairCategory category, int index) {
		if (index < 0) {
			return Complete;
		}

		switch (category) {
			case ShellRepairCategory.ExternalGroup:
				return index < ExternalGroupFacets.Length ? GroupMean(index) : Complete;
			case ShellRepairCategory.Internal:
				return index < _internal.Length ? _internal[index] : Complete;
			default:
				return index < _hardpoint.Length ? _hardpoint[index] : Complete;
		}
	}

	/// <summary>
	/// <c>HercStatus_Set(block, category, index, value)</c> (<c>00411cbd</c>), <c>HercStatus_Get</c>'s
	/// setter. An external group is written through <c>HercStatus_SetGroup</c> (<c>00411c78</c>), which
	/// puts the value in every facet the group covers, so the group's mean reads it back exactly.
	/// </summary>
	public void SetCondition(ShellRepairCategory category, int index, int value) {
		if (index < 0) {
			return;
		}

		switch (category) {
			case ShellRepairCategory.ExternalGroup:
				if (index < ExternalGroupFacets.Length) {
					foreach (int facet in ExternalGroupFacets[index]) {
						if (facet < _external.Length) {
							_external[facet] = (short)value;
						}
					}
				}

				break;
			case ShellRepairCategory.Internal:
				if (index < _internal.Length) {
					_internal[index] = (short)value;
				}

				break;
			default:
				if (index < _hardpoint.Length) {
					_hardpoint[index] = (short)value;
				}

				break;
		}
	}

	/// <summary>
	/// <c>Repair_Apply</c> (<c>004113af</c>) — <paramref name="target"/> into all 13 external facets, all
	/// nine internals and each mount slot below the capacity, except that an empty slot is set to 100.
	/// A fitted mount at 0 is written too, although <see cref="ShellRepairCosts.HercCost"/> bills nothing for it.
	/// </summary>
	public void ApplyRepair(int target) {
		Array.Fill(_external, (short)target);
		Array.Fill(_internal, (short)target);
		for (int slot = 0; slot < MountCapacity && slot < _hardpoint.Length; slot++) {
			_hardpoint[slot] = WeaponAt(slot) == 0 ? (short)Complete : (short)target;
		}
	}

	/// <summary>
	/// The whole 66-byte status block as <c>PlayerMec_WriteEntry</c> (<c>004106b7</c>) copies it out of the record's <c>+0x08</c> span
	/// into <c>player.mec</c>: the 13 external facets, the ten internal entries — the nine components and
	/// the overall condition — and the ten hardpoints, every one an <c>int16</c>.
	/// </summary>
	public (byte[] External, byte[] Internal, byte[] Hardpoint) StatusBlock() {
		static byte[] Bytes(IEnumerable<short> values) => values.SelectMany(BitConverter.GetBytes).ToArray();
		return (Bytes(_external), Bytes(_internal.Append(_overall)), Bytes(_hardpoint));
	}

	/// <summary>A copy of the status block's three arrays, for <see cref="RestoreStatus"/> to put back.</summary>
	public ShellMachineStatus CaptureStatus() =>
		new((short[])_external.Clone(), (short[])_internal.Clone(), (short[])_hardpoint.Clone());

	/// <summary>Puts back a status block <see cref="CaptureStatus"/> took, whichever machine it was taken from.</summary>
	public void RestoreStatus(ShellMachineStatus status) {
		Array.Copy(status.External, _external, Math.Min(status.External.Length, _external.Length));
		Array.Copy(status.Internal, _internal, Math.Min(status.Internal.Length, _internal.Length));
		Array.Copy(status.Hardpoint, _hardpoint, Math.Min(status.Hardpoint.Length, _hardpoint.Length));
	}

	/// <summary>
	/// The weapon id fitted in one mount slot, or 0 for an empty slot — the first <c>int16</c> of the
	/// mount record at <c>+0x50 + slot*4</c>, which every screen reads the same way.
	/// </summary>
	public int WeaponAt(int slot) => Mount(slot)?.WeaponId ?? 0;

	/// <summary>The unit fitted in one mount slot — the pointer at <c>+0x50 + slot*4</c> — or null for an empty slot.</summary>
	public ShellWeaponUnit? Mount(int slot) => slot >= 0 && slot < _mounts.Length ? _mounts[slot] : null;

	internal void SetMount(int slot, ShellWeaponUnit? unit) {
		if (slot >= 0 && slot < _mounts.Length) {
			_mounts[slot] = unit;
		}
	}

	/// <summary>
	/// <c>HercStatus_OverallCondition</c> (<c>00411bd4</c>) — all 13 external facets, the nine
	/// internals and one entry per mount slot up to the capacity, over <c>capacity + 22</c>. It is
	/// called with <c>+0x4c</c>, so an empty slot's 100 counts towards the mean.
	/// </summary>
	public int OverallCondition {
		get {
			int total = 0;
			foreach (short facet in _external) {
				total += facet;
			}

			foreach (short component in _internal) {
				total += component;
			}

			for (int slot = 0; slot < MountCapacity; slot++) {
				total += Condition(ShellRepairCategory.Hardpoint, slot);
			}

			return total / (MountCapacity + _external.Length + _internal.Length);
		}
	}

	/// <summary>
	/// <c>HercStatus_GroupMean</c> (<c>00411c29</c>) — a group's facets summed and divided by how many
	/// there are, integer division as the original does it.
	/// </summary>
	private int GroupMean(int group) {
		int[] facets = ExternalGroupFacets[group];
		int total = 0;
		foreach (int facet in facets) {
			total += facet < _external.Length ? _external[facet] : Complete;
		}

		return total / facets.Length;
	}

	/// <summary>
	/// A machine just bought — <c>Herc_Order</c> (<c>00411019</c>) over the record <c>HercRecord_Ctor</c>
	/// (<c>00410d7a</c>) leaves: every condition 100, no weapon in any mount, the chassis type, the
	/// capacity <c>Herc_CapacityForType</c> (<c>00410d54</c>) reads, 0% built, and
	/// <paramref name="buildMissions"/>, the chassis's <c>herc_inf.dat</c> build time, left to go.
	/// </summary>
	public static ShellBayMachine Ordered(int chassisType, int buildMissions) {
		static short[] Full(int length) => Enumerable.Repeat((short)Complete, length).ToArray();

		int capacity = HercLUT.GetById((short)chassisType)?.HardpointMax ?? -1;
		return new ShellBayMachine(chassisType, capacity, 0, buildMissions, Full(HercExternals.Values().Count),
			Full(DamageRepairCost.InternalCount), Complete, Full(MountSlots), new ShellWeaponUnit?[MountSlots]);
	}

	/// <summary>
	/// A machine of <paramref name="chassisType"/> with nothing fitted — <c>HercRecord_Ctor</c>
	/// (<c>00410d7a</c>) then <c>Herc_SetType</c> (<c>00410fde</c>): built with no missions left, every
	/// condition 100, and the capacity <c>Herc_CapacityForType</c> (<c>00410d54</c>) reads.
	/// <see cref="Fit"/> arms it.
	/// </summary>
	public static ShellBayMachine Delivered(int chassisType) {
		var machine = Ordered(chassisType, 0);
		return new ShellBayMachine(chassisType, machine.MountCapacity, Complete, 0, machine._external, machine._internal,
			machine._overall, machine._hardpoint, machine._mounts);
	}

	/// <summary>
	/// <c>Herc_FitNewUnit</c> (<c>004115c6</c>) — a new unit of <paramref name="weaponId"/> in <paramref name="slot"/> carrying
	/// <paramref name="guidance"/>, taken from no stock, and the hardpoint at 100. How a mission's own
	/// machine is armed.
	/// </summary>
	internal void Fit(int slot, int weaponId, int guidance) {
		SetMount(slot, new ShellWeaponUnit(weaponId, guidance: guidance));
		SetCondition(ShellRepairCategory.Hardpoint, slot, Complete);
	}

	/// <summary>
	/// <c>HercRecord_ReadCatalogForm</c> (<c>00410e79</c>) over a <c>gam\*.dat</c> record: the type, the
	/// build percentage and each listed mount, the unit's condition and guidance from the file and its
	/// fitted condition and the hardpoint's left at 100.
	/// </summary>
	public static ShellBayMachine FromCatalog(ShellHercData record) {
		var machine = Ordered(record.HercId, 0);
		var built = new ShellBayMachine(record.HercId, machine.MountCapacity, record.BuildPercent, record.BuildMissionsLeft,
			machine._external, machine._internal, machine._overall, machine._hardpoint, machine._mounts);
		foreach (var (slot, entry) in record.Hardpoints ?? new Dictionary<short, UiWeaponEntry>()) {
			built.SetMount(slot, new ShellWeaponUnit(entry.WeaponId, condition: entry.Condition,
				guidance: entry.Guidance?.Id ?? ShellWeaponUnit.NoGuidance));
		}

		return built;
	}

	/// <summary>Builds the view over one parsed bay record.</summary>
	public static ShellBayMachine From(HercBayEntry entry) {
		var external = new short[HercExternals.Values().Count];
		foreach (var facet in HercExternals.Values()) {
			external[facet.Id] = entry.ExternalConditions != null
				&& entry.ExternalConditions.TryGetValue(facet, out var part) ? part.Health : (short)Complete;
		}

		// Ids 0-8 are the nine named components; id 9 is the machine's overall condition and is not one
		// of them, so the repair list stops short of it.
		var internals = new short[DamageRepairCost.InternalCount];
		for (int i = 0; i < internals.Length; i++) {
			var component = HercInternals.GetById((short)i);
			internals[i] = component != null && entry.InternalConditions != null
				&& entry.InternalConditions.TryGetValue(component, out var part) ? part.Health : (short)Complete;
		}

		var hardpoint = new short[entry.HardpointConditions.Length];
		var mounts = new ShellWeaponUnit?[entry.HardpointConditions.Length];
		for (int slot = 0; slot < hardpoint.Length; slot++) {
			hardpoint[slot] = entry.HardpointConditions[slot]?.Health ?? (short)Complete;
			mounts[slot] = entry.Mounts.TryGetValue((short)slot, out var weapon) && weapon.Id != null
				? ShellWeaponUnit.From(weapon) : null;
		}

		short overall = entry.InternalConditions != null
			&& entry.InternalConditions.TryGetValue(HercInternals.Pilot, out var overallPart) ? overallPart.Health : (short)Complete;
		return new ShellBayMachine(entry.ChassisType?.Id ?? 0, entry.MountCapacity, entry.BuildPercent, entry.BuildMissionsLeft,
			external, internals, overall, hardpoint, mounts);
	}

	/// <summary>
	/// The record as <c>Herc_Write</c> (<c>0041123e</c>) serializes it: the type twice, the status block,
	/// the build state, the capacity, and each fitted mount below the capacity in slot order.
	/// </summary>
	internal HercBayEntry ToEntry() {
		var entry = new HercBayEntry {
			ChassisType = HercLUT.GetById((short)ChassisType),
			ChassisIndex = (short)ChassisType,
			ExternalConditions = HercExternals.Values().ToDictionary(facet => facet,
				facet => new ShellHercPart(facet.Id, facet.Label, _external[facet.Id])),
			InternalConditions = new Dictionary<HercInternals, ShellHercPart>(),
			BuildPercent = (short)BuildPercent,
			BuildMissionsLeft = (short)BuildMissionsLeft,
			MountCapacity = (short)MountCapacity,
		};

		for (int i = 0; i < _internal.Length; i++) {
			var component = HercInternals.GetById((short)i)!;
			entry.InternalConditions[component] = new ShellHercPart(component.Id, component.Label, _internal[i]);
		}

		entry.InternalConditions[HercInternals.Pilot] = new ShellHercPart(HercInternals.Pilot.Id, HercInternals.Pilot.Label, _overall);
		for (int slot = 0; slot < entry.HardpointConditions.Length; slot++) {
			short condition = slot < _hardpoint.Length ? _hardpoint[slot] : (short)Complete;
			entry.HardpointConditions[slot] = new ShellHercPart((short)slot, "hardpoint_" + slot, condition);
		}

		for (int slot = 0; slot < MountCapacity; slot++) {
			if (Mount(slot) is { } unit) {
				entry.Mounts[(short)slot] = unit.ToEntry();
			}
		}

		entry.MountsOccupied = (short)entry.Mounts.Count;
		return entry;
	}
}

/// <summary>
/// A copy of one machine's 66-byte status block — the three arrays <see cref="ShellBayMachine"/> keeps —
/// as the repair screen's CANCEL restores it.
/// </summary>
public sealed record ShellMachineStatus(short[] External, short[] Internal, short[] Hardpoint);
