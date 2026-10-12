namespace HercWorks.Core.Data.File;

/// <summary>
/// OPAQUE - an ES1 file whose bytes are not understood, kept whole so it writes back unchanged. No field is modelled:
/// what the bytes are is an Open item in the file's doc. Its one use is <c>dmg\HOVTANK.DMG</c>
/// (docs/es1/formats/dmg-damage-file.md#hovtankdmg). Read by <see cref="Io.Transform.Common.Es1OpaqueFileTransformer"/>.
/// </summary>
public class Es1OpaqueFile {
	/// <summary>The file's content, as read.</summary>
	public byte[] Bytes { get; set; } = [];
}
