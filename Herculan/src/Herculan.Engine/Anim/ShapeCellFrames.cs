namespace Herculan.Engine.Anim;

/// <summary>
/// A drawn shape's <b>per-sequence cell-frame array</b> — <c>shapeInstance+8</c>, the array
/// <c>TSCellAnimPart_Render</c> (<c>004767e4</c>) indexes by the part's
/// <see cref="HercWorks.Core.Data.File.Dts.Part.TSCellAnimPart.AnimSequence"/> to decide which of
/// its children is the one drawn.
///
/// <para>Every entry starts at zero, which is the intact shape. Damage is what moves one: the game
/// steps a destroyed part's sequence to the cell that shows it destroyed.</para>
///
/// <para>This is state, not geometry: the renderer holds every cell of every sequence built and
/// gated, and reads this array to pick which of them are on screen — see
/// <c>Render.DtsMeshBuilder.BuildCells</c>.</para>
/// </summary>
public sealed class ShapeCellFrames {
	private readonly short[] _frames = new short[SequenceCount];

	/// <summary>
	/// How many sequences the array covers. Comfortably above the largest index any retail
	/// <c>.DMG</c> or <c>BASES.DAT</c> record names; the original sizes its own array from the
	/// shape.
	/// </summary>
	public const int SequenceCount = 64;

	/// <summary>
	/// Which cell sequence <paramref name="sequence"/> is showing. Out-of-range sequences read zero
	/// and are silently ignored on write, so a data file naming one costs a part its damage state
	/// rather than throwing.
	/// </summary>
	public short this[int sequence] {
		get => (uint)sequence < SequenceCount ? _frames[sequence] : (short)0;
		set {
			if ((uint)sequence < SequenceCount) {
				_frames[sequence] = value;
			}
		}
	}

	/// <summary>Whether any sequence has moved off cell zero — the whole shape is intact if not.</summary>
	public bool AllIntact {
		get {
			foreach (short frame in _frames) {
				if (frame != 0) {
					return false;
				}
			}

			return true;
		}
	}
}
