using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /DBSIM/DAT/WEAPONS.DAT — DBSIM's own runtime weapon-mount-template table (distinct from
/// SHELL0/GAM/WEAPONS.DAT, the UI-facing weapon catalog, <see cref="Shell.WeaponsDat"/>). Loaded by
/// <c>Weapons_LoadResourceTables</c> (<c>0040fc8c</c>) from a resource named "weapons"; one record per
/// weapon id, 33 in retail.
///
/// <para>Records are variable-length, not a fixed stride: each opens with a <c>.DMG</c> piece record
/// and a <c>.COL</c> cluster, read by those formats' own readers, and ends in a fixed 48-byte tail —
/// see <see cref="WeaponMountTemplate"/>. The tail is kept raw so the file round-trips byte-exact. Layout: docs/retail/formats/weapons-dat-sim.md.</para>
/// </summary>
public class Weapons {
	public short Total { get; set; }
	public WeaponMountTemplate[]? Templates { get; set; }

	public Weapons() { }

	public Weapons(short total) {
		Total = total;
	}

	public WeaponMountTemplate NewWeaponMountTemplate() => new();

	/// <summary>
	/// One weapon's mount-template record. See docs/retail/formats/weapons-dat-sim.md for the
	/// field-by-field evidence.
	/// </summary>
	public class WeaponMountTemplate {
		/// <summary>
		/// In-memory <c>0x00</c>-<c>0x11</c>, a <c>.DMG</c> piece record read by
		/// <c>HercPiece_ReadRecord</c>: the piece a fitted weapon's mount component takes in place of
		/// the chassis file's own — its armour (1500 to 15000), no debris group or cell sequence, the
		/// destruct flag, and one internal at spill weight 20. See
		/// docs/retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece.
		/// </summary>
		public HercSimDamage.HercPiece Piece { get; set; } = new() { MappedInternals = [] };

		/// <summary>
		/// In-memory <c>0x12</c>-<c>0x21</c>, a <c>.COL</c> cluster read by
		/// <c>Collision_ReadCluster</c>: the weapon's own hit spheres, about its mount point, which a
		/// fitted weapon puts into its mount component's cluster. Its component index (19 on disk) is
		/// overwritten at load. See
		/// docs/retail/formats/collision-spheres.md#a-fitted-weapon-brings-its-own-spheres.
		/// </summary>
		public ColliderCluster Cluster { get; set; } = new(0, []);

		/// <summary>
		/// The 48-byte tail, record offset <c>0x22</c>-<c>0x51</c>, kept raw. The doc's table gives the
		/// decoded fields by in-memory offset; tail-relative is that minus <c>0x22</c>: the four model
		/// shapes at <c>0x00</c>-<c>0x06</c>, <see cref="InternalMaximum"/> <c>0x08</c>, minimum range
		/// (int32) <c>0x0a</c>, range (int32) <c>0x0e</c>, AI shot-value penalty <c>0x12</c>, energy
		/// thresholds <c>0x14</c>/<c>0x16</c>, magazine size <c>0x18</c>, barrel count <c>0x1a</c>,
		/// <see cref="ProjDatIndex"/> <c>0x1c</c>, muzzle offset <c>0x1e</c>-<c>0x22</c>, side muzzle
		/// offset (three int16) <c>0x24</c>-<c>0x28</c>, refire delay <c>0x2a</c>, combat-rating value
		/// <c>0x2c</c>, <see cref="DamageIconIndex"/> <c>0x2e</c>. See
		/// docs/retail/formats/weapons-dat-sim.md#decoded-tail-fields.
		/// </summary>
		public byte[] Tail { get; set; } = new byte[0x30];

		/// <summary>
		/// Tail-relative <c>0x08</c> (in-memory <c>0x2a</c>) — the maximum of the internal behind the
		/// mount component, which a fitted weapon writes into its slot's internal. 500 on the guns,
		/// 15000 on the three big ones (ids 19-21), 0 on the pods. See
		/// docs/retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece.
		/// </summary>
		public short InternalMaximum => BitConverter.ToInt16(Tail, 0x08);

		/// <summary>
		/// Tail-relative <c>0x1c</c> (in-memory <c>0x3e</c>) — how the weapon reaches its
		/// <see cref="ProjectileData"/> record: <c>0x21</c> (33) for none (<c>ECM</c>), <c>0x22</c> (34)
		/// for a <c>Rocket</c> record chosen by the hardpoint's ammunition type
		/// (<c>MSL6</c>/<c>MSL8</c>/<c>MSL10</c>/<c>FLYMSL</c>), otherwise a direct PROJ.DAT index. The
		/// non-firing entries carry 0, which their mount constructors never consume. See
		/// docs/retail/formats/weapons-dat-sim.md#the-projdat-index--tail-relative-offset-0x1c-absolute-offset-0x3e.
		/// </summary>
		public short ProjDatIndex => BitConverter.ToInt16(Tail, 0x1c);

		/// <summary>
		/// Tail-relative <c>0x0e</c> (in-memory <c>0x30</c>) — the weapon's range in world units, the
		/// length <c>Bullet_FireBurst</c> gives the ray. See docs/retail/formats/weapons-dat-sim.md.
		/// </summary>
		public int Range => BitConverter.ToInt32(Tail, 0x0e);

		/// <summary>
		/// Tail-relative <c>0x16</c> (in-memory <c>0x38</c>) — the upper energy threshold, which the
		/// beam fire paths pass as the shot's power. See docs/retail/formats/weapons-dat-sim.md.
		/// </summary>
		public short ShotCost => BitConverter.ToInt16(Tail, 0x16);

		/// <summary>
		/// Tail-relative <c>0x2e</c> (in-memory <c>0x50</c>): which icon of the <c>WEAPONS</c> bank the
		/// Heads-Down Display's damage detail draws for this weapon, before the <c>.PDG</c>
		/// hardpoint's own frame offset is added. -1 draws none. See
		/// docs/retail/formats/weapons-dat-sim.md.
		/// </summary>
		public short DamageIconIndex => BitConverter.ToInt16(Tail, 0x2e);

		/// <summary>
		/// Which shape of <c>dts\MECHWPNS.DTS</c> this weapon is drawn as when it is fitted to a
		/// hardpoint whose mounting code (<c>.GL +6</c>,
		/// <see cref="Dbsim.GunLayout.HardpointEntry.MountingCode"/>) is
		/// <paramref name="mountingCode"/> — four shorts at tail-relative <c>0x00</c>-<c>0x06</c>,
		/// one per code, read by <c>WeaponMount_ShapeForMountingCode</c> (<c>0040fab0</c>) as <c>template[0x22 + code * 2]</c>.
		///
		/// <para>The four are the same gun modelled for the four ways it can hang off a chassis, so
		/// an autocannon reads four different shapes and a shoulder-mounted launcher reads the same
		/// one four times. <b>The shape's flipbook is the muzzle flash</b> — see
		/// <c>Herculan.Engine.Sim.WeaponMount.FlashCell</c>.</para>
		///
		/// <para>Code 4 is the invisible mounting and has no entry: nothing is drawn for it and the
		/// base mount constructor loads no shape at all.</para>
		/// </summary>
		public short ModelShapeIndex(int mountingCode) =>
			mountingCode >= 0 && mountingCode < 4 ? BitConverter.ToInt16(Tail, mountingCode * 2) : (short)-1;
	}
}
