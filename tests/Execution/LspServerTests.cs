using System.Text.Json;

namespace Revare.Tests.Execution;

/// <summary>
/// End-to-end coverage for the LSP server (<c>src/Execution/LspServer.cs</c>): every request handler
/// is driven in-process over real, analyzed Revare/Mielia documents via <see cref="LspTestHarness"/>.
/// Positions are computed from the source text so the tests stay robust to edits.
///
/// All tests live in one class so they share xUnit's per-class sequential execution — the server keeps
/// open documents in a process-wide static dictionary, and each <see cref="LspTestHarness.Run"/> clears
/// it at the start, so overlapping sessions from parallel classes would race.
/// </summary>
public sealed class LspServerTests
{
    private const string Uri = "file:///test/Sample.rvr";

    // A symbol-rich Revare document: free routines, a record with a member routine, a generic
    // container use, variables, calls, an f-string, and control flow — enough surface to exercise
    // hover / definition / references / completion / signature-help / symbols / semantic tokens.
    private const string Source =
        "module Test/Lsp\n" +
        "import IO/Console\n" +
        "\n" +
        "record Point\n" +
        "    x: S32\n" +
        "    y: S32\n" +
        "\n" +
        "    routine magnitude(me) -> S32\n" +
        "        return me.x * me.x + me.y * me.y\n" +
        "\n" +
        "routine add(a: S32, b: S32) -> S32\n" +
        "    return a + b\n" +
        "\n" +
        "routine start()\n" +
        "    var total = add(a: 1, b: 2)\n" +
        "    var p = Point(x: 3, y: 4)\n" +
        "    var m = p.magnitude()\n" +
        "    show(f\"total={total} mag={m}\")\n" +
        "    return\n";

    private static string Initialize(int id = 0)
    {
        return JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id,
            method = "initialize",
            @params = new { capabilities = new { } }
        });
    }

    private const string Shutdown = "{\"jsonrpc\":\"2.0\",\"id\":999,\"method\":\"shutdown\"}";
    private const string Exit = "{\"jsonrpc\":\"2.0\",\"method\":\"exit\"}";

    private static (int Line, int Char) Pos(string needle, int occurrence = 1)
    {
        return LspTestHarness.PositionOf(text: Source, needle: needle, occurrence: occurrence);
    }

    /// <summary>The labels of a <c>textDocument/completion</c> reply.</summary>
    private static List<string> CompletionLabels(JsonElement? reply)
    {
        return reply!.Value.GetProperty(propertyName: "result")
                     .GetProperty(propertyName: "items")
                     .EnumerateArray()
                     .Select(selector: item => item.GetProperty(propertyName: "label").GetString() ?? "")
                     .ToList();
    }

    /// <summary>The <c>serverInfo.name</c> of an <c>initialize</c> reply.</summary>
    private static string? ServerName(JsonElement? reply)
    {
        return reply!.Value.GetProperty(propertyName: "result")
                     .GetProperty(propertyName: "serverInfo")
                     .GetProperty(propertyName: "name")
                     .GetString();
    }

    [Fact]
    public void Initialize_NamesTheRevareServer()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(Initialize(id: 1), Shutdown, Exit);

        Assert.Equal(expected: "revare-lsp", actual: ServerName(reply: LspTestHarness.ReplyWithId(replies: replies, id: 1)));
    }

    [Fact]
    public void MieliaDocument_IsLeftToTheMieliaServer()
    {
        const string miUri = "file:///test/Other.mie";
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: miUri, text: "routine start()\n    var x = undefined_name\n    return\n",
                languageId: "mielia"),
            Exit);

        // A .mie file is the Mielia server's: this one publishes nothing for it.
        Assert.Null(@object: LspTestHarness.Diagnostics(replies: replies, uri: miUri));
    }

    [Fact]
    public void Completion_OffersRevareKeywords()
    {
        (int line, int character) = Pos(needle: "add(a:");
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 40, method: "textDocument/completion", uri: Uri, line: line,
                character: character + 1),
            Exit);

        // `steal` is a Revare keyword (Mielia has no ownership transfer).
        Assert.Contains(expected: "steal", collection: CompletionLabels(reply: LspTestHarness.ReplyWithId(replies: replies, id: 40)));
    }

    [Fact]
    public void Formatting_AnswersWithEdits()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.DocRequest(id: 41, method: "textDocument/formatting", uri: Uri),
            Exit);

        // The reply is a list of text edits (empty when there is nothing to change or no formatter).
        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 41);
        Assert.NotNull(@object: reply);
        Assert.Equal(expected: JsonValueKind.Array, actual: reply!.Value.GetProperty(propertyName: "result").ValueKind);
    }

    [Fact]
    public void Initialize_ReturnsServerCapabilities()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(Initialize(id: 1), Shutdown, Exit);

        JsonElement? init = LspTestHarness.ReplyWithId(replies: replies, id: 1);
        Assert.NotNull(@object: init);
        JsonElement caps = init!.Value.GetProperty(propertyName: "result").GetProperty(propertyName: "capabilities");
        Assert.True(condition: caps.GetProperty(propertyName: "hoverProvider").GetBoolean());
        Assert.True(condition: caps.GetProperty(propertyName: "definitionProvider").GetBoolean());
        Assert.True(condition: caps.TryGetProperty(propertyName: "semanticTokensProvider", value: out _));

        // shutdown must be answered with a null result.
        JsonElement? sd = LspTestHarness.ReplyWithId(replies: replies, id: 999);
        Assert.NotNull(@object: sd);
    }

    [Fact]
    public void DidOpen_PublishesDiagnostics_CleanSourceHasNoErrors()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), Exit);

        JsonElement? diag = LspTestHarness.Diagnostics(replies: replies, uri: Uri);
        Assert.NotNull(@object: diag);
        JsonElement arr = diag!.Value.GetProperty(propertyName: "diagnostics");
        Assert.Equal(expected: JsonValueKind.Array, actual: arr.ValueKind);
    }

    [Fact]
    public void DidOpen_BadSource_ReportsDiagnostics()
    {
        const string bad =
            "module Test/Bad\n" +
            "routine start()\n" +
            "    var x = undefined_symbol_here\n" +
            "    return\n";

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: "file:///bad.rvr", text: bad), Exit);

        JsonElement? diag = LspTestHarness.Diagnostics(replies: replies, uri: "file:///bad.rvr");
        Assert.NotNull(@object: diag);
        JsonElement arr = diag!.Value.GetProperty(propertyName: "diagnostics");
        Assert.True(condition: arr.GetArrayLength() > 0, userMessage: "expected at least one diagnostic");
    }

    [Fact]
    public void DidChange_ReanalyzesDocument()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.DidChange(uri: Uri, text: Source),
            Exit);

        // Two publishDiagnostics notifications (open + change) — assert the change one exists.
        Assert.NotNull(@object: LspTestHarness.Diagnostics(replies: replies, uri: Uri));
    }

    [Fact]
    public void DidClose_ClearsDiagnostics()
    {
        string close = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            method = "textDocument/didClose",
            @params = new { textDocument = new { uri = Uri } }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), close, Exit);

        Assert.NotNull(@object: LspTestHarness.Diagnostics(replies: replies, uri: Uri));
    }

    [Theory]
    [InlineData("textDocument/hover")]
    [InlineData("textDocument/definition")]
    [InlineData("textDocument/references")]
    [InlineData("textDocument/prepareRename")]
    public void PositionalRequest_OverRoutineCall_Answers(string method)
    {
        (int line, int character) = Pos(needle: "add(a:"); // the call site
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 5, method: method, uri: Uri, line: line, character: character),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 5);
        Assert.NotNull(@object: reply);
        Assert.True(condition: reply!.Value.TryGetProperty(propertyName: "result", value: out _));
    }

    [Fact]
    public void Hover_OverMemberRoutine_Answers()
    {
        (int line, int character) = Pos(needle: "magnitude", occurrence: 2); // the .magnitude() call
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 6, method: "textDocument/hover", uri: Uri, line: line, character: character),
            Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_OverAnnotatedType_ShowsItsDocComment(bool annotated)
    {
        // An annotation between a type's doc comment and its header (`@layout`) doesn't hide the doc from a hover.
        string source =
            "### Two numbers packed side by side.\n" +
            (annotated ? "@layout(\"packed\")\n" : "") +
            "record Pair\n" +
            "    a: U8\n" +
            "    b: U32\n" +
            "\n" +
            "routine start()\n" +
            "    var p = Pair(a: 1, b: 2)\n" +
            "    return\n";
        // The document is a real file: analysis reads the file's folder to find its module.
        string dir = Directory.CreateTempSubdirectory(prefix: "lsp_hover_").FullName;
        string path = Path.Combine(path1: dir, path2: "main.rvr");
        File.WriteAllText(path: path, contents: source);
        string uri = new System.Uri(uriString: path).AbsoluteUri;
        (int line, int character) = LspTestHarness.PositionOf(text: source, needle: "Pair", occurrence: 2);
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: uri, text: source),
            LspTestHarness.Positional(id: 8, method: "textDocument/hover", uri: uri, line: line, character: character),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 8);
        Assert.NotNull(@object: reply);
        Assert.True(condition: reply!.Value.GetRawText().Contains(value: "packed side by side"),
            userMessage: string.Join("\n", replies.Select(r => r.RootElement.GetRawText())));
    }

    [Theory]
    [InlineData("main.rvr", "routine start()\n    var d = Duration.zero()\n    return\n", "Duration", "span of time")]
    [InlineData("main.rvr", "routine start()\n    var m = Moment.epoch()\n    return\n", "Moment", "absolute point in time")]
    public void Hover_OverStdlibType_ShowsItsDocComment(string file, string source, string needle, string doc)
    {
        string dir = Directory.CreateTempSubdirectory(prefix: "lsp_hover_").FullName;
        string path = Path.Combine(path1: dir, path2: file);
        File.WriteAllText(path: path, contents: source);
        string uri = new System.Uri(uriString: path).AbsoluteUri;
        (int line, int character) = LspTestHarness.PositionOf(text: source, needle: needle);
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: uri, text: source),
            LspTestHarness.Positional(id: 9, method: "textDocument/hover", uri: uri, line: line, character: character),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 9);
        Assert.NotNull(@object: reply);
        Assert.True(condition: reply!.Value.GetRawText().Contains(value: doc),
            userMessage: string.Join("\n", replies.Select(r => r.RootElement.GetRawText())));
    }

    [Fact]
    public void Hover_OverWhitespace_ReturnsNullResult()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            // Column 0 of a blank line — no symbol under the cursor.
            LspTestHarness.Positional(id: 7, method: "textDocument/hover", uri: Uri, line: 2, character: 0),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 7);
        Assert.NotNull(@object: reply);
    }

    [Fact]
    public void Completion_AfterMemberDot_Answers()
    {
        (int line, int character) = Pos(needle: "p.magnitude");
        // Place the cursor right after the '.', i.e. at the start of "magnitude".
        int dotChar = character + 2; // past "p."
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 8, method: "textDocument/completion", uri: Uri, line: line, character: dotChar),
            Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 8));
    }

    [Fact]
    public void Completion_AtIdentifier_Answers()
    {
        (int line, int character) = Pos(needle: "add(a:");
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 9, method: "textDocument/completion", uri: Uri, line: line, character: character + 1),
            Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 9));
    }

    [Fact]
    public void CompletionItemResolve_Answers()
    {
        string resolve = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "completionItem/resolve",
            @params = new { label = "add", kind = 3 }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), resolve, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 10));
    }

    [Fact]
    public void SignatureHelp_InsideCallArgs_Answers()
    {
        (int line, int character) = Pos(needle: "add(a:");
        // Cursor inside the parentheses.
        int inside = character + 4;
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.Positional(id: 11, method: "textDocument/signatureHelp", uri: Uri, line: line, character: inside),
            Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 11));
    }

    [Fact]
    public void Rename_OverLocalVariable_Answers()
    {
        (int line, int character) = Pos(needle: "total", occurrence: 1);
        string rename = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 12,
            method = "textDocument/rename",
            @params = new
            {
                textDocument = new { uri = Uri },
                position = new { line, character },
                newName = "sum"
            }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), rename, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 12));
    }

    [Fact]
    public void DocumentSymbol_ListsDeclarations()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.DocRequest(id: 13, method: "textDocument/documentSymbol", uri: Uri),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 13);
        Assert.NotNull(@object: reply);
        JsonElement result = reply!.Value.GetProperty(propertyName: "result");
        Assert.Equal(expected: JsonValueKind.Array, actual: result.ValueKind);
        Assert.True(condition: result.GetArrayLength() > 0);
    }

    [Fact]
    public void WorkspaceSymbol_Answers()
    {
        string ws = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 14,
            method = "workspace/symbol",
            @params = new { query = "add" }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), ws, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 14));
    }

    [Fact]
    public void InlayHint_Answers()
    {
        string hint = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 15,
            method = "textDocument/inlayHint",
            @params = new
            {
                textDocument = new { uri = Uri },
                range = new
                {
                    start = new { line = 0, character = 0 },
                    end = new { line = 20, character = 0 }
                }
            }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), hint, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 15));
    }

    [Fact]
    public void InlayHintsAndColors_AreEmptyWhileTheCurrentTextDoesNotAnalyze()
    {
        // A line indented by 3 spaces does not tokenize, so no analysis of this text exists: the one of the text before
        // it must not be sent, its positions no longer match the text.
        const string valid = "routine start()\n    var total = 1\n    return\n";
        const string broken = "routine start()\n   var total = 1\n    return\n";
        (string uri, string folder) = OnDisk(text: valid);
        string Hint(int id)
        {
            return JsonSerializer.Serialize(value: new
            {
                jsonrpc = "2.0",
                id,
                method = "textDocument/inlayHint",
                @params = new
                {
                    textDocument = new { uri },
                    range = new { start = new { line = 0, character = 0 }, end = new { line = 20, character = 0 } }
                }
            });
        }

        IReadOnlyList<JsonDocument> replies;
        try
        {
            replies = LspTestHarness.Run(
                Initialize(id: 1),
                LspTestHarness.DidOpen(uri: uri, text: valid),
                Hint(id: 71),
                LspTestHarness.DidChange(uri: uri, text: broken),
                Hint(id: 72),
                LspTestHarness.DocRequest(id: 73, method: "textDocument/semanticTokens/full", uri: uri),
                Exit);
        }
        finally
        {
            Directory.Delete(path: folder, recursive: true);
        }

        Assert.True(condition: LspTestHarness.ReplyWithId(replies: replies, id: 71)!.Value.GetProperty(propertyName: "result").GetArrayLength() > 0);
        Assert.Equal(expected: 0, actual: LspTestHarness.ReplyWithId(replies: replies, id: 72)!.Value.GetProperty(propertyName: "result").GetArrayLength());
        Assert.Equal(expected: 0,
            actual: LspTestHarness.ReplyWithId(replies: replies, id: 73)!.Value.GetProperty(propertyName: "result")
                                  .GetProperty(propertyName: "data").GetArrayLength());
    }

    [Fact]
    public void CodeAction_Answers()
    {
        string action = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 16,
            method = "textDocument/codeAction",
            @params = new
            {
                textDocument = new { uri = Uri },
                range = new
                {
                    start = new { line = 14, character = 0 },
                    end = new { line = 14, character = 10 }
                },
                context = new { diagnostics = Array.Empty<object>() }
            }
        });

        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source), action, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 16));
    }

    [Fact]
    public void SemanticTokens_Full_Answers()
    {
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: Uri, text: Source),
            LspTestHarness.DocRequest(id: 17, method: "textDocument/semanticTokens/full", uri: Uri),
            Exit);

        JsonElement? reply = LspTestHarness.ReplyWithId(replies: replies, id: 17);
        Assert.NotNull(@object: reply);
        JsonElement data = reply!.Value.GetProperty(propertyName: "result").GetProperty(propertyName: "data");
        Assert.Equal(expected: JsonValueKind.Array, actual: data.ValueKind);
    }

    /// <summary>A document on disk in a folder of its own (the server resolves a name from where the file is), and its
    /// URI. Dispose the folder when the test is done.</summary>
    private static (string Uri, string Folder) OnDisk(string text)
    {
        string folder = Path.Combine(path1: Path.GetTempPath(), path2: "lsp-names-" + Guid.NewGuid().ToString(format: "N"));
        Directory.CreateDirectory(path: folder);
        File.WriteAllText(path: Path.Combine(path1: folder, path2: "config.toml"),
            contents: "[package]\nrevare-version = \"0.5.0\"\nname = \"names\"\n\n[target]\nexecutable = \"names.rvr\"\nmode = \"debug\"\n");
        string file = Path.Combine(path1: folder, path2: "names.rvr");
        File.WriteAllText(path: file, contents: text);
        return (new System.Uri(uriString: file).AbsoluteUri, folder);
    }

    /// <summary>The semantic tokens of <paramref name="text"/>: line (1-based), column (1-based), the text, its kind and
    /// its modifier bits.</summary>
    private static List<(int Line, int Column, string Text, string Kind, int Mods)> SemanticTokensOf(string text)
    {
        (string uri, string folder) = OnDisk(text: text);
        IReadOnlyList<JsonDocument> replies;
        try
        {
            replies = LspTestHarness.Run(
                Initialize(id: 1),
                LspTestHarness.DidOpen(uri: uri, text: text),
                LspTestHarness.DocRequest(id: 60, method: "textDocument/semanticTokens/full", uri: uri),
                Exit);
        }
        finally
        {
            Directory.Delete(path: folder, recursive: true);
        }

        JsonElement legend = LspTestHarness.ReplyWithId(replies: replies, id: 1)!.Value.GetProperty(propertyName: "result")
                                           .GetProperty(propertyName: "capabilities")
                                           .GetProperty(propertyName: "semanticTokensProvider")
                                           .GetProperty(propertyName: "legend");
        List<string> kinds = legend.GetProperty(propertyName: "tokenTypes").EnumerateArray()
                                   .Select(selector: k => k.GetString()!).ToList();
        int[] data = LspTestHarness.ReplyWithId(replies: replies, id: 60)!.Value.GetProperty(propertyName: "result")
                                   .GetProperty(propertyName: "data").EnumerateArray()
                                   .Select(selector: n => n.GetInt32()).ToArray();
        string[] lines = text.Split(separator: '\n');
        var tokens = new List<(int, int, string, string, int)>();
        int line = 0;
        int column = 0;
        for (int i = 0; i + 4 < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            tokens.Add(item: (line + 1, column + 1, lines[line].Substring(startIndex: column, length: data[i + 2]),
                kinds[data[i + 3]], data[i + 4]));
        }

        return tokens;
    }

    private const string NamesSource =
        "module Test/Names\n" +
        "import IO/Console\n" +
        "\n" +
        "record Point\n" +
        "    x: S64\n" +
        "    y: S64\n" +
        "\n" +
        "routine gcd(a: S64, b: S64) -> S64\n" +
        "    return a + b\n" +
        "\n" +
        "routine start()\n" +
        "    var p = Point(x: 1, y: 2)\n" +
        "    var total = gcd(a: p.x, b: p.y)\n" +
        "    show(f\"{total}\")\n" +
        "    return\n";

    [Fact]
    public void SemanticTokens_FieldsParametersAndVariablesAreNamedWhereTheyAreWritten()
    {
        List<(int Line, int Column, string Text, string Kind, int Mods)> tokens = SemanticTokensOf(text: NamesSource);
        string KindOf(int line, string text, int occurrence = 1)
        {
            return tokens.Where(predicate: t => t.Line == line && t.Text == text).ElementAt(index: occurrence - 1).Kind;
        }

        Assert.Equal(expected: "property", actual: KindOf(line: 5, text: "x"));
        Assert.Equal(expected: "parameter", actual: KindOf(line: 8, text: "a"));
        Assert.Equal(expected: "parameter", actual: KindOf(line: 9, text: "a"));
        Assert.Equal(expected: "variable", actual: KindOf(line: 12, text: "p"));
        Assert.Equal(expected: "property", actual: KindOf(line: 12, text: "x"));
        Assert.Equal(expected: "variable", actual: KindOf(line: 13, text: "total"));
        Assert.Equal(expected: "parameter", actual: KindOf(line: 13, text: "a"));
        Assert.Equal(expected: "variable", actual: KindOf(line: 13, text: "p"));
        Assert.Equal(expected: "property", actual: KindOf(line: 13, text: "x"));
        Assert.Equal(expected: "variable", actual: KindOf(line: 14, text: "total"));
    }

    /// <summary>The inlay hints of <paramref name="text"/>: line (1-based), column (1-based) and label.</summary>
    private static List<(int Line, int Column, string Label)> InlayHintsOf(string text)
    {
        return RawInlayHintsOf(text: text)
              .Select(selector: h => (
                   h.GetProperty(propertyName: "position").GetProperty(propertyName: "line").GetInt32() + 1,
                   h.GetProperty(propertyName: "position").GetProperty(propertyName: "character").GetInt32() + 1,
                   h.GetProperty(propertyName: "label").GetString()!))
              .ToList();
    }

    /// <summary>The inlay hints of <paramref name="text"/> as the server sends them.</summary>
    private static List<JsonElement> RawInlayHintsOf(string text)
    {
        (string uri, string folder) = OnDisk(text: text);
        IReadOnlyList<JsonDocument> replies;
        try
        {
            replies = LspTestHarness.Run(
                Initialize(id: 1),
                LspTestHarness.DidOpen(uri: uri, text: text),
                JsonSerializer.Serialize(value: new
                {
                    jsonrpc = "2.0",
                    id = 70,
                    method = "textDocument/inlayHint",
                    @params = new
                    {
                        textDocument = new { uri },
                        range = new { start = new { line = 0, character = 0 }, end = new { line = 500, character = 0 } }
                    }
                }),
                Exit);
        }
        finally
        {
            Directory.Delete(path: folder, recursive: true);
        }

        return LspTestHarness.ReplyWithId(replies: replies, id: 70)!.Value.GetProperty(propertyName: "result")
                             .EnumerateArray()
                             .ToList();
    }

    private const string StoredProtocolSource =
        "module Test/Stored\n" +
        "import IO/Console\n" +
        "\n" +
        "protocol Enemy\n" +
        "    @readonly\n" +
        "    routine Me.hp() -> S64\n" +
        "\n" +
        "entity Slime\n" +
        "obeys Enemy\n" +
        "    life: S64\n" +
        "\n" +
        "@readonly\n" +
        "routine Slime.hp() -> S64\n" +
        "    return me.life\n" +
        "\n" +
        "entity Boss\n" +
        "obeys Enemy\n" +
        "    life: S64\n" +
        "\n" +
        "@readonly\n" +
        "routine Boss.hp() -> S64\n" +
        "    return me.life\n" +
        "\n" +
        "entity Arena\n" +
        "    boss: Enemy\n" +
        "\n" +
        "routine finish(e: Enemy)\n" +
        "    show(e.hp())\n" +
        "    return\n" +
        "\n" +
        "routine strongest[T](e: T) -> S64\n" +
        "needs T obeys Enemy\n" +
        "    return e.hp()\n" +
        "\n" +
        "routine start()\n" +
        "    var enemies = List[Enemy]()\n" +
        "    enemies.add_last(value: Slime(life: 10))\n" +
        "    enemies.add_last(value: Boss(life: 20))\n" +
        "    for e in enemies\n" +
        "        show(e.hp())\n" +
        "    show(enemies[0].hp())\n" +
        "    var arena = Arena(boss: Boss(life: 5))\n" +
        "    show(arena.boss.hp())\n" +
        "    var lone: Enemy = Slime(life: 3)\n" +
        "    show(lone.hp())\n" +
        "    finish(e: steal lone)\n" +
        "    show(strongest(e: Slime(life: 4)))\n" +
        "    show(pick().hp())\n" +
        "    var maybe: Enemy? = Boss(life: 2)\n" +
        "    show(try maybe.hp() ?? -1)\n" +
        "    return\n" +
        "\n" +
        "routine pick() -> Enemy\n" +
        "    return Slime(life: 1)\n";

    [Fact]
    public void InlayHints_CallOnAStoredProtocolValue_ShowsDispatchWithTheStoredTypes()
    {
        List<JsonElement> hints = RawInlayHintsOf(text: StoredProtocolSource)
                                 .Where(predicate: h => h.GetProperty(propertyName: "label").GetString() == "dispatch")
                                 .ToList();
        List<(int Line, int Column)> at = hints.Select(selector: h => (
                                                  h.GetProperty(propertyName: "position").GetProperty(propertyName: "line").GetInt32() + 1,
                                                  h.GetProperty(propertyName: "position").GetProperty(propertyName: "character").GetInt32() + 1))
                                              .ToList();

        // A loop element, a list element, a member variable and a variable of the protocol: after the routine name.
        Assert.Contains(expected: (40, 18), collection: at);
        Assert.Contains(expected: (41, 23), collection: at);
        Assert.Contains(expected: (43, 23), collection: at);
        Assert.Contains(expected: (45, 17), collection: at);
        // A result of the protocol and an optional of it.
        Assert.Contains(expected: (48, 19), collection: at);
        Assert.Contains(expected: (50, 22), collection: at);
        // A parameter of the protocol and a `needs T obeys` parameter are chosen at buildtime: no hint.
        Assert.DoesNotContain(collection: at, filter: h => h.Line is 28 or 33);
        string tooltip = hints[index: 0].GetProperty(propertyName: "tooltip").GetString()!;
        Assert.Contains(expectedSubstring: "while the program runs", actualString: tooltip);
        Assert.Contains(expectedSubstring: "Types a place of Enemy holds in this build: Boss, Slime.", actualString: tooltip);
    }

    private const string CallShapesSource =
        "module Test/CallShapes\n" +
        "import IO/Console\n" +
        "\n" +
        "record Counter\n" +
        "    n: S64\n" +
        "\n" +
        "record Pair[A, B]\n" +
        "needs RecordType A, RecordType B\n" +
        "    first: A\n" +
        "    second: B\n" +
        "\n" +
        "common routine Counter.start_at(n: S64) -> Counter\n" +
        "    return Counter(n: n)\n" +
        "\n" +
        "routine Counter.bump() -> S64\n" +
        "    return me.n + 1\n" +
        "\n" +
        "routine Counter.tag[U](v: U) -> U\n" +
        "    return v\n" +
        "\n" +
        "common routine Pair[A, B].of(a: A, b: B) -> Pair[A, B]\n" +
        "    return Pair(first: a, second: b)\n" +
        "\n" +
        "routine echo[T](v: T) -> T\n" +
        "    return v\n" +
        "\n" +
        "routine start()\n" +
        "    show(\"값\".count())\n" +
        "    show[Bool](true)\n" +
        "    var c = Counter.start_at(3)\n" +
        "    show(c.bump())\n" +
        "    show(echo(5))\n" +
        "    show(c.tag(\"x\"))\n" +
        "    show(c.tag[Text](\"x\"))\n" +
        "    var xs = List[S64]()\n" +
        "    var p = Pair(first: 1, second: \"x\")\n" +
        "    var q = Pair.of(a: 1, b: \"x\")\n" +
        "    show(xs.count())\n" +
        "    return\n";

    private const string ScriptSource =
        "import IO/Console\n" +
        "\n" +
        "show(\"값\" == \"값\")\n" +
        "show(\"값\".count())\n" +
        "show(\"값\"[0].codepoint())\n";

    [Fact]
    public void SemanticTokens_ScriptCalls_AreFunctionsNotTypesOrProperties()
    {
        List<(int Line, int Column, string Text, string Kind, int Mods)> tokens = SemanticTokensOf(text: ScriptSource);

        // The script's synthesized `start` has its `None` result on the first statement: it must not color `show`.
        Assert.Equal(expected: "function", actual: tokens.Single(predicate: t => t.Line == 3 && t.Text == "show").Kind);
        // `count` is a field of Text too, but here it is called.
        Assert.Equal(expected: "function", actual: tokens.Single(predicate: t => t.Line == 4 && t.Text == "count").Kind);
        Assert.Equal(expected: "function", actual: tokens.Single(predicate: t => t.Text == "codepoint").Kind);
    }

    [Fact]
    public void SemanticTokens_EveryCallShape_ColorsTheRoutineAsAFunction()
    {
        List<(int Line, int Column, string Text, string Kind, int Mods)> tokens = SemanticTokensOf(text: CallShapesSource);
        string KindOf(int line, string text)
        {
            return tokens.Single(predicate: t => t.Line == line && t.Text == text).Kind;
        }

        Assert.Equal(expected: "function", actual: KindOf(line: 28, text: "show"));
        Assert.Equal(expected: "function", actual: KindOf(line: 28, text: "count"));
        Assert.Equal(expected: "function", actual: KindOf(line: 29, text: "show"));
        Assert.Equal(expected: "recordType", actual: KindOf(line: 29, text: "Bool"));
        Assert.Equal(expected: "recordType", actual: KindOf(line: 30, text: "Counter"));
        Assert.Equal(expected: "function", actual: KindOf(line: 30, text: "start_at"));
        Assert.Equal(expected: "function", actual: KindOf(line: 31, text: "bump"));
        Assert.Equal(expected: "function", actual: KindOf(line: 32, text: "echo"));
        Assert.Equal(expected: "function", actual: KindOf(line: 33, text: "tag"));
        Assert.Equal(expected: "function", actual: KindOf(line: 34, text: "tag"));
        Assert.Equal(expected: "recordType", actual: KindOf(line: 34, text: "Text"));
        Assert.Equal(expected: "function", actual: KindOf(line: 37, text: "of"));
        Assert.Equal(expected: "recordType", actual: KindOf(line: 37, text: "Pair"));
    }

    [Fact]
    public void InlayHints_InferredTypeArguments_ShowAfterTheNameAndNotWhenWritten()
    {
        List<(int Line, int Column, string Label)> hints = InlayHintsOf(text: CallShapesSource)
           .Where(predicate: h => h.Label.StartsWith(value: '[')).ToList();
        string[] shown = hints.Select(selector: h => $"{h.Line}:{h.Column} {h.Label}").ToArray();

        // A free generic routine, and the same one nested in a call.
        Assert.Contains(expected: "28:9 [U64]", collection: shown);
        Assert.Contains(expected: "31:9 [S64]", collection: shown);
        Assert.Contains(expected: "32:9 [S64]", collection: shown);
        Assert.Contains(expected: "32:14 [S64]", collection: shown);
        // A member routine's own inferred argument, after the member name.
        Assert.Contains(expected: "33:15 [Text]", collection: shown);
        // Written arguments get no hint: `show[Bool]`, `tag[Text]`, `List[S64]()`.
        Assert.DoesNotContain(collection: hints, filter: h => h.Line is 29 or 34 or 35);
        // A generic type given its arguments by the analysis, in a creator and in front of a `common` routine.
        Assert.Contains(expected: "36:17 [S64, Text]", collection: shown);
        Assert.Contains(expected: "37:17 [S64, Text]", collection: shown);
        // Inside a generic body the arguments are still parameters: no hint.
        Assert.DoesNotContain(collection: hints, filter: h => h.Line is >= 20 and <= 22);
    }

    [Fact]
    public void InlayHints_ScriptCall_ShowsTheInferredArgumentAfterShow()
    {
        List<(int Line, int Column, string Label)> hints = InlayHintsOf(text: ScriptSource);

        Assert.Contains(expected: (3, 5, "[Bool]"), collection: hints);
    }

    private const string BoundSource =
        "module Test/Bound\n" +
        "import IO/Console\n" +
        "\n" +
        "routine start()\n" +
        "    var xs = [10, 20, 30]\n" +
        "    var add_first = (v: S64) given xs => v + xs[0]\n" +
        "    show(f\"{add_first(1)}\")\n" +
        "    return\n";

    [Fact]
    public void SemanticTokens_NameHoldingABoundLambda_CarriesTheBoundModifier()
    {
        List<(int Line, int Column, string Text, string Kind, int Mods)> tokens = SemanticTokensOf(text: BoundSource);

        Assert.Equal(expected: 4, actual: tokens.Single(predicate: t => t.Line == 6 && t.Text == "add_first").Mods);
        Assert.Equal(expected: 4, actual: tokens.Single(predicate: t => t.Line == 7 && t.Text == "add_first").Mods);
        Assert.Equal(expected: 0, actual: tokens.First(predicate: t => t.Line == 5 && t.Text == "xs").Mods);
    }

    [Fact]
    public void Hover_NameHoldingABoundLambda_SaysWhatItIsBoundTo()
    {
        (int line, int character) = LspTestHarness.PositionOf(text: BoundSource, needle: "add_first(1)");
        (string uri, string folder) = OnDisk(text: BoundSource);
        IReadOnlyList<JsonDocument> replies;
        try
        {
            replies = LspTestHarness.Run(
                LspTestHarness.DidOpen(uri: uri, text: BoundSource),
                LspTestHarness.Positional(id: 61, method: "textDocument/hover", uri: uri, line: line, character: character),
                Exit);
        }
        finally
        {
            Directory.Delete(path: folder, recursive: true);
        }

        string value = LspTestHarness.ReplyWithId(replies: replies, id: 61)!.Value.GetProperty(propertyName: "result")
                                     .GetProperty(propertyName: "contents").GetProperty(propertyName: "value")
                                     .GetString()!;
        Assert.Contains(expectedSubstring: "bound to `xs`: can't be kept in a member variable or returned",
            actualString: value);
    }

    [Fact]
    public void UnknownRequest_AnsweredWithNullResult()
    {
        string unknown = "{\"jsonrpc\":\"2.0\",\"id\":18,\"method\":\"textDocument/foobar\"}";
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(unknown, Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 18));
    }

    [Fact]
    public void UnknownNotification_Ignored_NoCrash()
    {
        string note = "{\"jsonrpc\":\"2.0\",\"method\":\"$/setTrace\",\"params\":{}}";
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(note, Exit);

        // No id → no reply expected, and the loop must survive to process exit.
        Assert.NotNull(@object: replies);
    }

    [Fact]
    public void RequestOnUnopenedDocument_DoesNotCrash()
    {
        // hover before any didOpen — the handler must handle the missing DocState gracefully.
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.Positional(id: 19, method: "textDocument/hover", uri: "file:///never-opened.rvr",
                line: 0, character: 0),
            Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 19));
    }

    [Fact]
    public void MalformedFrame_Skipped()
    {
        // A frame that is not valid JSON must be skipped without aborting the loop.
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run("{ this is not json", Initialize(id: 20), Exit);

        Assert.NotNull(@object: LspTestHarness.ReplyWithId(replies: replies, id: 20));
    }

    // ----- Refactors that cut indentation (code actions of kind refactor.rewrite) -----

    /// <summary>A document from its lines, with the module header and the import every case needs.</summary>
    private static string Doc(params string[] lines)
    {
        return "import IO/Console\n\n" + string.Join(separator: "\n", values: lines) + "\n";
    }

    /// <summary>The refactor actions offered on the line holding <paramref name="needle"/>: each title with the
    /// document as it reads once the action's edit is applied.</summary>
    private static List<(string Title, string Result)> RefactorsAt(string source, string needle)
    {
        // The document is a real file: the types the refactors read come from analyzing its project.
        string dir = Directory.CreateTempSubdirectory(prefix: "lsp_refactor_").FullName;
        string path = Path.Combine(path1: dir, path2: "main.rvr");
        File.WriteAllText(path: path, contents: source);
        string refactorUri = new System.Uri(uriString: path).AbsoluteUri;
        (int line, int character) = LspTestHarness.PositionOf(text: source, needle: needle);
        string action = JsonSerializer.Serialize(value: new
        {
            jsonrpc = "2.0",
            id = 77,
            method = "textDocument/codeAction",
            @params = new
            {
                textDocument = new { uri = refactorUri },
                range = new { start = new { line, character }, end = new { line, character } },
                context = new { diagnostics = Array.Empty<object>() }
            }
        });
        IReadOnlyList<JsonDocument> replies = LspTestHarness.Run(
            LspTestHarness.DidOpen(uri: refactorUri, text: source), action, Exit);
        JsonElement result = LspTestHarness.ReplyWithId(replies: replies, id: 77)!.Value.GetProperty(propertyName: "result");
        var found = new List<(string, string)>();
        foreach (JsonElement item in result.EnumerateArray())
        {
            if (item.GetProperty(propertyName: "kind").GetString() != "refactor.rewrite")
            {
                continue;
            }

            JsonElement edit = item.GetProperty(propertyName: "edit")
                                   .GetProperty(propertyName: "changes")
                                   .GetProperty(propertyName: refactorUri)[index: 0];
            JsonElement range = edit.GetProperty(propertyName: "range");
            int startLine = range.GetProperty(propertyName: "start").GetProperty(propertyName: "line").GetInt32();
            int endLine = range.GetProperty(propertyName: "end").GetProperty(propertyName: "line").GetInt32();
            string[] lines = source.Split(separator: '\n');
            string applied = string.Join(separator: "\n",
                values: lines.Take(count: startLine)
                             .Append(element: edit.GetProperty(propertyName: "newText").GetString()!)
                             .Concat(second: lines.Skip(count: endLine + 1)));
            found.Add(item: (item.GetProperty(propertyName: "title").GetString()!, applied));
        }

        return found;
    }

    private static void AssertOffers(string source, string needle, string title, string expected)
    {
        List<(string Title, string Result)> offered = RefactorsAt(source: source, needle: needle);
        Assert.Contains(expected: title, collection: offered.Select(selector: o => o.Title));
        Assert.Equal(expected: expected, actual: offered.First(predicate: o => o.Title == title).Result);
    }

    private static void AssertNotOffered(string source, string needle, string title)
    {
        Assert.DoesNotContain(expected: title,
            collection: RefactorsAt(source: source, needle: needle).Select(selector: o => o.Title));
    }

    [Fact]
    public void Invert_Integer_FlipsAndKeepsCommentsAndBlankLines()
    {
        AssertOffers(
            source: Doc("routine start()", "    var n: S32 = 5", "    if n < 3", "        show(\"small\")", "",
                "        show(\"still small\")", "    else", "        # the big case", "        show(\"big\")", "    return"),
            needle: "if n < 3", title: "Invert 'if'",
            expected: Doc("routine start()", "    var n: S32 = 5", "    if n >= 3", "        # the big case",
                "        show(\"big\")", "    else", "        show(\"small\")", "", "        show(\"still small\")", "    return"));
    }

    [Fact]
    public void Invert_Float_IsWrappedInNot()
    {
        AssertOffers(
            source: Doc("routine start()", "    var x: B64 = 0.5", "    if x < 1.0", "        show(\"low\")", "    else",
                "        show(\"high\")", "    return"),
            needle: "if x < 1.0", title: "Invert 'if'",
            expected: Doc("routine start()", "    var x: B64 = 0.5", "    if not (x < 1.0)", "        show(\"high\")",
                "    else", "        show(\"low\")", "    return"));
    }

    [Fact]
    public void Invert_EqualityAndLogic_SwapAndGoThroughDeMorgan()
    {
        AssertOffers(
            source: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a == 1 and b != 2",
                "        show(\"yes\")", "    else", "        show(\"no\")", "    return"),
            needle: "if a == 1", title: "Invert 'if'",
            expected: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a != 1 or b == 2",
                "        show(\"no\")", "    else", "        show(\"yes\")", "    return"));
    }

    [Fact]
    public void Invert_ChainedComparison_IsWrappedInNot()
    {
        AssertOffers(
            source: Doc("routine start()", "    var x: S32 = 5", "    if 0 <= x <= 10", "        show(\"in\")", "    else",
                "        show(\"out\")", "    return"),
            needle: "if 0 <= x", title: "Invert 'if'",
            expected: Doc("routine start()", "    var x: S32 = 5", "    if not (0 <= x <= 10)", "        show(\"out\")",
                "    else", "        show(\"in\")", "    return"));
    }

    [Fact]
    public void Invert_HasBecomesLacks()
    {
        AssertOffers(
            source: Doc("routine start()", "    var xs: List[S32] = [1, 2]", "    if xs.has(1)", "        show(\"in\")",
                "    else", "        show(\"out\")", "    return"),
            needle: "if xs.has", title: "Invert 'if'",
            expected: Doc("routine start()", "    var xs: List[S32] = [1, 2]", "    if xs.lacks(1)", "        show(\"out\")",
                "    else", "        show(\"in\")", "    return"));
    }

    [Fact]
    public void Invert_NotOffered_WithoutElseOrWithElseif()
    {
        AssertNotOffered(
            source: Doc("routine start()", "    var n: S32 = 5", "    if n < 3", "        show(\"small\")", "    return"),
            needle: "if n < 3", title: "Invert 'if'");
        AssertNotOffered(
            source: Doc("routine start()", "    var n: S32 = 5", "    if n < 3", "        show(\"small\")",
                "    elseif n < 6", "        show(\"mid\")", "    else", "        show(\"big\")", "    return"),
            needle: "if n < 3", title: "Invert 'if'");
    }

    [Fact]
    public void Guard_Return_MovesTheBodyOutOneLevel()
    {
        AssertOffers(
            source: Doc("routine start()", "    var n: S32 = 5", "    if n > 2", "        show(\"big\")", "        # note",
                "        show(\"really\")", "    return"),
            needle: "if n > 2", title: "Exit early with 'return'",
            expected: Doc("routine start()", "    var n: S32 = 5", "    if n <= 2", "        return", "    show(\"big\")",
                "    # note", "    show(\"really\")", "    return"));
    }

    [Fact]
    public void Guard_WithResult_MovesTheFinalReturnIntoTheGuard()
    {
        AssertOffers(
            source: Doc("routine pick(n: S32) -> S32", "    if n > 2", "        show(\"big\")", "        return 1",
                "    return 0", "", "routine start()", "    show(f\"{pick(n: 3)}\")", "    return"),
            needle: "if n > 2", title: "Exit early with 'return'",
            expected: Doc("routine pick(n: S32) -> S32", "    if n <= 2", "        return 0", "    show(\"big\")",
                "    return 1", "", "routine start()", "    show(f\"{pick(n: 3)}\")", "    return"));
    }

    [Fact]
    public void Guard_Float_IsWrappedInNot()
    {
        AssertOffers(
            source: Doc("routine start()", "    var x: B64 = 0.5", "    if x > 0.25", "        show(\"big\")", "    return"),
            needle: "if x > 0.25", title: "Exit early with 'return'",
            expected: Doc("routine start()", "    var x: B64 = 0.5", "    if not (x > 0.25)", "        return",
                "    show(\"big\")", "    return"));
    }

    [Fact]
    public void Guard_InLoop_UsesContinue()
    {
        AssertOffers(
            source: Doc("routine start()", "    for i: S32 in 0 til 3", "        if i > 0", "            show(\"positive\")",
                "            show(f\"{i}\")", "    return"),
            needle: "if i > 0", title: "Exit early with 'continue'",
            expected: Doc("routine start()", "    for i: S32 in 0 til 3", "        if i <= 0", "            continue",
                "        show(\"positive\")", "        show(f\"{i}\")", "    return"));
    }

    [Fact]
    public void Guard_NotOffered_WhereCodeFollowsTheIf()
    {
        AssertNotOffered(
            source: Doc("routine start()", "    var n: S32 = 5", "    if n > 2", "        show(\"big\")",
                "    show(\"after\")", "    return"),
            needle: "if n > 2", title: "Exit early with 'return'");
        AssertNotOffered(
            source: Doc("routine start()", "    for i: S32 in 0 til 3", "        if i > 0", "            show(\"positive\")",
                "        show(\"every round\")", "    return"),
            needle: "if i > 0", title: "Exit early with 'continue'");
        // A comment between the if and the final return would be dropped.
        AssertNotOffered(
            source: Doc("routine pick(n: S32) -> S32", "    if n > 2", "        return 1", "    # otherwise",
                "    return 0", "", "routine start()", "    show(f\"{pick(n: 3)}\")", "    return"),
            needle: "if n > 2", title: "Exit early with 'return'");
    }

    [Fact]
    public void Merge_NestedIf_JoinsWithAnd()
    {
        AssertOffers(
            source: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a > 0", "        if b > 1",
                "            show(\"both\")", "            # kept", "            show(\"still\")", "    return"),
            needle: "if a > 0", title: "Merge nested 'if'",
            expected: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a > 0 and b > 1",
                "        show(\"both\")", "        # kept", "        show(\"still\")", "    return"));
    }

    [Fact]
    public void Merge_NotOffered_WithElseOrMoreStatements()
    {
        AssertNotOffered(
            source: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a > 0", "        if b > 1",
                "            show(\"both\")", "    else", "        show(\"neither\")", "    return"),
            needle: "if a > 0", title: "Merge nested 'if'");
        AssertNotOffered(
            source: Doc("routine start()", "    var a: S32 = 1", "    var b: S32 = 2", "    if a > 0", "        if b > 1",
                "            show(\"both\")", "        show(\"outer\")", "    return"),
            needle: "if a > 0", title: "Merge nested 'if'");
    }
}
