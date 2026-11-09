namespace Herculan.Es1.Host;

/// <summary>The flags only a Metaltech: Earthsiege install takes.</summary>
public sealed class Es1Options {
	/// <summary>The flag that runs the simulator on the install's <c>data\</c> files without the shell.</summary>
	public const string SimulatorFlag = "--sim";

	/// <summary>Whether <c>--theater</c> or <c>--world</c> was given, which an Earthsiege 2 install refuses.</summary>
	public bool Staged { get; set; }

	/// <summary>Whether <see cref="SimulatorFlag"/> was given.</summary>
	public bool Simulator { get; set; }

	/// <summary>
	/// The arguments handed to <c>ESCD.EXE</c>'s launcher, in their order on the command line, which it hands on to the
	/// shell and the simulator (<c>Es1Launcher</c>).
	/// </summary>
	public List<string> ShellArguments { get; } = [];

	/// <summary>
	/// Whether <paramref name="argument"/> is one of <c>GO.EXE</c>'s switches as the host takes them: a single <c>-</c>
	/// and a letter (<c>--</c> opens the host's own flags), or <c>/</c> and a letter alone, since a longer <c>/</c> argument
	/// is a path. Retail's parser reads only the letter after either (<c>Es1ShellSwitches</c>).
	/// </summary>
	public static bool IsShellSwitch(string argument) =>
		(argument.Length >= 2 && argument[0] == '-' && argument[1] != '-' && char.IsLetter(argument[1]))
		|| (argument.Length == 2 && argument[0] == '/' && char.IsLetter(argument[1]));
	public int? Theater { get; set; }
	public int? WorldVariant { get; set; }
	public int? WorldDetail { get; set; }

	/// <summary>The header words the flags override, null for each one not given.</summary>
	public Es1HeaderOverrides ToOverrides() => new(Theater, WorldVariant, WorldDetail);
}
