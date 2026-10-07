using Herculan.Engine.Numerics;

namespace Herculan.Engine.Shell;

/// <summary>
/// The intro, <c>ShellMap_IntroStep</c>: the state machine that runs the first time the briefing comes
/// up — the full view held, the zoom to the squad, the pan along the nav path with the briefing's
/// lines, and the zoom back out — and the slow first paint that opens it.
/// </summary>
public sealed partial class ShellMap {
	/// <summary><c>+0x172</c>. Named where the value has one meaning; the rest are the switch's own numbers.</summary>
	private enum State : short {
		Hold = 0,
		ZoomInStart = 1,
		ZoomIn = 2,
		SquadWait = 3,
		SquadNext = 4,
		NextStop = 5,
		PanStart = 6,
		Pan = 7,
		PanEnd = 8,
		LineAfterPan = 9,
		LineStep = 10,
		LineStepB = 11,
		LineTimed = 12,
		Wait = 13,
		WaitE = 14,
		WaitF = 15,
		Leftover = 16,
		ZoomOutStart = 17,
		ZoomOut = 18,
		Done = 19,
	}

	/// <summary><c>DAT_00471c44</c> and <c>DAT_00471c48</c>: how long the full view holds, and how long each zoom takes, in timer ticks.</summary>
	private const int HoldTicks = 60;
	private const int ZoomTicks = 60;

	/// <summary><c>DAT_00471c4c</c>, between squad members, and <c>DAT_00471c50</c>, the pauses around the path.</summary>
	private const int SquadTicks = 15;
	private const int PauseTicks = 15;

	/// <summary><c>DAT_00471c54</c> and <c>DAT_00471c58</c>: a pan takes 15 ticks for every whole 50000 of distance, and 15 more.</summary>
	private const int PanDistanceUnit = 50000;
	private const int PanTicksPerUnit = 15;

	/// <summary><c>DAT_00471c98</c> and <c>DAT_00471c9c</c>, the pause between lines and the time per character; both 0 in the image.</summary>
	private const int LineTicks = 0;
	private const int CharacterTicks = 0;

	/// <summary><c>DAT_00471c2c</c>, <c>DAT_00471c38</c> and <c>DAT_00471c40</c>: the first paint's waits after the clear, after the relief and after each grid line.</summary>
	private const int SlowPaintClearWait = 10;
	private const int SlowPaintReliefWait = 10;

	private State _state = State.Hold;
	private State _next;
	private uint _deadline;
	private uint? _slowPaintStart;
	private bool _skip;
	private bool _finished;
	private int _doneCountdown = 2;
	private int _squadShown;
	private int _pathShown;
	private int _markersShown;
	private int _textIndex;
	private int _stop;
	private int _stopsReached;
	private bool _textFlag;
	private int _panTicks;
	private (int X, int Y, int Z) _from;
	private (int X, int Y, int Z) _to;
	private (int X, int Y, int Z) _step;

	/// <summary>Whether the intro still has frames to run — <c>ShellMap_IntroStep</c>'s return, false once <c>+0x17a</c> is set.</summary>
	public bool IntroRunning => !_finished;

	/// <summary>
	/// A click or <c>Esc</c> or <c>Space</c> during the intro, which <c>ShellMap_SkipIntro</c> (<c>004253ef</c>) answers by jumping
	/// to the closing zoom. It waits until the intro's first paint has finished, as the original's does.
	/// </summary>
	public void Skip() => _skip = true;

	/// <summary>
	/// One pass of <c>ShellMap_IntroStep</c>, the intro's state machine, at <paramref name="now"/> in the
	/// original's timer ticks (<c>GetTickCount() &gt;&gt; 4</c>). The caller paints after every pass.
	/// Returns whether the intro is still running.
	/// </summary>
	public bool Advance(uint now) {
		if (_slowPaintStart is { } start) {
			if (now - start < SlowPaintClearWait + SlowPaintReliefWait + GridLineCount()) {
				return true;
			}

			_slowPaintStart = null;
		}

		if (_skip && _state < State.ZoomOut) {
			_state = State.ZoomOutStart;
		}

		_skip = false;
		Step(now);
		return !_finished;
	}

	private int GridLineCount() {
		var scratch = new ShellSurface(1, 1);
		return PaintGrid(scratch, Clamp((CameraX, CameraY, CameraZ)), 0);
	}

	private void Step(uint now) {
		switch (_state) {
			case State.Hold:
				if (_deadline == 0) {
					_deadline = now + HoldTicks;
					(CameraX, CameraY, CameraZ) = FullView;
					_slowPaintStart = now;
					return;
				}

				if (now < _deadline) {
					return;
				}

				_state = State.ZoomInStart;
				goto case State.ZoomInStart;
			case State.ZoomInStart:
				_deadline += ZoomTicks;
				StartMove(SquadView, ZoomTicks);
				_state = State.ZoomIn;
				goto case State.ZoomIn;
			case State.ZoomIn:
				if (!Move(now, ZoomTicks)) {
					_state = State.SquadNext;
				}

				return;
			case State.SquadWait:
				if (now < _deadline) {
					return;
				}

				_squadShown++;
				goto case State.SquadNext;
			case State.SquadNext:
				if (_squadShown < _squadCount) {
					_deadline = now + SquadTicks;
					_state = State.SquadWait;
				} else {
					_stopsReached = 1;
					_textIndex = 0;
					_stop = 0;
					_next = State.NextStop;
					_deadline = now + PauseTicks;
					_state = State.WaitF;
				}

				return;
			case State.NextStop:
				if (_textCount <= _textIndex) {
					goto case State.PanStart;
				}

				if (_path == null || _stopsReached < _path.Length) {
					_deadline = now + LineTicks;
					_next = State.PanStart;
					_state = State.Wait;
					return;
				}

				_textIndex--;
				_textFlag = true;
				goto case State.Leftover;
			case State.Leftover:
				if (_textIndex < _textCount) {
					if (!_textFlag) {
						_textIndex++;
						_next = State.Leftover;
						_state = State.LineTimed;
						_textFlag = true;
					} else {
						_deadline = now + LineTicks;
						_next = State.Leftover;
						_state = State.WaitE;
						_textFlag = false;
					}
				} else {
					_state = State.NextStop;
				}

				return;
			case State.PanStart:
				_pathShown++;
				_markersShown++;
				_stop++;
				_stopsReached++;
				if (_path == null || _path.Length < _stopsReached) {
					_state = State.ZoomOutStart;
					return;
				}

				var target = Point(_path[Math.Min(_stop, _path.Length - 1)]);
				int distance = SimMath.FastMagnitude3D(CameraX - target.X, CameraY - target.Y, 0);
				_panTicks = (distance / PanDistanceUnit + 1) * PanTicksPerUnit;
				StartMove((target.X, target.Y, CameraZ), _panTicks);
				_deadline = now + (uint)_panTicks;
				_state = State.Pan;
				goto case State.Pan;
			case State.Pan:
				if (Move(now, _panTicks)) {
					return;
				}

				_state = State.PanEnd;
				goto case State.PanEnd;
			case State.PanEnd:
				// With no time per character, a line never outlasts the pan and this waits for nothing.
				goto case State.LineAfterPan;
			case State.LineAfterPan:
				_textIndex++;
				if (_textIndex < _textCount) {
					_deadline = now + LineTicks;
					_next = State.LineStep;
					_state = State.WaitE;
				} else if (_textIndex == _textCount && _path != null && _stopsReached < _path.Length) {
					_textIndex--;
					_state = State.NextStop;
				} else {
					_deadline = now + PauseTicks;
					_next = State.LineStep;
					_state = State.WaitF;
				}

				return;
			case State.LineStep:
				if (_textIndex < _textCount) {
					_next = State.LineStepB;
					_state = State.LineTimed;
					return;
				}

				goto case State.LineStepB;
			case State.LineStepB:
				if (_textIndex < _textCount) {
					_textIndex++;
					if (_textIndex < _textCount - 1) {
						_deadline = now + LineTicks;
						_next = State.NextStop;
						_state = State.WaitE;
					} else {
						_state = State.NextStop;
					}
				} else {
					_state = State.NextStop;
				}

				return;
			case State.LineTimed:
				if (_textIndex < _textCount) {
					_deadline = now + CharacterTicks;
					_state = State.Wait;
				}

				goto case State.Wait;
			case State.Wait:
			case State.WaitE:
			case State.WaitF:
				if (now >= _deadline) {
					_state = _next;
				}

				return;
			case State.ZoomOutStart:
				_squadShown = (byte)_squadCount;
				if (_path != null) {
					_pathShown = (byte)_path.Length;
					_markersShown = (byte)(_pathShown - 1);
				}

				_deadline = now + ZoomTicks;
				StartMove(FullView, ZoomTicks);
				_state = State.ZoomOut;
				goto case State.ZoomOut;
			case State.ZoomOut:
				if (!Move(now, ZoomTicks)) {
					_state = State.Done;
				}

				return;
			case State.Done:
				if (!_finished && --_doneCountdown == 0) {
					_finished = true;
				}

				return;
		}
	}

	/// <summary>A move to <paramref name="to"/> over <paramref name="ticks"/>, stepped by whole-tick fractions computed once, as the originals are.</summary>
	private void StartMove((int X, int Y, int Z) to, int ticks) {
		_from = (CameraX, CameraY, CameraZ);
		_to = to;
		_step = ticks == 0 ? (0, 0, 0) : ((to.X - _from.X) / ticks, (to.Y - _from.Y) / ticks, (to.Z - _from.Z) / ticks);
	}

	/// <summary>Where the move is at <paramref name="now"/>, landing exactly on its target once its deadline passes. Returns whether it is still under way.</summary>
	private bool Move(uint now, int ticks) {
		if (now < _deadline) {
			int elapsed = ticks - (int)(_deadline - now);
			(CameraX, CameraY, CameraZ) = (_from.X + _step.X * elapsed, _from.Y + _step.Y * elapsed, _from.Z + _step.Z * elapsed);
			return true;
		}

		(CameraX, CameraY, CameraZ) = _to;
		return false;
	}
}
