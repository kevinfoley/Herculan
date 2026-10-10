using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One drawn instance of a <see cref="BspTree"/> — one object's copy of one <c>TSBSPPart</c> — and
/// the <see cref="SceneItem"/>s that draw its children. <see cref="SceneRenderer"/> paints the children
/// of a group in the tree's order for the eye, so that where two of them overlap on screen the one
/// the walk reaches later is what shows, as in the original (docs/retail/rendering/dts-texture-binding.md,
/// "<c>TSBSPPart</c> child selection"), and each child's paint layers in ascending order
/// (<see cref="PaintLayers"/>). An item joins a group through <see cref="SceneItem.BspGroup"/>,
/// <see cref="SceneItem.BspLeaf"/> and <see cref="SceneItem.PaintLayer"/>.
///
/// <para>A group can sit inside a child of another: a weapon's shape, whose own levels carry parts of
/// their own, is drawn in the turn of the machine's hardpoint slot it is spliced into
/// (<see cref="BspTree.TryGetLeafOfPart"/>). The nested group is then painted, whole, in its parent
/// child's turn.</para>
/// </summary>
public sealed class BspDrawGroup {
	private readonly Func<int, Matrix4x4> _frameToWorld;
	private readonly Dictionary<int, Vector3> _eyes = new();
	private Vector3 _eyeWorld;

	/// <param name="tree">The part's tree.</param>
	/// <param name="frameToWorld">
	/// A frame's own space to render world space, for each frame the tree's planes and children are in
	/// (<see cref="BspTree.Frames"/>; <c>-1</c> is the object). A posed shape answers with the node's
	/// posed transform, a shape baked at its rest pose with <see cref="BspTree.RestOffset"/> in front of
	/// the object's.
	/// </param>
	/// <param name="parent">The group whose child this one is drawn inside, or null.</param>
	/// <param name="parentLeaf">Which child of <paramref name="parent"/>.</param>
	public BspDrawGroup(BspTree tree, Func<int, Matrix4x4> frameToWorld, BspDrawGroup? parent = null,
			int parentLeaf = -1) {
		Tree = tree;
		_frameToWorld = frameToWorld;
		Parent = parent;
		ParentLeaf = parentLeaf;
		EyeInFrame = EyeIn;
		Order = new int[tree.LeafCount];
		LightRetarget = new Matrix4x4?[tree.LeafCount];
		Items = new List<SceneItem>[tree.LeafCount];
		Children = new List<BspDrawGroup>[tree.LeafCount];
		for (int i = 0; i < tree.LeafCount; i++) {
			Items[i] = new List<SceneItem>();
			Children[i] = new List<BspDrawGroup>();
		}
	}

	/// <summary>
	/// A group for a shape baked at its rest pose and placed whole by <paramref name="objectToWorld"/>:
	/// every frame is the rest offset in front of the object.
	/// </summary>
	public static BspDrawGroup AtRest(BspTree tree, Func<Matrix4x4> objectToWorld,
			BspDrawGroup? parent = null, int parentLeaf = -1) =>
		new(tree, frame => Matrix4x4.CreateTranslation(tree.RestOffset(frame)) * objectToWorld(),
			parent, parentLeaf);

	public BspTree Tree { get; }

	public BspDrawGroup? Parent { get; }

	public int ParentLeaf { get; }

	// What the renderer collects into the group over one pass. Kept on the group so a pass allocates
	// nothing: BeginPass clears them the first time the pass touches the group.

	internal int Pass { get; private set; } = -1;

	/// <summary>The pass's items, per child.</summary>
	internal List<SceneItem>[] Items { get; }

	/// <summary>The groups drawn inside each child this pass.</summary>
	internal List<BspDrawGroup>[] Children { get; }

	/// <summary>Scratch for <see cref="BspTree.PaintOrder"/>.</summary>
	internal int[] Order { get; }

	/// <summary>How many leaves of <see cref="Order"/> this pass's walk wrote.</summary>
	internal int OrderCount { get; set; }

	/// <summary>
	/// Per child, what carries this pass's effect lights from where they stand into the frame the
	/// original shades that child's faces against them in, or null where the two agree — see
	/// <c>SceneRenderer</c>'s <c>PrepareBspGroup</c>.
	/// </summary>
	internal Matrix4x4?[] LightRetarget { get; }

	/// <summary>A frame's own space to render world space — the constructor's <c>frameToWorld</c>.</summary>
	internal Matrix4x4 FrameToWorld(int frame) => _frameToWorld(frame);

	/// <summary><see cref="EyeIn"/>, made a delegate once rather than on every walk.</summary>
	internal Func<int, Vector3> EyeInFrame { get; }

	internal void BeginPass(int pass, Vector3 eyeWorld) {
		Pass = pass;
		_eyeWorld = eyeWorld;
		_eyes.Clear();
		for (int i = 0; i < Items.Length; i++) {
			Items[i].Clear();
			Children[i].Clear();
		}
	}

	/// <summary>
	/// How many stencil values painting this group takes this pass: one per paint layer of each child
	/// that draws anything, plus whatever the groups drawn inside that child take.
	/// </summary>
	internal int StencilSpan() {
		int span = 0;
		for (int i = 0; i < Children.Length; i++) {
			span += ChildSpan(i);
		}

		return span;
	}

	/// <summary>The stencil values one child takes — see <see cref="StencilSpan"/>.</summary>
	internal int ChildSpan(int leaf) {
		if (Items[leaf].Count == 0 && Children[leaf].Count == 0) {
			return 0;
		}

		int span = LayerCount(leaf);
		foreach (var child in Children[leaf]) {
			span += child.StencilSpan();
		}

		return span;
	}

	/// <summary>
	/// How many paint layers child <paramref name="leaf"/>'s items this pass run to
	/// (<see cref="SceneItem.PaintLayer"/>), at least one. A layer below the top one with nothing drawn
	/// this pass still takes its value, which costs nothing but the value.
	/// </summary>
	internal int LayerCount(int leaf) {
		int top = 0;
		foreach (var item in Items[leaf]) {
			top = Math.Max(top, item.PaintLayer);
		}

		return top + 1;
	}

	/// <summary>The eye in a frame's own space, worked out once per frame per pass.</summary>
	private Vector3 EyeIn(int frame) {
		if (_eyes.TryGetValue(frame, out var eye)) {
			return eye;
		}

		eye = Matrix4x4.Invert(_frameToWorld(frame), out var worldToFrame)
			? Vector3.Transform(_eyeWorld, worldToFrame)
			: _eyeWorld;
		_eyes[frame] = eye;
		return eye;
	}
}
