using System.Buffers.Binary;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's HMI driver containers (<see cref="Es1HmiDriverContainer"/>), walking the entries from the header's
/// end as <c>HmiMidi_LoadDriverImage</c> (<c>ES1DBSIM 00081365</c>) does (docs/es1/formats/hmi-driver-container.md#layout).
/// Writing a parsed container gives back its bytes.
///
/// <para>A file shorter than the header, with a negative image count, or whose entries or images run past the end throws
/// <see cref="InvalidDataException"/>.</para>
/// </summary>
public class Es1HmiDriverContainerTransformer : ByteTransformer<Es1HmiDriverContainer> {
	/// <exception cref="InvalidDataException">The bytes do not walk the layout.</exception>
	public override Es1HmiDriverContainer? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		byte[] b = inputArray;
		if (b.Length < Es1HmiDriverContainer.HeaderSize) {
			throw new InvalidDataException($"{b.Length} bytes are shorter than a driver container's header.");
		}

		var container = new Es1HmiDriverContainer {
			Head = b[..0x20], ImageCount = Int(b, 0x20), FirstEntryOffset = Int(b, 0x24), LengthLow16 = Int(b, 0x28),
		};
		if (container.ImageCount < 0) {
			throw new InvalidDataException($"The image count, {container.ImageCount}, is negative.");
		}

		int at = Es1HmiDriverContainer.HeaderSize;
		for (int i = 0; i < container.ImageCount; i++) {
			if (Es1HmiDriverContainer.EntryHeaderSize > b.Length - at) {
				throw new InvalidDataException($"Entry {i} at 0x{at:x} runs past the end of {b.Length} bytes.");
			}

			int size = Int(b, at + 0x24);
			int image = at + Es1HmiDriverContainer.EntryHeaderSize;
			if (size < 0 || size > b.Length - image) {
				throw new InvalidDataException($"Entry {i}'s image of {size} bytes at 0x{image:x} runs past the end of {b.Length} bytes.");
			}

			container.Images.Add(new Es1HmiDriverImage {
				NameField = b[at..(at + 0x20)], EntryLength = Int(b, at + 0x20), DeviceId = Int(b, at + 0x28), Flags = Int(b, at + 0x2c),
				Image = b[image..(image + size)],
			});
			at = image + size;
		}

		container.AfterImages = b[at..];
		return container;
	}

	public override byte[]? Write(Es1HmiDriverContainer source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Head);
		w.Write(source.ImageCount);
		w.Write(source.FirstEntryOffset);
		w.Write(source.LengthLow16);
		foreach (var entry in source.Images) {
			w.Write(entry.NameField);
			w.Write(entry.EntryLength);
			w.Write(entry.Image.Length);
			w.Write(entry.DeviceId);
			w.Write(entry.Flags);
			w.Write(entry.Image);
		}

		w.Write(source.AfterImages);
		w.Flush();
		return o.ToArray();
	}

	private static int Int(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
