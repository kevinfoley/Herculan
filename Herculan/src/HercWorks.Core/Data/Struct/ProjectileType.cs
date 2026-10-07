namespace HercWorks.Core.Data.Struct;

/// <summary>
/// A PROJ.DAT record's <c>Type</c> — the firing-mechanism selector. These are the four literal
/// categories DBSIM ever hands its PROJ.DAT lookup (<c>Proj_LookupRecord</c>, <c>0040ffc8</c>), and
/// each builds a different thing: <c>0</c> and <c>3</c> each their own projectile class, <c>2</c> a
/// travelling round with no guidance, and <c>4</c> a beam that resolves its hit at fire time with
/// no persisting object (every Beam record has <c>Speed</c> 0).
///
/// <para>The C++ class behind each is named by the binary itself — `0` is <c>ROCKET</c>, `2` is
/// <c>BULLET</c>, `3` is <c>GRENADE</c>, `4` has no class at all. See
/// docs/retail/simulation/weapon-damage-types.md, "Type — a firing-mechanism selector".</para>
/// </summary>
public sealed class ProjectileType {
	public static readonly ProjectileType Rocket = new("ROCKET", 0);
	public static readonly ProjectileType Bullet = new("BULLET", 2);

	/// <summary>
	/// Type 3 — the cut <c>GRENADE</c> class. Unreachable in retail: nothing constructs it, so no
	/// <c>PROJ.DAT</c> record carrying this type is ever looked up.
	/// </summary>
	public static readonly ProjectileType Grenade = new("GRENADE", 3);

	public static readonly ProjectileType Beam = new("BEAM", 4);

	/// <summary>The four types, in id order.</summary>
	public static IReadOnlyList<ProjectileType> All { get; } = new[] { Rocket, Bullet, Grenade, Beam };

	private static readonly Dictionary<short, ProjectileType> ById = All.ToDictionary(p => p.Val);

	public string Type { get; }
	public short Val { get; }

	private ProjectileType(string type, short bit) {
		Type = type;
		Val = bit;
	}

	public override string ToString() => Type;

	public static ProjectileType? ForId(short id) => ById.GetValueOrDefault(id);
}
