// rename_symbol: a symbol-aware rename over a solution, through Roslyn's Renamer.
//
//   dotnet run --project tools/scripts/rename_symbol -- <solution> <Type|Type.Member> <NewName> [options]
//
// <Type> is a simple name (MissionGroup164), a namespace-qualified name (HercWorks.Core.X.MissionGroup164)
// or a metadata name (Outer+Inner). References, including <see cref> in doc comments, are renamed in
// every project of the solution. Plain text in comments and strings is left alone unless asked for.
//
// Options:
//   --overloads    rename every overload of a method (also lets an overloaded name resolve)
//   --comments     also rename the name where it appears as plain text in comments
//   --strings      also rename the name inside string literals
//   --rename-file  rename a type's file when it is named after the type
//   --dry-run      print the files that would change and write nothing
//   --allow-errors rename even though a project has compile errors (references there may be missed)
//
// The solution must be restored first. MSBuildLocator picks the newest installed .NET SDK.
//
// Exit codes: 0 renamed (or dry run), 1 load/resolve/apply failure, 2 bad arguments.
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace RenameSymbol;

internal static class Program
{
    internal static readonly string[] Options = ["--overloads", "--comments", "--strings", "--rename-file", "--dry-run", "--allow-errors"];

    private static async Task<int> Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var unknown = flags.Except(Options).ToList();
        if (positional.Length != 3 || unknown.Count > 0)
        {
            foreach (var f in unknown)
                Console.Error.WriteLine($"unknown option {f}");
            Console.Error.WriteLine("usage: rename_symbol <solution> <Type|Type.Member> <NewName> [" + string.Join("] [", Options) + "]");
            return 2;
        }

        // Must run before any method that touches an MSBuild or MSBuildWorkspace type is JIT-compiled,
        // which is why the work lives in Tool and is not inlined here.
        MSBuildLocator.RegisterDefaults();
        return await Run(positional, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> Run(string[] positional, HashSet<string> flags) =>
        Tool.RunAsync(Path.GetFullPath(positional[0]), positional[1], positional[2], flags);
}
