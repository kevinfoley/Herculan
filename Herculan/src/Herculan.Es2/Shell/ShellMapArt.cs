using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es2.Content;

namespace Herculan.Es2.Shell;

/// <summary>The map's icons, <c>dba\mis_icon.dba</c>, which <c>ShellMap_EnsureResourcesLoaded</c> (<c>00423db4</c>) loads.</summary>
public sealed class ShellMapArt {
	private readonly DynamixBitmap[]? _icons;

	private ShellMapArt(DynamixBitmap[]? icons) => _icons = icons;

	public DynamixBitmap? Icon(int frame) => _icons is { } icons && frame >= 0 && frame < icons.Length ? icons[frame] : null;

	public static ShellMapArt Load(GameContent content) => new(ShellArt.ReadBankFrames(content, "MIS_ICON"));
}
