using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's shadow, a <c>FlatObj</c> of the flat shape set drawn flat at height 0 (docs/es1/simulation/mech.md#the-shadows).
/// </summary>
/// <param name="Root">The flat set's root the chassis list's byte names: 0 under a foot, 2 under the body.</param>
/// <param name="PartId">The part it follows, the byte five on in the list (file offset 117 on).</param>
public sealed class Es1Shadow(int root, int partId) {
	/// <summary>The depth it is built at, out of the draw's range until it is first placed.</summary>
	public const int BuildDepth = -100000;

	public int Root { get; } = root;

	public int PartId { get; } = partId;

	/// <summary><c>+0x20</c>, the position; its Z is set to 0 at every draw (<c>FlatObj_DrawMechCopy</c>, <c>00011988</c>).</summary>
	public Vec3i Position { get; set; } = new(0, 0, BuildDepth);

	/// <summary><c>+0x0a</c>, the heading it takes from the machine.</summary>
	public short Heading { get; set; }

	/// <summary>The frame <c>FlatObj_DrawMechCopy</c> draws it at: its heading, at its position with Z 0.</summary>
	public Transform3 DrawFrame {
		get {
			var frame = Transform3.FromEuler(0, 0, Heading);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = 0;
			return frame;
		}
	}
}

/// <summary>
/// A HERC, the <c>0x24f</c>-byte machine of <c>g_MechPool</c>, as <c>Mech_Constructor</c> (<c>ES1DBSIM 00011330</c>) builds
/// it (docs/es1/simulation/mech.md#construction) and the second pass places it (docs/es1/formats/script-dat.md#the-second-pass).
/// The fields are the ones this step reads and writes; the rest of the object comes with the steps that use it.
/// </summary>
public sealed class Es1Mech {
	/// <summary>The rotation sentinel's first angle, which <c>Mech_AttachToGroup</c> tests at <c>+6</c>.</summary>
	public const short RotationSentinel = -32768;

	/// <summary>The type tag <c>Mech_Constructor</c> stores at <c>+0</c>, which the distance bands' shades list is keyed by.</summary>
	public const short TypeTag = 7;

	/// <summary>The energy pool the constructor fills, <c>+0x228</c>.</summary>
	public const short InitialEnergy = 10000;

	/// <summary>
	/// <c>Mech_Constructor</c>: steps 1, 5, 7, 8 and 11, with the others' call sites kept. <paramref name="type"/> is the
	/// chassis' type record.
	/// </summary>
	public Es1Mech(Es1MechType type) {
		Type = type;

		// Step 1: SimObjectBase_ConstructWithDetailTable on the record's roots, an animated instance on root 0.
		Shape = type.Animation != null ? new ShapeInstance(type.Animation) : null;

		// Step 3. PLACEHOLDER: Component_AllocDamageArrays (00009b8c) with 21 internals and 28 components; step 7 ports
		// the damage header.
		// Step 4. PLACEHOLDER: the five lock countdowns at +0x1ee; step 7 ports missile lock.
		// Step 5: the throttle, speed, turn and turret rates and angles start at 0.
		Energy = InitialEnergy;

		// Step 6. PLACEHOLDER: Shield_Init (0000fabc) on the record's shield capacity; step 7 ports the shields.

		// Step 7: the locomotion thread on the record's +0x12 (file offset 16, the forward stop sequence) with no test of
		// the sequence; the twist and pitch threads for a sequence that is not negative.
		// PLACEHOLDER: the word +6 of the two stop sequences (+0x12, +0x14) is zeroed, which root motion reads; step 4,
		// which ports root motion, ports it.
		if (Shape != null) {
			LocomotionThread = Shape.AddThread(type.Chassis.AnimId_StopMove);
			TwistThread = type.Chassis.AnimId_TorsoTwist >= 0 ? Shape.AddThread(type.Chassis.AnimId_TorsoTwist) : null;
			PitchThread = type.Chassis.AnimId_TorsoPitch >= 0 ? Shape.AddThread(type.Chassis.AnimId_TorsoPitch) : null;
		}

		WorthNoSalvage = type.Chassis.SalvageScale == 0;

		// Step 8: one shadow per kind byte at +0x72 (file offset 112) up to the first negative one, its part five on.
		var bytes = type.Chassis.ShadowBytes;
		for (int i = 0; i < 5 && (sbyte)bytes[i] >= 0; i++) {
			Shadows.Add(new Es1Shadow((sbyte)bytes[i], (sbyte)bytes[i + 5]));
		}

		// Step 9. PLACEHOLDER: the behaviour state, deciding or player; step 8 ports the state table.
		// Step 10. PLACEHOLDER: dmg\<name> and col\<name> loaded and bound (Damage_LoadMechDmgFile, Collision_RegisterObject,
		// Component_LinkMaxRefData); step 4 (collision) and step 7 (damage) port them.
		// Step 11: the footfall latches are step 4's.
	}

	/// <summary><c>+0x188</c>, the type record.</summary>
	public Es1MechType Type { get; }

	/// <summary><c>+0x2e</c>, the animated shape instance; null for a shape with no animation.</summary>
	public ShapeInstance? Shape { get; }

	/// <summary><c>+0x1c0</c>, <c>+0x1c4</c> and <c>+0x1c8</c>: the locomotion, twist and pitch threads.</summary>
	public AnimationThread? LocomotionThread { get; }

	/// <inheritdoc cref="LocomotionThread"/>
	public AnimationThread? TwistThread { get; }

	/// <inheritdoc cref="LocomotionThread"/>
	public AnimationThread? PitchThread { get; }

	/// <summary><c>+0x1cc</c> and <c>+0x1d0</c>, the shadows.</summary>
	public List<Es1Shadow> Shadows { get; } = new();

	/// <summary><c>+0x20</c>, the position.</summary>
	public Vec3i Position { get; set; }

	/// <summary><c>+6</c>, <c>+8</c> and <c>+0xa</c>: the Euler angles, the heading last.</summary>
	public (short X, short Y, short Z) Rotation { get; set; }

	/// <summary><c>+0x3f</c>, the group.</summary>
	public Es1MissionGroup? Group { get; set; }

	/// <summary><c>+0x43</c>, its ref's place among the group's eight.</summary>
	public int MemberSlot { get; set; }

	/// <summary><c>+0x9d</c>, the player's machine.</summary>
	public bool IsPlayer { get; set; }

	/// <summary><c>+0x91</c>, set when the record's <c>.MSN</c> <c>0x08</c> is not 0 (docs/es1/formats/script-dat.md#open).</summary>
	public bool Word08Set { get; set; }

	/// <summary><c>+0x1e6</c>, the record's <c>.MSN</c> <c>0x0a</c> when it is not negative (docs/es1/formats/script-dat.md#open).</summary>
	public short Word1E6 { get; set; }

	/// <summary><c>+0x174</c> and <c>+0x176</c>, the record's <c>.MSN</c> <c>0x4a</c> and <c>0x4c</c>; <c>0xffff</c> for the second until set.</summary>
	public (short First, short Second) Word174 { get; set; } = (0, -1);

	/// <summary><c>+0x16c</c> and <c>+0x170</c>, the two actions; the second fires when the machine goes out of the fight.</summary>
	public (Es1Action? First, Es1Action? Second) Actions { get; set; }

	/// <summary><c>+0x232</c>, a squad machine's <c>data\player.mec</c> pilot word 1.</summary>
	public short PilotWord { get; set; }

	/// <summary><c>+0xad</c>, worth no salvage: the record's salvage scale is 0.</summary>
	public bool WorthNoSalvage { get; }

	/// <summary><c>+0x228</c>, the energy pool.</summary>
	public short Energy { get; set; }

	/// <summary><c>+0x198</c>, the mount manager.</summary>
	public Es1WeaponMounts? Mounts { get; private set; }

	/// <summary>
	/// The frame <c>Context_GetTransform</c> of <c>+6</c> builds: the rotation from the angles, at the position.
	/// </summary>
	public Transform3 Frame {
		get {
			var frame = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
			frame.X = Position.X;
			frame.Y = Position.Y;
			frame.Z = Position.Z;
			return frame;
		}
	}

	/// <summary>
	/// <c>Mech_ConfigureLoadout</c> (<c>00012c00</c>): the manager, the local one for the player's machine; root motion
	/// with time 0; and each fitted mount's model stamped with its hardpoint's node.
	/// </summary>
	public void ConfigureLoadout(Func<int, short> fit, Func<int, short> ammunition, Es1Weapons weapons,
			Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor) {
		Mounts = Es1WeaponMounts.Build(IsPlayer, Type.GunLayout, fit, ammunition, weapons, modelFor,
			sequence => Shape!.AddThread(sequence));
		ApplyRootMotionIfEnabled(0);

		foreach (var mount in Mounts.Mounts) {
			if (mount == null) {
				continue;
			}

			// The hardpoint's node, found through the shape's slot +0x24, stamped onto the model (Shape_StampTransformId).
			if (Type.Roots.Count > 0 && Es1MechType.FindPart(Type.Roots[0], mount.Hardpoint.BoneId) is { } part) {
				mount.ModelTransformId = part.Transform;
			}

			// PLACEHOLDER: the template's spheres, piece and internal maximum put into the machine's model and table
			// (Collision_ReplaceClusters, HercPiece_ReplacePieces, HercPiece_SetInternalMaxima), the anchors bound and the
			// occupancy flags cleared; step 7 ports them with the damage tables.
		}
	}

	/// <summary>
	/// <c>SimObject_ApplyRootMotionIfEnabled</c> (<c>0000215c</c>) with the instance animated: the root seeded with the
	/// identity, the animation stepped by <paramref name="time"/>, the root's translation put through the rotation and added
	/// to the position and its angles to the object's (docs/es1/simulation/object-classes.md#animation-threads-and-root-motion).
	/// The root is the locomotion thread's, the first registered.
	/// </summary>
	public void ApplyRootMotionIfEnabled(short time) {
		if (Shape == null || LocomotionThread == null) {
			return;
		}

		LocomotionThread.WriteRoot(Transform3.Identity);
		Shape.StepAnimation(time);
		var root = LocomotionThread.ReadRoot();
		var moved = Frame.RotateVector(root.X, root.Y, root.Z);
		Position += moved;
		var (x, y, z) = root.ToEuler();
		Rotation = ((short)(Rotation.X + x), (short)(Rotation.Y + y), (short)(Rotation.Z + z));
		LocomotionThread.WriteRoot(Transform3.Identity);
	}

	/// <summary>A node's pose in the machine's own frame, by its part id; the identity for a part the shape lacks.</summary>
	public Transform3 PartTransform(int partId) {
		if (Shape == null || Type.Animation is not { } animation) {
			return Transform3.Identity;
		}

		int transformId = animation.TransformIdOfPart(partId);
		return transformId >= 0 ? Shape.NodeTransform(transformId) : Transform3.Identity;
	}

	/// <summary>
	/// The placement half of <c>Mech_PlaceLegsOnGround</c> (<c>000144dc</c>): each shadow at its part's node through the
	/// machine's frame, with the machine's heading (docs/es1/simulation/mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc).
	/// </summary>
	public void PlaceShadows() {
		var frame = Frame;
		foreach (var shadow in Shadows) {
			var node = PartTransform(shadow.PartId);
			shadow.Position = frame.TransformPoint(node.X, node.Y, node.Z);
			shadow.Heading = Rotation.Z;
		}
	}
}
