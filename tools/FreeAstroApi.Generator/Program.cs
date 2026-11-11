// FreeAstroApi SDK generator.
//
//   dotnet run --project tools/FreeAstroApi.Generator -- generate [--spec <path>] [--out <dir>] [--check]
//
// Writes src/FreeAstroApi/Generated/*.g.cs from the vendored spec; `--check` fails when the committed
// files differ from what the spec produces. Fetching and normalising the spec is done by
// scripts/spec_sync.py, which is shared by every FreeAstroAPI SDK.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FreeAstroApi.Generator;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (GeneratorException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: generate [--spec <path>] [--out <dir>] [--check]");
            return 1;
        }

        var root = FindRepositoryRoot();
        switch (args[0])
        {
            case "generate":
            {
                var spec = Option(args, "--spec") ?? Path.Combine(root, "spec", "freeastro.json");
                var outDir = Option(args, "--out") ?? Path.Combine(root, "src", "FreeAstroApi", "Generated");
                var check = args.Contains("--check");
                var result = SdkGenerator.Generate(File.ReadAllText(spec));
                return check ? Check(result, outDir) : Write(result, outDir);
            }
            default:
                throw new GeneratorException($"unknown command '{args[0]}'");
        }
    }

    private static int Write(GenerationResult result, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var stale in Directory.GetFiles(outDir, "*.g.cs"))
        {
            if (!result.Files.ContainsKey(Path.GetFileName(stale))) File.Delete(stale);
        }
        foreach (var (name, content) in result.Files)
        {
            File.WriteAllText(Path.Combine(outDir, name), content);
        }
        Console.WriteLine($"Generated {result.OperationCount} operations, {result.ModelCount} models into {outDir}");
        return 0;
    }

    private static int Check(GenerationResult result, string outDir)
    {
        var problems = new List<string>();
        foreach (var (name, content) in result.Files)
        {
            var path = Path.Combine(outDir, name);
            if (!File.Exists(path) || File.ReadAllText(path) != content) problems.Add(path);
        }
        if (Directory.Exists(outDir))
        {
            problems.AddRange(Directory.GetFiles(outDir, "*.g.cs").Where(f => !result.Files.ContainsKey(Path.GetFileName(f))));
        }
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("Generated code is out of date. Run `scripts/generate.sh`:");
            foreach (var p in problems) Console.Error.WriteLine($"  {p}");
            return 1;
        }
        Console.WriteLine($"Checked {result.OperationCount} operations, {result.ModelCount} models: up to date.");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreeAstroApi.slnx"))) return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}

internal sealed class GeneratorException(string message) : Exception(message);
