using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The repair screen's geometry, its selection rules and the cost model behind its readouts.
///
/// <para>The rects in <see cref="ShellRepairScreen"/> are parent-relative, as <c>Repair_BuildScreen</c> (<c>00432037</c>)
/// writes them, so what is pinned here is the composition — the absolutes below were added up by hand
/// from the widget tree, and a wrong parent or a dropped offset moves them. That is the error a parse
/// landing on EOF cannot catch.</para>
/// </summary>
public class ShellRepairScreenTests {
	/// <summary>
	/// The content panel. Unlike the save screen's it is not centred: it takes the right two thirds of
	/// the canvas and leaves the left for the machine's damage diagram and the squad roster.
	/// </summary>
	[Fact]
	public void PlacesTheContentPanel() {
		var panel = ShellRepairScreen.PanelRect;

		Assert.Equal(new ShellRect(241, 43, 632, 473), panel);
		Assert.Equal(392, panel.Width);
		Assert.Equal(431, panel.Height);

		// Clear of the tab strip above it and inside the canvas on both far edges.
		Assert.True(panel.Y0 > ShellLayout.TabBottom);
		Assert.True(panel.X1 < ShellLayout.CanvasWidth);
		Assert.True(panel.Y1 < ShellLayout.CanvasHeight);

		// Both pictures the screen leaves room for sit entirely to its left.
		Assert.True(ShellRepairDiagrams.ExternalPictureRect.X1 < panel.X0);
		Assert.True(ShellRepairDiagrams.InternalsPictureRect.X1 < panel.X0);
	}

	/// <summary>
	/// Both lists' rows: 13 tall on a 12-pixel pitch, so each overlaps its neighbour's border row. The
	/// ten hardpoint rows are a second run inside the same panel, below a gap that separates them from
	/// the six component groups, and the last of them lands one pixel inside the panel's own bottom.
	/// </summary>
	[Fact]
	public void PlacesBothListsRows() {
		var screen = new ShellRepairScreen();

		var firstGroup = screen.RowRect(0, 0);
		var secondGroup = screen.RowRect(0, 1);
		var lastGroup = screen.RowRect(0, ShellRepairScreen.ExternalRowCount - 1);
		var firstMount = screen.RowRect(0, ShellRepairScreen.ExternalRowCount);
		var lastMount = screen.RowRect(0, ShellRepairScreen.ColumnZeroRowCount - 1);
		var firstInternal = screen.RowRect(1, 0);
		var lastInternal = screen.RowRect(1, ShellRepairScreen.InternalRowCount - 1);

		Assert.Equal(new ShellRect(257, 97, 458, 109), firstGroup);
		Assert.Equal(new ShellRect(257, 109, 458, 121), secondGroup);
		Assert.Equal(new ShellRect(257, 157, 458, 169), lastGroup);
		Assert.Equal(new ShellRect(257, 183, 458, 195), firstMount);
		Assert.Equal(new ShellRect(257, 291, 458, 303), lastMount);
		Assert.Equal(new ShellRect(257, 338, 458, 350), firstInternal);
		Assert.Equal(new ShellRect(257, 434, 458, 446), lastInternal);

		Assert.Equal(13, firstGroup.Height);
		Assert.Equal(12, secondGroup.Y0 - firstGroup.Y0);
		Assert.Equal(firstGroup.Y1, secondGroup.Y0);

		// The two runs share one panel, and the gap between them is the builder's own.
		Assert.True(firstMount.Y0 > lastGroup.Y1);

		// Every row of both lists is the same 202 wide, on the same two edges.
		foreach (var row in new[] { firstGroup, lastMount, firstInternal, lastInternal }) {
			Assert.Equal(202, row.Width);
			Assert.Equal(firstGroup.X0, row.X0);
			Assert.Equal(firstGroup.X1, row.X1);
		}
	}

	/// <summary>
	/// The four buttons. Three are children of a framed readout panel and CANCEL of the content panel,
	/// so getting a parent wrong moves one of them by that panel's offset and nothing else.
	/// </summary>
	[Fact]
	public void PlacesTheButtons() {
		var screen = new ShellRepairScreen();

		Assert.Equal(new ShellRect(495, 266, 600, 281), screen.ButtonRect(ShellRepairButton.Repair));
		Assert.Equal(new ShellRect(495, 357, 600, 372), screen.ButtonRect(ShellRepairButton.RepairAll));
		Assert.Equal(new ShellRect(495, 408, 600, 423), screen.ButtonRect(ShellRepairButton.Scrap));
		Assert.Equal(new ShellRect(515, 446, 583, 461), screen.ButtonRect(ShellRepairButton.Cancel));

		// The three inside a panel share a width; CANCEL is its own size in the content panel.
		Assert.Equal(106, screen.ButtonRect(ShellRepairButton.Repair).Width);
		Assert.Equal(106, screen.ButtonRect(ShellRepairButton.Scrap).Width);
		Assert.Equal(69, screen.ButtonRect(ShellRepairButton.Cancel).Width);
		Assert.All(new[] { ShellRepairButton.Repair, ShellRepairButton.RepairAll, ShellRepairButton.Scrap,
			ShellRepairButton.Cancel }, button => Assert.Equal(16, screen.ButtonRect(button).Height));
	}

	/// <summary>
	/// A <c>(column, row)</c> pair is a category and an index within it — <c>Repair_HotspotCategory</c> (<c>00433410</c>) and
	/// <c>Repair_HotspotIndex</c> (<c>00433431</c>). The three categories are the status block's own accessor modes, which is
	/// why one number does for the list, the condition array and the price table alike.
	/// </summary>
	[Fact]
	public void ResolvesARowPairIntoACategoryAndIndex() {
		Assert.Equal(ShellRepairCategory.ExternalGroup, ShellRepairScreen.CategoryOf(0, 0));
		Assert.Equal(ShellRepairCategory.ExternalGroup, ShellRepairScreen.CategoryOf(0, 5));
		Assert.Equal(ShellRepairCategory.Hardpoint, ShellRepairScreen.CategoryOf(0, 6));
		Assert.Equal(ShellRepairCategory.Hardpoint, ShellRepairScreen.CategoryOf(0, 15));
		Assert.Equal(ShellRepairCategory.Internal, ShellRepairScreen.CategoryOf(1, 0));
		Assert.Equal(ShellRepairCategory.Internal, ShellRepairScreen.CategoryOf(1, 8));

		// Only the hardpoint rows are offset, by the six group rows above them.
		Assert.Equal(5, ShellRepairScreen.IndexOf(ShellRepairCategory.ExternalGroup, 5));
		Assert.Equal(0, ShellRepairScreen.IndexOf(ShellRepairCategory.Hardpoint, 6));
		Assert.Equal(9, ShellRepairScreen.IndexOf(ShellRepairCategory.Hardpoint, 15));
		Assert.Equal(8, ShellRepairScreen.IndexOf(ShellRepairCategory.Internal, 8));

		// Both lists end to end are the builder's twenty-five handlers, so no row is unaccounted for.
		Assert.Equal(25, ShellRepairScreen.ColumnZeroRowCount + ShellRepairScreen.InternalRowCount);
	}

	/// <summary>
	/// <b>An unfitted hardpoint cannot be selected at all.</b> A row past the machine's mount capacity
	/// and a row whose slot holds no weapon are both refused, and the refusal is silent — the selection
	/// does not move and nothing repaints. A component group and an internal always answer.
	/// </summary>
	[Fact]
	public void RefusesAHardpointRowWithNothingFittedInIt() {
		// Three mounts, two of them filled: slot 1 is empty and slots 3 upward are past capacity.
		var screen = ScreenFor(BayEntry(mountCapacity: 3, weapons: new[] { 5, 0, 7 }));

		Assert.True(screen.Select(0, 6));
		Assert.Equal(ShellRepairCategory.Hardpoint, screen.SelectedCategory);
		Assert.Equal(0, screen.SelectedIndex);

		// The empty mount, and one past capacity.
		Assert.False(screen.Select(0, 7));
		Assert.False(screen.Select(0, 9));
		Assert.Equal(0, screen.SelectedIndex);

		// The fitted one beyond the empty slot still answers.
		Assert.True(screen.Select(0, 8));
		Assert.Equal(2, screen.SelectedIndex);

		// And the two component lists take any of their rows.
		Assert.True(screen.Select(1, 4));
		Assert.Equal(ShellRepairCategory.Internal, screen.SelectedCategory);
		Assert.True(screen.Select(0, 3));

		// Clicking the row already selected is a no-op, the same early return the original opens with.
		Assert.False(screen.Select(0, 3));
	}

	/// <summary>
	/// Both repair buttons gate on the salvage pool <i>net of the build queue</i>, and SCRAP is dead
	/// while the machine is the only one the player can fly.
	/// </summary>
	[Fact]
	public void GatesTheButtonsOnAffordabilityAndOnTheLastMachine() {
		var screen = ScreenFor(BayEntry(mountCapacity: 2, weapons: new[] { 5, 6 }, groupCondition: 50),
			salvage: 1_000_000);

		Assert.True(screen.IsEnabled(ShellRepairButton.Repair));
		Assert.True(screen.IsEnabled(ShellRepairButton.RepairAll));

		// The queue's commitment is money the repair bay cannot see.
		screen.QueuedKilograms = 1_000_000;
		Assert.Equal(0, screen.AvailableKilograms);
		Assert.False(screen.IsEnabled(ShellRepairButton.Repair));
		Assert.False(screen.IsEnabled(ShellRepairButton.RepairAll));

		// CANCEL never gates, and one deployable machine cannot be scrapped.
		Assert.True(screen.IsEnabled(ShellRepairButton.Cancel));
		Assert.False(screen.IsEnabled(ShellRepairButton.Scrap));
	}

	/// <summary>
	/// A disabled button does not answer a click; a row does, over its own rect.
	/// </summary>
	[Fact]
	public void HitTestsRowsAndLiveButtonsOnly() {
		var screen = ScreenFor(BayEntry(mountCapacity: 2, weapons: new[] { 5, 6 }), salvage: 1_000_000);

		var row = screen.RowRect(1, 3);
		Assert.Equal((1, 3), screen.RowAt(row.X0, row.Y0 + 1));
		Assert.Null(screen.RowAt(row.X0 - 1, row.Y0 + 1));

		// Rows share their border line, and the row built later — the lower one — takes it.
		Assert.Equal((1, 3), screen.RowAt(row.X0, row.Y0));
		Assert.Equal((1, 4), screen.RowAt(row.X0, row.Y1));

		var repair = screen.ButtonRect(ShellRepairButton.Repair);
		Assert.Equal(ShellRepairButton.Repair, screen.ButtonAt(repair.X0 + 1, repair.Y0 + 1));

		// SCRAP is gated by the one-machine rule here, so its rect answers nothing.
		var scrap = screen.ButtonRect(ShellRepairButton.Scrap);
		Assert.Null(screen.ButtonAt(scrap.X0 + 1, scrap.Y0 + 1));
	}

	/// <summary>
	/// The damage bands are the repair ladder's own, so the word a condition prints and the level it
	/// would be worked to are the same number. 90 and up is level 0 and only a dead component is 5.
	/// </summary>
	[Theory]
	[InlineData(100, 0)]
	[InlineData(90, 0)]
	[InlineData(89, 1)]
	[InlineData(80, 1)]
	[InlineData(79, 2)]
	[InlineData(60, 2)]
	[InlineData(59, 3)]
	[InlineData(30, 3)]
	[InlineData(29, 4)]
	[InlineData(1, 4)]
	[InlineData(0, 5)]
	public void BandsAConditionOntoTheRepairLadder(int condition, int level) =>
		Assert.Equal(level, ShellRepairCosts.LevelForCondition(condition));

	/// <summary>
	/// One colour per band, from the table at <c>004765ca</c>, and a destroyed component's is the odd
	/// one out. Taken at each band's lower edge, so a colour shifted by one band shows here.
	/// </summary>
	[Fact]
	public void ColoursAConditionByItsBand() {
		var colours = new[] { 90, 80, 60, 30, 1, 0 }.Select(ShellRepairScreen.BandColor);
		Assert.Equal(new byte[] { 14, 13, 12, 11, 10, 39 }, colours);
		Assert.Equal(14, ShellRepairScreen.BandColor(100));
	}

	/// <summary>
	/// The retail string overrun: six levels, four words. Levels 4 and 5 read <c>0x6c</c> and
	/// <c>0x6d</c> — <c>% Complete</c> and <c>Unassigned</c> — and the port reproduces that rather than
	/// clamping; see Herculan/KNOWN_ISSUES.md. Pinned so that a later change which "fixes" it has to do
	/// so deliberately.
	/// </summary>
	[Fact]
	public void ReadsTheLastTwoConditionWordsPastTheEndOfTheirRun() {
		Assert.Equal(0x68, ShellRepairScreen.ConditionWordText(100));
		Assert.Equal(0x69, ShellRepairScreen.ConditionWordText(80));
		Assert.Equal(0x6a, ShellRepairScreen.ConditionWordText(60));
		Assert.Equal(0x6b, ShellRepairScreen.ConditionWordText(30));
		Assert.Equal(0x6c, ShellRepairScreen.ConditionWordText(29));
		Assert.Equal(0x6d, ShellRepairScreen.ConditionWordText(0));
	}

	/// <summary>
	/// <b>A full rebuild costs about 72.5% of the chassis price, for every chassis.</b> The fifteen
	/// shares sum to 950 and the two Q10 steps land there whatever the price is, since the only
	/// per-chassis input is the price itself — which is the cross-check that says the expansion is
	/// being done in the right order and in the right arithmetic.
	/// </summary>
	[Fact]
	public void PricesAFullRebuildAtAboutSeventyTwoPercentOfTheChassis() {
		var costs = ShellRepairCosts.Expand(RetailValues(), new short[] { 100, 300 });

		// A machine at zero everywhere, so every component is billed the whole of its unit value.
		int wrecked = costs.HercCost(Machine(chassisType: 0, mountCapacity: 0, weapons: Array.Empty<int>(),
			groupCondition: 0, internalCondition: 0));
		int price = 100 * ShellRepairCosts.KilogramsPerTon;

		Assert.InRange(wrecked / (double)price, 0.72, 0.73);

		// And the same fraction on a chassis three times the price.
		int expensive = costs.HercCost(Machine(chassisType: 1, mountCapacity: 0,
			weapons: Array.Empty<int>(), groupCondition: 0, internalCondition: 0));
		Assert.InRange(expensive / (double)(300 * ShellRepairCosts.KilogramsPerTon), 0.72, 0.73);

		// An undamaged machine is billed nothing at all.
		Assert.Equal(0, costs.HercCost(Machine(mountCapacity: 0, weapons: Array.Empty<int>())));
	}

	/// <summary>
	/// <b>The per-item figure repairs one band, not to full.</b> <c>Repair_LevelStepCost</c> (<c>00413871</c>) targets the floor
	/// of the level above the one the component sits in — a component at 50 is level 3 and is priced up
	/// to 60 — where <c>Repair_HercCost</c> prices the whole machine to 100. The screen shows both at
	/// once and they are not two spellings of one figure.
	/// </summary>
	[Fact]
	public void PricesOneItemUpToTheNextBandAndTheMachineToFull() {
		// Q10(100, 100000) = 9765, then Q10(800, 9765) = 7628 — the Cockpit group's unit value on a
		// 100-ton chassis.
		var costs = ShellRepairCosts.Expand(RetailValues(), new short[] { 100 });
		int unit = costs.UnitValue(0, ShellRepairCategory.ExternalGroup, 0);
		Assert.Equal(7628, unit);

		// One level: 50 is level 3, whose next band up floors at 60.
		Assert.Equal((60 - 50) * unit / 100, ShellRepairCosts.SelectedItemCost(unit, 50));

		// To full: the same component all the way to 100.
		Assert.Equal((100 - 50) * unit / 100, ShellRepairCosts.ItemCost(unit, 50, 100));

		// Already at the top of the ladder: level 0 is the one case the per-item figure does go to 100.
		Assert.Equal((100 - 95) * unit / 100, ShellRepairCosts.SelectedItemCost(unit, 95));
		Assert.Equal(0, ShellRepairCosts.SelectedItemCost(unit, 100));
		Assert.Equal(0, ShellRepairCosts.ItemCost(unit, 100, 100));
	}

	/// <summary>
	/// A group's condition is the mean of its facets, not one of them — the six groups partition all
	/// thirteen external entries exactly once, and the group is the granularity the bay prices at.
	/// </summary>
	[Fact]
	public void ReadsAnExternalGroupAsTheMeanOfItsFacets() {
		var entry = BayEntry(mountCapacity: 0, weapons: Array.Empty<int>());

		// The cockpit's two facets, front and rear.
		entry.ExternalConditions![HercExternals.CockpitFront].Health = 100;
		entry.ExternalConditions[HercExternals.CockpitRear].Health = 50;

		// The left leg's three: thigh, calf, foot.
		entry.ExternalConditions[HercExternals.LegLeftTop].Health = 30;
		entry.ExternalConditions[HercExternals.LegLeftMid].Health = 60;
		entry.ExternalConditions[HercExternals.LegLeftFoot].Health = 61;

		var machine = ShellBayMachine.From(entry);
		Assert.Equal(75, machine.Condition(ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(50, machine.Condition(ShellRepairCategory.ExternalGroup, 4));

		// Integer division, as the original does it: 151 / 3 truncates rather than rounds.
		Assert.All(ShellBayMachine.ExternalGroupFacets, facets => Assert.NotEmpty(facets));
		Assert.Equal(13, ShellBayMachine.ExternalGroupFacets.Sum(f => f.Length));
	}

	/// <summary>
	/// The Razor's rows name nacelles and wings where a walker's name torsos and legs. The substitution
	/// is per chassis type, not per component list, so every one of the fifteen rows is affected at
	/// once.
	/// </summary>
	[Fact]
	public void NamesTheFlyersComponentsInPlaceOfAWalkers() {
		// The Cockpit and the internals from the sensor array down are shared.
		Assert.Equal(0x4e, ShellRepairScreen.ComponentNameText(0, ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(0x4e, ShellRepairScreen.ComponentNameText(ShellRepairScreen.FlyerChassisType,
			ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(0x56, ShellRepairScreen.ComponentNameText(ShellRepairScreen.FlyerChassisType,
			ShellRepairCategory.Internal, 2));

		// Left Torso becomes Left Nacelle, and the leg servos become wing servos.
		Assert.Equal(0x4f, ShellRepairScreen.ComponentNameText(0, ShellRepairCategory.ExternalGroup, 1));
		Assert.Equal(0x5d, ShellRepairScreen.ComponentNameText(ShellRepairScreen.FlyerChassisType,
			ShellRepairCategory.ExternalGroup, 1));
		Assert.Equal(0x54, ShellRepairScreen.ComponentNameText(0, ShellRepairCategory.Internal, 0));
		Assert.Equal(0x62, ShellRepairScreen.ComponentNameText(ShellRepairScreen.FlyerChassisType,
			ShellRepairCategory.Internal, 0));
	}

	/// <summary>
	/// Nothing a paint draws escapes its own widget — the title bar's hatch most of all, which is laid
	/// down 700 pixels wide for a panel 392 across and is stopped by the clip alone.
	/// </summary>
	[Fact]
	public void ClipsEveryPaintToItsOwnWidget() {
		var surface = new ShellSurface(ShellLayout.CanvasWidth, ShellLayout.CanvasHeight);
		var owned = new[] { ShellRepairScreen.PanelRect, ShellSquadPanel.PanelRect, ShellSquadPanel.ReadoutRect };

		ScreenFor(BayEntry(mountCapacity: 2, weapons: new[] { 5, 6 })).Paint(surface, null, null, null);

		for (int y = 0; y < ShellLayout.CanvasHeight; y++) {
			for (int x = 0; x < ShellLayout.CanvasWidth; x++) {
				if (!owned.Any(rect => rect.Contains(x, y))) {
					Assert.Equal(ShellSurface.Transparent, surface.At(x, y));
				}
			}
		}

		Assert.False(surface.IsBlank);
	}

	/// <summary>
	/// <c>Repair_TargetForLevel</c> (<c>00413838</c>): 100 from the top band, otherwise the floor of the
	/// band above — the same target the per-item cost is quoted to.
	/// </summary>
	[Theory]
	[InlineData(0, 100)]
	[InlineData(1, 90)]
	[InlineData(2, 80)]
	[InlineData(3, 60)]
	[InlineData(4, 30)]
	[InlineData(5, 1)]
	public void TargetsTheFloorOfTheBandAbove(int level, int target) =>
		Assert.Equal(target, ShellRepairCosts.TargetForLevel(level));

	/// <summary>
	/// REPAIR lifts the selection one band and takes the quoted cost off the whole pool — not the net
	/// figure the button is gated on, so a queued weapon does not change what is charged.
	/// </summary>
	[Fact]
	public void RepairLiftsTheSelectionOneBandAndChargesTheWholePool() {
		var hangar = HangarWith(salvage: 1_000_000, BayEntry(mountCapacity: 0, weapons: Array.Empty<int>(),
			groupCondition: 50, internalCondition: 25));
		var screen = ScreenFor(hangar);
		screen.QueuedKilograms = 400_000;

		// The Cockpit group at 50 is level 3, priced up to 60: (60 - 50) * 7628 / 100.
		Assert.Equal(762, screen.SelectionCost);
		Assert.Equal(762, screen.Repair());
		Assert.Equal(60, screen.SelectionCondition);
		Assert.Equal(1_000_000 - 762, hangar.SalvageKilograms);

		// Again: 60 is level 2, so the next REPAIR goes to 80, and the one after to 90.
		screen.Repair();
		Assert.Equal(80, screen.SelectionCondition);
		screen.Repair();
		Assert.Equal(90, screen.SelectionCondition);

		// Nothing else moved: the other groups and every internal are where they were.
		Assert.Equal(50, screen.Machine!.Condition(ShellRepairCategory.ExternalGroup, 1));
		Assert.Equal(25, screen.Machine.Condition(ShellRepairCategory.Internal, 0));

		// An internal at 25 is level 4, lifted to 30.
		screen.Select(1, 0);
		screen.Repair();
		Assert.Equal(30, screen.Machine.Condition(ShellRepairCategory.Internal, 0));
	}

	/// <summary>
	/// An external group is written through <c>HercStatus_SetGroup</c> (<c>00411c78</c>), every facet it
	/// covers at once, so a group whose facets differ comes out uniform.
	/// </summary>
	[Fact]
	public void RepairWritesEveryFacetOfAGroup() {
		var entry = BayEntry(mountCapacity: 0, weapons: Array.Empty<int>());
		entry.ExternalConditions![HercExternals.CockpitFront].Health = 100;
		entry.ExternalConditions[HercExternals.CockpitRear].Health = 50;
		var screen = ScreenFor(HangarWith(salvage: 1_000_000, entry));

		// The mean, 75, is level 2: both facets go to 80, the front one down from 100.
		screen.Repair();
		var status = screen.Machine!.CaptureStatus();
		Assert.Equal(80, status.External[HercExternals.CockpitFront.Id]);
		Assert.Equal(80, status.External[HercExternals.CockpitRear.Id]);
	}

	/// <summary>
	/// REPAIR ALL charges the rebuild figure and sets everything to 100 — a fitted mount at 0 too,
	/// although the rebuild figure bills nothing for it — and leaves an empty mount at 100.
	/// </summary>
	[Fact]
	public void RepairAllRebuildsTheMachineIncludingADestroyedMount() {
		var entry = BayEntry(mountCapacity: 3, weapons: new[] { 5, 0, 7 }, groupCondition: 40,
			internalCondition: 70);
		entry.HardpointConditions[0]!.Health = 0;
		entry.HardpointConditions[2]!.Health = 55;
		var hangar = HangarWith(salvage: 1_000_000, entry);
		var screen = ScreenFor(hangar);

		int quoted = screen.MachineCost;
		Assert.Equal(quoted, screen.RepairAll());
		Assert.Equal(1_000_000 - quoted, hangar.SalvageKilograms);

		var machine = screen.Machine!;
		for (int group = 0; group < ShellRepairScreen.ExternalRowCount; group++) {
			Assert.Equal(100, machine.Condition(ShellRepairCategory.ExternalGroup, group));
		}

		for (int component = 0; component < ShellRepairScreen.InternalRowCount; component++) {
			Assert.Equal(100, machine.Condition(ShellRepairCategory.Internal, component));
		}

		Assert.Equal(100, machine.Condition(ShellRepairCategory.Hardpoint, 0));
		Assert.Equal(100, machine.Condition(ShellRepairCategory.Hardpoint, 1));
		Assert.Equal(100, machine.Condition(ShellRepairCategory.Hardpoint, 2));
		Assert.Equal(0, screen.MachineCost);
	}

	/// <summary>
	/// CANCEL puts the pool and the machine back as they stood on entry, and stays on the screen.
	/// </summary>
	[Fact]
	public void CancelRestoresThePoolAndTheMachineFromEntry() {
		var hangar = HangarWith(salvage: 1_000_000, BayEntry(mountCapacity: 0, weapons: Array.Empty<int>(),
			groupCondition: 50, internalCondition: 50));
		var screen = ScreenFor(hangar);

		screen.Repair();
		screen.Select(1, 2);
		screen.Repair();
		screen.RepairAll();
		Assert.NotEqual(1_000_000, hangar.SalvageKilograms);

		screen.Cancel();
		Assert.Equal(1_000_000, hangar.SalvageKilograms);
		Assert.Equal(50, screen.Machine!.Condition(ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(50, screen.Machine.Condition(ShellRepairCategory.Internal, 2));
		Assert.Equal(0, screen.SelectedBay);
		Assert.Equal((1, 2), (screen.SelectedColumn, screen.SelectedRow));
	}

	/// <summary>
	/// The snapshot is retaken on every bay change, so CANCEL undoes only the work on the bay now
	/// selected, and the pool it restores already carries what the earlier bay's repairs cost.
	/// </summary>
	[Fact]
	public void CancelUndoesOnlyTheBaySelectedSinceTheLastChange() {
		var hangar = HangarWith(salvage: 1_000_000,
			BayEntry(mountCapacity: 0, weapons: Array.Empty<int>(), groupCondition: 50),
			BayEntry(mountCapacity: 0, weapons: Array.Empty<int>(), groupCondition: 50));
		var screen = ScreenFor(hangar);

		screen.Repair();
		int afterFirstBay = hangar.SalvageKilograms;

		Assert.True(screen.SelectBay(1));
		screen.Repair();
		screen.Cancel();

		Assert.Equal(afterFirstBay, hangar.SalvageKilograms);
		Assert.Equal(50, hangar.Bay(1)!.Condition(ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(60, hangar.Bay(0)!.Condition(ShellRepairCategory.ExternalGroup, 0));
	}

	/// <summary>
	/// Every entry retakes the snapshot and puts the selection back on the first row, as
	/// <c>Repair_Enter</c> (<c>004332ec</c>) does with <c>Repair_SelectHotspot(0, 0)</c>.
	/// </summary>
	[Fact]
	public void EnteringRetakesTheSnapshotAndResetsTheSelection() {
		var hangar = HangarWith(salvage: 1_000_000, BayEntry(mountCapacity: 0, weapons: Array.Empty<int>(),
			groupCondition: 50, internalCondition: 50));
		var screen = ScreenFor(hangar);

		screen.Select(1, 4);
		screen.Repair();
		int afterRepair = hangar.SalvageKilograms;
		Assert.True(afterRepair < 1_000_000);

		screen.Enter();
		Assert.Equal((0, 0), (screen.SelectedColumn, screen.SelectedRow));

		screen.Repair();
		screen.Cancel();
		Assert.Equal(afterRepair, hangar.SalvageKilograms);
		Assert.Equal(50, screen.Machine!.Condition(ShellRepairCategory.ExternalGroup, 0));
		Assert.Equal(60, screen.Machine.Condition(ShellRepairCategory.Internal, 4));
	}

	/// <summary><c>gam\damage.dat</c> round-trips: the walk is fixed-length apart from its one count.</summary>
	[Fact]
	public void RoundTripsTheComponentValueTable() {
		var transformer = new DamageRepairCostTransformer();
		byte[] bytes = transformer.Write(RetailValues())!;

		// Two scale words, six shares, nine shares, a count and 33 values.
		Assert.Equal(2 * (1 + 6 + 9 + 1 + 1 + 33), bytes.Length);

		var parsed = transformer.Parse(bytes)!;
		Assert.Equal(800, parsed.ChassisScale);
		Assert.Equal(1000, parsed.WeaponScale);
		Assert.Equal(33, parsed.WeaponValue.Length);
		Assert.Equal(RetailValues().ExternalGroupPercent, parsed.ExternalGroupPercent);
		Assert.Equal(RetailValues().InternalPercent, parsed.InternalPercent);
	}

	/// <summary>The retail file's own contents, which every figure above is derived from.</summary>
	private static DamageRepairCost RetailValues() => new() {
		ChassisScale = 800,
		ExternalGroupPercent = new short[] { 100, 50, 50, 50, 100, 100 },
		InternalPercent = new short[] { 100, 100, 50, 50, 25, 100, 25, 25, 25 },
		WeaponScale = 1000,
		WeaponValue = new short[33],
	};

	private static ShellRepairScreen ScreenFor(HercBayEntry entry, int salvage = 0) {
		var save = new PlayerSave { SalvageTotal = salvage };
		save.HercBay[0] = entry;
		return new ShellRepairScreen(ShellHangar.From(save),
			ShellRepairCosts.Expand(RetailValues(), new short[] { 100 }));
	}

	/// <summary>A hangar with one machine per entry, from bay 0 up.</summary>
	private static ShellHangar HangarWith(int salvage, params HercBayEntry[] entries) {
		var save = new PlayerSave { SalvageTotal = salvage };
		for (short bay = 0; bay < entries.Length; bay++) {
			save.HercBay[bay] = entries[bay];
		}

		return ShellHangar.From(save);
	}

	/// <summary>A screen over <paramref name="hangar"/>, entered as the host enters it, so its snapshot is taken.</summary>
	private static ShellRepairScreen ScreenFor(ShellHangar hangar) {
		var screen = new ShellRepairScreen(hangar, ShellRepairCosts.Expand(RetailValues(), new short[] { 100 }));
		screen.Enter();
		return screen;
	}

	private static ShellBayMachine Machine(int mountCapacity, int[] weapons, int chassisType = 0,
			int groupCondition = 100, int internalCondition = 100) =>
		ShellBayMachine.From(BayEntry(mountCapacity, weapons, chassisType, groupCondition,
			internalCondition));

	private static HercBayEntry BayEntry(int mountCapacity, int[] weapons, int chassisType = 0,
			int groupCondition = 100, int internalCondition = 100) {
		var entry = new HercBayEntry {
			ChassisType = HercLUT.GetById((short)chassisType),
			MountCapacity = (short)mountCapacity,
			BuildPercent = 100,
			ExternalConditions = new Dictionary<HercExternals, ShellHercPart>(),
			InternalConditions = new Dictionary<HercInternals, ShellHercPart>(),
		};

		foreach (var facet in HercExternals.Values()) {
			entry.ExternalConditions[facet] = new ShellHercPart(facet.Id, facet.Label, (short)groupCondition);
		}

		foreach (var component in HercInternals.Values()) {
			if (component.Id < HercInternals.ServosLegLeftRear.Id) {
				entry.InternalConditions[component] =
					new ShellHercPart(component.Id, component.Label, (short)internalCondition);
			}
		}

		for (short slot = 0; slot < entry.HardpointConditions.Length; slot++) {
			entry.HardpointConditions[slot] = new ShellHercPart(slot, "hardpoint_" + slot, 100);
			if (slot < weapons.Length && weapons[slot] != 0) {
				entry.Mounts[slot] = new ShellWeaponEntry { Id = WeaponLUT.GetById(weapons[slot]) };
			}
		}

		return entry;
	}
}
