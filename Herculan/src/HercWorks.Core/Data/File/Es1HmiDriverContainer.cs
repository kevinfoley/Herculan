namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of ES1's three HMI driver containers in <c>SOS\</c>, <c>HMIMDRV.386</c>, <c>HMIDRV.386</c> or <c>HMIDET.386</c>:
/// a run of driver images behind a short header (docs/es1/formats/hmi-driver-container.md#layout). Read by
/// <see cref="Io.Transform.Common.Es1HmiDriverContainerTransformer"/>.
/// </summary>
public class Es1HmiDriverContainer {
	/// <summary>The size of the header, which <c>HmiMidi_LoadDriverImage</c> (<c>ES1DBSIM 00081365</c>) reads first.</summary>
	public const int HeaderSize = 0x2c;

	/// <summary>The size of an entry's header, which the loader reads before each image.</summary>
	public const int EntryHeaderSize = 0x30;

	/// <summary>Bytes <c>+0x00</c>–<c>+0x1f</c>: <c>0x33</c>, then 31 zero bytes in all three files.</summary>
	public byte[] Head { get; set; } = [];

	/// <summary><c>+0x20</c>: the number of images.</summary>
	public int ImageCount { get; set; }

	/// <summary>
	/// <c>+0x24</c>: the offset of the first entry, <see cref="HeaderSize"/> in all three files. The loader reads the entries
	/// from <see cref="HeaderSize"/> whatever it holds.
	/// </summary>
	public int FirstEntryOffset { get; set; }

	/// <summary><c>+0x28</c>: the low 16 bits of the file's length, sign-extended, in all three files.</summary>
	public int LengthLow16 { get; set; }

	/// <summary>The <see cref="ImageCount"/> entries, in file order from <see cref="HeaderSize"/>.</summary>
	public List<Es1HmiDriverImage> Images { get; set; } = [];

	/// <summary>The bytes after the last image, to the end of the file; empty in all three files.</summary>
	public byte[] AfterImages { get; set; } = [];
}

/// <summary>One entry of an <see cref="Es1HmiDriverContainer"/>: its 0x30-byte header and the image after it.</summary>
public class Es1HmiDriverImage {
	/// <summary>
	/// <c>+0x00</c>–<c>+0x1f</c>: the image's name, zero-terminated. The bytes after the terminator are not zero in every
	/// entry, so the field is kept whole.
	/// </summary>
	public byte[] NameField { get; set; } = [];

	/// <summary>The name in <see cref="NameField"/>, up to its first zero byte.</summary>
	public string Name {
		get {
			int end = Array.IndexOf(NameField, (byte)0);
			return System.Text.Encoding.Latin1.GetString(NameField, 0, end < 0 ? NameField.Length : end);
		}
	}

	/// <summary><c>+0x20</c>: the image's size plus <c>0x5c</c> in every entry of the three files.</summary>
	public int EntryLength { get; set; }

	/// <summary><c>+0x28</c>: the device id the image drives, the id the loader matches.</summary>
	public int DeviceId { get; set; }

	/// <summary><c>+0x2c</c>: flags, 0 in every entry of <c>HMIMDRV.386</c>.</summary>
	public int Flags { get; set; }

	/// <summary>
	/// From <c>+0x30</c>: the image, as many bytes as the entry's <c>+0x24</c> gives, the size the loader skips by and reads.
	/// Flat 32-bit code and data addressed from 0 (docs/es1/formats/hmi-driver-container.md#an-image).
	/// </summary>
	public byte[] Image { get; set; } = [];
}
