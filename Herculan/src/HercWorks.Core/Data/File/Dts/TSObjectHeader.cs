namespace HercWorks.Core.Data.File.Dts;

/// <summary>
/// A chunk's 4-byte type marker, <c>[subtype:u16][family:u16]</c> little-endian — family <c>0x14</c>
/// for the TS types, <c>0x1e</c> for the AN (animation) types. DBSIM's
/// <c>g_TSObjectTypeRegistry</c> (<c>004a63c8</c>) is keyed by the same value. See
/// docs/retail/formats/dts-texture-binding.md, "Poly types and their colour mechanisms (DBSIM.EXE)".
/// </summary>
public sealed class TSObjectHeader {
	public static readonly TSObjectHeader TSPoly = new(new byte[] { 0x01, 0x00, 0x14, 0x00 }, "TSPoly");
	public static readonly TSObjectHeader TSSolidPoly = new(new byte[] { 0x02, 0x00, 0x14, 0x00 }, "TSSolidPoly");
	public static readonly TSObjectHeader TSShadedPoly = new(new byte[] { 0x03, 0x00, 0x14, 0x00 }, "TSShadedPoly");
	public static readonly TSObjectHeader TSBasePart = new(new byte[] { 0x05, 0x00, 0x14, 0x00 }, "TSBasePart");
	public static readonly TSObjectHeader TSPartList = new(new byte[] { 0x07, 0x00, 0x14, 0x00 }, "TSPartList");
	public static readonly TSObjectHeader TSShape = new(new byte[] { 0x08, 0x00, 0x14, 0x00 }, "TSShape");
	public static readonly TSObjectHeader TSGouraudPoly = new(new byte[] { 0x09, 0x00, 0x14, 0x00 }, "TSGouraudPoly");
	public static readonly TSObjectHeader TSBSPGroup = new(new byte[] { 0x0a, 0x00, 0x14, 0x00 }, "TSBSPGroup");
	public static readonly TSObjectHeader TSCellAnimPart = new(new byte[] { 0x0b, 0x00, 0x14, 0x00 }, "TSCellAnimPart");
	public static readonly TSObjectHeader TSDetailPart = new(new byte[] { 0x0c, 0x00, 0x14, 0x00 }, "TSDetailPart");
	public static readonly TSObjectHeader TSTexture4Poly = new(new byte[] { 0x0f, 0x00, 0x14, 0x00 }, "TSTexture4Poly");
	public static readonly TSObjectHeader TSGroup = new(new byte[] { 0x14, 0x00, 0x14, 0x00 }, "TSGroup");
	public static readonly TSObjectHeader TSAliasSolidPoly = new(new byte[] { 0x10, 0x00, 0x14, 0x00 }, "TSAliasSolidPoly");
	public static readonly TSObjectHeader TSAliasShadedPoly = new(new byte[] { 0x11, 0x00, 0x14, 0x00 }, "TSAliasShadedPoly");
	public static readonly TSObjectHeader TSAliasGouraudPoly = new(new byte[] { 0x12, 0x00, 0x14, 0x00 }, "TSAliasGouraudPoly");
	public static readonly TSObjectHeader TSBitmapPart = new(new byte[] { 0x13, 0x00, 0x14, 0x00 }, "TSBitmapPart");
	public static readonly TSObjectHeader TSBSPPart = new(new byte[] { 0x15, 0x00, 0x14, 0x00 }, "TSBSPPart");
	public static readonly TSObjectHeader ANSequence = new(new byte[] { 0x01, 0x00, 0x1e, 0x00 }, "ANSequence");
	public static readonly TSObjectHeader ANAnimList = new(new byte[] { 0x02, 0x00, 0x1e, 0x00 }, "ANAnimList");
	public static readonly TSObjectHeader ANShape = new(new byte[] { 0x03, 0x00, 0x1e, 0x00 }, "ANShape");
	public static readonly TSObjectHeader ANCyclicSequence = new(new byte[] { 0x04, 0x00, 0x1e, 0x00 }, "ANCyclicSequence");

	private static readonly IReadOnlyList<TSObjectHeader> All = new[]
	{
		TSPoly, TSSolidPoly, TSShadedPoly, TSBasePart, TSPartList, TSShape, TSGouraudPoly,
		TSBSPGroup, TSCellAnimPart, TSDetailPart, TSTexture4Poly, TSGroup, TSAliasSolidPoly,
		TSAliasShadedPoly, TSAliasGouraudPoly, TSBitmapPart, TSBSPPart, ANSequence, ANAnimList, ANShape,
		ANCyclicSequence
	};

	private readonly byte[] _val;
	private readonly string _id;

	private TSObjectHeader(byte[] val, string id) {
		_val = val;
		_id = id;
	}

	public byte[] Val() => _val;

	public string Id() => _id;

	public static TSObjectHeader? FindVal(byte[] marker) =>
		All.FirstOrDefault(hdr => hdr._val.SequenceEqual(marker));
}
