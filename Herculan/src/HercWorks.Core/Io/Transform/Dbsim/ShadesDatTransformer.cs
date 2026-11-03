using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>Transforms byte[] data to and from SHADES.DAT (see <see cref="ShadesDat"/>).</summary>
public class ShadesDatTransformer : ByteTransformer<ShadesDat> {
	public override ShadesDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length < 2) {
			return null;
		}

		SetBytes(inputArray);
		var data = new ShadesDat { TagCount = IndexByte() };
		int lists = IndexByte();
		for (int i = 0; i < lists; i++) {
			byte tag = IndexByte();
			var repoints = new ShadeRepoint[IndexByte()];
			for (int j = 0; j < repoints.Length; j++) {
				repoints[j] = new ShadeRepoint(IndexByte(), IndexByte());
			}

			data.Lists.Add(new ShadeList(tag, repoints));
		}

		if (Index != inputArray.Length) {
			throw new InvalidDataException(
				$"SHADES.DAT ends at byte {Index} of {inputArray.Length}.");
		}

		return data;
	}

	public override byte[]? Write(ShadesDat source) {
		if (source.Lists.Count > byte.MaxValue) {
			return null;
		}

		using var outStream = new MemoryStream();
		outStream.WriteByte(source.TagCount);
		outStream.WriteByte((byte)source.Lists.Count);
		foreach (var list in source.Lists) {
			if (list.Repoints.Length > byte.MaxValue) {
				return null;
			}

			outStream.WriteByte(list.Tag);
			outStream.WriteByte((byte)list.Repoints.Length);
			foreach (var repoint in list.Repoints) {
				outStream.WriteByte(repoint.Ramp);
				outStream.WriteByte(repoint.Column);
			}
		}

		return outStream.ToArray();
	}
}
