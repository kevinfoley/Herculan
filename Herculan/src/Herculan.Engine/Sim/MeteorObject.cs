using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Sim;

/// <summary>
/// A drop pod — DBSIM's <c>METEOR</c> class, built by <c>Meteor_Construct</c> (<c>00409b44</c>),
/// advanced by <c>Meteor_Tick</c> (<c>00409d2c</c>) and drawn by <c>Meteor_Render</c>
/// (<c>00409cd0</c>), out of the ten-entry pool at <c>g_MeteorPool</c>.
///
/// <para>It is how a mission delivers reinforcements out of the sky: <see cref="MissionGroup"/> puts
/// one in the air when its arrival action fires with a pod verb and <b>leaves its own gate set</b>,
/// so the group it carries is still out of the mission while the pod is falling. The pod clears that
/// gate when it lands and finishes opening, which is the moment the group becomes real — and it
/// carries the group there, so where the pod lands is where the group is.</para>
///
/// <para>Like <see cref="DebrisObject"/> and <see cref="ImpactEffect"/> it comes out of an effect
/// pool <c>Sim_MainTick</c> walks ahead of the object list, not out of the object list itself:
/// nothing can see it, target it or shoot it, and it is not a <see cref="SimObject"/>.</para>
/// </summary>
public sealed class MeteorObject {
	/// <summary>
	/// <c>Meteor_Construct</c> (<c>00409b44</c>) — puts a pod in the air on a trajectory that lands
	/// it on <paramref name="target"/>.
	///
	/// <para><b>The pod is not dropped straight down.</b> It is placed a random heading's worth of
	/// distance <i>short</i> of the target and flown in along that heading at a flat 2,000 units a
	/// tick, so it comes in on a slant. That horizontal offset is the roll: 70,000 units plus up to
	/// 25,000 more. The fall time is that distance over the horizontal speed, and the launch height
	/// is then whatever a constant <see cref="Gravity"/> would need over that many ticks —
	/// <c>(n²/2)·50</c>, which for the roll's range is 30,600 to 55,200 units up. <b>The height is
	/// derived, not drawn</b>; reading the drawn figure as an altitude puts the pod twice as high as
	/// it belongs.</para>
	///
	/// <para>Both axes are then flown at constant velocity — the acceleration exists only to pick
	/// the vertical speed — so the pod's descent is a straight line and reaches the ground in exactly
	/// the tick the horizontal run ends. It is the terrain query, not the count, that ends the fall.</para>
	/// </summary>
	/// <param name="target">The point the pod is aimed at, from <see cref="Deployment.PickPointNearPlayer"/>.</param>
	/// <param name="group">The group it delivers, or null for a pod that carries nothing.</param>
	/// <param name="random">The simulation's generator, for the heading and the run-in distance.</param>
	internal MeteorObject(Vec3i target, MissionGroup? group, SimRandom random) {
		Group = group;
		Heading = random.Next() & 0xffff;

		int runIn = random.NextBelow(RunInSpread) + RunInBase;
		int ticks = runIn / HorizontalSpeed;
		int height = (ticks * ticks >> 1) * Gravity;

		short cos = BinaryAngle.Cos(Heading);
		short sin = BinaryAngle.Sin(Heading);

		// The start offset is (0, -runIn, 0) rotated by the heading, and the velocity is
		// (0, HorizontalSpeed, 0) through the same rotation -- so the pod starts behind the target
		// along its own facing and flies forward onto it.
		Position = new Vec3i(
			target.X + (int)(((long)runIn * sin + 0x2000) >> 14),
			target.Y - (int)(((long)runIn * cos + 0x2000) >> 14),
			target.Z + height);

		_velocity = (
			(short)(int)(((long)-HorizontalSpeed * sin + 0x2000) >> 14),
			(short)(int)(((long)HorizontalSpeed * cos + 0x2000) >> 14),
			(short)(ticks == 0 ? -height : -height / ticks));

		Pitch = (short)SimTrig.Atan2(_velocity.Z, HorizontalSpeed);
	}

	/// <summary>The group the pod delivers. Null for one carrying nothing, which nothing spawns.</summary>
	public MissionGroup? Group { get; }

	/// <summary>Where it is, in world units.</summary>
	public Vec3i Position { get; private set; }

	/// <summary>Which way it flies — <c>obj+0x10</c>, drawn once at construction and never changed.</summary>
	public int Heading { get; }

	/// <summary>
	/// <c>obj+0x0c</c> — the nose angle, <c>atan2(2000, vz)</c> so the shape lies along its own fall
	/// line. Zeroed on landing, which stands the pod up.
	/// </summary>
	public short Pitch { get; private set; }

	/// <summary><c>obj+0x4b</c> — whether it is down.</summary>
	public bool Landed { get; private set; }

	/// <summary>
	/// <c>obj+0x4c</c> — whether the landing blast <b>caught</b> anything, which <b>suppresses the
	/// group handover</b>. A pod that comes down on top of a machine or a building delivers nothing:
	/// the group it carried stays out of the mission for the rest of the run. That is a real
	/// behaviour of the original and the reason the pod's arrival point is picked without the object
	/// test — see <see cref="Deployment.PickPointNearPlayer"/>.
	///
	/// <para>Caught, not hurt: <see cref="SimWorld.ExplosiveBlastSweep"/> answers on range alone, so a
	/// machine whose shields swallow the blast still stops the delivery.</para>
	/// </summary>
	public bool BlastObstructed { get; private set; }

	/// <summary>
	/// <c>obj+0x4d</c> — the opening animation's accumulator, advanced at
	/// <see cref="OpenRate"/> per second once landed. The drawn cell is this shifted down ten bits.
	/// </summary>
	public int OpenProgress { get; private set; }

	/// <summary>Which cell of the opening flipbook is showing — <c>obj+0x4d &gt;&gt; 10</c>.</summary>
	public int AnimationFrame => OpenProgress >> 10;

	/// <summary>
	/// <c>obj+0x51</c> — whether the descent whistle has been played. It goes off once, the first
	/// tick the pod is under <see cref="WhistleAltitude"/>, and never again.
	/// </summary>
	public bool WhistlePlayed { get; private set; }

	/// <summary>Its frame, for a renderer: the pitch about X and the heading about Z.</summary>
	public Transform3 WorldTransform {
		get {
			var frame = Transform3.FromEuler(Pitch, 0, (short)Heading);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>
	/// <c>Meteor_Tick</c> (<c>00409d2c</c>) — two phases, and the pod spends its whole life in one
	/// then the other.
	///
	/// <para><b>Falling.</b> Integrate by the constant velocity, then ask the terrain how high the
	/// ground is under the new position. Under it means down: the pod snaps to ground height, stands
	/// up, makes its landing noise and sets off a blast — and whether that blast <i>hit</i> anything
	/// decides whether the group is ever delivered.</para>
	///
	/// <para><b>Landed.</b> The opening flipbook runs. When it passes its last cell the pod hands its
	/// own landed position to the group's leader, clears the group's gate, and is freed. The handover
	/// is the last thing that happens, not the first: a pod that is still opening has delivered
	/// nothing.</para>
	/// </summary>
	/// <param name="world">The running world.</param>
	/// <param name="frameCount">
	/// How many cells the opening shape has, which is what ends the animation. A shape the install
	/// does not have reports zero and the pod opens instantly, which is the same handover on the tick
	/// after it lands.
	/// </param>
	/// <returns>Whether the pod is finished and should be released.</returns>
	internal bool Tick(SimWorld world, int frameCount) {
		if (!Landed) {
			var next = new Vec3i(
				Position.X + _velocity.X, Position.Y + _velocity.Y, Position.Z + _velocity.Z);

			Pitch = (short)SimTrig.Atan2(_velocity.Z, HorizontalSpeed);

			// The original plays both sounds at the camera itself, not at the pod; the tweak moves them
			// onto it. See TweakSoundReach.
			bool fromPod = world.Tweaks.GetSettingValue(TweakSettingDefinitions.DropPodSoundFromPod);

			if (!WhistlePlayed && next.Z < WhistleAltitude) {
				if (fromPod) {
					world.Sounds?.PlayAt(SoundId.PodFalling, next, TweakSoundReach, this);
				} else {
					world.Sounds?.PlayAt(SoundId.PodFalling, world.ListenerPosition);
				}

				WhistlePlayed = true;
			} else if (WhistlePlayed && fromPod) {
				world.Sounds?.MoveTo(SoundId.PodFalling, next, TweakSoundReach, this);
			}

			int ground = world.GroundHeightAt(next);
			if (next.Z < ground) {
				Landed = true;
				Pitch = 0;
				next = new Vec3i(next.X, next.Y, ground);

				if (fromPod) {
					world.Sounds?.PlayAt(SoundId.PodLanded, next, TweakSoundReach);
				} else {
					world.Sounds?.PlayAt(SoundId.PodLanded, world.ListenerPosition);
				}

				// The pod passes no attacker, so anything it kills on the way down is nobody's kill. The
				// answer is "was anything in range", not "was anything hurt" -- see BlastObstructed.
				BlastObstructed = world.ExplosiveBlastSweep(
					next, BlastRadius, BlastDamage, attacker: null, excluded: null);
			}

			Position = next;
			return false;
		}

		if (AnimationFrame < frameCount) {
			OpenProgress += SimMath.IntegrateRateOverTick(OpenRate);
			return false;
		}

		if (Group is { Leader: { } leader } group && !BlastObstructed) {
			leader.Position = Position;
			group.Deploy();
		}

		// A pod that carried a group leaves its mark on the ground whether or not it delivered it —
		// the flat set's root 3 at the landing point, for the rest of the mission. It keeps no pointer
		// to it. See docs/retail/simulation/ground-shapes.md.
		if (Group != null) {
			world.SpawnGroundShape(GroundShape.DropPodShapeIndex, Position);
		}

		return true;
	}

	private readonly (short X, short Y, short Z) _velocity;

	/// <summary>How far short of its target the pod starts, at minimum.</summary>
	public const int RunInBase = 70000;

	/// <summary>And how much more, at most — <c>Math_RandomBelow(25000)</c>.</summary>
	public const short RunInSpread = 25000;

	/// <summary>The pod's flat horizontal speed, in world units per tick.</summary>
	public const int HorizontalSpeed = 2000;

	/// <summary>
	/// The constant the launch height is derived from, in world units per tick squared. It is never
	/// applied: the descent is flown at the constant speed it implies.
	/// </summary>
	public const int Gravity = 50;

	/// <summary>The absolute height the descent whistle starts at.</summary>
	public const int WhistleAltitude = 50000;

	/// <summary>
	/// The volume and range both pod sounds are placed with under
	/// <see cref="Settings.TweakSettingDefinitions.DropPodSoundFromPod"/>. Not the original's, which
	/// plays them at the camera by their catalog rows: volume 70, rolloff from 5 × 1024 world units
	/// (31 m) to a cutoff at 100 × 1024 (614 m).
	///
	/// <para>A pod lands about 150,000 units (900 m) from the player
	/// (<see cref="MissionGroup.PodDistance"/>) and starts up to <see cref="RunInBase"/> +
	/// <see cref="RunInSpread"/> further out in a random direction, so the whistle can begin about
	/// 1.5 km away. The rows' 614 m cutoff would leave most pods unheard, so the cutoff here is 4 km,
	/// at 1000 units per 6 m (<see cref="Render.WorldScale.WorldUnitsPerMeter"/>), and the volume is
	/// the catalog's full 100. Rolloff starts where the rows' does. The director's rolloff is linear to the cutoff, so a pod is heard at
	/// about 74 at 1 km and 77 at a typical landing, against the 69 retail plays both sounds at
	/// wherever the pod is.</para>
	///
	/// <para>The pod falls for 1.4 to 1.9 seconds and covers up to 95,000 units across in that time,
	/// so the whistle is moved with it every tick rather than left where it started. The pod passes
	/// itself as the whistle's source, so only the pod whose whistle is the newest copy moves it.</para>
	/// </summary>
	public static readonly SoundReach TweakSoundReach = new(
		Volume: 100,
		MinRange: null,
		MaxRange: 4000 * 1000 / 6);

	/// <summary>How far the landing blast reaches.</summary>
	public const int BlastRadius = 3000;

	/// <summary>And how hard it hits.</summary>
	public const short BlastDamage = 10000;

	/// <summary>How fast the opening flipbook runs, in accumulator units per second.</summary>
	public const short OpenRate = 0x5dc;

	/// <summary>
	/// The pool's size, from <c>Meteor_LoadResources</c> (<c>00409a34</c>) — ten pods at once, which
	/// no retail mission comes close to.
	/// </summary>
	public const int PoolSize = 10;

	/// <summary>The shape file the pod is drawn from, by the literal name <c>meteor</c>.</summary>
	public const string ShapeLibraryName = "METEOR.DTS";

	/// <summary>
	/// And the bank <c>Meteor_LoadResources</c> binds to every shape in it, by the literal
	/// <c>impact</c>.
	/// </summary>
	public const string TextureBankName = "IMPACT";

	/// <summary>The root drawn while the pod is in the air.</summary>
	public const int FallingShapeIndex = 0;

	/// <summary>
	/// And the one drawn once it is down — a separate shape instance in the original
	/// (<c>obj+0x41</c>), and the one whose cells are the pod opening.
	/// </summary>
	public const int OpeningShapeIndex = 1;
}
