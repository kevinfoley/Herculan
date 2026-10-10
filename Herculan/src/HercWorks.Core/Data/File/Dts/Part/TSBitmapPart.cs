using System.Text;

namespace HercWorks.Core.Data.File.Dts.Part;

/// <summary>
/// BmpTag is a plain zero-based frame index into whichever DBA bitmap-array is bound to the owning
/// TSShape at render time; OfsX/OfsY are the anchor pixel the part's centre lands on. The
/// rest of the blit — scale, rotation and the vertical squash — is in docs/retail/rendering/dts-billboards.md.
///
/// <para>Which DBA is bound is not recorded in the .DTS or the .DBA: for a mech it is chosen by
/// <c>HercSimDat.TextureGroup</c> (file offset 148) through a 7-entry group table, which
/// docs/retail/rendering/dts-texture-binding.md carries under "DBSIM's mech-to-texture mapping".</para>
/// </summary>
public class TSBitmapPart : TSBasePart {
	public short BmpTag { get; set; }
	/// <summary>The anchor's x. The original reads this byte signed; <see cref="OfsY"/> it reads unsigned.</summary>
	public byte OfsX { get; set; }
	public byte OfsY { get; set; }

	public TSBitmapPart() : base(TSObjectHeader.TSBitmapPart) { }

	public TSBitmapPart(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));

		str = JsonString(str);

		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str = base.JsonString(str);

		str.Append(",\n");
		str.Append("\"bmp_tag\" : ").Append(BmpTag).Append(",\n");
		str.Append("\"ofs_x\" : ").Append(OfsX).Append(",\n");
		str.Append("\"ofs_y\" : ").Append(OfsY);

		return str;
	}
}
