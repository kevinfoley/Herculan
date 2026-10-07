using HercWorks.Vol;

namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// <c>DATA\LANGUAGE.CFG</c> — its first byte, which <c>Language_GetFolderName</c> (<c>0045efe0</c>)
/// maps to <c>ENGLISH</c>, <c>FRENCH</c>, <c>GERMAN</c> or <c>SPANISH</c>. The installer writes
/// 'E', 'F' or 'G'. See docs/retail/retail-builds.md.
/// </summary>
public class Language : DataFile {
	public Language() : base("LANGUAGE.CFG", "DATA/") { }
}
