using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// The grenade, the grenade's file (docs/es1/simulation/projectiles.md#the-grenade): <c>GrenadeVtable</c>
/// (<c>ES1DBSIM 00090b7e</c>). The grenade launcher's dispatch calls <see cref="FireNoOp"/>, which launches nothing, and
/// nothing references the class's constructor, <c>Grenade_Construct</c> (<c>000078c4</c>), whose type tables
/// <c>g_GrenadeTypeTable</c> and <c>g_GrenadeShapes</c> the file's phase-2 loader, a lone <c>RET</c>, never fills; so no
/// grenade is ever built and the class is not ported past its tick.
/// </summary>
public static class Es1Grenade {
	/// <summary>
	/// <c>Grenade_FireNoOp</c> (<c>00007b44</c>)(throw distance, machine, angles, muzzle point, subtype): a lone
	/// <c>RET 0x14</c>, which reads none of its arguments.
	/// </summary>
	public static void FireNoOp(int throwDistance, IEs1SimObject machine, (short Pitch, short Roll, short Heading) angles,
			Vec3i muzzle, short subtype) {
		_ = (throwDistance, machine, angles, muzzle, subtype);
	}

	/// <summary><c>Grenade_TickNoOp</c> (<c>00007b47</c>), slot <c>+0x18</c>: 0, so a grenade would never end.</summary>
	public static short TickNoOp() => 0;
}
