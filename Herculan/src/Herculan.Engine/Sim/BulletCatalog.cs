using HercWorks.Core.Data.Struct.Dbsim;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>dat\BULLETS.DAT</c>, the travelling projectile's own table — the counterpart of
/// <see cref="Content.BeamAppearance"/>'s <c>BEAM.DAT</c> for everything that is not a beam.
///
/// <para><c>Bullet_LoadResources</c> (<c>0040ade0</c>) — the bullet module's init, named by the <c>BULLET.CPP</c> string at
/// <c>00498601</c> — loads three resources once at startup and this is the first: a count followed
/// by that many fourteen-byte records, into the table at <c>DAT_004a977c</c>. The other two are
/// <c>dts\BULLETS.DTS</c> (the shapes the records' first field indexes, into <c>DAT_004a9784</c>)
/// and <c>dba\BULLETS.DBA</c> (the bank every one of those shapes is textured from) — see
/// <see cref="Scene.SceneModelLibrary.Bullet"/>.</para>
///
/// <para><b>Indexed by the firing <c>PROJ.DAT</c> record's subtype id</b>, not by weapon id, exactly
/// as <c>BEAM.DAT</c> is: <c>Bullet_Construct</c> stores that id at the object's <c>+0x41</c> and
/// <c>Bullet_GetTypeRecord</c> (<c>0040adc0</c>) is nothing but <c>table + id * 14</c>.</para>
///
/// <para>Retail ships twelve records, of which only seven subtype ids are reachable — ids 3, 4, 5, 10
/// and 11 are carried by no <c>Bullet</c> record. (They do exist on <c>Beam</c> records, which read
/// <c>BEAM.DAT</c> instead.) The field map and the retail table are in
/// docs/retail/simulation/projectiles.md.</para>
/// </summary>
public sealed class BulletCatalog {
	/// <summary>The resource folder and name <c>Bullet_LoadResources</c> (<c>0040ade0</c>) opens, by the literal name <c>bullets</c>.</summary>
	public const string ResourceFolder = "dat";

	/// <inheritdoc cref="ResourceFolder" />
	public const string TableResource = "BULLETS.DAT";

	/// <summary>
	/// The rate the age counter climbs at, and the unit <see cref="BulletType.Lifetime"/>
	/// is measured against — <c>Bullet_TickUpdate</c>'s literal <c>Math_IntegrateRateOverTick(0x200)</c>
	/// and its <c>lifetime * 0x200 &lt; age</c> test. Since <c>0x200</c> is the rate per 125 ms, a
	/// record's lifetime is directly a count of 125 ms intervals: ATC20's 20 is 2.5 seconds.
	/// </summary>
	public const short AgeRate = 0x200;

	private readonly BulletData _table;

	private BulletCatalog(BulletData table) {
		_table = table;
	}

	/// <summary>How many records were read.</summary>
	public int Count => _table.Entries?.Length ?? 0;

	/// <summary>
	/// Parses the table. Returns null when the resource is missing or unreadable, in which case
	/// nothing that fires a travelling projectile can build one — see
	/// <see cref="SimWorld.FireBullet"/>.
	/// </summary>
	public static BulletCatalog? Load(byte[]? bulletsDat) =>
		bulletsDat != null
			&& new BulletDataTransformer().Parse(bulletsDat) is BulletData { Entries: not null } table
			? new BulletCatalog(table)
			: null;

	/// <summary>
	/// The record for <paramref name="subtypeId"/>, or null when the id is outside the table — which
	/// no retail <c>Bullet</c> record is, but a hand-edited <c>PROJ.DAT</c> could be.
	/// </summary>
	public BulletType? Record(int subtypeId) =>
		_table.Entries is { } entries && subtypeId >= 0 && subtypeId < entries.Length
			? entries[subtypeId]
			: null;
}
