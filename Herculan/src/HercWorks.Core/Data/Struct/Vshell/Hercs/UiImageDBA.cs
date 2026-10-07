using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// The shared core of the shell's screen-layout records (<c>gam\arm_*.dat</c>, <c>gam\rpr_*.dat</c>,
/// <c>gam\arm_weap.dat</c>): a top-left corner, a frame of the matching <c>dba\</c> sheet, and blit
/// flags. See <c>docs/retail/formats/herc-catalogs.md#the-screen-layout-families</c>.
/// </summary>
public class UiImageDBA {
	public DynamixBitmapArray? Dba { get; set; }
	public int OriginX { get; set; }
	public int OriginY { get; set; }

	/// <summary>Frame index into the matching <c>dba\</c> sheet.</summary>
	public short FrameId { get; set; }

	/// <summary>Blit flags. Retail data holds 0, or 2 to mirror the frame left to right.</summary>
	public BlitFlag? BlitFlags { get; set; }

	/// <summary>
	/// The blit-flag values. <see cref="Normal"/> and <see cref="FlipX"/> are the two retail data uses;
	/// <see cref="FlipXY"/> and <see cref="FlipY"/> are not established.
	/// </summary>
	public sealed class BlitFlag {
		public static readonly BlitFlag Normal = new(0);
		public static readonly BlitFlag FlipXY = new(1);
		public static readonly BlitFlag FlipX = new(2);
		public static readonly BlitFlag FlipY = new(3);

		private static readonly IReadOnlyList<BlitFlag> All = new[] { Normal, FlipXY, FlipX, FlipY };
		private static readonly Dictionary<short, BlitFlag> ById = All.ToDictionary(f => f.Val);

		public short Val { get; }

		private BlitFlag(short flag) {
			Val = flag;
		}

		public static BlitFlag? Get(short v) => ById.GetValueOrDefault(v);
	}
}
