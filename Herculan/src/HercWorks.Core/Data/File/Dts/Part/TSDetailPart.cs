using System.Text;

namespace HercWorks.Core.Data.File.Dts.Part;

/// <summary>
/// One piece of a shape at several levels of detail; one part is drawn, chosen by projected size.
/// See docs/retail/formats/dts-texture-binding.md, "TSDetailPart level selection and STRUCTURE DETAIL".
/// </summary>
public class TSDetailPart : TSPartList {
	/// <summary>
	/// Projected-size thresholds, ascending and index-aligned with <see cref="TSPartList.Parts"/>:
	/// part 0 is the coarsest. On disk they fill whatever of the chunk follows the part list.
	/// </summary>
	public short[]? Details { get; set; }

	public TSDetailPart() : base(TSObjectHeader.TSDetailPart) { }

	public TSDetailPart(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"details\" : ").Append(Details == null ? "null" : "[" + string.Join(", ", Details) + "]");

		return str;
	}
}
