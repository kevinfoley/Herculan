using System.Numerics;
using System.Reflection;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using Herculan.Opl3;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// The floating "About" window opened from the menu bar: the engine's name, its version, and the licences it is
/// released under. The first is <c>LICENSE.txt</c> from the repository root, embedded in the engine library (see
/// <c>Herculan.Engine.csproj</c>) so that a build run straight from <c>bin</c>, which has no copy beside it, shows
/// it too. The second is the LGPL text of <c>Herculan.Opl3</c>, the one assembly <c>LICENSE.txt</c> does not cover,
/// embedded in that assembly as <c>COPYING</c>.
/// </summary>
public sealed class AboutWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 520f;
	private const float LicenseHeight = 200f;

	private const string ProductName = "HERCULAN Engine";
	private const string LicenseResource = "LICENSE.txt";

	/// <summary>The heading of <c>Herculan.Opl3</c>'s licence: the assembly's name and what it ports, names left untranslated.</summary>
	private const string Opl3Heading = "Herculan.Opl3 (Nuked-OPL3 1.8)";
	private const string Opl3LicenseResource = "COPYING";

	private readonly LocalizationTable _localization;
	private readonly string _version;
	private readonly string _license;
	private readonly string _opl3License;

	/// <summary>Whether the window is open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	public AboutWindow(LocalizationTable localization) {
		_localization = localization;
		_version = "v" + DisplayVersion();
		_license = Unwrap(ReadResource(typeof(AboutWindow).Assembly, LicenseResource));
		_opl3License = Unwrap(ReadResource(typeof(Opl3Chip).Assembly, Opl3LicenseResource));
	}

	/// <summary>
	/// Draws the window, if it is open. Call once per frame, inside the ImGui frame.
	/// </summary>
	/// <param name="centre">When the window is centred on the window (<see cref="PanelCentring.Condition"/>).</param>
	public void Draw(ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(_localization.GetStringOrKey("about.title") + "###about", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			ImGui.TextUnformatted(ProductName);
			ImGui.TextUnformatted(_version);

			ImGui.SeparatorText(_localization.GetStringOrKey("about.license"));
			if (ImGui.BeginChild("##license", new Vector2(0f, ScaledImGui.Scaled(LicenseHeight)), ImGuiChildFlags.Border)) {
				ImGui.TextWrapped(_license);
			}
			ImGui.EndChild();

			ImGui.SeparatorText(Opl3Heading);
			if (ImGui.BeginChild("##opl3license", new Vector2(0f, ScaledImGui.Scaled(LicenseHeight)), ImGuiChildFlags.Border)) {
				ImGui.TextWrapped(_opl3License);
			}
			ImGui.EndChild();

			ImGui.Separator();
			if (ImGui.Button(_localization.GetStringOrKey("general.close"), new Vector2(ImGui.GetContentRegionAvail().X, 0f))) {
				stayOpen = false;
			}
		}

		ImGui.End();

		if (!stayOpen) {
			IsOpen = false;
		}
	}

	/// <summary>The release number, without the commit hash the SDK appends to the informational version after a '+'.</summary>
	internal static string DisplayVersion() {
		var assembly = typeof(AboutWindow).Assembly;
		string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (informational != null) {
			int plus = informational.IndexOf('+');
			return plus >= 0 ? informational[..plus] : informational;
		}

		return assembly.GetName().Version?.ToString(3) ?? "?";
	}

	private static string ReadResource(Assembly assembly, string name) {
		using var stream = assembly.GetManifestResourceStream(name);
		if (stream == null) {
			return "";
		}

		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	// LICENSE.txt and COPYING are hard-wrapped at about 75 columns, which TextWrapped would break again at the panel's width into
	// ragged lines. This joins each paragraph's lines into one, keeping blank lines and "- " list items as they are.
	private static string Unwrap(string text) {
		var paragraphs = new List<string>();
		var current = new List<string>();

		foreach (string raw in text.ReplaceLineEndings("\n").Split('\n')) {
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("- ")) {
				if (current.Count > 0) {
					paragraphs.Add(string.Join(' ', current));
					current.Clear();
				}
			}

			if (line.Length == 0) {
				paragraphs.Add("");
			} else {
				current.Add(line);
			}
		}

		if (current.Count > 0) {
			paragraphs.Add(string.Join(' ', current));
		}

		return string.Join('\n', paragraphs).Trim('\n');
	}
}
