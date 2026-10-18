using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// A fitted weapon brings its own hit spheres and damage record, which <c>Mech_ConfigureLoadout</c>
/// puts in place of the chassis files' placeholders, and the occupancy pass after it takes out
/// every component left with no internal. See docs/retail/formats/collision-spheres.md and
/// docs/retail/formats/dmg-damage-file.md.
///
/// <para>The tests that read the retail files skip silently when no Earthsiege 2 install can be
/// found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class WeaponMountFitTests {
	/// <summary>
	/// Every template's front block is a <c>.DMG</c> piece and a <c>.COL</c> cluster, read by those
	/// formats' own walks; the records are variable-length, so a misread desyncs every one after it,
	/// and the file writes back byte for byte.
	/// </summary>
	[Fact]
	public void RoundTripsRetailFileByteForByte() {
		if (ComputerWarningTests.Content() is not { } content
				|| content.Read("dat", "WEAPONS.DAT") is not { } bytes
				|| new WeaponsSimTransformer().Parse(bytes) is not { } weapons) {
			return;
		}

		Assert.Equal(bytes, new WeaponsSimTransformer().Write(weapons));
	}

	/// <summary>
	/// The replacement copies the type's model rather than writing into it, moves every sphere by
	/// the offset, keeps each radius and derives the bound again — and only for the first cluster,
	/// node by node, that carries the component.
	/// </summary>
	[Fact]
	public void ReplaceClusterCopiesTheSharedModel() {
		var placeholder = new ColliderCluster(19, Array.Empty<ColliderSphere>());
		var neighbour = new ColliderCluster(5, new[] { new ColliderSphere(1, 2, 3, 40) });
		var model = new[] {
			new ColliderNode(3, new[] { neighbour, placeholder }),
			new ColliderNode(4, new[] { placeholder }),
		};

		var spheres = new[] { new ColliderSphere(0, -250, 0, 80), new ColliderSphere(0, 100, 0, 90) };
		var fitted = CollisionModel.ReplaceCluster(model, 19, spheres, new Vec3i(10, 20, 30));

		Assert.Empty(model[0].Clusters[1].Spheres);
		var replaced = fitted[0].Clusters[1];
		Assert.Equal(new[] { new ColliderSphere(10, -230, 30, 80), new ColliderSphere(10, 120, 30, 90) },
			replaced.Spheres);
		Assert.NotEqual(0, replaced.Bound.Radius);

		Assert.Equal(neighbour, fitted[0].Clusters[0]);
		Assert.Same(model[1].Clusters, fitted[1].Clusters);
		Assert.Empty(fitted[1].Clusters[0].Spheres);
	}

	/// <summary>
	/// One gun in fit slot 0 of an ACHILLES: its mount takes the gun's spheres about its mount point,
	/// the gun's armour and the gun's internal maximum, it stays occupied, and an empty hardpoint and a
	/// weapon bracket, which list no internal, do not.
	/// </summary>
	[Fact]
	public void FittedMountTakesItsWeaponsPieceAndEmptyOnesDropOut() {
		if (ComputerWarningTests.Content() is not { } content
				|| content.Read("dat", "ACHILLES.DAT") is not { } datBytes
				|| new HercSimDataTransformer().Parse(datBytes) is not HercSimDat data
				|| content.Read("dmg", "ACHILLES.DMG") is not { } dmgBytes
				|| new HercDamageFileTransformer().Parse(dmgBytes) is not HercSimDamage damageModel
				|| content.Read("gl", "ACHILLES.GL") is not { } glBytes
				|| new GunLayoutTransformer().Parse(glBytes) is not GunLayout hardpoints
				|| WeaponCatalog.Load(content.Read("dat", "WEAPONS.DAT"), content.Read("dat", "PROJ.DAT"))
					is not { } catalog) {
			return;
		}

		var damage = new ComponentDamage(damageModel, ComponentDamage.MechComponentCount,
			ComponentDamage.MechDependentCount, new SimRandom(1));
		var mech = new MechObject("ACHILLES", data, 0,
			new MechLoadout(new[] { 1 }, Array.Empty<short>()), hardpoints: hardpoints,
			weapons: catalog, collision: CollisionModelReader.Load(content, "ACHILLES"), damage: damage);

		var mount = Assert.Single(mech.Weapons.Mounts, m => m.LoadoutSlot == 0);
		var template = mount.Template!;
		Assert.NotEmpty(template.Cluster.Spheres);
		var offset = mount.MountPointOffset;
		Assert.Equal(
			template.Cluster.Spheres.Select(s =>
				new ColliderSphere((short)(s.X + offset.X), (short)(s.Y + offset.Y), (short)(s.Z + offset.Z), s.Radius)),
			mech.Collision.SelectMany(n => n.Clusters).Single(c => c.ComponentIndex == 19).Spheres);

		Assert.Equal(template.Piece.Armor, damage.Piece(19)!.Armor);
		Assert.Equal(template.InternalMaximum, damage.DependentMax(HercInternals.FirstWeaponMountId));
		Assert.True(damage.IsActive(19));
		Assert.False(damage.IsActive(20));
		Assert.False(damage.IsActive(2));
		Assert.True(damage.IsActive(7));

		// The type's own record is untouched: the next machine of the type starts from the file.
		var fromFile = new HercDamageFileTransformer().Parse(dmgBytes) as HercSimDamage;
		Assert.Equal(fromFile!.ComponentData![19].Armor, damageModel.ComponentData![19].Armor);
		Assert.NotEqual(template.Piece.Armor, damageModel.ComponentData[19].Armor);
	}
}
