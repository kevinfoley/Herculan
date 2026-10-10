using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim.Anim;

/// <summary>
/// One machine's animated shape: its <see cref="ShapeAnimation"/> plus the threads playing on it,
/// and the node poses that come out of the two together.
///
/// <para>A HERC runs <b>three</b> threads at once, built in this order by <c>Mech_Constructor</c>
/// (<c>00415bb0</c>): locomotion on <c>typeRec+0x12</c> at <c>mech+0x22c</c>, torso twist on
/// <c>typeRec+0x1c</c> at <c>+0x230</c>, torso pitch on <c>typeRec+0x24</c> at <c>+0x234</c>. On
/// retail data they cover disjoint nodes and never meet; where a shape's threads do,
/// <see cref="LocalOf"/> settles it the way the original does.</para>
///
/// <para>DBSIM keeps a ready-made array of every node's world transform on the shape instance
/// (<c>shapeInst+0x16</c>) and rebuilds all of it each tick: <c>ShapeInst_EvalAllNodeLocals</c>
/// (<c>004789f4</c>) runs every thread over the local array, then
/// <c>ShapeInst_BuildWorldTransforms</c> (<c>00478b58</c>) composes the locals up the relation list.
/// This composes one requested node instead, so the dirty-flag machinery driving that rebuild has no
/// counterpart and needs none.</para>
/// </summary>
public sealed class ShapeInstance {
	private readonly List<AnimationThread> _threads = new();

	public ShapeInstance(ShapeAnimation animation) {
		Animation = animation;
	}

	/// <summary>The shape's animation data, shared by every machine of the type.</summary>
	public ShapeAnimation Animation { get; }

	/// <summary>The threads playing on this shape, in the order they were added.</summary>
	public IReadOnlyList<AnimationThread> Threads => _threads;

	/// <summary>
	/// Adds a thread playing <paramref name="sequence"/> from its first frame, stopped —
	/// <c>ShapeInst_AddThread</c> (<c>00402374</c>), which builds the thread, cuts it to the sequence, sets its rate to zero
	/// and registers it on the shape instance (<c>ShapeInst_RegisterThread</c>, <c>00478930</c>). A
	/// caller that wants the thread running sets <see cref="AnimationThread.Rate"/> itself, as the
	/// locomotion tick does.
	///
	/// <para>The original re-sorts its thread slots by priority on every registration and every
	/// step; this list stays in registration order and <see cref="LocalOf"/> applies the ranking
	/// when it reads.</para>
	/// </summary>
	public AnimationThread AddThread(int sequence) {
		var thread = new AnimationThread(Animation);
		thread.SetSequence(sequence, 0, 0);
		_threads.Add(thread);
		return thread;
	}

	/// <summary>
	/// <c>AnimThread_StepAll</c>'s (<c>004789a0</c>) advance half — every thread on this shape moved
	/// on by one timestep's worth of animation time. The original then re-sorts the threads by
	/// priority and reads the first one's ramped root transform into the node array; a caller that
	/// wants that root motion asks the
	/// thread for it directly (<see cref="AnimationThread.ReadRoot"/>), as
	/// <see cref="MechObject.IntegrateMotion"/> and <see cref="BaseObject.StepAnimation"/> both do.
	///
	/// <para>A thread whose rate is zero is unmoved by this, which is why a turret thread parked by
	/// <see cref="AnimationThread.SeekToPosition"/> stays where the seek put it.</para>
	/// </summary>
	public void StepAnimation(short delta) {
		foreach (var thread in _threads) {
			thread.Advance(delta);
		}
	}

	/// <summary>
	/// One node's pose in shape space: its own local transform composed up its parent chain.
	/// The original composes every node into its world slot through
	/// <c>ShapeInst_ComposeChildTransform</c> (<c>00478ab8</c>); this composes one node per request.
	///
	/// <para>Identity for an unknown id, so a shape with no such node puts whatever rides it at the
	/// machine's own origin rather than somewhere invented.</para>
	/// </summary>
	/// <param name="transformId">The node, in the transform id space the relations table uses.</param>
	public Transform3 NodeTransform(int transformId) {
		var accumulated = Transform3.Identity;
		var parents = Animation.ParentTransform;

		for (int step = 0; transformId >= 0 && step <= parents.Length; step++) {
			if (LocalOf(transformId) is { } local) {
				accumulated = Transform3.Concat(accumulated, NodeLocal(local));
			}

			transformId = transformId < parents.Length ? parents[transformId] : -1;
		}

		return accumulated;
	}

	/// <summary>
	/// Which transform a node holds right now: of the threads that animate it, the one whose playing
	/// sequence has the lowest <see cref="AnimSequence.Priority"/> wins, the earlier-added on a tie,
	/// and a node no thread animates keeps its rest transform.
	///
	/// <para><c>ShapeInst_EvalAllNodeLocals</c> (<c>004789f4</c>) runs the thread slots last to
	/// first, each overwriting the locals of every node its sequence covers, so the first slot's
	/// writes stand; <c>ShapeInst_SortThreadsByPriority</c> (<c>004788c4</c>) keeps the slots in
	/// ascending priority, swapping only on strictly greater, so equal priorities never change places
	/// and stay in registration order. Ranking at read time gives the same answer as keeping the
	/// list sorted.</para>
	///
	/// <para>On retail data it decides nothing: the sequences a type record names cover disjoint
	/// nodes on every chassis. See docs/retail/rendering/dts-node-posing.md, "Several threads on one
	/// shape".</para>
	/// </summary>
	private AnimTransform? LocalOf(int transformId) {
		AnimTransform? winner = null;
		int winnerPriority = 0;
		foreach (var thread in _threads) {
			if (!thread.TryGetLocal(transformId, out var local)) {
				continue;
			}

			int priority = Animation.Sequences[thread.Sequence].Priority;
			if (winner == null || priority < winnerPriority) {
				winner = local;
				winnerPriority = priority;
			}
		}

		if (winner != null) {
			return winner;
		}

		var defaults = Animation.DefaultTransforms;
		if (transformId < 0 || transformId >= defaults.Length) {
			return null;
		}

		int index = defaults[transformId];
		return index >= 0 && index < Animation.Transforms.Length ? Animation.Transforms[index] : null;
	}

	/// <summary>One pool entry as a transform: the euler rotation with the translation hung off it.</summary>
	private static Transform3 NodeLocal(in AnimTransform source) {
		var transform = Transform3.FromEuler(source.RotationX, source.RotationY, source.RotationZ);
		transform.X = source.X;
		transform.Y = source.Y;
		transform.Z = source.Z;
		return transform;
	}
}
