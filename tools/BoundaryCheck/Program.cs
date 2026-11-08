// Lists every reference from a core file of Herculan.Engine to a symbol declared in ES2's game code, so the core
// can be shown free of ES2 before that code leaves for its own assembly (Herculan/docs/herculan/planning.md, "How
// the two games share code"). es2-files.txt beside this file says which folders and files are ES2's.
//
// Roslyn binds the whole engine compilation, method bodies and doc crefs included. Building the core folders
// alone is not a substitute: once a file's using line fails to bind, the compiler stops reporting errors in its
// bodies, so that build undercounts.
//
// Usage: dotnet run --project tools/BoundaryCheck [-- --code]
//   --code  report only references in code and using lines, not in doc comments.
// Build the engine first: the references are its resolved ReferencePath items. Exits 1 when anything is reported.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

bool codeOnly = args.Contains("--code");

string root = FindRoot(Directory.GetCurrentDirectory()) ?? FindRoot(AppContext.BaseDirectory)
	?? throw new InvalidOperationException("Run from inside the repository: no Herculan/src/Herculan.Engine above here.");
string engine = Path.Combine(root, "Herculan", "src", "Herculan.Engine");
string project = Path.Combine(engine, "Herculan.Engine.csproj");
string listFile = Path.Combine(root, "tools", "BoundaryCheck", "es2-files.txt");

// es2-files.txt: one path per line, relative to the engine project; a trailing '/' marks a folder, whose
// namespace counts as ES2's too. '#' starts a comment line.
var entries = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
var folders = entries.Where(e => e.EndsWith('/')).Select(e => e.TrimEnd('/')).ToList();
var listedFiles = entries.Where(e => !e.EndsWith('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
var es2Namespaces = folders.Select(f => "Herculan.Engine." + f.Replace('/', '.')).ToList();
foreach (var f in folders.Where(f => !Directory.Exists(Path.Combine(engine, f))))
	Console.Error.WriteLine($"es2-files.txt: no folder {f}/");
foreach (var f in listedFiles.Where(f => !File.Exists(Path.Combine(engine, f))))
	Console.Error.WriteLine($"es2-files.txt: no file {f}");

using var msbuild = JsonDocument.Parse(RunMsbuild(project));
var items = msbuild.RootElement.GetProperty("Items");
var properties = msbuild.RootElement.GetProperty("Properties");

var references = new List<MetadataReference>();
foreach (var item in items.GetProperty("ReferencePath").EnumerateArray()) {
	string path = item.GetProperty("FullPath").GetString()!;
	if (!File.Exists(path)) {
		Console.Error.WriteLine($"Missing reference {path}: build Herculan/HerculanEngine.sln first.");
		return 2;
	}
	references.Add(MetadataReference.CreateFromFile(path));
}

var parse = new CSharpParseOptions(
	LanguageVersionFacts.TryParse(properties.GetProperty("LangVersion").GetString(), out var lang) ? lang : LanguageVersion.Default,
	DocumentationMode.Diagnose,
	SourceCodeKind.Regular,
	(properties.GetProperty("DefineConstants").GetString() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));

var trees = items.GetProperty("Compile").EnumerateArray()
	.Select(i => i.GetProperty("FullPath").GetString()!)
	.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parse, p))
	.ToList();
const string GlobalUsingsPath = "<global usings>";
trees.Add(CSharpSyntaxTree.ParseText(string.Concat(items.GetProperty("Using").EnumerateArray().Select(GlobalUsing)), parse, GlobalUsingsPath));

Compilation compilation = CSharpCompilation.Create("Herculan.Engine", trees, references,
	new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
		allowUnsafe: properties.GetProperty("AllowUnsafeBlocks").GetString() == "true",
		nullableContextOptions: Enum.TryParse<NullableContextOptions>(properties.GetProperty("Nullable").GetString(), true, out var nullable)
			? nullable : NullableContextOptions.Disable));
// Source generators fill in partial members such as [GeneratedRegex]; without them those are compile errors.
var generators = items.GetProperty("Analyzer").EnumerateArray()
	.Select(i => new AnalyzerFileReference(i.GetProperty("FullPath").GetString()!, new AnalyzerLoader()))
	.SelectMany(a => a.GetGenerators(LanguageNames.CSharp))
	.ToList();
CSharpGeneratorDriver.Create(generators, parseOptions: parse).RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _);
var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
if (errors.Count > 0) {
	Console.Error.WriteLine($"The engine does not compile here ({errors.Count} errors), so names in broken code may be missed:");
	foreach (var e in errors.Take(5)) Console.Error.WriteLine("  " + e);
}

string Rel(string path) => Path.GetRelativePath(engine, path).Replace('\\', '/');
bool IsEs2File(string rel) => listedFiles.Contains(rel) || folders.Any(f => rel.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
bool IsEs2Namespace(string name) => es2Namespaces.Any(n => name == n || name.StartsWith(n + "."));

// file -> kind ("code", "using", "doc") -> the ES2 names it uses.
var found = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
foreach (var tree in trees) {
	if (tree.FilePath == GlobalUsingsPath) continue;
	string rel = Rel(tree.FilePath);
	if (IsEs2File(rel)) continue;
	var model = compilation.GetSemanticModel(tree);
	foreach (var node in tree.GetRoot().DescendantNodes(descendIntoTrivia: true)) {
		if (node is not (SimpleNameSyntax or CrefSyntax)) continue;
		string kind = node is CrefSyntax || node.FirstAncestorOrSelf<DocumentationCommentTriviaSyntax>(ascendOutOfTrivia: false) != null ? "doc"
			: node.FirstAncestorOrSelf<UsingDirectiveSyntax>() != null ? "using"
			: "code";
		if (codeOnly && kind == "doc") continue;
		var info = model.GetSymbolInfo(node);
		var symbols = info.Symbol != null ? [info.Symbol] : info.CandidateSymbols.ToArray();
		foreach (var raw in symbols) {
			var symbol = raw.OriginalDefinition;
			if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
			string? name = null;
			if (symbol is INamespaceSymbol ns) {
				if (IsEs2Namespace(ns.ToDisplayString())) name = ns.ToDisplayString();
			} else {
				string? declared = symbol.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath)
					.FirstOrDefault(p => p != "" && p != GlobalUsingsPath);
				if (declared != null && IsEs2File(Rel(declared)))
					name = symbol is INamedTypeSymbol ? symbol.Name : symbol.ContainingType?.Name + "." + symbol.Name;
			}
			if (name == null) continue;
			if (!found.TryGetValue(rel, out var kinds)) found[rel] = kinds = new(StringComparer.Ordinal);
			if (!kinds.TryGetValue(kind, out var names)) kinds[kind] = names = new(StringComparer.Ordinal);
			names.Add(name);
		}
	}
}

foreach (var (file, kinds) in found)
	Console.WriteLine(file + " | " + string.Join(" | ", kinds.Select(k => k.Key + ": " + string.Join(", ", k.Value))));
int inCode = found.Count(f => f.Value.ContainsKey("code") || f.Value.ContainsKey("using"));
Console.WriteLine(codeOnly
	? $"{inCode} core files reference ES2 in code."
	: $"{found.Count} core files reference ES2: {inCode} in code, {found.Count - inCode} only in doc comments.");
return found.Count == 0 ? 0 : 1;

static string? FindRoot(string start) {
	for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
		if (File.Exists(Path.Combine(dir.FullName, "Herculan", "src", "Herculan.Engine", "Herculan.Engine.csproj")))
			return dir.FullName;
	return null;
}

static string RunMsbuild(string project) {
	var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
	foreach (var arg in new[] { "msbuild", project, "-nologo", "-t:ResolveReferences",
		"-getItem:ReferencePath,Compile,Using,Analyzer","-getProperty:LangVersion,DefineConstants,Nullable,AllowUnsafeBlocks" })
		start.ArgumentList.Add(arg);
	using var process = Process.Start(start)!;
	var stderr = process.StandardError.ReadToEndAsync();
	string stdout = process.StandardOutput.ReadToEnd();
	process.WaitForExit();
	int json = stdout.IndexOf('{');
	if (process.ExitCode != 0 || json < 0)
		throw new InvalidOperationException($"dotnet msbuild failed ({process.ExitCode}):\n{stdout}{stderr.Result}");
	return stdout[json..];
}

static string GlobalUsing(JsonElement item) {
	string name = item.GetProperty("Identity").GetString()!;
	string alias = item.TryGetProperty("Alias", out var a) ? a.GetString() ?? "" : "";
	bool isStatic = item.TryGetProperty("Static", out var s) && s.GetString() == "true";
	return alias != "" ? $"global using {alias} = {name};\n"
		: isStatic ? $"global using static {name};\n"
		: $"global using {name};\n";
}

sealed class AnalyzerLoader : IAnalyzerAssemblyLoader {
	public void AddDependencyLocation(string fullPath) { }
	public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
}
