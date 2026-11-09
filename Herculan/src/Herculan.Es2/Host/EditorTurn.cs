using Herculan.Engine.Platform;
using Herculan.Es2.World;

namespace Herculan.Es2.Host;

/// <summary>
/// One turn of the mission editor, in <paramref name="window"/> on <paramref name="session"/>'s install, opening
/// <paramref name="mission"/> or, for null, the mission it last had open. The host builds it, since the editor's assembly
/// references this one; <see cref="Launcher"/> runs it between the shell's turns.
/// </summary>
public delegate EditorTurnEnd EditorTurn(HostSession session, HostWindow window, string? mission);

/// <summary>How a turn of the mission editor ended.</summary>
public abstract record EditorTurnEnd {
	private EditorTurnEnd() { }

	/// <summary>Its window was closed, which ends the run.</summary>
	public sealed record Closed : EditorTurnEnd;

	/// <summary>Back to game: the shell comes up again.</summary>
	public sealed record BackToShell : EditorTurnEnd;

	/// <summary>Play: the mission runs, then the editor opens again on it.</summary>
	/// <param name="Mission">The <c>.MSN</c> name the editor opens again once the mission ends.</param>
	/// <param name="Launch">The handoff the simulator runs.</param>
	public sealed record Play(string Mission, ShellLaunch Launch) : EditorTurnEnd;
}
