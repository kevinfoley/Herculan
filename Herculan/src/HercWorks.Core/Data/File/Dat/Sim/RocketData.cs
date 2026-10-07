using HercWorks.Core.Data.Struct.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /SIMVOL0/DAT/ROCKETS.DAT — a <c>UINT16</c> count, then 14-byte <see cref="RocketType"/> records
/// indexed by a <see cref="ProjectileData"/> record's subtype id. See docs/retail/formats/rockets-dat.md.
/// </summary>
public class RocketData {
	public short Total { get; set; }
	public RocketType[]? Entries { get; set; }
}
