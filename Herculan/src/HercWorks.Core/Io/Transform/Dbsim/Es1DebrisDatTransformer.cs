using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>dat\DEBRIS.DAT</c> (<see cref="Es1DebrisDat"/>; docs/es1/formats/debris-dat.md), the walk of
/// <c>Debris_LoadResources</c> (<c>0000607c</c>) and <c>Debris_LoadPieceList</c> (<c>00005ed0</c>). Writing a parsed file
/// gives back its bytes.
///
/// <para>A walk that runs past the end, a negative count, or bytes left after the last group throw
/// <see cref="InvalidDataException"/>, so a file the model would not give back whole is rejected.</para>
/// </summary>
public class Es1DebrisDatTransformer : ByteTransformer<Es1DebrisDat> {
	/// <exception cref="InvalidDataException">The file does not walk to its end.</exception>
	public override Es1DebrisDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		var groups = new Es1DebrisGroup[Count(inputArray.Length)];
		for (int g = 0; g < groups.Length; g++) {
			Need(2, inputArray.Length);
			short throwCount = IndexShortLE();
			var pieces = new Es1DebrisPiece[Count(inputArray.Length)];
			for (int p = 0; p < pieces.Length; p++) {
				Need(4, inputArray.Length);
				pieces[p] = new Es1DebrisPiece { ShapeIndex = IndexShortLE(), Weight = IndexShortLE() };
			}

			groups[g] = new Es1DebrisGroup { ThrowCount = throwCount, Pieces = pieces };
		}

		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the last group.");
		}

		return new Es1DebrisDat { Groups = groups };
	}

	public override byte[]? Write(Es1DebrisDat source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)source.Groups.Length);
		foreach (var group in source.Groups) {
			w.Write(group.ThrowCount);
			w.Write((short)group.Pieces.Length);
			foreach (var piece in group.Pieces) {
				w.Write(piece.ShapeIndex);
				w.Write(piece.Weight);
			}
		}

		w.Flush();
		return o.ToArray();
	}

	private int Count(int length) {
		Need(2, length);
		short count = IndexShortLE();
		return count >= 0 ? count : throw new InvalidDataException($"A negative count, {count}, at {Index - 2}.");
	}

	private void Need(int bytes, int length) {
		if (Index + bytes > length) {
			throw new InvalidDataException($"The walk runs past the end at {Index}.");
		}
	}
}
