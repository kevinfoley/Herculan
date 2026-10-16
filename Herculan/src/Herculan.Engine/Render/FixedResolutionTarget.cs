using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// An offscreen frame of a fixed size that a host draws into instead of the window, then scales into the window as
/// <see cref="FrameFit"/> places it, pixel for pixel with no filtering, over black. Its colour, depth and stencil buffers
/// are the window's own formats, so whatever draws into the window can draw into it unchanged. The GL objects are made
/// on the first <see cref="Bind"/>.
/// </summary>
public sealed class FixedResolutionTarget : IDisposable {
	private readonly GL _gl;
	private uint _framebuffer;
	private uint _colorBuffer;
	private uint _depthStencilBuffer;

	public FixedResolutionTarget(GL gl, Vector2D<int> size) {
		_gl = gl;
		Size = size;
	}

	/// <summary>The size the frame is drawn at, in pixels.</summary>
	public Vector2D<int> Size { get; }

	/// <summary>
	/// Binds the frame for drawing, clears it to black with depth and stencil cleared too, and sets the viewport to the
	/// whole of it, with the scissor test off.
	/// </summary>
	public void Bind() {
		Create();
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.Disable(EnableCap.ScissorTest);
		_gl.Viewport(0, 0, (uint)Size.X, (uint)Size.Y);
		_gl.ColorMask(true, true, true, true);
		_gl.DepthMask(true);
		_gl.StencilMask(0xff);
		_gl.ClearColor(0f, 0f, 0f, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
	}

	/// <summary>
	/// Scales the frame into the window (framebuffer 0) at <paramref name="fit"/>'s placement over black, and leaves the
	/// window bound with its whole viewport set and the scissor test off, for whatever draws over it at full resolution.
	/// </summary>
	public void Present(FrameFit fit) {
		var window = fit.Window;
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		_gl.Disable(EnableCap.ScissorTest);
		_gl.Viewport(0, 0, (uint)Math.Max(window.X, 1), (uint)Math.Max(window.Y, 1));
		_gl.ColorMask(true, true, true, true);
		_gl.ClearColor(0f, 0f, 0f, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

		var (left, top, width, height) = fit.Placement;
		if (_framebuffer == 0 || width <= 0 || height <= 0) {
			return;
		}

		// GL's window origin is the bottom-left corner.
		int bottom = window.Y - top - height;
		_gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
		_gl.BlitFramebuffer(0, 0, Size.X, Size.Y, left, bottom, left + width, bottom + height,
			ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	private void Create() {
		if (_framebuffer != 0) {
			return;
		}

		_colorBuffer = _gl.GenRenderbuffer();
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorBuffer);
		_gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)Size.X, (uint)Size.Y);

		_depthStencilBuffer = _gl.GenRenderbuffer();
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthStencilBuffer);
		_gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)Size.X, (uint)Size.Y);
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

		_gl.GetInteger(GetPName.DrawFramebufferBinding, out int previousFramebuffer);
		_framebuffer = _gl.GenFramebuffer();
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
			RenderbufferTarget.Renderbuffer, _colorBuffer);
		_gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
			RenderbufferTarget.Renderbuffer, _depthStencilBuffer);

		var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
		if (status != GLEnum.FramebufferComplete) {
			Dispose();
			throw new InvalidOperationException($"The {Size.X}x{Size.Y} frame target is incomplete: {status}.");
		}
	}

	public void Dispose() {
		if (_framebuffer != 0) {
			_gl.DeleteFramebuffer(_framebuffer);
			_gl.DeleteRenderbuffer(_colorBuffer);
			_gl.DeleteRenderbuffer(_depthStencilBuffer);
			_framebuffer = _colorBuffer = _depthStencilBuffer = 0;
		}
	}
}
