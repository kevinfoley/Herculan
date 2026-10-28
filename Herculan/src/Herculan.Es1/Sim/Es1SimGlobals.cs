using Herculan.Engine.Numerics;
using Herculan.Es1.Projectiles;

namespace Herculan.Es1.Sim;

/// <summary>
/// The simulator's globals a machine's tick reads and writes: the clock, the generator, the dwell jitter, the turret's
/// servo latches, the live object list, the structures, the zone's shapes and the pixel pick. Retail keeps each at a fixed address; the world owns one of these.
/// </summary>
public sealed class Es1SimGlobals {
	/// <summary>The tick length and the helpers that scale by it (<see cref="Es1SimClock"/>).</summary>
	public Es1SimClock Clock { get; } = new();

	/// <summary>
	/// The simulation's generator at <c>000b5bd4</c>, which <c>Main_StaticInit</c> (<c>00057414</c>) seeds with
	/// <c>Math_RandomSeed</c> (<c>00063aa4</c>) from ES2's table and cursors, so it starts in the state ES2's port starts in
	/// (docs/es1/simulation/fixed-point-math.md#helpers-that-match, docs/es1/simulation/main-loop.md#functions).
	/// </summary>
	public SimRandom Random { get; } = new();

	/// <summary>
	/// The simulator's second generator at <c>000b5c48</c>, which <c>Main_StaticInit</c> seeds as it seeds
	/// <see cref="Random"/>, so it starts in the same state (docs/es1/simulation/main-loop.md#functions); a message post
	/// rolls its variant on it (docs/es1/simulation/cockpit-messages.md#posting). The clouds and the ground's specks,
	/// which retail also draws on it (docs/es1/formats/wld-world.md), draw on their own in this engine
	/// (<see cref="Scene.Es1Clouds"/>), so its state at a post is not retail's.
	/// </summary>
	public SimRandom PresentationRandom { get; } = new();

	/// <summary>
	/// <c>CockpitViewInstance</c> (<c>00092d1c</c>), the first cockpit built, which <c>ColorSchemePanels_LoadAll</c>
	/// stores when it is none (docs/es1/simulation/cockpit-panel.md#building-it); none before one is built.
	/// </summary>
	public View.Es1Cockpit? CockpitViewInstance { get; set; }

	/// <summary>
	/// <c>HddDisplayInstance</c> (<c>000944cc</c>), the Heads-Down Display last built, which <c>HddDisplay_Ctor</c> stores
	/// and <c>HddGauge_Name</c> reads (docs/es1/simulation/heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258);
	/// none before one is built.
	/// </summary>
	public Hdd.Es1HddDisplay? HddDisplayInstance { get; set; }

	/// <summary>The speech slots and the lip-sync player (<see cref="Sound.Es1VoiceLines"/>), with no slots until the mission start's <see cref="Sound.Es1VoiceLines.InitSlots"/>.</summary>
	public Sound.Es1VoiceLines VoiceLines { get; } = new();

	/// <summary><c>Behaviour_DwellJitter</c> (<c>000bc426</c>).</summary>
	public Es1DwellJitter DwellJitter { get; } = new();

	/// <summary>The servo-sound helpers' latches and last angles (<see cref="Es1TurretServos"/>).</summary>
	public Es1TurretServos TurretServos { get; } = new();

	/// <summary><c>LiveObjectList</c>, the objects <see cref="AddLiveObject"/> has added, in the order added.</summary>
	public List<IEs1SimObject> LiveObjects { get; } = new();

	/// <summary>
	/// Script.dat header word 8 (<c>000bc4ac</c>), which picks the world file (docs/es1/formats/wld-world.md#which-file) and
	/// <c>Detection_ContactScanRange</c>'s range (docs/es1/simulation/world.md#small-functions).
	/// </summary>
	public short HeaderWord8 { get; set; }

	/// <summary>
	/// Script.dat header word 3 (<c>000bc4a2</c>), the mission's selector: the arm the player's think runs
	/// (docs/es1/simulation/mech-ai.md#the-players-think--mech_behaviourplayerthink-00015f80) and the question the status
	/// asks (docs/es1/simulation/mission-status.md#the-status--mission_status-0000f4e8).
	/// </summary>
	public short HeaderWord3 { get; set; }

	/// <summary>
	/// <c>MissionModeFlag</c> (<c>000bc4a0</c>), script.dat header word 2, the load's mode
	/// (docs/es1/formats/script-dat.md#the-header), which <see cref="FreeShots"/> tests for 0.
	/// </summary>
	public short MissionModeFlag { get; set; }

	/// <summary><c>UnlimitedAmmoFlag</c> (<c>000bc4a6</c>), script.dat header word 5, which <see cref="FreeShots"/> tests for 1.</summary>
	public short UnlimitedAmmoFlag { get; set; }

	/// <summary>
	/// Script.dat header word 6 (<c>000bc4a8</c>), which with <see cref="MissionModeFlag"/> 0 refuses the player's damage at
	/// 1 (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250).
	/// </summary>
	public short HeaderWord6 { get; set; }

	/// <summary>
	/// Script.dat header word 7 (<c>000bc4aa</c>), the difficulty <c>Damage_ScaleByDifficulty</c> reads
	/// (docs/es1/simulation/world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c).
	/// </summary>
	public short HeaderWord7 { get; set; }

	/// <summary><c>LocalPlayerMech</c> (<c>000b5b86</c>), which the simulation stores; none before it is built.</summary>
	public Es1Mech? LocalPlayerMech { get; set; }

	/// <summary><c>PlayerMech</c> (<c>000bc42a</c>), which the simulation stores; none before it is built.</summary>
	public Es1Mech? PlayerMech { get; set; }

	/// <summary>
	/// <c>MissionVars</c> (<c>000bc4b8</c>), the mission's flags (<see cref="Es1MissionScript.Flags"/>), which the
	/// out-of-action reports write (<see cref="Es1MissionGroup.ApplyOutOfAction"/>); empty until a world is spawned.
	/// </summary>
	public short[] MissionVars { get; set; } = [];

	/// <summary>
	/// <c>MissionActions</c> (<c>000bc496</c>), block 5's actions (<see cref="Es1MissionScript.Actions"/>), which
	/// <see cref="Es1Actions.EvaluateTriggers"/> walks; empty until a world is spawned.
	/// </summary>
	public IReadOnlyList<Es1Action> MissionActions { get; set; } = [];

	/// <summary><c>g_MechPool</c>'s machines in build order, which the blasts and the outnumbered damage walk from the end.</summary>
	public IReadOnlyList<Es1Mech> MechPool { get; set; } = [];

	/// <summary>
	/// <see cref="MissionModeFlag"/> 0 with <see cref="UnlimitedAmmoFlag"/> 1: the trigger's free-shot flag, which spares
	/// the rounds, and the energy turn's refund of the budget (docs/es1/simulation/weapon-mounts.md#energy--weaponmounts_arbitrateenergy-0000cc2c).
	/// </summary>
	public bool FreeShots => MissionModeFlag == 0 && UnlimitedAmmoFlag == 1;

	/// <summary>The player's think's latch and data link (<see cref="Es1PlayerThinkGlobals"/>).</summary>
	public Es1PlayerThinkGlobals PlayerThink { get; } = new();

	/// <summary>The enemy-sighted callout's globals (<see cref="Es1SensorGlobals"/>).</summary>
	public Es1SensorGlobals Sensors { get; } = new();

	/// <summary>
	/// <c>g_CenterTurretMode</c> (<c>000b5ba4</c>): the turret centres instead of following the twist and pitch. The tick's
	/// controls and <c>Player_PerFrameCockpitUpdate</c> both set it, so it lives with the globals both reach.
	/// </summary>
	public bool CenterTurretMode { get; set; }

	/// <summary>
	/// <c>EnergyMeter_Value</c> (<c>000af6fc</c>), the energy pool as <c>Player_PerFrameCockpitUpdate</c> hands it to the
	/// energy meter: (pool &lt;&lt; 10) / 10000.
	/// </summary>
	public int EnergyMeterValue { get; set; }

	/// <summary><c>Mech_LockTonePlay</c>'s latches (<see cref="Es1LockTone"/>).</summary>
	public Es1LockTone LockTone { get; } = new();

	/// <summary>
	/// <c>ObjectList_Add</c> (<c>0000db10</c>) on <c>LiveObjectList</c>: the object stored at the count's place and given
	/// that slot at <c>+0x45</c> (docs/es1/simulation/sim-object-base.md#the-live-object-list).
	/// </summary>
	public void AddLiveObject(IEs1SimObject simObject) {
		simObject.ListSlot = (short)LiveObjects.Count;
		LiveObjects.Add(simObject);
	}

	/// <summary>
	/// <c>g_BasePool</c> (<c>000aed70</c>)'s structures in build order. PLACEHOLDER: empty: blocks 7 and 8 are not spawned
	/// (the base constructor <c>00003cbc</c> is unread, docs/es1/formats/script-dat.md#open); the plan's flyers-and-bases
	/// step fills it.
	/// </summary>
	public List<Es1Structure> Structures { get; } = new();

	/// <summary><c>Zone_ShapeCount</c> and <c>Zone_ShapeObjects</c>, the zone's placed shapes; empty until the zone is loaded.</summary>
	public Es1ZoneShapes ZoneShapes { get; set; } = Es1ZoneShapes.Empty;

	/// <summary>The pixel pick, which the gunsight's click arms and the 3D pass answers (<see cref="Es1ObjectPick"/>).</summary>
	public Es1ObjectPick ObjectPick { get; } = new();

	/// <summary>
	/// <c>g_ProjectilePool</c> and the rounds' type tables (<see cref="Es1ProjectilePool"/>); a pool with no tables until
	/// the world's load gives it its own.
	/// </summary>
	public Es1ProjectilePool Projectiles { get; set; } = new();

	/// <summary>The effect light manager (<see cref="Effects.Es1EffectLights"/>).</summary>
	public Effects.Es1EffectLights EffectLights { get; } = new();

	/// <summary><c>g_FlatObjPool</c> and <c>g_FlatShapes</c>, the ground shapes' places and shapes (<see cref="Effects.Es1FlatObjPool"/>).</summary>
	public Effects.Es1FlatObjPool FlatObjects { get; } = new();

	/// <summary>
	/// <c>g_ExplosionPool</c> and the impact effects' tables (<see cref="Effects.Es1ExplosionPool"/>); a pool with no
	/// tables until the world's load gives it its own, on <see cref="EffectLights"/> and <see cref="FlatObjects"/>.
	/// </summary>
	public Effects.Es1ExplosionPool Explosions { get; set; }

	/// <summary>
	/// <c>g_DebrisPool</c> and <c>DEBRIS.DAT</c>'s groups and shapes (<see cref="Effects.Es1DebrisPool"/>); a pool with no
	/// groups or shapes until the world's load gives it its own.
	/// </summary>
	public Effects.Es1DebrisPool Debris { get; set; } = new();

	/// <summary>
	/// The smoke emitters' and balls' pools and the balls' shapes (<see cref="Effects.Es1SmokePool"/>); with no shapes
	/// until the world's load gives it its own.
	/// </summary>
	public Effects.Es1SmokePool Smoke { get; set; } = new();

	/// <summary>
	/// <c>g_TSDetailBiasForMounts</c> (<c>000bc2b8</c>) as <c>ShapeDetail_ApplyDetailSetting</c> last set it, which a thrown
	/// gun keeps (<see cref="Es1WeaponMount.Destroy"/>): the host hands the applied detail setting's in; 0 until it does.
	/// </summary>
	public Func<int> MountDetailBias { get; set; } = () => 0;

	public Es1SimGlobals() {
		Explosions = new Effects.Es1ExplosionPool(EffectLights, FlatObjects);
	}
}

/// <summary>
/// <c>Behaviour_DwellJitter</c> (<c>000bc426</c>), the <c>int16</c> <c>Behaviour_SetState</c> steps by 13 a state change
/// (docs/es1/simulation/shields-and-behaviour-states.md#the-behaviour-block). It lies past the image's file bytes
/// (<c>0xadcd8</c> of <c>0xbdd40</c>), so it starts at 0, as ES2's does.
/// </summary>
public sealed class Es1DwellJitter {
	public short Value { get; private set; }

	/// <summary>The step <c>Behaviour_SetState</c> takes: 13 added, then the low four bits.</summary>
	public short Next() {
		Value += 0xd;
		return (short)(Value & 0xf);
	}
}

/// <summary>
/// An object of <c>LiveObjectList</c>, as far as <c>Mech_CollisionTest</c> (<c>00014174</c>,
/// docs/es1/simulation/mech-locomotion.md#collision--mech_collisiontest-00014174) and the sensor model
/// (docs/es1/simulation/sensor-model.md#the-fields) read and write it: the fields every simulated object shares and the
/// vtable slots they call, with the object base's answer (docs/es1/simulation/sim-object-base.md#the-base-classs-own-functions)
/// for a slot a class leaves to it.
/// </summary>
public interface IEs1SimObject {
	/// <summary><c>+0x20</c>, the position.</summary>
	Vec3i Position { get; }

	/// <summary><c>+0xa</c>, the heading.</summary>
	short Heading { get; }

	/// <summary><c>+0x45</c>, the slot <c>ObjectList_Add</c> gave it, which indexes the per-object byte tables.</summary>
	short ListSlot { get; set; }

	/// <summary><c>+0x8f</c>, radar-visible: an active radar has shown it up.</summary>
	bool RadarVisible { get; set; }

	/// <summary><c>+0x90</c>, the radar: the object emits while it is set.</summary>
	bool RadarOn { get; }

	/// <summary><c>+0x93</c>, destroyed.</summary>
	bool Destroyed { get; }

	/// <summary><c>+0x98</c>, engaged, which the sensor model sets for an enemy known within 50000.</summary>
	bool Engaged { get; set; }

	/// <summary><c>+0x9c</c>, the per-tick gate: while set, the sensor model runs none of the object's engagement action; its tick clears it last.</summary>
	bool EngagementGate { get; set; }

	/// <summary><c>+0x9d</c>, the player's machine.</summary>
	bool IsPlayer { get; }

	/// <summary><c>+0x9e</c>, immobilized.</summary>
	bool Immobilized { get; }

	/// <summary><c>+0x15c</c>, how many objects hold it as their target.</summary>
	short TargetHolders { get; set; }

	/// <summary>
	/// Its shape's radius, the <c>int16</c> <c>+8</c> of the dword <c>+4</c> of its instance <c>+0x2e</c>, which the gunsight's
	/// update reads for the target box (docs/es1/simulation/cockpit-gunsight-hud.md#the-state-block).
	/// </summary>
	short ShapeRadius { get; }

	/// <summary>Slot <c>+0x34</c>'s first triple, the point an aim at the object is taken to, from its position.</summary>
	Vec3i AimOffset { get; }

	/// <summary><c>+0x16c</c>, the engagement action, or null.</summary>
	Es1Action? EngagementAction { get; }

	/// <summary>The contacts <c>+0xbc</c>, the line-of-sight cache <c>+0x10c</c> and the two countdowns <c>+0x178</c> and <c>+0x17b</c>.</summary>
	Es1SensorTables Sensors { get; }

	/// <summary>Slot <c>+0x40</c>, the turret twist: the base's <c>SimObject_GetTorsoTwistAngleZero</c> (<c>0000d6d0</c>) answers 0.</summary>
	short TorsoTwistAngle => 0;

	/// <summary>Slot <c>+0x48</c>, the sensor arc: the base's <see cref="Es1SensorModel.BearingInSensorArc"/>.</summary>
	bool BearingInSensorArc(short bearing) => Es1SensorModel.BearingInSensorArc(Heading, bearing);

	/// <summary>
	/// Slot <c>+0x4c</c>, the enemy-sighted callout, with the globals it reads and <c>PlayerMech</c>
	/// (<paramref name="playerMech"/>): the base's <c>SimObject_AiEnemySightedNoOp</c> (<c>0000e75c</c>) does nothing.
	/// </summary>
	void AiEnemySighted(IEs1SimObject sighted, Es1SimGlobals globals, Es1Mech playerMech) {
	}

	/// <summary><c>+0x3f</c>, the group.</summary>
	Es1MissionGroup? Group { get; }

	/// <summary><c>+0x162</c>, the target class, 0 for a HERC.</summary>
	short TargetClass { get; }

	/// <summary>Slot <c>+0x60</c>, the body radius; an object whose radius is 0 blocks nothing.</summary>
	short BodyRadius { get; }

	/// <summary>Slot <c>+0x6c</c>: something ran into it.</summary>
	void RunInto();

	/// <summary>
	/// Slot <c>+0x64</c>, what the damage write tells the attacker that put <paramref name="victim"/> out of the fight:
	/// the base's <c>SimObject_CreditNeutralisedTargetNoOp</c> (<c>0000d774</c>) does nothing.
	/// </summary>
	void CreditNeutralisedTarget(bool alreadyImmobilized, Es1Mech victim, Es1SimGlobals globals) {
	}

	/// <summary><c>+0x96</c>, the ECM spoof, which turns the missiles it fires off their aim (docs/es1/simulation/rockets.md#homing--rocket_homingsteer-00006eb4).</summary>
	bool EcmSpoofed { get; }

	/// <summary><c>+0x9b</c>, jamming, which an anti-radiation missile steers at.</summary>
	bool Jamming { get; }

	/// <summary><c>+0x9f</c>, disarmed, which keeps a shot that strikes it from the side that is not the shooter's off its slot <c>+0x54</c>.</summary>
	bool Disarmed { get; }

	/// <summary><c>+0xaf</c>, skip one weapon choice, which a flying electro-optical missile sets on the machine that fired it.</summary>
	bool SkipWeaponChoice { get; set; }

	/// <summary><c>+0x15e</c>, its own target, or null.</summary>
	IEs1SimObject? Target { get; }

	/// <summary><c>Context_GetTransform</c> of <c>+6</c>: the rotation from its angles, at its position.</summary>
	Transform3 Frame { get; }

	/// <summary>Slot <c>+0x24</c>, the hit test: the distance along <paramref name="shot"/>'s ray at which it is struck, 0 for a miss.</summary>
	int HitTest(Es1Shot shot, Es1SimGlobals globals);

	/// <summary>Slot <c>+0x44</c>, the damage, Q8 from 0, intact, to <c>0x100</c>.</summary>
	short OverallDamage { get; }

	/// <summary>
	/// Slot <c>+0x28</c>, the aim node: the node transform, in the object's own frame, whose translation a missile with no
	/// component steers at; null for none, which steers it at the position.
	/// </summary>
	Transform3? AimNodeTransform();

	/// <summary>Slot <c>+0x58</c>: the component a missile fired with <paramref name="angles"/> from <paramref name="from"/> steers at, or −1.</summary>
	short ComponentNearestAim(Vec3i from, (short Pitch, short Roll, short Heading) angles);

	/// <summary>Slot <c>+0x5c</c>: where <paramref name="component"/> stands in the world.</summary>
	Vec3i ComponentPosition(short component);

	/// <summary>Slot <c>+0x70</c>: whether a missile of <paramref name="subtype"/> it fires has lock, so takes its target.</summary>
	bool MissileLockState(short subtype);
}
