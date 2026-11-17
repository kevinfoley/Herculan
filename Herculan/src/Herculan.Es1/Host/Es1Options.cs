using Herculan.Engine.Games;
using Herculan.Es1.World;

namespace Herculan.Es1.Host;

/// <summary>
/// The flags only a Metaltech: Earthsiege install takes (<see cref="IGameModule.TryParseOption"/>).
/// docs/herculan/herculan-command-line.md describes every flag.
/// </summary>
public sealed class Es1Options {
	/// <summary>The flag that runs the simulator on the install's <c>data\</c> files without the shell.</summary>
	public const string SimulatorFlag = "--sim";

	/// <summary>The first of these flags given (<see cref="IGameModule.FirstOwnOption"/>).</summary>
	public string? FirstOwnOption { get; private set; }

	/// <summary>Whether <c>--theater</c> or <c>--world</c> was given, which stage the simulator.</summary>
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

	/// <summary>
	/// Whether a launch with these flags and <paramref name="common"/>'s runs the simulator rather than the launcher: a named
	/// mission, a tape to play, record or demo, or <see cref="SimulatorFlag"/>.
	/// </summary>
	public bool RunsSimulator(CommonOptions common) =>
		common.MissionPath != null || common.PlayTape != null || common.DemoTape || common.RecordTape != null || Simulator;

	/// <summary>Takes the flag at <c>args[i]</c> when it is one of these (<see cref="IGameModule.TryParseOption"/>).</summary>
	public bool TryParse(string[] args, ref int i, List<string> errors) {
		string flag = args[i];
		if (flag == "--theater") {
			// Header word 0, the theater, over the one data\script.dat holds, which a single mission's load draws
			// (docs/es1/formats/msn-mission-file.md#header-patches). An engine aid.
			if (OptionValues.TryReadInt(args, ref i, 0, Es1Theater.Names.Count - 1, errors, out int theater)) {
				Theater = theater;
			}
			Staged = true;
		} else if (flag == "--world") {
			// Header words 9 and 8, which pick the theater's palette and world file
			// (docs/es1/formats/wld-world.md#which-file), over the ones data\script.dat holds. An engine aid.
			if (OptionValues.TryReadInt(args, ref i, 0, Es1Theater.WorldVariants - 1, errors, out int variant)
					&& OptionValues.TryReadInt(args, ref i, 0, Es1Theater.WorldDetails - 1, errors, out int detail, "--world")) {
				WorldVariant = variant;
				WorldDetail = detail;
			}
			Staged = true;
		} else if (flag == SimulatorFlag) {
			// The simulator on the install's data\ files, as DBSIM.EXE runs on what the shell left there, without the
			// shell, which a bare run opens. An engine aid.
			Simulator = true;
		} else if (IsShellSwitch(flag)) {
			// One of GO.EXE's own switches, -A, -C, -M, -S, -X or -eggplant, the launcher's command line, which it hands on
			// to the shell (Es1ShellSwitches) and the simulator (Es1Launcher).
			ShellArguments.Add(flag);
		} else {
			return false;
		}

		FirstOwnOption ??= flag;
		return true;
	}

	/// <summary>What the flags cannot do together, once parsing ends (<see cref="IGameModule.CheckOptions"/>).</summary>
	public void Check(CommonOptions common, List<string> errors) {
		bool simulator = RunsSimulator(common);
		if (Staged && !simulator) {
			errors.Add($"--theater and --world stage the simulator: add {SimulatorFlag} or name a mission.");
		}
		if (simulator && ShellArguments.Count > 0) {
			errors.Add($"{ShellArguments[0]} is a switch of GO.EXE's shell, which does not run with the simulator.");
		}
	}

	/// <summary>Metaltech: Earthsiege's section of <c>--help</c> (<see cref="IGameModule.Usage"/>).</summary>
	public const string Usage = """
		Metaltech: Earthsiege (an install holding GO.EXE runs ESCD.EXE's loop of its shell and simulator, or with a
		named <mission> loads it as a single mission into DATA and runs the simulator alone; an install of it refuses
		Earthsiege 2's options)
		  --sim                       the simulator on the DATA files the install holds, without the shell
		  -A  -C  -M  -S  -X          ESCD.EXE's command line, handed on to its shell and simulator
		  --theater <0-3>             header word 0: desert, temper, ice, urban, over DATA\script.dat's
		  --world <0-2> <0-1>         header words 9 and 8, the palette and world file, over DATA\script.dat's
		""";
}
