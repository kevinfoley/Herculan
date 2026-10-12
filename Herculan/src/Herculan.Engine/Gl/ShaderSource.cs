using System.Reflection;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// Loads GLSL out of the <c>Render/Shaders</c> folder, where a program's stages share one
/// <c>.glsl</c> file and are selected by <c>#ifdef</c>.
///
/// <para>One file per program rather than one per stage because the two stages have to agree: the
/// vertex shader's <c>out</c> declarations and the fragment shader's <c>in</c> declarations are the
/// same list, and a mismatch between them is a link error at startup. Sharing a file lets that list
/// be written once (see the <c>VARYING</c> macro in <c>Scene.glsl</c>).</para>
///
/// <para>The files are embedded in the assembly rather than copied beside it: shaders are part of
/// the engine, not content the game install supplies, and embedding means there is no path to
/// resolve and nothing to go missing at runtime.</para>
/// </summary>
public static class ShaderSource {
	private const string ResourceFolder = "Herculan.Engine.Render.Shaders.";

	/// <summary>
	/// Reads one shader file by name — <c>"Scene.glsl"</c>, not a path — with both stages still in
	/// it. Use <see cref="ForStage"/> to get the text for one of them.
	/// </summary>
	public static string Load(string fileName) => Load(typeof(ShaderSource).Assembly, ResourceFolder, fileName);

	/// <summary>
	/// Reads one shader file embedded in another assembly — a game assembly's own programs — under the resource
	/// folder <paramref name="resourceFolder"/>, which ends in a dot (<c>"Herculan.Es1.Render.Shaders."</c>).
	/// </summary>
	public static string Load(Assembly assembly, string resourceFolder, string fileName) {
		string resource = resourceFolder + fileName;
		using Stream? stream = assembly.GetManifestResourceStream(resource);
		if (stream == null) {
			throw new FileNotFoundException(
				$"No embedded shader '{resource}'. Shader files must be listed as <EmbeddedResource> in " +
				$"{assembly.GetName().Name}.csproj.", fileName);
		}

		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	/// <summary>
	/// The same source cut down to one stage, by defining the symbol that stage's <c>#ifdef</c> is
	/// keyed on — <c>VERTEX_SHADER</c> or <c>FRAGMENT_SHADER</c> — plus any
	/// <paramref name="variantDefines"/> the caller wants set, which is how one file yields more than
	/// one program (<c>EDITOR_GRID</c>).
	///
	/// <para>The defines have to go after the <c>#version</c> line, which GLSL requires to come first,
	/// so they are spliced in behind it and followed by a <c>#line</c> directive putting the count
	/// back. Without that every compiler error would report a line past where it is in the file.</para>
	/// </summary>
	public static string ForStage(string source, ShaderType stage, IReadOnlyList<string>? variantDefines = null) {
		string define = stage switch {
			ShaderType.VertexShader => "VERTEX_SHADER",
			ShaderType.FragmentShader => "FRAGMENT_SHADER",
			_ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "No define is assigned to this stage.")
		};

		int firstBreak = source.IndexOf('\n');
		if (firstBreak < 0) {
			throw new InvalidOperationException("Shader source has no #version line to splice a stage define after.");
		}

		string version = source[..firstBreak].TrimEnd('\r');
		if (!version.StartsWith("#version", StringComparison.Ordinal)) {
			throw new InvalidOperationException($"Shader source must open with #version, not '{version}'.");
		}

		var header = new System.Text.StringBuilder(version).Append('\n');
		header.Append("#define ").Append(define).Append(" 1\n");
		foreach (string variant in variantDefines ?? Array.Empty<string>()) {
			header.Append("#define ").Append(variant).Append(" 1\n");
		}

		// However many lines went in above, the next one is the file's own line 2.
		return header.Append("#line 2\n").Append(source[(firstBreak + 1)..]).ToString();
	}
}
