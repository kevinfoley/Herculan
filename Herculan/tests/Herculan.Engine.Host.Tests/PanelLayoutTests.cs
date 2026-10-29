using System.Numerics;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Engine.Updates;
using Herculan.Tests.Shared;
using ImGuiNET;
using Xunit;

namespace Herculan.Engine.Host.Tests;

// Every ImGui panel the host shows, laid out in every .lang file with the game's own fonts and style, without a window or
// a GPU (ImGuiLayoutCheck): a translation longer than the room it was given fails it.
public class PanelLayoutTests {
	// Big enough that no floating panel meets the edge of the screen, which is a matter of the window's size, not of
	// the language.
	private static readonly Vector2 Screen = new(1920f, 1440f);

	// Frames drawn before the last is checked: a popup opens, and a window fitted to its content takes its size, over
	// the first few.
	private const int Frames = 4;

	// A path the Settings window shows without wrapping; short, so a long one on the machine running the tests does
	// not fail them. It need not exist: every read of it finds nothing.
	private const string InstallRoot = @"C:\Games\ES2";

	private sealed record Panel(Vector2 Screen, Func<LocalizationTable, Scene> Open);

	// What one panel is drawn with each frame, and what it holds to be disposed of after.
	private sealed class Scene(Action draw, params IDisposable[] held) : IDisposable {
		public void Draw() => draw();

		public void Dispose() {
			foreach (var item in held) {
				item.Dispose();
			}
		}
	}

	private static readonly Dictionary<string, Panel> Panels = new() {
		["Install prompt"] = new(new(InstallPrompt.WindowWidth, InstallPrompt.WindowHeight), localization => StartupPrompt(localization, refuse: false)),
		["Install prompt, path refused"] = new(new(InstallPrompt.WindowWidth, InstallPrompt.WindowHeight), localization => StartupPrompt(localization, refuse: true)),
		["Install window"] = new(new(InstallWindow.WindowWidth, InstallWindow.WindowHeight), localization => {
			var panel = new InstallPanel(localization);
			return new Scene(() => InstallWindow.Draw(panel, 0), panel);
		}),
		["Menu bar"] = new(Screen, localization => MenuBar(localization, _ => { })),
		["Settings"] = new(Screen, localization => MenuBar(localization, bar => bar.Settings.IsOpen = true)),
		["Settings, install folder prompt"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.Install, refuse: false)),
		["Settings, install folder refused"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.Install, refuse: true)),
		["Settings, disc folder prompt"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.DiscFolder, refuse: false)),
		["Settings, disc folder refused"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.DiscFolder, refuse: true)),
		["Settings, disc image prompt"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.DiscImage, refuse: false)),
		["Settings, disc image refused"] = new(Screen, localization => SettingsPrompt(localization, SettingsWindow.PromptKind.DiscImage, refuse: true)),
		["Settings, install from a disc"] = new(Screen, localization => MenuBar(localization, bar => {
			bar.Settings.IsOpen = true;
			bar.Settings.OpenInstall();
		})),
		["Controller"] = new(Screen, localization => MenuBar(localization, bar => bar.Controller.Open())),
		["Keys"] = new(Screen, localization => MenuBar(localization, bar => bar.Keys.IsOpen = true)),
		["Tweaks"] = new(Screen, localization => MenuBar(localization, bar => bar.Tweaks.IsOpen = true)),
		["Accessibility"] = new(Screen, localization => MenuBar(localization, bar => bar.Accessibility.IsOpen = true)),
		["About"] = new(Screen, localization => MenuBar(localization, bar => bar.About.IsOpen = true)),
		["Welcome"] = new(Screen, localization => MenuBar(localization, bar => bar.Welcome.Raise())),
		["Update"] = new(Screen, localization => MenuBar(localization, bar =>
			bar.Update.Offer(new ReleaseCheckResult(ReleaseCheckStatus.NewerAvailable, new Version(10, 20, 30), "https://example.com/")))),
	};

	public static IEnumerable<object[]> Cases() =>
		from locale in new LocalizationTable(LocalizationTable.DefaultLocale).GetLocales().Order()
		from panel in Panels.Keys
		select new object[] { locale, panel };

	[Theory]
	[MemberData(nameof(Cases))]
	public void NothingIsCutOff(string locale, string panel) {
		var localization = new LocalizationTable(locale);
		var (screen, open) = Panels[panel];
		var context = ImGui.CreateContext();
		try {
			var (glyphs, _) = ImGuiLayoutCheck.SetUp(screen, "OpenSans-Regular.ttf", "OpenSans-Italic.ttf", null);
			using var scene = open(localization);
			var cut = ImGuiLayoutCheck.CutOffs(scene.Draw, Frames, glyphs);
			Assert.True(cut.Count == 0, $"{panel} in {locale}:{Environment.NewLine}{string.Join(Environment.NewLine, cut)}");
		} finally {
			ImGui.DestroyContext(context);
		}
	}

	private static Scene StartupPrompt(LocalizationTable localization, bool refuse) {
		var prompt = InstallPrompt.Create(localization, "install_prompt.quit", refuse ? InstallRoot : "",
			alternativeKey: "install_prompt.install");
		if (refuse) {
			prompt.Accept();
		}

		var language = new InterfaceLanguageCombo(localization);
		return new Scene(() => InstallPrompt.Draw(prompt, language, 0, out _));
	}

	private static Scene SettingsPrompt(LocalizationTable localization, SettingsWindow.PromptKind kind, bool refuse) =>
		MenuBar(localization, bar => {
			bar.Settings.IsOpen = true;
			bar.Settings.OpenPrompt(kind);
			if (refuse) {
				// Each prompt refuses a path that is not there with a message naming it.
				bar.Settings.Prompt!.Accept();
			}
		});

	// The menu bar up, with whatever open does opened over it.
	private static Scene MenuBar(LocalizationTable localization, Action<HostMenuBar> open) {
		var session = new HostSession(InstallRoot, localization, Path.Combine(ImGuiLayoutCheck.FontFolder, "OpenSans-Regular.ttf"),
			Path.Combine(ImGuiLayoutCheck.FontFolder, "OpenSans-Italic.ttf"));
		var settings = new SettingsWindow(session, restartShell: () => { });
		var bar = new HostMenuBar(localization, KeyMap.Retail, new TweaksMenu(new TweakSettings(), localization), settings,
			new ControllerWindow(localization, () => null));
		bar.Show();
		open(bar);
		return new Scene(() => bar.Draw(0, fullScreen: false), settings, session);
	}
}
