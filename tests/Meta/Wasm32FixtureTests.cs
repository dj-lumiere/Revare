using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Revare.Tests.Meta;

/// <summary>
/// Builds every <c>tests/Fixtures/Stdlib/*.rvr</c> fixture for WebAssembly under WASI and runs it in a WASI runtime,
/// comparing its output with the fixture's <c>.expected.txt</c>. The target comes from
/// <c>tests/Fixtures/Wasm32/config.toml</c> (<c>[target] triple</c>, <c>backend</c>, <c>runner</c>), each fixture is
/// built on its own with those settings, and a fixture with a <c>&lt;stem&gt;.arch</c> file holding
/// <c>not wasm32</c> is left out (the line's comment says why). The test is in the <c>Wasm32</c> category: CI runs it
/// in a job of its own, and it does nothing when no WASI runtime is there.
/// </summary>
public sealed partial class Wasm32FixtureTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    private static readonly string FixturesDir = Path.Combine(path1: RepoRoot, path2: "tests", path3: "Fixtures",
        path4: "Stdlib");

    private static readonly string ConfigFile = Path.Combine(RepoRoot, "tests", "Fixtures", "Wasm32", "config.toml");

    [GeneratedRegex(pattern: @"^\s*module\s+(\S+)\s*$")]
    private static partial Regex ModuleRe();

    [GeneratedRegex(pattern: @"^\s*runner\s*=\s*""([^""]+)""")]
    private static partial Regex RunnerRe();

    /// <summary>Every fixture that runs on wasm32 gives its expected output there.</summary>
    [Fact]
    [Trait(name: "Category", value: "Wasm32")]
    public void Wasm32_FixturesOutputMatchExpected()
    {
        string config = File.ReadAllText(path: ConfigFile);
        string? runner = config.Split(separator: '\n')
                               .Select(selector: l => RunnerRe().Match(input: l))
                               .FirstOrDefault(predicate: m => m.Success)
                               ?.Groups[groupnum: 1].Value;
        if (!RunnerFound(runner: runner ?? "wasmtime"))
        {
            return;
        }

        // A runner named by a path is a path from this config.toml's folder: each fixture's copy names it whole.
        if (runner != null && (runner.Contains(value: '/') || runner.Contains(value: '\\')))
        {
            string whole = Path.GetFullPath(path: runner, basePath: Path.GetDirectoryName(path: ConfigFile)!);
            config = config.Replace(oldValue: $"\"{runner}\"", newValue: $"\"{whole.Replace(oldValue: "\\", newValue: "/")}\"");
        }

        string[] stems = Directory.GetFiles(path: FixturesDir, searchPattern: "*.rvr")
                                  .Select(selector: f => Path.GetFileNameWithoutExtension(path: f))
                                  .Where(predicate: stem => !LeftOut(stem: stem))
                                  .Order(comparer: StringComparer.Ordinal)
                                  .ToArray();
        Assert.NotEmpty(collection: stems);

        string root = Path.Combine(path1: Path.GetTempPath(), path2: "anv-wasm32-" + Guid.NewGuid().ToString(format: "N"));
        var failures = new ConcurrentBag<string>();
        try
        {
            Parallel.ForEach(source: stems, parallelOptions: new ParallelOptions { MaxDegreeOfParallelism = 3 },
                body: stem =>
                {
                    if (RunFixture(stem: stem, root: root, config: config) is { } failure)
                    {
                        failures.Add(item: failure);
                    }
                });
        }
        finally
        {
            try { Directory.Delete(path: root, recursive: true); }
            catch (IOException) { }
        }

        Assert.True(condition: failures.IsEmpty,
            userMessage: $"{failures.Count} of {stems.Length} fixtures failed on wasm32:\n" +
                         string.Join(separator: "\n\n", values: failures.Order(comparer: StringComparer.Ordinal)));
    }

    /// <summary>Whether the fixture's <c>.arch</c> file leaves it out on wasm32.</summary>
    private static bool LeftOut(string stem)
    {
        string arch = Path.Combine(path1: FixturesDir, path2: stem + ".arch");
        return File.Exists(path: arch) && File.ReadLines(path: arch)
                                              .Select(selector: l => l.Split(separator: '#')[0].Trim())
                                              .Any(predicate: l => l == "not wasm32");
    }

    /// <summary>Builds and runs one fixture for wasm32. Returns null when its output is the expected one, else what
    /// went wrong.</summary>
    private static string? RunFixture(string stem, string root, string config)
    {
        string source = Path.Combine(path1: FixturesDir, path2: stem + ".rvr");
        string? module = File.ReadLines(path: source)
                             .Select(selector: l => ModuleRe().Match(input: l))
                             .FirstOrDefault(predicate: m => m.Success)
                             ?.Groups[groupnum: 1].Value;
        if (module == null)
        {
            return $"{stem}: no module line";
        }

        string dir = Path.Combine(path1: root, path2: stem);
        Directory.CreateDirectory(path: dir);
        string fixtures = FixturesDir.Replace(oldValue: "\\", newValue: "/");
        File.WriteAllText(path: Path.Combine(path1: dir, path2: "config.toml"),
            contents: config.Replace(oldValue: "[target]",
                newValue: $"[target]\nexecutable = \"main.rvr\"\nmodule_paths = [\"{fixtures}\"]"));
        string leaf = module[(module.LastIndexOf(value: '/') + 1)..];
        File.WriteAllText(path: Path.Combine(path1: dir, path2: "main.rvr"),
            contents: $"module Wasm32Run\n\nimport IO/Console\nimport {module}\n\nroutine start()\n    {leaf}.start()\n    return\n");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestHelpers.ToolchainDll(sourcePath: source), "run", Path.Combine(path1: dir, path2: "main.rvr") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
            Environment =
            {
                [key: "DOTNET_gcServer"] = "0", [key: "DOTNET_GCConserveMemory"] = "9",
                [key: "RV_FLUSH_STDOUT"] = "1", [key: "RV_NO_DAEMON"] = "1"
            }
        };
        using Process p = Process.Start(startInfo: psi)!;
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(milliseconds: TestHelpers.ChildTimeoutMs(baseMs: 300_000)))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }

            return $"{stem}: timed out";
        }

        string expected = Normalize(text: File.ReadAllText(path: Path.Combine(path1: FixturesDir,
            path2: stem + ".expected.txt")));
        string actual = Normalize(text: stdout.Result);
        return p.ExitCode == 0 && actual == expected
            ? null
            : $"{stem}: exit {p.ExitCode}\n--- expected ---\n{expected}\n--- actual ---\n{actual}\n--- stderr ---\n" +
              stderr.Result.Trim();
    }

    private static string Normalize(string text) =>
        string.Join(separator: "\n", values: text.Replace(oldValue: "\r\n", newValue: "\n")
                                                 .TrimEnd(trimChar: '\n')
                                                 .Split(separator: '\n')
                                                 .Select(selector: l => l.TrimEnd()));

    /// <summary>Whether the WASI runtime is there: a path that exists, or a name on PATH.</summary>
    private static bool RunnerFound(string runner)
    {
        if (runner.Contains(value: '/') || runner.Contains(value: '\\'))
        {
            return File.Exists(path: Path.GetFullPath(path: runner, basePath: Path.GetDirectoryName(path: ConfigFile)!));
        }

        return (Environment.GetEnvironmentVariable(variable: "PATH") ?? "")
               .Split(separator: Path.PathSeparator)
               .Any(predicate: d => File.Exists(path: Path.Combine(path1: d, path2: runner)) ||
                                    File.Exists(path: Path.Combine(path1: d, path2: runner + ".exe")));
    }

    private static string LocateRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(path: Path.Combine(path1: dir, path2: "Revare.csproj")))
        {
            dir = Path.GetDirectoryName(path: dir);
        }

        return dir ?? throw new InvalidOperationException(message: "Could not find the Revare project folder.");
    }
}
