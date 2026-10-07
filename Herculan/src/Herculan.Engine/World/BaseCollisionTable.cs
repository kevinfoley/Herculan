using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <summary>
/// <c>dat\BASECOL.DAT</c> — one hit-sphere model per structure type, in the same order as
/// <c>dat\BASES.DAT</c>'s 65 types, read as one continuous stream partway through
/// <c>Base_LoadResources</c> (<c>00405fac</c>). The record format and the reader are
/// <see cref="CollisionModelReader"/>; this type is only the per-type walk over it.
///
/// <para><b>Verified against the retail file</b> (4,938 content bytes): the walk lands exactly on
/// the end after 65 types, and the geometry reads as deliberate hand-authored hitboxes — a
/// three-component bunker with a sphere cluster per section, a gun tower with a separate cluster per
/// barrel. Types whose <see cref="BaseType.HasCollisionModel"/> is false state a count of zero, with
/// one exception, type 3, which carries a model the type flag leaves unused.</para>
///
/// <para><b>Not the whole system.</b> Mechs and flyers have collision models of their own, loaded
/// by name from <c>col\&lt;NAME&gt;.COL</c> through the same reader rather than from this one
/// table — see <see cref="CollisionModelReader.Load"/>.</para>
/// </summary>
public sealed class BaseCollisionTable {
	/// <summary>VOL folder and name of the table.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The table's resource name.</summary>
	public const string ResourceName = "BASECOL.DAT";

	/// <summary>The node index meaning "these spheres are in the object's own frame".</summary>
	public const short ObjectFrameNode = CollisionModelReader.ObjectFrameNode;

	private readonly ColliderNode[][] _models;

	private BaseCollisionTable(ColliderNode[][] models) {
		_models = models;
	}

	/// <summary>How many types the table covers.</summary>
	public int Count => _models.Length;

	/// <summary>
	/// The model for a type, or an empty array when the file has none for it. Whether the model is
	/// actually used is <see cref="BaseType.HasCollisionModel"/>, not whether this is empty.
	/// </summary>
	public ColliderNode[] this[int typeIndex] =>
		typeIndex >= 0 && typeIndex < _models.Length ? _models[typeIndex] : Array.Empty<ColliderNode>();

	/// <param name="content">Mounted archives.</param>
	/// <param name="typeCount">
	/// How many types to read, which is <see cref="BaseTypeTable.Count"/> — the file carries no count
	/// of its own, exactly as the original reads it.
	/// </param>
	public static BaseCollisionTable Load(GameContent content, int typeCount) {
		byte[] bytes = content.ReadRequired(ResourceFolder, ResourceName);
		int offset = 0;

		var models = new ColliderNode[typeCount][];
		for (int type = 0; type < typeCount; type++) {
			models[type] = CollisionModelReader.Read(bytes, ref offset);
		}

		// The retail file is padded to an even length by the VOL prefix codec's own rule, so one
		// trailing byte is expected and anything more means the walk went wrong.
		if (offset < bytes.Length - 1) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{ResourceName}: walked {offset} of {bytes.Length} bytes across " +
				$"{typeCount} types — the record shape does not match this file.");
		}

		return new BaseCollisionTable(models);
	}
}
