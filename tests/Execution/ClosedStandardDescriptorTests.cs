using System.Diagnostics;
using System.Text;

namespace Revare.Tests.Execution;

/// <summary>
/// A built program started with descriptor 0, 1 or 2 closed. On Linux and macOS <c>open</c> hands out the lowest free
/// descriptor, so without the program start's check the first file a program opens would become its standard output
/// or error. The tests build a small program, start it through a shell that closes the descriptor, and check that
/// what it printed did not land in the file it had open. POSIX only: they return at once on Windows.
/// </summary>
public sealed class ClosedStandardDescriptorTests
{
    /// <summary>Opens a file, writes "file", prints on standard output and error, then closes the file.</summary>
    private const string Program = """
                                   module ClosedDescriptorProbe
                                   import IO/Console
                                   import IO/File

                                   routine start()
                                       var target = File("OUT_PATH")
                                       drop FileManager.make_file(at: target)
                                       using FileManager.open_write(at: target) as sink
                                           drop sink.push(data: "file".encode_as_utf8())
                                           show("console")
                                           alert("alert")
                                       return
                                   """;

    [Theory]
    [InlineData(">&-")]
    [InlineData("2>&-")]
    [InlineData("<&-")]
    [InlineData("<&- >&- 2>&-")]
    public void ClosedStandardDescriptorDoesNotBecomeTheFile(string redirect)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            return;
        }

        string dir = Path.Combine(path1: Path.GetTempPath(),
            path2: "anv-closed-fd-" + Guid.NewGuid().ToString(format: "N"));
        Directory.CreateDirectory(path: dir);
        try
        {
            string output = Path.Combine(path1: dir, path2: "out.txt");
            string source = Path.Combine(path1: dir, path2: "main.rvr");
            File.WriteAllText(path: source, contents: Program.Replace(oldValue: "OUT_PATH", newValue: output));

            (int buildCode, string buildText) = Start(dir: dir, file: "dotnet",
                args: [TestHelpers.ToolchainDll(sourcePath: source), "build", source], noDaemon: true);
            Assert.True(condition: buildCode == 0, userMessage: buildText);

            string exe = Path.Combine(path1: dir, path2: "main.exe");
            if (!File.Exists(path: exe))
            {
                exe = Path.Combine(path1: dir, path2: "main");
            }

            (int code, string text) = Start(dir: dir, file: "/bin/sh", args: ["-c", $"\"{exe}\" {redirect}"],
                noDaemon: false);
            Assert.True(condition: code == 0, userMessage: text);
            Assert.Equal(expected: "file", actual: File.ReadAllText(path: output));
        }
        finally
        {
            try { Directory.Delete(path: dir, recursive: true); }
            catch
            {
                /* best-effort temp cleanup */
            }
        }
    }

    /// <summary>Runs a process to its end and returns its exit code and everything it printed.</summary>
    private static (int Code, string Text) Start(string dir, string file, string[] args, bool noDaemon)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = dir,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(item: arg);
        }

        if (noDaemon)
        {
            psi.Environment[key: "RV_NO_DAEMON"] = "1";
        }

        using var process = Process.Start(startInfo: psi)!;
        var text = new StringBuilder();
        process.OutputDataReceived += (_, e) => { lock (text) { text.AppendLine(value: e.Data); } };
        process.ErrorDataReceived += (_, e) => { lock (text) { text.AppendLine(value: e.Data); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(milliseconds: TestHelpers.ChildTimeoutMs(baseMs: 300_000)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(message: $"{file} did not finish.\n{text}");
        }

        process.WaitForExit();
        return (process.ExitCode, text.ToString());
    }
}
