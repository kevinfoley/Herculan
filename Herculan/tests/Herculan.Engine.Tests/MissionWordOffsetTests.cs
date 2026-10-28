using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionGenerator"/> reads <c>.MSN</c> records as arrays of <c>int16</c> words through the
/// <c>…Word</c> constants on the model classes. These pin each constant to its property: a synthetic file
/// whose every word is distinct is parsed by <see cref="MissionFileTransformer"/>, and the word each
/// constant names in the record's word view must be the value the property holds.
/// </summary>
public class MissionWordOffsetTests {
	/// <summary>Row 8's waypoint count, which sits at word 4 and has no property of its own.</summary>
	private const int WaypointCount = 2;

	[Fact]
	public void EveryNamedWordOffsetReadsItsProperty() {
		var rows = new Dictionary<int, short[]>();
		short[] Record(int row, int words) {
			var record = new short[words];
			for (int i = 0; i < words; i++) {
				record[i] = (short)(row * 100 + i + 1);
			}

			rows[row] = record;
			return record;
		}

		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		writer.Write(MissionGenerator.Revision);
		void Row(int row, int words) {
			writer.Write((short)1);
			foreach (short word in Record(row, words)) {
				writer.Write(word);
			}
		}

		Row(1, 7);
		Row(2, 41);
		Row(3, 4);
		Row(4, 72);
		writer.Write((short)0);
		Row(6, 11);
		Row(7, 5);
		writer.Write((short)1);
		short[] route = Record(8, 5);
		route[4] = WaypointCount;
		foreach (short word in route) {
			writer.Write(word);
		}

		writer.Write((short)801);
		writer.Write((short)802);
		Row(9, 6);
		Row(10, 41);
		Row(11, 15);
		Row(12, 72);
		Row(13, 51);
		Row(14, 31);
		Row(15, 11);
		Row(16, 82);
		Row(17, 29);
		writer.Flush();

		var file = new MissionFileTransformer().Parse(stream.ToArray())!;

		var condition = file.Conditions![0];
		short[] words = rows[1];
		Assert.Equal(words[MapObject.GUIDWord], condition.GUID);
		Assert.Equal(words[MissionCondition14.ConditionRefWord], condition.ConditionRef);
		Assert.Equal(words[MissionCondition14.TypeWord], condition.Type);
		Assert.Equal(words[MissionCondition14.FlagIndexOrRangeLowerWord], condition.FlagIndexOrRangeLower);
		Assert.Equal(words[MissionCondition14.OperatorOrRangeUpperOrResultWord], condition.OperatorOrRangeUpperOrResult);
		Assert.Equal(words[MissionCondition14.ComparisonOperandWord], condition.ComparisonOperand);
		Assert.Equal(words[MissionCondition14.LatestDrawWord], condition.LatestDraw);

		var choice = file.MovieChoices![0];
		words = rows[3];
		Assert.Equal(words[MapObject.GUIDWord], choice.GUID);
		Assert.Equal(words[MovieChoice8.ConditionRefWord], choice.ConditionRef);
		Assert.Equal(words[MovieChoice8.ValueWord], choice.Value);

		var text = file.Texts![0];
		words = rows[4];
		Assert.Equal(words[MissionText144.ConditionRefWord], text.ConditionRef);
		AssertSpan(words, MissionText144.ObjectiveLinesWord, text.ObjectiveLines);
		Assert.Equal(words[MissionText144.MovieRefWord], text.MovieRef);

		var point = file.Points![0];
		words = rows[6];
		Assert.Equal(words[MapObject.GUIDWord], point.GUID);
		Assert.Equal(words[MapPoint22.ConditionRefWord], point.ConditionRef);
		Assert.Equal(words[MapPoint22.RandomTableKeyWord], point.RandomTableKey);
		Assert.Equal(words[MapPoint22.SumFlagWord], point.SumFlag);
		AssertInt(words, MapPoint22.XWord, point.X);
		AssertInt(words, MapPoint22.YWord, point.Y);
		AssertInt(words, MapPoint22.ZWord, point.Z);

		var heading = file.Headings![0];
		words = rows[7];
		Assert.Equal(words[MapObject.GUIDWord], heading.GUID);
		Assert.Equal(words[Heading10.ConditionRefWord], heading.ConditionRef);
		Assert.Equal(words[Heading10.RandomTableKeyWord], heading.RandomTableKey);
		Assert.Equal(words[Heading10.DegreesWord], heading.Degrees);

		var group = file.WaypointGroups![0];
		words = rows[8];
		Assert.Equal(words[MapObject.GUIDWord], group.GUID);
		Assert.Equal(words[WaypointGroup.ConditionRefWord], group.ConditionRef);
		Assert.Equal(words[WaypointGroup.RandomTableKeyWord], group.RandomTableKey);
		Assert.Equal(new short[] { 801, 802 }, group.Waypoints);

		var area = file.TriggerAreas![0];
		words = rows[9];
		Assert.Equal(words[MapObject.GUIDWord], area.GUID);
		Assert.Equal(words[TriggerArea12.ConditionRefWord], area.ConditionRef);
		Assert.Equal(words[TriggerArea12.ShapeWord], area.Shape);
		Assert.Equal(words[TriggerArea12.PointRefWord], area.PointRef);
		Assert.Equal(words[TriggerArea12.SecondPointOrRadiusWord], area.SecondPointOrRadius);

		var action = file.Actions![0];
		words = rows[10];
		Assert.Equal(words[MapObject.GUIDWord], action.GUID);
		Assert.Equal(words[MissionAction82.ConditionRefWord], action.ConditionRef);
		Assert.Equal(words[MissionAction82.TypeWord], action.Type);
		AssertSpan(words, MissionAction82.AreaRefsWord, action.AreaRefs);
		AssertSpan(words, MissionAction82.CounterPairsWord, action.CounterPairs);
		AssertSpan(words, MissionAction82.TextRefsWord, action.TextRefs);
		Assert.Equal(words[MissionAction82.MessageIdWord], action.MessageId);
		Assert.Equal(words[MissionAction82.TargetRefWord], action.TargetRef);

		var timer = file.ActionTimers![0];
		words = rows[11];
		Assert.Equal(words[MapObject.GUIDWord], timer.GUID);
		Assert.Equal(words[ActionTimer30.ConditionRefWord], timer.ConditionRef);
		Assert.Equal(words[ActionTimer30.PrimaryActionRefWord], timer.PrimaryActionRef);
		Assert.Equal(words[ActionTimer30.DelayWord], timer.Delay);
		AssertSpan(words, ActionTimer30.SequenceRefsWord, timer.SequenceRefs);

		var mech = file.Mechs![0];
		words = rows[12];
		Assert.Equal(words[MapObject.GUIDWord], mech.GUID);
		Assert.Equal(words[MechRosterEntry144.ConditionRefWord], mech.ConditionRef);
		Assert.Equal(words[MechRosterEntry144.RandomTableKeyWord], mech.RandomTableKey);
		Assert.Equal(words[MechRosterEntry144.AiRadarActiveWord], mech.AiRadarActive);
		Assert.Equal(words[MechRosterEntry144.TypeIndexWord], mech.TypeIndex);
		AssertSpan(words, MechRosterEntry144.WeaponRefsWord, mech.WeaponRefs);
		Assert.Equal(words[MechRosterEntry144.PositionRefWord], mech.PositionRef);
		Assert.Equal(words[MechRosterEntry144.HeadingRefWord], mech.HeadingRef);
		AssertSpan(words, MechRosterEntry144.OutOfActionReportWord, mech.OutOfActionReport);
		AssertSpan(words, MechRosterEntry144.WeaponSecondaryWord, mech.WeaponSecondary);
		Assert.Equal(words[MechRosterEntry144.Constant2Word], mech.Constant2);
		Assert.Equal(words[MechRosterEntry144.EngagementActionRefWord], mech.EngagementActionRef);
		Assert.Equal(words[MechRosterEntry144.DefeatActionRefWord], mech.DefeatActionRef);
		Assert.Equal(words[MechRosterEntry144.StartingConditionWord], mech.StartingCondition);

		var flyer = file.Flyers![0];
		words = rows[13];
		Assert.Equal(words[MapObject.GUIDWord], flyer.GUID);
		Assert.Equal(words[FlyerRosterEntry102.ConditionRefWord], flyer.ConditionRef);
		Assert.Equal(words[FlyerRosterEntry102.RandomTableKeyWord], flyer.RandomTableKey);
		AssertSpan(words, FlyerRosterEntry102.FlagSpanWord, flyer.FlagSpan);
		Assert.Equal(words[FlyerRosterEntry102.PositionRefWord], flyer.PositionRef);
		Assert.Equal(words[FlyerRosterEntry102.HeadingRefWord], flyer.HeadingRef);
		AssertSpan(words, FlyerRosterEntry102.OutOfActionReportWord, flyer.OutOfActionReport);
		Assert.Equal(words[FlyerRosterEntry102.EngagementActionRefWord], flyer.EngagementActionRef);
		Assert.Equal(words[FlyerRosterEntry102.DefeatActionRefWord], flyer.DefeatActionRef);
		Assert.Equal(words[FlyerRosterEntry102.UnkVal_100Word], flyer.UnkVal_100);

		var structure = file.Bases![0];
		words = rows[14];
		Assert.Equal(words[MapObject.GUIDWord], structure.GUID);
		Assert.Equal(words[BaseRosterEntry62.ConditionRefWord], structure.ConditionRef);
		Assert.Equal(words[BaseRosterEntry62.RandomTableKeyWord], structure.RandomTableKey);
		Assert.Equal(words[BaseRosterEntry62.TypeIndexWord], structure.TypeIndex);
		Assert.Equal(words[BaseRosterEntry62.PositionRefWord], structure.PositionRef);
		Assert.Equal(words[BaseRosterEntry62.HeadingRefWord], structure.HeadingRef);
		AssertSpan(words, BaseRosterEntry62.OutOfActionReportWord, structure.OutOfActionReport);
		Assert.Equal(words[BaseRosterEntry62.EngagementActionRefWord], structure.EngagementActionRef);
		Assert.Equal(words[BaseRosterEntry62.DefeatActionRefWord], structure.DefeatActionRef);
		Assert.Equal(words[BaseRosterEntry62.StartingConditionWord], structure.StartingCondition);

		var order = file.Orders![0];
		words = rows[15];
		Assert.Equal(words[MapObject.GUIDWord], order.GUID);
		Assert.Equal(words[MissionOrder22.ConditionRefWord], order.ConditionRef);
		Assert.Equal(words[MissionOrder22.RandomTableKeyWord], order.RandomTableKey);
		Assert.Equal(words[MissionOrder22.VerbWord], order.Verb);
		Assert.Equal(words[MissionOrder22.PointRefWord], order.PointRef);
		Assert.Equal(words[MissionOrder22.RouteRefWord], order.RouteRef);
		Assert.Equal(words[MissionOrder22.SubjectKindWord], order.SubjectKind);
		Assert.Equal(words[MissionOrder22.SubjectRefWord], order.SubjectRef);
		Assert.Equal(words[MissionOrder22.ActionRefWord], order.ActionRef);

		var squad = file.Groups![0];
		words = rows[16];
		Assert.Equal(words[MapObject.GUIDWord], squad.GUID);
		Assert.Equal(words[MissionGroup164.ConditionRefWord], squad.ConditionRef);
		Assert.Equal(words[MissionGroup164.PaintsGroundWord], squad.PaintsGround);
		Assert.Equal(words[MissionGroup164.MemberKindWord], squad.MemberKind);
		Assert.Equal(words[MissionGroup164.PositionRefWord], squad.PositionRef);
		Assert.Equal(words[MissionGroup164.HeadingRefWord], squad.HeadingRef);
		Assert.Equal(words[MissionGroup164.RouteRefWord], squad.RouteRef);
		AssertSpan(words, MissionGroup164.MemberRefsWord, squad.MemberRefs);
		AssertSpan(words, MissionGroup164.OrderRefsWord, squad.OrderRefs);
		Assert.Equal(words[MissionGroup164.SideWord], squad.Side);
		Assert.Equal(words[MissionGroup164.DeploymentActionRefWord], squad.DeploymentActionRef);
		AssertSpan(words, MissionGroup164.OutOfActionReportWord, squad.OutOfActionReport);
		Assert.Equal(words[MissionGroup164.MapShownWord], squad.MapShown);

		var objective = file.Objectives![0]!;
		words = rows[17];
		Assert.Equal(words[MissionObjective58.ConditionRefWord], objective.ConditionRef);
		Assert.Equal(words[MissionObjective58.RequiredWord], objective.Required);
		Assert.Equal(words[MissionObjective58.SubjectKindWord], objective.SubjectKind);
		Assert.Equal(words[MissionObjective58.SubjectRefWord], objective.SubjectRef);
		Assert.Equal(words[MissionObjective58.PointRefWord], objective.PointRef);
		Assert.Equal(words[MissionObjective58.RouteRefWord], objective.RouteRef);
		Assert.Equal(words[MissionObjective58.TextRefWord], objective.TextRef);
		for (int pair = 0; pair < objective.Pairs.Length; pair++) {
			Assert.Equal(words[MissionObjective58.PairsWord + pair * 2], objective.Pairs[pair].CounterRef);
			Assert.Equal(words[MissionObjective58.PairsWord + pair * 2 + 1], objective.Pairs[pair].Op);
		}
	}

	private static void AssertSpan(short[] words, int first, short[] property) =>
		Assert.Equal(words[first..(first + property.Length)], property);

	private static void AssertInt(short[] words, int first, int property) =>
		Assert.Equal((ushort)words[first] | (words[first + 1] << 16), property);
}
