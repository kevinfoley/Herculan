using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's flyer type file (<see cref="Es1FlyerSimData"/>; docs/es1/formats/bullets-skimmer-dat.md#datskimmerdat),
/// field by field in file order. Writing a parsed file gives back its bytes.
///
/// <para>A file of any length but <see cref="Es1FlyerSimData.Length"/> throws <see cref="InvalidDataException"/>, so a file
/// this model would not give back whole is rejected. The retail loader reads the first 34 bytes and asserts when the
/// read fails.</para>
/// </summary>
public class Es1FlyerSimDataTransformer : ByteTransformer<Es1FlyerSimData> {
	/// <exception cref="InvalidDataException">The file is not <see cref="Es1FlyerSimData.Length"/> bytes.</exception>
	public override Es1FlyerSimData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		if (inputArray.Length != Es1FlyerSimData.Length) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {Es1FlyerSimData.Length}.");
		}

		return new Es1FlyerSimData {
			SpeedTurn = IndexShortLE(),
			SpeedReverse = IndexShortLE(),
			SpeedForward = IndexShortLE(),
			SpeedAccelDecel = IndexShortLE(),
			TurnAccelDecel = IndexShortLE(),
			CameraPartId = IndexShortLE(),
			AnimId_Walk = IndexShortLE(),
			MaxBankAngle = IndexShortLE(),
			Unk16_val = IndexShortLE(),
			Bytes12 = IndexSegment(16),
		};
	}

	public override byte[]? Write(Es1FlyerSimData source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		foreach (short value in new[] {
			source.SpeedTurn, source.SpeedReverse, source.SpeedForward, source.SpeedAccelDecel, source.TurnAccelDecel,
			source.CameraPartId, source.AnimId_Walk, source.MaxBankAngle, source.Unk16_val,
		}) {
			w.Write(value);
		}

		w.Write(source.Bytes12);
		w.Flush();
		return o.ToArray();
	}
}
