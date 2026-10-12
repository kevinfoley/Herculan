using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Dbsim;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>bst\TECH_0.DAT</c> and <c>bst\TECH_SNG.DAT</c> (<see cref="Es1TechFile"/>), as <c>Tech_Read</c>
/// (<c>ES1GO 00001e16</c>) reads them.
/// </summary>
public class Es1TechFileTransformer : Es1CountedTableTransformer<Es1TechFile, Es1TechRecord> {
	protected override int RecordLength => Es1TechRecord.Length;

	protected override Es1TechRecord ReadRecord() => new() {
		Kind = IndexShortLE(),
		Index = IndexShortLE(),
		GoodGradeMissions = IndexShortLE(),
		AnyGradeMissions = IndexShortLE(),
	};

	protected override void WriteRecord(BinaryWriter w, Es1TechRecord record) {
		w.Write(record.Kind);
		w.Write(record.Index);
		w.Write(record.GoodGradeMissions);
		w.Write(record.AnyGradeMissions);
	}

	protected override Es1TechFile Create(Es1TechRecord[] records) => new() { Records = records };

	protected override IReadOnlyList<Es1TechRecord> RecordsOf(Es1TechFile file) => file.Records;
}
