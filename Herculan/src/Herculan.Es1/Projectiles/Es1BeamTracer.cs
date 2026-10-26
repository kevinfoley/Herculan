using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// A beam's tracer, the beams' file's class (docs/es1/simulation/projectiles.md#beams): <c>BeamTracerVtable</c>
/// (<c>ES1DBSIM 00090d1e</c>), built by <c>BeamTracer_Ctor</c> (<c>00008268</c>) after <c>Bullet_FireBurst</c>
/// (<c>000088cc</c>) has swept the shot; it holds the quads the frame draws and ends at the first tick after it.
/// </summary>
public sealed class Es1BeamTracer : Es1Projectile {
	/// <summary>The projectile record type a beam's shot looks up.</summary>
	public const short RecordType = 4;

	/// <summary>The kind word <c>+0x12</c> of a beam's shot.</summary>
	public const short ShotKind = 5;

	/// <summary><c>Bullet_FireBurst</c>'s ray clearance.</summary>
	public const short Slack = 200;

	/// <summary>The jagged subtype, the <c>ELF</c>.</summary>
	public const short JaggedSubtype = 1;

	/// <summary>A jagged chain's node spacing, the step <c>Math_NormalizeVec3ToLength</c> rescales to: <c>0x400</c>.</summary>
	public const short NodeStep = 0x400;

	/// <summary>The mask of <c>Math_RandomNext</c> each axis of a node after the first is raised by.</summary>
	public const int NodeJitter = 0x7f;

	/// <summary><c>Beam_HalfWidths</c> (<c>00090cd8</c>), by subtype.</summary>
	public static readonly short[] HalfWidths = [60, 30, 120, 20, 25, 30];

	/// <summary><c>Beam_Colors</c> (<c>00090ce4</c>), the palette index each subtype fills in.</summary>
	public static readonly short[] Colors = [10, 102, 10, 88, 88, 88];

	/// <summary><c>Beam_QuadIndexOffsets</c> (<c>00090cf0</c>).</summary>
	private static readonly short[] QuadIndexOffsets = [1, 0, 1, 2];

	/// <summary>
	/// <c>Beam_QuadIndexTable</c> (<c>000bc202</c>), which <c>Beam_BuildIndexTable</c> (<c>00008230</c>), the file's
	/// phase-2 loader, fills: entry <i>i</i> is (<i>i</i> &gt;&gt; 1) + <c>Beam_QuadIndexOffsets</c>[<i>i</i> &amp; 3],
	/// over 80 entries.
	/// </summary>
	public static readonly short[] QuadIndexTable = BuildIndexTable();

	private short _lifeCountdown;

	private Es1BeamTracer(byte stale) : base(null, stale, null) {
	}

	/// <summary><c>+0x4c</c>, the subtype, which picks the half-width and the color.</summary>
	public short BeamSubtype { get; private set; }

	/// <summary><c>+0x4e</c>, the quad count.</summary>
	public sbyte QuadCount { get; private set; }

	/// <summary><c>+0x4f</c>, the jagged flag: set for the <c>ELF</c>'s chain.</summary>
	public bool Jagged { get; private set; }

	/// <summary><c>+0x50</c> and <c>+0x52</c>, the points, which a quad's four corners index through <see cref="QuadIndexTable"/>.</summary>
	public Vec3i[] Points { get; private set; } = [];

	/// <summary>
	/// The four points of quad <paramref name="quad"/>, in the table's order from the offset <paramref name="quad"/> &lt;&lt;
	/// (3 − the jagged flag): quad <i>k</i> of a chain is points 2<i>k</i> + 1, 2<i>k</i>, 2<i>k</i> + 2, 2<i>k</i> + 3, and
	/// the two quads of a straight beam points 1, 0, 2, 3 and 5, 4, 6, 7.
	/// </summary>
	public (Vec3i A, Vec3i B, Vec3i C, Vec3i D) Quad(int quad) {
		int offset = quad << (Jagged ? 2 : 3);
		return (Points[QuadIndexTable[offset]], Points[QuadIndexTable[offset + 1]], Points[QuadIndexTable[offset + 2]],
			Points[QuadIndexTable[offset + 3]]);
	}

	/// <summary><see cref="Colors"/> of the subtype; 0 past the table, where retail reads past it.</summary>
	public short Color => BeamSubtype >= 0 && BeamSubtype < Colors.Length ? Colors[BeamSubtype] : (short)0;

	private static short[] BuildIndexTable() {
		var table = new short[80];
		for (int i = 0; i < table.Length; i++) {
			table[i] = (short)((i >> 1) + QuadIndexOffsets[i & 3]);
		}

		return table;
	}

	/// <summary>
	/// <c>Bullet_FireBurst</c>(<paramref name="power"/>, <paramref name="machine"/>, <paramref name="range"/>,
	/// <paramref name="shotTransform"/>, <paramref name="subtype"/>) (docs/es1/simulation/projectiles.md#firing--bullet_fireburst-000088cc):
	/// the ray along <paramref name="shotTransform"/>'s Y for <paramref name="range"/> with <see cref="Slack"/>, the shot
	/// of <c>Proj_LookupRecord</c>(<paramref name="subtype"/>, 4)'s armor and shield each <c>Math_Q10Multiply</c>'d by the
	/// power, its splash factor and effects, with <see cref="ShotKind"/>; the sweep, whose answer, or the range for none,
	/// is how far the beam goes; and one tracer from the transform's translation to there. Answers the tracer, or null
	/// with the pool full.
	/// </summary>
	public static Es1BeamTracer? FireBurst(Es1SimGlobals globals, short power, IEs1SimObject machine, int range,
			Transform3 shotTransform, short subtype) {
		var record = globals.Projectiles.LookupRecord(subtype, RecordType);
		var shot = new Es1Shot(new Es1Ray(shotTransform, range, Slack), machine) {
			Armor = (short)SimMath.Q10MultiplyRounded(record?.DamageArmor ?? 0, power),
			Shield = (short)SimMath.Q10MultiplyRounded(record?.DamageShield ?? 0, power),
			SplashFactor = record?.SplashFactor ?? 0,
			Effects = record,
			Kind = ShotKind,
		};

		int travelled = Es1ShotSweep.Raycast(shot, globals);
		if (travelled == 0) {
			travelled = range;
		}

		var farEnd = shotTransform.TransformPoint(0, travelled, 0);
		var muzzle = new Vec3i(shotTransform.X, shotTransform.Y, shotTransform.Z);
		var tracer = Construct(globals, machine, farEnd, muzzle, subtype);
		// PLACEHOLDER: Sound_PlayAt (0004deb8)(muzzle, 0xb); no plan step ports ES1's sound yet.
		return tracer;
	}

	/// <summary>
	/// <c>BeamTracer_Ctor</c>(0, <paramref name="owner"/>, <paramref name="farEnd"/>, <paramref name="muzzle"/>,
	/// <paramref name="subtype"/>) (docs/es1/simulation/projectiles.md#the-tracer--beamtracer_ctor-00008268): an element of
	/// the pool, none when it is full, with no shape; the subtype, the owner and the position at the muzzle, and the life
	/// countdown at 0. The <c>ELF</c>'s chain: (<c>Math_DistanceBetweenPoints</c> &gt;&gt; 10) + 1 quads, a node a
	/// <see cref="NodeStep"/> on from the muzzle each, the last the far end, every node after the first raised by
	/// <c>Math_RandomNext</c> &amp; <see cref="NodeJitter"/> on X, Y and Z in turn, written to points 2<i>k</i> and 2<i>k</i>
	/// + 1 with the half-width added to point 2<i>k</i>'s Z. Every other subtype: two quads over eight points, points 0, 1, 4
	/// and 5 the muzzle and 2, 3, 6 and 7 the far end, with the half-width added to and taken from the Z of 0 and 1, of 2
	/// and 3, and of 6 and 7, and the X of 4 and 5.
	/// </summary>
	/// <remarks>
	/// The subtype byte <c>+0x3b</c> takes the element's word <c>+0x4c</c> as it stood before the constructor, which is
	/// whatever the element last held (docs/es1/simulation/projectiles.md#open); here it is 0.
	/// </remarks>
	private static Es1BeamTracer? Construct(Es1SimGlobals globals, IEs1SimObject owner, Vec3i farEnd, Vec3i muzzle,
			short subtype) {
		var pool = globals.Projectiles;
		if (!pool.CanAllocate) {
			return null;
		}

		var tracer = new Es1BeamTracer(0) {
			BeamSubtype = subtype,
			Owner = owner,
		};

		short halfWidth = subtype >= 0 && subtype < HalfWidths.Length ? HalfWidths[subtype] : (short)0;
		if (subtype == JaggedSubtype) {
			// The node count is a byte, the points' count a word.
			sbyte nodes = (sbyte)((farEnd.ApproxDistanceTo(muzzle) >> 10) + 1);
			tracer.QuadCount = nodes;
			tracer.Jagged = true;
			tracer.Points = new Vec3i[Math.Max(0, (int)(short)(nodes * 2 + 2))];
			var (stepX, stepY, stepZ) = Es1GridShape.RescaleToLength(NodeStep,
				farEnd.X - muzzle.X, farEnd.Y - muzzle.Y, farEnd.Z - muzzle.Z);
			var running = muzzle;
			for (int k = 0; k < nodes + 1; k++) {
				var node = k == nodes ? farEnd : running;
				if (k != 0) {
					int x = node.X + (globals.Random.Next() & NodeJitter);
					int y = node.Y + (globals.Random.Next() & NodeJitter);
					int z = node.Z + (globals.Random.Next() & NodeJitter);
					node = new Vec3i(x, y, z);
				}

				tracer.Points[2 * k + 1] = node;
				tracer.Points[2 * k] = new Vec3i(node.X, node.Y, node.Z + halfWidth);
				running = new Vec3i(running.X + stepX, running.Y + stepY, running.Z + stepZ);
			}
		} else {
			tracer.QuadCount = 2;
			tracer.Points = [
				new(muzzle.X, muzzle.Y, muzzle.Z + halfWidth), new(muzzle.X, muzzle.Y, muzzle.Z - halfWidth),
				new(farEnd.X, farEnd.Y, farEnd.Z + halfWidth), new(farEnd.X, farEnd.Y, farEnd.Z - halfWidth),
				new(muzzle.X + halfWidth, muzzle.Y, muzzle.Z), new(muzzle.X - halfWidth, muzzle.Y, muzzle.Z),
				new(farEnd.X, farEnd.Y, farEnd.Z + halfWidth), new(farEnd.X, farEnd.Y, farEnd.Z - halfWidth),
			];
		}

		tracer._lifeCountdown = 0;
		tracer.Position = muzzle;
		pool.Add(tracer);
		return tracer;
	}

	/// <summary>
	/// <c>BeamTracer_LifeTick</c> (<c>000089a8</c>), slot <c>+0x18</c>: 1 when <c>Math_CountdownTimerTick</c> of the life
	/// countdown reads 0, which the constructor's 0 makes the first tick.
	/// </summary>
	public override short Tick(Es1ProjectileTick tick) =>
		tick.Globals.Clock.CountdownTimerTick(ref _lifeCountdown) == 0 ? (short)1 : (short)0;

	/// <summary><c>BeamTracer_Dtor</c> (<c>00008768</c>), slot <c>+0xc</c>: the point array freed, then the class destructor.</summary>
	public override void Destroy() => Points = [];
}
