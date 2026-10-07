namespace HercWorks.Core.Data.Struct.Herc;

/// <summary>
/// A HERC's internal components by index. Two index spaces share it:
///
/// <para>DBSIM's <c>.DMG</c> files carry 22 internal slots: 0-8 the nine components, 9 the pilot,
/// 10 and 11 the rear leg servos of a four-legged chassis, and 12-21 the ten weapon mounts' own
/// internals, maximum 0 in every file until a fitted weapon gives its slot one
/// (<c>docs/retail/formats/dmg-damage-file.md#the-two-index-spaces</c>).</para>
///
/// <para>The shell's HERC status block carries only ids 0-9: 0-8 the same nine components, and 9 the
/// machine's overall condition — the mean the debrief copies into the pilot's own condition — under
/// the <see cref="Pilot"/> entry. Shell readers and editors therefore stop at
/// <see cref="ServosLegLeftRear"/> (<c>docs/retail/formats/save-games.md#the-66-byte-status-block</c>).</para>
/// </summary>
public sealed class HercInternals {
	public static readonly HercInternals ServosLegLeft = new(0, "Left Leg Servos");
	public static readonly HercInternals ServosLegRight = new(1, "Right Leg Servos");
	public static readonly HercInternals SensorArray = new(2, "Sensor Array");
	public static readonly HercInternals TargComp = new(3, "Targeting Computer");
	public static readonly HercInternals ShieldGen = new(4, "Shield Generator");
	public static readonly HercInternals Engine = new(5, "Engine");
	public static readonly HercInternals Hydraulics = new(6, "Hydraulics");
	public static readonly HercInternals Stabilizers = new(7, "Stabilizers");
	public static readonly HercInternals LifeSupport = new(8, "Life Support");
	public static readonly HercInternals Pilot = new(9, "Pilot");
	public static readonly HercInternals ServosLegLeftRear = new(10, "Rear Left Leg Servos");
	public static readonly HercInternals ServosLegRightRear = new(11, "Rear Right Leg Servos");
	/// <summary>The first weapon mount's internal; fit slot <c>n</c>'s is this plus <c>n</c>.</summary>
	public const short FirstWeaponMountId = 12;

	/// <summary>
	/// Ids 12-21, one per weapon-mount fit slot: the internal a fitted weapon's piece lists behind
	/// its mount component (docs/retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece).
	/// </summary>
	public static readonly IReadOnlyList<HercInternals> WeaponMounts = Enumerable.Range(0, 10)
		.Select(slot => new HercInternals((short)(FirstWeaponMountId + slot), $"Weapon Mount {slot + 1}"))
		.ToArray();

	private static readonly IReadOnlyList<HercInternals> All = new[]
	{
		ServosLegLeft, ServosLegRight, SensorArray, TargComp, ShieldGen, Engine, Hydraulics,
		Stabilizers, LifeSupport, Pilot, ServosLegLeftRear, ServosLegRightRear
	}.Concat(WeaponMounts).ToArray();

	private static readonly Dictionary<short, HercInternals> ById = All.ToDictionary(e => e.Id);

	public short Id { get; }
	public string Label { get; }

	private HercInternals(short id, string label) {
		Id = id;
		Label = label;
	}

	public static HercInternals? GetById(short id) => ById.GetValueOrDefault(id);

	public static IReadOnlyList<HercInternals> Values() => All;

	public static HercInternals? GetByName(string name) =>
		All.FirstOrDefault(e => string.Equals(name, e.Label, StringComparison.OrdinalIgnoreCase));
}
