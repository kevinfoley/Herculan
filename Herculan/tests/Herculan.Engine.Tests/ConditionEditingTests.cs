using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ConditionEditing"/> and <see cref="MissionGenerator.Conditions"/> over small synthetic missions: a condition
/// can require only an earlier one, a delete is refused while any row or the text references it, the operator's wording
/// reads as the load tests, and the generation's view of row 1 lines up with row 1's records.
/// </summary>
public class ConditionEditingTests {
	private const short Flag = 5;

	/// <summary>
	/// Catches dropping the load-order check: a requirement, or a range's draw, naming a later row-1 record is refused, as
	/// the load reads row 1 in order and such a record never holds.
	/// </summary>
	[Fact]
	public void RequirementsAndDrawsMustComeEarlier() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: Flag, upper: MissionGenerator.OpEqual, operand: 0))
			.Add(1, Condition(guid: 1, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: 2, condition: 1, type: 2, lower: 0, upper: 9, operand: 0))
			.Add(1, Condition(guid: 3, condition: -1, type: 1, lower: 11, upper: 0, operand: 0)), flags: new short[MissionGenerator.CampaignFlagCount]);
		byte[] opened = Write(document);

		Assert.NotNull(ConditionEditing.WhyNoRequirement(document, 0, 1));
		Assert.Throws<InvalidOperationException>(() => ConditionEditing.SetRequirement(document, 0, 1));
		Assert.Throws<InvalidOperationException>(() => ConditionEditing.SetRequirement(document, 2, 3));
		Assert.Equal(opened, Write(document));
		Assert.DoesNotContain((short)3, ConditionEditing.Earlier(document, 2));

		ConditionEditing.SetRequirement(document, 1, 0);
		Assert.Equal(0, ConditionEditing.Record(document, 1).ConditionRef);
		Assert.True(ConditionEditing.Outcome(document, 1).Survived);
	}

	/// <summary>
	/// Catches a namer that misses a row the load gates on: an <c>.ENG</c> record's condition and a row-5 debrief record's
	/// condition word each refuse the delete on their own.
	/// </summary>
	[Fact]
	public void DeleteIsRefusedWhileTheTextOrADebriefReferencesIt() {
		var withText = Open(Mission(), text: new MissionStringFile {
			Strings = [new MissionStringFile.StringEntry { Guid = 200, ConditionRef = 0, ParentRef = -99, Len = 2, Val = "A\0" }],
		});
		Assert.Equal(0, Assert.Single(ConditionEditing.Namers(withText, 0).Text));
		Assert.Throws<InvalidOperationException>(() => ConditionEditing.Delete(withText, 0));

		var debrief = Enumerable.Repeat((short)-1, 32).ToArray();
		debrief[0] = 0;
		var withDebrief = Open(Mission().Add(5, Enumerable.Repeat((short)-1, 32).ToArray()).Add(5, debrief));
		Assert.Equal(new MsnRecordId(5, 1), Assert.Single(ConditionEditing.Namers(withDebrief, 0).Records));
		Assert.Throws<InvalidOperationException>(() => ConditionEditing.Delete(withDebrief, 0));

		var unreferenced = Open(Mission());
		ConditionEditing.Delete(unreferenced, 0);
		Assert.Empty(ConditionEditing.Records(unreferenced));
	}

	/// <summary>
	/// Catches a description that puts an operator on the wrong side (the load's <c>&gt;</c> and <c>&gt;=</c> take the value on
	/// the left): for every operator and a flag below, at and above the value, the flag test holds in the generation exactly
	/// when its description, read as written, is true.
	/// </summary>
	[Fact]
	public void OperatorWordingReadsAsTheLoadTests() {
		const short value = 3;
		foreach (short op in ConditionEditing.Operators) {
			foreach (short flag in new short[] { value - 1, value, value + 1 }) {
				var flags = new short[MissionGenerator.CampaignFlagCount];
				flags[Flag] = flag;
				var document = Open(new SyntheticMsn()
					.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: Flag, upper: op, operand: value)), flags);

				string[] words = ConditionEditing.Describe(document.Mission, 0)!.Split(' ');
				Assert.Equal(["flag", Flag.ToString(), value.ToString()], new[] { words[0], words[1], words[3] });
				bool described = words[2] switch {
					"=" => flag == value,
					"!=" => flag != value,
					"<" => flag < value,
					"<=" => flag <= value,
					">" => flag > value,
					">=" => flag >= value,
					_ => throw new InvalidOperationException(words[2]),
				};

				Assert.Equal(described, ConditionEditing.Outcome(document, 0).Survived);
			}
		}
	}

	/// <summary>
	/// Catches the generation's view of row 1 drifting from row 1's indices (an outcome kept only for survivors, or a draw
	/// reported for a record that made none): a draw whose requirement fails makes no draw, the next draw takes the first
	/// roll, and each range over it holds exactly when that draw is in its ends.
	/// </summary>
	[Fact]
	public void OutcomesLineUpWithRowOne() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: Flag, upper: MissionGenerator.OpEqual, operand: 1))
			.Add(1, Condition(guid: 1, condition: 0, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: 2, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: 3, condition: 2, type: 2, lower: 0, upper: 6, operand: 0))
			.Add(1, Condition(guid: 4, condition: 2, type: 2, lower: 8, upper: 9, operand: 0)),
			new short[MissionGenerator.CampaignFlagCount], rolls: [7, 1]);

		Assert.False(ConditionEditing.Outcome(document, 0).Survived);
		Assert.Equal(new ConditionOutcome(false, null), ConditionEditing.Outcome(document, 1));
		Assert.False(ConditionEditing.RequirementHolds(document, 1));
		short drawn = Assert.IsType<short>(ConditionEditing.Outcome(document, 2).Draw);
		Assert.Equal((short)7, drawn);
		foreach (short range in new short[] { 3, 4 }) {
			var record = ConditionEditing.Record(document, range);
			Assert.Equal(record.FlagIndexOrRangeLower <= drawn && drawn <= record.OperatorOrRangeUpperOrResult,
				ConditionEditing.Outcome(document, range).Survived);
		}
	}

	// One flag test, condition 0, testing flag <Flag> = 0, which holds with every flag 0.
	private static SyntheticMsn Mission() => new SyntheticMsn()
		.Add(1, Condition(guid: 0, condition: -1, type: 0, lower: Flag, upper: MissionGenerator.OpEqual, operand: 0));

	private static MissionDocument Open(SyntheticMsn msn, short[]? flags = null, short[]? rolls = null, MissionStringFile? text = null) {
		var draws = rolls ?? [];
		return MissionDocument.Read(msn.Bytes(), text == null ? null : new MissionStringFileTransformer().Write(text),
			new MissionGeneration(flags ?? new short[MissionGenerator.CampaignFlagCount], new short?[MissionGeneration.HeaderWords],
				() => {
					int next = 0;
					return _ => next < draws.Length ? draws[next++] : 0;
				}));
	}

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
