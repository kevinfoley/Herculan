using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The cockpit computer's damage warnings — that each one is said once and that only the machine the
/// player is flying says anything at all. Both are properties of the guards rather than of the
/// damage, so they are checked by driving real damage into a real chassis and reading the message
/// port's input, not by reproducing the guards here.
///
/// <para>The latches these rest on are not the warnings' own bookkeeping: <c>+0xa8</c>/<c>+0xa9</c>
/// are also the two speed penalties and <c>+0xaa</c>/<c>+0xab</c> the reactor's output grades, so a
/// warning that repeated would mean one of those had come unlatched. See
/// docs/retail/simulation/component-damage.md.</para>
///
/// <para>Every test skips silently when no Earthsiege 2 install can be found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class ComputerWarningTests {
	/// <summary>
	/// The warnings whose one-shot is a latch byte rather than a difference across one write: the two
	/// leg grades and the reactor. The shield-generator pair and <c>WEAPON DESTROYED</c> are excluded
	/// deliberately — they are differences, and <c>WEAPON DESTROYED</c> is *supposed* to be said again
	/// for a second hardpoint lost in a later write.
	/// </summary>
	private static readonly int[] LatchedWarnings = {
		SystemMessages.InternalDamageLegServos,
		SystemMessages.StructuralFailureImminent,
		SystemMessages.InternalDamageEngine,
	};

	/// <summary>
	/// A machine shelled until it comes apart says each latched warning exactly once, however many
	/// hits land in the same band.
	/// </summary>
	[Fact]
	public void EachLatchedWarningIsSaidOnce() {
		if (Shell(player: true) is not { } log) {
			return;
		}

		// The positive control: this is worthless as a "said once" check if the run never reached the
		// bands at all.
		Assert.NotEmpty(log.Said);

		foreach (int id in LatchedWarnings) {
			Assert.True(1 == log.Said.Count(said => said == id), $"id 0x{id:x} — log [{string.Join(",", log.Said.Select(v => $"0x{v:x}"))}]");
		}
	}

	/// <summary>
	/// And an AI machine says nothing, because every one of these posts is gated on
	/// <c>SimObject.LocallyPiloted</c>. The player's own run above is this one's positive control:
	/// the same shelling on the same chassis is what fills the log when the machine is flown.
	/// </summary>
	[Fact]
	public void AnAiMachineSaysNothing() {
		if (Shell(player: false) is not { } log) {
			return;
		}

		Assert.Empty(log.Said);
	}

	/// <summary>
	/// <c>ENEMY TARGET DESTROYED</c> needs both halves of its predicate: the player's own machine
	/// landed the shot, and the thing that died is what the player had selected. Killing something
	/// the player was not boxing is silent.
	/// </summary>
	[Fact]
	public void TheKillAnnouncementNeedsTheVictimToBeTheSelectedTarget() {
		if (Content() is not { } content
				|| Spawn(content, "OUTLAW") is not { } player
				|| Spawn(content, "OUTLAW") is not { } victim) {
			return;
		}

		player.IsPlayer = true;

		var log = new MessageLog();
		var world = FlatWorld(log, player, victim);

		Shell(world, victim, player);
		Assert.True(victim.Destroyed, "the victim should have been destroyed");
		Assert.DoesNotContain(SystemMessages.EnemyTargetDestroyed, log.Said);

		// Again, with the second machine boxed this time.
		if (Spawn(content, "OUTLAW") is not { } selected) {
			return;
		}

		world.Add(selected);
		selected.Position = victim.Position;
		player.Target = selected;
		log.Said.Clear();

		Shell(world, selected, player);
		Assert.True(selected.Destroyed, "the selected machine should have been destroyed");
		Assert.Equal(1, log.Said.Count(said => said == SystemMessages.EnemyTargetDestroyed));
	}

	/// <summary>
	/// <c>SHIELDS CRITICAL</c>'s latch is the one in this family that re-arms, so it gets its own
	/// check: it survives a tick inside the hysteresis band and is released by a tick above it.
	///
	/// <para>Only the release is exercised here — <c>Mech_DirectFireHitTest</c> owns the set, and
	/// reaching it needs a shot in flight.</para>
	/// </summary>
	[Fact]
	public void TheShieldsCriticalLatchIsReleasedOnlyAboveTheClearThreshold() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		mech.IsPlayer = true;
		var world = FlatWorld(new MessageLog(), mech);

		// Empty, latched — where a machine sits the moment the warning fires.
		mech.Shields.Empty();
		mech.ShieldsDownAlert = true;

		world.Tick();
		Assert.True(mech.Shields.Total <= MechObject.ShieldsDownAlertClearCharge,
			"a tick's recharge should not have carried the array over the threshold on its own");
		Assert.True(mech.ShieldsDownAlert, "the latch should hold while the array is still below the threshold");

		// Refilled to the chassis' full 3500, which clears the threshold outright.
		mech.Shields.RefillToBalance();
		Assert.True(mech.Shields.Total > MechObject.ShieldsDownAlertClearCharge, "the refill should clear the threshold");

		world.Tick();
		Assert.False(mech.ShieldsDownAlert, "the latch should be released once the array is back above the threshold");
	}

	/// <summary>
	/// And the release is gated on the same <c>mech+0xa3</c> the set is: an AI machine's latch is not
	/// touched by its own power tick, however full its array is.
	/// </summary>
	[Fact]
	public void AnAiMachineNeverReleasesTheShieldsCriticalLatch() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		mech.IsPlayer = false;
		var world = FlatWorld(new MessageLog(), mech);

		mech.ShieldsDownAlert = true;
		Assert.True(mech.Shields.Total > MechObject.ShieldsDownAlertClearCharge, "the machine should spawn with a full array");

		world.Tick();
		Assert.True(mech.ShieldsDownAlert, "only the locally piloted machine's latch is released");
	}

	/// <summary>
	/// A ray that reaches the shooter's own selected target marks the <i>shooter</i> engaged —
	/// <c>Sim_RaycastObjectList</c>'s half of <c>obj+0x9e</c>, which is reached with nothing in
	/// detection range of either machine. A ray that reaches something else does not.
	/// </summary>
	[Fact]
	public void ShootingTheSelectedTargetMarksTheShooterEngaged() {
		if (Content() is not { } content
				|| Spawn(content, "OUTLAW") is not { } shooter
				|| Spawn(content, "OUTLAW") is not { } target
				|| Spawn(content, "OUTLAW") is not { } bystander) {
			return;
		}

		var world = FlatWorld(new MessageLog(), shooter, target, bystander);

		// The bystander stands down the shooter's +Y axis, where the ray goes. The target stands well
		// off to the side, so the ray never reaches it at all — the sweep shortens rather than stops,
		// so a target merely *behind* the bystander would still be struck, and still engaged.
		bystander.Position = shooter.Position + new Vec3i(0, 6000, 0);
		target.Position = shooter.Position + new Vec3i(20000, 0, 0);

		// The positive control: the ray does stop on the bystander, so "not engaged" below cannot be
		// a shot that simply missed everything.
		var probe = Shot(shooter);
		Assert.NotEqual(0, world.Raycast(probe));
		Assert.Same(bystander, probe.HitObject);

		// Selected the machine off to the side: the ray never reaches it, so nobody is engaged.
		shooter.Target = target;
		world.Raycast(Shot(shooter));
		Assert.False(shooter.Engaged, "stopping on something else is not engaging the target");
		Assert.False(target.Engaged);

		// Now the selected target is the machine the ray actually reaches.
		shooter.Target = bystander;
		world.Raycast(Shot(shooter));
		Assert.True(shooter.Engaged, "the shooter is the one marked engaged, not the machine it hit");
		Assert.False(bystander.Engaged, "the struck object fires its action but is not itself marked");
	}

	/// <summary>
	/// A hitscan shot straight down the shooter's +Y axis, owned by it. The muzzle is lifted to
	/// torso height: fired from the machine's own origin it starts at ground level, and the terrain
	/// query clips the ray to nothing before an object is ever tested.
	/// </summary>
	internal static WeaponShot Shot(MechObject shooter) {
		var muzzle = shooter.WorldTransform;
		muzzle.Z += MuzzleHeight;
		return new WeaponShot(muzzle, ShotRange, ShotDamage, ShotDamage, TestRound, shooter, excluded: null);
	}

	/// <summary>Roughly the hit cylinder's own centre height, <c>typeRecord+0x18</c>.</summary>
	private const int MuzzleHeight = 1000;

	private const int ShotRange = 30000;
	private const short ShotDamage = 100;

	/// <summary>A bare <c>PROJ.DAT</c> row — only the impact-effect arrays are read off it.</summary>
	private static readonly ProjectileData.Projectile TestRound = new() {
		DamageArmor = ShotDamage,
		DamageShield = ShotDamage,
		SplashFactor = 0,
		ImpactFXShield = new short[] { 11, 11, 11, 11 },
		ImpactFXGround = new short[] { 0, 1, 4, 5 },
		ImpactFXArmor = new short[] { 0, 1, 4, 5 },
	};

	/// <summary>
	/// Walks one machine from pristine to wrecked with repeated blasts on its own position, and hands
	/// back everything the computer said on the way. Null when there is no install to read.
	/// </summary>
	private static MessageLog? Shell(bool player) {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return null;
		}

		mech.IsPlayer = player;

		var log = new MessageLog();
		Shell(FlatWorld(log, mech), mech, attacker: null);
		return log;
	}

	/// <summary>
	/// Small blasts at the machine's own feet until it is destroyed. Each one rolls per component, so
	/// this is many partial writes rather than one flattening hit — which is the point: the bands are
	/// crossed repeatedly and by different parts.
	/// </summary>
	private static void Shell(SimWorld world, MechObject victim, MechObject? attacker) {
		for (int i = 0; i < BlastCount && !victim.Destroyed; i++) {
			victim.ExplosiveDamage(world, BlastDamage, victim.Position, BlastRadius, attacker);
		}
	}

	/// <summary>
	/// Fixed, so a failure is reproducible — though the run does not depend on it: the blasts are
	/// small enough that every band is crossed in order whatever the draws are.
	/// </summary>
	private const int RandomSeed = 1;

	/// <summary>
	/// Small blasts, and a great many of them. The size is what matters: a heavier shell can take a
	/// side of legs from healthy to destroyed inside one write, skipping the <c>0x50</c> band
	/// entirely, and then <c>INTERNAL DAMAGE: LEG SERVOS</c> is never reached — correctly, but the
	/// test would be checking nothing.
	/// </summary>
	private const short BlastDamage = 200;

	/// <inheritdoc cref="BlastDamage"/>
	private const int BlastCount = 8000;

	/// <summary>Wide enough that the falloff never spares a component; the grind is the point.</summary>
	private const int BlastRadius = 40000;

	/// <summary>Records the message ids posted to the port and ignores every other noise.</summary>
	internal sealed class MessageLog : ISoundSink {
		public List<int> Said { get; } = new();

		public void Say(int messageId) => Said.Add(messageId);

		public void Play(int id) { }

		public void PlayAt(int id, Vec3i position, SoundReach? reach, object? source) { }

		public void Stop(int id) { }

		public void MoveTo(int id, Vec3i position, SoundReach? reach, object? source) { }

		public void SetPitch(int id, int rate) { }

		public void SquadSay(int messageId, object speaker) { }

		public void CommandSay(int messageId) { }

		public void Unsay(int messageId) => Said.Remove(messageId);

		public void SquadUnsay(int messageId, object? speaker) { }
	}

	internal static SimWorld FlatWorld(ISoundSink sounds, params MechObject[] mechs) {
		const int widthShift = 8;
		const int cellShift = 12;
		const int cellCount = 1 << (widthShift * 2);

		var heights = new byte[cellCount];
		Array.Fill(heights, (byte)40);

		var terrain = new HeightGrid(widthShift, widthShift, cellShift, 16, 10, heights, new byte[cellCount]);

		// Pinned rather than left on SimRandom's vanilla state, because the shelling below is a long
		// chain of ~51% per-component rolls and which bands it crosses, in what order, is a property
		// of the stream. A test of "each warning is said once" should not also be a test of the
		// retail seed table, and should not move if that table is ever revisited.
		var world = new SimWorld(terrain, random: new SimRandom(RandomSeed)) { Sounds = sounds };

		int middle = (1 << (widthShift + cellShift)) / 2;
		foreach (var mech in mechs) {
			mech.Position = new Vec3i(middle, middle, terrain.HeightAtWorld(middle, middle));
			world.Add(mech);
		}

		return world;
	}

	/// <summary>
	/// A bare chassis with its component health — and nothing else, because nothing else is on any
	/// of these paths. The <c>.DMG</c> is not optional here: without it <c>_damage</c> is null, every
	/// damage entry point returns at its first line, and a test that shells the machine would pass
	/// while proving nothing.
	/// </summary>
	internal static MechObject? Spawn(GameContent content, string herc) {
		if (content.Read("dat", herc + ".DAT") is not { } datBytes
				|| new HercSimDataTransformer().Parse(datBytes) is not HercSimDat data
				|| content.Read("dmg", herc + ".DMG") is not { } dmgBytes
				|| new HercDamageFileTransformer().Parse(dmgBytes) is not HercSimDamage damageModel) {
			return null;
		}

		var damage = new ComponentDamage(damageModel, ComponentDamage.MechComponentCount,
			ComponentDamage.MechDependentCount, new SimRandom(RandomSeed));

		return new MechObject(herc, data, 0, MechLoadout.None, damage: damage);
	}

	internal static GameContent? Content() {
		string? root = GameInstall.Locate(null);
		return root != null ? GameContent.MountSimulator(root) : null;
	}
}
