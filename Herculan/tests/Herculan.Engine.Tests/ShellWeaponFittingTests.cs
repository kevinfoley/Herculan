using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Fitting a weapon on the WEAPONS screen, and the armory stock it moves units in and out of:
/// <c>Herc_FitMount</c> (<c>004114ec</c>), <c>Arming_SelectRow</c>'s refusal (<c>0043f71c</c>), the steppers, and the
/// two scrap paths that also move stock. See docs/retail/shell/weapons-and-repair.md#fitting-a-weapon.
/// </summary>
public class ShellWeaponFittingTests {
	private const int Atc20 = 1;
	private const int Atc35 = 2;
	private const int Rack = 0xd;
	private const int RazorLauncher = 0x10;

	[Fact]
	public void FitTakesTheHeadOfTheStockListAndItsFitCondition() {
		// The loader pushes each unit onto the head, so the file's last unit is fitted first.
		var hangar = Hangar(new[] { 0, 0 }, (Atc20, new[] { 60, 75 }));
		var machine = hangar.Bay(0)!;

		hangar.FitMount(machine, 0, Atc20);

		Assert.Equal(Atc20, machine.WeaponAt(0));
		Assert.Equal(75, machine.Mount(0)!.FitCondition);
		Assert.Equal(75, machine.Condition(ShellRepairCategory.Hardpoint, 0));
		Assert.Equal(1, hangar.WeaponsOwned(Atc20));
	}

	[Fact]
	public void FitReturnsTheOldUnitAndRefittingPutsTheSameUnitBack() {
		var hangar = Hangar(new[] { Atc20 }, (Atc35, new[] { 100 }));
		var machine = hangar.Bay(0)!;
		var original = machine.Mount(0)!;

		hangar.FitMount(machine, 0, Atc35);
		Assert.Equal(1, hangar.WeaponsOwned(Atc20));
		Assert.Equal(0, hangar.WeaponsOwned(Atc35));

		hangar.FitMount(machine, 0, Atc20);
		Assert.Same(original, machine.Mount(0));
		Assert.Equal(0, hangar.WeaponsOwned(Atc20));
		Assert.Equal(1, hangar.WeaponsOwned(Atc35));
	}

	[Theory]
	[InlineData(Rack, 1)]
	[InlineData(RazorLauncher, ShellWeaponUnit.NoGuidance)]
	[InlineData(Atc20, ShellWeaponUnit.NoGuidance)]
	public void FitResetsGuidanceToArhForTheThreeRacksOnly(int weapon, int guidance) {
		var hangar = Hangar(new[] { 0 }, (weapon, new[] { 100 }));
		var machine = hangar.Bay(0)!;

		hangar.FitMount(machine, 0, weapon);

		Assert.Equal(guidance, machine.Mount(0)!.Guidance);
	}

	[Fact]
	public void NoneEmptiesTheSlotAtFullCondition() {
		var hangar = Hangar(new[] { Atc20 });
		var machine = hangar.Bay(0)!;
		machine.SetCondition(ShellRepairCategory.Hardpoint, 0, 40);

		hangar.FitMount(machine, 0, 0);

		Assert.Null(machine.Mount(0));
		Assert.Equal(100, machine.Condition(ShellRepairCategory.Hardpoint, 0));
		Assert.Equal(1, hangar.WeaponsOwned(Atc20));
	}

	[Fact]
	public void AnEmptyStockListLeavesTheSlotEmptyAndItsConditionUntouched() {
		var hangar = Hangar(new[] { Rack });
		var machine = hangar.Bay(0)!;
		machine.SetCondition(ShellRepairCategory.Hardpoint, 0, 40);

		hangar.FitMount(machine, 0, Atc20);

		Assert.Null(machine.Mount(0));
		Assert.Equal(40, machine.Condition(ShellRepairCategory.Hardpoint, 0));
		Assert.Equal(1, hangar.WeaponsOwned(Rack));
	}

	[Fact]
	public void ScrappingAHercReturnsOnlyMountsAtEightyOrBetter() {
		var hangar = Hangar(new[] { Atc20, Atc20 });
		var machine = hangar.Bay(0)!;
		machine.SetCondition(ShellRepairCategory.Hardpoint, 0, 80);
		machine.SetCondition(ShellRepairCategory.Hardpoint, 1, 79);

		hangar.Scrap(0, costs: null);

		Assert.Null(hangar.Bay(0));
		Assert.Equal(1, hangar.WeaponsOwned(Atc20));
	}

	[Fact]
	public void ScrappingAWeaponsStockEmptiesItsList() {
		var hangar = Hangar(new[] { 0 }, (Atc20, new[] { 100, 100 }));

		hangar.ScrapStock(Atc20, valueTons: 3);

		Assert.Equal(0, hangar.WeaponsOwned(Atc20));
		Assert.Equal(3000, hangar.SalvageKilograms);
	}

	[Fact]
	public void ARowClickWithNoHardpointFitsNothing() {
		// Not ATC20: the entry lights its row, row 0, and clicking the lit row with no hardpoint is a no-op.
		var hangar = Hangar(new[] { 0 }, (Atc35, new[] { 100 }));
		var screen = new ShellWeaponsScreen(hangar, 0);

		Assert.True(screen.SelectRow(ShellWeaponsScreen.RowOfWeapon(Atc35), fit: true));

		Assert.Null(hangar.Bay(0)!.Mount(0));
		Assert.Equal(1, hangar.WeaponsOwned(Atc35));
	}

	[Fact]
	public void ARowClickWithAHardpointFitsTheRowsWeapon() {
		var hangar = Hangar(new[] { 0, 0 }, (Atc20, new[] { 100 }));
		var screen = new ShellWeaponsScreen(hangar, 0);

		Assert.True(screen.SelectHardpoint(1));
		Assert.Equal(ShellWeaponsScreen.RowOfWeapon(0), screen.SelectedRow);
		Assert.True(screen.SelectRow(ShellWeaponsScreen.RowOfWeapon(Atc20), fit: true));

		Assert.Equal(Atc20, hangar.Bay(0)!.WeaponAt(1));
		Assert.Equal(0, hangar.WeaponsOwned(Atc20));
	}

	[Fact]
	public void AnArhRackLetsAnUnheldAutocannon20Through() {
		// The refusal reads the mount's guidance kind where the weapon id belongs: ARH is 1, and so is ATC20.
		var hangar = Hangar(new[] { 0 }, (Rack, new[] { 100 }), (Atc20, Array.Empty<int>()),
			(Atc35, Array.Empty<int>()));
		var machine = hangar.Bay(0)!;
		hangar.FitMount(machine, 0, Rack);
		var screen = new ShellWeaponsScreen(hangar, 0);
		screen.SelectHardpoint(0);

		Assert.False(screen.SelectRow(ShellWeaponsScreen.RowOfWeapon(Atc35), fit: true));
		Assert.Equal(Rack, machine.WeaponAt(0));

		Assert.True(screen.SelectRow(ShellWeaponsScreen.RowOfWeapon(Atc20), fit: true));
		Assert.Null(machine.Mount(0));
		Assert.Equal(1, hangar.WeaponsOwned(Rack));
	}

	[Fact]
	public void AGuidanceButtonWritesItsKindIntoTheMount() {
		var hangar = Hangar(new[] { 0 }, (Rack, new[] { 100 }));
		var machine = hangar.Bay(0)!;
		hangar.FitMount(machine, 0, Rack);
		var screen = new ShellWeaponsScreen(hangar, 0);
		screen.SelectHardpoint(0);

		screen.ShowGuidance((int)ShellWeaponsButton.Eo);

		Assert.Equal(3, machine.Mount(0)!.Guidance);
	}

	[Fact]
	public void TheSteppersWrapThroughTheMounts() {
		var hangar = Hangar(new[] { 0, 0, 0 });
		var screen = new ShellWeaponsScreen(hangar, 0);

		Assert.True(screen.PreviousHardpoint());
		Assert.Equal(2, screen.SelectedHardpoint);
		Assert.True(screen.NextHardpoint());
		Assert.Equal(0, screen.SelectedHardpoint);
		Assert.True(screen.PreviousHardpoint());
		Assert.Equal(2, screen.SelectedHardpoint);
	}

	/// <summary>
	/// One machine in bay 0 with a mount per entry of <paramref name="mounts"/> (0 for empty), and each
	/// named weapon unlocked with one unit per condition listed, in file order.
	/// </summary>
	private static ShellHangar Hangar(int[] mounts, params (int Weapon, int[] Conditions)[] stock) {
		var save = new PlayerSave();
		save.HercBay[0] = BayEntry(mounts);
		save.Inventory = new Inventory {
			Items = stock.Select(entry => new Inventory.InventoryItem(entry.Conditions.Length) {
				Id = WeaponLUT.GetById(entry.Weapon),
				UnlockFlag = 1,
				Units = entry.Conditions.Select(condition => new ShellWeaponEntry {
					Id = WeaponLUT.GetById(entry.Weapon),
					FitCondition = (short)condition,
					Condition = 100,
					Guidance = MissileType.None,
				}).ToArray(),
			}).ToArray(),
		};

		return ShellHangar.From(save);
	}

	private static HercBayEntry BayEntry(int[] mounts) {
		var entry = new HercBayEntry {
			ChassisType = HercLUT.GetById(0),
			MountCapacity = (short)mounts.Length,
			BuildPercent = 100,
		};

		for (short slot = 0; slot < entry.HardpointConditions.Length; slot++) {
			entry.HardpointConditions[slot] = new ShellHercPart(slot, "hardpoint_" + slot, 100);
			if (slot < mounts.Length && mounts[slot] != 0) {
				entry.Mounts[slot] = new ShellWeaponEntry {
					Id = WeaponLUT.GetById(mounts[slot]), FitCondition = 100, Condition = 100,
					Guidance = MissileType.None,
				};
			}
		}

		return entry;
	}
}
