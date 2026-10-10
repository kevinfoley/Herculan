using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>Actions_EvaluateTriggers</c> (<c>00426b70</c>) — the mission's trigger layer, run once a frame
/// from <c>Sim_MainTick</c> with the player's machine.
///
/// <para>It walks the whole action array and, for each action, decides <b>whose position</b> is
/// offered to that action's areas. Nothing else in the simulation looks at where anything is on the
/// mission's behalf: this is the one place a mission notices that something has happened.</para>
/// </summary>
public static class MissionTriggers {
	/// <summary>
	/// One pass over the action array. The subject an action's <see cref="MissionAction.Type"/>
	/// selects:
	///
	/// <list type="table">
	/// <item><term>0</term><description>the player's machine</description></item>
	/// <item><term>1</term><description>every member of the player's group</description></item>
	/// <item><term>2 / 3</term><description>every member of every <i>deployed</i> human / Cybrid group</description></item>
	/// <item><term>4 / 5 / 6</term><description>every member of every <i>deployed</i> mech / flyer / base group</description></item>
	/// <item><term>7 / 8 / 9</term><description>the action's own resolved target object</description></item>
	/// <item><term>10</term><description>every member of the action's own resolved target group</description></item>
	/// </list>
	///
	/// <para><b>The deployment gate is part of the trigger test.</b> Types 2-6 skip a group that has
	/// not entered the mission, so an undeployed group cannot trip an action — including, notably, the
	/// one it is itself waiting on.</para>
	///
	/// <para>The group sweeps stop at the first group that fires the action, as the original's
	/// short-circuiting loop condition does; an action with no player and no resolved target simply
	/// finds no subject and is offered nothing.</para>
	/// </summary>
	public static void Evaluate(SimWorld world) {
		var actions = world.Mission.Actions;

		for (int i = 0; i < actions.Count; i++) {
			var action = actions[i];
			short type = action.Record.Type;

			switch (type) {
				case MissionAction.SubjectPlayer:
					if (world.PlayerMech is { } player) {
						action.TestTrigger(world, player.Position);
					}

					break;

				case MissionAction.SubjectPlayerGroup:
					TestGroup(world, action, world.PlayerMech?.Group);
					break;

				case MissionAction.SubjectHumanGroups:
				case MissionAction.SubjectCybridGroups:
				case MissionAction.SubjectMechGroups:
				case MissionAction.SubjectFlyerGroups:
				case MissionAction.SubjectBaseGroups:
					TestDeployedGroups(world, action, type);
					break;

				case MissionAction.SubjectTargetMech:
				case MissionAction.SubjectTargetFlyer:
				case MissionAction.SubjectTargetBase:
					if (action.TargetObject is { } subject) {
						action.TestTrigger(world, subject.Position);
					}

					break;

				case MissionAction.SubjectTargetGroup:
					TestGroup(world, action, action.TargetGroup);
					break;
			}
		}
	}

	/// <summary>
	/// The five sweeps over every group, which differ only in what they filter on: a side for types
	/// 2 and 3, and the group's own roster discriminator for 4, 5 and 6. Both filters sit alongside
	/// the deployment gate in the original's one loop condition.
	/// </summary>
	private static void TestDeployedGroups(SimWorld world, MissionActionState action, short type) {
		var groups = world.Groups;

		for (int i = 0; i < groups.Count; i++) {
			var group = groups[i];

			bool matches = type switch {
				MissionAction.SubjectHumanGroups => group.Side == MissionSide.Human,
				MissionAction.SubjectCybridGroups => group.Side == MissionSide.Cybrid,
				MissionAction.SubjectMechGroups => group.Kind == MissionUnitKind.Mech,
				MissionAction.SubjectFlyerGroups => group.Kind == MissionUnitKind.Flyer,
				_ => group.Kind == MissionUnitKind.Base
			};

			if (!matches || group.AwaitingDeployment) {
				continue;
			}

			if (TestGroup(world, action, group)) {
				return;
			}
		}
	}

	/// <summary>
	/// <c>Action_TestTriggerForGroup</c> (<c>00423508</c>) — the action offered every member of one
	/// group in turn, stopping at the first that fires it.
	/// </summary>
	private static bool TestGroup(SimWorld world, MissionActionState action, MissionGroup? group) {
		if (group == null) {
			return false;
		}

		var members = group.Members;
		for (int i = 0; i < members.Count; i++) {
			if (action.TestTrigger(world, members[i].Position)) {
				return true;
			}
		}

		return false;
	}
}
