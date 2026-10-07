using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// <c>Mission_Box</c> (<c>004aa6c4</c>..<c>d0</c>) — the bounding box of <c>script.dat</c> block 1's
/// coordinate list, which <c>DBSim_LoadScriptDat</c> (<c>00424308</c>) accumulates as it reads the
/// block.
/// </summary>
public readonly record struct MissionBox(int MinX, int MinY, int MaxX, int MaxY) {
	/// <summary>Whether the box holds anything: a mission with no coordinates leaves it inverted.</summary>
	public bool IsEmpty => MaxX < MinX || MaxY < MinY;

	/// <summary>Span on x, in world units.</summary>
	public int Width => MaxX - MinX;

	/// <summary>Span on y.</summary>
	public int Height => MaxY - MinY;

	/// <summary>This box grown by <paramref name="margin"/> world units on every edge.</summary>
	public MissionBox Grown(int margin) =>
		new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

	/// <summary>The bounding box of <paramref name="points"/>, or an empty box when there are none.</summary>
	public static MissionBox Of(IReadOnlyList<Vec3i> points) {
		ArgumentNullException.ThrowIfNull(points);
		if (points.Count == 0) {
			return new MissionBox(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
		}

		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		foreach (var point in points) {
			minX = Math.Min(minX, point.X);
			minY = Math.Min(minY, point.Y);
			maxX = Math.Max(maxX, point.X);
			maxY = Math.Max(maxY, point.Y);
		}

		return new MissionBox(minX, minY, maxX, maxY);
	}
}
