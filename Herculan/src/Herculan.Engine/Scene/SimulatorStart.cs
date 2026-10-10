using Herculan.Engine.Audio;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Scene;

/// <summary>
/// One mission brought up for the simulator's turn, before its window opens: the handoff it reads, the
/// archives and scene built from it, its audio and message ports, the install's simulator preferences, and the input tape
/// it replays or records.
/// </summary>
public sealed class SimulatorStart {
	public required string InstallRoot { get; init; }
	public required GameDisc? Disc { get; init; }
	public required GameContent Content { get; init; }
	public required MissionScene Scene { get; init; }
	public required GameAudio Audio { get; init; }

	/// <summary>The cockpit's message ports and the coarse clock they run on.</summary>
	public required MessagePorts Ports { get; init; }
	public required SimulatorPreferences Preferences { get; init; }

	/// <summary>The script.dat the mission was loaded from; results.dat and mission.var are written beside it.</summary>
	public required string ScriptPath { get; init; }

	/// <summary>The folder holding prefs.cfg, keyjoy.cfg and Herculan's own device map.</summary>
	public required string? DataDirectory { get; init; }

	public required ShellLaunch? ShellLaunch { get; init; }
	public required InputTapePlayer? TapePlayer { get; init; }
	public required InputTapeRecorder? TapeRecorder { get; init; }
	public required bool DemoTape { get; init; }

	/// <summary>Whether the window stays windowed whatever option 6 says.</summary>
	public required bool KeptWindowed { get; init; }
	public required bool StartFullScreen { get; init; }

	/// <summary>The preferences panel's two gates: whether a sound device came up, and whether the voice archive is there.</summary>
	public required bool SoundAvailable { get; init; }
	public required bool VoiceAvailable { get; init; }

	/// <summary>
	/// The squad: the player.mec entries other than the player's own, in file order — the order the comm boxes
	/// are numbered in, and the three machines the original keeps in g_SquadmateMachines (004d044c).
	/// </summary>
	public required IReadOnlyList<SceneObject> SquadPlacements { get; init; }

	/// <summary>Whether the player's machine is the RAZOR — DBSim_LoadScriptDat's own <c>playerMechType == 8</c>.</summary>
	public required bool PilotingRazor { get; init; }

	public Mission Mission => Scene.Mission;
	public SimWorld World => Scene.World;
}
