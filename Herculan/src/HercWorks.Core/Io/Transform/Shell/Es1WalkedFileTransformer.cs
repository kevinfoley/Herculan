using System.Text;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// The shape of ES1's shell readers for files walked field by field from the first byte to the last, with no length of
/// their own: <see cref="ReadFile"/> reads the model from the cursor and <see cref="WriteFile"/> writes it back, and writing
/// a parsed file gives back its bytes.
///
/// <para>A file that ends inside a field, and bytes left after the model, throw <see cref="InvalidDataException"/>, so a file
/// the model would not give back whole is rejected. The retail loaders check neither.</para>
/// </summary>
public abstract class Es1WalkedFileTransformer<T> : ByteTransformer<T> where T : class {
	/// <summary>Reads the file's content from the cursor; the base checks that it ends where the file does.</summary>
	protected abstract T ReadFile();

	/// <summary>Writes the file's content.</summary>
	protected abstract void WriteFile(BinaryWriter w, T source);

	/// <exception cref="InvalidDataException">The bytes do not walk to the file's end.</exception>
	public sealed override T? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		T file = ReadFile();
		if (Index != inputArray.Length) {
			throw new InvalidDataException($"{inputArray.Length - Index} bytes follow the last record, at {Index}.");
		}

		return file;
	}

	public sealed override byte[]? Write(T source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		WriteFile(w, source);
		w.Flush();
		return o.ToArray();
	}

	/// <summary>A byte at the cursor.</summary>
	protected byte ReadByte() {
		Require(1);
		return IndexByte();
	}

	/// <summary>An <c>int16</c> at the cursor.</summary>
	protected short ReadInt16() {
		Require(2);
		return IndexShortLE();
	}

	/// <summary>An <c>int32</c> at the cursor.</summary>
	protected int ReadInt32() {
		Require(4);
		return IndexIntLE();
	}

	/// <summary><paramref name="count"/> <c>int16</c> at the cursor.</summary>
	protected short[] ReadInt16s(int count) {
		Require(2 * count);
		return IndexShortLEArray(count);
	}

	/// <summary><paramref name="count"/> bytes at the cursor.</summary>
	protected byte[] ReadBytes(int count) {
		Require(count);
		return IndexSegment(count);
	}

	/// <summary>
	/// A NUL-terminated name at the cursor that <c>Stream_ReadCString</c> (<c>ES1GO 00020154</c>) reads into
	/// <paramref name="bufferLength"/> bytes. A name with no NUL in that many bytes throws: the retail reader takes it whole,
	/// with no NUL, and the next field starts after it, which the model could not give back.
	/// </summary>
	protected string ReadCString(int bufferLength) {
		int end = Array.IndexOf(Bytes!, (byte)0, Index, Math.Min(bufferLength, Bytes!.Length - Index));
		if (end < 0) {
			throw new InvalidDataException($"The name at {Index} has no NUL in its {bufferLength} bytes.");
		}

		string name = Encoding.Latin1.GetString(Bytes!, Index, end - Index);
		Index = end + 1;
		return name;
	}

	/// <summary>Writes <paramref name="name"/> and its NUL.</summary>
	protected static void WriteCString(BinaryWriter w, string name) {
		w.Write(Encoding.Latin1.GetBytes(name));
		w.Write((byte)0);
	}

	/// <summary>Writes each of <paramref name="values"/>.</summary>
	protected static void WriteInt16s(BinaryWriter w, IEnumerable<short> values) {
		foreach (short value in values) {
			w.Write(value);
		}
	}

	/// <summary>
	/// A HERC record at the cursor, as <c>Herc_ReadFromStream</c> (<c>ES1GO 0002352f</c>) reads it
	/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-record).
	/// </summary>
	protected Data.File.Dat.Shell.Es1HercRecord ReadHerc() {
		var herc = new Data.File.Dat.Shell.Es1HercRecord { Chassis = ReadInt16() };
		short mounts = ReadInt16();
		if (mounts < 0) {
			throw new InvalidDataException($"The mount count at {Index - 2} is {mounts}.");
		}

		herc.Weapons = ReadInt16s(mounts);
		herc.MountWords = ReadInt16s(mounts);
		herc.Word = ReadInt16();
		herc.Status = ReadInt16s(Data.File.Dat.Shell.Es1HercRecord.StatusLength);
		return herc;
	}

	/// <summary>Writes a HERC record in <see cref="ReadHerc"/>'s layout.</summary>
	protected static void WriteHerc(BinaryWriter w, Data.File.Dat.Shell.Es1HercRecord herc) {
		if (herc.MountWords.Length != herc.Weapons.Length || herc.Status.Length != Data.File.Dat.Shell.Es1HercRecord.StatusLength) {
			throw new InvalidDataException("A HERC needs one mount word per weapon and a 33-word status block.");
		}

		w.Write(herc.Chassis);
		w.Write(checked((short)herc.Weapons.Length));
		WriteInt16s(w, herc.Weapons);
		WriteInt16s(w, herc.MountWords);
		w.Write(herc.Word);
		WriteInt16s(w, herc.Status);
	}

	private void Require(int count) {
		if (Index + count > Bytes!.Length) {
			throw new InvalidDataException($"The file ends at {Bytes.Length}, inside a field read at {Index}.");
		}
	}
}
