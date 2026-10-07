using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>Transforms byte[] data to and from LC_WPNS.DAT (see <see cref="LcWeaponData"/>).</summary>
public class LcWeaponDataTransformer : ByteTransformer<LcWeaponData> {
	public override LcWeaponData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}
		SetBytes(inputArray);

		short total = IndexShortLE();
		var data = new LcWeaponData { Total = total, Entries = new LcWeaponSlot[total] };

		for (int i = 0; i < total; i++) {
			data.Entries[i] = new LcWeaponSlot {
				PitchArc = IndexShortLE(),
				YawArc = IndexShortLE(),
				Range = IndexIntLE(),
				OffsetX = IndexIntLE(),
				OffsetY = IndexIntLE(),
				OffsetZ = IndexIntLE(),
				RefireDelay = IndexShortLE()
			};
		}

		return data;
	}

	public override byte[]? Write(LcWeaponData data) {
		if (data?.Entries == null) {
			return null;
		}

		using var outStream = new MemoryStream();
		Emit(outStream, WriteShortLE((short)data.Entries.Length));

		foreach (var entry in data.Entries) {
			Emit(outStream, WriteShortLE(entry.PitchArc));
			Emit(outStream, WriteShortLE(entry.YawArc));
			Emit(outStream, WriteIntLE(entry.Range));
			Emit(outStream, WriteIntLE(entry.OffsetX));
			Emit(outStream, WriteIntLE(entry.OffsetY));
			Emit(outStream, WriteIntLE(entry.OffsetZ));
			Emit(outStream, WriteShortLE(entry.RefireDelay));
		}

		return outStream.ToArray();
	}

	private static void Emit(MemoryStream outArr, byte[] data) => outArr.Write(data, 0, data.Length);
}
