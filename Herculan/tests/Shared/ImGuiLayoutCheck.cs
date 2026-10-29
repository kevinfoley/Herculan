using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Tests.Shared;

// Lays ImGui panels out with the hosts' own fonts and style (ScaledImGui.AddFonts and ApplyScale) at 100%, without a window
// or a GPU, and reports everything a frame cuts off. It reads the last frame's draw data for the two ways ImGui cuts text off:
// - Something drawn past the right or bottom edge of the window, child region, table column or menu bar it sits in,
//   which the GPU's clip rectangle hides. Text grows rightward and downward, so the other two edges are not checked,
//   and a child region scrolls its content by design, so only its right edge is.
// - A label ImGui shortens itself to its widget's frame (a fixed-width button, a selectable, a combo's preview, a
//   field's hint): it trims the glyph on the frame's edge, which then shows a part of the font atlas no whole glyph does.
// Then, in one more frame, each window's scroll range, which catches a widget drawn wholly outside, which ImGui leaves out of
// the draw data. Linked into each test project that lays out panels.
internal static class ImGuiLayoutCheck {
	// How far an edge may be passed by antialiasing's fringe and a glyph's padding.
	private const float Tolerance = 1.5f;

	/// <summary>The folder the hosts' Open Sans faces are copied to beside the tests.</summary>
	public static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Open_Sans", "static");

	/// <summary>
	/// The current context as a 100% display's window sets it up, minus the GPU: the fonts in <see cref="FontFolder"/> named by
	/// <paramref name="regular"/>, <paramref name="italic"/> and <paramref name="bold"/> added and the atlas built here, not
	/// uploaded. Returns the atlas's glyphs, and the bold face.
	/// </summary>
	public static unsafe (AtlasGlyphs Glyphs, ImFontPtr? Bold) SetUp(Vector2 screen, string regular, string? italic, string? bold) {
		var io = ImGui.GetIO();
		io.NativePtr->IniFilename = null;
		io.DisplaySize = screen;
		io.DeltaTime = 1f / 60f;
		var (_, _, boldFont) = ScaledImGui.AddFonts(io.Fonts, Path.Combine(FontFolder, regular),
			italic != null ? Path.Combine(FontFolder, italic) : null, ScaledImGui.FontSize, 1f,
			bold != null ? Path.Combine(FontFolder, bold) : null);
		io.Fonts.GetTexDataAsAlpha8(out nint _, out int _, out int _);
		io.Fonts.SetTexID(1);
		ScaledImGui.ApplyScale(1f, 1f, ScaledImGui.FontSize);

		// Read from ImGui's own ImFontGlyph, 40 bytes: its bit-fields Colored, Visible and then the codepoint from the low bit
		// of the first word, and U0, V0, U1 and V1 from byte 24. ImGui.NET's copy gives each of its first three bit-fields a
		// word of its own, so its layout is 48 bytes and indexing through it reads the wrong glyphs. Were these offsets wrong,
		// every glyph drawn would read as cut short.
		var glyphs = new AtlasGlyphs();
		for (int f = 0; f < io.Fonts.Fonts.Size; f++) {
			var font = io.Fonts.Fonts[f];
			byte* data = (byte*)font.Glyphs.Data;
			for (int g = 0; g < font.Glyphs.Size; g++) {
				var uv = *(Vector4*)(data + g * 40 + 24);
				glyphs.Whole.Add(uv);
				glyphs.Codepoints.TryAdd(new Vector2(uv.X, uv.Y), (char)(*(uint*)(data + g * 40) >> 2));
			}
		}

		return (glyphs, boldFont);
	}

	/// <summary>
	/// Draws <paramref name="draw"/> for <paramref name="frames"/> frames, then everything the last cut off and every window
	/// its content runs past, one line each; empty when everything fits.
	/// </summary>
	public static List<string> CutOffs(Action draw, int frames, AtlasGlyphs glyphs) {
		for (int frame = 0; frame < frames; frame++) {
			ImGui.NewFrame();
			draw();
			ImGui.Render();
		}

		var data = ImGui.GetDrawData();
		var cut = CutOff(data, glyphs);
		cut.AddRange(Overflowing(draw, Windows(data)));
		return cut;
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
	private static List<string> Overflowing(Action draw, List<string> windows) {
		var overflowing = new List<string>();
		ImGui.NewFrame();
		draw();
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
	private static List<string> CutOff(ImDrawDataPtr data, AtlasGlyphs glyphs) {
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
					cut.Add($"  \"{owner}\": runs {right:0} px past the right edge, at y {rightAt.Y - clip.Y:0}: "
							+ $"\"{LineText(list, command, rightAt.Y - ImGui.GetFontSize() / 2f, rightAt.Y + ImGui.GetFontSize() / 2f, glyphs)}\"");
				}
				if (!scrolls && bottom > Tolerance) {
					cut.Add($"  \"{owner}\": runs {bottom:0} px past the bottom edge, at x {bottomAt.X - clip.X:0}");
				}

				for (int i = start; i + 6 <= end; i++) {
					if (!IsQuad(list, i)) {
						continue;
					}

					int b = list.IdxBuffer[i];
					var first = list.VtxBuffer[(int)command.VtxOffset + b];
					var third = list.VtxBuffer[(int)command.VtxOffset + b + 2];
					var uv = new Vector4(first.uv.X, first.uv.Y, third.uv.X, third.uv.Y);
					// A filled rectangle's quad shows the atlas's one white pixel at every corner.
					if (uv.X != uv.Z && !glyphs.Whole.Contains(uv)) {
						cut.Add($"  \"{owner}\": a label is cut short at ({first.pos.X - clip.X:0}, {first.pos.Y - clip.Y:0}): "
							+ $"\"{LineText(list, command, first.pos.Y, third.pos.Y, glyphs)}\"");
						break;
					}

					i += 5;
				}
			}
		}

		return cut;
	}

	// Whether the six indices from <i> are a quad of their own, as a glyph is: b, b+1, b+2, b, b+2, b+3, with the atlas's
	// corners on b and b+2.
	private static bool IsQuad(ImDrawListPtr list, int i) {
		int b = list.IdxBuffer[i];
		return list.IdxBuffer[i + 1] == b + 1 && list.IdxBuffer[i + 2] == b + 2 && list.IdxBuffer[i + 3] == b
			&& list.IdxBuffer[i + 4] == b + 2 && list.IdxBuffer[i + 5] == b + 3;
	}

	// The text one draw command lays out on the line between <top> and <bottom>, each glyph whose middle lies there, read back
	// from its quad by its atlas corner, left to right; a gap wider than a fifth of the font's size reads as a space, since
	// ImGui draws none. The bounds are widened by a pixel's rounding, so a glyph of a lower x-height still counts.
	private static string LineText(ImDrawListPtr list, ImDrawCmdPtr command, float top, float bottom, AtlasGlyphs glyphs) {
		var found = new List<(float Left, float Right, char Glyph)>();
		int start = (int)command.IdxOffset;
		int end = start + (int)command.ElemCount;
		for (int i = start; i + 6 <= end; i++) {
			if (!IsQuad(list, i)) {
				continue;
			}

			int b = list.IdxBuffer[i];
			var first = list.VtxBuffer[(int)command.VtxOffset + b];
			var third = list.VtxBuffer[(int)command.VtxOffset + b + 2];
			float middle = (first.pos.Y + third.pos.Y) / 2f;
			if (middle >= top - 2f && middle <= bottom + 2f && glyphs.Codepoints.TryGetValue(first.uv, out char glyph)) {
				found.Add((first.pos.X, third.pos.X, glyph));
			}

			i += 5;
		}

		found.Sort((a, b) => a.Left.CompareTo(b.Left));
		var text = new System.Text.StringBuilder();
		float space = ImGui.GetFontSize() / 5f;
		for (int g = 0; g < found.Count; g++) {
			if (g > 0 && found[g].Left - found[g - 1].Right > space) {
				text.Append(' ');
			}

			text.Append(found[g].Glyph);
		}

		return text.ToString();
	}
}

/// <summary>Where the font atlas holds each whole glyph of every font, and which character each is by its top left corner.</summary>
internal sealed class AtlasGlyphs {
	public HashSet<Vector4> Whole { get; } = [];

	public Dictionary<Vector2, char> Codepoints { get; } = [];
}
