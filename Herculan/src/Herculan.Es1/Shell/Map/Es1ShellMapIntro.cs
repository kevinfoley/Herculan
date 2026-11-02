using Herculan.Engine.Numerics;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// <c>ShellMap_IntroStep</c> (<c>00009cfb</c>)(map) (docs/es1/shell/map.md#the-intro), and the globals it keeps from one
/// step to the next: the camera move's start <c>ShellMap_IntroFrom</c> (<c>0009a4ec</c>), end <c>ShellMap_IntroTo</c>
/// (<c>0009a4fc</c>) and step <c>ShellMap_IntroDelta</c> (<c>0009a4dc</c>), the leg's ticks
/// <c>ShellMap_IntroLegTicks</c> (<c>0009a50c</c>), the waypoint number <c>ShellMap_IntroWaypoint</c> (<c>0009a510</c>),
/// the ref pointer <c>ShellMap_IntroWaypointRef</c> (<c>0009a508</c>) and the byte <c>ShellMap_IntroLineWait</c>
/// (<c>0009a512</c>). They are the image's, so they outlive a map: the shell keeps one of these for its run.
/// </summary>
public sealed class Es1ShellMapIntro {
	private (int X, int Y, int Z) _from, _to, _delta;
	private int _legTicks;
	private short _waypoint;
	private bool _lineWait;

	/// <summary>The ref pointer as an index into the nav path's refs; none while the map had no path.</summary>
	private int? _waypointRef;

	/// <summary>
	/// One step, by the state <see cref="Es1ShellMap.IntroState"/> (docs/es1/shell/map.md#the-intro); every paint is
	/// <see cref="Es1ShellMapPaint.Paint"/>, slot <c>+4</c>, on <paramref name="display"/>'s draw page, and every deadline
	/// test reads <c>ShellMap_Ticks</c> afresh, unsigned. After the step, with the intro over,
	/// <see cref="Es1ShellMap.RedrawDue"/> is cleared. A move's tick count of 0, where retail divides by 0, gives a step of
	/// 0, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> Step(Es1ShellMap map, Es1VideoMemory display) {
		var bnd = map.Bnd;
		var lines = map.ObjectiveLines;
		switch (map.IntroState) {
			case 0:
				if (map.IntroDeadline == 0) {
					// The slow paint on the visible page, which VGA8_SetDrawPage makes the draw page, then the paint again on
					// the draw page it put back.
					int kept = display.DrawPage;
					display.DrawPage = display.VisiblePage;
					(map.CameraX, map.CameraY, map.Altitude) = map.FullView;
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					display.DrawPage = kept;
					map.SlowPaint = false;
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					map.IntroDeadline = After(map, bnd.FullViewHold);
					break;
				}

				if (Before(map, map.IntroDeadline)) {
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					break;
				}

				map.IntroState = 1;
				goto case 1;
			case 1:
				map.IntroDeadline = unchecked(map.IntroDeadline + bnd.ZoomTicks);
				StartMove(Camera(map), map.SquadView, bnd.ZoomTicks);
				map.IntroState = 2;
				goto case 2;
			case 2:
				bool moving = Before(map, map.IntroDeadline);
				if (moving) {
					MoveCamera(map, bnd.ZoomTicks);
				} else {
					(map.CameraX, map.CameraY, map.Altitude) = _to;
				}

				foreach (var wait in Paint(map, display)) {
					yield return wait;
				}

				if (!moving) {
					map.IntroState = 4;
				}

				break;
			case 3:
				if (Before(map, map.IntroDeadline)) {
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					break;
				}

				map.RevealedSquad = unchecked((byte)(map.RevealedSquad + 1));
				goto case 4;
			case 4:
				foreach (var wait in Paint(map, display)) {
					yield return wait;
				}

				if (map.RevealedSquad < map.SquadSize) {
					map.IntroDeadline = After(map, bnd.MemberTicks);
					map.IntroState = 3;
				} else {
					_waypoint = 1;
					map.IntroLine = 0;
					_waypointRef = map.NavPath == null ? null : 0;
					Wait(map, After(map, bnd.SectionPause), 0xf, 5);
				}

				break;
			case 5:
				if (map.IntroLine >= lines.Count) {
					goto case 6;
				}

				if (map.NavPath is not { } stepPath || _waypoint < stepPath.Length) {
					Wait(map, After(map, bnd.StepPause), 0xd, 6);
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					break;
				}

				map.IntroLine--;
				_lineWait = true;
				goto case 0x10;
			case 0x10:
				if (map.IntroLine >= lines.Count) {
					map.IntroState = 5;
					break;
				}

				if (_lineWait && Line(map, map.IntroLine).Length != 0) {
					Wait(map, After(map, bnd.StepPause), 0xe, 0x10);
					_lineWait = false;
					break;
				}

				map.IntroLine++;
				while (map.IntroLine < lines.Count && Line(map, map.IntroLine).Length == 0) {
					map.IntroLine++;
				}

				map.IntroResumeState = 0x10;
				map.IntroState = 0xc;
				_lineWait = true;
				break;
			case 6:
				map.RevealedPath = unchecked((byte)(map.RevealedPath + 1));
				map.RevealedMarkers = unchecked((byte)(map.RevealedMarkers + 1));
				_waypointRef++;
				_waypoint++;
				if (map.NavPath is not { } path || path.Length < _waypoint) {
					map.IntroState = 0x11;
					break;
				}

				// The point of the ref the pointer stands on, at the camera's altitude; a ref past the path, which retail reads
				// past it, takes the point of ref −1, an engine guard.
				int index = _waypointRef ?? 0;
				var point = map.PointAt(index < path.Length ? path[index] : -1);
				var target = (point.X, point.Y, map.Altitude);
				int distance = SimMath.FastMagnitude3D(unchecked(map.CameraX - target.X), unchecked(map.CameraY - target.Y), 0);
				_legTicks = unchecked((distance / bnd.LegDistanceUnit + 1) * bnd.LegTicksPerUnit);
				StartMove(Camera(map), target, _legTicks);
				map.IntroDeadline = After(map, _legTicks);
				map.IntroState = 7;
				goto case 7;
			case 7:
				if (Before(map, map.IntroDeadline)) {
					MoveCamera(map, _legTicks);
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}

					break;
				}

				(map.CameraX, map.CameraY, map.Altitude) = _to;
				foreach (var wait in Paint(map, display)) {
					yield return wait;
				}

				map.IntroState = 8;
				goto case 8;
			case 8:
				if (map.IntroLine < lines.Count) {
					int ticks = unchecked((short)Line(map, map.IntroLine).Length * bnd.TicksPerChar - _legTicks);
					if (ticks > 0) {
						Wait(map, After(map, ticks), 0xd, 9);
						break;
					}
				}

				goto case 9;
			case 9:
				map.IntroLine++;
				if (map.IntroLine < lines.Count) {
					Wait(map, After(map, bnd.StepPause), 0xe, 10);
				} else if (map.IntroLine == lines.Count && map.NavPath is { } legPath && _waypoint < legPath.Length) {
					map.IntroLine--;
					map.IntroState = 5;
				} else {
					Wait(map, After(map, bnd.SectionPause), 0xf, 10);
				}

				break;
			case 10:
				if (map.IntroLine < lines.Count) {
					map.IntroResumeState = 0xb;
					map.IntroState = 0xc;
					break;
				}

				goto case 0xb;
			case 0xb:
				if (map.IntroLine < lines.Count) {
					map.IntroLine++;
					Wait(map, After(map, bnd.StepPause), 0xe, 5);
				} else {
					map.IntroState = 5;
				}

				break;
			case 0xc:
				if (map.IntroLine < lines.Count) {
					map.IntroDeadline = After(map, unchecked(Line(map, map.IntroLine).Length * bnd.TicksPerChar));
					map.IntroState = 0xd;
				}

				goto case 0xd;
			case 0xd:
			case 0xe:
			case 0xf:
				if (Before(map, map.IntroDeadline)) {
					foreach (var wait in Paint(map, display)) {
						yield return wait;
					}
				} else {
					map.IntroState = map.IntroResumeState;
				}

				break;
			case 0x11:
				map.SlowPaint = false;
				map.RevealedSquad = unchecked((byte)map.SquadSize);
				if (map.NavPath is { } revealed) {
					map.RevealedPath = unchecked((byte)revealed.Length);
					map.RevealedMarkers = unchecked((byte)(map.RevealedPath - 1));
				}

				map.IntroDeadline = After(map, bnd.ZoomTicks);
				StartMove(Camera(map), map.FullView, bnd.ZoomTicks);
				map.IntroState = 0x12;
				goto case 0x12;
			case 0x12:
				if (Before(map, map.IntroDeadline)) {
					MoveCamera(map, bnd.ZoomTicks);
				} else {
					(map.CameraX, map.CameraY, map.Altitude) = _to;
					map.IntroState = 0x13;
				}

				foreach (var wait in Paint(map, display)) {
					yield return wait;
				}

				break;
			case 0x13:
				foreach (var wait in Paint(map, display)) {
					yield return wait;
				}

				if (!map.IntroEnded) {
					map.ClosingPaints = unchecked((byte)(map.ClosingPaints - 1));
					if (map.ClosingPaints == 0) {
						map.IntroEnded = true;
					}
				}

				break;
		}

		if (map.IntroEnded) {
			map.RedrawDue = false;
		}
	}

	private static IEnumerable<Es1ShellWait> Paint(Es1ShellMap map, Es1VideoMemory display) => Es1ShellMapPaint.Paint(map, display);

	private static (int X, int Y, int Z) Camera(Es1ShellMap map) => (map.CameraX, map.CameraY, map.Altitude);

	/// <summary>The move from <paramref name="from"/> to <paramref name="to"/>: the step each axis's difference over <paramref name="ticks"/>, truncated.</summary>
	private void StartMove((int X, int Y, int Z) from, (int X, int Y, int Z) to, int ticks) {
		_from = from;
		_to = to;
		_delta = unchecked((Divide(to.X - from.X, ticks), Divide(to.Y - from.Y, ticks), Divide(to.Z - from.Z, ticks)));

		static int Divide(int a, int b) => b == 0 ? 0 : a / b;
	}

	/// <summary>The camera at the move's start plus its step times the ticks gone, <paramref name="ticks"/> less those left before the deadline.</summary>
	private void MoveCamera(Es1ShellMap map, int ticks) {
		unchecked {
			int gone = ticks - (int)((uint)map.IntroDeadline - (uint)Ticks(map));
			map.CameraX = _from.X + _delta.X * gone;
			map.CameraY = _from.Y + _delta.Y * gone;
			map.Altitude = _from.Z + _delta.Z * gone;
		}
	}

	/// <summary>A wait: the deadline <paramref name="deadline"/>, the waiting state and the state it goes on to.</summary>
	private static void Wait(Es1ShellMap map, int deadline, short state, short resume) {
		map.IntroDeadline = deadline;
		map.IntroResumeState = resume;
		map.IntroState = state;
	}

	/// <summary><c>ShellMap_Ticks</c>, 0 without the map's resources.</summary>
	private static int Ticks(Es1ShellMap map) => map.Resources?.Ticks ?? 0;

	private static int After(Es1ShellMap map, int ticks) => unchecked(Ticks(map) + ticks);

	/// <summary>Whether <c>ShellMap_Ticks</c> is still below <paramref name="deadline"/>, both unsigned.</summary>
	private static bool Before(Es1ShellMap map, int deadline) => (uint)Ticks(map) < (uint)deadline;

	/// <summary>
	/// Objective line <paramref name="index"/>, which every caller has tested below the count. State 5 lowers line 0 to −1
	/// when the nav path has one waypoint or none, and state <c>0x10</c> then reads the dword before the lines'
	/// array, <c>+0x144</c>, as a string; here line −1 is empty, an engine guard.
	/// </summary>
	private static string Line(Es1ShellMap map, int index) => index >= 0 ? map.ObjectiveLines[index] : "";
}
