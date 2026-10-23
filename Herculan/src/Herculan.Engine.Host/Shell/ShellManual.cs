using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The shell's way into the on-line manual, <c>OnlineManual_Open</c> (<c>004317ea</c>), which both the main menu's
/// <c>ONLINE MANUAL</c> and the strip's square button run (docs/retail/shell/screen-layout.md, "What a tab click does").
/// </summary>
sealed class ShellManual(string installRoot, GameDisc? disc, FrontEndWindow window, GameInProgress game, ShellAudio audio) {
	// Out of full screen with option 6 written to match, the options committed and all 54 saved, the shell's sound
	// stopped, then the help file — here through OnlineManual, since there is no WinHelp to hand it to.
	public void Open() {
		if (window.FullScreen) {
			window.ToggleFullScreen();
		}

		game.Options.Set(Prefs.DisplayModeOption, 0);
		game.Options.Commit();
		game.Options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
		audio.Sound?.Stop();
		OnlineManual.Open(installRoot, disc);
	}
}
