using HercWorks.Core.Data.Struct;
using System.Text;

namespace HercWorks.Core.Data.File.Dts.Anim;

/// <summary>
/// One keyframe of an <see cref="ANAnimList"/>'s transform pool: three euler angles, then a
/// translation. See docs/retail/rendering/dts-node-posing.md, "Keyframe interpolation".
/// </summary>
public class ANAnimListTransform {
	public Vec3Short? Rotation { get; set; }
	public Vec3Short? Translation { get; set; }

	/// <summary>Offset of the record in the buffer it was read from.</summary>
	public int Index { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{ \"class\" : \"").Append(GetType().Name).Append("\",\n");
		str.Append("\"index\" : ").Append(Index).Append(",\n");
		str.Append("\"rotation\" : ").Append(Rotation).Append(",\n");
		str.Append("\"translation\" : ").Append(Translation).Append("\n");
		str.Append("}\n");

		return str.ToString();
	}
}
