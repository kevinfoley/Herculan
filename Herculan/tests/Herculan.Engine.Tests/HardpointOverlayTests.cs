using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <c>gam\arm_hots.dat</c> and <c>gam\rpr_hots.dat</c> — the clickable regions laid over a chassis
/// picture, both read by <c>Hotspots_BuildOverlay</c> (<c>0043c1a0</c>) through one piece of code.
///
/// <para>What these pin is the reading that a parse alone cannot settle: the four <c>int32</c> are
/// two inclusive corners, not a corner and a size. Retail's values stay inside the canvas either way,
/// so the discriminator is the geometry — a chassis's left and right hardpoints are mirror images
/// only when read as corners — plus the fact that the bytes go straight to <c>ESRect_Ctor</c>'s rect
/// argument. Get it wrong and every hotspot is the right shape in the wrong place.</para>
///
/// <para>Retail-data cases skip silently without an install, as the rest of the suite does.</para>
/// </summary>
public class HardpointOverlayTests {
	/// <summary>The chassis count the reader asserts.</summary>
	private const int ChassisCount = 9;

	private static HardpointOverlayConfig? Load(string name) {
		if (GameInstall.Locate(null) is not { } root) {
			return null;
		}

		var content = GameContent.Mount(GameInstall.ArchiveDirectory(root), new[] { "SHELL0.VOL" });
		return content.Read("gam", name) is { } bytes
			? new HardpointOverlayTransformer().Parse(bytes)
			: null;
	}

	/// <summary>
	/// Nine groups with sequential ids in both files, and the per-chassis area counts. The arming
	/// counts are each chassis's mount capacity and the repair counts are a flat six, which is what
	/// says the two files carry different things through the same format.
	/// </summary>
	[Theory]
	[InlineData("ARM_HOTS.DAT", new[] { 3, 5, 5, 8, 9, 9, 10, 4, 7 })]
	[InlineData("RPR_HOTS.DAT", new[] { 6, 6, 6, 6, 6, 6, 6, 6, 6 })]
	public void RetailFileHasNineChassisGroups(string name, int[] areaCounts) {
		if (Load(name) is not { } overlay) {
			return;
		}

		Assert.NotNull(overlay.Entries);
		Assert.Equal(ChassisCount, overlay.Entries.Length);

		for (int i = 0; i < overlay.Entries.Length; i++) {
			Assert.Equal(i, overlay.Entries[i].HercId);
			Assert.Equal(areaCounts[i], overlay.Entries[i].Areas!.Length);
		}
	}

	/// <summary>
	/// Every area is a well-formed inclusive rect inside the 640x480 canvas. The all-zero areas 1 and 2
	/// of the Tomahawk's and Maverick's repair groups are the one exception, and they are counted rather than
	/// waved past, so a parse that silently produced more of them would fail here.
	/// </summary>
	[Theory]
	[InlineData("ARM_HOTS.DAT", 0)]
	[InlineData("RPR_HOTS.DAT", 4)]
	public void EveryAreaIsARectInsideTheCanvas(string name, int expectedEmpty) {
		if (Load(name) is not { } overlay) {
			return;
		}

		int empty = 0;
		foreach (var herc in overlay.Entries!) {
			foreach (var area in herc.Areas!) {
				if (area is { X0: 0, Y0: 0, X1: 0, Y1: 0 }) {
					empty++;
					continue;
				}

				Assert.True(area.X1 > area.X0, $"chassis {herc.HercId} area {area.Id}: x1 <= x0");
				Assert.True(area.Y1 > area.Y0, $"chassis {herc.HercId} area {area.Id}: y1 <= y0");
				Assert.InRange(area.X0, 0, ShellLayout.CanvasWidth - 1);
				Assert.InRange(area.X1, 0, ShellLayout.CanvasWidth - 1);
				Assert.InRange(area.Y0, 0, ShellLayout.CanvasHeight - 1);
				Assert.InRange(area.Y1, 0, ShellLayout.CanvasHeight - 1);
			}
		}

		Assert.Equal(expectedEmpty, empty);
	}

	/// <summary>
	/// The first chassis's first two arming areas, verbatim, and the mirror symmetry that settles the
	/// corner-versus-size reading. Read as <c>{x, y, w, h}</c> these would be two overlapping boxes
	/// 70 and 191 pixels wide; read as corners they are a 34x32 and a 35x32 hardpoint either side of
	/// x≈114.
	/// </summary>
	[Fact]
	public void ArmingHardpointsAreMirroredAboutTheChassisCentre() {
		if (Load("ARM_HOTS.DAT") is not { } overlay) {
			return;
		}

		var areas = overlay.Entries![0].Areas!;
		var left = areas[0];
		var right = areas[1];

		Assert.Equal((37, 94, 70, 125), (left.X0, left.Y0, left.X1, left.Y1));
		Assert.Equal((157, 94, 191, 125), (right.X0, right.Y0, right.X1, right.Y1));

		// Same band, and the two gaps to the mirror axis agree to within a pixel.
		Assert.Equal(left.Y0, right.Y0);
		Assert.Equal(left.Y1, right.Y1);
		Assert.InRange(Math.Abs((left.X0 + right.X1) - (left.X1 + right.X0)), 0, 2);
	}

	/// <summary>
	/// The six areas <c>rpr_hots.dat</c> carries per chassis are the six external groups — the file's
	/// own count agrees with the repair screen's split point, where column 0's rows turn from groups
	/// into hardpoints.
	/// </summary>
	[Fact]
	public void RepairFileCarriesOneAreaPerExternalGroup() {
		if (Load("RPR_HOTS.DAT") is not { } overlay) {
			return;
		}

		foreach (var herc in overlay.Entries!) {
			Assert.Equal(ShellRepairScreen.ExternalRowCount, herc.Areas!.Length);
		}
	}

	/// <summary>
	/// Both files round-trip byte for byte. The writer is independent of the reader, so a field the
	/// reader misordered would come back transposed here rather than cancelling out.
	/// </summary>
	[Theory]
	[InlineData("ARM_HOTS.DAT")]
	[InlineData("RPR_HOTS.DAT")]
	public void RoundTripsRetailFileByteForByte(string name) {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		var content = GameContent.Mount(GameInstall.ArchiveDirectory(root), new[] { "SHELL0.VOL" });
		if (content.Read("gam", name) is not { } original) {
			return;
		}

		var transformer = new HardpointOverlayTransformer();
		var parsed = transformer.Parse(original);

		Assert.NotNull(parsed);
		Assert.Equal(original, transformer.Write(parsed));
	}
}
