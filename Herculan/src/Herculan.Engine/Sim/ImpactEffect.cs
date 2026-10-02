using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What happens where a shot lands — DBSIM's explosion class, built by <c>Explosion_Construct</c> (<c>00407f1c</c>) and
/// advanced by <c>Explosion_TickUpdate</c> (<c>0040813c</c>), allocated from the pool at <c>g_ExplosionPool</c> (<c>004a96a2</c>)
/// (docs/simulation/impact-effects.md).
///
/// <para>An effect is a <c>dts\EXPLOS.DTS</c> root standing still at the point of impact, playing
/// its flipbook of billboards through exactly once. <c>Explosion_TickUpdate</c> is the whole of its life:
/// count the type's <see cref="ExplosionTypeEntry.FrameInterval"/> down, step the shape's
/// cell-animation frame when it expires, and end the effect on the step that wraps the frame back to
/// zero. Nothing moves it and nothing else can stop it.</para>
///
/// <para>Like a <see cref="BeamTracer"/> and a <see cref="Projectile"/> it is <b>not</b> a
/// <see cref="SimObject"/> in the original either — it comes from a pool of its own that
/// <c>Sim_MainTick</c> walks ahead of the machine list, so nothing can shoot it and nothing collides
/// with it. The original's pool holds 40 and this list is unbounded, and the original's owner-based
/// draw rule is not ported (both under Open in the doc).</para>
///
/// <para>A row with a nonzero <see cref="ExplosionTypeEntry.LightMode"/> also claims a dynamic
/// light for as long as the flipbook runs — <see cref="EffectLightField"/>, whose slot this drives
/// from the row's per-frame intensity ramp. <c>LightMode</c> 1 and 2 reach the same code; the
/// original tests the field only against zero. A flipbook longer than the row's twelve ramp entries
/// reads on into the row's later fields, as the original does — <see cref="ExplosionCatalog.RampWord"/>.</para>
///
/// <para>A row with a nonzero <see cref="ExplosionTypeEntry.GroundShape"/> lays a
/// <see cref="Sim.GroundShape"/> under the effect for as long as it runs, stepping its cell with the
/// flipbook. No retail row asks for one. The proximity radius the type's own query slot reports on is
/// unread — nothing queries it.</para>
/// </summary>
public sealed class ImpactEffect {
	private readonly ExplosionTypeEntry _record;
	private readonly ExplosionCatalog _catalog;
	private readonly int _frameCount;
	private readonly EffectLightField? _lights;
	private short _timer;

	/// <param name="typeId">The <c>EXPLOS.DAT</c> type row, which is what a <c>PROJ.DAT</c> <c>ImpactFX</c> array holds.</param>
	/// <param name="catalog">The table the row is in, which the light's ramp can read past the row into.</param>
	/// <param name="record">That row.</param>
	/// <param name="frameCount">How many frames the row's shape has — see <see cref="ExplosionCatalog.FrameCount"/>.</param>
	/// <param name="position">Where the shot landed, in world units.</param>
	/// <param name="lights">
	/// The field a light-bearing row claims a slot in, or null to run the effect without one.
	/// </param>
	/// <param name="world">
	/// The world a row asking for a ground shape takes it from, or null to run the effect without one.
	/// </param>
	internal ImpactEffect(
			short typeId, ExplosionCatalog catalog, ExplosionTypeEntry record, int frameCount, Vec3i position,
			EffectLightField? lights = null, SimWorld? world = null) {
		TypeId = typeId;
		_catalog = catalog;
		_record = record;
		_frameCount = frameCount;
		Position = position;

		// The constructor resets the shape instance's own frame counter for this sequence, so an
		// effect always opens on frame 0 however the shape was left by the last one to use it.
		Frame = 0;
		_timer = record.FrameInterval;

		// EffectLight_Construct (00407604): the row's LightMode is tested against zero and nothing else, and the slot
		// opens on FrameIntensity[0] — the one ramp entry the tick never reaches, because it reads
		// the ramp at the frame it has just stepped to and stops the effect when that wraps to 0.
		_lights = record.LightMode != 0 ? lights : null;
		LightHandle = _lights?.Claim(position, FrameIntensity(0)) ?? -1;

		// Explosion_Construct (00407fc5): root 1 of the flat set at the effect's own point, before the
		// light. The pointer is kept at effect+0x4f and the destructor (Explosion_Destruct, 00407e48)
		// queues it for deletion.
		if (record.GroundShape != 0 && world != null) {
			_world = world;
			GroundShape = world.SpawnGroundShape(Sim.GroundShape.ImpactShapeIndex, position);
			_groundShapeFrames = world.GroundShapeFrameCount(Sim.GroundShape.ImpactShapeIndex);
		}
	}

	/// <summary>
	/// <c>effect+0x4f</c> — the ground shape this effect laid, or null for a row that asks for none (every
	/// retail row) or a spawn into a full pool.
	/// </summary>
	public GroundShape? GroundShape { get; private set; }

	private readonly SimWorld? _world;
	private readonly int _groundShapeFrames;

	/// <summary>The <c>EXPLOS.DAT</c> type row this effect is, <c>obj+0x41</c>.</summary>
	public short TypeId { get; }

	/// <summary>
	/// Which <see cref="EffectLightField"/> slot this effect's light occupies, or -1 when the row
	/// asks for no light or every slot was busy. The handle the original keeps at
	/// <c>handle+0x0c</c>.
	/// </summary>
	public int LightHandle { get; private set; } = -1;

	/// <summary>Which <c>EXPLOS.DTS</c> root it draws — the type row's own first field.</summary>
	public int ShapeIndex => _record.ShapeIndex;

	/// <summary>Where it sits, in world units. Written once at construction; nothing moves it.</summary>
	public Vec3i Position { get; }

	/// <summary>
	/// The shape's cell-animation frame. The original keeps it on the shape instance rather than on
	/// the effect, which is the same thing given one instance per effect.
	/// </summary>
	public int Frame { get; private set; }

	/// <summary>
	/// <c>Explosion_TickUpdate</c> (<c>0040813c</c>), called directly by <c>Sim_MainTick</c>. Returns whether the effect is finished and should be
	/// freed — which happens the moment the flipbook wraps, so the animation plays exactly once.
	///
	/// <para>A shape with no frames at all ends on its first timer expiry, matching the original's
	/// own branch for a negative animation sequence: there is no frame to step, so there is nothing
	/// left to draw.</para>
	/// </summary>
	internal bool Tick() {
		if (SimMath.CountdownTimerTick(ref _timer) != 0) {
			return false;
		}

		if (_frameCount <= 0) {
			Release();
			return true;
		}

		Frame = (Frame + 1) % _frameCount;
		if (Frame == 0) {
			Release();
			return true;
		}

		// EffectLight_SetIntensity (004076a0), driven from the ramp at the frame just stepped to. Reached only for a
		// nonzero frame, which is why FrameIntensity[0] is the constructor's business alone.
		_lights?.SetIntensity(LightHandle, FrameIntensity(Frame));

		// The ground shape's own sequence 0 steps beside it, modulo its own cell count rather than the
		// effect's, so the two flipbooks need not be the same length.
		if (GroundShape != null && _groundShapeFrames > 0) {
			GroundShape.Frame = (GroundShape.Frame + 1) % _groundShapeFrames;
		}

		_timer = _record.FrameInterval;
		return false;
	}

	/// <summary>
	/// The type row's intensity ramp at one frame, as the original reads it — the word's low byte,
	/// read past the ramp's twelve entries when the flipbook is longer.
	/// </summary>
	private int FrameIntensity(int frame) => _catalog.RampWord(TypeId, frame) & 0xff;

	/// <summary>
	/// What the effect's end hands back: the light slot (<c>EffectLight_Destruct</c>,
	/// <c>0040765c</c>) and the ground shape (<c>Explosion_Destruct</c>, <c>00407e48</c>).
	/// </summary>
	private void Release() {
		_lights?.Release(LightHandle);
		LightHandle = -1;

		_world?.ReleaseGroundShape(GroundShape);
		GroundShape = null;
	}
}
