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

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void HeaderCarriesDifficultyAtOffsetFourteen(short difficulty) {
		var bytes = new byte[ScriptDatHeader.Size];
		System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), difficulty);

		Assert.Equal(difficulty, ScriptDatHeader.Read(bytes).Difficulty);
	}

	/// <summary>
	/// The original indexes four-entry tables with whatever the file says; a hand-edited
	/// <c>script.dat</c> is held to the four levels here instead. This is the one divergence on the
	/// header path, so it is worth a test of its own.
	/// </summary>
	[Theory]
	[InlineData(-1, 0)]
	[InlineData(4, 3)]
	[InlineData(short.MaxValue, 3)]
	[InlineData(short.MinValue, 0)]
	public void HeaderClampsDifficultyToTheFourLevels(short onDisk, int expected) {
		var bytes = new byte[ScriptDatHeader.Size];
		System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), onDisk);

		Assert.Equal(expected, ScriptDatHeader.Read(bytes).Difficulty);
	}

	/// <summary>
	/// Offset 14 and nothing beside it: a header carrying a difficulty must leave the four fields the
	/// loader already decoded alone. Catches an off-by-two against the neighbouring cheat flags.
	/// </summary>
	[Fact]
	public void DifficultyDoesNotDisturbTheOtherHeaderFields() {
		var bytes = new byte[ScriptDatHeader.Size];
		System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), 3);

		var header = ScriptDatHeader.Read(bytes);

		Assert.Equal(0, header.TheaterIndex);
		Assert.Equal(0, header.ZoneIndex);
		Assert.Equal(0, header.ObjectiveType);
		Assert.Equal(0, header.TheaterVariant);
	}

	// ---- the tables -------------------------------------------------------------------------

	/// <summary><c>DAT_0049a73c</c> and <c>DAT_0049a744</c>, read straight out of the binary.</summary>
	[Theory]
	[InlineData(0, 3500, 300)]
	[InlineData(1, 2800, 600)]
	[InlineData(2, 2100, 800)]
	[InlineData(3, 1400, 1000)]
	public void DamageScaleMatchesTheRetailTables(int difficulty, int human, int cybrid) {
		var world = new SimWorld(FlatTerrain()) { Difficulty = difficulty };

		Assert.Equal(human, world.DamageScaleFor(MissionSide.Human));
		Assert.Equal(cybrid, world.DamageScaleFor(MissionSide.Cybrid));
	}

	/// <summary>
	/// The property that makes difficulty a difficulty: a human shot is scaled up at every level and a
	/// Cybrid one down at every level, and each step closes the gap between them.
	/// </summary>
	[Fact]
	public void EachStepMovesTheTwoSidesTowardsEachOther() {
		var world = new SimWorld(FlatTerrain());

		for (int level = 0; level < ScriptDatHeader.DifficultyLevels; level++) {
			world.Difficulty = level;
			Assert.True(world.DamageScaleFor(MissionSide.Human) > Q10Unit);
			Assert.True(world.DamageScaleFor(MissionSide.Cybrid) < Q10Unit);

			if (level == 0) {
				continue;
			}

			int human = world.DamageScaleFor(MissionSide.Human);
			int cybrid = world.DamageScaleFor(MissionSide.Cybrid);
			world.Difficulty = level - 1;

			Assert.True(human < world.DamageScaleFor(MissionSide.Human));
			Assert.True(cybrid > world.DamageScaleFor(MissionSide.Cybrid));
			world.Difficulty = level;
		}
	}

	/// <summary>
	/// The aim-scatter table is indexed by the same value, so it must be the same width. It read five
	/// entries once, which would have been a fifth difficulty level that does not exist.
	/// </summary>
	[Fact]
	public void AimScatterTableIsOneEntryPerDifficultyLevel() {
		Assert.Equal(ScriptDatHeader.DifficultyLevels, MechObject.AiAimScatter.Length);
	}

	// ---- where the scale is applied ---------------------------------------------------------

	[Theory]
	[InlineData(0, 3500)]
	[InlineData(1, 2800)]
	[InlineData(2, 2100)]
	[InlineData(3, 1400)]
	public void RaycastScalesBothDamageFiguresOfAnOwnedShot(int difficulty, int scale) {
		var attacker = new Marker(MissionSide.Human);
		var world = WorldWith(attacker, difficulty);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 40);
		world.Raycast(shot);

		Assert.Equal((short)SimMath.Q10Multiply(scale, 100), shot.DamageArmor);
		Assert.Equal((short)SimMath.Q10Multiply(scale, 40), shot.DamageShield);
	}

	/// <summary>A Cybrid shot takes the other table — the side is the <i>firing</i> side.</summary>
	[Fact]
	public void RaycastTakesTheFiringSidesTable() {
		var attacker = new Marker(MissionSide.Cybrid);
		var world = WorldWith(attacker, difficulty: 2);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 100);
		world.Raycast(shot);

		Assert.Equal((short)SimMath.Q10Multiply(800, 100), shot.DamageArmor);
	}

	/// <summary>
	/// <b>A shot with no attacker is left alone</b> — the original's own <c>+0x0e != 0</c> gate, and
	/// what keeps a flyer's airframe contacts out of the scale entirely.
	/// </summary>
	[Fact]
	public void RaycastLeavesAnUnownedShotUnscaled() {
		var world = WorldWith(null, difficulty: 0);
		var shot = new WeaponShot(Transform3.Identity, 5000, Round(), power: 0, owner: null);

		world.Raycast(shot);

		Assert.Equal(Round().DamageArmor, shot.DamageArmor);
		Assert.Equal(Round().DamageShield, shot.DamageShield);
	}

	/// <summary>
	/// The plasma round empties its record before the raycast, so the scale finds zeros there and the
	/// round is scaled once — in <c>Detonate</c> — rather than twice. This is the ordering test: it
	/// fails if the scale is ever moved forward to construction.
	/// </summary>
	[Fact]
	public void AnEmptiedRecordIsNotScaledByTheRaycast() {
		var attacker = new Marker(MissionSide.Human);
		var world = WorldWith(attacker, difficulty: 0);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 100);
		shot.StashDamage();
		world.Raycast(shot);

		Assert.Equal(0, shot.DamageArmor);
		Assert.Equal(0, shot.DamageShield);
	}

	/// <summary>
	/// And what the stash put aside stays unscaled, as the original's
	/// <c>Bullet_StashDirectFireDamage</c> stashes it before the scale runs. A structure struck on its
	/// collision-volume path reads this figure back, so scaling it would double-dip that one path.
	/// </summary>
	[Fact]
	public void TheStashedFigureIsTheUnscaledOne() {
		var attacker = new Marker(MissionSide.Human);
		var world = WorldWith(attacker, difficulty: 0);

		var shot = OwnedShot(attacker, damageArmor: 100, damageShield: 40);
		shot.StashDamage();
		world.Raycast(shot);

		Assert.Equal(100, shot.StashedDamageArmor);
		Assert.Equal(40, shot.StashedDamageShield);
	}

	// ---- the plasma blast -------------------------------------------------------------------

	/// <summary>
	/// The plasma round's blast, which reaches the scale by its own route: fire one at a target that
	/// reports a hit, and the figure that arrives at the blast is the power-scaled armour damage with
	/// the difficulty factor on top of it — once.
	/// </summary>
	[Theory]
	[InlineData(0, 3500)]
	[InlineData(2, 2100)]
	[InlineData(3, 1400)]
	public void PlasmaBlastIsScaledOnceByDifficulty(int difficulty, int scale) {
		var attacker = new Marker(MissionSide.Human);
		var victim = new Marker(MissionSide.Cybrid) { Struck = true, Position = Muzzle };
		var world = WorldWith(attacker, difficulty);
		world.Add(victim);

		var round = Round();
		round.SubtypeId = Projectile.PlasmaSubtype;

		var shot = new Projectile(round, Record(), Muzzle, (0, 0, 0), ownerSpeed: 0,
			power: Q10Unit, owner: attacker, random: world.Random);

		SimMath.TickDelta = SimMath.VanillaTickDelta;
		shot.Tick(world);

		Assert.Equal((short)SimMath.Q10Multiply(scale, round.DamageArmor), victim.BlastDamage);
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

		/// <summary>The figure the last blast arrived with.</summary>
		public short BlastDamage { get; private set; }

		public override int HitRadius => 500;

		public override int DirectFireHitTest(SimWorld world, WeaponShot shot) =>
			Struck ? MinimumHit : 0;

		public override void ExplosiveDamage(SimWorld world, short damage, Vec3i hitPoint,
			int blastRadius, SimObject? attacker) => BlastDamage = damage;

		public override void Tick(SimWorld world) { }

		/// <summary>Inside <see cref="WeaponShot.MinimumScanDistance"/>, so the sweep stops here.</summary>
		private const int MinimumHit = 100;
	}
}
