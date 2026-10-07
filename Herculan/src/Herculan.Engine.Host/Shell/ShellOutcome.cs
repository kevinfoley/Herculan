using Herculan.Engine.World;
namespace Herculan.Engine.Host.Shell;

/// <summary>
/// How the shell's turn is ending, read once its window has gone: the exit code and the mission handed over, which
/// <see cref="ShellHost.Run"/> returns to the launcher, and the blank the window ends on.
/// </summary>
sealed class ShellOutcome {
	/// <summary>
	/// <c>Shell_SetExitCode</c>'s code: the 0 the startup leaves, <see cref="ShellHost.DemoExitCode"/>, or
	/// <see cref="ShellHost.SettingsRestartCode"/>.
	/// </summary>
	public int ExitCode { get; set; }

	/// <summary>The mission the shell hands over, which ends it on exit code 2.</summary>
	public ShellLaunch? Launch { get; set; }

	/// <summary>Set by QUIT, whose blank is the last thing the window shows.</summary>
	public bool Blanked { get; set; }
}
