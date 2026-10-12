using HercWorks.Core.Data.File.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>dat\BASECOL.DAT</c>: every structure type's hit-sphere model, back to back in <c>BASES.DAT</c> type
/// order with no count, each in ES2's <c>.COL</c> layout (<see cref="HercCollider"/>). Layout and load:
/// docs/es1/formats/bases-dat.md#the-collision-models. Read by
/// <see cref="Io.Transform.Dbsim.Es1BaseCollisionModelsTransformer"/>.
/// </summary>
public class Es1BaseCollisionModels {
	/// <summary>One model per structure type, in type order; a model with no nodes is two zero bytes.</summary>
	public HercCollider[] Models { get; set; } = [];
}
