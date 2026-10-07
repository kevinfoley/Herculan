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

	/// <summary>
	/// The entry of a machine, a structure or a flyer, made the first time it is asked for and kept: the object
	/// is in the world for the whole mission, and its cell is what anything filed with it reads.
	/// </summary>
	public DrawEntry ObjectEntry(SimObject subject) {
		if (!_objectEntries.TryGetValue(subject, out var entry)) {
			entry = new DrawEntry(subject) {
				Tag = subject switch {
					MechObject => ObjectTypeTag.Herc,
					FlyerObject => ObjectTypeTag.Flyer,
					_ => ObjectTypeTag.Structure,
				},
			};
			_objectEntries[subject] = entry;
		}

		return entry;
	}

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
		}

		return entry;
	}

	/// <summary>Forgets last frame's transient entries, before this frame's are made.</summary>
	public void BeginFrame() => _frameEntries.Clear();

	/// <summary>
	/// Scene_SubmitFrameObjects (0042841c), once a frame: the table every pass of this frame files and culls by.
	/// The machine, structure and flyer walks come first, in the original's order -- structures, machines,
	/// flyers -- because a machine standing in a structure is filed under the cell the structure walk has just
	/// picked, and an owned effect and a fire under their owner's. An object still waiting on its mission
	/// action is not submitted. The rest follow; nothing is filed with any of them, so their order changes
	/// nothing.
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
		table.Entries.AddRange(_frameEntries.Values);

		// Every object of the class, drawn or not: a structure with no model of its own still files the
		// machines standing in it.
		void Submit<T>() where T : SimObject {
			foreach (var sceneObject in scene.Objects) {
				if (sceneObject.Object is not T subject || subject.AwaitingDeployment) {
					continue;
				}

				var entry = ObjectEntry(subject);
				entry.Position = subject.Position;
				entry.FilingRadius = subject.HitRadius;
				entry.ShapeRadius = subject.ShapeRadius;
				entry.EntryHeight = subject is MechObject or BaseObject ? subject.SightHeight : 0;
				entry.FileWith = (subject as MechObject)?.StandingIn;
				table.Entries.Add(entry);
			}
		}
	}
}
