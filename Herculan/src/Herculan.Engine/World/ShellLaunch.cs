namespace Herculan.Engine.World;

/// <summary>
/// A mission handed over by the shell — <c>Rock &amp; Roll &gt;</c> or a training launch — or by a mission
/// named on the command line: the <c>script.dat</c> the handoff was written beside, and the folder the
/// simulator's own settings are read from and written back to.
/// </summary>
public sealed record ShellLaunch(string ScriptPath, string DataDirectory);
