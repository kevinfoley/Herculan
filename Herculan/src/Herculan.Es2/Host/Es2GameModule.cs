using Herculan.Engine.Games;
using Herculan.Engine.Localization;
using Herculan.Engine.Settings;
using Herculan.Es2.Host.Install;
using Herculan.Es2.Host.Shell;
using Herculan.Es2.Input;
using Herculan.Es2.Install;
using Herculan.Es2.Settings;
using HercWorks.Core.Io.Transform;

namespace Herculan.Es2.Host;

/// <summary>
/// Earthsiege 2 as the host runs it (<see cref="IGameModule"/>): by default <c>ES.EXE</c>'s loop of front end and
/// simulator (<see cref="Launcher.RunShellLoop"/>), or one mission (<see cref="Launcher.RunMission"/>) or one movie
/// (<see cref="MovieHost"/>) when the command line asks for it; <c>--install</c> (<see cref="DiscInstall"/>) needs no
/// install. The mission editor's assembly references this one, so the host hands its turn in (<paramref name="editor"/>).
/// </summary>
public sealed class Es2GameModule(EditorTurn editor) : IGameModule {
	/// <summary>Earthsiege 2's identity: <c>ES2_GAME_PATH</c>, an <c>ES2</c> folder, <c>install-path.txt</c>.</summary>
	public static GameIdentity Identity { get; } = new(RetailGame.Earthsiege2, NameKey: "game.earthsiege2", FolderName: "ES2",
		PathVariable: "ES2_GAME_PATH", RememberedPathFileName: "install-path.txt", PromptKeyPrefix: "install_prompt");

	private readonly Es2Options _options = new();
	private HostSession? _session;

	GameIdentity IGameModule.Identity => Identity;

	/// <inheritdoc/>
	/// <remarks>Earthsiege 2 has none beyond the core's.</remarks>
	public IReadOnlyList<A11ySettingDefinition<bool>> AccessibilitySettings => [];

	/// <inheritdoc/>
	public string? FirstOwnOption => _options.FirstOwnOption;

	/// <inheritdoc/>
	/// <remarks>The <see cref="HostSession.InstallRoot"/> of the session <see cref="Run"/> holds.</remarks>
	public string? ActiveInstall => _session?.InstallRoot;

	/// <inheritdoc/>
	/// <remarks>Earthsiege 2's is <see cref="Es2OptionParser.Usage"/>.</remarks>
	public string Usage => Es2OptionParser.Usage;

	/// <inheritdoc/>
	/// <remarks>The flags are <see cref="Es2OptionParser"/>'s, parsed into this module's <see cref="Es2Options"/>.</remarks>
	public bool TryParseOption(string[] args, ref int i, List<string> errors) => Es2OptionParser.TryParse(_options, args, ref i, errors);

	/// <inheritdoc/>
	/// <remarks>The checks are <see cref="Es2OptionParser.Check"/>'s.</remarks>
	public void CheckOptions(CommonOptions options, List<string> errors) => Es2OptionParser.Check(_options, options, errors);

	/// <inheritdoc/>
	/// <remarks>
	/// The one such run is <c>--install</c>: <see cref="DiscInstall.Run"/>, when both its source and its destination were
	/// given.
	/// </remarks>
	public int? RunWithoutInstall() => _options is { InstallSource: { } source, InstallDestination: { } destination }
		? DiscInstall.Run(source, destination, _options.InstallSize, _options.InstallLanguage, _options.InstallDiscFiles)
		: null;

	/// <inheritdoc/>
	/// <remarks>Earthsiege 2's prompt is <see cref="InstallPrompt.Run"/>.</remarks>
	public string? AskForInstall(LocalizationTable localization, string fontPath, string? refused, string dismissKey) =>
		InstallPrompt.Run(localization, fontPath, refused, dismissKey);

	/// <inheritdoc/>
	/// <remarks>
	/// Opens a <see cref="HostSession"/> on the install, first writing into <c>data\drive.cfg</c> a disc image found the
	/// way the install was when the one it names opens no disc, then runs what the class summary lists. A switched run
	/// (<see cref="GameRunContext.Switched"/>) always goes to the shell loop.
	/// </remarks>
	public int Run(GameRunContext context) {
		var common = context.Options;
		_options.MissionPath = common.MissionPath;
		_options.ScreenshotPath = common.ScreenshotPath;
		_options.SilentAudio = common.SilentAudio;
		_options.PlayTape = common.PlayTape;
		_options.DemoTape = common.DemoTape;
		_options.RecordTape = common.RecordTape is { } tape ? Path.ChangeExtension(tape, InputTapePlayer.Extension) : null;

		TweakSettings.Current = new TweakSettings(Es2TweakSettingDefinitions.All, Es2TweakSettingDefinitions.FileName);
		TweakSettings.Current.LoadFromDisk();

		string root = context.InstallRoot;
		using var session = new HostSession(root, context.Localization, context.FontPath, context.ItalicFontPath) {
			RememberInstall = !common.AskForInstall,
			Games = context.Games,
		};
		_session = session;
		try {
			// A drive.cfg that opens no disc is given an image found the way the install was, written in as if chosen in Settings.
			if (session.Disc == null && GameInstall.FindDiscImage(root, ShellHost.DiscCheckMovie) is { } foundImage) {
				try {
					GameInstall.WriteDiscImage(root, foundImage);
					session.ReopenDisc();
				} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
					Console.Error.WriteLine($"Found the disc image {foundImage}, but could not write it into data\\drive.cfg ({ex.Message}).");
				}
			}

			// The disc GameInstall.OpenDiscFile falls back from, said once: a disc without the movie the shell's startup
			// probes for (ShellHost.DiscCheckMovie) is not the CD.
			if (session.Disc is { } startupDisc) {
				Console.WriteLine($"Disc: {startupDisc.Location}");
				if (!startupDisc.FileExists(ShellHost.DiscCheckMovie)) {
					Console.Error.WriteLine($"data\\drive.cfg specifies {startupDisc.Location}, but it is not a retail CD. "
						+ "Falling back to the install directory for some files.");
				}
			}

			// --movie shares even less: no archives, no zone, no shell art — one file and a quad. See MovieHost.
			if (!context.Switched && _options.MoviePath != null) {
				return MovieHost.Run(root, session.Disc, _options.MoviePath, common.ScreenshotPath, common.SilentAudio);
			}

			return common.RunMission && !context.Switched
				? Launcher.RunMission(session, context.Window(), _options)
				: Launcher.RunShellLoop(session, context.Window(), _options, editor);
		} finally {
			_session = null;
		}
	}
}
