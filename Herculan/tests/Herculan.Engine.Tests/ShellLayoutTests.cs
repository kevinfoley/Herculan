using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The shell's geometry and its widget interaction, both of which are pure and neither of which
/// touches GL. Worth pinning for the same reason <see cref="CockpitScreenLayoutTests"/> pins the
/// cockpit's: the failure mode is a click region sitting a few pixels off the button it belongs to,
/// which is invisible until someone misses a click.
/// </summary>
public class ShellLayoutTests {
	/// <summary>The id of the button under a canvas point, asserting there is one.</summary>
	private static int ButtonIdAt(ShellScreen screen, float x, float y) {
		var button = screen.ButtonAt(x, y);
		Assert.NotNull(button);
		return button.Id;
	}

	/// <summary>
	/// The eight tab rects, straight out of <c>ServiceBay_BuildScreen</c>'s immediates. The strip is
	/// generated from a pitch rather than a table, so this is what says the pitch is right — get it
	/// wrong by one and tab 0 still lands correctly while tab 7 is eight pixels out.
	/// </summary>
	[Theory]
	[InlineData(0, 0x19, 0x63)]
	[InlineData(1, 0x65, 0xaf)]
	[InlineData(2, 0xb1, 0xfb)]
	[InlineData(3, 0xfd, 0x147)]
	[InlineData(4, 0x149, 0x193)]
	[InlineData(5, 0x195, 0x1df)]
	[InlineData(6, 0x1e1, 0x22b)]
	[InlineData(7, 0x22d, 0x277)]
	public void TabRectsMatchTheOriginalsImmediates(int index, int x0, int x1) {
		var tab = ShellLayout.Tab(index);

		Assert.Equal(new ShellRect(x0, 4, x1, 0x1b), tab);
		Assert.Equal(75, tab.Width);
		Assert.Equal(24, tab.Height);
	}

	/// <summary>A spread of window shapes: the native 4:3, wider, taller, and one with no clean scale.</summary>
	public static TheoryData<int, int> Windows() {
		var data = new TheoryData<int, int>();
		foreach ((int w, int h) in new[] { (640, 480), (1280, 960), (1920, 480), (640, 960), (1367, 769) }) {
			data.Add(w, h);
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Windows))]
	public void WindowToCanvasAndBackIsIdentity(int width, int height) {
		var layout = ShellScreenLayout.Create(width, height);

		foreach ((float x, float y) in new[] { (0f, 0f), (1f, 1f), (319.5f, 240.25f), (639f, 479f) }) {
			var (windowX, windowY) = layout.CanvasToWindow(x, y);
			var (canvasX, canvasY) = layout.WindowToCanvas(windowX, windowY);

			Assert.Equal(x, canvasX, 3);
			Assert.Equal(y, canvasY, 3);
		}
	}

	/// <summary>
	/// On any window at least 4:3 the scale is purely the height ratio, the canvas is flush to the top
	/// and bottom, and whatever is left over is split evenly either side.
	/// </summary>
	[Theory]
	[InlineData(640, 480)]
	[InlineData(1280, 960)]
	[InlineData(1920, 480)]
	public void ScalesByHeightAndCentresHorizontally(int width, int height) {
		var layout = ShellScreenLayout.Create(width, height);

		Assert.Equal(height / (float)ShellLayout.CanvasHeight, layout.Scale, 4);
		Assert.Equal(0f, layout.OriginY, 4);

		var (_, top) = layout.CanvasToWindow(0f, 0f);
		var (_, bottom) = layout.CanvasToWindow(0f, ShellLayout.CanvasHeight);
		Assert.Equal(0f, top, 3);
		Assert.Equal(height, bottom, 3);

		var (left, _) = layout.CanvasToWindow(0f, 0f);
		var (right, _) = layout.CanvasToWindow(ShellLayout.CanvasWidth, 0f);
		Assert.Equal(left, width - right, 3);
	}

	/// <summary>
	/// A window narrower than 4:3 falls back to fitting by width, so no widget is pushed off the edge
	/// — see <see cref="ShellScreenLayout"/> for why that clamp is there.
	/// </summary>
	[Fact]
	public void NarrowWindowFitsByWidthInstead() {
		var layout = ShellScreenLayout.Create(640, 960);

		Assert.Equal(1f, layout.Scale, 4);
		Assert.Equal(0f, layout.OriginX, 4);
		Assert.Equal(240f, layout.OriginY, 4);
	}

	/// <summary>The frame's buttons answer for their own rects and for nothing between them.</summary>
	[Fact]
	public void HitTestPicksTheButtonUnderThePoint() {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.WeaponsTab);

		Assert.Equal(ShellScreen.MenuButtonId, ButtonIdAt(screen, 10, 10));
		Assert.Equal(0, ButtonIdAt(screen, ShellLayout.Tab(0).X0, ShellLayout.TabTop));
		Assert.Equal(7, ButtonIdAt(screen, ShellLayout.Tab(7).X1, ShellLayout.TabBottom));

		// The one-pixel gutter between two tabs, and the content area below the strip.
		Assert.Null(screen.ButtonAt(ShellLayout.Tab(0).X1 + 1, ShellLayout.TabTop));
		Assert.Null(screen.ButtonAt(320, ShellLayout.TabBottom + 1));
	}

	/// <summary>Exactly one tab is latched, and clicking another moves the latch to it.</summary>
	[Fact]
	public void SelectingATabLatchesOnlyThatOne() {
		var screen = ShellScreen.CreateFrame(null);
		Assert.Equal(0, screen.SelectedTab);

		screen.SelectTab(4);

		Assert.Equal(4, screen.SelectedTab);
		Assert.Equal(1, screen.Buttons.Count(button => button.ShowsLit));
		Assert.True(screen.Button(4)!.ShowsLit);

		// The menu button is not a tab and is never latched by one.
		Assert.False(screen.Button(ShellScreen.MenuButtonId)!.ShowsLit);
	}

	/// <summary>
	/// A tab fires on the left press, and the release that follows puts its lit flag out without
	/// repainting, so the face stays lit.
	/// </summary>
	[Fact]
	public void TabFiresOnTheLeftPress() {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.WeaponsTab);
		var pointer = new ShellPointer(screen);
		var tab = ShellLayout.Tab(3);
		var fired = new List<int>();

		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		pointer.Press(ShellMouseButton.Left, widget => fired.Add(widget.Index));
		Assert.Equal([3], fired);

		pointer.Move(screen.HitAt(320, ShellLayout.CanvasHeight - 1));
		pointer.Release(ShellMouseButton.Left, widget => fired.Add(widget.Index));
		Assert.Equal([3], fired);
		Assert.True(screen.Button(3)!.ShowsLit);
	}

	/// <summary>
	/// The right button fires a tab on its release, even after leaving it and coming back, and a tab
	/// latched that way is left drawn unlit.
	/// </summary>
	[Fact]
	public void TabFiresOnTheRightRelease() {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.WeaponsTab);
		var pointer = new ShellPointer(screen);
		var tab = ShellLayout.Tab(4);
		void Fire(ShellWidget widget) => screen.SelectTab(widget.Index);

		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		pointer.Press(ShellMouseButton.Right, Fire);
		Assert.Equal(ShellScreen.WeaponsTab, screen.SelectedTab);

		pointer.Move(screen.HitAt(320, ShellLayout.CanvasHeight - 1));
		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		pointer.Release(ShellMouseButton.Right, Fire);
		Assert.Equal(4, screen.SelectedTab);
		Assert.False(screen.Button(4)!.ShowsLit);
	}

	/// <summary>A disabled button neither answers a hit test nor takes a press.</summary>
	[Fact]
	public void DisabledButtonTakesNoClicks() {
		var screen = ShellScreen.CreateFrame(null);
		var pointer = new ShellPointer(screen);
		var tab = ShellLayout.Tab(2);
		screen.Button(2)!.Enabled = false;

		Assert.Null(screen.ButtonAt(tab.X0 + 1, tab.Y0 + 1));

		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		pointer.Press(ShellMouseButton.Left, _ => Assert.Fail("A disabled tab fired."));
	}

	/// <summary>
	/// The main menu and the save screen leave the whole strip unlit, where the six from WEAPONS on
	/// latch their own plate. Both are still the screen that is up.
	/// </summary>
	[Theory]
	[InlineData(ShellScreen.MainMenuTab, false)]
	[InlineData(ShellScreen.SaveTab, false)]
	[InlineData(ShellScreen.WeaponsTab, true)]
	[InlineData(ShellScreen.MissionTab, true)]
	public void OnlyTheTabsFromWeaponsOnLatch(int tab, bool latches) {
		var screen = ShellScreen.CreateFrame(null);

		screen.SelectTab(tab);

		Assert.Equal(tab, screen.SelectedTab);
		Assert.Equal(latches ? 1 : 0, screen.Buttons.Count(button => button.ShowsLit));
		Assert.Equal(latches, screen.Button(tab)!.ShowsLit);
	}

	/// <summary>
	/// The strip refresh's gate: the three tabs behind the salvage economy answer only in the campaign.
	/// It writes five tabs and no others, so MISSION stays live in training too.
	/// </summary>
	[Theory]
	[InlineData(ShellCampaignMode.Campaign, true)]
	[InlineData(ShellCampaignMode.Training, false)]
	public void TrainingGatesRepairBuildAndArmory(ShellCampaignMode mode, bool economy) {
		var screen = ShellScreen.CreateFrame(null, mode: mode);

		Assert.Equal(economy, screen.Button(ShellScreen.RepairTab)!.Enabled);
		Assert.Equal(economy, screen.Button(ShellScreen.BuildTab)!.Enabled);
		Assert.Equal(economy, screen.Button(ShellScreen.ArmoryTab)!.Enabled);

		Assert.True(screen.Button(ShellScreen.WeaponsTab)!.Enabled);
		Assert.True(screen.Button(ShellScreen.CrewTab)!.Enabled);
		Assert.True(screen.Button(ShellScreen.MainMenuTab)!.Enabled);
		Assert.True(screen.Button(ShellScreen.SaveTab)!.Enabled);
		Assert.True(screen.Button(ShellScreen.MissionTab)!.Enabled);
		Assert.True(screen.Button(ShellScreen.MenuButtonId)!.Enabled);
	}

	/// <summary>
	/// Each named index lands on its own entry of the table at <c>0046dcdc</c>, by name and back again,
	/// case-insensitively. A name inserted or dropped ahead of one moves it onto its neighbour. The
	/// stage-indexed runs are the two stage tests below.
	/// </summary>
	[Theory]
	[InlineData(ShellPalette.Interstitial, "intr_pt1")]
	[InlineData(ShellPalette.ServiceBay, "palette")]
	[InlineData(ShellPalette.Arming, "arming")]
	[InlineData(ShellPalette.CampaignMapEarth, "cam_er")]
	[InlineData(ShellPalette.CampaignMapMoon, "cam_moon")]
	public void NamedPalettesIndexTheirOwnEntry(int index, string name) {
		Assert.Equal(name, ShellPalette.Name(index), ignoreCase: true);
		Assert.Equal(index, ShellPalette.IndexOf(name));
	}

	/// <summary>An index outside the table, and a name it does not carry, both read as null.</summary>
	[Fact]
	public void PaletteLookupsOutsideTheTableAreNull() {
		Assert.Null(ShellPalette.Name(-1));
		Assert.Null(ShellPalette.Name(ShellPalette.Names.Length));
		Assert.Null(ShellPalette.IndexOf("NOSUCH"));
	}

	/// <summary>Which palette each tab is drawn through, as <c>Shell_SelectTabPalette</c> (<c>0043b162</c>) picks it.</summary>
	[Theory]
	[InlineData(ShellScreen.MainMenuTab, ShellPalette.ServiceBay)]
	[InlineData(ShellScreen.SaveTab, ShellPalette.ServiceBay)]
	[InlineData(ShellScreen.WeaponsTab, ShellPalette.Arming)]
	[InlineData(ShellScreen.RepairTab, ShellPalette.ServiceBay)]
	[InlineData(ShellScreen.BuildTab, ShellPalette.Arming)]
	[InlineData(ShellScreen.ArmoryTab, ShellPalette.Arming)]
	[InlineData(ShellScreen.CrewTab, ShellPalette.Arming)]
	public void TabPaletteMatchesTheOriginalsSwitch(int tab, int index) =>
		Assert.Equal(index, ShellPalette.ForTab(tab));

	/// <summary>
	/// The mission tab's three faces, each indexed by the campaign stage. The map's Earth-to-Moon
	/// boundary and the last briefing and debrief entries all land on stage 5, which is what says the
	/// stage counts from one at runtime.
	/// </summary>
	[Fact]
	public void MissionTabPaletteFollowsTheStage() {
		for (int stage = 1; stage <= ShellPalette.StageCount; stage++) {
			Assert.Equal($"BR_W{stage}",
				ShellPalette.Name(ShellPalette.ForTab(ShellScreen.MissionTab, ShellMissionView.Briefing, stage)!.Value));
			Assert.Equal($"DB_W{stage}",
				ShellPalette.Name(ShellPalette.ForTab(ShellScreen.MissionTab, ShellMissionView.Debriefing, stage)!.Value));

			Assert.Equal(stage == ShellPalette.LunarStage ? ShellPalette.CampaignMapMoon : ShellPalette.CampaignMapEarth,
				ShellPalette.ForTab(ShellScreen.MissionTab, ShellMissionView.Map, stage));
		}
	}

	/// <summary>
	/// The theater palette for a stage, from the mission screen's location update. The five run
	/// ALPH, DELT, OMIC, BRAV, LUNA — the same five names, in the same order, that the location art's
	/// own four-entry table names and stops one short of.
	/// </summary>
	[Fact]
	public void TheaterPaletteFollowsTheStage() {
		Assert.Equal("ALPH", ShellPalette.Name(ShellPalette.ForStage(1)));
		Assert.Equal("DELT", ShellPalette.Name(ShellPalette.ForStage(2)));
		Assert.Equal("OMIC", ShellPalette.Name(ShellPalette.ForStage(3)));
		Assert.Equal("BRAV", ShellPalette.Name(ShellPalette.ForStage(4)));
		Assert.Equal("LUNA", ShellPalette.Name(ShellPalette.ForStage(ShellPalette.LunarStage)));
	}
}
