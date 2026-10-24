using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The shell's way into the on-line manual, <c>OnlineManual_Open</c> (<c>004317ea</c>), which both the main menu's
/// <c>ONLINE MANUAL</c> and the strip's square button run (docs/retail/shell/screen-layout.md, "What a tab click does").
/// </summary>
sealed class ShellManual(string installRoot, GameDisc? disc, GameInProgress game) {
	// The options committed and all 54 saved, then the help file — here through OnlineManual, since there is no WinHelp
	// to hand it to. Retail first leaves full screen and writes option 6 to match, and stops the shell's sound; this
	// stays full screen behind the browser, so option 6 is left as it is, and leaves the sound to the focus change
	// (ShellAudio.FocusChanged), so a browser that never takes the focus leaves the music playing (KNOWN_ISSUES.md).
	public void Open() {
		game.Options.Commit();
		game.Options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
		OnlineManual.Open(installRoot, disc);
	}
}
