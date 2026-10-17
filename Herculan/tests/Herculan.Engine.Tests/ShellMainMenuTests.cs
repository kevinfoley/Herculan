using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The main menu's startup sequence, CONTINUE GAME's END OF GAME alert, INSTANT ACTION's rows past the
/// practice list, and the strip the menu hides. See docs/retail/shell/main-menu.md#the-main-menu.
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

	/// <summary>
	/// An update late by several ticks runs one, and the next comes a whole period after it, as the alarm
	/// drops the time it overshot.
	/// </summary>
	[Fact]
	public void StartupDropsMissedTicks() {
		var startup = new ShellStartupSequence();
		startup.Show(Start);

		Assert.True(startup.Advance(Start + 3 * Tick + 100, () => { }));
		Assert.Equal(1, startup.Frame);

		Assert.False(startup.Advance(Start + 4 * Tick + 99, () => { }));
		Assert.Equal(1, startup.Frame);

		Assert.True(startup.Advance(Start + 4 * Tick + 100, () => { }));
		Assert.Equal(2, startup.Frame);
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

	/// <summary>
	/// While the alert is up only OKAY answers, where the builder puts it inside the alert: 0x41-0xa5
	/// across and 0x45-0x54 down from the alert's corner at (0xcf, 0xcb), so over canvas (300, 280).
	/// The alert's own body just above it answers nothing. OKAY's handler takes the alert down.
	/// </summary>
	[Fact]
	public void EndOfGameAnswersOnlyOnOkay() {
		var dialog = new ShellEndOfGameDialog();
		dialog.Open(0);

		Assert.Equal(ShellWidgetKind.EndOfGameOkay, dialog.HitAt(300, 280)?.Widget.Kind);
		Assert.Null(dialog.HitAt(300, 260));

		dialog.Close();
		Assert.False(dialog.IsOpen);
	}

	/// <summary>
	/// INSTANT ACTION's rows past the eight listed — its first and last are here — write the table's
	/// chassis, Apocalypse and Samson, into option 40, light no listed row and leave Herc Type's greying
	/// as the last listed row left it.
	/// </summary>
	[Theory]
	[InlineData(8, 5)]
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
	/// A frame built on the main menu, as the shell starts, has no strip until the bare frame, which
	/// shows it with no tab up and regated for the mode it is given. Which tabs show the strip is
	/// <see cref="ShellLayoutTests.OnlyTheTabsFromWeaponsOnLatchAndShowTheStrip"/>.
	/// </summary>
	[Fact]
	public void TheShellStartsWithTheStripHidden() {
		var screen = ShellScreen.CreateFrame(null, mode: ShellCampaignMode.Training);
		Assert.False(screen.StripVisible);

		screen.ReturnToFrame(ShellCampaignMode.Campaign);

		Assert.True(screen.StripVisible);
		Assert.Equal(ShellScreen.NoTab, screen.SelectedTab);
		Assert.True(screen.Button(ShellScreen.RepairTab)!.Enabled);
	}
}
