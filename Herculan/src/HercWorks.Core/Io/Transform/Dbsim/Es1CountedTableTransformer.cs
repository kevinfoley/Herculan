namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// The shape several of ES1's <c>dat\</c> tables share: an <c>int16</c> count, then that many fixed-length records and
/// nothing after them (<c>MFORMS</c>, <c>FFORMS</c>, <c>BFORMS</c>, <c>STYLES</c>; docs/es1/formats/formations-dat.md,
/// styles-dat.md). Writing a parsed file gives back its bytes.
///
/// <para>A file whose length is not 2 + its count × the record length throws <see cref="InvalidDataException"/>, so a file
/// the model would not give back whole is rejected. The retail loaders check neither.</para>
/// </summary>
public abstract class Es1CountedTableTransformer<TFile, TRecord> : ByteTransformer<TFile> where TFile : class {
	/// <summary>A record's length in the file.</summary>
	protected abstract int RecordLength { get; }

	/// <summary>Reads one record at the cursor.</summary>
	protected abstract TRecord ReadRecord();

	/// <summary>Writes one record, <see cref="RecordLength"/> bytes.</summary>
	protected abstract void WriteRecord(BinaryWriter w, TRecord record);

	/// <summary>The file holding <paramref name="records"/>.</summary>
	protected abstract TFile Create(TRecord[] records);

	/// <summary>The file's records, in file order.</summary>
	protected abstract IReadOnlyList<TRecord> RecordsOf(TFile file);

	/// <exception cref="InvalidDataException">The file is not 2 + its count × the record length bytes.</exception>
	public override TFile? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		short count = inputArray.Length >= 2 ? IndexShortLE() : (short)-1;
		if (count < 0 || inputArray.Length != 2 + count * RecordLength) {
			throw new InvalidDataException(
				$"The file is {inputArray.Length} bytes, not 2 + {RecordLength} × its count {count}.");
		}

		var records = new TRecord[count];
		for (int i = 0; i < count; i++) {
			records[i] = ReadRecord();
		}

		return Create(records);
	}

	public override byte[]? Write(TFile source) {
		var records = RecordsOf(source);
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)records.Count);
		foreach (var record in records) {
			WriteRecord(w, record);
		}

		w.Flush();
		return o.ToArray();
	}
}
