using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The main menu's startup sequence, CONTINUE GAME's END OF GAME alert, INSTANT ACTION's rows past the
/// practice list, and the strip the menu hides. See docs/retail/shell/screen-layout.md#the-main-menu.
/// </summary>
public class ShellMainMenuTests {
	private const long Start = 10_000;
	private const int Tick = ShellStartupSequence.TickMilliseconds;

	/// <summary>The show puts frame 0 up, and nothing moves until the first 500 ms tick.</summary>
	[Fact]
	public void StartupShowsFrameZeroUntilTheFirstTick() {
		var startup = new ShellStartupSequence();
		int switches = 0;

		startup.Show(Start);
		Assert.True(startup.IsUp);
		Assert.Equal(0, startup.Frame);

		Assert.False(startup.Advance(Start + Tick - 1, () => switches++));
		Assert.Equal(0, startup.Frame);
		Assert.Equal(0, switches);
	}

	/// <summary>
	/// One frame a tick, the switch sound on the first tick only, and the menu at frame 5, 2.5 s after the
	/// show — when the widget goes down and stops advancing.
	/// </summary>
	[Fact]
	public void StartupPlaysTheSwitchOnceAndEndsAtFrameFive() {
		var startup = new ShellStartupSequence();
		int switches = 0;
		startup.Show(Start);

		for (int tick = 1; tick < 5; tick++) {
			Assert.True(startup.Advance(Start + tick * Tick, () => switches++));
			Assert.Equal(tick, startup.Frame);
			Assert.True(startup.IsUp);
			Assert.False(startup.Done);
		}

		Assert.Equal(1, switches);

		Assert.True(startup.Advance(Start + 5 * Tick, () => switches++));
		Assert.Equal(5, startup.Frame);
		Assert.True(startup.Done);
		Assert.False(startup.IsUp);

		Assert.False(startup.Advance(Start + 20 * Tick, () => switches++));
		Assert.Equal(5, startup.Frame);
		Assert.Equal(1, switches);
	}

	/// <summary>An update late by several ticks runs every one of them, as the alarm's queued ticks do.</summary>
	[Fact]
	public void StartupCatchesUpOnMissedTicks() {
		var startup = new ShellStartupSequence();
		startup.Show(Start);

		Assert.True(startup.Advance(Start + 3 * Tick + 100, () => { }));
		Assert.Equal(3, startup.Frame);

		startup.Advance(Start + 4 * Tick, () => { });
		Assert.Equal(4, startup.Frame);
	}

	/// <summary>The six frames are bay2a_80 to bay2a_84, the last twice.</summary>
	[Fact]
	public void StartupFramesRepeatTheLastBackdrop() {
		Assert.Equal(new[] { "BAY2A_80", "BAY2A_81", "BAY2A_82", "BAY2A_83", "BAY2A_84", "BAY2A_84" },
			ShellStartupSequence.FrameNames);
	}

	/// <summary>FUN_0044cecf's reasons: state 0 the war lost, 1 the cybrids defeated, 3 killed.</summary>
	[Theory]
	[InlineData(0, 0x130)]
	[InlineData(1, 0x131)]
	[InlineData(3, 0x12f)]
	public void EndOfGameNamesTheReason(int state, int text) {
		var dialog = new ShellEndOfGameDialog();

		dialog.Open(state);

		Assert.True(dialog.IsOpen);
		Assert.Equal(text, dialog.FirstLineText);
	}

	/// <summary>
	/// A state with no line leaves the first line as it was — the builder's empty entry 0 at first, the
	/// last reason afterwards.
	/// </summary>
	[Fact]
	public void EndOfGameKeepsTheLineForAnUnnamedState() {
		var dialog = new ShellEndOfGameDialog();

		dialog.Open(4);
		Assert.Equal(0, dialog.FirstLineText);

		dialog.Open(3);
		dialog.Close();
		dialog.Open(4);
		Assert.Equal(0x12f, dialog.FirstLineText);
	}

	/// <summary>While the alert is up only OKAY answers; OKAY takes it down.</summary>
	[Fact]
	public void EndOfGameAnswersOnlyOnOkay() {
		var dialog = new ShellEndOfGameDialog();
		dialog.Open(0);
		var okay = ShellEndOfGameDialog.OkayButtonRect;

		Assert.Equal(ShellWidgetKind.EndOfGameOkay, dialog.HitAt(okay.X0 + 1, okay.Y0 + 1)?.Widget.Kind);
		Assert.Null(dialog.HitAt(ShellEndOfGameDialog.PanelRect.X0 + 2, ShellEndOfGameDialog.PanelRect.Y0 + 2));

		dialog.Close();
		Assert.False(dialog.IsOpen);
	}

	/// <summary>
	/// INSTANT ACTION's rows 8-10 write the table's chassis — Apocalypse, Maverick, Samson — into option 40,
	/// light no listed row and leave Herc Type's greying as the last listed row left it.
	/// </summary>
	[Theory]
	[InlineData(8, 5)]
	[InlineData(9, 7)]
	[InlineData(10, 3)]
	public void InstantActionRowsWriteTheirChassis(int row, int chassis) {
		var options = SimulatorPreferences.Defaults();
		var practice = new ShellPracticeScreen(options);
		practice.Show();
		Assert.False(practice.IsEnabled(ShellPracticeButton.HercType));

		Assert.True(practice.SelectRow(row));

		Assert.Equal(row, practice.SelectedRow);
		Assert.Equal(chassis, options[ShellPracticeScreen.HercTypeOption]);
		Assert.False(practice.IsEnabled(ShellPracticeButton.HercType));

		Assert.True(practice.SelectRow(4));
		Assert.True(practice.SelectRow(row));
		Assert.True(practice.IsEnabled(ShellPracticeButton.HercType));
	}

	/// <summary>No row past INSTANT ACTION's three is taken.</summary>
	[Fact]
	public void PracticeRejectsRowsPastTheStage() {
		var practice = new ShellPracticeScreen(SimulatorPreferences.Defaults());

		Assert.False(practice.SelectRow(ShellPracticeScreen.SelectableRowCount));
		Assert.False(practice.SelectRow(-1));
	}

	/// <summary>
	/// The main menu and the save screen hide the strip, and the tabs from WEAPONS on show it; the bare
	/// frame puts it back with no tab up.
	/// </summary>
	[Theory]
	[InlineData(ShellScreen.MainMenuTab, false)]
	[InlineData(ShellScreen.SaveTab, false)]
	[InlineData(ShellScreen.WeaponsTab, true)]
	[InlineData(ShellScreen.MissionTab, true)]
	public void OnlyTheLatchingTabsShowTheStrip(int tab, bool visible) {
		var screen = ShellScreen.CreateFrame(null, ShellScreen.CrewTab);

		screen.SelectTab(tab);

		Assert.Equal(visible, screen.StripVisible);
		Assert.Equal(visible, screen.HitAt(ShellLayout.Tab(3).X0 + 1, ShellLayout.TabTop + 1) != null);
	}

	/// <summary>A frame built on the main menu, as the shell starts, has no strip until the bare frame.</summary>
	[Fact]
	public void TheShellStartsWithTheStripHidden() {
		var screen = ShellScreen.CreateFrame(null);
		Assert.False(screen.StripVisible);

		screen.ReturnToFrame(ShellCampaignMode.Campaign);

		Assert.True(screen.StripVisible);
		Assert.Equal(ShellScreen.NoTab, screen.SelectedTab);
	}
}
