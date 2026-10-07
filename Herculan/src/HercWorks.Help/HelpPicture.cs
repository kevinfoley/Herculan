using HercWorks.Help.Internal;

namespace HercWorks.Help;

/// <summary>
/// A decoded <c>|bm</c> picture: 24-bit pixels, top row first, three bytes per pixel in R, G, B order,
/// and the clickable regions over it.
/// </summary>
public sealed record HelpPicture(int Width, int Height, byte[] Rgb, IReadOnlyList<HelpHotspot> Hotspots);

/// <summary>
/// A picture's clickable region (docs/retail/formats/winhelp.md#hotspots). <paramref name="Kind"/> is the
/// command byte, <c>0xE6</c> — a pop-up — throughout the corpus.
/// </summary>
public sealed record HelpHotspot(byte Kind, int Left, int Top, int Width, int Height, uint Hash, string Name, string Context);

/// <summary>
/// Decodes the one picture layout the corpus uses: a single 24-bit device-independent bitmap, unpacked
/// or run-length packed (docs/retail/formats/winhelp.md#pictures).
/// </summary>
internal static class PictureReader {
	private const ushort Magic = 0x706C;
	private const int MaxHotspots = 256;

	public static HelpPicture Read(byte[] bytes, int start, int end, HelpLimits limits) {
		var file = new ByteCursor(bytes, start, end);
		if (file.U16() != Magic) {
			throw new MalformedHelpException("picture magic");
		}

		if (file.U16() != 1) {
			throw new MalformedHelpException("more than one picture is not supported");
		}

		uint at = file.U32();
		if (at >= (uint)(end - start)) {
			throw new MalformedHelpException("picture offset");
		}

		var picture = new ByteCursor(bytes, start + (int)at, end);
		int pictureStart = picture.Position;
		int type = picture.U8();
		int packing = picture.U8();
		picture.CompressedUnsignedLong();
		picture.CompressedUnsignedLong();
		int planes = picture.CompressedUnsignedShort();
		int bitCount = picture.CompressedUnsignedShort();
		long width = picture.CompressedUnsignedLong();
		long height = picture.CompressedUnsignedLong();
		long coloursUsed = picture.CompressedUnsignedLong();
		picture.CompressedUnsignedLong();
		long dataSize = picture.CompressedUnsignedLong();
		long hotspotSize = picture.CompressedUnsignedLong();
		uint dataAt = picture.U32();
		uint hotspotAt = picture.U32();

		if (type != 6 || planes != 1 || bitCount != 24 || coloursUsed != 0 || packing is not (0 or 1)) {
			throw new MalformedHelpException($"picture type {type}, packing {packing}, {bitCount} bits is not supported");
		}

		if (width < 1 || height < 1 || width > limits.MaxPictureDimension || height > limits.MaxPictureDimension
			|| width * height > limits.MaxPicturePixels) {
			throw new MalformedHelpException($"picture of {width}x{height}");
		}

		int w = (int)width, h = (int)height;
		int stride = (w * 3 + 3) & ~3;
		var pixels = Region(bytes, pictureStart, end, dataAt, dataSize);
		byte[] dib = packing == 1 ? Unpack(pixels, stride * h) : Pad(pixels, stride * h);

		var rgb = new byte[w * h * 3];
		for (int y = 0; y < h; y++) {
			int source = (h - 1 - y) * stride;
			int target = y * w * 3;
			for (int x = 0; x < w; x++, source += 3, target += 3) {
				rgb[target] = dib[source + 2];
				rgb[target + 1] = dib[source + 1];
				rgb[target + 2] = dib[source];
			}
		}

		IReadOnlyList<HelpHotspot> hotspots = hotspotSize == 0 ? [] : ReadHotspots(Region(bytes, pictureStart, end, hotspotAt, hotspotSize), limits);
		return new HelpPicture(w, h, rgb, hotspots);
	}

	private static ByteCursor Region(byte[] bytes, int pictureStart, int end, uint at, long size) {
		if (at > (uint)(end - pictureStart) || size > end - pictureStart - at) {
			throw new MalformedHelpException("picture data outside the picture");
		}

		return new ByteCursor(bytes, pictureStart + (int)at, pictureStart + (int)at + (int)size);
	}

	// Run-length packing (docs/retail/formats/winhelp.md#pictures). The output is clamped to the bitmap, which
	// some retail pictures overrun with padding, and input left over once it is full is ignored.
	private static byte[] Unpack(ByteCursor packed, int length) {
		var output = new byte[length];
		int written = 0;
		while (written < length && !packed.AtEnd) {
			int control = packed.U8();
			if ((control & 0x80) != 0) {
				int count = control & 0x7F;
				var literal = packed.Bytes(Math.Min(count, packed.Remaining));
				int take = Math.Min(literal.Length, length - written);
				literal[..take].CopyTo(output.AsSpan(written));
				written += take;
			} else {
				byte value = packed.U8();
				int take = Math.Min(control, length - written);
				output.AsSpan(written, take).Fill(value);
				written += take;
			}
		}

		if (written < length) {
			throw new MalformedHelpException($"run-length data fills {written} of {length} bytes");
		}

		return output;
	}

	// Unpacked pixels. Bytes beyond the bitmap are padding; too few is malformed.
	private static byte[] Pad(ByteCursor data, int length) {
		if (data.Remaining < length) {
			throw new MalformedHelpException($"{data.Remaining} pixel bytes for a {length}-byte bitmap");
		}

		return data.Bytes(length).ToArray();
	}

	private static List<HelpHotspot> ReadHotspots(ByteCursor data, HelpLimits limits) {
		if (data.U8() != 1) {
			throw new MalformedHelpException("hotspot header");
		}

		int count = data.U16();
		uint macroSize = data.U32();
		if (count > MaxHotspots || macroSize != 0) {
			throw new MalformedHelpException($"{count} hotspots, {macroSize} bytes of hotspot macros");
		}

		var regions = new (byte Kind, int Left, int Top, int Width, int Height, uint Hash)[count];
		for (int i = 0; i < count; i++) {
			byte kind = data.U8();
			data.Skip(2);
			regions[i] = (kind, data.U16(), data.U16(), data.U16(), data.U16(), data.U32());
			if (kind != 0xE6) {
				throw new MalformedHelpException($"hotspot kind {kind:x2} is not supported");
			}
		}

		var hotspots = new List<HelpHotspot>(count);
		foreach (var r in regions) {
			string name = data.String(limits.MaxStringBytes);
			string context = data.String(limits.MaxStringBytes);
			hotspots.Add(new HelpHotspot(r.Kind, r.Left, r.Top, r.Width, r.Height, r.Hash, name, context));
		}

		return hotspots;
	}
}
