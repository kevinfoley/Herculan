using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What happens where a shot lands: a <c>dts\EXPLOS.DTS</c> root standing still at the point of
/// impact, playing its flipbook through once. DBSIM's explosion class, built by
/// <c>Explosion_Construct</c> (<c>00407f1c</c>) and advanced by <c>Explosion_TickUpdate</c>
/// (<c>0040813c</c>); its retail behavior is docs/retail/simulation/impact-effects.md.
///
/// <para>It is not a <see cref="SimObject"/>, as a <see cref="BeamTracer"/> and a
/// <see cref="Projectile"/> are not: it lives in a pool of <see cref="PoolSize"/> that the tick walks
/// ahead of the machine list, and keeps its slot after it has played out until the render-time
/// flush — see <see cref="EffectPools.SpawnImpactEffect"/> and <see cref="EffectPools.ImpactEffects"/>.</para>
///
/// <para>Its <see cref="Owner"/>, the object it was spawned on, decides whether it is drawn
/// (<see cref="HiddenFromOwnerCockpit"/>) and which terrain cell it is filed under
/// (<see cref="Render.ObjectDrawTable"/>).</para>
///
/// <para>A row with a nonzero <see cref="ExplosionTypeEntry.LightMode"/> holds an
/// <see cref="EffectLightField"/> slot from construction to <see cref="Destruct"/>, driven from the
/// row's per-frame intensity ramp; one with a nonzero <see cref="ExplosionTypeEntry.GroundShape"/>
/// lays a <see cref="Sim.GroundShape"/> that steps with the flipbook. The row's proximity radius is
/// not read: its one reader in the original, <c>Explosion_ProximityTest</c> (<c>00408100</c>), has no
/// reference found (docs/retail/formats/explos-dat.md).</para>
/// </summary>
public sealed class ImpactEffect {
	/// <summary>
	/// How many effects can exist at once — <c>g_ExplosionPool</c>'s count, <c>Pool_Init(pool, 0x28, 0x5b)</c>
	/// in <c>Explosion_LoadResources</c> (<c>00407b54</c>). See docs/retail/simulation/impact-effects.md.
	/// </summary>
	public const int PoolSize = 40;

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
	/// <param name="owner">The object the effect was spawned on, or null — see <see cref="Owner"/>.</param>
	/// <param name="lights">
	/// The field a light-bearing row claims a slot in, or null to run the effect without one.
	/// </param>
	/// <param name="world">
	/// The world a row asking for a ground shape takes it from, or null to run the effect without one.
	/// </param>
	internal ImpactEffect(
			short typeId, ExplosionCatalog catalog, ExplosionTypeEntry record, int frameCount, Vec3i position,
			SimObject? owner = null, EffectLightField? lights = null, SimWorld? world = null) {
		TypeId = typeId;
		_catalog = catalog;
		_record = record;
		_frameCount = frameCount;
		Position = position;
		Owner = owner;

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
	/// <c>effect+0x57</c> — the object the effect was spawned on, or null. Which sites pass one is in
	/// docs/retail/simulation/impact-effects.md#construction--explosion_construct-00407f1c. The draw
	/// reads it: <see cref="HiddenFromOwnerCockpit"/>, and the owner's terrain cell the effect is
	/// filed under (<c>Explosion_GetOwnerDrawCell</c>, <c>00408228</c>).
	/// </summary>
	public SimObject? Owner { get; }

	/// <inheritdoc cref="ExplosionTypeEntry.ObjectClass"/>
	/// <remarks>The tag decides how far away the effect is still drawn — see <see cref="Render.ObjectTypeTag"/>.</remarks>
	public short ObjectClass => _record.ObjectClass;

	/// <summary>
	/// Whether the effect has played out on this tick. It keeps its pool slot until
	/// <see cref="EffectPools.ImpactEffects"/>' render-time flush takes it out of the list.
	/// </summary>
	internal bool Finished { get; private set; }

	/// <summary>
	/// <c>Explosion_IsHiddenFromOwnerCockpit</c> (<c>00408240</c>) — whether the view leaves this effect out:
	/// it has an owner, the camera is attached to that owner (<paramref name="cameraAttachedTo"/>, the
	/// object <c>Cam_IsAttachedTo</c> (<c>00401078</c>) would answer true for), and it is not one of the types
	/// always drawn. So from inside a cockpit the effects on that machine's own hull are not drawn. See
	/// docs/retail/simulation/impact-effects.md#drawing.
	/// </summary>
	public bool HiddenFromOwnerCockpit(SimObject? cameraAttachedTo) {
		// The type id is the byte at +0x41, tested as 2 or as an unsigned (id - 11) < 4.
		byte type = (byte)TypeId;
		if (type == 2 || (byte)(type - 11) < 4) {
			return false;
		}

		return Owner != null && ReferenceEquals(Owner, cameraAttachedTo);
	}

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
	/// <c>Explosion_TickUpdate</c> (<c>0040813c</c>), called directly by <c>Sim_MainTick</c>. Sets <see cref="Finished"/> the
	/// moment the flipbook wraps, so the animation plays exactly once; the effect is not freed here but
	/// queued for the render-time flush, which runs <see cref="Destruct"/>.
	///
	/// <para>A shape with no frames at all ends on its first timer expiry, matching the original's
	/// own branch for a negative animation sequence: there is no frame to step, so there is nothing
	/// left to draw.</para>
	/// </summary>
	internal void Tick() {
		if (Finished || SimMath.CountdownTimerTick(ref _timer) != 0) {
			return;
		}

		if (_frameCount <= 0) {
			Finished = true;
			return;
		}

		Frame = (Frame + 1) % _frameCount;
		if (Frame == 0) {
			Finished = true;
			return;
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
	}

	/// <summary>
	/// The type row's intensity ramp at one frame, as the original reads it — the word's low byte,
	/// read past the ramp's twelve entries when the flipbook is longer.
	/// </summary>
	private int FrameIntensity(int frame) => _catalog.RampWord(TypeId, frame) & 0xff;

	/// <summary>
	/// <c>Explosion_Destruct</c> (<c>00407e48</c>), run by the flush that frees the pool slot: queues the
	/// light handle for the light pool's own flush (<see cref="EffectLightField.Release"/>) and hands the
	/// ground shape back.
	/// </summary>
	internal void Destruct() {
		_lights?.Release(LightHandle);
		LightHandle = -1;

		_world?.ReleaseGroundShape(GroundShape);
		GroundShape = null;
	}
}
