using System.Text;
using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>wld\worldN</c> (<see cref="Es1WorldData"/>) field by field, in the order of
/// <c>World_LoadTheater</c> (<c>ES1DBSIM 000251c4</c>; docs/es1/formats/wld-world.md#the-layout). Writing a parsed
/// file gives back its bytes.
///
/// <para>A count that runs past the end, a string without its NUL, or bytes left after the fifth string throw
/// <see cref="InvalidDataException"/>, so a file this model would not give back whole is rejected. The retail loader
/// tests none of this.</para>
/// </summary>
public class Es1WorldDataTransformer : ByteTransformer<Es1WorldData> {
	/// <exception cref="InvalidDataException">The file does not walk to its last byte by the layout.</exception>
	public override Es1WorldData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		Need(Es1WorldData.HeaderShorts * 2);
		var wld = new Es1WorldData { Header = IndexShortLEArray(Es1WorldData.HeaderShorts) };

		wld.DistanceBandsA = ReadCountedInts();
		wld.DistanceBandsB = ReadCountedInts();

		Need(4);
		wld.RampRows = IndexShortLE();
		wld.RampColumns = IndexShortLE();
		int columns = wld.RampColumns;
		if (columns < 0) {
			throw new InvalidDataException($"The column count at 0x{Index - 2:x} is {columns}.");
		}

		Need(columns * 8L + 2 + 8 + 12);
		wld.RampTableA = IndexIntLEArray(columns);
		wld.BetweenRampTables = IndexShortLE();
		wld.RampTableB = IndexIntLEArray(columns);
		wld.RampExtraA = IndexSegment(4);
		wld.RampExtraB = IndexSegment(4);

		wld.CloudCount = IndexShortLE();
		wld.PointCount = IndexShortLE();
		wld.DistanceOffsetSmall = IndexIntLE();
		wld.DistanceOffsetLarge = IndexIntLE();

		wld.SkyBank = ReadString();
		wld.CloudBank = ReadString();
		wld.ImpactPalette = ReadString();
		wld.GroundTextureBank = ReadString();
		wld.GroundFadeFile = ReadString();

		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the fifth string at 0x{Index:x}.");
		}

		return wld;
	}

	public override byte[]? Write(Es1WorldData source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);

		foreach (short value in source.Header) {
			w.Write(value);
		}

		WriteCountedInts(w, source.DistanceBandsA);
		WriteCountedInts(w, source.DistanceBandsB);

		w.Write(source.RampRows);
		w.Write(source.RampColumns);
		foreach (int value in source.RampTableA) {
			w.Write(value);
		}

		w.Write(source.BetweenRampTables);
		foreach (int value in source.RampTableB) {
			w.Write(value);
		}

		w.Write(source.RampExtraA);
		w.Write(source.RampExtraB);

		w.Write(source.CloudCount);
		w.Write(source.PointCount);
		w.Write(source.DistanceOffsetSmall);
		w.Write(source.DistanceOffsetLarge);

		foreach (string value in new[] { source.SkyBank, source.CloudBank, source.ImpactPalette, source.GroundTextureBank, source.GroundFadeFile }) {
			w.Write(Encoding.ASCII.GetBytes(value));
			w.Write((byte)0);
		}

		w.Flush();
		return o.ToArray();
	}

	private static void WriteCountedInts(BinaryWriter w, int[] values) {
		w.Write(values.Length);
		foreach (int value in values) {
			w.Write(value);
		}
	}

	/// <summary>An <c>int32</c> count and that many <c>int32</c>.</summary>
	private int[] ReadCountedInts() {
		Need(4);
		int count = IndexIntLE();
		if (count < 0) {
			throw new InvalidDataException($"The count at 0x{Index - 4:x} is {count}.");
		}

		Need(count * 4L);
		return IndexIntLEArray(count);
	}

	/// <summary>One NUL-terminated string, the NUL consumed.</summary>
	private string ReadString() {
		int end = Array.IndexOf(Bytes!, (byte)0, Index);
		if (end < 0) {
			throw new InvalidDataException($"The string at 0x{Index:x} has no NUL.");
		}

		string value = IndexString(end - Index);
		Index++;
		return value;
	}

	private void Need(long bytes) {
		if (Index + bytes > Bytes!.Length) {
			throw new InvalidDataException($"The file ends at 0x{Bytes.Length:x}, {bytes} bytes being needed from 0x{Index:x}.");
		}
	}
}
