using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ObjectiveEditing"/> and <see cref="MissionTextEditing"/> over small synthetic missions: new text takes ids
/// nothing references, a shared text is copied before it is edited, a delete keeps text another record references, the
/// counter writes stay counted, and the text never grows past what DBSIM reads.
/// </summary>
public class ObjectiveEditingTests {
	private const short Hercs = 0;
	private const short Squad = 9;

	/// <summary>
	/// Catches a new failure text whose ids are taken from the <c>.ENG</c>'s largest alone, which here lands on the id an
	/// action references with no record of its own and would hand the action the new text; and three ids or records that are
	/// not consecutive, which DBSIM reads as three consecutive <c>mission.str</c> lines from the first.
	/// </summary>
	[Fact]
	public void ANewObjectiveTakesThreeConsecutiveIdsNothingReferences() {
		var action = Action(30, type: 0);
		action[MissionAction82.TextRefsWord] = 4;
		var document = Open(Mission().Add(10, action), Lines((1, "a"), (2, "b"), (3, "c")));

		ObjectiveEditing.Create(document, ObjectiveEditing.GroupSubject, Squad, out int record);

		var created = document.Mission.Objectives![record]!;
		Assert.Equal(5, created.TextRef);
		Assert.Equal([5, 6, 7], document.Text!.Strings![^3..].Select(entry => entry.Guid).ToArray());
		int line = document.Script.Objectives[ObjectiveEditing.ExportedIndex(document, record)!.Value].TextRef;
		for (int part = 0; part < MissionTextEditing.FailureLines; part++) {
			var supplier = document.Text.Strings[document.Provenance.Lines[line + part].Supplier];
			Assert.Equal(5 + part, supplier.Guid);
		}

		Assert.Equal(0, created.PairCount);
		Assert.Equal(ObjectiveEditing.Mandatory, created.Required);
	}

	/// <summary>
	/// Catches an edit of a failure text two objectives reference written in place, which changes the other objective's text
	/// too, and a copy of the edited line alone, which leaves the edited objective reading two of its lines from the
	/// other's ids, out of order.
	/// </summary>
	[Fact]
	public void AnEditOfASharedFailureTextCopiesItsThreeLines() {
		var document = Open(Mission().Add(17, Objective(text: 1)).Add(17, Objective(text: 1)), Lines((1, "a"), (2, "b"), (3, "c")));

		MissionTextEditing.SetLine(document, TextOwner.Objective(0), 1, "changed");

		var edited = MissionTextEditing.IdsOf(document, TextOwner.Objective(0));
		Assert.NotEqual(1, edited[0]);
		Assert.Equal(["a", "changed", "c"], edited.Select(id => MissionTextEditing.LineText(document, id) ?? "(none)").ToArray());
		Assert.Equal(1, document.Mission.Objectives![1]!.TextRef);
		Assert.Equal("b", MissionTextEditing.LineText(document, 2));
		Assert.False(MissionTextEditing.Shared(document, TextOwner.Objective(0)));
	}

	/// <summary>Catches a text edit the undo history does not put back, which an undo would leave in the <c>.ENG</c>.</summary>
	[Fact]
	public void UndoPutsTheTextBack() {
		var document = Open(Mission().Add(17, Objective(text: 1)), Lines((1, "a \n"), (2, "b"), (3, "c")));

		var edit = MissionTextEditing.SetLine(document, TextOwner.Objective(0), 0, "new");

		Assert.Equal("new\n", document.Text!.Strings![0].Val!.TrimEnd('\0'));
		document.Undo(edit);
		Assert.Equal("a \n", document.Text!.Strings![0].Val!.TrimEnd('\0'));
	}

	/// <summary>
	/// Catches a delete that removes every line of the objective's failure text, which takes away a line the text package
	/// references, and one that removes none, which leaves lines nothing references counting toward DBSIM's 50.
	/// </summary>
	[Fact]
	public void ADeleteKeepsTextAnotherRecordReferences() {
		var package = Text();
		package[MissionText144.ObjectiveLinesWord] = 2;
		var document = Open(Mission().Add(4, package).Add(17, Objective(text: 1)), Lines((1, "a"), (2, "b"), (3, "c")));

		ObjectiveEditing.Delete(document, 0);

		Assert.Empty(document.Mission.Objectives!);
		Assert.Equal([2], document.Text!.Strings!.Select(entry => entry.Guid).ToArray());
		Assert.Equal([0], document.Script.ObjectiveTextRefs);
	}

	/// <summary>
	/// Catches a remove of a counter write that leaves a gap, or leaves the pair count at <c>0x10</c> as it was, which no
	/// retail record has.
	/// </summary>
	[Fact]
	public void ThePairCountTracksTheFilledPairs() {
		var document = Open(Mission().Add(17, Objective(text: -1)), Lines());

		ObjectiveEditing.AddPair(document, 0);
		ObjectiveEditing.AddPair(document, 0);
		ObjectiveEditing.SetPair(document, 0, 1, 300, ObjectiveEditing.CounterDecrement);
		Assert.Equal(2, document.Mission.Objectives![0]!.PairCount);

		ObjectiveEditing.RemovePair(document, 0, 0);

		var objective = document.Mission.Objectives![0]!;
		Assert.Equal(1, objective.PairCount);
		Assert.Equal(((short)300, ObjectiveEditing.CounterDecrement), (objective.Pairs[0].CounterRef, objective.Pairs[0].Op));
		Assert.Equal(((short)-1, (short)-1), (objective.Pairs[1].CounterRef, objective.Pairs[1].Op));
		Assert.Equal(300, document.Script.Objectives[0].CounterRefs[0]);
	}

	/// <summary>
	/// Catches the 50-line cap not checked, or checked one line off: a new objective's three lines refused once they would
	/// take the text past 50, a new line accepted up to the 50th and refused after it.
	/// </summary>
	[Fact]
	public void TextIsNotAddedPastWhatTheSimulatorReads() {
		var text = Enumerable.Range(1, 48).Select(id => ((short)id, $"line {id}")).ToArray();
		var document = Open(Mission().Add(4, Text()), Lines(text));
		byte[] opened = Write(document);

		Assert.NotNull(ObjectiveEditing.WhyNoCreate(document, ObjectiveEditing.GroupSubject, Squad));
		Assert.Throws<InvalidOperationException>(() => ObjectiveEditing.Create(document, ObjectiveEditing.GroupSubject, Squad, out _));
		Assert.Equal(opened, Write(document));

		MissionTextEditing.AddLine(document, MissionTextList.Briefing, out _);
		MissionTextEditing.AddLine(document, MissionTextList.Briefing, out _);
		Assert.Equal(MissionTextEditing.LineCap, MissionTextEditing.Lines(document));
		Assert.NotNull(MissionTextEditing.WhyNoAdd(document, MissionTextList.Briefing));
		Assert.Throws<InvalidOperationException>(() => MissionTextEditing.AddLine(document, MissionTextList.Briefing, out _));
	}

	/// <summary>
	/// Catches a first objective line added to a mission with no text package that appends none or one block 13 is not
	/// written from, and a remove that empties the slot instead of moving the later lines up, which block 13's count reads
	/// as the list ending there.
	/// </summary>
	[Fact]
	public void TheObjectiveLinesStayFilledFromTheFirst() {
		var document = Open(Mission(), Lines());

		MissionTextEditing.AddLine(document, MissionTextList.Objectives, out short first);
		MissionTextEditing.AddLine(document, MissionTextList.Objectives, out short second);

		Assert.Single(document.Mission.Texts!);
		Assert.Equal(-1, document.Mission.Texts![0].MovieRef);
		Assert.Equal(2, document.Script.ObjectiveTextRefs.Length);

		MissionTextEditing.RemoveLine(document, MissionTextList.Objectives, 0);

		Assert.Equal([second, -1], document.Mission.Texts![0].ObjectiveLines[..2]);
		Assert.DoesNotContain(document.Text!.Strings!, entry => entry.Guid == first);
		Assert.Single(document.Script.ObjectiveTextRefs);
	}

	// Group 0, the squad: the player's machine, facing a heading of its own, with no order.
	private static SyntheticMsn Mission() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		return new SyntheticMsn()
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(16, squad);
	}

	// A mandatory row-17 objective on group 0 that the squad is lost, with failure text <text>, writing no counter.
	private static short[] Objective(short text) {
		var words = SyntheticMsn.Objective(condition: -1, kind: 0, subject: Squad);
		words[MissionObjective58.RequiredWord] = 1;
		words[MissionObjective58.RequiredWord + 1] = 1; // the condition code
		words[MissionObjective58.TextRefWord] = text;
		words[MissionObjective58.TextRefWord + 1] = 0; // the pair count
		return words;
	}

	// A .ENG of unconditioned records, in order.
	private static byte[] Lines(params (short Id, string Text)[] lines) =>
		new MissionStringFileTransformer().Write(new MissionStringFile {
			Strings = lines.Select(line => MissionTextEditing.Entry(line.Id, line.Text)).ToArray(),
		})!;

	private static MissionDocument Open(SyntheticMsn msn, byte[] text) =>
		MissionDocument.Read(msn.Bytes(), text, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
