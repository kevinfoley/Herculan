using HercWorks.Core.Data.File.Dts;
using HercWorks.Vol;
using System.Numerics;
using System.Text;

namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// A <c>.DTS</c> ThreeSpace shape file: a sequence of root chunks. A machine's roots are complete
/// alternate models, one per level of detail; a shape library's are unrelated shapes. See
/// docs/retail/rendering/mech-shape-drawing.md, "The LOD root is chosen per frame, per object".
/// </summary>
public class DynamixThreeSpaceModel {
	/// <summary>
	/// Not in the file: a name for <see cref="ToString"/>'s JSON dump. The transformer does not set
	/// it, so the dump's "file" field is empty unless a caller supplies one.
	/// </summary>
	public string? FileName { get; set; }

	/// <summary><see cref="FileName"/> with any extension stripped; empty when unset.</summary>
	private string NameNoExt() =>
		FileName == null ? string.Empty
			: FileName.LastIndexOf('.') != -1 ? FileName[..FileName.LastIndexOf('.')]
			: FileName;

	/// <summary>The file's top-level chunks, in file order.</summary>
	public List<TSObject>? Roots { get; set; }

	/// <summary>Not in the file; the transformer does not set it.</summary>
	public Vector3 Center { get; set; }

	/// <summary>
	/// Not in the file: a <c>.DTS</c> names no texture, so a caller that wants one bound sets these.
	/// Which bank retail binds is in docs/retail/rendering/dts-texture-binding.md, "DBA binding".
	/// </summary>
	public string? TextureName { get; set; }

	/// <inheritdoc cref="TextureName"/>
	public DynamixBitmapArray? TextureDBA { get; set; }

	public override string ToString() {
		var str = new StringBuilder();

		str.Append("{\"file\" : \"").Append(NameNoExt()).Append("\",\n");
		str.Append("\"meshes\" : [\n");
		for (int s = 0; s < Roots!.Count; s++) {
			str.Append(Roots[s].ToString());
			if (s < Roots.Count - 1) {
				str.Append(",\n");
			}
		}
		str.Append("]}");

		return str.ToString();
	}
}
