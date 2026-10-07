using System.Numerics;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// A compiled and linked GLSL program, with uniform lookups cached by name.
///
/// <para>Kept deliberately thin. Per docs/herculan/planning.md's rendering decision the engine starts
/// on OpenGL concretely and does <i>not</i> build a backend abstraction up front — an abstraction
/// designed against GL alone tends to bake in assumptions (implicit state, no explicit
/// synchronisation) that don't survive contact with Vulkan. This type is a convenience over raw GL
/// calls, not a portable render interface pretending to be one.</para>
/// </summary>
public sealed class ShaderProgram : IDisposable {
	private readonly GL _gl;
	private readonly uint _handle;
	private readonly Dictionary<string, int> _uniforms = new();

	/// <summary>
	/// Private so that <see cref="Load"/> is the only way in: shader source belongs in a file under
	/// <c>Render/Shaders</c>, not in a string constant beside the renderer that uses it.
	/// </summary>
	private ShaderProgram(GL gl, string vertexSource, string fragmentSource) {
		_gl = gl;

		uint vertex = Compile(ShaderType.VertexShader, vertexSource);
		uint fragment = Compile(ShaderType.FragmentShader, fragmentSource);

		_handle = _gl.CreateProgram();
		_gl.AttachShader(_handle, vertex);
		_gl.AttachShader(_handle, fragment);
		_gl.LinkProgram(_handle);

		_gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int linked);
		if (linked == 0) {
			string log = _gl.GetProgramInfoLog(_handle);
			throw new InvalidOperationException($"Shader program failed to link: {log}");
		}

		// Shader objects stay alive only as long as they are attached to an unlinked program.
		_gl.DetachShader(_handle, vertex);
		_gl.DetachShader(_handle, fragment);
		_gl.DeleteShader(vertex);
		_gl.DeleteShader(fragment);
	}

	/// <summary>
	/// Compiles the program in <c>Render/Shaders/<paramref name="fileName"/></c>, whose two stages
	/// share the one file. See <see cref="ShaderSource"/>.
	/// </summary>
	/// <param name="variantDefines">
	/// Preprocessor symbols to define, selecting which optional blocks of the file are compiled in.
	/// Code left out this way is not in the program at all — no uniforms, no varyings, nothing to
	/// branch on at runtime.
	/// </param>
	public static ShaderProgram Load(GL gl, string fileName, params string[] variantDefines) {
		string source = ShaderSource.Load(fileName);
		try {
			return new ShaderProgram(gl,
				ShaderSource.ForStage(source, ShaderType.VertexShader, variantDefines),
				ShaderSource.ForStage(source, ShaderType.FragmentShader, variantDefines));
		} catch (InvalidOperationException ex) {
			// The driver's log counts lines within one stage and names no file, which is no help at
			// all once more than one shader file exists.
			throw new InvalidOperationException($"{fileName}: {ex.Message}", ex);
		}
	}

	public void Use() => _gl.UseProgram(_handle);

	public void SetMatrix(string name, Matrix4x4 value) {
		ReadOnlySpan<float> elements = stackalloc float[] {
			value.M11, value.M12, value.M13, value.M14,
			value.M21, value.M22, value.M23, value.M24,
			value.M31, value.M32, value.M33, value.M34,
			value.M41, value.M42, value.M43, value.M44
		};

		// transpose MUST stay false, and the reason is easy to get backwards. System.Numerics uses
		// the row-vector convention (v * M) and stores elements row-major; GLSL uses the
		// column-vector convention (M * v) and, with transpose false, reads a uniform array
		// column-major. So writing the elements out in System.Numerics' row-major order and telling
		// GL *not* to transpose hands it the transpose of the matrix — which is precisely the
		// column-vector form of the same transform. Passing true instead cancels that out and gives
		// GL a matrix whose translation sits in the bottom row, where a column-vector multiply
		// ignores it and the perspective divide produces a negative w; geometry then clips away
		// entirely and the screen shows nothing but the clear colour.
		_gl.UniformMatrix4(Location(name), 1, false, elements);
	}

	public void SetVector2(string name, Vector2 value) => _gl.Uniform2(Location(name), value.X, value.Y);

	public void SetVector3(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);

	public void SetVector4(string name, Vector4 value) =>
		_gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);

	public void SetFloat(string name, float value) => _gl.Uniform1(Location(name), value);

	public void SetInt(string name, int value) => _gl.Uniform1(Location(name), value);

	public void SetUInt(string name, uint value) => _gl.Uniform1(Location(name), value);

	public void SetSamplerTexture(string name, uint textureHandle, uint textureUnit) {
		_gl.ActiveTexture(TextureUnit.Texture0 + (int)textureUnit);
		_gl.BindTexture(TextureTarget.Texture2D, textureHandle);
		_gl.Uniform1(Location(name), (int)textureUnit);
	}

	private int Location(string name) {
		if (_uniforms.TryGetValue(name, out int cached)) {
			return cached;
		}

		int location = _gl.GetUniformLocation(_handle, name);
		_uniforms[name] = location;
		return location;
	}

	private uint Compile(ShaderType type, string source) {
		uint shader = _gl.CreateShader(type);
		_gl.ShaderSource(shader, source);
		_gl.CompileShader(shader);

		_gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
		if (compiled == 0) {
			string log = _gl.GetShaderInfoLog(shader);
			_gl.DeleteShader(shader);
			throw new InvalidOperationException($"{type} failed to compile: {log}");
		}

		return shader;
	}

	public void Dispose() => _gl.DeleteProgram(_handle);
}
