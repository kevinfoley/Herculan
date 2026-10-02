using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// What DBSIM knows about a weapon id before any machine is fitted with one: the mount-template
/// table out of <c>dat\WEAPONS.DAT</c>, the projectile table out of <c>dat\PROJ.DAT</c>, and the two
/// name tables that live in <c>DBSIM.EXE</c> itself.
///
/// <para><b>The simulator does not use the shell's weapon names.</b> <c>Weapons_LoadResourceTables</c>
/// (<c>0040fc8c</c>) walks a 33-entry array of string pointers at <c>00498eb0</c> as it reads the
/// template table and stores one into each record's <c>+0x52</c>; that pointer is what a weapon
/// gauge prints. The names there are not <c>SHELL0.VOL</c>'s catalog names — the catalog calls id 7
/// <c>EMPC</c>, id 28 <c>MFAC</c> and id 30 <c>SHLD</c>, the simulator calls them <c>EMP</c>,
/// <c>MAGN</c> and <c>SHIELD</c> — so a cockpit fed from the shell catalog prints the wrong words.
/// See <see cref="MountNames"/>.</para>
/// </summary>
public sealed class WeaponCatalog {
	/// <summary>
	/// The simulator's own weapon names, <c>DBSIM.EXE</c>'s pointer array at <c>00498eb0</c>, indexed
	/// by the same 0-32 weapon id <c>player.mec</c> and <c>script.dat</c> carry.
	///
	/// <para>Ids 13-16 read as bare round counts (<c>6</c>, <c>8</c>, <c>10</c>, <c>24</c>) because a
	/// missile launcher never prints this name: it prints its loaded ammunition type instead — see
	/// <see cref="MissileAmmoNames"/> and <see cref="MountName"/>. Those counts do match the
	/// launchers' own magazine sizes in the template table, which is a useful cross-check on the
	/// indexing but not something anything reads.</para>
	/// </summary>
	public static readonly IReadOnlyList<string> MountNames = new[] {
		"",       "ATC20",  "ATC35",  "ATC50",  "ATC75",  "ATC100", "ELF",    "EMP",
		"LAS100", "LAS200", "LAS300", "LAS400", "LAS500", "6",      "8",      "10",
		"24",     "PBEAM",  "ECM",    "EMP",    "PBEAM",  "MISSL",  "ELF2",   "EMP2",
		"PBW2",   "PLAS",   "LAEW",   "MINE",   "MAGN",   "TARG",   "SHIELD", "TURBO",
		"ENERGY",
	};

	/// <summary>
	/// The four missile guidance types, <c>DBSIM.EXE</c>'s pointer array at <c>004989c8</c>, indexed
	/// by a <c>PROJ.DAT</c> Rocket record's own subtype id. Semi-active radar homing, active radar
	/// homing, anti-radiation and electro-optical.
	/// </summary>
	public static readonly IReadOnlyList<string> MissileAmmoNames = new[] { "SARH", "ARH", "ARM", "EO" };

	/// <summary>
	/// The template's <c>ProjDatIndex</c> sentinel for "resolve by (Rocket, secondary key) search"
	/// rather than by direct index — the four tube/rack launchers, whose actual projectile is the
	/// ammunition the hardpoint was loaded with.
	/// </summary>
	public const short MissileLookupSentinel = 0x22;

	/// <summary>The sentinel for "no projectile at all" — only <c>ECM</c> carries it.</summary>
	public const short NoProjectileSentinel = 0x21;

	/// <summary>
	/// The one secondary key <c>MechLoadout_ConstructWeaponMounts</c> rewrites before looking it up:
	/// a hardpoint carrying 5 is treated as carrying 0. Retail data uses 5 as the filler value in
	/// every non-launcher slot, so this is what keeps a launcher with no explicit ammunition type
	/// resolving to the first one.
	/// </summary>
	public const short DefaultSecondaryKey = 5;

	private readonly Weapons _templates;
	private readonly ProjectileData _projectiles;

	private WeaponCatalog(Weapons templates, ProjectileData projectiles) {
		_templates = templates;
		_projectiles = projectiles;
	}

	/// <summary>The resource folder both tables live in, and their names inside it.</summary>
	public const string ResourceFolder = "dat";
	public const string TemplateResource = "WEAPONS.DAT";
	public const string ProjectileResource = "PROJ.DAT";

	/// <summary>
	/// Parses both tables, the way <c>Weapons_LoadResourceTables</c> loads them back to back. Returns
	/// null when either resource is unreadable — a machine then gets no mounts rather than mounts
	/// built on guesses.
	/// </summary>
	public static WeaponCatalog? Load(byte[]? weaponsDat, byte[]? projDat) =>
		weaponsDat != null && projDat != null
			&& new WeaponsSimTransformer().Parse(weaponsDat) is Weapons { Templates: not null } templates
			&& new ProjectileDataTransformer().Parse(projDat) is ProjectileData { Data: not null } projectiles
			? new WeaponCatalog(templates, projectiles)
			: null;

	/// <summary>
	/// The mount template for a weapon id — <c>WeaponMountTemplate_GetByWeaponId</c>
	/// (<c>0040fe84</c>), a flat index. Null for an id the table has no record for.
	/// </summary>
	public Weapons.WeaponMountTemplate? Template(int weaponId) =>
		_templates.Templates is { } table && weaponId >= 0 && weaponId < table.Length
			? table[weaponId]
			: null;

	/// <summary>
	/// The <c>PROJ.DAT</c> record a mount fires, resolved exactly as
	/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0040fff8</c>) resolves it: the launcher sentinel
	/// takes the hardpoint's own secondary key through a <c>(Rocket, key)</c> search, the
	/// no-projectile sentinel resolves to nothing, and every other value is a direct index.
	/// </summary>
	/// <param name="weaponId">The hardpoint's weapon id.</param>
	/// <param name="secondaryKey">
	/// The hardpoint's parallel second value — its loaded ammunition type. Only launchers read it.
	/// </param>
	public ProjectileData.Projectile? Projectile(int weaponId, short secondaryKey) {
		if (Template(weaponId)?.ProjDatIndex is not { } index || _projectiles.Data is not { } records) {
			return null;
		}

		if (index == MissileLookupSentinel) {
			short key = secondaryKey == DefaultSecondaryKey ? (short)0 : secondaryKey;
			return records.FirstOrDefault(r => r.Type == ProjectileType.Rocket && r.SubtypeId == key);
		}

		return index != NoProjectileSentinel && index >= 0 && index < records.Length ? records[index] : null;
	}

	/// <summary>
	/// The subtype ids four <c>PROJ.DAT</c> records should carry, by <c>PROJ.DAT</c> index:
	/// <c>ATC75</c> and <c>ATC100</c> take <c>BULLETS.DAT</c> 10 and 11, <c>LAS400</c> and
	/// <c>LAS500</c> take <c>BEAM.DAT</c> 8 and 9. Retail gives them 1, 2, 4 and 5, the ids of
	/// <c>ATC35</c>, <c>ATC50</c>, <c>LAS200</c> and <c>LAS300</c>. See
	/// docs/formats/proj-dat.md#lookup.
	/// </summary>
	private static readonly IReadOnlyDictionary<int, short> CorrectedSubtypeIds = new Dictionary<int, short> {
		[23] = 10, [24] = 11, [25] = 8, [26] = 9,
	};

	/// <summary>
	/// <see cref="Projectile"/> with the subtype id <see cref="CorrectedSubtypeIds"/> names, for the
	/// <c>FixWeaponDamageRecords</c> tweak: the shot then finds its own record when it looks it up again,
	/// and flies and draws as its own <c>BULLETS.DAT</c> or <c>BEAM.DAT</c> record. A record that
	/// no earlier one shadows, or whose corrected id another record of its type already carries, is
	/// returned as it stands, so edited data is left alone.
	/// </summary>
	public ProjectileData.Projectile? CorrectedProjectile(int weaponId, short secondaryKey) {
		if (Projectile(weaponId, secondaryKey) is not { Type: { } type } own
			|| Template(weaponId)?.ProjDatIndex is not { } index
			|| !CorrectedSubtypeIds.TryGetValue(index, out short subtypeId)
			|| Lookup(type, own.SubtypeId) == own
			|| Lookup(type, subtypeId) != null) {
			return Projectile(weaponId, secondaryKey);
		}

		return new ProjectileData.Projectile {
			Type = own.Type,
			SubtypeId = subtypeId,
			DamageShield = own.DamageShield,
			DamageArmor = own.DamageArmor,
			SplashFactor = own.SplashFactor,
			Speed = own.Speed,
			ImpactFXShield = own.ImpactFXShield,
			ImpactFXArmor = own.ImpactFXArmor,
			ImpactFXGround = own.ImpactFXGround,
		};
	}

	/// <summary>
	/// <c>Proj_LookupRecordByIndex</c> (<c>0040ffb0</c>) — a <c>PROJ.DAT</c> record by its flat
	/// position in the table, which is a different thing from the <c>(category, subtype)</c> search
	/// <see cref="Lookup"/> performs. The flyer AI's lead calculation is handed a flat index.
	/// </summary>
	public ProjectileData.Projectile? ProjectileAt(int index) =>
		_projectiles.Data is { } records && index >= 0 && index < records.Length
			? records[index]
			: null;

	/// <summary>
	/// <c>Proj_LookupRecord</c> (<c>0040ffc8</c>) — the linear search by category and subtype id that
	/// <c>Bullet_Construct</c> and <c>Rocket_Construct</c> resolve a fire call's first argument with.
	/// </summary>
	public ProjectileData.Projectile? Lookup(ProjectileType category, short subtypeId) =>
		_projectiles.Data?.FirstOrDefault(r => r.Type == category && r.SubtypeId == subtypeId);

	/// <summary>
	/// The name a mount's gauge prints — <c>WeaponMount_GetDisplayName</c> (<c>0040e18c</c>).
	///
	/// <para>Two cases, and the discriminator is the resolved projectile rather than the weapon id: a
	/// mount whose record is a <see cref="ProjectileType.Rocket"/> prints that record's guidance
	/// type (<c>ARH</c>, <c>SARH</c>, ...) — a launcher is named by what is loaded in it, which is
	/// why the same <c>MSL10</c> hardpoint reads differently in two different fits. Everything else
	/// prints the weapon id's own name.</para>
	///
	/// <para>The original indexes <see cref="MissileAmmoNames"/> with no bounds check, and one real
	/// weapon — <c>MISSL</c> (id 21), whose template points straight at the <c>BMSL</c> record with
	/// subtype 4 — indexes one past its four entries and prints whatever bytes follow. That is a bug
	/// in a weapon no player machine mounts; the id's own name is used here rather than reproducing a
	/// read off the end of a table.</para>
	/// </summary>
	public string MountName(int weaponId, short secondaryKey) {
		if (Projectile(weaponId, secondaryKey) is { } missile && missile.Type == ProjectileType.Rocket
			&& missile.SubtypeId >= 0 && missile.SubtypeId < MissileAmmoNames.Count) {
			return MissileAmmoNames[missile.SubtypeId];
		}

		return weaponId >= 0 && weaponId < MountNames.Count ? MountNames[weaponId] : string.Empty;
	}

	/// <summary>
	/// Which mount class a weapon id builds — <c>MechLoadout_ConstructWeaponMounts</c>'s switch,
	/// which is the only thing that decides it. The four live cases differ in what they carry
	/// (rounds vs. a capacitor vs. nothing), what they draw from the Master Energy Pool, and which of
	/// the weapon-gauge classes the cockpit gives them.
	/// </summary>
	public static WeaponMountKind Kind(int weaponId) => weaponId switch {
		// WeaponMount_CtorAmmunition (0040e140) — rounds. The autocannons, the four missile launchers, MISSL and LAEW.
		// LAEW (26) then has its vtable overwritten with GRENADE_WPN's (GrenadeMountVtable, 00499248), a
		// hold-to-throw trigger whose fire dispatch spends a round and launches nothing; its magazine of
		// 0 keeps it from ever firing, so this engine builds it as a plain ammunition mount.
		1 or 2 or 3 or 4 or 5 or 13 or 14 or 15 or 16 or 21 or 26 => WeaponMountKind.Ammunition,

		// The two ELFs. The factory runs WeaponMount_CtorEnergy and then overwrites the vtable
		// pointer with PTR_DAT_004992c0 — so they are energy mounts that have replaced their fire
		// dispatch, their readiness test and their pool turn. See WeaponMountKind.Elf.
		6 or 22 => WeaponMountKind.Elf,

		// WeaponMount_CtorEnergy (0040e074) — a capacitor charged off the pool. The lasers, EMP, plasma and the beams.
		7 or 8 or 9 or 10 or 11 or 12 or 17 or 19 or 20 or 23 or 24 or 25 or 28
			=> WeaponMountKind.Energy,

		// The five equipment pods, each its own trivial subclass with no capacitor and no rounds.
		18 or 29 or 30 or 31 or 32 => WeaponMountKind.Pod,

		// Id 0 is an empty slot; id 27 (MINE) has no case in the switch at all, so no mount object is
		// ever built for it even though the catalog lists it.
		_ => WeaponMountKind.None,
	};
}

/// <summary>Which of <c>MechLoadout_ConstructWeaponMounts</c>'s mount classes a hardpoint builds.</summary>
public enum WeaponMountKind {
	/// <summary>No mount object at all — an empty slot, or a catalog id the factory has no case for.</summary>
	None,

	/// <summary>A magazine of rounds. Draws nothing from the pool and prints a round count.</summary>
	Ammunition,

	/// <summary>A capacitor charged off the Master Energy Pool. Prints a charge bar.</summary>
	Energy,

	/// <summary>
	/// <c>ELF</c> and <c>ELF2</c> — an energy mount with the vtable at <c>004992c0</c> swapped in
	/// over the energy class's. It carries and charges the same capacitor, and prints the same bar,
	/// but it will not <b>start</b> firing below a full one and its shot is worth a fixed 1200
	/// however much is left. See <c>WeaponMount.CanFire</c>.
	/// </summary>
	Elf,

	/// <summary>An equipment pod. Fires nothing, prints nothing, and is in no fire group.</summary>
	Pod,
}
