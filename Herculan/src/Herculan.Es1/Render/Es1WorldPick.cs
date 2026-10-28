using Herculan.Engine.Render;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The pixel pick's readback: which of the frame's objects, the zone's shapes, the HERCs and the flyers (<see cref="Es1WorldDraw.FrameObjects"/>),
/// the world pass drew at one pixel of the world layer. This is this engine's departure from retail's pick, the user's
/// choice (docs/herculan/plan-es1-first-mission.md, decision 16): retail keeps the last object its farthest-first painter
/// paints over the point (docs/es1/simulation/terrain.md#the-pixel-pick-and-the-occlusion-probe), and this engine, which
/// draws the objects with a depth test, keeps the one the GPU drew there, the one shown.
///
/// <para>Each object is drawn again by the same renderer, from the same camera and viewport as the pass, with the scissor
/// on the one pixel, the colour and depth left unwritten and the depth test passing only a fragment at the depth the
/// pass left there, under an occlusion query. The pixel's last writer in the pass wrote that depth, so its object is
/// the one whose query passes; the same program reproduces the pass's depths and discarded texels. Should two objects'
/// fragments lie at the very same depth there, the first in the draw's order is taken. The shadows, the ground and the
/// sky, which the pick does not see, are not drawn again: the depth between the shadows and the objects is cleared, so
/// where no object drew the pixel keeps the far plane's depth and no query passes.</para>
/// </summary>
internal sealed class Es1WorldPick : IDisposable {
	private readonly GL _gl;
	private readonly SceneRenderer _renderer;
	private readonly Es1WorldDraw _draw;
	private readonly List<uint> _queries = new();

	public Es1WorldPick(GL gl, SceneRenderer renderer, Es1WorldDraw draw) {
		_gl = gl;
		_renderer = renderer;
		_draw = draw;
	}

	/// <summary>
	/// The object the last world pass, drawn from <paramref name="camera"/> in <paramref name="viewport"/>, drew at the
	/// pixel the bound world layer's scissor holds, or null for none. The objects stand placed as that pass placed them.
	/// </summary>
	public object? ObjectAt(Camera camera, (int X, int Y, int Width, int Height) viewport) {
		var drawn = new List<(object Object, uint Query)>();
		_gl.GetInteger(GetPName.DepthFunc, out int depthFunc);
		_gl.ColorMask(false, false, false, false);
		_gl.DepthMask(false);
		_gl.DepthFunc(DepthFunction.Equal);
		_renderer.Backdrop = null;
		try {
			foreach (var (frameObject, items) in _draw.FrameObjects) {
				if (!items.Any(item => item.Visible && item.DetailSelected)) {
					continue;
				}

				if (drawn.Count == _queries.Count) {
					_queries.Add(_gl.GenQuery());
				}

				uint query = _queries[drawn.Count];
				_gl.BeginQuery(QueryTarget.AnySamplesPassed, query);
				_renderer.Render(camera, items, viewport.X, viewport.Y, viewport.Width, viewport.Height);
				_gl.EndQuery(QueryTarget.AnySamplesPassed);
				drawn.Add((frameObject, query));
			}
		} finally {
			_gl.DepthFunc((DepthFunction)depthFunc);
			_gl.DepthMask(true);
			_gl.ColorMask(true, true, true, true);
		}

		foreach (var (frameObject, query) in drawn) {
			_gl.GetQueryObject(query, QueryObjectParameterName.Result, out uint passed);
			if (passed != 0) {
				return frameObject;
			}
		}

		return null;
	}

	public void Dispose() {
		foreach (uint query in _queries) {
			_gl.DeleteQuery(query);
		}

		_queries.Clear();
	}
}
