using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Scene;

/// <summary>
/// How each drawn object is filed by terrain cell, which each pass's walk turns into whether it is drawn --
/// see ObjectDrawTable. A machine, a structure and a flyer keep one entry for the mission, shared by every
/// item it draws as; everything rebuilt each frame gets a new one each frame, shared by its items and its
/// billboards.
/// </summary>
public sealed class DrawFiling(MissionScene scene) {
	private readonly Dictionary<SimObject, DrawEntry> _objectEntries = new();
	private readonly Dictionary<object, DrawEntry> _frameEntries = new(ReferenceEqualityComparer.Instance);

	// This frame's transient entries in the order they were made, the beam spans' among them.
	private readonly List<DrawEntry> _frameOrder = new();

	/// <summary>
	/// The entry of a machine, a structure or a flyer, made the first time it is asked for and kept: the object
	/// is in the world for the whole mission, and its cell is what anything filed with it reads.
	/// </summary>
	public DrawEntry ObjectEntry(SimObject subject) {
		if (!_objectEntries.TryGetValue(subject, out var entry)) {
			entry = new DrawEntry(subject) { Tag = TagOf(subject) };
			_objectEntries[subject] = entry;
		}

		return entry;
	}

	/// <summary>The type tag of a machine, a structure, a ground vehicle or a flyer.</summary>
	public static ObjectTypeTag TagOf(SimObject subject) => subject switch {
		MechObject => ObjectTypeTag.Herc,
		FlyerObject => ObjectTypeTag.Flyer,
		BaseObject { Class: BaseObject.StructureClass.GroundVehicle } => ObjectTypeTag.GroundVehicle,
		_ => ObjectTypeTag.Structure,
	};

	/// <summary>
	/// The entry of something rebuilt every frame, made the first time this frame it is asked for, so a round's
	/// mesh and its billboards share one. Scene_SubmitObject (004282d8) files it by its own position and shape
	/// radius unless it is filed with another object.
	/// </summary>
	public DrawEntry FrameEntry(object subject, ObjectTypeTag tag, Vec3i position, int shapeRadius,
			SimObject? fileWith = null) {
		if (!_frameEntries.TryGetValue(subject, out var entry)) {
			entry = new DrawEntry(subject) {
				Tag = tag,
				Position = position,
				FilingRadius = shapeRadius,
				ShapeRadius = shapeRadius,
				FileWith = fileWith,
			};
			_frameEntries[subject] = entry;
			_frameOrder.Add(entry);
		}

		return entry;
	}

	/// <summary>
	/// A new entry this frame for one tracer object of <paramref name="tracer"/>, filed by <paramref name="start"/> with no
	/// radius — see <see cref="BeamSpanDraw"/>. Each span of a straight beam is an object of its own in the original, so
	/// each call makes another.
	/// </summary>
	public DrawEntry SpanEntry(BeamTracer tracer, Vec3i start) {
		var entry = new DrawEntry(tracer) { Tag = ObjectTypeTag.Projectile, Position = start };
		_frameOrder.Add(entry);
		return entry;
	}

	/// <summary>Forgets last frame's transient entries, before this frame's are made.</summary>
	public void BeginFrame() {
		_frameEntries.Clear();
		_frameOrder.Clear();
	}

	/// <summary>
	/// Scene_SubmitFrameObjects (0042841c), once a frame: the table every pass of this frame files and culls by.
	/// The machine, structure and flyer walks come first, in the original's order -- structures, machines,
	/// flyers -- because a machine standing in a structure is filed under the cell the structure walk has just
	/// picked, and an owned effect and a fire under their owner's. An object still waiting on its mission
	/// action is not submitted. The rest follow in the original's pool order -- projectiles (bullets, missiles,
	/// then beam spans, where the original's one pool interleaves them by age), impact effects, debris,
	/// fires, drop pods -- each in the order it was made this frame, which is its list's, oldest first. Nothing is
	/// filed with any of them; the order decides which of two entries at the same distance in one cell the
	/// painter's-order draw paints first (docs/retail/rendering/terrain-drawing.md, "What is filed where").
	///
	/// <para>The camera this frame's passes ride is set here; the missile camera's pass, which rides nothing,
	/// clears it around its own draw.</para>
	/// </summary>
	public void Submit(GroundShapeLayer groundLayer, SimObject? cameraAttachedTo) {
		var table = groundLayer.Objects;
		table.Entries.Clear();
		table.LocalPlayer = scene.PlayerMech;
		table.CameraAttachedTo = cameraAttachedTo;

		Submit<BaseObject>();
		Submit<MechObject>();
		Submit<FlyerObject>();

		// Stable, so each pool keeps the order its entries were made in.
		table.Entries.AddRange(_frameOrder.OrderBy(entry => PoolOrder(entry.Tag)));

		// Every object of the class, drawn or not: a structure with no model of its own still files the
		// machines standing in it.
		void Submit<T>() where T : SimObject {
			foreach (var sceneObject in scene.Objects) {
				if (sceneObject.Object is not T subject || subject.AwaitingDeployment) {
					continue;
				}

				var entry = ObjectEntry(subject);
				Place(entry, subject);
				entry.FileWith = (subject as MechObject)?.StandingIn;
				table.Entries.Add(entry);
			}
		}
	}

	// Where a transient entry's pool comes in Scene_SubmitFrameObjects (0042841c)'s walk after the flyers.
	private static int PoolOrder(ObjectTypeTag tag) => tag switch {
		ObjectTypeTag.Projectile => 0,
		ObjectTypeTag.Effect or ObjectTypeTag.EffectFar => 1,
		ObjectTypeTag.Debris => 2,
		ObjectTypeTag.Fire => 3,
		ObjectTypeTag.DropPod => 4,
		_ => 5,
	};

	/// <summary>Brings an object's entry to where it stands now: what the submit files it by and measures it from.</summary>
	public static void Place(DrawEntry entry, SimObject subject) {
		entry.Position = subject.Position;
		entry.FilingRadius = subject.HitRadius;
		entry.ShapeRadius = subject.ShapeRadius;
		entry.EntryHeight = entry.Tag is ObjectTypeTag.Herc or ObjectTypeTag.Structure ? subject.SightHeight : 0;
	}
}
