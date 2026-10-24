using System.Numerics;
using Herculan.Engine.Host.Install;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Settings;
using Herculan.Engine.Updates;
using ImGuiNET;
using Xunit;

namespace Herculan.Engine.Host.Tests;

// Every ImGui panel the host shows, laid out in every .lang file with the game's own fonts and style
// (ScaledImGui.AddFonts and ApplyScale) at 100%, without a window or a GPU: a translation longer than the room it was
// given fails it. The check reads the last frame's draw data for the two ways ImGui cuts text off:
// - Something drawn past the right or bottom edge of the window, child region, table column or menu bar it sits in,
//   which the GPU's clip rectangle hides. Text grows rightward and downward, so the other two edges are not checked,
//   and a child region scrolls its content by design, so only its right edge is.
// - A label ImGui shortens itself to its widget's frame (a fixed-width button, a selectable, a combo's preview, a
//   field's hint): it trims the glyph on the frame's edge, which then shows a part of the font atlas no whole glyph does.
public class PanelLayoutTests {
	// Big enough that no floating panel meets the edge of the screen, which is a matter of the window's size, not of
	// the language.
	private static readonly Vector2 Screen = new(1920f, 1440f);

	// How far an edge may be passed by antialiasing's fringe and a glyph's padding.
	private const float Tolerance = 1.5f;

	// Frames drawn before the last is checked: a popup opens, and a window fitted to its content takes its size, over
	// the first few.
	private const int Frames = 4;

	// A path the Settings window shows without wrapping; short, so a long one on the machine running the tests does
	// not fail them. It need not exist: every read of it finds nothing.
	private const string InstallRoot = @"C:\Games\ES2";

	private static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static");

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
			var glyphs = SetUp(screen);
			using var scene = open(localization);
			for (int frame = 0; frame < Frames; frame++) {
				ImGui.NewFrame();
				scene.Draw();
				ImGui.Render();
			}

			var data = ImGui.GetDrawData();
			var cut = CutOff(data, glyphs);
			cut.AddRange(Overflowing(scene, Windows(data)));
			Assert.True(cut.Count == 0, $"{panel} in {locale}:{Environment.NewLine}{string.Join(Environment.NewLine, cut)}");
		} finally {
			ImGui.DestroyContext(context);
		}
	}

	// The context as a 100% display's window sets it up, minus the GPU: the font atlas is built here, not uploaded.
	// Returns where in the atlas each whole glyph of every font lies.
	private static unsafe HashSet<Vector4> SetUp(Vector2 screen) {
		var io = ImGui.GetIO();
		io.NativePtr->IniFilename = null;
		io.DisplaySize = screen;
		io.DeltaTime = 1f / 60f;
		ScaledImGui.AddFonts(io.Fonts, Path.Combine(FontFolder, "OpenSans-Regular.ttf"), Path.Combine(FontFolder, "OpenSans-Italic.ttf"),
			ScaledImGui.FontSize, 1f);
		io.Fonts.GetTexDataAsAlpha8(out nint _, out int _, out int _);
		io.Fonts.SetTexID(1);
		ScaledImGui.ApplyScale(1f, 1f, ScaledImGui.FontSize);

		// Read from ImGui's own ImFontGlyph, 40 bytes with U0, V0, U1 and V1 from byte 24: ImGui.NET's copy gives each of
		// its first three bit-fields a word of its own, so its layout is 48 bytes and indexing through it reads the wrong
		// glyphs. Were these offsets wrong, every glyph drawn would read as cut short.
		var glyphs = new HashSet<Vector4>();
		for (int f = 0; f < io.Fonts.Fonts.Size; f++) {
			var font = io.Fonts.Fonts[f];
			byte* data = (byte*)font.Glyphs.Data;
			for (int g = 0; g < font.Glyphs.Size; g++) {
				glyphs.Add(*(Vector4*)(data + g * 40 + 24));
			}
		}

		return glyphs;
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
		var session = new HostSession(InstallRoot, localization, Path.Combine(FontFolder, "OpenSans-Regular.ttf"),
			Path.Combine(FontFolder, "OpenSans-Italic.ttf"));
		var settings = new SettingsWindow(session, restartShell: () => { });
		var bar = new HostMenuBar(localization, KeyMap.Retail, new TweaksMenu(new TweakSettings(), localization), settings,
			new ControllerWindow(localization, () => null));
		bar.Show();
		open(bar);
		return new Scene(() => bar.Draw(0, fullScreen: false), settings, session);
	}

	// The top-level windows the frame drew: its draw lists' owners, less child regions, which are named after their
	// window with a '/', and the screen's own lists behind and in front of every window.
	private static List<string> Windows(ImDrawDataPtr data) {
		var windows = new List<string>();
		for (int l = 0; l < data.CmdListsCount; l++) {
			string owner = data.CmdLists[l]._OwnerName.ToString();
			if (!owner.Contains('/') && !owner.StartsWith("##Background") && !owner.StartsWith("##Foreground")) {
				windows.Add(owner);
			}
		}

		return windows;
	}

	// How far each window's content runs past it, which the draw data alone misses once a widget is wholly outside:
	// ImGui does not draw it at all. Read in one more frame, appending to each window after the scene has drawn it,
	// since a window's scroll range is only to be had from inside it.
	private static List<string> Overflowing(Scene scene, List<string> windows) {
		var overflowing = new List<string>();
		ImGui.NewFrame();
		scene.Draw();
		foreach (string window in windows) {
			ImGui.Begin(window);
			float wider = ImGui.GetScrollMaxX();
			float taller = ImGui.GetScrollMaxY();
			ImGui.End();
			if (wider > Tolerance) {
				overflowing.Add($"  \"{window}\": its content is {wider:0} px wider than it");
			}
			// The main menu bar is one line whose layout always runs past it below; only its width says anything.
			if (taller > Tolerance && window != "##MainMenuBar") {
				overflowing.Add($"  \"{window}\": its content is {taller:0} px taller than it");
			}
		}

		ImGui.Render();
		return overflowing;
	}

	// Everything cut off, one line each: the window it is in, how, and where from the clip rectangle's top left, which
	// is about the window's own.
	private static List<string> CutOff(ImDrawDataPtr data, HashSet<Vector4> glyphs) {
		var cut = new List<string>();
		for (int l = 0; l < data.CmdListsCount; l++) {
			var list = data.CmdLists[l];
			string owner = list._OwnerName.ToString();
			bool scrolls = owner.Contains('/');
			for (int c = 0; c < list.CmdBuffer.Size; c++) {
				var command = list.CmdBuffer[c];
				if (command.UserCallback != 0) {
					continue;
				}

				var clip = command.ClipRect;
				int start = (int)command.IdxOffset;
				int end = start + (int)command.ElemCount;
				Vector2 Vertex(int index) => list.VtxBuffer[(int)command.VtxOffset + list.IdxBuffer[index]].pos;

				var (right, rightAt) = (0f, Vector2.Zero);
				var (bottom, bottomAt) = (0f, Vector2.Zero);
				for (int i = start; i < end; i++) {
					var at = Vertex(i);
					if (at.X - clip.Z > right) {
						(right, rightAt) = (at.X - clip.Z, at);
					}
					if (at.Y - clip.W > bottom) {
						(bottom, bottomAt) = (at.Y - clip.W, at);
					}
				}

				if (right > Tolerance) {
					cut.Add($"  \"{owner}\": runs {right:0} px past the right edge, at y {rightAt.Y - clip.Y:0}");
				}
				if (!scrolls && bottom > Tolerance) {
					cut.Add($"  \"{owner}\": runs {bottom:0} px past the bottom edge, at x {bottomAt.X - clip.X:0}");
				}

				// A glyph is a quad of its own: indices b, b+1, b+2, b, b+2, b+3, with the atlas's corners on b and b+2.
				for (int i = start; i + 6 <= end; i++) {
					int b = list.IdxBuffer[i];
					if (list.IdxBuffer[i + 1] != b + 1 || list.IdxBuffer[i + 2] != b + 2 || list.IdxBuffer[i + 3] != b
							|| list.IdxBuffer[i + 4] != b + 2 || list.IdxBuffer[i + 5] != b + 3) {
						continue;
					}

					var first = list.VtxBuffer[(int)command.VtxOffset + b];
					var third = list.VtxBuffer[(int)command.VtxOffset + b + 2];
					var uv = new Vector4(first.uv.X, first.uv.Y, third.uv.X, third.uv.Y);
					// A filled rectangle's quad shows the atlas's one white pixel at every corner.
					if (uv.X != uv.Z && !glyphs.Contains(uv)) {
						cut.Add($"  \"{owner}\": a label is cut short at ({first.pos.X - clip.X:0}, {first.pos.Y - clip.Y:0})");
						break;
					}

					i += 5;
				}
			}
		}

		return cut;
	}
}
