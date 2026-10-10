using System.Text;

namespace HercWorks.Core.Data.File.Dts.Bsp;

/// <summary>
/// A <see cref="TSGroup"/> with a BSP tree over its own polys appended, which orders their drawing;
/// see <see cref="TSBSPGroupNode"/> and docs/retail/rendering/dts-texture-binding.md, "TSBSPGroup poly order".
/// </summary>
public class TSBSPGroup : TSGroup {
	public TSBSPGroupNode[]? GroupNodes { get; set; }

	public TSBSPGroup() : base(TSObjectHeader.TSBSPGroup) { }

	public TSBSPGroup(TSObjectHeader hdr) : base(hdr) { }

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
		str.Append("\"bsp_nodes\" : [\n");
		for (int n = 0; n < GroupNodes!.Length; n++) {
			str.Append(GroupNodes[n].ToString());
			if (n < GroupNodes.Length - 1) {
				str.Append(",");
			}
			str.Append("\n");
		}
		str.Append("]");

		return str;
	}
}
