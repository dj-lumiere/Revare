using Builder.Execution;
using TypeModel.Enums;

namespace Revare.Tests.Execution;

/// <summary>
/// The language server's doc comment help (<c>src/Execution/LspDocComments.cs</c>): the frame <c>###</c> fills in above
/// a declaration, and the quick fixes that add the lines a doc comment lacks or remove the ones that name nothing.
/// </summary>
public sealed class LspDocCommentTests
{
    private const string File = "/test/Docs.rvr";

    private static string Doc(params string[] lines) => string.Join(separator: "\n", values: lines) + "\n";

    private static readonly string[] Clamp =
    [
        "@readonly",
        "routine clamp[T](value: S64, low: S64) -> S64",
        "    if value < low",
        "        throw IndexOutOfRangeError(index: value)",
        "    return value",
        ""
    ];

    /// <summary>The text after the quick fix called <paramref name="title"/> is applied on the line holding
    /// <paramref name="needle"/>, or null when it is not offered.</summary>
    private static string? Fix(string source, string needle, string title)
    {
        string[] lines = source.Split(separator: '\n');
        int line = Array.FindIndex(array: lines, match: l => l.Contains(value: needle));
        LspRefactors.Refactor? fix = LspDocComments.Fixes(text: source, fileName: File, language: Language.Revare,
                first: line, last: line, korean: false)
           .FirstOrDefault(predicate: f => f.Title == title);
        return fix == null
            ? null
            : string.Join(separator: "\n",
                values: lines.Take(count: fix.StartLine)
                             .Append(element: fix.NewText)
                             .Concat(second: lines.Skip(count: fix.EndLine + 1)));
    }

    [Fact]
    public void Frame_Routine_HasEveryLine()
    {
        string source = Doc(lines: ["module Test/Docs", "", "###", .. Clamp]);
        Assert.Equal(expected: string.Join(separator: "\n", "###", "### :typeparam T:", "### :param value:",
                "### :param low:", "### :returns:", "### :throws: {IndexOutOfRangeError}"),
            actual: LspDocComments.Frame(text: source, fileName: File, language: Language.Revare, line: 2, snippet: false));
    }

    [Fact]
    public void Frame_Snippet_PutsTabStops()
    {
        string source = Doc("module Test/Docs", "", "###", "routine show(text: S64)", "    return");
        Assert.Equal(expected: "### $1\n### :param text: $2$0",
            actual: LspDocComments.Frame(text: source, fileName: File, language: Language.Revare, line: 2, snippet: true));
    }

    [Fact]
    public void Frame_LeavesOutTheReceiversParameters()
    {
        string source = Doc("module Test/Docs", "", "record Box[T]", "    item: T", "", "###",
            "routine Box[T].pick[U](other: U) -> T", "    return me.item");
        Assert.Equal(expected: "###\n### :typeparam U:\n### :param other:\n### :returns:",
            actual: LspDocComments.Frame(text: source, fileName: File, language: Language.Revare, line: 5, snippet: false));
    }

    [Fact]
    public void Frame_UnderAnExistingDoc_IsNotOffered()
    {
        string source = Doc(lines: ["module Test/Docs", "", "### Clamps.", "###", .. Clamp]);
        Assert.Null(@object: LspDocComments.Frame(text: source, fileName: File, language: Language.Revare, line: 3,
            snippet: false));
    }

    [Fact]
    public void Fill_AddsMissingLinesInOrder()
    {
        string source = Doc(lines: ["module Test/Docs", "", "### Clamps a value.", "### :param low: The floor.",
            "###     It is kept.", .. Clamp]);
        Assert.Equal(expected: Doc(lines: ["module Test/Docs", "", "### Clamps a value.", "### :typeparam T:",
                "### :param value:", "### :param low: The floor.", "###     It is kept.", "### :returns:",
                "### :throws: {IndexOutOfRangeError}", .. Clamp]),
            actual: Fix(source: source, needle: "routine clamp", title: "Fill in the doc comment"));
    }

    [Fact]
    public void Remove_DropsStaleLines()
    {
        string source = Doc("module Test/Docs", "", "### Shows.", "### :param text: The text.", "### :param gone: Old.",
            "### :returns: Nothing.", "routine show(text: S64)", "    return");
        Assert.Equal(expected: Doc("module Test/Docs", "", "### Shows.", "### :param text: The text.",
                "routine show(text: S64)", "    return"),
            actual: Fix(source: source, needle: "### :param gone", title: "Remove doc lines that match nothing"));
    }
}
