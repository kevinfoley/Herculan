using System.Globalization;
using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The "Terrain" window's fields (<see cref="MissionTerrainWindow"/>): the terrain the mission has, its heightmap pictured as
/// a terrain image is (<see cref="TerrainPictures"/>) with its cell size and highest point; replacing it from a terrain
/// image (<see cref="TerrainImageForm"/>, <see cref="TerrainChanger"/>), the form starting at that cell size and highest
/// point; and exporting the mission's heightmap as an image, <c>&lt;mission&gt;.png</c> in a folder typed in or chosen with
/// the system's folder picker (<see cref="NativePathPicker"/>), replacing a file of that name there.
/// </summary>
internal sealed class TerrainView {
	private const string BrowseLabel = "Browse...";

	// The picture's side, in pixels on a 100% display (ScaledImGui.Scaled).
	private const float PictureSize = 200f;

	// A cell's raw height is in steps of the height scale; a white pixel is the 255th (TerrainImageForm).
	private const int TopStep = byte.MaxValue;

	private readonly TerrainChanger _terrain;
	private readonly TerrainImageForm _form;
	private readonly TerrainPictures? _pictures;
	private readonly Func<string> _name;
	private bool _formStarted;
	private string _folder = "";
	private Task<string?>? _picking;
	private string? _exported;

	// The heightmap pictured, and the own terrain or zone number it was read for; a new own terrain (a replacement, a
	// sculpting stroke, an undo) or zone reads it again.
	private bool _currentRead;
	private ZoneFiles? _currentOwn;
	private short _currentZone;
	private (int Width, int Height, byte[] Heights)? _current;
	private string? _currentProblem;

	/// <param name="name">What the mission is called, which names the exported image.</param>
	/// <param name="pictures">How the current heightmap is pictured, or null for no picture.</param>
	public TerrainView(TerrainChanger terrain, TerrainImageForm form, TerrainPictures? pictures, Func<string> name) {
		_terrain = terrain;
		_form = form;
		_pictures = pictures;
		_name = name;
	}

	/// <param name="owner">The native window to parent the pickers to on Windows (0 for none).</param>
	public void Draw(nint owner) {
		DrawCurrent();
		ImGui.Separator();
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

	// The terrain the mission has: its heightmap, cell size and highest point.
	private void DrawCurrent() {
		ImGui.Text("Current terrain");
		Refresh();
		if (_current is { } current && current.Width == current.Height
				&& _pictures?.Picture(current.Heights, current.Width) is { } texture) {
			float side = ScaledImGui.Scaled(PictureSize);
			var border = ImGui.GetStyle().Colors[(int)ImGuiCol.Border];
			ImGui.Image((nint)texture.Handle, new Vector2(side, side), Vector2.Zero, Vector2.One, Vector4.One, border);
			ImGui.TextWrapped("Higher ground is lighter: the lowest ground is black, the highest white.");
		} else if (_currentProblem != null) {
			EditorText.Problem(_currentProblem);
		}

		var (shift, scale) = _terrain.Scale;
		ImGui.Text($"Cell size: {Meters(1 << shift, "0.0")}");
		ImGui.Text($"Highest point: {Meters(TopStep * scale, "#,0")}");
	}

	// Reads the heightmap again when the mission's terrain has changed since it was read.
	private void Refresh() {
		var own = _terrain.OwnZone;
		short zone = _terrain.Zone;
		if (_currentRead && ReferenceEquals(own, _currentOwn) && zone == _currentZone) {
			return;
		}

		if (_current is { } old) {
			_pictures?.Forget(old.Heights);
		}

		_currentRead = true;
		_currentOwn = own;
		_currentZone = zone;
		_current = null;
		_currentProblem = null;
		try {
			_current = _terrain.Heightmap();
		} catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException) {
			Console.Error.WriteLine($"Cannot read the heightmap: {ex.Message}");
			_currentProblem = $"Cannot read the heightmap: {ex.Message}";
		}
	}

	private static string Meters(int worldUnits, string format) =>
		(worldUnits / WorldScale.WorldUnitsPerMeter).ToString(format, CultureInfo.InvariantCulture) + " m";

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
