namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's dat\WEAPONS.DAT, in SIMVOL0.VOL: <see cref="TemplateCount"/> weapon mount templates in ES2's record layout
/// (<see cref="Weapons.WeaponMountTemplate"/>) with no count before them, then <see cref="ProjectileCount"/> projectile
/// records in ES2's <c>PROJ.DAT</c> layout (<see cref="ProjectileData.Projectile"/>), also with no count, which ES1 keeps
/// in this file and has no <c>PROJ.DAT</c> for. Layout: docs/es1/formats/weapons-dat-sim.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1WeaponsSimTransformer"/>.
/// </summary>
public class Es1Weapons {
	/// <summary>How many templates the loader reads, a constant of the program: one per weapon id 0-16.</summary>
	public const int TemplateCount = 17;

	/// <summary>How many projectile records the file holds: the 0x2ac bytes the loader reads after the templates.</summary>
	public const int ProjectileCount = 19;

	/// <summary>The templates, by weapon id.</summary>
	public Weapons.WeaponMountTemplate[] Templates { get; set; } = [];

	/// <summary>The projectile records, by the index a template's <see cref="Weapons.WeaponMountTemplate.ProjDatIndex"/> names.</summary>
	public ProjectileData.Projectile[] Projectiles { get; set; } = [];
}
