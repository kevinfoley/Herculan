using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="DebriefText"/> and <see cref="MissionTextEditing.SetLineUnder"/> over small synthetic missions: the outcomes are
/// the conditions of the debrief's lines and movie but the loss line's, and an outcome's edit writes its own record.
/// </summary>
public class DebriefTextTests {
	private const short Line = 7;
	private const short Loss = 9;
	private const short Movie = 20;
	private const short Won = 44;
	private const short Lost = 45;

	/// <summary>
	/// Catches an outcome edit that writes the line's base record (the supplier every other line edit writes), which changes
	/// the text every other outcome and Otherwise show, and one the undo history does not put back.
	/// </summary>
	[Fact]
	public void AnOutcomeEditWritesItsOwnRecordAndNotTheBase() {
		var document = Open(Debrief([Line]), Entry(Line, -1, "otherwise"), Entry(Line, Won, "won"), Entry(Line, Lost, "lost"));

		var edit = MissionTextEditing.SetLineUnder(document, Owner(0), 0, Won, "changed");

		Assert.Equal(["otherwise", "changed", "lost"], Texts(document));
		document.Undo(edit);
		Assert.Equal(["otherwise", "won", "lost"], Texts(document));
	}

	/// <summary>
	/// Catches an edit of a line the text package also references written in place, which changes the briefing too; a copy
	/// that leaves out the line's other records, which drops the other outcomes' text from the debrief; and a row-5 ref the
	/// undo history does not put back.
	/// </summary>
	[Fact]
	public void AnEditOfASharedDebriefLineCopiesEveryRecordOfIt() {
		var package = Text();
		package[MissionText144.ObjectiveLinesWord + 10] = Line; // the first briefing slot, after the ten objective lines
		var msn = Debrief([Line]).Add(4, package);
		var document = Open(msn, Entry(Line, -1, "otherwise"), Entry(Line, Won, "won"));

		var edit = MissionTextEditing.SetLineUnder(document, Owner(0), 0, Won, "changed");

		short copy = DebriefText.Lines(document)[0].Id;
		Assert.NotEqual(Line, copy);
		Assert.Equal("otherwise", MissionTextEditing.TextUnder(document, copy, -1));
		Assert.Equal("changed", MissionTextEditing.TextUnder(document, copy, Won));
		Assert.Equal("won", MissionTextEditing.TextUnder(document, Line, Won));
		Assert.Equal(Line, document.Mission.Texts![0].BriefingLines[0]);
		document.Undo(edit);
		Assert.Equal(Line, DebriefText.Lines(document)[0].Id);
	}

	/// <summary>
	/// Catches the loss line's ten conditions listed as outcomes, an outcome the movie alone carries left out, and an outcome
	/// listed once per record that carries it.
	/// </summary>
	[Fact]
	public void TheOutcomesAreTheLinesAndMoviesConditionsButTheLossLines() {
		const short MovieOnly = 46;
		var msn = Debrief([Loss, Line], movie: Movie)
			.Add(3, [Movie, -1, -1, 57]).Add(3, [Movie, Lost, -99, 51]).Add(3, [Movie, MovieOnly, -99, 39]);
		var entries = new List<MissionStringFile.StringEntry> { Entry(Loss, -1, "") };
		foreach (short condition in AddLossConditions(msn, first: 60)) {
			entries.Add(Entry(Loss, condition, "lost something"));
		}

		entries.AddRange([Entry(Line, -1, "otherwise"), Entry(Line, Won, "won"), Entry(Line, Lost, "lost"), Entry(Line, Won, "won again")]);
		var document = Open(msn, [.. entries]);

		Assert.Equal(Loss, DebriefText.LossLineOf(document)?.Line.Id);
		Assert.Equal([Won, Lost, MovieOnly], DebriefText.Outcomes(document));
	}

	// A mission whose one row-5 record references <lines> from its first slot, and movie GUID <movie>.
	private static SyntheticMsn Debrief(short[] lines, short movie = -1) {
		var words = Enumerable.Repeat((short)-1, DebriefText.RecordLength / 2).ToArray();
		lines.CopyTo(words, 1);
		words[^1] = movie;
		return new SyntheticMsn().Add(5, words);
	}

	// Adds the loss line's conditions to row 1 from GUID <first> up, answering the ten its records carry, in order: flag 8
	// and 9 tests, two ranges over a draw below 11 that requires flag 10 = 1, and two flag 10 = 2 tests
	// (docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line).
	private static IReadOnlyList<short> AddLossConditions(SyntheticMsn msn, short first) {
		var carried = new List<short>();
		short guid = first;
		short Test(short flag, short op, short value, bool carry = true) {
			msn.Add(1, Condition(guid, -1, 0, flag, op, value));
			if (carry) {
				carried.Add(guid);
			}

			return guid++;
		}

		foreach (short flag in new short[] { 8, 9 }) {
			Test(flag, MissionGenerator.OpEqual, 1);
			Test(flag, MissionGenerator.OpEqual, 2);
			Test(flag, MissionGenerator.OpGreater, 2);
		}

		short once = Test(10, MissionGenerator.OpEqual, 1, carry: false);
		short draw = guid++;
		msn.Add(1, Condition(draw, once, 1, 11, 0, 0));
		foreach (var (low, high) in new (short, short)[] { (0, 9), (10, 10) }) {
			msn.Add(1, Condition(guid, draw, 2, low, high, 0));
			carried.Add(guid++);
		}

		Test(10, MissionGenerator.OpEqual, 2);
		Test(10, MissionGenerator.OpEqual, 2);
		return carried;
	}

	private static TextOwner Owner(int slot) => new(5, 0, null, slot);

	// A .ENG record of <id> under <condition>, -99 beside a condition as the files carry it.
	private static MissionStringFile.StringEntry Entry(short id, short condition, string text) {
		var entry = MissionTextEditing.Entry(id, text);
		entry.ConditionRef = condition;
		entry.ParentRef = condition == -1 ? (short)-1 : (short)-99;
		return entry;
	}

	private static string[] Texts(MissionDocument document) =>
		document.Text!.Strings!.Select(entry => entry.Val!.TrimEnd('\0')).ToArray();

	private static MissionDocument Open(SyntheticMsn msn, params MissionStringFile.StringEntry[] entries) =>
		MissionDocument.Read(msn.Bytes(), new MissionStringFileTransformer().Write(new MissionStringFile { Strings = entries })!,
			new MissionGeneration(new short[MissionGenerator.CampaignFlagCount], new short?[MissionGeneration.HeaderWords], () => _ => 0));
}
