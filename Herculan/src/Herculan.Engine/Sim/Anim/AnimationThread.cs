using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim.Anim;

/// <summary>
/// One playing animation on a shape — DBSIM's 0x52-byte animation thread (constructed by
/// <c>AnimThread_Ctor</c> (<c>00478d3c</c>), stepped by <c>AnimThread_Advance</c> (<c>00479614</c>)). A mech keeps one, at <c>mech+0x22c</c>.
///
/// <para>Beyond the obvious sequence/frame cursor, a thread carries two things locomotion depends
/// on. The first is the <b>root-motion accumulator</b>: while a ground-movement sequence plays, the
/// thread holds the current frame's whole ground displacement and hands out the ramped fraction of
/// it that has elapsed. Seeding <see cref="ReadRoot"/>'s source to identity, stepping, then reading
/// back yields exactly the displacement covered by that step — which is how a HERC moves, since
/// HERCs have no velocity vector (see docs/retail/simulation/mech-locomotion.md). The second is the
/// <b>transition search</b>: asking for a target sequence does not cut to it, it looks for a
/// transition frame that leads there and plays through it.</para>
/// </summary>
public sealed class AnimationThread {
	private const short NoFrame = -1;

	/// <summary>Set once the thread has reached <see cref="TargetSequence"/>.</summary>
	private const int FlagAtTarget = 1;

	/// <summary>Set while the current frame is a transition frame rather than a sequence frame.</summary>
	private const int FlagInTransition = 2;

	/// <summary>
	/// Fixed-point scale of the sub-tick remainder <see cref="SeekToPosition"/> keeps — 14 bits,
	/// which is the whole of what the original's own Q14 scaling discards, so nothing is lost twice.
	/// </summary>
	private const int SeekFractionBits = 14;

	private readonly ShapeAnimation _animation;

	private short _sequence;
	private short _frame;
	private short _nextSequence;
	private short _nextFrame;

	// Frame the transition search stopped on. Reaching it hands playback to the transition fields
	// below instead of to the sequence's own next frame.
	private short _scanFrame = NoFrame;
	private short _transitionSequence;
	private short _transitionFrame;
	private short _transitionDuration;
	private short _transitionTransform;

	private short _targetSequence = NoFrame;
	private short _targetFrame = NoFrame;
	private short _targetFlags;

	private short _frameAccumulator;
	private short _frameDuration;

	// Sub-tick remainder of _frameAccumulator, in SeekFractionBits fixed point. Only a seek produces
	// one; playback zeroes it, and it is zero throughout on every thread that is only ever advanced.
	private int _frameAccumulatorFraction;

	private bool _hasGroundMotion;
	private short _groundX, _groundY, _groundZ;
	private short _groundRotationX, _groundRotationY, _groundRotationZ;

	private Transform3 _root = Transform3.Identity;
	private short _rate;
	private int _flags;

	public AnimationThread(ShapeAnimation animation) {
		_animation = animation;
		SetSequence(0, 0, 0);
		_root = Transform3.Identity;
	}

	/// <summary>The sequence currently playing.</summary>
	public int Sequence => _sequence;

	/// <summary>The frame currently playing.</summary>
	public int Frame => _frame;

	/// <summary>
	/// The frame playback will advance to next. It equals <see cref="Frame"/> only at the end of a
	/// sequence that does not loop, which is how a one-shot is known to have played out — see
	/// <c>MechObject.FallDown</c>.
	/// </summary>
	public int NextFrame => _nextFrame;

	/// <summary>The sequence <see cref="SetTarget"/> last asked for, or -1.</summary>
	public int TargetSequence => _targetSequence;

	/// <summary>Whether playback has reached its target sequence.</summary>
	public bool AtTarget => (_flags & FlagAtTarget) != 0;

	/// <summary>Whether a transition frame is currently playing.</summary>
	public bool InTransition => (_flags & FlagInTransition) != 0;

	/// <summary>
	/// Whether playback is inside one sequence rather than crossing into another. The gait state
	/// machine only acts while this holds; mid-change it freezes the speed scalar instead.
	/// </summary>
	public bool IsSettled => _sequence == _nextSequence;

	/// <summary>
	/// Playback rate as a Q8 multiplier on the timestep — negative plays backwards.
	/// <c>AnimThread_SetPlaybackRate</c> (<c>004795cc</c>): flipping the sign re-runs the transition search, since a transition that
	/// leads to the target playing forwards is not the one that leads there playing backwards.
	/// </summary>
	public short Rate {
		get => _rate;
		set {
			short previous = _rate;
			_rate = value;
			if ((value < 0) != (previous < 0)) {
				SetTarget(_targetSequence, _targetFrame, _targetFlags);
			}
		}
	}

	/// <summary>
	/// <c>AnimThread_SetSequence</c> (<c>004791a0</c>) — cuts straight to a sequence and frame, abandoning any target. Used by
	/// the gait state machine when it wants a sequence to start immediately rather than be
	/// transitioned into.
	/// </summary>
	public void SetSequence(int sequence, int frame, short accumulator) {
		_sequence = (short)sequence;
		_frame = (short)frame;
		_nextSequence = _sequence;

		var current = _animation.Sequences[_sequence];
		_nextFrame = (short)current.NextFrame(_frame);
		_frameAccumulator = accumulator;
		_frameAccumulatorFraction = 0;
		_frameDuration = current.FrameDurations[_frame];

		LoadGroundTransform(_sequence, _frame);

		_targetFrame = NoFrame;
		_targetSequence = NoFrame;
		_targetFlags = 0;
		_scanFrame = NoFrame;
		_flags = FlagAtTarget;
	}

	/// <summary>
	/// <c>AnimThread_SetTarget</c> (<c>00479570</c>) — asks playback to reach <paramref name="sequence"/>, via whatever
	/// transition leads there. <paramref name="frame"/> of -1 means any frame of it will do; bit 0
	/// of <paramref name="flags"/> makes playback stop dead on arrival instead of continuing.
	/// </summary>
	public void SetTarget(int sequence, int frame, short flags) {
		_targetSequence = (short)sequence;
		_targetFrame = (short)frame;
		_targetFlags = flags;
		_flags &= ~FlagAtTarget;

		if (_targetSequence != NoFrame
			&& (_sequence != _targetSequence || _frame != _targetFrame || _frameAccumulator != 0)) {
			FindTransition();
			return;
		}

		_flags |= FlagAtTarget;
		_scanFrame = NoFrame;
	}

	/// <summary>
	/// Drops any target and marks playback settled, leaving the current sequence running. The gait
	/// state machine does this inline in five places rather than through a helper.
	/// </summary>
	public void ClearTarget() {
		_scanFrame = NoFrame;
		_targetFrame = NoFrame;
		_targetSequence = NoFrame;
		_targetFlags = 0;
		_flags = FlagAtTarget;
	}

	/// <summary>
	/// The whole thread state, for the save/restore a blocked move needs — <c>SimObject_PushTransform</c> (<c>00402628</c>) and
	/// <c>SimObject_PopTransform</c> (<c>004027fc</c>) copy the thread's whole 0x52 bytes so a rejected step can be replayed
	/// without the animation having advanced twice. <see cref="FrameAccumulatorFraction"/> is this
	/// engine's own field and has no counterpart in them; it rides along so that the cursor
	/// round-trips whole.
	/// </summary>
	public readonly record struct State(
		short Sequence, short Frame, short NextSequence, short NextFrame, short ScanFrame,
		short TransitionSequence, short TransitionFrame, short TransitionDuration,
		short TransitionTransform, short TargetSequence, short TargetFrame, short TargetFlags,
		short FrameAccumulator, short FrameDuration, bool HasGroundMotion,
		short GroundX, short GroundY, short GroundZ,
		short GroundRotationX, short GroundRotationY, short GroundRotationZ,
		Transform3 Root, short Rate, int Flags, int FrameAccumulatorFraction);

	/// <summary>Captures the state <see cref="Restore"/> puts back.</summary>
	public State Capture() => new(
		_sequence, _frame, _nextSequence, _nextFrame, _scanFrame,
		_transitionSequence, _transitionFrame, _transitionDuration, _transitionTransform,
		_targetSequence, _targetFrame, _targetFlags, _frameAccumulator, _frameDuration,
		_hasGroundMotion, _groundX, _groundY, _groundZ,
		_groundRotationX, _groundRotationY, _groundRotationZ, _root, _rate, _flags,
		_frameAccumulatorFraction);

	/// <summary>Puts back a state captured by <see cref="Capture"/>.</summary>
	public void Restore(in State state) {
		_sequence = state.Sequence;
		_frame = state.Frame;
		_nextSequence = state.NextSequence;
		_nextFrame = state.NextFrame;
		_scanFrame = state.ScanFrame;
		_transitionSequence = state.TransitionSequence;
		_transitionFrame = state.TransitionFrame;
		_transitionDuration = state.TransitionDuration;
		_transitionTransform = state.TransitionTransform;
		_targetSequence = state.TargetSequence;
		_targetFrame = state.TargetFrame;
		_targetFlags = state.TargetFlags;
		_frameAccumulator = state.FrameAccumulator;
		_frameAccumulatorFraction = state.FrameAccumulatorFraction;
		_frameDuration = state.FrameDuration;
		_hasGroundMotion = state.HasGroundMotion;
		_groundX = state.GroundX;
		_groundY = state.GroundY;
		_groundZ = state.GroundZ;
		_groundRotationX = state.GroundRotationX;
		_groundRotationY = state.GroundRotationY;
		_groundRotationZ = state.GroundRotationZ;
		_root = state.Root;
		_rate = state.Rate;
		_flags = state.Flags;
	}

	/// <summary>
	/// <c>AnimThread_Advance</c> (<c>00479614</c>) — advances playback by <paramref name="delta"/> (Q8 animation ticks,
	/// scaled by <see cref="Rate"/>), crossing as many frames as that covers and committing each
	/// crossed frame's root motion as it goes.
	/// </summary>
	public void Advance(short delta) {
		if ((_flags & FlagAtTarget) != 0 && (_targetFlags & 1) != 0) {
			return;
		}

		short step = (short)(((long)delta * _rate) >> 8);
		if (step == 0) {
			return;
		}

		// A thread that is advancing is no longer parked where a seek put it, and every path below
		// moves the accumulator on its own terms. Nothing in the engine both seeks and advances one
		// thread — the torso's two never advance, the locomotion one is never seeked — so this is
		// hygiene rather than a case that arises.
		_frameAccumulatorFraction = 0;

		step = (short)(_frameAccumulator + step);

		// Playing backwards off the front of a sequence that does not wrap: hold at frame 0.
		if (step < 0 && _frame == 0 && _frame != _scanFrame
			&& _animation.Sequences[_sequence].PreviousFrame(_frame) == 0) {
			step = 0;
		}

		if (step == 0) {
			_frameAccumulator = 0;
			if ((_flags & FlagInTransition) != 0) {
				LandFromTransition();
			}
			NoteTargetReached();
			return;
		}

		if (step < _frameDuration) {
			if (step >= 0) {
				_frameAccumulator = step;
				return;
			}

			StepBackward(step);
			return;
		}

		StepForward(step);
	}

	/// <summary>
	/// <c>AnimThread_ReadRootTransform</c> (<c>00478fa8</c>) — the root transform as it stands part way through the current frame:
	/// the stored transform with the elapsed fraction of this frame's ground motion applied.
	/// </summary>
	public Transform3 ReadRoot() {
		if (!_hasGroundMotion) {
			return _root;
		}

		return Transform3.Concat(ScaledGroundTransform(), _root);
	}

	/// <summary>
	/// <c>AnimThread_WriteRootTransform</c> (<c>00479088</c>) — the inverse of <see cref="ReadRoot"/>: stores whatever transform would
	/// make <see cref="ReadRoot"/> return <paramref name="value"/> right now. Seeding identity here
	/// and reading back after <see cref="Advance"/> gives the step's own displacement and nothing
	/// else.
	/// </summary>
	public void WriteRoot(in Transform3 value) {
		if (!_hasGroundMotion) {
			_root = value;
			return;
		}

		var scaled = ScaledGroundTransform();
		scaled.TransposeRotation();
		var moved = scaled.RotateVector(-scaled.X, -scaled.Y, -scaled.Z);
		scaled.X = moved.X;
		scaled.Y = moved.Y;
		scaled.Z = moved.Z;

		_root = Transform3.Concat(scaled, value);
	}

	/// <summary>
	/// <c>AnimThread_SeekToPosition</c> (<c>00479238</c>) — parks playback at a <i>fraction</i> of a
	/// sequence rather than playing through it: <paramref name="position"/> is Q14 across the
	/// sequence's whole duration, and this finds the frame and intra-frame offset that lands on.
	///
	/// <para>This is how the torso is aimed. Its threads never advance — the mech constructor gives
	/// them a rate of zero — and the twist and pitch sequences are one full sweep of their node, so
	/// setting a position in the sequence <i>is</i> setting an angle. See
	/// <see cref="MechObject.TorsoTwistTick"/>.</para>
	///
	/// <para>Not retail, with <paramref name="keepFraction"/> set — the
	/// <see cref="Settings.TweakSettingDefinitions.SmootherTurretMovement"/> tweak: the sub-tick
	/// remainder the original's Q14 scale-down truncates away is kept, and
	/// <see cref="FrameFraction"/> spends it, so a seeked pose is no longer quantised to a whole
	/// animation tick (docs/retail/simulation/torso-aim.md, "Angle to pose"). On OUTLAW that is
	/// 1747 drawn twist poses from centre to limit where retail has 170, and the drawn view moves
	/// every tick from half stick up rather than from three quarters. Clear, it is the original's
	/// arithmetic exactly. Playback is untouched: only a seek produces a remainder, and the
	/// locomotion thread is never seeked.</para>
	///
	/// <para>What stepping is left comes from the cosine table, not the animation.
	/// <see cref="SimTrig.Cos"/> quantises a rotation to its 16-BAM step, so the drawn view still moves
	/// in 0.101° increments and a twist under about 2°/s still steps. The Q10 <see cref="FrameFraction"/>
	/// is fine enough to carry the fraction past that point: one Q10 unit is 8 binary angle across a
	/// torso sweep's 45° keyframe interval.</para>
	/// </summary>
	/// <param name="keepFraction">Keep the sub-tick remainder; false is the original's truncation.</param>
	public void SeekToPosition(int sequence, short position, bool keepFraction) {
		var target = _animation.Sequences[sequence];

		int total = 0;
		foreach (short duration in target.FrameDurations) {
			total += duration;
		}

		// The original walks off the end of the frame list if this ever exceeds the total; it cannot,
		// since position is a 14-bit fraction and this scales it by total - 1. The original's own
		// Q14Multiply is that product shifted down; the shift is taken apart here so the bits it
		// throws away can be kept.
		int scaled = position * (short)(total - 1);
		int remaining = scaled >> SeekFractionBits;
		// The tweak keeps the sub-tick remainder rather than truncating to a whole animation tick.
		int fraction = keepFraction ? scaled & ((1 << SeekFractionBits) - 1) : 0;

		int frame = 0;
		while (frame < target.FrameCount - 1 && remaining >= target.FrameDurations[frame]) {
			remaining = (ushort)(remaining - target.FrameDurations[frame]);
			frame++;
		}

		SetSequence(sequence, frame, (short)remaining);

		// After SetSequence, which clears the fraction along with the rest of the cursor.
		_frameAccumulatorFraction = fraction;
	}

	/// <summary>
	/// This thread's contribution to one node: the interpolated local transform when the playing
	/// sequence animates that node, nothing when it does not.
	///
	/// <para>A shape carries several threads at once — a HERC has three, one for locomotion and one
	/// each for torso twist and pitch — and each writes only the nodes its own sequence covers.
	/// <see cref="ShapeInstance"/> is what puts them together; this is one thread's answer.</para>
	/// </summary>
	public bool TryGetLocal(int transformId, out AnimTransform local) {
		local = default;

		int index = AnimatedIndexOf(_animation.Sequences[_sequence], _frame, transformId);
		if (index < 0 || index >= _animation.Transforms.Length) {
			return false;
		}

		local = InterpolatedLocal(index, _animation.Sequences[_nextSequence], transformId,
			FrameFraction());
		return true;
	}

	/// <summary>
	/// One node's local transform part way between the frame playing and the frame playback is headed
	/// for — <c>AnimThread_EvalNodeLocals</c> (<c>004799a4</c>)'s inner loop.
	///
	/// <para>Where the two frames name the same pool entry the entry stands as it is, which is the
	/// original's own short-circuit and not merely an optimisation: identical indices are identical
	/// transforms, so blending them would only cost rounding.</para>
	/// </summary>
	private AnimTransform InterpolatedLocal(int index, AnimSequence upcoming, int transformId,
			int fraction) {
		int nextIndex = TransformIndexOf(upcoming, _nextFrame, transformId);
		if (nextIndex == index || nextIndex < 0 || nextIndex >= _animation.Transforms.Length) {
			return _animation.Transforms[index];
		}

		return AnimTransform.Blend(_animation.Transforms[index], _animation.Transforms[nextIndex],
			fraction);
	}

	/// <summary>
	/// How far through the current frame playback stands, in Q10 (0x400 == a whole frame) with the
	/// divide rounded — <c>AnimThread_EvalNodeLocals</c> (<c>004799a4</c>)'s <c>(elapsed * 0x400 + duration / 2) / duration</c>.
	///
	/// <para>This is the same elapsed fraction <see cref="ScaledGroundTransform"/> ramps ground motion
	/// by. That both the pose and the ground movement ride the one fraction is the whole reason the
	/// original looks smooth at any speed: a slow walk stretches the keyframes out in time, and the
	/// pose keeps moving between them rather than stepping.</para>
	/// </summary>
	private int FrameFraction() {
		if (_frameDuration == 0) {
			return 0;
		}

		if (_frameAccumulatorFraction == 0) {
			return (_frameAccumulator * 0x400 + _frameDuration / 2) / _frameDuration;
		}

		// The same expression with both terms scaled up by the fraction's own bits. It is kept
		// separate from the line above rather than replacing it because the two disagree by one Q10
		// unit on an odd duration, where the original's `duration / 2` rounding bias truncates and
		// this one does not — and every advanced thread must keep the original's answer.
		long elapsed = ((long)_frameAccumulator << SeekFractionBits) + _frameAccumulatorFraction;
		long duration = (long)_frameDuration << SeekFractionBits;
		return (int)((elapsed * 0x400 + duration / 2) / duration);
	}

	/// <summary>
	/// Which entry of the transform pool a node holds on one frame: that frame's if the playing
	/// sequence animates it, its default otherwise. -1 when it is neither, which is a node the shape
	/// never places.
	/// </summary>
	private int TransformIndexOf(AnimSequence sequence, int frame, int transformId) {
		int index = AnimatedIndexOf(sequence, frame, transformId);
		if (index >= 0) {
			return index;
		}

		var defaults = _animation.DefaultTransforms;
		return transformId >= 0 && transformId < defaults.Length ? defaults[transformId] : -1;
	}

	/// <summary>
	/// Which entry of the transform pool <paramref name="sequence"/> gives a node on one frame, or
	/// -1 when the sequence does not animate that node at all — no fall back to its default, which
	/// is what lets a caller tell "this thread poses this node" from "it does not".
	/// </summary>
	private static int AnimatedIndexOf(AnimSequence sequence, int frame, int transformId) {
		// Part id 0 is skipped, as AnimThread_EvalNodeLocals (004799a4) skips it: column 0 of every sequence carries the
		// sequence's *root motion*, not a pose, and the original never writes it into the node array
		// — that node keeps its default. Without this a caller asking for node 0 gets the ramped
		// ground displacement back as though it were a pose, which is the same displacement the
		// object's own position already carries.
		int column = transformId > 0 ? sequence.ColumnOf(transformId) : -1;
		if (column < 0) {
			return -1;
		}

		int offset = frame * sequence.PartCount + column;
		return offset >= 0 && offset < sequence.TransformIndices.Length
			? sequence.TransformIndices[offset]
			: -1;
	}

	/// <summary>The whole of the current frame's ground motion, unscaled.</summary>
	private Transform3 GroundTransform() {
		var transform = Transform3.FromEuler(_groundRotationX, _groundRotationY, _groundRotationZ);
		transform.X = _groundX;
		transform.Y = _groundY;
		transform.Z = _groundZ;
		return transform;
	}

	/// <summary>
	/// The fraction of the current frame's ground motion that has elapsed. Both the rotation and the
	/// translation are scaled linearly and truncated, exactly as the original does — which is what
	/// makes a HERC's ground speed pulse within a stride rather than hold steady.
	/// </summary>
	private Transform3 ScaledGroundTransform() {
		int elapsed = _frameAccumulator;
		int duration = _frameDuration;
		if (duration == 0) {
			// A zero-length frame has no elapsed fraction to scale. The original divides
			// unconditionally here, so this state cannot arise in it; the limit is the identity.
			return Transform3.Identity;
		}

		var transform = Transform3.FromEuler(
			(short)(_groundRotationX * elapsed / duration),
			(short)(_groundRotationY * elapsed / duration),
			(short)(_groundRotationZ * elapsed / duration));
		transform.X = _groundX * elapsed / duration;
		transform.Y = _groundY * elapsed / duration;
		transform.Z = _groundZ * elapsed / duration;
		return transform;
	}

	/// <summary><c>AnimThread_CommitGroundMovement</c> (<c>00478e60</c>) — folds a whole frame's ground motion into the stored transform.</summary>
	private void CommitFrameForward() {
		if (_hasGroundMotion) {
			_root = Transform3.Concat(GroundTransform(), _root);
		}
	}

	/// <summary><c>AnimThread_UncommitGroundMovement</c> (<c>00478ee8</c>) — the same, undone, for backward playback.</summary>
	private void CommitFrameBackward() {
		if (!_hasGroundMotion) {
			return;
		}

		var transform = GroundTransform();
		transform.TransposeRotation();
		var moved = transform.RotateVector(-transform.X, -transform.Y, -transform.Z);
		transform.X = moved.X;
		transform.Y = moved.Y;
		transform.Z = moved.Z;

		_root = Transform3.Concat(transform, _root);
	}

	/// <summary><c>AnimThread_LoadFrameGroundMovement</c> (<c>00478de8</c>) — loads a frame's root-part transform as this frame's ground motion.</summary>
	private void LoadGroundTransform(int sequence, int frame) {
		var current = _animation.Sequences[sequence];
		if (!current.GroundMovement) {
			_hasGroundMotion = false;
			return;
		}

		// Part index 0 is the root; the sequence's transform indices are row-major by frame.
		SetGroundTransform(current.TransformIndices[frame * current.PartCount]);
	}

	private void SetGroundTransform(int transformIndex) {
		var transform = _animation.Transforms[transformIndex];
		_groundX = transform.X;
		_groundY = transform.Y;
		_groundZ = transform.Z;
		_groundRotationX = transform.RotationX;
		_groundRotationY = transform.RotationY;
		_groundRotationZ = transform.RotationZ;
		_hasGroundMotion = true;
	}

	private void LandFromTransition() {
		var current = _animation.Sequences[_sequence];
		_nextSequence = _sequence;
		_nextFrame = (short)current.NextFrame(_frame);
		_frameDuration = current.FrameDurations[_frame];
		_flags &= ~FlagInTransition;
		LoadGroundTransform(_sequence, _frame);
	}

	private void NoteTargetReached() {
		if (_sequence == _targetSequence && (_targetFrame == _frame || _targetFrame == NoFrame)) {
			_flags |= FlagAtTarget;
		}
	}

	private void StepForward(short step) {
		while (step != 0 && _frameDuration <= step) {
			CommitFrameForward();
			_sequence = _nextSequence;
			_frame = _nextFrame;

			if (_nextFrame == _scanFrame) {
				_nextSequence = _transitionSequence;
				_nextFrame = _transitionFrame;
				step = (short)(step - _frameDuration);
				_frameDuration = _transitionDuration;
				_frameAccumulator = 0;
				_scanFrame = NoFrame;
				_flags |= FlagInTransition;
				SetGroundTransform(_transitionTransform);
			} else {
				_nextFrame = (short)_animation.Sequences[_nextSequence].NextFrame(_nextFrame);
				step = (short)(step - _frameDuration);

				short duration = _animation.Sequences[_sequence].FrameDurations[_frame];
				_frameDuration = duration;
				if (duration == 0) {
					step = 0;
				}

				_frameAccumulator = 0;
				_flags &= ~FlagInTransition;
				LoadGroundTransform(_sequence, _frame);
			}

			if (_sequence == _targetSequence && (_targetFrame == _frame || _targetFrame == NoFrame)) {
				_flags |= FlagAtTarget;
				if ((_targetFlags & 1) != 0) {
					step = 0;
				}
			}
		}

		_frameAccumulator = step;
	}

	private void StepBackward(short step) {
		while (step < 0) {
			_nextSequence = _sequence;
			_nextFrame = _frame;

			if (_frame == _scanFrame) {
				_sequence = _transitionSequence;
				_frame = _transitionFrame;
				_frameDuration = _transitionDuration;
				_scanFrame = NoFrame;
				_flags |= FlagInTransition;
				SetGroundTransform(_transitionTransform);
			} else {
				_frame = (short)_animation.Sequences[_sequence].PreviousFrame(_frame);
				_frameDuration = _animation.Sequences[_sequence].FrameDurations[_frame];
				_flags &= ~FlagInTransition;
				LoadGroundTransform(_sequence, _frame);
			}

			CommitFrameBackward();

			if (-_frameDuration == step || -step < _frameDuration) {
				_frameAccumulator = (short)(step + _frameDuration);
				step = 0;
			} else {
				_frameAccumulator = 0;
				step = (short)(step + _frameDuration);
			}

			if (_sequence == _targetSequence && (_targetFrame == _frame || _targetFrame == NoFrame)) {
				_flags |= FlagAtTarget;
				if ((_targetFlags & 1) != 0) {
					step = 0;
					_frameAccumulator = 0;
				}
			}
		}
	}

	/// <summary>
	/// <c>AnimThread_FindTransition</c> (<c>004792c8</c>) — walks the current sequence's frames looking for one that offers a
	/// transition into the target sequence, and arms it. Gives up after a full lap, leaving
	/// <see cref="_scanFrame"/> at -1 so playback simply continues where it is.
	/// </summary>
	private void FindTransition() {
		bool backward = _rate < 0;
		short start = backward ? _frame : _nextFrame;
		_scanFrame = start;

		do {
			var current = _animation.Sequences[_sequence];
			int first = current.FirstTransitions[_scanFrame];
			int remaining = current.TransitionCounts[_scanFrame];

			for (; remaining != 0; remaining--, first++) {
				var transition = _animation.Transitions[first];
				if (transition.DestinationSequence != _targetSequence) {
					continue;
				}

				_transitionDuration = transition.Duration;
				_transitionSequence = _targetSequence;
				_transitionFrame = transition.DestinationFrame;
				_transitionTransform = transition.TransformIndex;

				if (_targetFrame == NoFrame) {
					return;
				}

				// Only accept a transition whose landing frame is on the near side of the target,
				// so playback still runs into it rather than past it.
				bool reachesTarget = backward
					? _transitionFrame > _targetFrame
						|| (_transitionFrame == _targetFrame && _frameAccumulator == 0)
					: _transitionFrame < _targetFrame
						|| (_transitionFrame == _targetFrame && _frameAccumulator == 0);

				if (reachesTarget) {
					return;
				}
			}

			short previous = _scanFrame;
			_scanFrame = (short)(backward
				? current.PreviousFrame(_scanFrame)
				: current.NextFrame(_scanFrame));

			if (previous == _scanFrame || _scanFrame == start) {
				break;
			}
		} while (true);

		_scanFrame = NoFrame;
	}
}
