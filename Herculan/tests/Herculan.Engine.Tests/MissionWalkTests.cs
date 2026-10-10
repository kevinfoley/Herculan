using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Walks the player's HERC around a real mission — the whole chain at once: <c>script.dat</c> and
/// <c>player.mec</c> to a scene, the zone's own heightmap under the machine's feet, and the control
/// law driving it over real terrain rather than the flat plate
/// <see cref="MechLocomotionTests"/> uses.
///
/// <para>Skips silently when no Earthsiege 2 install or mission can be found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class MissionWalkTests {
	[Fact]
	public void PlayerMechWalksAcrossRealTerrain() {
		if (Load() is not { } scene || scene.PlayerMech is not { Thread: not null } mech) {
			return;
		}

		var start = mech.Position;
		var full = new MechControls(0, -MechControls.AxisFull);

		for (int i = 0; i < 500; i++) {
			mech.Controls = full;
			scene.World.Tick();
		}

		int travelled = SimMath.FastMagnitude2D(
			mech.Position.X - start.X, mech.Position.Y - start.Y);

		// 20 seconds of walking should cover hundreds of metres. The only ways it does not are a
		// collision it cannot back out of or ground too steep to leave, both of which are legitimate
		// but would make this a poor smoke test — so this asserts it moved a long way, not a
		// specific distance.
		Assert.True(travelled > 20000,
			$"{mech.Name} covered only {travelled} world units in 500 ticks");

		// And it must still be standing on the zone, not inside it or floating over it.
		Assert.Equal(scene.World.Terrain.HeightAtWorld(mech.Position.X, mech.Position.Y)
			+ mech.Type.RideHeight, mech.Position.Z);
	}

	/// <summary>
	/// A machine with no pilot is driven by its behaviour state's think, and the two states with no
	/// think — <c>deciding</c> and <c>in limbo</c> — leave it nothing to drive the control law with, so
	/// a machine that never leaves them stands where the mission put it.
	///
	/// <para>Nothing here asks that a driven machine moves: whether one does depends on whether its
	/// order leaves it anywhere to go, and a mission can open with every enemy group awaiting
	/// deployment and only the squad, on station around a player who has not moved, in the world.
	/// <see cref="SquadmatesKeepingFormationFollowTheWalkingPlayer"/> tests a think driving the
	/// control law where the code guarantees it has to.</para>
	/// </summary>
	[Fact]
	public void UnpilotedMachinesMoveOnlyUnderAThinkThatDrivesThem() {
		if (Load() is not { } scene) {
			return;
		}

		var others = scene.Objects
			.Select(o => o.Object)
			.OfType<MechObject>()
			.Where(mech => !mech.IsPlayer && mech.Thread != null)
			.Select(mech => (Mech: mech, Start: mech.Position))
			.ToList();

		if (others.Count == 0) {
			return;
		}

		// Sampled per tick rather than up front: every machine starts in `deciding`, whose think is
		// none, and takes its real state from the reassess on the tick after.
		var driven = new HashSet<MechObject>();

		for (int i = 0; i < 100; i++) {
			scene.World.Tick();

			foreach (var (mech, _) in others) {
				if (mech.Behaviour.State is { Think: not ThinkSlot.None }) {
					driven.Add(mech);
				}
			}
		}

		foreach (var (mech, start) in others.Where(o => !driven.Contains(o.Mech))) {
			Assert.Equal(start.X, mech.Position.X);
			Assert.Equal(start.Y, mech.Position.Y);
		}
	}

	/// <summary>
	/// A squadmate in <c>patrolling</c>, <c>search/destroy</c> or <c>guarding</c> with no standing
	/// squad order (and, guarding, no FIRE AT WILL) keeps formation on its group's leader, which is
	/// the player — docs/retail/simulation/ai-navigation.md. It starts on its post, so once the player
	/// has walked it further off than the formation station range the post has left it behind, and
	/// the think has to walk it after the player.
	///
	/// <para>Skips when the mission's squad has no machine in such a state, and when the player is
	/// stopped — by ground too steep to climb, or by walking into a squadmate — before it has gone
	/// that far.</para>
	/// </summary>
	[Fact]
	public void SquadmatesKeepingFormationFollowTheWalkingPlayer() {
		if (Load() is not { } scene || scene.PlayerMech is not { Thread: not null } player) {
			return;
		}

		var squad = scene.Objects
			.Select(o => o.Object)
			.OfType<MechObject>()
			.Where(mech => !mech.IsPlayer && mech.Thread != null && mech.Group is { LedByPlayer: true })
			.Select(mech => (Mech: mech, Start: mech.Position))
			.ToList();

		var playerStart = player.Position;
		var full = new MechControls(0, -MechControls.AxisFull);
		var kept = new HashSet<MechObject>();
		var left = new HashSet<MechObject>();
		int playerTravelled = 0;

		for (int i = 0; i < 200; i++) {
			player.Controls = full;
			scene.World.Tick();

			// Every machine starts in `deciding` and takes its order's state on the tick after; past
			// that, any tick out of formation-keeping drops it from the question.
			foreach (var (mech, _) in squad) {
				if (KeepsFormation(mech)) {
					kept.Add(mech);
				} else if (mech.Behaviour.State != BehaviourState.Deciding) {
					left.Add(mech);
				}
			}

			// Measured at the halfway mark, so a follower has the second half of the run to answer.
			if (i == 99) {
				playerTravelled = SimMath.FastMagnitude2D(
					player.Position.X - playerStart.X, player.Position.Y - playerStart.Y);
			}
		}

		// Twice the range, because FastMagnitude2D is an approximation of the ground distance the
		// think measures.
		if (playerTravelled <= 2 * MechObject.FormationStationRange) {
			return;
		}

		foreach (var (mech, start) in squad.Where(o => kept.Contains(o.Mech) && !left.Contains(o.Mech))) {
			int moved = SimMath.FastMagnitude2D(mech.Position.X - start.X, mech.Position.Y - start.Y);
			Assert.True(moved > 0,
				$"{mech.Name} kept formation but stood still while the player walked {playerTravelled} units");
		}
	}

	private static bool KeepsFormation(MechObject mech) =>
		mech.SquadOrderVerb == MechObject.SquadOrderNone
		&& (mech.Behaviour.State == BehaviourState.Patrolling
			|| mech.Behaviour.State == BehaviourState.SearchDestroy
			|| (mech.Behaviour.State == BehaviourState.Guarding && !mech.FireAtWill));

	/// <summary>
	/// A squadmate's formation post is the slot offset that spread it at spawn, applied to the
	/// leader — so before anything has moved, every follower in the player's group stands on its own
	/// post. A follower with no offset would take the player's own position as its post and walk into
	/// them.
	/// </summary>
	[Fact]
	public void SquadmatesStartOnTheirFormationPosts() {
		if (Load() is not { } scene) {
			return;
		}

		var squad = scene.Objects
			.Select(o => o.Object)
			.OfType<MechObject>()
			.Where(mech => mech.Group is { LedByPlayer: true })
			.ToList();

		foreach (var mech in squad) {
			if (mech.Group!.Leader is not MechObject leader || ReferenceEquals(leader, mech)) {
				continue;
			}

			Assert.NotNull(mech.FormationOffset);

			var post = mech.FormationPostAround(leader.Position, leader.Heading);
			Assert.Equal(post.X, mech.Position.X);
			Assert.Equal(post.Y, mech.Position.Y);
		}
	}

	private static MissionScene? Load() {
		string? root = GameInstall.Locate(null);
		if (root == null) {
			return null;
		}

		string script = MissionLoader.DefaultScriptPath(root);
		string data = Path.GetDirectoryName(script)!;
		if (!File.Exists(script) || !File.Exists(Path.Combine(data, MissionLoader.CountersFileName))
			|| !File.Exists(MissionLoader.PlayerPathFor(script))) {
			return null;
		}

		return MissionScene.Load(GameContent.MountSimulator(root), script);
	}
}
