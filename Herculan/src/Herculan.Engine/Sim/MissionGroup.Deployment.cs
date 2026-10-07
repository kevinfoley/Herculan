using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The half of a mission group that runs <i>before</i> it is in the mission —
/// <c>Group_DeploymentCheck</c> (<c>004236c4</c>) and the gate it clears.
/// </summary>
public sealed partial class MissionGroup {
	private MissionActionState? _deploymentAction;
	private bool _podLaunched;

	/// <summary>
	/// <c>group+0x14</c> — whether the group is built but not in the mission. It has objects, and
	/// they hold positions, but those positions are a placeholder the arrival overwrites: nothing
	/// draws them, ticks them, collides with them, sees them or shoots them.
	///
	/// <para><b>This is the one gate.</b> <see cref="SimObject.AwaitingDeployment"/> is a read of it,
	/// so there is no second flag that can disagree, and arrival releases it exactly once, from
	/// <see cref="Deploy"/>.</para>
	/// </summary>
	public bool AwaitingDeployment => _deploymentAction != null;

	/// <summary>The action the group is waiting on, or null once it has arrived.</summary>
	public MissionActionState? DeploymentAction => _deploymentAction;

	/// <summary>
	/// <c>Group_DeploymentCheck</c> (<c>004236c4</c>) — run once a frame for a waiting group, in the
	/// slot <see cref="AiTick"/> occupies for one that has arrived. <c>Sim_MainTick</c> picks between
	/// the two on the gate, so a group does exactly one of the two things a frame and never both.
	///
	/// <para>It does nothing at all until the group's action fires. Then the action's <b>verb</b>
	/// picks one of three arrivals:</para>
	///
	/// <list type="bullet">
	/// <item><b>Drop pod</b> (verbs 2 and 3) — a <see cref="MeteorObject"/> is launched at a point
	/// 150,000 units from the player and <b>the gate stays set</b>. The pod carries the group and
	/// clears it when it lands and opens, so arrival happens seconds later and somewhere the pod
	/// decides. Verb 2 scatters the bearing across the half-circle ahead of the player, verb 3 keeps
	/// it near his own heading. The launch is latched so a group only ever gets one pod.</item>
	/// <item><b>On foot</b> (verbs 4 and 5) — the leader is put down at a picked point, facing away
	/// from the bearing it was found on, the rest of the group is dressed around it by the same
	/// formation offsets that spread it at spawn, and the gate is cleared at once. Verb 4 walks the
	/// group on 90,000 units behind the player, verb 5 150,000 ahead. Implemented from the decompile
	/// and unexercised: no retail <c>.MSN</c> authors verb 4 or 5. The arrival rules are in
	/// docs/retail/simulation/mission-deployment.md, "Arrival".</item>
	/// <item><b>In place</b> (any other verb, which in retail data means verb 1) — the gate is
	/// cleared and nothing moves, so the group goes live exactly where the mission placed it. This is
	/// the one arrival for which the placed position is not a placeholder.</item>
	/// </list>
	/// </summary>
	public void DeploymentCheck(SimWorld world) {
		if (_deploymentAction is not { Activated: true } action) {
			return;
		}

		short verb = action.Record.Verb;

		if (verb is MissionAction.VerbPodWide or MissionAction.VerbPodNarrow) {
			if (_podLaunched) {
				return;
			}

			_podLaunched = true;

			// +-90 degrees for the wide verb, +-22.5 for the narrow one. Both are a centre minus a
			// masked draw, so the spread is one-sided in the roll and symmetric in the result.
			short bearing = verb == MissionAction.VerbPodWide
				? (short)(0x4000 - world.Random.NextMasked(0x7fff))
				: (short)(0x1000 - world.Random.NextMasked(0x1fff));

			var target = Deployment.PickPointNearPlayer(world, PodDistance, bearing,
				avoidObjects: false);

			world.LaunchDropPod(target, this);
			return;
		}

		if (verb is MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead) {
			bool behind = verb == MissionAction.VerbWalkBehind;

			short bearing = behind
				? (short)(-0x7000 - world.Random.NextMasked(0x1fff))
				: (short)(0x2000 - world.Random.NextMasked(0x3fff));

			var point = Deployment.PickPointNearPlayer(world,
				behind ? WalkOnDistanceBehind : WalkOnDistanceAhead, bearing, avoidObjects: true);

			PlaceOnFoot(point, (short)(bearing - 0x8000));
		}

		Deploy();
	}

	/// <summary>
	/// Puts the group down around <paramref name="point"/>: the leader on it exactly, and every other
	/// member at its own formation offset rotated by the leader's new heading — the same vtable
	/// <c>+0x78</c> call that spread the group over its spawn point at mission load.
	///
	/// <para><b>Only the leader is turned.</b> The others keep whatever heading they were placed
	/// with, which is the original's behaviour and not an omission; the AI turns them within a tick
	/// or two anyway.</para>
	/// </summary>
	internal void PlaceOnFoot(Vec3i point, short leaderHeading) {
		if (Leader is not { } leader) {
			return;
		}

		leader.Position = point;
		leader.Heading = leaderHeading & 0xffff;

		for (int i = 1; i < _members.Count; i++) {
			_members[i].Position = _members[i] is MechObject member
				? member.FormationPostAround(point, leader.Heading)
				: point;
		}
	}

	/// <summary>
	/// Clears <c>group+0x14</c> — the moment the group enters the mission. Called by
	/// <see cref="DeploymentCheck"/> for the two arrivals that place the group themselves, and by
	/// <see cref="MeteorObject"/> when its pod finishes opening.
	/// </summary>
	internal void Deploy() => _deploymentAction = null;

	/// <summary>How far from the player a drop pod is aimed.</summary>
	public const int PodDistance = 150000;

	/// <summary>How far behind the player a verb-4 group walks on.</summary>
	public const int WalkOnDistanceBehind = 90000;

	/// <summary>And how far ahead a verb-5 one does.</summary>
	public const int WalkOnDistanceAhead = 150000;
}
