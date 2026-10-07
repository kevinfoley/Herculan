namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - LANG0\[lang]\*.BIN — a <c>.BIN</c> string table (<c>estext.bin</c>, <c>weapons.bin</c>,
/// <c>esnames.bin</c> …): <c>uint32</c> count, <c>uint32</c> pool size, <c>count x uint16</c>
/// offsets into the pool, then the pool of NUL-terminated strings. This model keeps only the strings,
/// in index order; the writer regenerates the offsets. See
/// <c>docs/retail/formats/weapons-dat.md#the-bin-string-tables</c>.
/// </summary>
public class StringBinaryFile {
	public string[]? Values { get; set; }
}
