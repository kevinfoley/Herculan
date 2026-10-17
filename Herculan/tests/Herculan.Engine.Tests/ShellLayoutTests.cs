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
	/// The first and last tab rects, straight out of <c>ServiceBay_BuildScreen</c>'s immediates. The
	/// strip is generated from a start and a pitch rather than a table, so the two ends are what say
	/// both are right — get the pitch wrong by one and tab 0 still lands correctly while tab 7 is seven
	/// pixels out.
	/// </summary>
	[Theory]
	[InlineData(0, 0x19, 0x63)]
	[InlineData(7, 0x22d, 0x277)]
	public void TabRectsMatchTheOriginalsImmediates(int index, int x0, int x1) {
		Assert.Equal(new ShellRect(x0, 4, x1, 0x1b), ShellLayout.Tab(index));
	}

	/// <summary>
	/// The round trip, on a window taller than 4:3, which puts the canvas below a top margin, and on one
	/// wider, with no clean scale, which puts it right of a left margin.
	/// </summary>
	[Theory]
	[InlineData(640, 960)]
	[InlineData(1367, 769)]
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
	/// On a window wider than 4:3 the scale is purely the height ratio, the canvas is flush to the top
	/// and bottom, and whatever is left over is split evenly either side: 1600x900 scales by 900/480 =
	/// 1.875 rather than the width's 2.5, and the 1200-pixel canvas leaves 200 either side.
	/// </summary>
	[Fact]
	public void ScalesByHeightAndCentresHorizontally() {
		var layout = ShellScreenLayout.Create(1600, 900);

		Assert.Equal(1.875f, layout.Scale, 4);
		Assert.Equal((200f, 0f), layout.CanvasToWindow(0f, 0f));
		Assert.Equal((1400f, 900f), layout.CanvasToWindow(ShellLayout.CanvasWidth, ShellLayout.CanvasHeight));
	}

	/// <summary>
	/// A window narrower than 4:3 falls back to fitting by width, so no widget is pushed off the edge
	/// — see <see cref="ShellScreenLayout"/> for why that clamp is there. 800x1200 scales by 800/640 =
	/// 1.25 rather than the height's 2.5, and the 600-pixel canvas leaves 300 above and below.
	/// </summary>
	[Fact]
	public void NarrowWindowFitsByWidthInstead() {
		var layout = ShellScreenLayout.Create(800, 1200);

		Assert.Equal(1.25f, layout.Scale, 4);
		Assert.Equal(0f, layout.OriginX, 4);
		Assert.Equal(300f, layout.OriginY, 4);
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

	/// <summary>
	/// A tab fires on the left press, and the release over it neither fires again nor repaints: it
	/// puts the lit flag out, so the face the handler latched stays drawn lit.
	/// </summary>
	[Fact]
	public void TabFiresOnTheLeftPress() {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.WeaponsTab);
		var pointer = new ShellPointer(screen);
		var tab = ShellLayout.Tab(3);
		var fired = new List<int>();
		void Fire(ShellWidget widget) {
			fired.Add(widget.Index);
			screen.SelectTab(widget.Index);
		}

		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		pointer.Press(ShellMouseButton.Left, Fire);
		Assert.Equal([3], fired);

		pointer.Release(ShellMouseButton.Left, Fire);
		Assert.Equal([3], fired);
		Assert.False(screen.Button(3)!.Lit);
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

	/// <summary>
	/// A disabled button does not answer a hit test, and one disabled while the pointer is already on
	/// it does not take the press either: the strip's handler ignores the click itself.
	/// </summary>
	[Fact]
	public void DisabledButtonTakesNoClicks() {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.WeaponsTab);
		var pointer = new ShellPointer(screen);
		var tab = ShellLayout.Tab(4);
		pointer.Move(screen.HitAt(tab.X0 + 1, tab.Y0 + 1));
		screen.Button(4)!.Enabled = false;

		Assert.Null(screen.ButtonAt(tab.X0 + 1, tab.Y0 + 1));

		pointer.Press(ShellMouseButton.Left, _ => Assert.Fail("A disabled tab fired."));
		Assert.False(screen.Button(4)!.Lit);
	}

	/// <summary>
	/// The main menu and the save screen leave the whole strip unlit and hide it, where the six from
	/// WEAPONS on latch their own plate, alone, and show it. All four are the screen that is up. The
	/// frame starts on CREW, latched, so it is the clear ahead of the latch that puts CREW's plate out.
	/// </summary>
	[Theory]
	[InlineData(ShellScreen.MainMenuTab, false)]
	[InlineData(ShellScreen.SaveTab, false)]
	[InlineData(ShellScreen.WeaponsTab, true)]
	[InlineData(ShellScreen.MissionTab, true)]
	public void OnlyTheTabsFromWeaponsOnLatchAndShowTheStrip(int tab, bool latches) {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.CrewTab);

		screen.SelectTab(tab);

		Assert.Equal(tab, screen.SelectedTab);
		Assert.Equal(latches ? 1 : 0, screen.Buttons.Count(button => button.ShowsLit));
		Assert.Equal(latches, screen.Button(tab)!.ShowsLit);
		Assert.Equal(latches, screen.StripVisible);
		Assert.Equal(latches, screen.HitAt(ShellLayout.Tab(3).X0 + 1, ShellLayout.TabTop + 1) != null);
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
	/// The theater palette for a stage, from the mission screen's location update: the run starts on
	/// ALPH at stage 1 and steps one entry a stage, to BRAV at stage 4, the last the location update
	/// reaches.
	/// </summary>
	[Fact]
	public void TheaterPaletteFollowsTheStage() {
		Assert.Equal("ALPH", ShellPalette.Name(ShellPalette.ForStage(1)));
		Assert.Equal("BRAV", ShellPalette.Name(ShellPalette.ForStage(4)));
	}
}
