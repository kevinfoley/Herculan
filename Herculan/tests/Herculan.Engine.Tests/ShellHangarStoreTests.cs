using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ShellHangar.Store"/> — that the screens' model, written back into the save it was read
/// from, loses nothing.
///
/// <para>The hangar rebuilds the save's armory, workshop queue, bays and squad records from its own
/// state rather than leaving the parsed blocks alone, so a field the reader keeps and the rebuild drops
/// comes back as a default and the next load reads it without complaint. An unchanged hangar stored
/// back has to give the file's own bytes, with one exception: every weapon's stock comes back in the
/// opposite order, because <c>Armory_Write</c> walks the list from the head and the reader pushes onto
/// it (docs/retail/formats/save-games.md#armory-stock-record). The expected bytes are the file's with each
/// stock reversed in the parsed model, so the reversal is pinned rather than tolerated.</para>
/// </summary>
public class ShellHangarStoreTests {
	[Fact]
	public void StoresAnUnchangedSyntheticSaveBackAsItWasRead() {
		AssertStoresBack(PlayerSaveRoundTripTests.BuildSave(), "synthetic");
	}

	/// <summary>Every save slot of a real install; skips silently without one, as the rest of the suite does.</summary>
	[Fact]
	public void StoresEveryRetailSaveBackAsItWasRead() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string savDir = ShellSaveSlots.Directory(root);
		if (!Directory.Exists(savDir)) {
			return;
		}

		foreach (string slot in Directory.GetFiles(savDir, "GAME_*.SAV")) {
			AssertStoresBack(File.ReadAllBytes(slot), Path.GetFileName(slot));
		}
	}

	/// <summary>Two stores in a row reverse every stock twice, which is the file again byte for byte.</summary>
	[Fact]
	public void TwoStoresGiveTheOriginalBytes() {
		byte[] original = PlayerSaveRoundTripTests.BuildSave();

		byte[] once = StoreUnchanged(original);
		Assert.NotEqual(original, once);
		Assert.Equal(original, StoreUnchanged(once));
	}

	/// <summary>
	/// What the screens change reaches the file: an order into an empty bay with its price off the pool
	/// and its build time left to go, and a squad member moved to another bay and position.
	/// </summary>
	[Fact]
	public void StoresWhatTheScreensChange() {
		var transformer = new PlayerSaveTransform();
		var save = Assert.IsType<PlayerSave>(transformer.Parse(PlayerSaveRoundTripTests.BuildSave()));

		// The synthetic save's squad pointers are out of range; point squad 0's at its first record.
		save.SquadTailAndPlayerHead[0] = 0;
		var hangar = ShellHangar.From(save);
		var member = Assert.Single(hangar.SquadMembers);
		int salvage = hangar.SalvageKilograms;

		int price = hangar.Order(bay: 5, chassisType: 1, priceTons: 20, buildMissions: 3);
		ShellHangar.SetBay(member, 5);
		hangar.SetSquadPosition(0, 2);
		hangar.Store(save);

		var reread = Assert.IsType<PlayerSave>(transformer.Parse(transformer.Write(save)!));
		reread.SquadTailAndPlayerHead[0] = 0;
		var reloaded = ShellHangar.From(reread);

		Assert.Equal(salvage - price, reloaded.SalvageKilograms);
		var ordered = Assert.IsType<ShellBayMachine>(reloaded.Bay(5));
		Assert.Equal(1, ordered.ChassisType);
		Assert.Equal(0, ordered.BuildPercent);
		Assert.Equal(3, ordered.BuildMissionsLeft);
		Assert.False(ordered.IsBuilt);

		var moved = Assert.Single(reloaded.SquadMembers);
		Assert.Equal(5, moved.Bay);
		Assert.Equal(2, moved.SquadPosition);
		Assert.Equal(5, reread.Squadmates![0].Bay);
		Assert.Equal(2, reread.Squadmates[0].SquadPosition);
	}

	/// <summary>
	/// Taking a squad member out of their bay puts them off strength, and both that byte and the player
	/// block's count of machines on strength are written.
	/// </summary>
	[Fact]
	public void StoresTheOnStrengthByteAndCount() {
		var transformer = new PlayerSaveTransform();
		var save = Assert.IsType<PlayerSave>(transformer.Parse(PlayerSaveRoundTripTests.BuildSave()));
		save.SquadTailAndPlayerHead[0] = 0;
		var hangar = ShellHangar.From(save);
		Assert.True(hangar.SquadMembers[0].OnStrength);
		int onStrength = hangar.MachinesOnStrength;

		hangar.UnassignSquadMemberBay(0);
		hangar.Store(save);

		var reread = Assert.IsType<PlayerSave>(transformer.Parse(transformer.Write(save)!));
		Assert.Equal(0, reread.Squadmates![0].OnStrength);
		Assert.Equal(-1, reread.Squadmates[0].Bay);
		Assert.Equal(onStrength - 1, reread.MachinesOnStrength);
	}

	private static void AssertStoresBack(byte[] original, string name) {
		var transformer = new PlayerSaveTransform();

		var expected = Assert.IsType<PlayerSave>(transformer.Parse(original));
		foreach (var item in expected.Inventory?.Items ?? Array.Empty<Inventory.InventoryItem>()) {
			if (item?.Units != null) {
				Array.Reverse(item.Units);
			}
		}

		byte[] want = transformer.Write(expected)!;
		byte[] got = StoreUnchanged(original);

		Assert.True(want.Length == got.Length, $"{name}: stored {got.Length} bytes, expected {want.Length}");
		int first = Enumerable.Range(0, want.Length).FirstOrDefault(i => want[i] != got[i], -1);
		Assert.True(first == -1, $"{name}: first difference at byte 0x{first:x}");
	}

	/// <summary>The save parsed, read into a hangar, stored straight back and written.</summary>
	private static byte[] StoreUnchanged(byte[] bytes) {
		var transformer = new PlayerSaveTransform();
		var save = Assert.IsType<PlayerSave>(transformer.Parse(bytes));
		ShellHangar.From(save).Store(save);
		return transformer.Write(save)!;
	}
}
