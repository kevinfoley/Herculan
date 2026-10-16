using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <c>gam\arm_hots.dat</c> and <c>gam\rpr_hots.dat</c> — the clickable regions laid over a chassis
/// picture, both read by <c>Hotspots_BuildOverlay</c> (<c>0043c1a0</c>) through one piece of code.
///
/// <para>What these pin is that the two files agree with what consumes them: the arming file's
/// per-chassis area counts with the mount capacities, the repair file's with the external groups,
/// and every area with the canvas. See docs/retail/formats/herc-catalogs.md for the format.</para>
///
/// <para>Retail-data cases skip silently without an install, as the rest of the suite does.</para>
/// </summary>
public class HardpointOverlayTests {
	private static HardpointOverlayConfig? Load(string name) {
		if (GameInstall.Locate(null) is not { } root) {
			return null;
		}

		var content = GameContent.MountShell(root);
		return content.Read("gam", name) is { } bytes
			? new HardpointOverlayTransformer().Parse(bytes)
			: null;
	}

	/// <summary>
	/// <c>arm_hots.dat</c> carries one area per weapon mount: each chassis's group, found by its own
	/// id, holds as many areas as that chassis's mount capacity. The arming tab bounds its mount loop on
	/// the capacity and indexes the group's areas with it, so the two retail sources have to agree.
	/// </summary>
	[Fact]
	public void ArmingFileCarriesOneAreaPerMount() {
		if (Load("ARM_HOTS.DAT") is not { } overlay) {
			return;
		}

		var players = HercLUT.Values().Where(h => h.IsPlayerChassis).ToArray();
		Assert.Equal(players.Length, overlay.Entries!.Length);

		foreach (var herc in players) {
			var group = Assert.Single(overlay.Entries, e => e.HercId == herc.Id);
			Assert.Equal(herc.HardpointMax, group.Areas!.Length);
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

		var content = GameContent.MountShell(root);
		if (content.Read("gam", name) is not { } original) {
			return;
		}

		var transformer = new HardpointOverlayTransformer();
		var parsed = transformer.Parse(original);

		Assert.NotNull(parsed);
		Assert.Equal(original, transformer.Write(parsed));
	}
}
