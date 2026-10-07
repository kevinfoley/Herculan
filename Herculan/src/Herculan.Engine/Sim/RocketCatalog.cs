using HercWorks.Core.Data.Struct.Dbsim;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>dat\ROCKETS.DAT</c>, the launcher family's own table — what <see cref="BulletCatalog"/> is to
/// a travelling gun round, this is to a <see cref="Rocket"/>.
///
/// <para><c>Rocket_LoadTypeTable_Unguided</c> (<c>0040a818</c>) reads it exactly as
/// <c>Bullet_LoadResources</c> reads its own: a count followed by that many fourteen-byte records,
/// into <c>maybe_RocketTypeTable_Unguided</c>. It then loads <c>dts\ROCKETS.DTS</c> into
/// <c>DAT_004a975c</c> and stops — <b>there is no <c>ROCKETS.DBA</c></b> and no bank is bound to any
/// rocket shape, which is the mechanical reason a rocket is ramp-coloured geometry rather than
/// textured (see <see cref="Scene.SceneModelLibrary.Rocket"/>).</para>
///
/// <para><b>Indexed by the firing <c>PROJ.DAT</c> record's subtype id</b>, like every other appearance
/// table here: <c>Rocket_GetTypeRecord</c> (<c>0040a234</c>) is <c>table + id * 14</c> against the id
/// <c>Rocket_Construct</c> stored at the object's <c>+0x41</c>.</para>
///
/// <para><b>The record layout is not <c>BULLETS.DAT</c>'s.</b> The two files share a stride and their
/// first two fields and nothing else — the readers are different functions reading different offsets,
/// which is why a rocket's record is its own <see cref="RocketType"/>. The field map and the retail
/// table are in docs/retail/formats/rockets-dat.md.</para>
///
/// <para>Retail ships five records, one per <c>Rocket</c> subtype id, four of them identical: every
/// launcher round flies for 80 ticks (3.2 s) and accelerates at the same figure, and the BMSL round
/// is the one with its own shape, a wider hit slack and a shape that does not animate.</para>
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
