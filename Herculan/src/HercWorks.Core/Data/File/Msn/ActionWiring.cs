namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Wires mission actions into the records that wait on them or fire them, in a <see cref="MissionDocument"/>: a group's
/// deployment action (row 16 <c>0x76</c>) and a roster object's engaged and defeated actions (rows 12 to 14). An order's
/// ending action is <see cref="GroupOrders.SetAction"/>'s. What each does is
/// docs/retail/simulation/mission-deployment.md#the-deployment-gate--group0x14 and
/// docs/retail/simulation/mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6.
///
/// <list type="bullet">
/// <item>Each is an overlay field whose unset value is <c>-1</c>, written through <see cref="MissionDocument.SetField"/>: in the
/// record supplying it, in an overlay record for a variant-keyed roster record whose variant supplies it, and unset in every
/// record of the GUID. An overlay cannot carry the unset value, so an action a variant supplies cannot be taken away
/// (<see cref="WhyNoNone"/>).</item>
/// <item>The player's squad is left out: none of the 138 row-16 records of retail squads has a deployment action, and none of
/// the 278 row-12 records they name an engaged or defeated action (<c>HercWorks.Query values --row 16 --field
/// DeploymentActionRef</c> and <c>--row 12 --field EngagementActionRef</c>, <c>DefeatActionRef</c>, with and without
/// <c>--non-squad</c>), and a squad machine is built from <c>player.mec</c>, not its record
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 6).</item>
/// </list>
/// </summary>
public static class ActionWiring {
	private const int ActionRow = 10;
	private const int MechRow = 12;
	private const int GroupRow = 16;
	private const int SquadBlock = 11;

	/// <summary>Why row-16 group <paramref name="group"/>'s deployment action cannot be set, or null when it can.</summary>
	public static string? WhyNoDeployment(MissionDocument document, short group) =>
		document.ExportedIndex(GroupRow, group) == null ? "The group is not shown in the editor."
			: group == document.GuidOf(SquadBlock, 0) ? "The player's squad is in the mission from the start."
			: null;

	/// <summary>Sets row-16 group <paramref name="group"/>'s deployment action, a row-10 GUID, or <c>-1</c> for none.</summary>
	public static MsnEdit SetDeployment(MissionDocument document, short group, short action) {
		if (WhyNoDeployment(document, group) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		CheckAction(document, action);
		return document.Change(() => document.SetField(MsnField.GroupDeployment, group, action));
	}

	/// <summary>Why row <paramref name="row"/>'s (12 to 14) GUID <paramref name="guid"/>'s own actions cannot be set, or null when they can.</summary>
	public static string? WhyNoObjectAction(MissionDocument document, int row, short guid) {
		if (row is < MechRow or > 14) {
			throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 12 to 14 are the rosters.");
		}

		if (document.ExportedIndex(row, guid) == null) {
			return "It is not shown in the editor.";
		}

		short squad = document.GuidOf(SquadBlock, 0);
		return row == MechRow && squad != -1 && (document.Mission.Groups ?? []).Any(record => record.GUID == squad && record.MemberRefs.Contains(guid))
			? "A machine of the player's squad is built from player.mec, not its record."
			: null;
	}

	/// <summary>
	/// Why <paramref name="field"/> of <paramref name="guid"/> cannot be set to none, or null when it can: a variant supplies it,
	/// and an overlay record cannot carry an unset value.
	/// </summary>
	public static string? WhyNoNone(MissionDocument document, MsnField field, short guid) =>
		document.ExportedIndex(field.Row, guid) != null && document.SuppliedByVariant(field, guid)
			? "A variant supplies it, so it cannot be taken away."
			: null;

	/// <summary>
	/// Sets the action row <paramref name="row"/>'s GUID <paramref name="guid"/> fires when engaged, or with
	/// <paramref name="defeated"/> when defeated, to row-10 GUID <paramref name="action"/>, or none for <c>-1</c>.
	/// </summary>
	public static MsnEdit SetObjectAction(MissionDocument document, int row, short guid, bool defeated, short action) {
		if (WhyNoObjectAction(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var field = MsnField.RosterAction(row, defeated);
		if ((action == -1 ? WhyNoNone(document, field, guid) : null) is { } noNone) {
			throw new InvalidOperationException(noNone);
		}

		CheckAction(document, action);
		return document.Change(() => document.SetField(field, guid, action));
	}

	internal static void CheckAction(MissionDocument document, short action) {
		if (action != -1 && document.ExportedIndex(ActionRow, action) == null) {
			throw new InvalidOperationException("That action is not shown in the editor.");
		}
	}
}
