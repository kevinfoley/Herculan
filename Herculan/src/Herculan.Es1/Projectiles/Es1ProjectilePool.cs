using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct.Dbsim;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// What a projectile's tick reads beyond itself: the simulation's globals, <c>PlayerInputBlock</c> as the tick's input
/// build left it, and the controls' state, whose latches and <c>maybe_WeaponChain_EoHold</c> a missile writes.
/// </summary>
/// <param name="Block">The tick's input block; null outside a tick, where retail's global still holds the last one.</param>
public sealed record Es1ProjectileTick(Es1SimGlobals Globals, Es1PlayerInputBlock? Block, Es1ControlsState? Controls);

/// <summary>
/// The projectile pool's file (docs/es1/simulation/projectiles.md#the-projectile-pool): <c>g_ProjectilePool</c>
/// (<c>ES1DBSIM 000af0b4</c>), <see cref="Capacity"/> elements, with its delete queue <c>g_ProjectileDeleteQueue</c>
/// (<c>000af0b8</c>), and the type tables the rounds' files load at phase 2: <c>dat\rockets</c> and <c>dts\rockets</c>
/// (<c>Rocket_LoadResources</c>, <c>000073d0</c>), <c>dat\bullets</c> and <c>dts\bullets</c> (<c>Bullet_LoadResources</c>,
/// <c>00007bb0</c>), and the projectile records <c>Weapons_LoadResourceTables</c> reads from <c>dat\weapons</c>.
/// </summary>
public sealed class Es1ProjectilePool {
	/// <summary><c>Projectile_LoadResources</c>' <c>Pool_Init</c>(0, <c>0x60</c>, <c>0xa0</c>): 160 elements.</summary>
	public const int Capacity = 160;

	private readonly List<Es1Projectile> _live = new();
	private readonly List<Es1Projectile> _deleteQueue = new();

	/// <summary>A pool with no type tables, as before the phase-2 loads: every lookup misses and no shape is built.</summary>
	public Es1ProjectilePool() {
	}

	/// <summary>The live projectiles in allocation order, oldest first, the order a <c>Pool_Prev</c> walk visits them.</summary>
	public IReadOnlyList<Es1Projectile> Live => _live;

	/// <summary>
	/// <c>MfdMissileView_TrackedRound</c> (<c>00093914</c>): the last missile the player's machine fired, which
	/// <see cref="Es1Rocket.Fire"/> stores and the missile camera clears once the pool no longer holds it
	/// (docs/es1/simulation/mfd-missile-camera.md#painting).
	/// </summary>
	public Es1Projectile? TrackedMissile { get; set; }

	/// <summary><c>ProjDat_RecordTable</c> (<c>000bc2ce</c>), the 19 projectile records of <c>dat\weapons</c>.</summary>
	public IReadOnlyList<ProjectileData.Projectile> Records { get; private init; } = [];

	/// <summary><c>g_RocketTypeTable</c> (<c>000bc1de</c>), <c>dat\rockets</c>' records by subtype.</summary>
	public IReadOnlyList<RocketType> RocketTypes { get; private init; } = [];

	/// <summary><c>g_RocketShapes</c> (<c>000bc1e4</c>), the roots of <c>dts\rockets</c>.</summary>
	public IReadOnlyList<TSObject> RocketShapes { get; private init; } = [];

	/// <summary><c>g_BulletTypeTable</c> (<c>000bc1f6</c>), <c>dat\bullets</c>' records by subtype.</summary>
	public IReadOnlyList<Es1BulletType> BulletTypes { get; private init; } = [];

	/// <summary><c>000bc1fc</c>, the roots of <c>dts\bullets</c>.</summary>
	public IReadOnlyList<TSObject> BulletShapes { get; private init; } = [];

	/// <summary>
	/// The phase-2 loads: the projectile records of <paramref name="weapons"/>, and <c>dat\rockets</c>, <c>dts\rockets</c>,
	/// <c>dat\bullets</c> and <c>dts\bullets</c> through <c>HercWorks.Core</c>. A missing file, where retail asserts, leaves
	/// its table empty.
	/// </summary>
	public static Es1ProjectilePool Load(GameContent content, Es1Weapons weapons) => new() {
		Records = weapons.Projectiles,
		RocketTypes = content.Read("dat", "ROCKETS.DAT") is { } rockets
			? new RocketDataTransformer().Parse(rockets)?.Entries ?? []
			: [],
		RocketShapes = Roots(content, "ROCKETS.DTS"),
		BulletTypes = content.Read("dat", "BULLETS.DAT") is { } bullets
			? new Es1BulletDataTransformer().Parse(bullets)?.Entries ?? []
			: [],
		BulletShapes = Roots(content, "BULLETS.DTS"),
	};

	private static IReadOnlyList<TSObject> Roots(GameContent content, string name) =>
		content.Read("dts", name) is { } bytes && new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }
			? roots
			: [];

	/// <summary>
	/// <c>Proj_LookupRecord</c> (<c>0000c2d0</c>)(<paramref name="subtype"/>, <paramref name="type"/>): the first of the
	/// records whose type is <paramref name="type"/> and subtype id <paramref name="subtype"/>, or null
	/// (docs/es1/formats/weapons-dat-sim.md#the-projectile-lookups).
	/// </summary>
	public static ProjectileData.Projectile? LookupRecord(IReadOnlyList<ProjectileData.Projectile> records, short subtype,
			short type) =>
		records.FirstOrDefault(record => record.Type?.Val == type && record.SubtypeId == subtype);

	/// <inheritdoc cref="LookupRecord(IReadOnlyList{ProjectileData.Projectile}, short, short)"/>
	public ProjectileData.Projectile? LookupRecord(short subtype, short type) => LookupRecord(Records, subtype, type);

	/// <summary><c>Pool_Alloc</c>: whether an element is free; the queued ones stay taken until <see cref="FlushDeletes"/>.</summary>
	internal bool CanAllocate => _live.Count + _deleteQueue.Count < Capacity;

	/// <summary>A constructed projectile taken into the pool, after <see cref="CanAllocate"/>.</summary>
	internal void Add(Es1Projectile projectile) => _live.Add(projectile);

	/// <summary>Whether <paramref name="projectile"/> is live, <c>Pool_Prev</c>'s walk for it.</summary>
	public bool Contains(Es1Projectile? projectile) => projectile != null && _live.Contains(projectile);

	/// <summary>
	/// <c>Sim_MainTick</c>'s walk (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700): each projectile
	/// through its tick, slot <c>+0x18</c>, oldest first, as <c>Pool_Prev</c> walks from the end of the in-use list, where
	/// <c>Pool_Alloc</c> puts the newest at its head; one fired during the walk is reached in it. One that answers non-zero
	/// is destroyed through slot <c>+0xc</c> with 3, which queues it.
	/// </summary>
	public void Tick(Es1ProjectileTick tick) {
		for (int i = 0; i < _live.Count;) {
			var projectile = _live[i];
			if (projectile.Tick(tick) != 0) {
				projectile.Destroy();
				_live.RemoveAt(i);
				_deleteQueue.Add(projectile);
			} else {
				i++;
			}
		}
	}

	/// <summary>
	/// <c>Projectile_FlushDeletes</c> (<c>00006db0</c>), phase 5, which <c>Sim_RenderFrame</c> runs first:
	/// <c>ObjectPool_FlushDeleteQueue</c>, each queued projectile given back to the pool.
	/// </summary>
	public void FlushDeletes() => _deleteQueue.Clear();
}
