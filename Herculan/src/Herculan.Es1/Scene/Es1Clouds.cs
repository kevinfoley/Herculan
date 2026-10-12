using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Scene;

/// <summary>
/// The world file's cloud layer, which <c>Clouds_Setup</c> (<c>ES1DBSIM 00027c00</c>) scatters round the viewer and
/// <c>Clouds_DriftAndDraw</c> (<c>00027d78</c>) drifts with one wind and carries over each frame
/// (docs/es1/rendering/ground-and-sky.md#the-clouds). This holds and moves the clouds; the backdrop draws them.
///
/// <para>Retail steps the wind once per frame drawn, so its clouds drift faster on a faster machine. The viewer steps
/// it <see cref="WindStepsPerSecond"/> times a second whatever the window's refresh rate, a rate the user chose.</para>
/// </summary>
public sealed class Es1Clouds {
	/// <summary>How many wind steps the viewer takes a second.</summary>
	public const int WindStepsPerSecond = 25;

	/// <summary><c>+0x69</c>, the field radius <c>Clouds_Setup</c> passes <c>TexPoly_SetQuadAndRadius</c>.</summary>
	public const int FieldRadius = 200000;

	/// <summary><c>+0x75</c>, the height a cloud's position stands at; its corners stand as high again above it.</summary>
	public const int Height = 10000;

	/// <summary><c>+0x6d</c> and <c>+0x71</c>, a panel's side.</summary>
	public const int Size = 25000;

	/// <summary><c>+0x79</c>, the margin either side of the field radius the carry-over uses.</summary>
	public const int Margin = 10000;

	/// <summary>How far the scatter shifts a 16-bit draw to make a cloud's distance from the viewer.</summary>
	private const int DistanceShift = 1;

	/// <summary>The panel's corners from the cloud's position, <c>TexPoly_SetQuadAndRadius</c>'s four points.</summary>
	public static readonly Vec3i[] Corners = [
		new(0, 0, Height), new(Size, 0, Height), new(Size, Size, Height), new(0, Size, Height),
	];

	private readonly Vec3i[] _positions;
	private readonly bool[] _carried;
	private Vec3i _wind;
	private Vec3i _last;
	private bool _setUp;
	private double _pendingSeconds;

	private Es1Clouds(int count, DynamixBitmapArray bank) {
		_positions = new Vec3i[count];
		_carried = new bool[count];
		Bank = bank;
	}

	/// <summary><c>dba\</c> the world file's cloud bank: the frames the panels show.</summary>
	public DynamixBitmapArray Bank { get; }

	/// <summary>The frame a panel shows from below, surface entry <c>+0x5d</c>, which the constructor zeroes.</summary>
	public int FrameBelow => 0;

	/// <summary>The frame every panel shows from above, surface entry <c>+0x65</c>, drawn once from 0 to 2.</summary>
	public int FrameAbove { get; private set; }

	/// <summary>The world file's clouds, or null when it names no bank or holds none.</summary>
	public static Es1Clouds? From(Es1WorldData world, DynamixBitmapArray? bank) =>
		world.CloudCount > 0 && bank != null ? new Es1Clouds(world.CloudCount, bank) : null;

	/// <summary>Counts time toward the wind steps the next <see cref="Frame"/> takes.</summary>
	public void Advance(double seconds) => _pendingSeconds += seconds;

	/// <summary>
	/// One frame of <c>Clouds_DriftAndDraw</c> seen from <paramref name="camera"/>: sets the layer up the first time,
	/// moves the first (count · (<paramref name="detail"/> + 1)) &gt;&gt; 8 clouds by the wind steps due, carries over
	/// every cloud, and returns the positions of those drawn.
	/// </summary>
	public ReadOnlySpan<Vec3i> Frame(Vec3i camera, int detail, Random random) {
		if (!_setUp) {
			SetUp(camera, random);
			_setUp = true;
		}

		int drawn = Math.Min(_positions.Length * (detail + 1) >> 8, _positions.Length);
		int steps = (int)(_pendingSeconds * WindStepsPerSecond);
		_pendingSeconds -= steps / (double)WindStepsPerSecond;
		for (int step = 0; step < steps; step++) {
			for (int i = 0; i < drawn; i++) {
				_positions[i] += _wind;
			}
		}

		CarryOver(camera);
		return _positions.AsSpan(0, drawn);
	}

	/// <summary>
	/// <c>TexPoly_DrawAtPoints</c>'s carry-over: while the viewer moves, a cloud farther than the field radius and the
	/// margin is reflected through the midpoint of the viewer's last and present positions, and is not carried again
	/// until it has come within the field radius less the margin.
	/// </summary>
	private void CarryOver(Vec3i camera) {
		if (camera.X != _last.X || camera.Y != _last.Y) {
			int midX = (_last.X + camera.X) >> 1;
			int midY = (_last.Y + camera.Y) >> 1;
			for (int i = 0; i < _positions.Length; i++) {
				var position = _positions[i];
				int distance = SimMath.FastMagnitude2D(position.X - camera.X, position.Y - camera.Y);
				if (!_carried[i] && distance > FieldRadius + Margin) {
					_positions[i] = new Vec3i(2 * midX - position.X, 2 * midY - position.Y, position.Z);
					_carried[i] = true;
				} else if (_carried[i] && distance < FieldRadius - Margin) {
					_carried[i] = false;
				}
			}
		}

		_last = camera;
	}

	/// <summary>
	/// <c>Clouds_Setup</c>: each cloud at a random bearing and an unsigned random distance from the viewer, one wind of
	/// 50 to 149 units a step, and the frame seen from above.
	///
	/// <para>The viewer runs no simulation, so its draws are not <c>Math_RandomNext</c>'s stream at that moment of a
	/// mission: each is a uniform value over the same range.</para>
	/// </summary>
	private void SetUp(Vec3i camera, Random random) {
		for (int i = 0; i < _positions.Length; i++) {
			int distance = random.Next(0x10000) << DistanceShift;
			short bearing = unchecked((short)random.Next(0x10000));
			_positions[i] = new Vec3i(
				camera.X + Es1Math.Q14Multiply(distance, SimTrig.Cos(bearing)),
				camera.Y + Es1Math.Q14Multiply(distance, SimTrig.Sin(bearing)),
				Height);
		}

		short heading = unchecked((short)random.Next(0x10000));
		int speed = 50 + random.Next(100);
		_wind = new Vec3i(Es1Math.Q14Multiply(speed, SimTrig.Cos(heading)),
			Es1Math.Q14Multiply(speed, SimTrig.Sin(heading)), 0);
		FrameAbove = random.Next(3);
		_last = camera;
	}
}
