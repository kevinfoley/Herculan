using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The three bitmap banks the gunsight's slide bars and heading tape load the first time one is built, each from
/// <c>dba\</c>, the folder the pointer <c>000980f8</c> names (docs/es1/simulation/cockpit-gunsight-hud.md#the-slide-bars,
/// docs/es1/simulation/cockpit-gunsight-hud.md#the-heading-tape). A missing file leaves its bank null, and what would
/// draw from it draws nothing.
/// </summary>
public sealed class Es1GunsightBanks {
	/// <summary><c>HudHSlidBank</c> (<c>ES1DBSIM 000936c8</c>), <c>dba\hudhslid</c>: the rotation indicator's track and bars.</summary>
	public IReadOnlyList<DynamixBitmap>? HSlid { get; init; }

	/// <summary><c>HudVSlidBank</c> (<c>000936cc</c>), <c>dba\hudvslid</c>: the vertical bar's track and bars.</summary>
	public IReadOnlyList<DynamixBitmap>? VSlid { get; init; }

	/// <summary><c>HudHTickBank</c> (<c>000936d0</c>), <c>dba\hudhtick</c>: the heading tape's strip, frame by frame.</summary>
	public IReadOnlyList<DynamixBitmap>? HTick { get; init; }

	/// <summary>The three banks from the content.</summary>
	/// <param name="content">The game's content.</param>
	public static Es1GunsightBanks Load(GameContent content) => new() {
		HSlid = Es1WidgetResources.LoadFrames(content, "HUDHSLID"),
		VSlid = Es1WidgetResources.LoadFrames(content, "HUDVSLID"),
		HTick = Es1WidgetResources.LoadFrames(content, "HUDHTICK"),
	};

	/// <summary>The frame at an index of a bank; null when the bank or the frame is missing.</summary>
	/// <param name="bank">The bank, or null.</param>
	/// <param name="index">The frame's index.</param>
	internal static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? bank, int index) =>
		bank != null && index >= 0 && index < bank.Count ? bank[index] : null;
}
