using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// What <c>ShellMap_LoadResources</c> (<c>000093b3</c>) loads at the start-up's step 13 for every map the shell builds
/// (docs/es1/shell/startup.md#the-start-up): the clock, <c>dba\icons</c>, <c>dba\mapshp</c> and the font
/// <c>dfn\map</c>.
/// </summary>
public sealed class Es1ShellMapResources {
	private Es1ShellMapResources() {
	}

	/// <summary><c>ShellMap_Ticks</c> (<c>0007e204</c>), the map's clock, 0 in the image (docs/es1/shell/map.md#the-tick).</summary>
	public int Ticks { get; private set; }

	/// <summary>The slot <c>Timer_AddCallback</c> answered, kept at <c>0007e20a</c>.</summary>
	public int TimerSlot { get; private set; }

	/// <summary><c>dba\icons</c> at <c>000aa2b8</c>, the icons' frames (docs/es1/shell/map.md#the-icons); none when it does not load.</summary>
	public DynamixBitmapArray? Icons { get; private set; }

	/// <summary><c>dba\mapshp</c> at <c>000aa2bc</c>, the 2D map's object bitmap (docs/es1/shell/map.md#the-shapes); none when it does not load.</summary>
	public DynamixBitmapArray? ObjectBitmaps { get; private set; }

	/// <summary>
	/// <c>ShellMap_LabelFont</c> (<c>000aa2c0</c>), <c>dfn\map</c>, which the map's labels draw in and its gadgets measure
	/// their text in; none when it does not load.
	/// </summary>
	public HudFont? LabelFont { get; private set; }

	/// <summary>
	/// <c>ShellMap_LoadResources</c>: <see cref="TimerTick"/> registered at a count of 2; <c>dba\icons</c> and
	/// <c>dba\mapshp</c> through <c>ShellMap_LoadResource</c> (<c>0000923c</c>); <c>dfn\map</c> through <c>00009275</c>,
	/// the same body returning a font. Retail loads each through <c>ClassItem_LoadResource</c> unchecked; here one that
	/// does not load is none, reported. The arena of <see cref="HercWorks.Core.Data.File.Bnd.Es1ShellMapBnd.ShapeArenaSize"/>
	/// bytes it allocates at <c>0007e20c</c> for <c>ShellMap_LoadShapeRoots</c> is not modelled.
	/// </summary>
	public static Es1ShellMapResources Load(GameContent content, Es1SimClock timer) {
		var resources = new Es1ShellMapResources();
		resources.TimerSlot = timer.AddCallback(2, resources.TimerTick);
		resources.Icons = ReadBitmaps(content, "ICONS.DBA");
		resources.ObjectBitmaps = ReadBitmaps(content, "MAPSHP.DBA");
		resources.LabelFont = HudFont.Load(content, "MAP", "dfn");
		if (resources.LabelFont == null) {
			Console.Error.WriteLine("dfn\\map does not load: the map's labels draw nothing.");
		}

		return resources;
	}

	/// <summary><c>ShellMap_TimerTick</c> (<c>000092ae</c>), every second interrupt of the shell's timer: <see cref="Ticks"/> up by 1.</summary>
	private void TimerTick() => Ticks++;

	private static DynamixBitmapArray? ReadBitmaps(GameContent content, string name) {
		var bitmaps = content.Read("dba", name) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes) : null;
		if (bitmaps == null) {
			Console.Error.WriteLine($"dba\\{name} does not load.");
		}

		return bitmaps;
	}
}
