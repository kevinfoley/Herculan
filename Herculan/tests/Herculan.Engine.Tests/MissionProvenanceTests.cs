using System.Text;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionProvenance"/> over small synthetic missions: each exported slot names the <c>.MSN</c>
/// record that supplies it, and that record, read back through <see cref="MissionFileTransformer"/>,
/// holds the value <c>script.dat</c> carries.
/// </summary>
public class MissionProvenanceTests {
	private const int FormationWord = MissionOrder22.VerbWord + 1;

	/// <summary>Catches a block written from the wrong row in the block-to-row map, block 13 included.</summary>
	[Fact]
	public void EachRowReachesItsOwnBlock() {
		var msn = new Msn();
		msn.Add(4, Text());
		msn.Add(6, Point(1, 100));
		msn.Add(7, Heading(1, 90));
		var (generator, file, script) = Generate(msn);

		var heading = generator.Provenance.Slot(2, 0)!;
		Assert.Equal(new MsnRecordId(7, 0), heading.Created);
		Assert.Equal(script.Headings[0].Degrees, file.Headings![heading.Word(Heading10.DegreesWord).Record.Index].Degrees);
		Assert.Equal(new ScriptDatSlot(1, 0), generator.Provenance.ExportedSlot(new MsnRecordId(6, 0)));
		Assert.Equal(new ScriptDatSlot(13, 0), generator.Provenance.ExportedSlot(new MsnRecordId(4, 0)));
	}

	/// <summary>Catches a whole-record replace that does not report its write, leaving the first record as the supplier.</summary>
	[Fact]
	public void ReplacedPointIsSuppliedByTheLastSurvivor() {
		var msn = new Msn();
		msn.Add(6, Point(1, 100));
		msn.Add(6, Point(1, 200));
		var (generator, file, script) = Generate(msn);

		var slot = generator.Provenance.Slot(1, 0)!;
		var supplier = slot.Word(MapPoint22.XWord).Record;
		Assert.Equal(new MsnRecordId(6, 0), slot.Created);
		Assert.Equal(new[] { new MsnRecordId(6, 1) }, slot.Merged);
		Assert.Equal(script.Coordinates[0].X, file.Points![supplier.Index].X);
		Assert.Equal(new ScriptDatSlot(1, 0), generator.Provenance.ExportedSlot(new MsnRecordId(6, 1)));
	}

	/// <summary>Catches an overlay reported for its whole span, or for the words it leaves because they are unset.</summary>
	[Fact]
	public void OverlayKeepsASupplierPerWord() {
		var msn = new Msn();
		msn.Add(15, Order(1, verb: 5, formation: -1));
		msn.Add(15, Order(1, verb: -1, formation: 3));
		var (generator, file, script) = Generate(msn);

		var slot = generator.Provenance.Slot(10, 0)!;
		var verb = slot.Word(MissionOrder22.VerbWord).Record;
		var formation = slot.Word(FormationWord).Record;
		Assert.NotEqual(verb, formation);
		Assert.Equal(script.Orders[0].Verb, file.Orders![verb.Index].Verb);
		Assert.Equal(script.Orders[0].FormationId, file.Orders![formation.Index].FormationId);
	}

	/// <summary>Catches a variant copy that is not reported, or is resolved against the wrong source slot.</summary>
	[Fact]
	public void VariantCopyNamesTheSourceRecord() {
		var msn = new Msn();
		msn.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 6));
		msn.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0));
		msn.Add(6, Point(-1, 100));
		msn.Add(6, Point(-1, 500, condition: 11));
		msn.Add(6, Point(1, 0, variant: 7));
		var (generator, file, script) = Generate(msn);

		var slot = generator.Provenance.Slot(1, 0)!;
		var x = slot.Word(MapPoint22.XWord);
		Assert.Equal(new MsnRecordId(6, 2), x.Record);
		Assert.Equal(script.Coordinates[0].X, file.Points![x.CopiedFrom!.Value.Index].X);
		Assert.Null(slot.Word(MapObject.GUIDWord).CopiedFrom);
		var copy = Assert.Single(slot.VariantCopies);
		Assert.Equal(x.CopiedFrom, copy.Source);
		Assert.Contains(MapPoint22.ZWord + 1, copy.Words);
	}

	/// <summary>Catches an exported index that counts the records whose GUID is -1.</summary>
	[Fact]
	public void GuidMinusOneRecordIsNotExported() {
		var msn = new Msn();
		msn.Add(6, Point(1, 100));
		msn.Add(6, Point(-1, 200));
		msn.Add(6, Point(2, 300));
		var (generator, file, script) = Generate(msn);

		var slot = generator.Provenance.Slot(1, 1)!;
		Assert.Equal(script.Coordinates.Length, generator.Provenance.Slots(1).Count);
		Assert.Equal(script.Coordinates[1].X, file.Points![slot.Created.Index].X);
		Assert.Null(generator.Provenance.ExportedSlot(new MsnRecordId(6, 1)));
	}

	/// <summary>Catches a record index counted among the survivors rather than the records read, in a GUID row and in row 17.</summary>
	[Fact]
	public void FailedRecordStillTakesAnIndex() {
		var msn = new Msn();
		msn.Add(6, Point(1, 100, condition: 99));
		msn.Add(6, Point(2, 300));
		msn.Add(17, Objective(condition: 99));
		msn.Add(17, Objective(condition: -1));
		var (generator, _, _) = Generate(msn);

		Assert.Equal(new MsnRecordId(6, 1), generator.Provenance.Slot(1, 0)!.Created);
		Assert.Equal(new MsnRecordId(17, 1), generator.Provenance.Slot(12, 0)!.Created);
		Assert.Null(generator.Provenance.ExportedSlot(new MsnRecordId(6, 0)));
	}

	/// <summary>Catches a replaced <c>.ENG</c> line that keeps naming the record it replaced, or that is given a line of its own.</summary>
	[Fact]
	public void ReplacedEngLineTakesTheLaterRecord() {
		var eng = Eng((5, "first"), (6, "other"), (5, "second"));
		var generator = MissionGenerator.Load(new Msn().Bytes(), eng, new short[MissionGenerator.CampaignFlagCount],
			new short[MissionGenerator.ClearListLength], _ => 0);
		var strings = new MissionStringFileTransformer().Parse(eng)!;

		var line = generator.Provenance.Lines[0];
		Assert.Equal(generator.TextLines.Count, generator.Provenance.Lines.Count);
		Assert.Equal(0, line.Created);
		Assert.Equal(generator.TextLines[0], strings.Strings![line.Supplier].Val);
		Assert.Equal(0, generator.Provenance.LineOf(2));
	}

	private static (MissionGenerator Generator, MissionFile File, HercWorks.Core.Data.File.Msn.Script.ScriptDat Script) Generate(Msn msn) {
		byte[] bytes = msn.Bytes();
		var generator = MissionGenerator.Load(bytes, null, new short[MissionGenerator.CampaignFlagCount],
			new short[MissionGenerator.ClearListLength], _ => 0);
		var file = new MissionFileTransformer().Parse(bytes)!;
		var script = new ScriptDatTransformer().Parse(generator.WriteScriptDat())!;
		return (generator, file, script);
	}

	private static short[] Condition(short guid, short condition, short type, short lower, short upper, short operand) {
		var words = new short[7];
		words[MapObject.GUIDWord] = guid;
		words[MissionCondition14.ConditionRefWord] = condition;
		words[MissionCondition14.TypeWord] = type;
		words[MissionCondition14.FlagIndexOrRangeLowerWord] = lower;
		words[MissionCondition14.OperatorOrRangeUpperOrResultWord] = upper;
		words[MissionCondition14.ComparisonOperandWord] = operand;
		return words;
	}

	private static short[] Text() => Enumerable.Repeat((short)-1, 72).ToArray();

	private static short[] Point(short guid, int x, short condition = -1, short variant = -1) {
		var words = new short[11];
		words[MapObject.GUIDWord] = guid;
		words[MapPoint22.ConditionRefWord] = condition;
		words[MapPoint22.VariantKeyWord] = variant;
		words[MapPoint22.XWord] = (short)x;
		words[MapPoint22.XWord + 1] = (short)(x >> 16);
		return words;
	}

	private static short[] Heading(short guid, short degrees) {
		var words = new short[5];
		words[MapObject.GUIDWord] = guid;
		words[Heading10.ConditionRefWord] = -1;
		words[Heading10.VariantKeyWord] = -1;
		words[Heading10.DegreesWord] = degrees;
		return words;
	}

	private static short[] Order(short guid, short verb, short formation) {
		var words = Enumerable.Repeat((short)-1, 11).ToArray();
		words[MapObject.GUIDWord] = guid;
		words[MissionOrder22.VerbWord] = verb;
		words[FormationWord] = formation;
		words[MissionOrder22.SubjectKindWord] = 1;
		return words;
	}

	private static short[] Objective(short condition) {
		var words = Enumerable.Repeat((short)-1, 29).ToArray();
		words[MissionObjective58.ConditionRefWord] = condition;
		words[MissionObjective58.SubjectKindWord] = 1;
		return words;
	}

	/// <summary>An <c>.ENG</c>: per record its id, no condition, a word nothing reads, the length and the text.</summary>
	private static byte[] Eng(params (short Id, string Text)[] records) {
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		writer.Write((short)records.Length);
		foreach (var (id, text) in records) {
			writer.Write(id);
			writer.Write((short)-1);
			writer.Write((short)0);
			writer.Write((short)text.Length);
			writer.Write(Encoding.ASCII.GetBytes(text));
		}

		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>A revision-5 <c>.MSN</c> built row by row; a row given nothing is written with a count of 0.</summary>
	private sealed class Msn {
		private readonly Dictionary<int, List<short[]>> _rows = new();

		public void Add(int row, short[] words) {
			if (!_rows.TryGetValue(row, out var records)) {
				_rows[row] = records = new();
			}

			records.Add(words);
		}

		public byte[] Bytes() {
			using var stream = new MemoryStream();
			var writer = new BinaryWriter(stream);
			writer.Write(MissionGenerator.Revision);
			for (int row = 1; row <= 17; row++) {
				var records = _rows.GetValueOrDefault(row) ?? [];
				writer.Write((short)records.Count);
				foreach (short word in records.SelectMany(record => record)) {
					writer.Write(word);
				}
			}

			writer.Flush();
			return stream.ToArray();
		}
	}
}
