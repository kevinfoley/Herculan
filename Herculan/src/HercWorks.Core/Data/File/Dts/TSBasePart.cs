using HercWorks.Core.Data.Struct;
using System.Text;

namespace HercWorks.Core.Data.File.Dts;

/// <summary>The head every part chunk carries.</summary>
public class TSBasePart : TSObject {
	/// <summary>
	/// The node whose transform the part is drawn through (<c>part+4</c>), as an index into the shape
	/// instance's per-node array; negative for none. See docs/retail/rendering/dts-node-posing.md,
	/// "The draw path".
	/// </summary>
	public short Transform { get; set; }

	/// <summary>
	/// The part's id (<c>part+6</c>) — what a find-by-id resolves, as the hardpoint splice and the
	/// cockpit camera bone do. See docs/retail/rendering/mech-shape-drawing.md, "Hardpoint attachment slots
	/// are overwritten every frame".
	/// </summary>
	public short IdNumber { get; set; }

	/// <summary>
	/// Bounding radius in world units (<c>part+8</c>), read by the detail selectors and the billboard
	/// scale. See docs/retail/formats/dgs-hd0-notes.md, "The bounding radius — shape+8".
	/// </summary>
	public short Radius { get; set; }

	/// <summary>The part's centre. A billboard is drawn at it.</summary>
	public Vec3Short? Center { get; set; }

	public TSBasePart() : base(TSObjectHeader.TSBasePart) { }

	public TSBasePart(TSObjectHeader hdr) : base(hdr) { }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append(MetaInfoString(GetType().Name));

		str = JsonString(str);

		str.Append("\n");
		str.Append("}\n");

		return str.ToString();
	}

	public override StringBuilder JsonString(StringBuilder str) {
		str.Append("\"transform\" : ").Append(Transform).Append(",\n");
		str.Append("\"uid\" : \"").Append(IdNumber).Append("\",\n");
		str.Append("\"radius\" : ").Append(Radius).Append(",\n");
		str.Append("\"center\" : ").Append(Center?.ToString());

		return str;
	}
}
