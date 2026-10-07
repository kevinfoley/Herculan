using HercWorks.Core.Data.Struct.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /SIMVOL0/DAT/BULLETS.DAT — a <c>UINT16</c> count, then 14-byte <see cref="BulletType"/> records
/// indexed by a <see cref="ProjectileData"/> record's subtype id. See docs/retail/simulation/projectiles.md.
/// </summary>
public class BulletData {
	public short Total { get; set; }
	public BulletType[]? Entries { get; set; }
}
