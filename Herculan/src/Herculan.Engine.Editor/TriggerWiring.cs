using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Something that waits on a trigger and acts when it fires, one entry of the trigger view's Then list, named by the record
/// that holds the link: a group's deployment action, an order slot's ending action, or a countdown's starting action
/// (docs/retail/simulation/mission-deployment.md#the-four-ways-an-action-activates).
/// </summary>
internal abstract record TriggerFollower;

/// <summary>Row-16 GUID <paramref name="Group"/> is not in the mission until the trigger fires (its deployment action).</summary>
internal sealed record BringsInGroup(short Group) : TriggerFollower;

/// <summary>Row-16 GUID <paramref name="Group"/> moves on from the order in its slot <paramref name="Slot"/> when the trigger fires.</summary>
internal sealed record EndsOrder(short Group, int Slot) : TriggerFollower;

/// <summary>Row-11 GUID <paramref name="Countdown"/> starts counting down when the trigger fires.</summary>
internal sealed record StartsCountdown(short Countdown) : TriggerFollower;

/// <summary>
/// What fires one trigger and what follows it, as the layout's base fields wire them: the countdowns whose fired slots name it,
/// the placed objects outside the squad that fire it when engaged or defeated, and what follows it (<see cref="Follows"/>),
/// in layout order: the groups waiting on it, then the order slots it ends, then the countdowns it starts.
/// </summary>
internal sealed record TriggerLinks(IReadOnlyList<short> FiringCountdowns, IReadOnlyList<ObjectKey> Engaged,
		IReadOnlyList<ObjectKey> Defeated, IReadOnlyList<TriggerFollower> Follows) {
	public static TriggerLinks None { get; } = new([], [], [], []);

	/// <summary>Whether anything other than its own areas fires it.</summary>
	public bool FiredOtherwise => FiringCountdowns.Count > 0 || Engaged.Count > 0 || Defeated.Count > 0;
}

/// <summary>
/// The links between the layout's triggers (row 10) and the records that fire them or wait on them, by GUID, for
/// <see cref="MissionIndex"/>: every countdown (row 11), every placed herc, flyer, structure and ground vehicle, every group
/// and each of its order slots, each by its base's fields, a conditional one included. A ref to a trigger the layout has no
/// entry for links nothing, as the generation resolves it to none. A squad HERC is left out: it is built from
/// <c>player.mec</c> and fires nothing of its record's (docs/retail/simulation/mission-deployment.md#the-out-of-action-report),
/// and so is a roster record no group places, which the mission never spawns.
/// </summary>
internal static class TriggerWiring {
	private const int ActionRow = 10;

	/// <summary>Each trigger's links in <paramref name="plan"/>; a trigger nothing fires or follows has no entry.</summary>
	public static IReadOnlyDictionary<short, TriggerLinks> Build(LayoutPlan plan) {
		var layout = plan.Layout;
		var links = new Dictionary<short, Builder>();
		Builder Of(short trigger) {
			if (!links.TryGetValue(trigger, out var builder)) {
				links[trigger] = builder = new Builder();
			}

			return builder;
		}

		bool Known(short trigger) => trigger != -1 && layout.Entry(ActionRow, trigger) != null;

		foreach (var countdown in layout.Entries<ActionTimer30>()) {
			foreach (short fired in countdown.Fields.SequenceRefs.Where(Known).Distinct()) {
				Of(fired).Firing.Add(countdown.Guid);
			}
		}

		foreach (var placed in plan.Objects.Where(placed => !placed.Squad)) {
			(short engaged, short defeated) = layout.Entry(placed.Key.Row, placed.Key.Guid)?.Fields switch {
				MechRosterEntry144 herc => (herc.EngagementActionRef, herc.DefeatActionRef),
				FlyerRosterEntry102 flyer => (flyer.EngagementActionRef, flyer.DefeatActionRef),
				BaseRosterEntry62 structure => (structure.EngagementActionRef, structure.DefeatActionRef),
				_ => ((short)-1, (short)-1),
			};

			if (Known(engaged)) {
				Of(engaged).Engaged.Add(placed.Key);
			}

			if (Known(defeated)) {
				Of(defeated).Defeated.Add(placed.Key);
			}
		}

		foreach (var group in plan.Groups.Where(group => Known(group.DeploymentAction))) {
			Of(group.DeploymentAction).Groups.Add(new BringsInGroup(group.Guid));
		}

		foreach (var group in plan.Groups) {
			for (int slot = 0; slot < group.Orders.Count; slot++) {
				if (group.Orders[slot] != -1 && layout.Entry<MissionOrder22>(group.Orders[slot])?.Fields is { } order
						&& Known(order.ActionRef)) {
					Of(order.ActionRef).Orders.Add(new EndsOrder(group.Guid, slot));
				}
			}
		}

		foreach (var countdown in layout.Entries<ActionTimer30>().Where(countdown => Known(countdown.Fields.PrimaryActionRef))) {
			Of(countdown.Fields.PrimaryActionRef).Countdowns.Add(new StartsCountdown(countdown.Guid));
		}

		return links.ToDictionary(pair => pair.Key, pair => pair.Value.Links());
	}

	private sealed class Builder {
		public List<short> Firing { get; } = new();
		public List<ObjectKey> Engaged { get; } = new();
		public List<ObjectKey> Defeated { get; } = new();
		public List<TriggerFollower> Groups { get; } = new();
		public List<TriggerFollower> Orders { get; } = new();
		public List<TriggerFollower> Countdowns { get; } = new();

		public TriggerLinks Links() => new(Firing, Engaged, Defeated, [.. Groups, .. Orders, .. Countdowns]);
	}
}

/// <summary>
/// The record edits behind a trigger's Then list: each entry is written in the record that holds the link, never in the
/// trigger's own (<see cref="TriggerFollower"/>), through Core's wiring edits: a group's deployment action
/// (<see cref="ActionWiring.SetDeployment"/>), an order slot's ending action (<see cref="GroupOrders.SetAction"/>, which copies
/// an order another slot shares first) and a countdown's starting action (<see cref="TimerEditing.SetStart"/>). Removing an
/// entry unsets that one field: the group is then in the mission from the start, the order no longer ends on a trigger, and
/// the countdown counts down from mission start.
/// </summary>
internal static class TriggerFollowers {
	/// <summary>Makes <paramref name="follower"/> wait on row-10 GUID <paramref name="trigger"/>.</summary>
	public static MsnEdit Add(MissionDocument document, short trigger, TriggerFollower follower) => Set(document, follower, trigger);

	/// <summary>Takes <paramref name="follower"/> off whatever trigger it waits on.</summary>
	public static MsnEdit Remove(MissionDocument document, TriggerFollower follower) => Set(document, follower, -1);

	/// <summary>Why <paramref name="follower"/> cannot be set to wait on a trigger, or null when it can.</summary>
	public static string? WhyNoAdd(MissionDocument document, TriggerFollower follower) => follower switch {
		BringsInGroup bring => ActionWiring.WhyNoDeployment(document, bring.Group),
		_ => null,
	};

	private static MsnEdit Set(MissionDocument document, TriggerFollower follower, short trigger) => follower switch {
		BringsInGroup bring => ActionWiring.SetDeployment(document, bring.Group, trigger),
		EndsOrder ends => GroupOrders.SetAction(document, ends.Group, ends.Slot, trigger),
		StartsCountdown starts => TimerEditing.SetStart(document, starts.Countdown, trigger),
		_ => throw new ArgumentOutOfRangeException(nameof(follower), follower, "No such link."),
	};
}
