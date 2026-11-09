namespace HercWorks.Video;

/// <summary>
/// One decoded frame, RGBA8, top row first.
///
/// <para>The layout matches <c>Herculan.Es2.Shell.ShellImage</c> so that a frame can be handed
/// straight to <c>GpuTexture</c> without a repack, and matches what <c>HercWorks.UI</c> wants for a
/// preview. Alpha is always 255; none of these codecs carries transparency.</para>
///
/// <para>A frame owns its pixel buffer and a player reuses the same <see cref="VideoFrame"/> across
/// the whole stream, because every codec here is interframe: each frame is decoded on top of the
/// one before it, so the buffer has to persist anyway.</para>
/// </summary>
public sealed class VideoFrame {
	/// <summary>Creates a frame buffer, sized once for the whole stream.</summary>
	public VideoFrame(int width, int height) {
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

		Width = width;
		Height = height;
		Rgba = new byte[checked(width * height * 4)];

		// Opaque from the start, so codecs only ever write colour.
		for (int i = 3; i < Rgba.Length; i += 4) {
			Rgba[i] = 0xFF;
		}
	}

	/// <summary>Frame width in pixels.</summary>
	public int Width { get; }

	/// <summary>Frame height in pixels.</summary>
	public int Height { get; }

	/// <summary>RGBA8 pixels, top row first, four bytes per pixel.</summary>
	public byte[] Rgba { get; }

	/// <summary>
	/// Incremented every time a decoder writes to this frame, so a renderer can tell whether it
	/// needs to re-upload the texture.
	/// </summary>
	public int Revision { get; private set; }

	/// <summary>Marks the buffer as changed.</summary>
	public void Touch() => Revision++;

	/// <summary>
	/// Writes one pixel, ignoring coordinates outside the frame.
	///
	/// <para>Every codec here computes its write position from counts taken out of the bitstream,
	/// so clipping in one place is what keeps a malformed run from being a range check at each of
	/// the several dozen call sites.</para>
	/// </summary>
	public void SetPixel(int x, int y, byte r, byte g, byte b) {
		if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) {
			return;
		}

		int at = ((y * Width) + x) * 4;
		Rgba[at] = r;
		Rgba[at + 1] = g;
		Rgba[at + 2] = b;
	}
}
