using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The startup sequence that first brings the main menu up, drawn through palette 1 as the startup
/// installs it before showing the sequence. A run staged on another screen, or for a screenshot,
/// starts without it — the staging flags are this engine's own. A return from a mission puts it up only
/// where the debrief sends the player back to the menu (CampaignScreens.ReturnFromMission).
/// </summary>
sealed class StartupScreen {
	private readonly ShellCanvas _canvas;
	private readonly GameContent _content;
	private readonly ShellAudio _audio;
	private readonly Action _repaint;
	private ShellStartupSequence? _sequence;
	private ShellImage?[] _frames = Array.Empty<ShellImage?>();

	public StartupScreen(ShellCanvas canvas, GameContent content, ShellAudio audio, Action repaint) {
		_canvas = canvas;
		_content = content;
		_audio = audio;
		_repaint = repaint;
	}

	/// <summary>Whether this turn has put the sequence up at all.</summary>
	public bool Present => _sequence != null;

	/// <summary>Whether the sequence is still running. It is the only widget up and takes no mouse events.</summary>
	public bool Running => _sequence is { Done: false };

	/// <summary>
	/// Whether the startup is in the blank it makes just before it shows the sequence, before which nothing is drawn but
	/// the intro movies.
	/// </summary>
	public bool BlankBeforeShow => _sequence is { Done: false, IsUp: false };

	/// <summary>
	/// The startup sequence up, with its frames, where it has not been: a first start, and wherever a return
	/// from the simulator goes back to the menu by way of it.
	/// </summary>
	public void Begin() {
		_sequence = new ShellStartupSequence();
		_frames = ShellStartupSequence.FrameNames.Select(name => _canvas.Art.LoadBitmap(_content, name)).ToArray();
	}

	/// <summary>
	/// One update of the startup sequence: shown on the first, then its alarm's ticks, the last of which
	/// hides it and puts the menu up.
	/// </summary>
	public void Advance() {
		if (_sequence == null) {
			return;
		}

		long now = Environment.TickCount64;
		if (!_sequence.IsUp) {
			_sequence.Show(now);
		} else if (!_sequence.Advance(now, () => _audio.Sound?.PlaySwitch())) {
			return;
		}

		_canvas.SetBackdropOverride(_sequence.Done ? null : _frames[_sequence.Frame]);
		if (_sequence.Done) {
			_repaint();
		}
	}
}
