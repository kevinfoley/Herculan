using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Core.Io.Transform.Shell;

namespace HercWorks.UI;

/// <summary>
/// One weapon's figures for the Compare Weapons screen: its price from the shell catalog
/// <c>gam\WEAPONS.DAT</c>, and what it does in a mission from the simulator's mount template
/// (<c>dat\WEAPONS.DAT</c>) and the <c>dat\PROJ.DAT</c> record its shot applies.
///
/// <para><b>The rates are worked out, not read.</b> No file states a fire rate: retail paces a
/// weapon by its refire delay, its capacitor or both, through mechanics that differ per mount
/// class. <see cref="Load"/> runs those mechanics for one mount with the trigger held until its
/// state repeats, and reads the rate, the damage and the energy off one period of that. The model
/// makes three assumptions retail does not guarantee, and the screen's tooltips say so: the 25 Hz
/// tick holds (a <c>SimTickDelta</c> of 81), the Master Energy Pool stays above its reserve, and
/// an energy weapon sits at the spawn power level of 960. Damage is before the mission difficulty's
/// scale.</para>
/// </summary>
public sealed class WeaponComparison {
	/// <summary>The weapon's id and catalog code.</summary>
	public required WeaponLUT Weapon { get; init; }

	/// <summary>
	/// <c>gam\WEAPONS.DAT</c>'s price, in tons — what the armory charges for one unit
	/// (docs/retail/shell/armory.md#one-currency-two-units).
	/// </summary>
	public int? ScrapCostTons { get; init; }

	/// <summary>
	/// What scrapping one unit yields, in tons: <c>Armory_ScrapValueTons</c> (<c>0041266a</c>) for a
	/// stock of one, a tenth of the price and at least 1 (docs/retail/shell/armory.md#scrapping).
	/// </summary>
	public int? SellValueTons => ScrapCostTons is { } price ? Math.Max(1, price / 10) : null;

	/// <summary>Whether the weapon has a panel in <c>gam\arm_weap.dat</c>, so the armory sells it.</summary>
	public bool InArmory { get; init; }

	/// <summary>What one mount of the weapon does with the trigger held; null for one that never fires.</summary>
	public FireCycle? Fire { get; init; }

	/// <summary>
	/// <see cref="Fire"/> with a shadowed record's own figures applied, as the engine's Fix weapon
	/// damage tweak does — see <see cref="ProjectileData.CorrectedSubtypeIds"/>. The same as
	/// <see cref="Fire"/> for every weapon the correction does not touch.
	/// </summary>
	public FireCycle? CorrectedFire { get; init; }

	/// <summary><see cref="Fire"/> or <see cref="CorrectedFire"/>.</summary>
	public FireCycle? FireFor(bool corrected) => corrected ? CorrectedFire : Fire;

	/// <summary>
	/// The weapon's range in metres, the template's world units at the HUD's own conversion
	/// (docs/retail/simulation/dbsim-physics-notes.md#world-units). A beam's ray is this long, and
	/// beyond it the weapon's lamp goes red and the fire chain skips it
	/// (docs/retail/simulation/weapon-mounts.md#readiness--weaponmounts_mountisready-00410970).
	/// </summary>
	public int? RangeMetres { get; init; }

	/// <summary>
	/// How far a launcher's round flies before it burns out, in metres, launched from a standing
	/// machine and flown straight — see <see cref="FlightUnits"/>. Null for anything but a launcher.
	/// </summary>
	public int? FlightRangeMetres { get; init; }

	/// <summary>An ammunition mount's magazine, in the rounds its gauge counts.</summary>
	public int? Magazine { get; init; }

	/// <summary>
	/// The rounds one shot spends from <see cref="Magazine"/> — the template's <c>+0x38</c>. The shot
	/// is still one projectile (docs/retail/simulation/weapon-firing.md#the-ammunition-dispatch).
	/// </summary>
	public int? RoundsPerShot { get; init; }

	/// <summary>
	/// The <c>PROJ.DAT</c> record the shot applies, when that is not the weapon's own: a record that
	/// shares its (Type, subtype id) with an earlier one is shadowed by it
	/// (docs/retail/formats/proj-dat.md#lookup).
	/// </summary>
	public (int Own, int Applied)? ShadowedRecord { get; init; }

	/// <summary>The weapon's own record's shield and armour damage, for a shadowed one.</summary>
	public (int Shield, int Armor)? OwnRecordDamage { get; init; }

	/// <summary>Whether <see cref="CorrectedFire"/> differs from <see cref="Fire"/>.</summary>
	public bool IsCorrected { get; init; }

	/// <summary>Files that were not found or did not parse, for the status line.</summary>
	public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

	/// <summary>Whether the whole hit is a blast, the plasma branch: <c>PLAS</c> and <c>MFAC</c>.</summary>
	public bool IsPlasma { get; init; }

	/// <summary>Whether the round is a <c>Rocket</c> record chosen by guidance kind, costed with <see cref="LauncherGuidance"/>.</summary>
	public bool IsLauncher { get; init; }

	/// <summary>
	/// One period of a mount's steady state with the trigger held: how many ticks it lasts, how many
	/// shots the mount fires in it and how many projectiles or beams those put out, the damage they
	/// carry and what the pool paid for them. A shot is one firing: <c>BEMP</c>'s three barrels and
	/// <c>EMP2</c>'s second volley are projectiles of the shot that started them. Damage is before the
	/// mission difficulty's scale.
	/// </summary>
	public sealed record FireCycle(int Ticks, int Shots, int Projectiles, long TotalShieldDamage, long TotalArmorDamage,
		long TotalSplashDamage, long PoolDraw, bool IsEnergy) {
		/// <summary>Shots a minute.</summary>
		public double ShotsPerMinute => Shots * 60.0 * TicksPerSecond / Ticks;

		/// <summary>Projectiles or beams per shot.</summary>
		public double ProjectilesPerShot => (double)Projectiles / Shots;

		/// <summary>Shield damage per projectile.</summary>
		public double ShieldDamage => (double)TotalShieldDamage / Projectiles;

		/// <summary>Armour damage per projectile.</summary>
		public double ArmorDamage => (double)TotalArmorDamage / Projectiles;

		/// <summary>The part of <see cref="ArmorDamage"/> a projectile delivers as an explosion rather than to the part it strikes.</summary>
		public double SplashDamage => (double)TotalSplashDamage / Projectiles;

		/// <summary>Shield damage a second with the trigger held.</summary>
		public double ShieldDps => (double)TotalShieldDamage * TicksPerSecond / Ticks;

		/// <summary>Armour damage a second with the trigger held.</summary>
		public double ArmorDps => (double)TotalArmorDamage * TicksPerSecond / Ticks;

		/// <summary>
		/// What the Master Energy Pool pays per shot; null for an ammunition mount, which draws nothing
		/// (docs/retail/simulation/reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4).
		/// </summary>
		public double? EnergyPerShot => IsEnergy ? (double)PoolDraw / Shots : null;
	}

	/// <summary>The HUD's metres: <c>(worldUnits / 1000) * 6</c>, <c>Hud_WorldUnitsToMetres</c> (<c>00434228</c>).</summary>
	private static int WorldUnitsToMetres(int units) => units / 1000 * 6;

	/// <summary>A rocket's speed at launch from a standing machine, <c>Rocket_Fire</c>'s literal 500 (docs/retail/simulation/rockets.md#spawning--rocket_fire-0040a9c4).</summary>
	private const int RocketLaunchSpeed = 500;

	/// <summary>
	/// The world units a rocket covers before it burns out, flown straight: <c>Rocket_TickUpdate</c>'s
	/// steps over its lifetime, each burning <c>IntegrateRateOverTick(acceleration)</c> into the speed,
	/// averaging that with the speed the tick opened at, capping it at the <c>PROJ.DAT</c> record's
	/// <c>Speed</c>, then moving <c>IntegrateRateOverTick(speed)</c>
	/// (docs/retail/simulation/rockets.md#flight--rocket_tickupdate-0040a538). The launching machine's own
	/// travel speed is added at launch; this is the standing figure.
	/// </summary>
	private static int FlightUnits(Core.Data.Struct.Dbsim.RocketType rocket, int speedCap) {
		int speed = RocketLaunchSpeed, distance = 0;
		for (int age = 1; age <= rocket.Lifetime; age++) {
			int opening = speed;
			speed = Math.Min((opening + (TickDelta * rocket.Acceleration >> 8) + opening) >> 1, speedCap);
			distance += TickDelta * speed >> 8;
		}

		return distance;
	}

	/// <summary>The simulation's tick rate, held by its 40 ms frame cap (docs/retail/simulation/dbsim-physics-notes.md#fixed-point-math-toolkit).</summary>
	public const int TicksPerSecond = 25;

	/// <summary><c>SimTickDelta</c> at that rate, what every countdown loses a tick.</summary>
	public const int TickDelta = 81;

	/// <summary>
	/// What an energy mount's capacitor and charge target hold at spawn, <c>Q10Multiply(820, 1200)</c>
	/// (docs/retail/simulation/weapon-mounts.md#energy).
	/// </summary>
	private const int SpawnPowerLevel = 960;

	/// <summary>An energy mount's charge rate, <c>+0x7f</c>, a flat 20 a tick.</summary>
	private const int ChargeRate = 20;

	/// <summary>The fixed power an ELF fires every shot at (docs/retail/simulation/weapon-mounts.md#elf-and-elf2).</summary>
	private const int ElfShotPower = 1200;

	/// <summary><c>PLAS</c>, the one weapon whose charge is half-efficient.</summary>
	private const int HalfEfficiencyWeaponId = 25;

	/// <summary>The template <c>PROJ.DAT</c> index meaning no record (<c>ECM</c>).</summary>
	private const short NoProjectile = 0x21;

	/// <summary>The template <c>PROJ.DAT</c> index meaning a <c>Rocket</c> record chosen by the loaded guidance kind.</summary>
	private const short LauncherProjectile = 0x22;

	/// <summary>
	/// The guidance kind a launcher is costed with: <c>ARH</c>, the one <c>Armory_DeliverQueue</c>
	/// (<c>00412428</c>) loads (docs/retail/formats/herc-catalogs.md#the-weapon-unit-record).
	/// </summary>
	public const short LauncherGuidance = 1;

	/// <summary><c>EMP2</c>'s <c>PROJ.DAT</c> row, which the charge-up branch reads as its two-volley flag.</summary>
	private const short BurstProjectileIndex = 0x13;

	/// <summary>The barrel count that fires three rounds a shot.</summary>
	private const short MultiBarrelCode = 3;

	/// <summary>The bullet subtype the plasma branch singles out (docs/retail/simulation/projectiles.md#the-plasma-branch).</summary>
	private const short PlasmaSubtype = 9;

	/// <summary>Every weapon id but <c>NONE</c>, read from the install once.</summary>
	public static IReadOnlyList<WeaponComparison> LoadAll() {
		var missing = new List<string>();
		var catalog = GamePaths.ReadOrReport(new WeaponsDatTransformer(), GamePaths.FindGamFile, "gam", "WEAPONS.DAT", missing);
		var panels = GamePaths.ReadOrReport(new ArmWeapTransformer(), GamePaths.FindGamFile, "gam", "ARM_WEAP.DAT", missing);
		var templates = GamePaths.ReadOrReport(new WeaponsSimTransformer(), GamePaths.FindSimDatFile, "dat", "WEAPONS.DAT", missing);
		var projectiles = GamePaths.ReadOrReport(new ProjectileDataTransformer(), GamePaths.FindSimDatFile, "dat", "PROJ.DAT", missing);
		var rockets = GamePaths.ReadOrReport(new RocketDataTransformer(), GamePaths.FindSimDatFile, "dat", "ROCKETS.DAT", missing);

		var armory = panels?.Entries?.Select(panel => (int)panel.Id).ToHashSet() ?? new HashSet<int>();
		return WeaponLUT.Values().Where(weapon => weapon.Id != WeaponLUT.None.Id)
			.Select(weapon => Load(weapon, catalog, armory, templates, projectiles, rockets, missing))
			.ToList();
	}

	private static WeaponComparison Load(WeaponLUT weapon, WeaponsDat? catalog, HashSet<int> armory, Weapons? templates,
			ProjectileData? projectiles, RocketData? rockets, IReadOnlyList<string> missing) {
		var template = templates?.Templates is { } all && weapon.Id < all.Length ? all[weapon.Id] : null;
		var records = projectiles?.Data;
		int? own = template == null || records == null ? null : OwnRecord(template, records);
		int? applied = own is { } index ? AppliedRecord(records![index], records) : null;
		int? corrected = own is { } ownAt && applied != own && IsCorrectable(template!, records![ownAt], records) ? own : applied;

		var kind = weapon.MountKind;
		bool fires = template != null && template.Range > 0
			&& kind is WeaponMountKind.Ammunition or WeaponMountKind.Energy or WeaponMountKind.Elf
			&& (kind != WeaponMountKind.Ammunition || template.MagazineSize > 0)
			&& applied != null;

		var record = applied is { } at ? records![at] : null;
		bool plasma = record is { SubtypeId: PlasmaSubtype } && record.Type == ProjectileType.Bullet;
		var fire = fires ? Simulate(weapon.Id, kind, template!, record!, plasma) : null;

		return new WeaponComparison {
			Weapon = weapon,
			ScrapCostTons = catalog?.Data.FirstOrDefault(entry => entry?.Id == weapon.Id)?.SalvageCost,
			InArmory = armory.Contains(weapon.Id),
			Fire = fire,
			CorrectedFire = fires && corrected != applied ? Simulate(weapon.Id, kind, template!, records![corrected!.Value], plasma) : fire,
			IsCorrected = fires && corrected != applied,
			RangeMetres = fires ? WorldUnitsToMetres(template!.Range) : null,
			FlightRangeMetres = fires && record!.Type == ProjectileType.Rocket
				&& rockets?.Entries is { } types && record.SubtypeId >= 0 && record.SubtypeId < types.Length
					? FlightUnits(types[record.SubtypeId], record.Speed) * 6 / 1000 : null,
			Magazine = fires && kind == WeaponMountKind.Ammunition ? template!.MagazineSize : null,
			RoundsPerShot = fires && kind == WeaponMountKind.Ammunition ? template!.ShotCost : null,
			ShadowedRecord = own is { } o && applied is { } a && o != a ? (o, a) : null,
			OwnRecordDamage = own is { } ownIndex && ownIndex != applied
				? (records![ownIndex].DamageShield, records[ownIndex].DamageArmor) : null,
			IsPlasma = fires && plasma,
			IsLauncher = fires && template!.ProjDatIndex == LauncherProjectile,
			Missing = missing
		};
	}

	/// <summary>
	/// The record the mount resolves (docs/retail/formats/weapons-dat-sim.md#the-projdat-index--tail-relative-offset-0x1c-absolute-offset-0x3e):
	/// none, a <c>Rocket</c> record by guidance kind, or a flat index.
	/// </summary>
	private static int? OwnRecord(Weapons.WeaponMountTemplate template, ProjectileData.Projectile[] records) {
		return template.ProjDatIndex switch {
			NoProjectile => null,
			LauncherProjectile => FirstMatch(records, ProjectileType.Rocket, LauncherGuidance),
			var index when index >= 0 && index < records.Length => index,
			_ => null
		};
	}

	/// <summary>
	/// Whether the Fix weapon damage tweak gives a shadowed record its own figures: it is one of
	/// <see cref="ProjectileData.CorrectedSubtypeIds"/>, and no record of its type already carries the
	/// corrected id, so edited data is left alone — the engine's own two guards.
	/// </summary>
	private static bool IsCorrectable(Weapons.WeaponMountTemplate template, ProjectileData.Projectile own,
			ProjectileData.Projectile[] records) =>
		ProjectileData.CorrectedSubtypeIds.TryGetValue(template.ProjDatIndex, out short subtypeId)
		&& FirstMatch(records, own.Type, subtypeId) == null;

	/// <summary>The record a fired shot applies: the first with the mount's record's (Type, subtype id).</summary>
	private static int AppliedRecord(ProjectileData.Projectile mount, ProjectileData.Projectile[] records) =>
		FirstMatch(records, mount.Type, mount.SubtypeId) ?? Array.IndexOf(records, mount);

	private static int? FirstMatch(ProjectileData.Projectile[] records, ProjectileType? type, short subtype) {
		int index = Array.FindIndex(records, record => record.Type == type && record.SubtypeId == subtype);
		return index < 0 ? null : index;
	}

	/// <summary>
	/// One mount with the trigger held, tick by tick, in <c>Sim_MainTick</c>'s order — the trigger in
	/// <c>Sim_PollPlayerInput</c>, then the mount's pool turn in <c>Mech_PerTickSystemsUpdate</c>
	/// (docs/retail/simulation/random-generator.md) — until its state repeats; the figures are that
	/// period's. A magazine never runs dry here, and the pool never runs short.
	///
	/// <para>The mechanics are docs/retail/simulation/weapon-firing.md and weapon-mounts.md: the
	/// readiness test per class, <c>WeaponMount_PrepareShot</c>'s refire delay, the beam, charge-up gun
	/// and ammunition dispatches, the ELF's sustain and fixed power, <c>EMP2</c>'s second volley a
	/// quarter delay on, and <c>WeaponMount_ChargeCapacitor</c>'s draw with <c>PLAS</c>'s half
	/// efficiency. The ELF's spin-up runs once per trigger pull and is left out.</para>
	/// </summary>
	private static FireCycle Simulate(int weaponId, WeaponMountKind kind, Weapons.WeaponMountTemplate template,
			ProjectileData.Projectile record, bool plasma) {
		bool energy = kind != WeaponMountKind.Ammunition;
		bool beam = record.Type == ProjectileType.Beam;
		int cost = template.ShotCost;
		int low = template.EnergyThresholdLow;
		int threshold = low < cost ? Math.Max(low, SpawnPowerLevel) : cost;

		int charge = SpawnPowerLevel, timer = 0;
		bool sustained = false, firedThisTick = false, bursting = false;
		int shots = 0, projectiles = 0;
		long shield = 0, armor = 0, splash = 0, draw = 0;

		var seen = new Dictionary<(int, int, bool, bool), (int Tick, int Shots, int Projectiles, long Shield, long Armor, long Splash, long Draw)>();
		for (int tick = 0; ; tick++) {
			var state = (charge, timer, sustained, bursting);
			if (seen.TryGetValue(state, out var start)) {
				return new FireCycle(tick - start.Tick, shots - start.Shots, projectiles - start.Projectiles,
					shield - start.Shield, armor - start.Armor, splash - start.Splash, draw - start.Draw, energy);
			}

			seen[state] = (tick, shots, projectiles, shield, armor, splash, draw);

			bool ready = kind switch {
				WeaponMountKind.Ammunition => timer == 0,
				WeaponMountKind.Elf => charge >= Math.Max(low, SpawnPowerLevel) || (sustained && charge >= cost),
				_ => timer == 0 && charge >= threshold
			};

			if (ready) {
				shots++;
				FireOnce();
			}

			// The pool turn: WeaponMount_RefireTick, then the energy classes' charge.
			timer = Math.Max(0, timer - TickDelta);
			sustained &= firedThisTick;
			firedThisTick = false;

			if (energy) {
				int deficit = SpawnPowerLevel - charge;
				bool half = weaponId == HalfEfficiencyWeaponId;
				if (half) {
					deficit *= 2;
				}

				int take = deficit < 1 ? 0 : Math.Min(ChargeRate, deficit);
				charge += take - (half ? take >> 1 : 0);
				draw += take;
			}

			if (bursting && timer == 0) {
				FireOnce();
			}
		}

		void FireOnce() {
			timer = template.RefireDelay;
			sustained = firedThisTick = true;

			switch (kind) {
				case WeaponMountKind.Ammunition:
					Hit(rounds: 1, power: null);
					return;

				case WeaponMountKind.Elf:
					charge -= cost;
					Hit(rounds: 1, power: ElfShotPower);
					return;
			}

			if (beam) {
				int power = Math.Min(cost, charge);
				charge -= power;
				Hit(rounds: 1, power);
				return;
			}

			if (charge >= cost) {
				charge -= cost;
				Hit(rounds: 1, power: null);
				return;
			}

			Hit(rounds: template.BarrelCount == MultiBarrelCode ? 3 : 1, power: charge);
			if (template.ProjDatIndex == BurstProjectileIndex && !bursting) {
				timer = template.RefireDelay >> 2;
				bursting = true;
				return;
			}

			bursting = false;
			charge = 0;
		}

		// A round's damage: the record's figures at the power it was fired at, Q10, or as they stand
		// for one fired unpowered. The plasma round empties its shot record and is a blast of the
		// armour figure alone; anything else splits its splash factor's share of the armour damage off
		// into an explosion on the machine it struck.
		void Hit(int rounds, int? power) {
			int Scaled(int figure) => power is { } p ? p * figure >> 10 : figure;

			int armorHit = Scaled(record.DamageArmor);
			int shieldHit = plasma ? armorHit : Scaled(record.DamageShield);
			int splashHit = plasma ? armorHit : record.SplashFactor * armorHit >> 10;

			projectiles += rounds;
			shield += (long)shieldHit * rounds;
			armor += (long)armorHit * rounds;
			splash += (long)splashHit * rounds;
		}
	}
}
