namespace HercWorks.Core.Data.Struct;

/// <summary>
/// A hardpoint's ammo type — the guidance kind a missile rack is loaded with, and the subtype that
/// picks its PROJ.DAT <c>Rocket</c> record. 0-3 are the four the shell offers; 4 is the BMSL's own
/// round; 5 means the hardpoint carries nothing guided. See
/// docs/retail/formats/herc-catalogs.md#the-weapon-unit-record.
/// </summary>
public sealed class MissileType {
	public static readonly MissileType Sarh = new(0, "Semi-Active Radar", "SARH");
	public static readonly MissileType Arh = new(1, "Active-Radar Homing", "ARH");
	public static readonly MissileType Arm = new(2, "Anti-Rad", "ARM");
	public static readonly MissileType Eo = new(3, "Electro-Optical", "EO");
	public static readonly MissileType Bmsl = new(4, "Big Missile", "BMSL");
	public static readonly MissileType None = new(5, "NONE", "NONE");

	private static readonly IReadOnlyList<MissileType> All = new[] { Sarh, Arh, Arm, Eo, Bmsl, None };
	private static readonly Dictionary<int, MissileType> ById = All.ToDictionary(m => m.Id);

	public int Id { get; }
	public string Name { get; }
	public string Abbrev { get; }

	private MissileType(int id, string name, string abbrev) {
		Id = id;
		Name = name;
		Abbrev = abbrev;
	}

	public static MissileType? GetById(int id) => ById.GetValueOrDefault(id);

	/// <summary>Every entry, in id order.</summary>
	public static IReadOnlyList<MissileType> Values() => All;

	public override string ToString() => Name;
}
