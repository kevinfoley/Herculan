using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>The alerts that stand over whichever screen is up and take the pointer while they are.</summary>
sealed class ShellDialogs {
	/// <summary>
	/// The WARNING dialog both SCRAP buttons open, built once as the original builds it at startup. At most one of
	/// it and <see cref="WeaponScrap"/> is ever up.
	/// </summary>
	public ShellScrapDialog Scrap { get; } = ShellScrapDialog.Herc();

	/// <summary>The scrap dialog's twin, which the armory's Scrap opens.</summary>
	public ShellScrapDialog WeaponScrap { get; } = ShellScrapDialog.Weapons();

	/// <summary>The dialog Rock &amp; Roll refuses through.</summary>
	public ShellLaunchRefusalDialog LaunchRefusal { get; } = new();

	/// <summary>The END OF GAME alert CONTINUE GAME puts up over the menu when the game it loaded is over.</summary>
	public ShellEndOfGameDialog EndOfGame { get; } = new();

	/// <summary>REPLAY MISSION?, which the debrief puts up when the campaign ends.</summary>
	public ShellReplayDialog Replay { get; } = new();

	/// <summary>
	/// The widget under a canvas point on whichever dialog is up, and whether one is: an open dialog takes the
	/// pointer even where it has no widget.
	/// </summary>
	public bool TryHitAt(float canvasX, float canvasY, out ShellHit? hit) {
		if (Scrap.IsOpen) {
			hit = Scrap.HitAt(canvasX, canvasY);
		} else if (WeaponScrap.IsOpen) {
			hit = WeaponScrap.HitAt(canvasX, canvasY);
		} else if (LaunchRefusal.IsOpen) {
			hit = LaunchRefusal.HitAt(canvasX, canvasY);
		} else if (EndOfGame.IsOpen) {
			hit = EndOfGame.HitAt(canvasX, canvasY);
		} else if (Replay.IsOpen) {
			hit = Replay.HitAt(canvasX, canvasY);
		} else {
			hit = null;
			return false;
		}

		return true;
	}

	/// <summary>The dialogs that paint over the screen beneath them; REPLAY MISSION? paints alone (CanvasContent.Repaint).</summary>
	public void PaintOver(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites) {
		Scrap.Paint(surface, text, sprites);
		WeaponScrap.Paint(surface, text, sprites);
		LaunchRefusal.Paint(surface, text, sprites);
		EndOfGame.Paint(surface, text, sprites);
	}
}
