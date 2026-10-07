using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Sim;

/// <summary>
/// Everything a transport (<see cref="BaseObject.StructureClass.Transport"/>) fires with, resolved once
/// per mission and shared by every transport in it: the three weapon slots of <c>dat\LC_WPNS.DAT</c>,
/// which <c>Base_LoadResources</c> (<c>00405fac</c>) reads into <c>g_LcWeaponSlots</c>, and the records
/// <c>Base_TransportThinkTick</c> (<c>004045c8</c>) names by literal for them
/// (docs/retail/simulation/structure-behaviour.md, "The transport").
/// </summary>
/// <param name="Slots">The <c>LC_WPNS.DAT</c> records; slot 0 is the launcher, slots 1 and 2 the beams.</param>
/// <param name="Missile">The <c>PROJ.DAT</c> rocket record slot 0 launches — see <see cref="MissileSubtype"/>.</param>
/// <param name="Beam">The <c>PROJ.DAT</c> beam record slots 1 and 2 fire — see <see cref="BeamSubtype"/>.</param>
/// <param name="BeamRange">The ray length a beam slot fires, out of the <see cref="BeamTemplate"/> template.</param>
/// <param name="BeamPower">The power a beam slot fires at, out of the same template.</param>
public sealed record TransportArmament(IReadOnlyList<LcWeaponSlot> Slots,
		ProjectileData.Projectile? Missile, ProjectileData.Projectile? Beam, int BeamRange, short BeamPower) {
	/// <summary>The resource folder the slot table lives in, and its name inside it.</summary>
	public const string ResourceFolder = "dat";

	/// <inheritdoc cref="ResourceFolder"/>
	public const string SlotResource = "LC_WPNS.DAT";

	/// <summary>The launcher slot's subtype — <c>Rocket_Fire</c>'s literal 3, the <c>EO</c> round.</summary>
	public const short MissileSubtype = 3;

	/// <summary>The beam slots' subtype — <c>Bullet_FireBurst</c>'s literal 3.</summary>
	public const short BeamSubtype = 3;

	/// <summary>
	/// The weapon id whose mount template supplies the beam slots' range and power —
	/// <c>WeaponMountTemplate_GetByWeaponId</c>'s literal 8, <c>LAS100</c>.
	/// </summary>
	public const int BeamTemplate = 8;

	/// <summary>
	/// How many slots the tick reads per weapon station. A table with fewer gives no armament at all,
	/// where the original would read past its end.
	/// </summary>
	public const int SlotCount = 3;

	/// <summary>
	/// Parses the slot table and resolves the records it fires. Null when the table is unreadable or
	/// short, which leaves every transport unarmed.
	/// </summary>
	public static TransportArmament? Load(byte[]? lcWpnsDat, WeaponCatalog? weapons) {
		if (new LcWeaponDataTransformer().Parse(lcWpnsDat) is not { Entries: { Length: >= SlotCount } slots }) {
			return null;
		}

		var template = weapons?.Template(BeamTemplate);
		return new TransportArmament(slots,
			weapons?.Lookup(ProjectileType.Rocket, MissileSubtype),
			weapons?.Lookup(ProjectileType.Beam, BeamSubtype),
			template?.Range ?? 0, template?.ShotCost ?? 0);
	}
}
