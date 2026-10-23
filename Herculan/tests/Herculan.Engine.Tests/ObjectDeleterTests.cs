using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.Scene;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ObjectDeleter.Resolve"/>, which names what refuses a delete by its identity in the layout, over a small
/// synthetic mission.
/// </summary>
public class ObjectDeleterTests {
	private const short Structures = 2;
	private const short FlagFiveIsSet = 20;

	/// <summary>
	/// Catches the references resolved through the generation again: an order that a group the mission as opened leaves out
	/// runs would be counted as run by no group, and its group would have no link.
	/// </summary>
	[Fact]
	public void AnOrderRunByAGroupTheGenerationDropsIsNamedByThatGroup() {
		var document = MissionDocument.Read(new SyntheticMsn()
				.Add(1, Condition(guid: FlagFiveIsSet, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
				.Add(14, Base(7))
				.Add(15, GroupOrder(30, 1))
				.Add(16, Group(0, 0))
				.Add(16, Group(1, Structures, position: -1, condition: -1, 7))
				.Add(16, Orders(Group(2, Structures, position: -1, condition: FlagFiveIsSet), 30))
				.Bytes(),
			null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount], new short?[MissionGeneration.HeaderWords], () => _ => 0));
		var index = new MissionIndex(LayoutPlacement.Place(document.Layout, LayoutFeaturesTests.Tables), new Dictionary<ObjectKey, SceneObject>());
		var deleter = new ObjectDeleter(document, index, null!, null!);

		var names = deleter.Resolve(document, RosterDeletion.GroupNamers(document, 1));

		Assert.Null(Exported(document, 16, 2));
		Assert.Equal([new EndsOrder(2, 0)], names.Orders);
		Assert.Equal(0, names.UnrunOrders);
	}

	// <group> with <order> in its first order slot.
	private static short[] Orders(short[] group, short order) {
		group[MissionGroup164.OrderRefsWord] = order;
		return group;
	}
}
