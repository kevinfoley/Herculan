using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Transforms byte[] data to and from DynamixPalette game files.
/// Ported from org.hercworks.core.io.transform.common.DynamixPaletteTransformer.
///
/// NOTE ON toByte()/byteOrder(): unlike .array() (see the ByteOps notes from earlier rounds),
/// .toByte() DOES respect the byteOrder tag — for LITTLE_ENDIAN it returns the value's
/// least-significant byte, i.e. equivalent to a plain narrowing (byte) cast here since every
/// value written is already guaranteed &lt; 256. Ported as a direct cast below.
/// </summary>
public class DynamixPaletteTransformer : ByteTransformer<DynamixPalette> {
	private readonly int _colorScalar = 4;

	public DynamixPaletteTransformer() { }

	public DynamixPaletteTransformer(int scalar) {
		_colorScalar = scalar;
	}

	public override DynamixPalette? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			// TODO (carried over from Java): log null
			return null;
		}
		SetBytes(inputArray);
		ResetIndex();

		var dpl = new DynamixPalette();

		Index += 4; // skip magic header bytes

		dpl.PaletteSizeByte = IndexIntLE();
		dpl.ColorCount = IndexIntLE();

		int colorIdx = 0;

		dpl.Scalar = _colorScalar;

		// DO NOT CHANGE - original "mostly working" 4-byte RGBA value read
		for (int clr = 0; clr < dpl.ColorCount; clr++) {
			dpl.Colors[colorIdx] = ToColorBytes(IndexSegmentLE(4), dpl.Scalar);
			colorIdx++;
		}

		ReadShadeRamps(dpl);

		return dpl;
	}

	/// <summary>
	/// Reads the shade-ramp table that follows the colour entries — see
	/// <see cref="DynamixPalette.ShadeRamps"/> for the layout and for what a ramp means. A count of
	/// zero is a table with no ramps, kept so the write path puts its four bytes back. Leaves the
	/// table empty rather than throwing when the tail is absent or short: a palette that is only
	/// colours is still a usable palette.
	/// </summary>
	private void ReadShadeRamps(DynamixPalette dpl) {
		int tailStart = Index;
		if (tailStart + 4 > Bytes!.Length) {
			return;
		}

		int rampCount = IndexIntLE();
		if (rampCount < 0 || rampCount > MaxShadeRamps) {
			Index = tailStart;
			return;
		}

		var ramps = new short[rampCount][];
		for (int ramp = 0; ramp < rampCount; ramp++) {
			if (Index + 2 > Bytes.Length) {
				Index = tailStart;
				return;
			}

			int length = IndexShortLE();
			if (length < 0 || Index + length * 2 > Bytes.Length) {
				Index = tailStart;
				return;
			}

			var entries = new short[length];
			for (int i = 0; i < length; i++) {
				entries[i] = IndexShortLE();
			}
			ramps[ramp] = entries;
		}

		dpl.ShadeRamps = ramps;
		dpl.ShadeRampBytes = Bytes[tailStart..Index];
	}

	/// <summary>
	/// Guard against a truncated file's leading bytes reading as an enormous ramp count. Retail
	/// states 256; anything past a byte's worth of slots could not be addressed by
	/// <c>Palette_ShadeRampLookup</c>'s own <c>value &amp; 0xff</c> anyway.
	/// </summary>
	private const int MaxShadeRamps = 256;

	public override byte[]? Write(DynamixPalette dpl) {
		using var objectBytes = new MemoryStream();

		objectBytes.Write(DynamixPalette.Header, 0, DynamixPalette.Header.Length);

		// Little-endian, matching Parse()'s IndexIntLE() on both fields. Writing these big-endian
		// produces a .DPL this class cannot read back.
		var sizeBytes = WriteIntLE(dpl.PaletteSizeByte);
		objectBytes.Write(sizeBytes, 0, sizeBytes.Length);

		var countBytes = WriteIntLE(dpl.ColorCount);
		objectBytes.Write(countBytes, 0, countBytes.Length);

		foreach (var color in dpl.Colors.Values) {
			var c = ToDynamixColor(color, dpl.Scalar);
			objectBytes.Write(c, 0, c.Length);
		}

		// The ramp table goes back exactly as it was read — nothing in this project edits it, so
		// re-serialising it could only introduce a difference.
		if (dpl.ShadeRampBytes is { Length: > 0 } ramps) {
			objectBytes.Write(ramps, 0, ramps.Length);
		}

		return objectBytes.ToArray();
	}

	/// <summary>Convert a 4-byte segment of a .DPL file into the ColorBytes wrapper.</summary>
	private static ColorBytes ToColorBytes(byte[] dynamixColor, int scalar) {
		var bytes = new byte[4];

		int ir = dynamixColor[0] & 0xFF;
		int ig = dynamixColor[1] & 0xFF;
		int ib = dynamixColor[2] & 0xFF;
		int ia = dynamixColor[3];

		ir = ir * scalar > 255 ? 255 : ir * scalar;
		ig = ig * scalar > 255 ? 255 : ig * scalar;
		ib = ib * scalar > 255 ? 255 : ib * scalar;
		ia = ia == 1 ? 255 : 0;

		bytes[0] = (byte)ir;
		bytes[1] = (byte)ig;
		bytes[2] = (byte)ib;
		bytes[3] = (byte)ia;

		var rawColor = new ColorBytes(bytes);
		rawColor.SetColor(RgbaColor.FromArgb(ia, ir, ig, ib));

		return rawColor;
	}

	/// <summary>
	/// Converts a ColorBytes object to a 4-byte array based on the parent DynamixPalette's
	/// scalar value.
	///
	/// Channel order is <c>byte0=R, byte1=G, byte2=B</c>, matching <see cref="ToColorBytes"/>.
	/// <b>The read path is the trusted side</b> — it is exercised against real <c>.DPL</c> files and
	/// this write path is not, so keep write aligned to read rather than the other way round.
	/// </summary>
	private static byte[] ToDynamixColor(ColorBytes color, int scalar) {
		var data = new byte[4];
		var c = color.GetColor();

		data[0] = (byte)(c.R / scalar);
		data[1] = (byte)(c.G / scalar);
		data[2] = (byte)(c.B / scalar);

		if (color.Array[3] != 0) {
			data[3] = 0x01;
		}

		return data;
	}
}
