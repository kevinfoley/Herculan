namespace Herculan.Engine.Sim;

/// <summary>
/// The catalog ids DBSIM addresses its sounds by — an index into <c>str\SOUNDS.STR</c>.
///
/// <para>Ids 0-9 are the music half of the catalog and 10 upwards the effects half; the split is
/// what <c>Sound_IsCategoryEnabled</c> (<c>00462680</c>) tests, and it is why every data-driven
/// table in the simulation plays its stored <c>SoundId + 10</c>. Use <see cref="FirstEffect"/>
/// rather than writing the 10 out.</para>
///
/// <para>Only the ids some ported code names as a literal are given constants here. The ones that
/// arrive from a data table — <c>BULLETS.DAT</c>'s fire sound, <c>ROCKETS.DAT</c>'s, an
/// <c>EXPLOS.DAT</c> row's impact sound — are not named, because the table decides them.</para>
/// </summary>
public static class SoundId {
	/// <summary>Lowest id in the effects half of the catalog, and the bias every data table stores against.</summary>
	public const int FirstEffect = 10;

	/// <summary>Number of entries the retail catalog holds.</summary>
	public const int Count = 57;

	/// <summary><c>laser1.wav</c> — the beam muzzle report, from <c>Bullet_FireBurst</c>.</summary>
	public const int BeamFire = 0x0b;

	/// <summary><c>gm_69.wav</c> — the console button click.</summary>
	public const int ButtonClick = 0x11;

	/// <summary><c>start3.wav</c> — the cockpit power-up sequence.</summary>
	public const int PowerUp = 0x13;

	/// <summary><c>bptslct.wav</c> — target acquired, and a unit or gridpoint picked on the command display's map.</summary>
	public const int TargetSelect = 0x14;

	/// <summary><c>trgloc.wav</c> — the missile lock tone, repeated on its blink cadence while locked.</summary>
	public const int LockTone = 0x15;

	/// <summary><c>trgunloc.wav</c> — played once when a lock is lost.</summary>
	public const int LockLost = 0x16;

	/// <summary><c>warn1.wav</c> — the general cockpit warning, five repeats.</summary>
	public const int Warning = 0x17;

	/// <summary><c>wrnwoop2.wav</c> — the rising warning whoop, five repeats.</summary>
	public const int WarningWhoop = 0x18;

	/// <summary><c>strcfail.wav</c> — structural failure, five repeats.</summary>
	public const int StructuralFailure = 0x19;

	/// <summary>
	/// <c>gnract.wav</c> — scanner switched to ACTIVE, from <c>Mech_ToggleRadarMode</c>
	/// (<c>0041b468</c>). The command display's [Tab] plays it as it makes a held unit the pick.
	/// </summary>
	public const int ScannerActive = 0x1a;

	/// <summary><c>gnrdact.wav</c> — scanner switched to PASSIVE, and the command display's [Tab] with no unit held.</summary>
	public const int ScannerPassive = 0x1b;

	/// <summary><c>whitenz.wav</c> — comm static, looped while a comm box has no signal.</summary>
	public const int CommStatic = 0x1c;

	/// <summary><c>foot2.wav</c> — a foot planting, from <c>Mech_PlaceLegsOnGround</c>.</summary>
	public const int Footfall = 0x1d;

	/// <summary><c>callpsa.wav</c> — played at the machine's own position from <c>Mech_LocomotionTick</c>.</summary>
	public const int LocomotionCallA = 0x1e;

	/// <summary><c>explo4.wav</c>.</summary>
	public const int Explosion4 = 0x21;

	/// <summary><c>explo2.wav</c> — the collision thump, from <c>Mech_CollisionTest</c> and the death fall.</summary>
	public const int Collision = 0x29;

	/// <summary>
	/// <c>throtl.wav</c> — the throttle lever moving, and the Turbo Pod engaging
	/// (<c>TurboPod_Engage</c>, <c>0040f09c</c>), which sounds the same servo.
	/// </summary>
	public const int Throttle = 0x2c;

	/// <summary><c>herceng1.wav</c> — the engine hum, looped for the machine's whole life. Only a flyer starts it; see docs/retail/simulation/audio.md, "The cockpit power-up".</summary>
	public const int EngineLoop = 0x2d;

	/// <summary><c>shield1.wav</c> — the shield sound. Despite the name its row plays once (loop count 1).</summary>
	public const int ShieldLoop = 0x2e;

	/// <summary><c>podin2.wav</c> — a drop pod falling.</summary>
	public const int PodFalling = 0x2f;

	/// <summary><c>podland.wav</c> — a drop pod striking the ground.</summary>
	public const int PodLanded = 0x30;

	/// <summary><c>flyby1.wav</c> — a flyer passing, looped while it is alive.</summary>
	public const int FlyerLoop = 0x31;

	/// <summary><c>missin.wav</c> — missile inbound, from <c>Rocket_TickUpdate</c>.</summary>
	public const int MissileInbound = 0x32;

	/// <summary>
	/// <c>fire1a.wav</c> — something burning. <c>fire.cpp</c> starts it on the first live
	/// <see cref="FireEffect"/> and stops it on the last, so it is one loop for every fire in the
	/// mission at once, kept positioned on whichever fire is nearest the camera — see
	/// <see cref="SimWorld.Tick"/>.
	/// </summary>
	public const int BurningObject = 0x33;

	/// <summary>
	/// The pitch <c>Cockpit_PowerUpSound</c> (<c>004328cc</c>) hands the engine loop immediately after starting it, as a 16.16
	/// ratio: <c>42000 / 65536</c>, about 0.64. The sample is pitched down to a hum.
	/// </summary>
	public const int EngineLoopPitch = 42000;
}
