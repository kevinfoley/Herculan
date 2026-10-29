using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The "Terrain" window's fields (<see cref="MissionTerrainWindow"/>): replacing the mission's terrain from a terrain image (<see cref="TerrainImageForm"/>,
/// <see cref="TerrainChanger"/>), the form starting at the cell size and highest point of the terrain the mission has; and
/// exporting the mission's heightmap as an image, <c>&lt;mission&gt;.png</c> in a folder typed in or chosen with the system's
/// folder picker (<see cref="NativePathPicker"/>), replacing a file of that name there.
/// </summary>
internal sealed class TerrainView {
	private const string BrowseLabel = "Browse...";

	// The picture's side, in pixels on a 100% display (ScaledImGui.Scaled).
	private const float PictureSize = 200f;

	private readonly TerrainChanger _terrain;
	private readonly TerrainImageForm _form;
	private readonly Func<string> _name;
	private bool _formStarted;
	private string _folder = "";
	private Task<string?>? _picking;
	private string? _exported;

	/// <param name="name">What the mission is called, which names the exported image.</param>
	public TerrainView(TerrainChanger terrain, TerrainImageForm form, Func<string> name) {
		_terrain = terrain;
		_form = form;
		_name = name;
	}

	/// <param name="owner">The native window to parent the pickers to on Windows (0 for none).</param>
	public void Draw(nint owner) {
		ImGui.Text("Replace terrain");
		EditorText.Note("Replace the mission's terrain with a heightmap image of your own, saved with the mission and exported "
			+ "in its mission bundle. Flyers keep their height above the ground; everything else stands on it.");
		if (!_formStarted) {
			StartForm();
		}

		string? why = _terrain.WhyFixed;
		ImGui.BeginDisabled(why != null);
		_form.Draw(owner);
		if (_form.HasImage) {
			_form.DrawPicture(ScaledImGui.Scaled(PictureSize));
			if (ImGui.Button("Replace terrain") && _form.Zone is { } zone) {
				_terrain.Request(zone);
				_formStarted = false;
			}
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		EditorText.Reason(_terrain.Failure, "Not replaced: ");
		if (_terrain.Note is { } note) {
			EditorText.Note(note);
		}

		DrawExport(owner);
	}

	// The form at the terrain the mission has now, with no image.
	private void StartForm() {
		var (shift, scale) = _terrain.Scale;
		shift = Math.Clamp(shift, ZoneFiles.MinCellSizeShift, ZoneFiles.MaxCellSizeShift);
		_form.Reset(shift, Math.Clamp(scale, ZoneFiles.MinHeightScale, ZoneFiles.MaxHeightScale(shift)));
		_formStarted = true;
	}

	private void DrawExport(nint owner) {
		if (_picking is { IsCompleted: true } picked) {
			_picking = null;
			if (picked.IsCompletedSuccessfully && picked.Result is { } folder) {
				_folder = folder;
			} else if (picked.IsFaulted) {
				Console.Error.WriteLine($"The folder picker failed: {picked.Exception?.InnerException?.Message}");
				_exported = "The folder picker failed; type the folder instead.";
			}
		}

		string file = _name() + TerrainImages.PngExtension;
		ImGui.Separator();
		ImGui.Text("Export heightmap");
		ImGui.TextWrapped($"Writes {file}, a grayscale image of the mission's terrain, into a folder.");
		bool canBrowse = NativePathPicker.IsAvailable;
		ImGui.BeginDisabled(_picking != null);
		ImGui.SetNextItemWidth(-(EditorText.ButtonRoom("Export") + (canBrowse ? EditorText.ButtonRoom(BrowseLabel) : 0f)));
		bool entered = ImGui.InputText("##heightmapFolder", ref _folder, 260, ImGuiInputTextFlags.EnterReturnsTrue);
		if (canBrowse) {
			ImGui.SameLine();
			if (ImGui.Button(BrowseLabel + "##heightmapFolder")) {
				_exported = null;
				_picking = NativePathPicker.PickAsync("Export heightmap into", _folder.Trim(), owner);
			}
		}

		ImGui.SameLine();
		if (ImGui.Button("Export") || entered) {
			_exported = Export(_folder.Trim(), file);
		}

		ImGui.EndDisabled();
		if (_exported != null) {
			EditorText.Note(_exported);
		}
	}

	// Writes the heightmap as <file> into <folder>; what came of it.
	private string Export(string folder, string file) {
		if (folder.Length == 0) {
			return "Choose a folder.";
		}

		if (!Directory.Exists(folder)) {
			return $"There is no folder {folder}.";
		}

		string path = Path.Combine(folder, file);
		try {
			var (width, height, heights) = _terrain.Heightmap();
			File.WriteAllBytes(path, TerrainImages.WritePng(width, height, heights));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
				or ArgumentException or NotSupportedException) {
			Console.Error.WriteLine($"Cannot export the heightmap: {ex.Message}");
			return $"Cannot export the heightmap: {ex.Message}";
		}

		return $"Exported the heightmap as {path}.";
	}
}
