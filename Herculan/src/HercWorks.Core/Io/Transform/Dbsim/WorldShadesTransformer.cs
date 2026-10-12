using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes <c>dat\shades.dat</c> (<see cref="WorldShades"/>) in <c>World_LoadShades</c>'s order
/// (docs/es1/rendering/polygon-shading.md#the-shades-file). Writing a parsed file gives back its bytes.
///
/// <para>A list that runs past the end, or bytes left after the last list, throw <see cref="InvalidDataException"/>.
/// The retail loader tests neither.</para>
/// </summary>
public class WorldShadesTransformer : ByteTransformer<WorldShades> {
	/// <exception cref="InvalidDataException">The file does not walk to its last byte by the layout.</exception>
	public override WorldShades? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		Need(2);
		var shades = new WorldShades { TagCapacity = IndexByte() };
		int lists = IndexByte();
		for (int i = 0; i < lists; i++) {
			Need(2);
			byte tag = IndexByte();
			int count = IndexByte();
			Need(count * 2);
			var pairs = new (byte, byte)[count];
			for (int k = 0; k < count; k++) {
				pairs[k] = (IndexByte(), IndexByte());
			}

			shades.Lists.Add(new WorldShadesList(tag, pairs));
		}

		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the last list at 0x{Index:x}.");
		}

		return shades;
	}

	public override byte[]? Write(WorldShades source) {
		using var o = new MemoryStream();
		o.WriteByte(source.TagCapacity);
		o.WriteByte((byte)source.Lists.Count);
		foreach (var list in source.Lists) {
			o.WriteByte(list.Tag);
			o.WriteByte((byte)list.Pairs.Count);
			foreach (var (ramp, column) in list.Pairs) {
				o.WriteByte(ramp);
				o.WriteByte(column);
			}
		}

		return o.ToArray();
	}

	private void Need(int bytes) {
		if (Index + bytes > Bytes!.Length) {
			throw new InvalidDataException($"{bytes} bytes are needed at 0x{Index:x}, past the file's end.");
		}
	}
}
