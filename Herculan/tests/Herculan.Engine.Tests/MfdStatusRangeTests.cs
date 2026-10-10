using Herculan.Engine.Cockpit;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The MFD status screen's <c>DIST:</c> range: each release's own reading with the tweak off, and the
/// scanner's ground range in metres with it on. See <see cref="MfdStatusSubject.Range"/>.
/// </summary>
public class MfdStatusRangeTests {
	// A subject 10000 units east of the viewer and 3000 above it, so the height difference shows.
	private static readonly Vec3i ViewerAt = new(0, 0, 0);
	private static readonly Vec3i SubjectAt = new(10000, 0, 3000);

	private static int Units => SimMath.FastMagnitude3D(10000, 0, 3000);

	[Fact]
	public void V10PrintsRawWorldUnits() {
		Assert.Equal(Units, MfdStatusSubject.Range(new Marker(SubjectAt), new Marker(ViewerAt), v110: false, scannerRange: false));
	}

	[Fact]
	public void V110PrintsTheSameRangeInMetres() {
		Assert.Equal(Units / 1000 * 6,
			MfdStatusSubject.Range(new Marker(SubjectAt), new Marker(ViewerAt), v110: true, scannerRange: false));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TweakPrintsGroundRangeInMetresOnEitherRelease(bool v110) {
		// 10000 units along the ground is 60 m; the 3000 units of height do not count.
		Assert.Equal(60, MfdStatusSubject.Range(new Marker(SubjectAt), new Marker(ViewerAt), v110, scannerRange: true));
	}

	[Fact]
	public void NoViewerReadsZero() {
		Assert.Equal(0, MfdStatusSubject.Range(new Marker(SubjectAt), null, v110: true, scannerRange: false));
	}

	private sealed class Marker : SimObject {
		public Marker(Vec3i position) => Position = position;

		public override int HitRadius => 500;

		public override void Tick(SimWorld world) {
		}
	}
}
