namespace Herculan.Engine.Cockpit;

/// <summary>
/// How the mission is ending, once something has decided it: Sim_Shutdown (00461eec) reads it after the window
/// has gone, to write the results and pick the exit code.
/// </summary>
public sealed class MissionOutcome {
	/// <summary>
	/// Whether the mission is over and only its window has still to go. It stops the last few frames before
	/// that from ticking or raising another panel.
	/// </summary>
	public bool Over { get; private set; }

	/// <summary>DAT_004d2582, the global quit flag EXIT EARTHSIEGE?'s QUIT sets, which makes the mission's exit code 0.</summary>
	public bool QuitGame { get; set; }

	/// <summary>Raised as the mission ends, for the host to close its window.</summary>
	public event Action? Ended;

	/// <summary>Ends the mission: an answer on the status alert, or a demo running out.</summary>
	public void End(bool quitGame = false) {
		Over = true;
		QuitGame = quitGame;
		Ended?.Invoke();
	}
}
