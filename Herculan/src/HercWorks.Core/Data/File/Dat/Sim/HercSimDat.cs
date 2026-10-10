namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - dat\[herc].dat — the 216-byte mech type record <c>MechType_InitOne</c> (<c>004201a8</c>)
/// loads into <c>MECH_TYPE_DATA[i]+2</c>, so record offset N is the exe's <c>typeRecord+N+2</c>.
/// Offsets below are record offsets, in decimal. The field table and per-chassis values are in
/// docs/retail/simulation/mech-locomotion.md#mech-type-record.
/// </summary>
public class HercSimDat {
	/// <summary>Offset 0 — maximum turn rate. Not rescaled at load.</summary>
	public short SpeedTurn { get; set; }

	/// <summary>Offset 2 — maximum reverse speed, negative.</summary>
	public short SpeedReverse { get; set; }

	/// <summary>Offset 4 — maximum forward speed.</summary>
	public short SpeedForward { get; set; }

	/// <summary>Offset 6 — how far the speed scalar moves toward its target each tick.</summary>
	public short SpeedAccelDecel { get; set; }

	/// <summary>Offset 8 — the same step for the turn rate.</summary>
	public short TurnAccelDecel { get; set; }

	/// <summary>Offset 10 — the model node the cockpit eye rides.</summary>
	public short CameraPartId { get; set; }

	/// <summary>Offset 12 — walk sequence id.</summary>
	public short AnimId_Walk { get; set; }

	/// <summary>Offset 14 — run sequence id.</summary>
	public short AnimId_Run { get; set; } = 2;

	/// <summary>Offset 16 — the stop / step-off sequence played when slowing to a halt forwards.</summary>
	public short AnimId_StopMove { get; set; } = 3;

	/// <summary>Offset 18 — the stop / step-off sequence played when slowing to a halt in reverse.</summary>
	public short AnimId_StopReverse { get; set; } = 4;

	/// <summary>Offset 20 — ride height, added to the terrain height under the machine.</summary>
	public short RideHeight { get; set; }

	/// <summary>
	/// Offset 22 — how high above the machine's origin its direct-fire hit cylinder is centred:
	/// 1000 heavy and medium, 750 light, 0 RAZOR.
	/// </summary>
	public short HitCenterHeight { get; set; }

	/// <summary>
	/// Offset 24 — the radius of that cylinder, and of the coarse reject in front of it: 2500 heavy,
	/// 1500 medium, 1000 SPIDER.
	/// </summary>
	public short HitRadius { get; set; }

	/// <summary>
	/// Offset 26 — the torso-twist sequence: a full turn of the torso node, which the twist angle
	/// seeks a position within. See docs/retail/simulation/torso-aim.md.
	/// </summary>
	public short AnimId_TorsoTwist { get; set; }

	/// <summary>Offset 28 — twist rate at full stick.</summary>
	public short TorsoTwistMaxRate { get; set; }

	/// <summary>Offset 30 — how fast the twist rate may build.</summary>
	public short TorsoTwistAccel { get; set; }

	/// <summary>
	/// Offset 32 — twist limit either way, as a binary angle: 14000 everywhere but the PITBULL's
	/// 32767, which is no limit.
	/// </summary>
	public short TorsoTwistLimit { get; set; }

	/// <summary>Offset 34 — the torso-pitch sequence, the pitch counterpart of <see cref="AnimId_TorsoTwist"/>.</summary>
	public short AnimId_TorsoPitch { get; set; }

	/// <summary>Offset 36 — pitch rate at full stick.</summary>
	public short TorsoPitchMaxRate { get; set; }

	/// <summary>Offset 38 — how fast the pitch rate may build.</summary>
	public short TorsoPitchAccel { get; set; }

	/// <summary>Offset 40 — pitch limit looking up.</summary>
	public short TorsoPitchMax { get; set; }

	/// <summary>Offset 42 — pitch limit looking down, negative.</summary>
	public short TorsoPitchMin { get; set; }

	/// <summary>Offset 44 — the speed at which the walk gait gives way to the run gait.</summary>
	public short GaitThreshold { get; set; }

	/// <summary>Record offset of <see cref="ModelLoDBoneIds"/>.</summary>
	public static int ModelLodArrOFs { get; set; } = 46;

	/// <summary>
	/// Offsets 46-65 — signed part ids, ended by a negative one: the parts whose nodes DBSIM uses to
	/// renumber each crude LOD root onto root 0's nodes at load. See
	/// docs/retail/rendering/mech-shape-drawing.md, "The crude roots are renumbered at load".
	/// </summary>
	public byte[] ModelLoDBoneIds { get; set; } = new byte[20];

	/// <summary>
	/// Offset 66 — the base term of the chassis' AI combat rating, which DBSIM's target weighting and
	/// flee check both weigh machines by. 1000 on every retail chassis. See
	/// docs/retail/simulation/ai-targeting.md#relative-combat-rating.
	/// </summary>
	public short AiRatingBase { get; set; } = 1000;

	/// <summary>
	/// Offset 68 — the death / fall sequence an immobilised machine goes down in; the pose at its last
	/// frame is where the wreck stays. Every biped states 7, the PITBULL 2, the SPIDER 1. See
	/// docs/retail/simulation/mech-locomotion.md#going-down.
	/// </summary>
	public short AnimId_Death { get; set; }

	/// <summary>Offset 70. Meaning not established.</summary>
	public short LegsCritFlags2 { get; set; }

	/// <summary>Offset 72 — how many legs the chassis walks on: 2, except the PITBULL's 4.</summary>
	public short LegCount { get; set; }

	/// <summary>
	/// Offset 74 — nonzero: this chassis leaves no wreck. On death it drops to <c>in limbo</c> rather
	/// than <c>dead</c>, is sunk out of the world and loses its shadows. Set only on the SPIDER. The
	/// debris a destroyed component throws is unaffected.
	/// </summary>
	public short VanishesOnDeath { get; set; }

	/// <summary>
	/// Offset 76 — the chassis' mass, the Q10 weight each party's speed carries in a collision:
	/// 5000 for a light through 20000 for the PITBULL, and 0 for the SPIDER.
	/// </summary>
	public short Mass { get; set; }

	/// <summary>
	/// Offset 78 — nonzero for a flyer (the RAZOR alone): selects the flight code paths and the
	/// <c>fm\&lt;NAME&gt;.FM</c> load. See docs/retail/simulation/razor-flight.md.
	/// </summary>
	public short FlyerFlag { get; set; }

	/// <summary>
	/// Offset 80 — which of <c>COCKPIT.DPL</c>'s nine 24-entry colour schemes the cockpit installs.
	/// A 0-8 permutation over the nine player HERCs. See docs/retail/rendering/cockpit-canopy-palette.md#palette.
	/// </summary>
	public short CockpitColorScheme { get; set; }

	/// <summary>
	/// Offset 82 — what the chassis' wreck is worth, a Q10 scale on its weighted remaining armour:
	/// 1024, 1500 or 800 across retail. <c>Mech_SalvageValue</c> (<c>00418e60</c>) reads it.
	/// </summary>
	public short SalvageScale { get; set; }

	/// <summary>
	/// Offset 84 — whether a hit can knock this chassis' weapon mounts out: 1 on every biped, 0 on
	/// the PITBULL. See docs/retail/simulation/weapon-mounts.md#the-chance-path--the-destruction-roll.
	/// </summary>
	public short WeaponMountsDestructible { get; set; }

	/// <summary>Offsets 86-97 — the chassis name, NUL-padded ASCII.</summary>
	public byte[]? NameBytes { get; set; }

	/// <summary>
	/// Offsets 98 and 100 — the pilot's eye relative to <see cref="CameraPartId"/>'s node, in that
	/// node's frame: fore/aft, then lift. The eye point is <c>(0, EyeOffsetY, EyeOffsetZ)</c>.
	/// </summary>
	public short EyeOffsetY { get; set; }

	/// <inheritdoc cref="EyeOffsetY"/>
	public short EyeOffsetZ { get; set; }

	/// <summary>
	/// Offsets 102 and 104 — the point the external camera orbits, in the machine's own frame:
	/// fore/aft (0 on every retail chassis), then height. See docs/retail/simulation/external-views.md.
	/// </summary>
	public short OrbitCentreY { get; set; }

	/// <inheritdoc cref="OrbitCentreY"/>
	public short OrbitCentreZ { get; set; }

	// 106 - blank

	/// <summary>Offset 108 — <see cref="GaitThreshold"/> on the reverse side.</summary>
	public short GaitThresholdReverse { get; set; }

	/// <summary>
	/// Offset 110 — the machine's body radius: what the blast sweep measures its surface by and what
	/// keeps two machines apart. 750 on every retail HERC. Distinct from <see cref="HitRadius"/>.
	/// See docs/retail/simulation/hit-detection.md#the-three-radius-slots.
	/// </summary>
	public short BodyRadius { get; set; }

	/// <summary>
	/// Offsets 112-121 as shorts, which is how the writer emits them. The bytes are really two
	/// per-entry lists, <see cref="LegKinds"/> and <see cref="LegPartIds"/>; these shorts have no
	/// meaning of their own.
	/// </summary>
	public short ModelFlagsShadow1 { get; set; }

	/// <inheritdoc cref="ModelFlagsShadow1"/>
	public short ModelFlagsShadow2 { get; set; }

	/// <inheritdoc cref="ModelFlagsShadow1"/>
	public short Unk116_val { get; set; }

	/// <inheritdoc cref="ModelFlagsShadow1"/>
	public short Unk118_val { get; set; }

	/// <inheritdoc cref="ModelFlagsShadow1"/>
	public short Unk120_val { get; set; }

	/// <summary>
	/// Offsets 112 and 117 read as <b>bytes</b>, one per shadow the machine casts — the entry's kind,
	/// which is also the index of the flat-set shape laid under it (<c>typeRec+0x72</c>), and the shape
	/// part id it follows (<c>typeRec+0x77</c>). The first list runs to its first negative byte, and
	/// that length is the entry count (<c>Mech_Constructor</c>, <c>Mech_PlaceLegsOnGround</c>
	/// <c>004195c8</c>), so it is not <see cref="LegCount"/>. Retail states kinds 0, 0, 2 on parts
	/// 14, 15, 12 on every HERC but the PITBULL (four feet, kind 0, on parts 14, 15, 22, 23) and the
	/// SPIDER (none). See docs/retail/simulation/ground-shapes.md#a-hercs-shadows.
	///
	/// <para><b>Read-only views.</b> These bytes overlap the shorts at 112-121, which are what the
	/// writer emits; setting these changes nothing on the way out.</para>
	/// </summary>
	public byte[] LegKinds { get; set; } = System.Array.Empty<byte>();

	/// <inheritdoc cref="LegKinds"/>
	public byte[] LegPartIds { get; set; } = System.Array.Empty<byte>();

	/// <summary>
	/// Offset 122 — the turn-in-place sequence. Uniform across the fleet: 7 frames of 1820 BAM each,
	/// no translation.
	/// </summary>
	public short AnimId_TurnInPlace { get; set; }

	/// <summary>Entries in <see cref="AiRatingSystemPenalty"/>.</summary>
	public static int AiRatingSystemPenaltyCount { get; set; } = 12;

	/// <summary>
	/// Offsets 124-147 — twelve shorts, one per system, of which the AI combat rating reads the first
	/// ten: what that system costs the rating once it is past 70% damaged. 500 for all twelve on every
	/// retail chassis.
	/// </summary>
	public short[]? AiRatingSystemPenalty { get; set; }

	/// <summary>
	/// Offset 148 — which shared texture group DBSIM binds to every sub-shape of this mech:
	/// <c>MechType_InitOne</c> writes <c>&amp;g_MechTextureGroupSlots + value*8</c> into
	/// <c>TSShape+0x26</c> of each root shape. <see cref="TextureGroupDbaBaseName"/> names the groups; the
	/// per-mech roster is in docs/retail/rendering/dts-texture-binding.md#dbsims-mech-to-texture-mapping.
	/// </summary>
	public short TextureGroup { get; set; }

	/// <summary>
	/// Maps <see cref="TextureGroup"/> to the simvol0/dba/&lt;name&gt;.DBA basename DBSIM loads for that
	/// group — the exe's literal 7-entry name table. Null for an out-of-range value.
	/// </summary>
	public static string? TextureGroupDbaBaseName(short textureGroup) => textureGroup switch {
		0 => "LIGHT",
		1 => "MEDIUM",
		2 => "HEAVY",
		3 => "ENEMY",
		4 => "APOCATEX",
		5 => "RAZORTEX",
		6 => "NEWHERCS",
		_ => null
	};

	/// <summary>
	/// Offsets 150, 152, 154 and 156 — the height a leg node's fore/aft position must cross for a
	/// <b>footfall</b>, one per gait: walking forward, walking backward, running, and the
	/// falling/landing case. <c>Mech_PlaceLegsOnGround</c> (<c>004195c8</c>) reads them as
	/// <c>typeRec+0x98 + gait*2</c>.
	///
	/// <para>A leg arms when it passes <see cref="FootfallRearmWalk"/> and fires when it comes back
	/// through this one, which is the instant the foot plants: the original plays sound <c>0x1d</c>
	/// and, for the player, kicks the cockpit view. The reverse gait's pair is negative and its two
	/// comparisons are the other way round.</para>
	/// </summary>
	public short FootfallTriggerWalk { get; set; }

	/// <inheritdoc cref="FootfallTriggerWalk"/>
	public short FootfallTriggerReverse { get; set; }

	/// <inheritdoc cref="FootfallTriggerWalk"/>
	public short FootfallTriggerRun { get; set; }

	/// <inheritdoc cref="FootfallTriggerWalk"/>
	public short FootfallTriggerLand { get; set; }

	// 158 - 169 - blank

	/// <summary>
	/// Offsets 170, 172 and 174 — the arming counterpart of <see cref="FootfallTriggerWalk"/>, in the
	/// same gait order (<c>typeRec+0xac + gait*2</c>). A leg that has not passed this since its last
	/// footfall cannot fire another.
	///
	/// <para>The landing gait's entry, at offset 176, is inside the blank run below: it is zero in
	/// every retail file and is left unparsed.</para>
	/// </summary>
	public short FootfallRearmWalk { get; set; }

	/// <inheritdoc cref="FootfallRearmWalk"/>
	public short FootfallRearmReverse { get; set; }

	/// <inheritdoc cref="FootfallRearmWalk"/>
	public short FootfallRearmRun { get; set; }

	// 176 - 189 - blank

	/// <summary>
	/// Offset 190 — the shield array's capacity before any Shield Pod: 3500 on every HERC, 0 on the
	/// SPIDER. See docs/retail/simulation/damage-system.md#the-shield-system.
	/// </summary>
	public short ShieldMaxTotal { get; set; }

	/// <summary>
	/// Offset 192 (<c>typeRec+0xc2</c>) — a flyer's HUD speed at its top airspeed, in km/h: 265 on the
	/// RAZOR. A walker's value is overwritten at load. See
	/// docs/retail/simulation/razor-flight.md#hud-speed.
	/// </summary>
	public short HudTopSpeed { get; set; }

	/// <summary>
	/// Offsets 194 and 196 — the stride-calibration pair <c>MechType_InitOne</c> turns into the Q16
	/// factor it rescales the speed fields by, <c>Q16Divide(offset196 * 400, offset194)</c>. See
	/// docs/retail/simulation/mech-locomotion.md#load-time-speed-rescale.
	/// </summary>
	public short StrideScaleDivisor { get; set; }

	/// <inheritdoc cref="StrideScaleDivisor"/>
	public short StrideScaleNumerator { get; set; }

	// 198 - 203 - blank

	/// <summary>
	/// Offsets 204-215 — the base name of the chassis' own debris table (<see cref="DebrisHerc"/>),
	/// NUL-padded; empty for a chassis that names none.
	/// </summary>
	public byte[]? DebrisFile { get; set; }
}
