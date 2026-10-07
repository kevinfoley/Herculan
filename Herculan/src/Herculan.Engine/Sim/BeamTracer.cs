using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The visible half of a beam shot — the tracer object <c>Bullet_FireBurst</c> (<c>0040bf74</c>)
/// spawns once the hit has already been resolved, constructed by <c>BeamTracer_Ctor</c>
/// (<c>0040b804</c>) and drawn by its class's vtable slot 0, <c>BeamTracer_Draw</c>
/// (<c>0040bc14</c>).
///
/// <para>It is not a <see cref="SimObject"/> in the original either: tracers live in their own pool
/// (<c>g_ProjectilePool</c> (<c>004a9746</c>)), which <c>Sim_MainTick</c> walks <b>before</b> the machine list — ticking
/// each one through vtable <c>+0x14</c> and freeing it the moment its countdown reaches zero — and
/// which the frame submit walks separately. Nothing raycasts against them and they carry no damage;
/// the shot they came from was resolved and finished before the first one was allocated.</para>
///
/// <para><b>One tracer per shot, holding its spans.</b> The original splits a straight run into
/// 5000-unit spans and allocates a tracer for each, and each is filed by terrain cell for drawing on
/// its own, so a span can be culled while its neighbours are drawn. This engine keeps the one object
/// and the span boundaries on it (<see cref="SpanCount"/>, <see cref="Span"/>); the renderer files and
/// draws each span as its own quad.</para>
/// </summary>
public sealed class BeamTracer {
	/// <summary>
	/// The countdown the constructor arms, at the object's <c>+0x5d</c> (<c>Math_CountdownTimerTick</c>
	/// takes a pointer one byte below the short it decrements, which is why the two offsets in the
	/// disassembly appear to disagree).
	///
	/// <para>It is in the same Q8-of-125 ms unit as every other timer in the simulation, so 56 is
	/// 27 ms — less than one tick's worth of <see cref="SimWorld.TickDelta"/>. A tracer is therefore
	/// alive for exactly one tick however fast the machine runs, which is what makes a held trigger
	/// read as a train of separate flashes rather than a continuous beam.</para>
	/// </summary>
	public const short InitialLife = 0x38;

	/// <summary>
	/// The two <c>PROJ.DAT</c> subtype ids <c>BeamTracer_Ctor</c> tests for before it builds anything —
	/// ELF and ELF2, the only two weapons that get the jagged chain instead of a straight segment.
	/// </summary>
	public const short ElfSubtype = 1;

	/// <inheritdoc cref="ElfSubtype"/>
	public const short Elf2Subtype = 7;

	/// <summary>How far apart the chain's nodes sit: the length the start-to-end delta is rescaled to.</summary>
	public const short NodeSpacing = 0x400;

	/// <summary>
	/// <c>Math_RandomNext() &amp; 0x7f</c>, added to every axis of every node but the first — 0 to
	/// 127, never negative. What that one-sidedness does to the chain's shape is in
	/// docs/retail/simulation/beam-visuals.md, "ELF and ELF2".
	/// </summary>
	public const int JitterMask = 0x7f;

	/// <summary>
	/// How many quads the original's vertex-index list can address; a longer chain would read past
	/// it. No retail weapon reaches it — see docs/retail/simulation/beam-visuals.md, "The paint uses the
	/// polygon renderers' project, clip and fill chain", for the table this comes from.
	/// </summary>
	public const int MaxQuads = 30;

	private readonly Vec3i[] _points;

	/// <param name="start">The muzzle point.</param>
	/// <param name="end">Where the beam stopped.</param>
	/// <param name="subtypeId">The <c>PROJ.DAT</c> subtype id, which selects the shape and the appearance.</param>
	/// <param name="halfWidth">
	/// The <c>BEAM.DAT</c> half-width for that subtype, which the jagged branch reads at construction
	/// time — it is baked into the geometry as the z offset between each node's two points, not
	/// applied at draw time as it is for a straight beam.
	/// </param>
	/// <param name="random">The simulation generator, which the node jitter draws from.</param>
	/// <param name="spanStep">The shot frame's <c>(0, <see cref="SpanLength"/>, 0)</c> through its rotation alone.</param>
	/// <param name="travelled">How far the beam reaches, which decides how many spans it is cut into.</param>
	internal BeamTracer(Vec3i start, Vec3i end, short subtypeId, int halfWidth, SimRandom random,
			Vec3i spanStep = default, int travelled = 0) {
		Start = start;
		End = end;
		SubtypeId = subtypeId;
		Life = InitialLife;
		_points = IsJagged ? BuildChain(start, end, halfWidth, random) : Array.Empty<Vec3i>();
		SpanStep = spanStep;

		// Bullet_FireBurst's loop allocates a span and takes 5000 off the distance while more than 5000
		// is left, then one span for the rest; the chain is one object whatever its length.
		SpanCount = IsJagged ? 1 : System.Math.Max(travelled - 1, 0) / SpanLength + 1;
	}

	/// <summary>
	/// The length <c>Bullet_FireBurst</c> (<c>0040bf74</c>) cuts a straight beam into, one tracer object
	/// per span.
	/// </summary>
	public const int SpanLength = 5000;

	/// <summary>
	/// One span's length along the shot: the shot frame's <c>(0, <see cref="SpanLength"/>, 0)</c> through
	/// <c>Transform_RotatePoint</c> (<c>004801f8</c>), which the loop adds to its start point per span.
	/// </summary>
	public Vec3i SpanStep { get; }

	/// <summary>How many tracer objects the original allocates for this shot. One for a chain.</summary>
	public int SpanCount { get; }

	/// <summary>
	/// Span <paramref name="index"/>'s two ends: from <see cref="Start"/> plus that many
	/// <see cref="SpanStep"/>s to one more, and the last to <see cref="End"/>. The start is the
	/// position the span's tracer is filed by.
	/// </summary>
	public (Vec3i From, Vec3i To) Span(int index) {
		var from = new Vec3i(Start.X + index * SpanStep.X, Start.Y + index * SpanStep.Y,
			Start.Z + index * SpanStep.Z);
		var to = index + 1 < SpanCount
			? new Vec3i(from.X + SpanStep.X, from.Y + SpanStep.Y, from.Z + SpanStep.Z)
			: End;
		return (from, to);
	}

	/// <summary>The muzzle point — <c>BeamTracer_Ctor</c>'s <c>param_3</c>, the first of the two points it stores.</summary>
	public Vec3i Start { get; }

	/// <summary>
	/// Where the beam stopped: the shot's own frame at <c>(0, travelled, 0)</c>, where travelled is
	/// the raycast's reported distance or the weapon's full range when it struck nothing.
	/// </summary>
	public Vec3i End { get; }

	/// <summary>
	/// The <c>PROJ.DAT</c> record's subtype id, which is what indexes <c>BEAM.DAT</c> — not the
	/// weapon id. It is <c>Bullet_FireBurst</c>'s own first parameter, passed straight through to the
	/// tracer and read back by the draw at <c>+0x52</c>.
	/// </summary>
	public short SubtypeId { get; }

	/// <summary>Ticks remaining, in the timer unit <see cref="InitialLife"/> documents.</summary>
	public short Life { get; private set; }

	/// <summary>
	/// Whether this shot took <c>BeamTracer_Ctor</c>'s second branch — the one ELF and ELF2 alone take,
	/// which stores a chain of jittered nodes instead of the muzzle and the hit.
	/// </summary>
	public bool IsJagged => SubtypeId is ElfSubtype or Elf2Subtype;

	/// <summary>
	/// The chain's point list, empty for a straight beam. Node <c>k</c> owns the pair
	/// <c>[2k]</c> (offset up in z by the <c>BEAM.DAT</c> half-width) and <c>[2k+1]</c> (on the node
	/// itself), so the ribbon stands <b>vertically in the world</b> and does not turn to face the
	/// viewer the way a straight beam's quad does.
	/// </summary>
	public IReadOnlyList<Vec3i> Points => _points;

	/// <summary>
	/// How many quads the chain spans — one fewer than its node count, since quad <c>k</c> is
	/// bounded by nodes <c>k</c> and <c>k+1</c>. Zero for a straight beam.
	/// </summary>
	public int QuadCount => _points.Length == 0 ? 0 : _points.Length / 2 - 1;

	/// <summary>
	/// The four points of quad <paramref name="index"/>, in the order the original's vertex-index
	/// table puts them — low, high, high, low, so the quad is wound as a ribbon rather than crossed.
	/// The table and how it is built are in docs/retail/simulation/beam-visuals.md, "The paint uses the
	/// polygon renderers' project, clip and fill chain".
	/// </summary>
	public (Vec3i A, Vec3i B, Vec3i C, Vec3i D) Quad(int index) {
		int node = index * 2;
		return (_points[node + 1], _points[node], _points[node + 2], _points[node + 3]);
	}

	/// <summary>
	/// <c>BeamTracer_LifeTick</c> (<c>0040c2a0</c>), vtable <c>+0x14</c> — one <c>Math_CountdownTimerTick</c> and nothing else.
	/// </summary>
	/// <returns>Whether the tracer has expired and should be freed.</returns>
	internal bool Tick() {
		Life = (short)(Life - SimMath.TickDelta);
		if (Life < 0) {
			Life = 0;
		}

		return Life == 0;
	}

	/// <summary>
	/// <c>BeamTracer_Ctor</c>'s jagged branch. One node per <see cref="NodeSpacing"/> units of the
	/// straight-line distance plus one, walked from the muzzle along a step rescaled to that
	/// spacing; the last node restarts from the exact endpoint instead of the walked position.
	///
	/// <para>Every node but the first is jittered — see <see cref="JitterMask"/> — the endpoint
	/// included.</para>
	/// </summary>
	private static Vec3i[] BuildChain(Vec3i start, Vec3i end, int halfWidth, SimRandom random) {
		// (char)(distance >> 10) + 1, a signed byte in the original.
		int nodeCount = (sbyte)((byte)(start.ApproxDistanceTo(end) >> 10) + 1);
		nodeCount = Math.Clamp(nodeCount, 1, MaxQuads);

		int stepX = end.X - start.X;
		int stepY = end.Y - start.Y;
		int stepZ = end.Z - start.Z;
		SimMath.ScaleToLength(ref stepX, ref stepY, ref stepZ, NodeSpacing);

		var points = new Vec3i[nodeCount * 2 + 2];
		var walked = start;
		for (int node = 0; node <= nodeCount; node++) {
			var here = node == nodeCount ? end : walked;
			if (node != 0) {
				here = new Vec3i(
					here.X + random.NextMasked(JitterMask),
					here.Y + random.NextMasked(JitterMask),
					here.Z + random.NextMasked(JitterMask));
			}

			points[node * 2] = new Vec3i(here.X, here.Y, here.Z + halfWidth);
			points[node * 2 + 1] = here;
			walked = new Vec3i(walked.X + stepX, walked.Y + stepY, walked.Z + stepZ);
		}

		return points;
	}
}
