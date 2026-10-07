using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

// Mech_PlaceLegsOnGround (004195c8): the shadows under the machine's parts, and footfall
// detection. See docs/retail/simulation/mech-locomotion.md and docs/retail/simulation/ground-shapes.md.
public sealed partial class MechObject {
	/// <summary>
	/// <c>mech+0x238</c>, count <c>mech+0x23c</c> — the machine's shadows, one <see cref="GroundShape"/>
	/// per entry of the chassis' part list (<see cref="MechTypeRecord.ShadowCount"/>): one under each
	/// foot and one under the body on a biped, one under each of four feet on the PITBULL, none on the
	/// SPIDER. <see cref="PlaceLegsOnGround"/> moves each under its
	/// part every movement tick. An entry is null once its leg has been shot off
	/// (<see cref="GradeLegs"/>), after the SPIDER's vanish, or when the pool was full at spawn.
	/// </summary>
	public IReadOnlyList<GroundShape?> Shadows => _shadows;

	private GroundShape?[] _shadows = System.Array.Empty<GroundShape?>();

	/// <summary>
	/// <c>Mech_Constructor</c>'s allocation (<c>00415e4a</c>-<c>00415f11</c>): entry <c>i</c> is the
	/// flat set's root <see cref="MechTypeRecord.LegKind"/>(<c>i</c>), a shadow, built at
	/// <see cref="GroundShape.HiddenDepth"/> so it is out of sight until the first placement. Called
	/// once, as the machine joins the world.
	/// </summary>
	internal void AllocateShadows(SimWorld world) {
		if (_shadows.Length > 0) {
			return;
		}

		_shadows = new GroundShape?[Type.ShadowCount];
		for (int i = 0; i < _shadows.Length; i++) {
			_shadows[i] = world.SpawnGroundShape(Type.LegKind(i),
				new Vec3i(0, 0, GroundShape.HiddenDepth));
		}
	}

	/// <summary>
	/// <c>ObjectPool_QueueForDelete</c> on one entry and a null in its slot — the leg branch of
	/// <c>Mech_ComponentDamageWrite</c> (<c>00418152</c>-<c>00418185</c>) for a leg whose servos read
	/// fully destroyed, and its no-wreck branch for every entry.
	/// </summary>
	private void ReleaseShadow(SimWorld world, int index) {
		if (index >= 0 && index < _shadows.Length) {
			world.ReleaseGroundShape(_shadows[index]);
			_shadows[index] = null;
		}
	}

	/// <summary>
	/// The first half of each pass of <c>Mech_PlaceLegsOnGround</c>'s loop, run for every entry
	/// whatever the machine is doing: the entry's shadow goes to its part's world position and takes
	/// the machine's heading. Its height, pitch and roll are the draw's business — see
	/// <see cref="GroundShape.ConformToTerrain"/>.
	/// </summary>
	private void PlaceShadows() {
		for (int i = 0; i < _shadows.Length; i++) {
			if (_shadows[i] is not { } shape) {
				continue;
			}

			// Transform_ApplyToPoint of the part's node translation through the machine's frame —
			// the translation PartTransform composes, and the machine's own origin for a part the
			// shape lacks.
			var part = PartTransform(Type.LegPartId(i));
			shape.Position = new Vec3i(part.X, part.Y, part.Z);
			shape.Heading = Heading;
		}
	}

	/// <summary>
	/// How near the camera a machine has to be for its footsteps to be played at all — the
	/// original's own test, made before it calls Sound_PlayAt rather than left to the catalog row's
	/// cutoff. <c>foot2</c>'s own range would allow 20480, so this is the tighter of the two.
	/// </summary>
	public const int FootfallAudibleRange = 15000;

	/// <summary>The gait the original's own leg thresholds are indexed by.</summary>
	private const int GaitWalking = 0;
	private const int GaitReversing = 1;
	private const int GaitRunning = 2;

	/// <summary>Per-leg arming flags — <c>mech+0x2b4</c>, one byte a leg.</summary>
	private bool[] _legArmed = System.Array.Empty<bool>();

	/// <summary>
	/// How many times a foot has planted. The host watches it for the edge rather than a flag, so a
	/// tick that plants two feet at once is still two footfalls.
	/// </summary>
	public int Footfalls { get; private set; }

	/// <summary>
	/// <c>Mech_PlaceLegsOnGround</c> (<c>004195c8</c>) — runs at the end of every movement tick and
	/// works out, per leg, whether the foot has just planted.
	///
	/// <para>The test is on the leg node's <b>fore/aft</b> position in the machine's own frame, not
	/// its height: a foot swings forward, past the gait's arming figure
	/// (<see cref="MechTypeRecord.FootfallRearm"/>), and plants as it comes back through the trigger
	/// (<see cref="MechTypeRecord.FootfallTrigger"/>). Reversing flips both comparisons, since the
	/// foot travels the other way, and it has its own pair of figures.</para>
	///
	/// <para>The plant does two things. It kicks the player's cockpit view (see
	/// <c>CockpitViewKick</c>, driven off <see cref="Footfalls"/>), and it plays sound <c>0x1d</c> for
	/// any machine within <see cref="FootfallAudibleRange"/> of the camera.</para>
	///
	/// <para>Before any of that, every pass of the loop moves its entry's shadow — see
	/// <see cref="PlaceShadows"/>. The original does both in the one loop, the placement first
	/// and unconditionally, so doing all the placement up front is the same outcome.</para>
	/// </summary>
	private void PlaceLegsOnGround(SimWorld world) {
		if (Thread is not { } thread || Animation is not { } animation) {
			return;
		}

		PlaceShadows();

		var type = Type;
		int legs = type.LegCount;
		if (legs <= 0) {
			return;
		}

		if (_legArmed.Length < legs) {
			System.Array.Resize(ref _legArmed, legs);
		}

		// The gait is read off the sequence playback is heading for, falling back to the one running.
		int sequence = thread.TargetSequence >= 0 ? thread.TargetSequence : thread.Sequence;

		int gait;
		if (sequence == type.WalkSequence || sequence == type.TurnInPlaceSequence
			|| sequence == type.StopForwardSequence || sequence == type.StopReverseSequence) {
			gait = AnimRate < 1 ? GaitReversing : GaitWalking;
		} else if (sequence == type.RunSequence) {
			gait = GaitRunning;
		} else {
			// Any other sequence — the death fall, a torso sweep — has no walk cycle to take steps
			// from. The original has a death-sequence arm of its own here, but it leaves the sound
			// id unset and so can never reach the footfall it guards: nothing to port.
			return;
		}

		// Reversing runs both comparisons the other way round; the original carries it as a flag
		// rather than negating the figures, because the figures are already signed for the gait.
		bool forward = gait != GaitReversing;
		short trigger = type.FootfallTrigger(gait);
		short rearm = type.FootfallRearm(gait);

		for (int leg = 0; leg < legs; leg++) {
			if (type.LegKind(leg) != 0 || LegLost(leg)) {
				// A leg the machine has had shot off plants nothing. The original deletes that leg's
				// shadow, and this loop is the walk over that array — a deleted slot is simply
				// not there any more. See MechObject.GradeLegs.
				continue;
			}

			int node = animation.TransformIdOfPart(type.LegPartId(leg));
			if (node < 0) {
				continue;
			}

			int position = NodeTransform(node).Y;

			if (forward ? rearm < position : position < rearm) {
				_legArmed[leg] = true;
			}

			if (_legArmed[leg] && (forward ? position < trigger : trigger < position)) {
				_legArmed[leg] = false;
				Footfalls++;

				// The original tests the distance to the camera itself before it plays anything,
				// rather than leaving the cutoff to foot2's own catalog range — which at 20480 is
				// the looser of the two.
				if (Position.ApproxDistanceTo(world.ListenerPosition) > FootfallAudibleRange) {
					continue;
				}

				// At the foot, not at the machine's origin — the original passes the leg node's own
				// world position, so a walking HERC's steps pan with the leg that took them.
				//
				// It has to be the world one. The trigger test above reads the node in shape space,
				// which is where the .DAT's thresholds are measured, but a shape-space point handed to
				// Sound_PlayAt places the step near the world origin instead of near the machine —
				// past foot2's 20480 cutoff from anywhere a mission actually happens, so the step is
				// computed, found inaudible and dropped.
				var foot = PartTransform(type.LegPartId(leg));
				world.Sounds?.PlayAt(SoundId.Footfall, new Vec3i(foot.X, foot.Y, foot.Z));
			}
		}
	}
}
