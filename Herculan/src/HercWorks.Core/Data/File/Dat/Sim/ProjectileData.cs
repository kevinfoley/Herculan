using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /DBSIM/DAT/PROJ.DAT — 27 records of 36 bytes behind a
/// <c>UINT16</c> count. Each record is <see cref="Projectile.Type"/>, <see cref="Projectile.SubtypeId"/>
/// (into BULLETS.DAT, ROCKETS.DAT or BEAM.DAT by type), DamageShield, DamageArmor,
/// <see cref="Projectile.SplashFactor"/>, Speed (fixed point, 5000 -> 500.0), then the impact-effect
/// arrays in the order shield, ground, armour.
///
/// <para><b>How a weapon reaches a record.</b> A catalog weapon's
/// <see cref="Weapons.WeaponMountTemplate.ProjDatIndex"/> is either a flat index into this table, a
/// sentinel meaning "no record" (<c>ECM</c> only), or — for <c>MSL6</c>/<c>MSL8</c>/<c>MSL10</c>/
/// <c>FLYMSL</c> — resolved through the mission's second loadout array, the ammunition type
/// (<c>MecEntry.WeaponAmmoTypes</c>, or <c>script.dat</c> block 7 offset <c>0x72</c>), which picks
/// among the <c>Rocket</c> records. Seven catalog ids (<c>NONE</c>, <c>LAEW</c>, <c>MINE</c>, <c>TARG</c>,
/// <c>SHLD</c>, <c>TURB</c>, <c>ENRG</c>) carry an all-zero placeholder template whose mount
/// constructors never consume the index 0 it reads. See docs/simulation/weapon-mounts.md.</para>
///
/// <para>The retail records, index by index, are tabulated in
/// docs/formats/proj-dat.md#the-retail-records, which also says how a fired shot resolves its
/// record a second time by (Type, SubtypeId).</para>
///
/// <para><b>Damage scaling.</b> A shot's power level — the capacitor charge it was fired at,
/// <c>min(template+0x38, mount+0x7d)</c> — is Q10-multiplied against DamageShield before shield
/// absorption, and against DamageArmor before the damage-application step;
/// <see cref="Projectile.SplashFactor"/>'s own multiplier one step further down is Q10 as well.
/// DamageShield/DamageArmor are the weapon's own base stats, not abstract multipliers. See
/// docs/simulation/weapon-firing.md and docs/formats/proj-dat.md#layout.</para>
///
/// <para><b><see cref="Projectile.Type"/> is a firing-mechanism selector</b>, not a cosmetic tag —
/// each value builds a different class; see <see cref="ProjectileType"/>. Every <c>Beam</c> (4)
/// record has <see cref="Projectile.Speed"/> 0 and resolves its hit synchronously at fire time
/// rather than as a travelling instance. <c>Bullet</c> (2) covers both the ATC progression and the
/// EMP-shaped high-shield entries: real flight time, and <see cref="Projectile.SplashFactor"/> 0
/// throughout — except one. <c>Rocket</c> (0) is the splash-capable guided weapon; <c>Grenade</c>
/// (3) is a cut class whose records no weapon template names (docs/formats/proj-dat.md#open).</para>
///
/// <para><b>The Plasma cannon is index 22</b>, the single <c>Bullet</c> record that breaks the
/// no-splash rule (<see cref="Projectile.SubtypeId"/> 9, 3000/3000, SplashFactor 1000). DBSIM's
/// <c>Bullet</c> per-tick method has a <c>SubtypeId == 9</c> branch calling the explosion formula
/// directly instead of the single-target hit path — a bullet with real flight time that explodes
/// with splash on impact.</para>
/// </summary>
public class ProjectileData {
	public short Total { get; set; }
	public Projectile[]? Data { get; set; }

	public Projectile NewProjectile() => new();

	public class Projectile {
		/// <summary>The firing-mechanism selector — which projectile class the record builds. See <see cref="ProjectileType"/>.</summary>
		public ProjectileType? Type { get; set; }

		/// <summary>The subtype id: the BULLETS.DAT, ROCKETS.DAT or BEAM.DAT record, by <see cref="Type"/>.</summary>
		public short SubtypeId { get; set; }
		public short DamageShield { get; set; }
		public short DamageArmor { get; set; }

		/// <summary>
		/// The Q10 fraction of this hit's shield-absorbed armour damage that <c>Mech_ApplyDirectFireDamage</c>
		/// (<c>004188c8</c>) diverts into a 500-unit secondary explosion on the struck object instead of
		/// the struck component's health. Zero means none. See docs/simulation/damage-system.md.
		/// </summary>
		public short SplashFactor { get; set; }

		public short Speed { get; set; }

		/// <summary>
		/// <c>EXPLOS.DAT</c> effect types for a shot the shields fully absorbed — impact group 0, one of
		/// the four drawn at random. The file order is shield, ground, armour. See
		/// docs/simulation/impact-effects.md.
		/// </summary>
		public short[] ImpactFXShield { get; set; } = new short[4];

		/// <summary>Impact group 2: an armour hit that dropped the struck component's health band.</summary>
		public short[] ImpactFXArmor { get; set; } = new short[4];

		/// <summary>Impact group 1: a shot ending on terrain, or an armour hit that left the band unchanged.</summary>
		public short[] ImpactFXGround { get; set; } = new short[4];
	}
}
