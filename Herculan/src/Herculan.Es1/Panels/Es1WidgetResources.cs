using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Es1.Panels;

/// <summary>
/// What <c>Widget_LoadStylesAndFonts</c> (<c>ES1DBSIM 00045e10</c>) loads at the mission start, <c>Sim_StartMission</c>'s
/// step 4 (docs/es1/formats/styles-dat.md#the-loads): the style table <c>dat\styles</c>, the seven <c>dfn\</c> fonts
/// <c>WidgetFont_Title</c> to <c>WidgetFont_Red6x8</c> (<c>000bd542</c>–<c>000bd55a</c>) and <c>dba\corners</c>,
/// <c>WidgetCorners</c> (<c>000bd53e</c>). A missing file leaves its member null, and what would draw with it draws
/// nothing.
/// </summary>
public sealed class Es1WidgetResources {
	/// <summary>The style table <c>00094ccc</c>.</summary>
	public Es1WidgetStyles? Styles { get; init; }

	/// <summary><c>WidgetFont_Title</c> (<c>000bd542</c>), <c>dfn\title</c>.</summary>
	public HudFont? Title { get; init; }

	/// <summary><c>WidgetFont_Cpylw</c> (<c>000bd546</c>), <c>dfn\cpylw</c>.</summary>
	public HudFont? Cpylw { get; init; }

	/// <summary><c>WidgetFont_Active</c> (<c>000bd54a</c>), <c>dfn\active</c>: a panel button's at rest.</summary>
	public HudFont? Active { get; init; }

	/// <summary><c>WidgetFont_Pushed</c> (<c>000bd54e</c>), <c>dfn\pushed</c>: a panel button's pressed.</summary>
	public HudFont? Pushed { get; init; }

	/// <summary><c>WidgetFont_Inactive</c> (<c>000bd552</c>), <c>dfn\inactive</c>: a panel button's greyed.</summary>
	public HudFont? Inactive { get; init; }

	/// <summary><c>WidgetFont_Green6x8</c> (<c>000bd556</c>), <c>dfn\green6x8</c>.</summary>
	public HudFont? Green6x8 { get; init; }

	/// <summary><c>WidgetFont_Red6x8</c> (<c>000bd55a</c>), <c>dfn\red6x8</c>.</summary>
	public HudFont? Red6x8 { get; init; }

	/// <summary><c>WidgetCorners</c>, the frames of <c>dba\corners</c>.</summary>
	public IReadOnlyList<DynamixBitmap>? Corners { get; init; }

	/// <summary>Style record <paramref name="index"/>; none when the table or the record is missing.</summary>
	public Es1WidgetStyle? Style(int index) =>
		Styles?.Styles is { } styles && index >= 0 && index < styles.Length ? styles[index] : null;

	/// <summary><c>Widget_LoadStylesAndFonts</c> from <paramref name="content"/>.</summary>
	public static Es1WidgetResources Load(GameContent content) => new() {
		Styles = content.Read("dat", "STYLES.DAT") is { } styles ? new Es1WidgetStylesTransformer().Parse(styles) : null,
		Title = HudFont.Load(content, "TITLE", "dfn"),
		Cpylw = HudFont.Load(content, "CPYLW", "dfn"),
		Active = HudFont.Load(content, "ACTIVE", "dfn"),
		Pushed = HudFont.Load(content, "PUSHED", "dfn"),
		Inactive = HudFont.Load(content, "INACTIVE", "dfn"),
		Green6x8 = HudFont.Load(content, "GREEN6X8", "dfn"),
		Red6x8 = HudFont.Load(content, "RED6X8", "dfn"),
		Corners = LoadFrames(content, "CORNERS"),
	};

	/// <summary>The frames of <c>dba\&lt;name&gt;</c>; null when the file is missing or does not parse.</summary>
	internal static IReadOnlyList<DynamixBitmap>? LoadFrames(GameContent content, string name) =>
		content.Read("dba", name + ".DBA") is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;
}
