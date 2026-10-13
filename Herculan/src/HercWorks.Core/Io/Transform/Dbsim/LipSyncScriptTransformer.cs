using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes a simulator's lip-sync script (<see cref="LipSyncScript"/>; docs/retail/formats/snc-lip-sync.md#layout),
/// the bytes <c>Snc_Load</c> (<c>00463270</c>) reads; the <c>0xff</c> terminator it appends is not in the file, and not in
/// the model. Writing a parsed script gives back its bytes.
///
/// <para>A file shorter than the length's 4 bytes, a negative length, or one that runs past the end throws
/// <see cref="InvalidDataException"/>.</para>
/// </summary>
public class LipSyncScriptTransformer : ByteTransformer<LipSyncScript> {
	/// <exception cref="InvalidDataException">The length is missing, negative or runs past the end.</exception>
	public override LipSyncScript? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length < 4) {
			throw new InvalidDataException($"{inputArray.Length} bytes is shorter than the script's length.");
		}

		SetBytes(inputArray);
		int length = IndexIntLE();
		if (length < 0 || 4L + length > inputArray.Length) {
			throw new InvalidDataException($"The length, {length}, runs past the end of {inputArray.Length} bytes.");
		}

		var events = new LipSyncEvent[length / 2];
		for (int i = 0; i < events.Length; i++) {
			events[i] = new LipSyncEvent { Frame = IndexByte(), Delta = IndexByte() };
		}

		return new LipSyncScript { Length = length, Events = events, Unread = IndexSegment(inputArray.Length - Index) };
	}

	public override byte[]? Write(LipSyncScript source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Length);
		foreach (var e in source.Events) {
			w.Write(e.Frame);
			w.Write(e.Delta);
		}

		w.Write(source.Unread);
		w.Flush();
		return o.ToArray();
	}
}
