using Herculan.Engine.Games;
using Herculan.Engine.Localization;
using Herculan.Engine.Settings;
using Herculan.Es1.Host.Install;
using Herculan.Es1.Settings;
using HercWorks.Core.Io.Transform;

namespace Herculan.Es1.Host;

/// <summary>
/// Metaltech: Earthsiege as the host runs it (<see cref="IGameModule"/>). With no mission named, none of
/// <c>--play</c>, <c>--demo</c> and <c>--record</c> and no <see cref="Es1Options.SimulatorFlag"/>, the launcher,
/// <c>ESCD.EXE</c>, as <c>ES.BAT</c> runs it, with <c>GO.EXE</c>'s switches as its own command line, which it hands on to
/// the shell and the simulator (<see cref="Es1Launcher"/>). Otherwise the simulator alone (<see cref="Es1MissionHost"/>):
/// a named mission loads as a single mission; <see cref="Es1Options.SimulatorFlag"/> runs it on the install's
/// <c>data\</c> files, as <c>DBSIM.EXE</c> does; <c>--play</c> and <c>--demo</c> play a tape, as its <c>-p</c> and
/// <c>-D</c> do, and <c>--record</c> records one, as its <c>-r</c> does.
/// </summary>
public sealed class Es1GameModule : IGameModule {
	/// <summary>Metaltech: Earthsiege's identity: <c>ES1_GAME_PATH</c>, an <c>ES1</c> folder, <c>es1-install-path.txt</c>.</summary>
	public static GameIdentity Identity { get; } = new(RetailGame.Earthsiege1, NameKey: "game.earthsiege1", FolderName: "ES1",
		PathVariable: "ES1_GAME_PATH", RememberedPathFileName: "es1-install-path.txt", PromptKeyPrefix: "es1_install_prompt");

	private readonly Es1Options _options = new();
	private string? _running;

	GameIdentity IGameModule.Identity => Identity;

	/// <inheritdoc/>
	/// <remarks>Its own are <see cref="Es1A11ySettingDefinitions.All"/>; which of the core's it honors, <see cref="Es1ShellMenus"/> lists.</remarks>
	public IReadOnlyList<A11ySettingDefinition<bool>> AccessibilitySettings => Es1A11ySettingDefinitions.All;

	public string? FirstOwnOption => _options.FirstOwnOption;

	/// <summary>The install the run started on: a change of install in Settings starts the run again (<see cref="Es1ShellMenus"/>).</summary>
	public string? ActiveInstall => _running;

	public string Usage => Es1Options.Usage;

	public bool TryParseOption(string[] args, ref int i, List<string> errors) => _options.TryParse(args, ref i, errors);

	public void CheckOptions(CommonOptions options, List<string> errors) => _options.Check(options, errors);

	public int? RunWithoutInstall() => null;

	public string? AskForInstall(LocalizationTable localization, string fontPath, string? refused, string dismissKey) =>
		Es1InstallPrompt.Run(localization, fontPath, refused, dismissKey);

	public int Run(GameRunContext context) {
		// Loaded for both turns: the shell's SYSTEM REPAIR and the menu bar's Tweaks window read them, and so does the
		// simulator, for its textures and sounds.
		TweakSettings.Current = new TweakSettings(Es1TweakSettingDefinitions.All, Es1TweakSettingDefinitions.FileName);
		TweakSettings.Current.LoadFromDisk();

		var common = context.Options;
		string root = context.InstallRoot;
		_running = root;
		try {
			if (context.Switched || !_options.RunsSimulator(common)) {
				return Es1Launcher.Run(root, _options.ShellArguments, context.FontPath, context.ItalicFontPath,
					inMission => new Es1ShellMenus(context.Localization, root, context.Games, inMission), common.ScreenshotPath,
					context.Window(), common.SilentAudio);
			}

			return Es1MissionHost.Run(root, common.MissionPath, _options.ToOverrides(), context.FontPath, context.ItalicFontPath,
				new Es1ShellMenus(context.Localization, root, context.Games, inMission: true), common.PlayTape, common.DemoTape,
				common.RecordTape, silent: common.SilentAudio);
		} finally {
			_running = null;
		}
	}
}
