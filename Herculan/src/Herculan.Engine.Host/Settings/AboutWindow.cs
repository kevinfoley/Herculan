using System.Numerics;
using System.Reflection;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "About" window opened from the menu bar: the engine's name, its version, and the licence it is
/// released under. The licence is <c>LICENSE.txt</c> from the repository root, embedded in the executable (see
/// <c>Herculan.Engine.Host.csproj</c>) so that a build run straight from <c>bin</c>, which has no copy beside it, shows
/// it too.
/// </summary>
sealed class AboutWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 520f;
	private const float LicenseHeight = 260f;

	private const string ProductName = "HERCULAN Engine";
	private const string LicenseResource = "LICENSE.txt";

	private readonly LocalizationTable _localization;
	private readonly string _version;
	private readonly string _license;

	/// <summary>Whether the window is open. Set by the menu bar; see <see cref="HostMenuBar"/>.</summary>
	public bool IsOpen { get; set; }

	public AboutWindow(LocalizationTable localization) {
		_localization = localization;
		_version = "v" + DisplayVersion();
		_license = Unwrap(ReadLicense());
	}

	/// <summary>
	/// Draws the window, if it is open. Call once per frame, inside the ImGui frame. <paramref name="centre"/> is when it
	/// is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
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

	private static string ReadLicense() {
		using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream(LicenseResource);
		if (stream == null) {
			return "";
		}

		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	// LICENSE.txt is hard-wrapped at about 75 columns, which TextWrapped would break again at the panel's width into
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
