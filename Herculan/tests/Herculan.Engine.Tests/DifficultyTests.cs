using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct.Dbsim;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The mission difficulty, from the byte the shell wrote to the damage a shot lands with.
///
/// <para>Nothing here needs an Earthsiege 2 install: the header is twenty bytes and the shot path is
/// exercised against a hand-built <c>PROJ.DAT</c> row, so these run on a bare checkout. What they
/// pin is the arithmetic and, more importantly, <b>where</b> it happens — the scale sits at the top
/// of the raycast in the original, and three separate behaviours fall out of that placement: a shot
/// with no attacker is never scaled, a plasma round is scaled once rather than twice, and the
/// stashed figure a structure reads back is the unscaled one. See docs/retail/simulation/difficulty.md.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class DifficultyTests {
	// ---- the header field -------------------------------------------------------------------

	/// <summary>
	/// Every header short carries its own value, so a field read from any neighbour's offset reads
	/// the wrong one. The two cheat flags take turns at 1, the only value they read as on, so a swap
	/// between them shows too.
	/// </summary>
	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void HeaderReadsEachFieldFromItsOwnOffset(bool unlimitedAmmunition, bool playerInvulnerable) {
		var bytes = new byte[ScriptDatHeader.Size];
		short[] shorts = [5, 7, 99, 11, 13, (short)(unlimitedAmmunition ? 1 : 0),
			(short)(playerInvulnerable ? 1 : 0), 2, 99, 17];
		for (int i = 0; i < shorts.Length; i++) {
			System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), shorts[i]);
		}

		var header = ScriptDatHeader.Read(bytes);

		Assert.Equal(5, header.TheaterIndex);
		Assert.Equal(7, header.ZoneIndex);
		Assert.Equal(11, header.ObjectiveType);
		Assert.Equal(13, header.TrainingMissionNumber);
		Assert.Equal(unlimitedAmmunition, header.UnlimitedAmmunition);
		Assert.Equal(playerInvulnerable, header.PlayerInvulnerable);
		Assert.Equal(2, header.Difficulty);
		Assert.Equal(17, header.TheaterVariant);
	}

	/// <summary>
	/// The original indexes four-entry tables with whatever the file says; a hand-edited
	/// <c>script.dat</c> is held to the four levels here instead. This is the one divergence on the
	/// header path, so it is worth a test of its own.
	/// </summary>
	[Theory]
	[InlineData(-1, 0)]
	[InlineData(4, 3)]
	public void HeaderClampsDifficultyToTheFourLevels(short onDisk, int expected) {
		var bytes = new byte[ScriptDatHeader.Size];
		System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), onDisk);

		Assert.Equal(expected, ScriptDatHeader.Read(bytes).Difficulty);
	}

	// ---- where the scale is applied ---------------------------------------------------------

	/// <summary>
	/// A human shot at difficulty 2 takes the human table's 2100: 100 and 40 come out as 205 and 82,
	/// truncated.
	/// </summary>
	[Fact]
	public void RaycastScalesBothDamageFiguresOfAnOwnedShot() {
		var attacker = new Marker(MissionSide.Human);
		var world = WorldWith(attacker, difficulty: 2);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 40);
		HitTests.Raycast(world, shot);

		Assert.Equal(205, shot.DamageArmor);
		Assert.Equal(82, shot.DamageShield);
	}

	/// <summary>
	/// A Cybrid shot takes the other table — the side is the <i>firing</i> side. At difficulty 2 that
	/// is 800, so 100 comes out as 78.
	/// </summary>
	[Fact]
	public void RaycastTakesTheFiringSidesTable() {
		var attacker = new Marker(MissionSide.Cybrid);
		var world = WorldWith(attacker, difficulty: 2);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 100);
		HitTests.Raycast(world, shot);

		Assert.Equal(78, shot.DamageArmor);
	}

	/// <summary>
	/// <b>A shot with no attacker is left alone</b> — the original's own <c>+0x0e != 0</c> gate, and
	/// what keeps a flyer's airframe contacts out of the scale entirely.
	/// </summary>
	[Fact]
	public void RaycastLeavesAnUnownedShotUnscaled() {
		var world = WorldWith(null, difficulty: 0);
		var shot = new WeaponShot(Transform3.Identity, 5000, Round(), power: 0, owner: null);

		HitTests.Raycast(world, shot);

		Assert.Equal(Round().DamageArmor, shot.DamageArmor);
		Assert.Equal(Round().DamageShield, shot.DamageShield);
	}

	/// <summary>
	/// What the stash puts aside stays unscaled, as the original's
	/// <c>Bullet_StashDirectFireDamage</c> stashes it before the scale runs. A structure struck on its
	/// collision-volume path reads this figure back, so scaling it would double-dip that one path.
	/// </summary>
	[Fact]
	public void TheStashedFigureIsTheUnscaledOne() {
		var attacker = new Marker(MissionSide.Human);
		var world = WorldWith(attacker, difficulty: 0);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 40);
		shot.StashDamage();
		HitTests.Raycast(world, shot);

		Assert.Equal(100, shot.StashedDamageArmor);
		Assert.Equal(40, shot.StashedDamageShield);
	}

	// ---- the plasma blast -------------------------------------------------------------------

	/// <summary>
	/// The plasma round's blast, which reaches the scale by its own route. Fired at half power into a
	/// target that reports a hit, it empties its record before the raycast, so the target takes nothing
	/// directly, and the blast arrives with the record's armour damage power-scaled to 50 and then
	/// difficulty-scaled once: 2100 × 50 >> 10 = 102.
	/// </summary>
	[Fact]
	public void PlasmaBlastIsScaledOnceByDifficulty() {
		var attacker = new Marker(MissionSide.Human);
		var victim = new Marker(MissionSide.Cybrid) { Struck = true, Position = Muzzle };
		var world = WorldWith(attacker, difficulty: 2);
		world.Add(victim);

		var round = Round();
		round.SubtypeId = Projectile.PlasmaSubtype;

		var shot = new Projectile(round, Record(), Muzzle, (0, 0, 0), ownerSpeed: 0,
			power: Q10Unit / 2, owner: attacker, random: world.Random);

		SimMath.TickDelta = SimMath.VanillaTickDelta;
		shot.Tick(world);

		Assert.Equal(0, victim.DirectDamage);
		Assert.Equal(102, victim.BlastDamage);
	}

	// ---- fixtures ---------------------------------------------------------------------------

	/// <summary>1.0 in the Q10 fixed point every one of these factors is expressed in.</summary>
	private const int Q10Unit = 1 << 10;

	private static readonly Vec3i Muzzle = new(1 << 18, 1 << 18, 40 << 8);

	/// <summary>A featureless grid, so the ray's terrain leg cannot stop it before an object does.</summary>
	private static HeightGrid FlatTerrain() {
		const int widthShift = 8;
		const int cellShift = 12;
		const int cellCount = 1 << (widthShift * 2);

		var heights = new byte[cellCount];
		System.Array.Fill(heights, (byte)40);

		return new HeightGrid(widthShift, widthShift, cellShift, 16, 10, heights, new byte[cellCount]);
	}

	private static SimWorld WorldWith(SimObject? attacker, int difficulty) {
		var world = new SimWorld(FlatTerrain()) { Difficulty = difficulty };

		if (attacker != null) {
			attacker.Position = Muzzle;
			world.Add(attacker);
		}

		return world;
	}

	private static WeaponShot OwnedShot(SimObject owner, short damageArmor, short damageShield) {
		var round = Round();
		round.DamageArmor = damageArmor;
		round.DamageShield = damageShield;

		// Power zero means "not fired from a capacitor", which leaves the record's own figures alone —
		// so what the raycast scales is exactly the number written above.
		return new WeaponShot(Transform3.Identity, 5000, round, power: 0, owner: owner);
	}

	/// <summary>A minimal <c>PROJ.DAT</c> row: the fields the shot path reads and nothing else.</summary>
	private static ProjectileData.Projectile Round() => new() {
		SubtypeId = 1,
		DamageArmor = 100,
		DamageShield = 40,
		Speed = 1000
	};

	/// <summary>A minimal <c>BULLETS.DAT</c> row — no scatter, no animation, a long enough life.</summary>
	private static BulletType Record() => new() {
		Lifetime = 1000,
		ClipRadius = 100,
		FrameInterval = 0,
		Scatter = 0
	};

	/// <summary>
	/// A stand-in object: it can be an attacker (for its side), a victim (reporting a hit and
	/// recording what the blast brought), or both. Nothing here needs a model or a machine.
	/// </summary>
	private sealed class Marker : SimObject {
		public Marker(MissionSide side) => Side = side;

		/// <summary>Whether this object reports itself hit by any shot that reaches it.</summary>
		public bool Struck { get; init; }

		/// <summary>The armour figure the last shot to reach this object carried.</summary>
		public short DirectDamage { get; private set; }

		/// <summary>The figure the last blast arrived with.</summary>
		public short BlastDamage { get; private set; }

		public override int HitRadius => 500;

		public override int DirectFireHitTest(SimWorld world, WeaponShot shot) {
			if (!Struck) {
				return 0;
			}

			DirectDamage = shot.DamageArmor;
			return MinimumHit;
		}

		public override void ExplosiveDamage(SimWorld world, short damage, Vec3i hitPoint,
			int blastRadius, SimObject? attacker) => BlastDamage = damage;

		public override void Tick(SimWorld world) { }

		/// <summary>Inside <see cref="WeaponShot.MinimumScanDistance"/>, so the sweep stops here.</summary>
		private const int MinimumHit = 100;
	}
}
