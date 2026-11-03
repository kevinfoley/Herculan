namespace Herculan.Es1.Shell;

/// <summary>
/// The switches <c>Shell_ParseCommandLine</c> (<c>ES1GO 0001272d</c>) reads (docs/es1/shell/startup.md#the-command-line):
/// <c>-A</c>, <c>-C</c>, <c>-M</c>, <c>-S</c> and <c>-X</c>, each also written with <c>/</c> and in either case.
///
/// <para><b>This engine's own:</b> <c>-eggplant</c> is read but not required. Without it retail's shell prints its
/// message and ends, since it expects <c>ES.BAT</c> to have started it; this engine starts the shell itself, as ES2's host
/// runs VSHELL's turn without <c>ES.EXE</c>, so a run without it goes on.</para>
/// </summary>
public sealed class Es1ShellSwitches {
	/// <summary><c>-eggplant</c>, compared whole and case-sensitively, which only <c>-</c> introduces.</summary>
	public const string Eggplant = "-eggplant";

	/// <summary>Whether <c>-eggplant</c> was given.</summary>
	public bool EggplantGiven { get; private set; }

	/// <summary><c>Shell_PlayIntro</c> (<c>0007fd0a</c>), 1 in the image; <c>-A</c> clears it.</summary>
	public bool PlayIntro { get; private set; } = true;

	/// <summary><c>Shell_AltDriveAllowed</c> (<c>0007fd0b</c>), which <c>-C</c> sets, so that <c>ALT_DRIVE</c> is honored.</summary>
	public bool AltDriveAllowed { get; private set; }

	/// <summary><c>Shell_MSwitchGiven</c> (<c>0007fd04</c>), which <c>-M</c> sets; startup.md's Open has no reader of it.</summary>
	public bool MSwitchGiven { get; private set; }

	/// <summary>
	/// <c>-S</c>: <c>Shell_MusicOption</c> (<c>0007fcb8</c>) and <c>Shell_SoundOption</c> (<c>0007fcb9</c>) set to 1,
	/// which <c>Shell_LoadData</c> then overwrites with options 0 and 1 (<see cref="Es1ShellData.MusicOption"/>,
	/// <see cref="Es1ShellData.SoundOption"/>).
	/// </summary>
	public bool SoundSwitch { get; private set; }

	/// <summary><c>Shell_DebugMode</c> (<c>0007fd08</c>), which <c>-X</c> sets.</summary>
	public bool DebugMode { get; private set; }

	/// <summary>
	/// <c>Shell_ParseCommandLine</c> on <paramref name="arguments"/>, the arguments after the program's path, which retail
	/// does not look at: from the last to the first, an argument opening with <c>/</c> or <c>-</c> is <c>-eggplant</c> when
	/// it is that exactly, and is otherwise looked up by its second character, upper-cased, among the five dwords at
	/// <c>0007e76c</c>; an argument matching none is passed over.
	/// </summary>
	public static Es1ShellSwitches Parse(IReadOnlyList<string> arguments) {
		var switches = new Es1ShellSwitches();
		for (int i = arguments.Count - 1; i >= 0; i--) {
			string argument = arguments[i];
			if (argument.Length == 0 || (argument[0] != '/' && argument[0] != '-')) {
				continue;
			}

			if (argument == Eggplant) {
				switches.EggplantGiven = true;
			}

			// The second character is the string's NUL for a lone "-", which matches no switch.
			char letter = argument.Length > 1 ? char.ToUpperInvariant(argument[1]) : '\0';
			switch (letter) {
				case 'A':
					switches.PlayIntro = false;
					break;
				case 'C':
					switches.AltDriveAllowed = true;
					break;
				case 'M':
					switches.MSwitchGiven = true;
					break;
				case 'S':
					switches.SoundSwitch = true;
					break;
				case 'X':
					switches.DebugMode = true;
					break;
			}
		}

		return switches;
	}
}
