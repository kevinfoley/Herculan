using System.Globalization;
using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Gl;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// How a <see cref="TerrainImageForm"/> pictures the heights it holds (<see cref="ZoneHeightmaps.Picture"/>), and frees the
/// picture of heights it no longer holds (<see cref="ZoneHeightmaps.Forget"/>).
/// </summary>
internal sealed record TerrainPictures(Func<byte[], int, GpuTexture?> Picture, Action<byte[]> Forget);

/// <summary>
/// The fields a mission's own terrain is made with, in File &gt; New mission and the Terrain window: a terrain image
/// (<see cref="TerrainImages"/>), typed in or chosen with the system's file picker (<see cref="NativePathPicker"/>), its cell
/// size shown in meters (<see cref="ZoneFiles.CellSizeShift"/>) and its highest point in meters, the height of a white
/// pixel, written as the height scale: 255 steps of it make the highest point, so the highest offered is
/// <c>255 * </c><see cref="ZoneFiles.MaxHeightScale"/> world units. <see cref="DrawPicture"/> pictures the image as the zone
/// list pictures a zone (<see cref="ZoneHeightmaps.Picture"/>).
/// </summary>
internal sealed class TerrainImageForm {
	private const string BrowseLabel = "Browse...";

	// A cell's raw height is in steps of the height scale; a white pixel is the 255th.
	private const int TopStep = byte.MaxValue;

	private readonly string _pickerTitle;
	private readonly TerrainPictures? _pictures;
	private string _path = "";
	private Task<string?>? _picking;
	private byte[]? _heights;
	private string? _problem;
	private bool _sizeChosen;

	/// <param name="pickerTitle">The file picker's title.</param>
	/// <param name="pictures">How the image is pictured, or null for no picture.</param>
	public TerrainImageForm(string pickerTitle, TerrainPictures? pictures) {
		_pickerTitle = pickerTitle;
		_pictures = pictures;
	}

	/// <summary>The cell size shift chosen; see <see cref="ZoneFiles.CellSizeShift"/>.</summary>
	public int CellSizeShift { get; private set; } = 14;

	/// <summary>The height scale chosen; see <see cref="ZoneFiles.HeightScale"/>.</summary>
	public int HeightScale { get; private set; } = 128;

	/// <summary>Whether an image is read, which <see cref="Zone"/> is made from.</summary>
	public bool HasImage => _heights != null;

	/// <summary>The terrain the fields make, or null with no image read.</summary>
	public ZoneFiles? Zone => _heights is { } heights ? ZoneFiles.Create(heights, CellSizeShift, HeightScale) : null;

	/// <summary>
	/// Starts the fields at <paramref name="cellSizeShift"/> and <paramref name="heightScale"/>, a terrain's own, which an
	/// image read keeps, and drops any image read.
	/// </summary>
	public void Reset(int cellSizeShift, int heightScale) {
		Reset();
		CellSizeShift = cellSizeShift;
		HeightScale = heightScale;
		_sizeChosen = true;
	}

	/// <summary>
	/// Drops any image read; the next one read starts at the cell size that makes its terrain 12.6 km wide, the editor's own
	/// starting point, unless a cell size is chosen first.
	/// </summary>
	public void Reset() {
		if (_heights != null) {
			_pictures?.Forget(_heights);
		}

		_heights = null;
		_problem = null;
		_sizeChosen = false;
	}

	/// <summary>Says an image is to be chosen, under the path, when none is read.</summary>
	public void AskForImage() => _problem ??= "Choose an image.";

	/// <summary>Draws the image's path and, once one is read, the cell size and highest point.</summary>
	/// <param name="owner">The native window to parent the file picker to on Windows (0 for none).</param>
	public void Draw(nint owner) {
		if (_picking is { IsCompleted: true } picked) {
			_picking = null;
			if (picked.IsCompletedSuccessfully && picked.Result is { } path) {
				_path = path;
				Load();
			} else if (picked.IsFaulted) {
				Console.Error.WriteLine($"The file picker failed: {picked.Exception?.InnerException?.Message}");
				_problem = "The file picker failed; type the image's path instead.";
			}
		}

		ImGui.TextWrapped("Image: grayscale PNG or raw 8-bit, "
			+ string.Join(" or ", ZoneFiles.Sides.Select(side => $"{side}x{side}")) + " pixels. Black is the lowest ground.");
		bool canBrowse = NativePathPicker.IsAvailable;
		ImGui.BeginDisabled(_picking != null);
		ImGui.SetNextItemWidth(canBrowse ? -EditorText.ButtonRoom(BrowseLabel) : -1f);
		if (ImGui.InputText("##terrainImage", ref _path, 260, ImGuiInputTextFlags.EnterReturnsTrue)) {
			Load();
		}

		if (canBrowse) {
			ImGui.SameLine();
			if (ImGui.Button(BrowseLabel)) {
				_problem = null;
				_picking = NativePathPicker.PickAsync(_pickerTitle, _path.Trim(), owner, TerrainImages.Filter);
			}
		}

		ImGui.EndDisabled();
		if (_picking != null) {
			ImGui.TextDisabled("Choose an image in the window that opened.");
		} else if (!canBrowse && _heights == null) {
			ImGui.TextDisabled("Press Enter to read it.");
		}

		if (_problem != null) {
			EditorText.Problem(_problem);
		}

		if (_heights != null) {
			DrawSize();
		}
	}

	/// <summary>Pictures the image read, <paramref name="side"/> pixels a side, and what its shades mean; nothing with none read.</summary>
	public void DrawPicture(float side) {
		if (_heights is not { } heights || _pictures?.Picture(heights, Side(heights)) is not { } texture) {
			return;
		}

		var border = ImGui.GetStyle().Colors[(int)ImGuiCol.Border];
		ImGui.Image((nint)texture.Handle, new Vector2(side, side), Vector2.Zero, Vector2.One, Vector4.One, border);
		ImGui.TextWrapped("Higher ground is lighter.");
	}

	// The cell size and highest point, and the size of terrain they make.
	private void DrawSize() {
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Cell size:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##cellSize", Meters(1 << CellSizeShift))) {
			for (int shift = ZoneFiles.MinCellSizeShift; shift <= ZoneFiles.MaxCellSizeShift; shift++) {
				if (ImGui.Selectable(Meters(1 << shift), shift == CellSizeShift)) {
					CellSizeShift = shift;
					HeightScale = Math.Min(HeightScale, ZoneFiles.MaxHeightScale(shift));
					_sizeChosen = true;
				}
			}

			ImGui.EndCombo();
		}

		ImGui.AlignTextToFramePadding();
		ImGui.Text("Highest point:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		float highest = TopStep * HeightScale / WorldScale.WorldUnitsPerMeter;
		float lowest = TopStep * ZoneFiles.MinHeightScale / WorldScale.WorldUnitsPerMeter;
		float most = TopStep * ZoneFiles.MaxHeightScale(CellSizeShift) / WorldScale.WorldUnitsPerMeter;
		if (ImGui.SliderFloat("##highestPoint", ref highest, lowest, most, "%.0f m", ImGuiSliderFlags.AlwaysClamp)) {
			HeightScale = Math.Clamp((int)MathF.Round(highest * WorldScale.WorldUnitsPerMeter / TopStep), ZoneFiles.MinHeightScale,
				ZoneFiles.MaxHeightScale(CellSizeShift));
		}

		string width = ZoneChoice.Kilometres(Side(_heights!) << CellSizeShift);
		ImGui.TextWrapped($"The terrain is {width} by {width} km.");
	}

	// Reads the image at the typed path, keeping what was read before when it cannot.
	private void Load() {
		string path = _path.Trim();
		if (path.Length == 0) {
			_problem = "Choose an image.";
			return;
		}

		if (TerrainImages.Read(path, out _problem) is not { } heights) {
			return;
		}

		if (_heights != null) {
			_pictures?.Forget(_heights);
		}

		_heights = heights;
		if (!_sizeChosen) {
			// 2^21 world units, 12.6 km: 128 cells of 2^14 or 256 of 2^13.
			CellSizeShift = Math.Clamp(21 - ZoneFiles.GridShift(Side(heights)), ZoneFiles.MinCellSizeShift, ZoneFiles.MaxCellSizeShift);
			HeightScale = Math.Min(HeightScale, ZoneFiles.MaxHeightScale(CellSizeShift));
		}
	}

	private static int Side(byte[] heights) => (int)Math.Sqrt(heights.Length);

	private static string Meters(int worldUnits) =>
		(worldUnits / WorldScale.WorldUnitsPerMeter).ToString("0.0", CultureInfo.InvariantCulture) + " m";
}
