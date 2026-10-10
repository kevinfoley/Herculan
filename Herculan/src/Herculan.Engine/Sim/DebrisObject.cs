using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// One thrown piece of wreckage — DBSIM's <c>debris.cpp</c> class, built by <c>Debris_Construct</c> and
/// advanced by <c>Debris_TickUpdate</c> (<c>Debris_TickUpdate</c>), allocated from the 150-entry pool at
/// <c>g_DebrisPool</c>.
///
/// <para>A piece is a root of a shape file thrown on a ballistic arc: it spins about
/// its own X axis, falls under a flat gravity, loses horizontal speed to drag, and bounces off the
/// terrain until it is moving slowly enough to settle. Nothing steers it and nothing can shoot it —
/// like a <see cref="BeamTracer"/> or an <see cref="ImpactEffect"/> it comes out of the effect pool
/// <c>Sim_MainTick</c> walks ahead of the machine list, not out of the object list.</para>
///
/// <para><b>A piece can burst.</b> One that names a <see cref="ChildGroup"/> carries a random
/// countdown of its own; when that runs out, or when it comes to rest, it sets off its
/// <see cref="DestroyEffect"/> and throws that group in turn — out of the database it was itself
/// thrown from, which is why it carries one. The throw window is tightened for the second
/// generation (<see cref="ChildThrowPitchMin"/>) so a burst scatters closer than the throw that made
/// it. A piece with no child group never bursts and lives until it settles.</para>
///
/// <para>Two things spawn one: a <see cref="DebrisGroup"/> through
/// <see cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)"/>, and a weapon mount shot off its hardpoint, which throws its
/// own gun model rather than anything out of a table — see <see cref="WeaponMount.Destroy"/>.</para>
/// </summary>
public sealed class DebrisObject {
	private readonly int _shapeRadius;
	private short _timer;

	/// <param name="shapeLibrary">
	/// Which shape file this piece is a root of — a debris database's own <c>.DTS</c>, or
	/// <see cref="WeaponMount.DebrisShapeLibraryName"/> for a gun knocked off its mount. Held as a
	/// name because a renderer keys its meshes on one, and because the two spawn paths do not share a
	/// shape file.
	/// </param>
	/// <param name="shapeIndex">Which root of it.</param>
	/// <param name="shapeRadius">
	/// That root's bounding radius, which is what the piece clears the ground by.
	/// </param>
	/// <param name="childGroup">
	/// The debris group this piece bursts into where it ends, or <c>-1</c> for one that simply comes
	/// to rest.
	/// </param>
	/// <param name="destroyEffect">The <c>EXPLOS.DAT</c> effect that goes off there, or <c>-1</c>.</param>
	/// <param name="childTable">
	/// The database <paramref name="childGroup"/> is resolved against — the one this piece was itself
	/// thrown out of. The original captures the installed alternate into the object at construction
	/// (<c>obj+0x5d</c>) and re-installs it from there when the piece bursts.
	/// </param>
	/// <param name="position">Where it starts, in world units.</param>
	/// <param name="euler">Its starting orientation.</param>
	/// <param name="velocity">Its launch velocity — see <see cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)"/>.</param>
	/// <param name="spinRate">How fast it tumbles, in BAM per second.</param>
	/// <param name="lifetime">
	/// Ticks of countdown before it bursts. Drawn for every piece, read only by one that has a child
	/// group.
	/// </param>
	/// <param name="hercDetailBias">Whether it is drawn under HERC DETAIL's bias — see <see cref="HercDetailBias"/>.</param>
	internal DebrisObject(string shapeLibrary, int shapeIndex, int shapeRadius, short childGroup,
			short destroyEffect, DebrisDatabase? childTable, Vec3i position,
			(short X, short Y, short Z) euler, (short X, short Y, short Z) velocity,
			short spinRate, short lifetime, bool hercDetailBias = false) {
		ShapeLibrary = shapeLibrary;
		HercDetailBias = hercDetailBias;
		ShapeIndex = shapeIndex;
		_shapeRadius = shapeRadius;
		ChildGroup = childGroup;
		DestroyEffect = destroyEffect;
		ChildTable = childTable;
		Position = position;
		Euler = euler;
		Velocity = velocity;
		SpinRate = spinRate;
		_timer = lifetime;
	}

	/// <summary>Which shape file this piece is a root of.</summary>
	public string ShapeLibrary { get; }

	/// <summary>And which root of it.</summary>
	public int ShapeIndex { get; }

	/// <summary>
	/// <c>obj+0x50</c>, the <c>TSDetailPart</c> bias <c>Debris_Draw</c> (<c>00408e6c</c>) pushes around
	/// the piece's render: 0 from <c>Debris_Construct</c>, which is every table-thrown piece, and
	/// HERC DETAIL's for the gun <see cref="WeaponMount.Destroy"/> throws, which is drawn under the
	/// same bias it was drawn under on its mount.
	///
	/// <para>A flag here rather than the number: the original copies the bias in at the throw, and
	/// this engine resolves it from the setting when the piece is drawn, so a HERC DETAIL change while
	/// a gun is in the air re-biases it here and not in the original.</para>
	/// </summary>
	public bool HercDetailBias { get; }

	/// <summary>The group it bursts into where it ends, or <c>-1</c>.</summary>
	public short ChildGroup { get; }

	/// <summary>The effect that goes off there, or <c>-1</c>.</summary>
	public short DestroyEffect { get; }

	/// <summary>The database <see cref="ChildGroup"/> is read against.</summary>
	public DebrisDatabase? ChildTable { get; }

	/// <summary>Where it is, in world units.</summary>
	public Vec3i Position { get; private set; }

	/// <summary>How it is turned. Only X moves: the tumble is about that axis alone.</summary>
	public (short X, short Y, short Z) Euler { get; private set; }

	/// <summary>Its shape-to-world frame, which is what a renderer draws it with.</summary>
	public Transform3 WorldTransform {
		get {
			var frame = Transform3.FromEuler(Euler.X, Euler.Y, Euler.Z);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>Its velocity, in world units per tick — <c>obj+0x4a</c>.</summary>
	public (short X, short Y, short Z) Velocity { get; private set; }

	/// <summary>
	/// How fast it tumbles, <c>obj+0x5b</c>. Always negative: the constructor draws
	/// <c>Q10Multiply(-1700, rand &amp; 0x3ff) - 800</c>, so every piece rotates the same way at
	/// between 800 and 2500 BAM a second.
	/// </summary>
	public short SpinRate { get; }

	/// <summary>
	/// <c>Debris_TickUpdate</c> (<c>Debris_TickUpdate</c>), vtable <c>+0x14</c>. Returns whether the piece
	/// is finished and should be freed.
	///
	/// <list type="number">
	/// <item><b>Spin and gravity</b> — the tumble is integrated into X, and
	/// <see cref="Gravity"/> into the vertical speed.</item>
	/// <item><b>Drag</b>, on the two horizontal axes only, at <see cref="HorizontalDrag"/> of the
	/// current speed per second. Nothing slows the fall.</item>
	/// <item><b>The move</b>, by the <i>average</i> of the speed before and after this tick's
	/// changes rather than by either — the original's own trapezoid, which is why a piece's first
	/// step is half a tick of gravity rather than a whole one.</item>
	/// <item><b>The ground</b>. A piece below the terrain plus its shape's clearance is snapped up to
	/// it and bounces at <see cref="Restitution"/>, its countdown cleared. Below
	/// <see cref="SettleSpeed"/> of rebound it has stopped moving and is done.</item>
	/// <item><b>The burst</b>, for a piece with a child group: on landing, or when its countdown runs
	/// out, it sets off its effect and throws that group.</item>
	/// </list>
	///
	/// <para>The gravity is <see cref="Gravity"/> everywhere except theater 4, the Moon, where it is
	/// <see cref="MoonGravity"/> and debris hangs noticeably longer.</para>
	/// </summary>
	internal bool Tick(SimWorld world) {
		var position = Position;
		var euler = Euler;
		var velocity = Velocity;

		euler.X += (short)SimMath.IntegrateRateOverTick(SpinRate);

		var before = velocity;
		velocity.Z += (short)SimMath.IntegrateRateOverTick(world.OnMoon ? MoonGravity : Gravity);
		velocity.X -= (short)SimMath.IntegrateRateOverTick((short)SimMath.Q10Multiply(HorizontalDrag, velocity.X));
		velocity.Y -= (short)SimMath.IntegrateRateOverTick((short)SimMath.Q10Multiply(HorizontalDrag, velocity.Y));

		// The average is taken in 16 bits and shifted there (SAR word), so a pair whose sum overflows
		// a short wraps rather than widening. No throw in the game comes close, but it is the
		// original's arithmetic and costs nothing to keep.
		position = new Vec3i(
			position.X + ((short)(before.X + velocity.X) >> 1),
			position.Y + ((short)(before.Y + velocity.Y) >> 1),
			position.Z + ((short)(before.Z + velocity.Z) >> 1));

		int floor = world.GroundHeightAt(position)
			+ SimMath.Q10Multiply(GroundClearanceScale, _shapeRadius);

		bool settled = false;
		if (position.Z < floor) {
			position = new Vec3i(position.X, position.Y, floor);
			velocity.Z = (short)-SimMath.Q10Multiply(Restitution, velocity.Z);
			_timer = 0;
			settled = velocity.Z < SettleSpeed;
		}

		Position = position;
		Euler = euler;
		Velocity = velocity;

		if (ChildGroup < 0) {
			// Nothing to burst into, so the only way out is coming to rest.
			return settled;
		}

		// The countdown is only consulted while the piece is still flying. Ground contact zeroes it
		// before this, so a piece that touches down bursts on that tick whether or not it settled.
		if (!settled && SimMath.CountdownTimerTick(ref _timer) != 0) {
			return false;
		}

		if (DestroyEffect >= 0) {
			world.Effects.SpawnImpactEffect(DestroyEffect, Position, owner: null);
		}

		// The burst re-installs the database this piece was thrown out of, so its child group is read
		// against the same table its parent was, however many spawn sites have run in between. At
		// EFFECTS DETAIL's lowest setting there is no second generation: the effect goes off and the
		// piece is simply gone.
		if (world.EffectsDetail != 0) {
			world.Effects.SpawnDebris(ChildGroup, Position, ChildTable,
				ChildThrowPitchMin, ChildThrowPitchMax, ChildThrowSpeedScale);
		}

		return true;
	}

	/// <summary>
	/// The vertical acceleration, in world units per second per second — <c>-0x20</c>, the tick's own
	/// literal. Small against the game's 166 units to the metre; debris arcs are slow and floaty in
	/// the original too.
	/// </summary>
	public const short Gravity = -0x20;

	/// <summary>
	/// What the original substitutes when <c>ScriptDatHeader</c> — the theater index — is 4, the
	/// Moon: <c>-10</c>, a third of the weight.
	/// </summary>
	public const short MoonGravity = -10;

	/// <summary>
	/// The horizontal drag coefficient, Q10 — <c>30/1024</c> of the current speed shed per second, on
	/// X and Y only.
	/// </summary>
	public const int HorizontalDrag = 0x1e;

	/// <summary>
	/// How much of its vertical speed a piece keeps when it hits the ground, Q10 — <c>450/1024</c>,
	/// so a little under half, and each bounce is under half the height of the last.
	/// </summary>
	public const int Restitution = 0x1c2;

	/// <summary>
	/// The rebound speed below which a piece has stopped for good. A bounce that comes back slower
	/// than this ends the piece rather than laying it on the ground: nothing draws settled wreckage.
	/// </summary>
	public const short SettleSpeed = 0x2d;

	/// <summary>
	/// What the shape's own bounding radius is scaled by to keep a piece off the ground, Q10 — about
	/// half, so a piece rests with its centre roughly its own half-radius above the terrain.
	/// </summary>
	public const int GroundClearanceScale = 500;

	/// <summary>
	/// How many pieces can be in the air at once — the pool's own size. The original's allocator
	/// returns nothing when it is full and the spawn is silently skipped, which is what
	/// <see cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)"/> does here.
	/// </summary>
	public const int PoolSize = 150;

	/// <inheritdoc cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const short ChildThrowPitchMin = 3000;

	/// <inheritdoc cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const short ChildThrowPitchMax = 8000;

	/// <inheritdoc cref="EffectPools.SpawnDebris(short, in Transform3, DebrisDatabase?, short, short, int)" />
	public const int ChildThrowSpeedScale = 0x140;
}
