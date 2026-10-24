using HercWorks.Core.Data.Struct.Dbsim;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>dat\ROCKETS.DAT</c>, the launcher family's own table — what <see cref="BulletCatalog"/> is to
/// a travelling gun round, this is to a <see cref="Rocket"/>.
///
/// <para>Read by <c>Rocket_LoadTypeTable_Unguided</c> (<c>0040a818</c>) and indexed by the firing
/// <c>PROJ.DAT</c> record's subtype id (<c>Rocket_GetTypeRecord</c>, <c>0040a234</c>). Its layout is
/// not <c>BULLETS.DAT</c>'s, which is why a rocket's record is its own <see cref="RocketType"/>. No
/// texture bank goes with the shapes, so a rocket is drawn ramp-colored (see
/// <see cref="Scene.SceneModelLibrary.Rocket"/>). The field map and the retail table are
/// docs/retail/formats/rockets-dat.md.</para>
/// </summary>
public sealed class RocketCatalog {
	/// <summary>The resource folder and name <c>Rocket_LoadTypeTable_Unguided</c> opens, by the literal name <c>rockets</c>.</summary>
	public const string ResourceFolder = "dat";

	/// <inheritdoc cref="ResourceFolder" />
	public const string TableResource = "ROCKETS.DAT";

	private readonly RocketData _table;

	private RocketCatalog(RocketData table) {
		_table = table;
	}

	/// <summary>How many records were read.</summary>
	public int Count => _table.Entries?.Length ?? 0;

	/// <summary>
	/// Parses the table. Returns null when the resource is missing or unreadable, in which case a
	/// launcher fires nothing — see <see cref="SimWorld.FireRocket"/>.
	/// </summary>
	public static RocketCatalog? Load(byte[]? rocketsDat) =>
		rocketsDat != null
			&& new RocketDataTransformer().Parse(rocketsDat) is RocketData { Entries: not null } table
			? new RocketCatalog(table)
			: null;

	/// <summary>
	/// The record for <paramref name="subtypeId"/>, or null when the id is outside the table — which
	/// no retail <c>Rocket</c> record is, but a hand-edited <c>PROJ.DAT</c> could be.
	/// </summary>
	public RocketType? Record(int subtypeId) =>
		_table.Entries is { } entries && subtypeId >= 0 && subtypeId < entries.Length
			? entries[subtypeId]
			: null;
}
