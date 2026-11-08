using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A link is an optional token on an entity (<c>Modifying[Node]?</c> / <c>Viewing[Node]?</c>). A member variable, a
/// collection's element, a global or a local may keep one: it never keeps its entity alive, and taking it with
/// <c>when</c> gives none once the entity ended. A token without <c>?</c> is still never kept (RV-S601, pointing to
/// <c>?</c>), a link never crosses a routine boundary (a parameter or a result), a place that keeps a link takes the
/// entity or a token the way a token parameter does, and the token a link gives may be the same entity as any other
/// name of its type.
/// </summary>
public class OptionalTokenLinkTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Node
                                       id: S64

                                   """;

    [Fact]
    public void Analyze_TokenWithoutQuestionMarkInMemberVariable_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   entity Child
                                                                       parent: Modifying[Node]

                                                                   routine start()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "without `?`");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenMemberVariableNotAllowed);
    }

    [Fact]
    public void Analyze_TokenWithoutQuestionMarkInList_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           entity Holder
                                               nodes: List[Viewing[Node]]

                                           routine start()
                                               return
                                           """,
            expectedErrorSubstring: "without `?`");
    }

    [Fact]
    public void Analyze_LinksInMemberVariableListAndLocal_Analyze()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Child
                                             parent: Modifying[Node]?
                                             seen: List[Viewing[Node]?]

                                         routine start()
                                             var a = Node(id: 1)
                                             var c = Child(parent: none, seen: [])
                                             c.parent = a
                                             c.seen.add_last(value: a)
                                             var kept = c.parent
                                             when kept
                                                 is None => show("none")
                                                 else as p => show(p.id)
                                             return
                                         """);
    }

    [Fact]
    public void Analyze_AssignmentMakesLinkFromEntityAndToken_Analyzes()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Child
                                             parent: Modifying[Node]?
                                             reader: Viewing[Node]?

                                         routine start()
                                             var a = Node(id: 1)
                                             var c = Child(parent: a, reader: none)
                                             var m = a.modify()
                                             c.parent = m
                                             c.reader = m
                                             c.reader = a
                                             return
                                         """);
    }

    [Fact]
    public void Analyze_LinkParameter_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine describe(link: Viewing[Node]?) -> S64
                                               when link
                                                   is None => return 0
                                                   else as n => return n.id

                                           routine start()
                                               return
                                           """,
            expectedErrorSubstring: "a link is taken where it is kept");
    }

    [Fact]
    public void Analyze_LinkResult_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           entity Child
                                               parent: Modifying[Node]?

                                           routine Child.parent_link() -> Modifying[Node]?
                                               return me.parent

                                           routine start()
                                               return
                                           """,
            expectedErrorSubstring: "a link is taken where it is kept");
    }

    [Fact]
    public void Analyze_TokenFromLinkMayBeSameEntity_EndsTokenOfOtherName()
    {
        AssertHasError(source: Prelude + """
                                         entity Box
                                             n: S64

                                         entity Cell
                                             box: Box
                                             other: Modifying[Cell]?

                                         routine Cell.swap_box()
                                             var b = me.box.modify()
                                             when me.other
                                                 is None => nothing
                                                 else as c => c.box = Box(n: 2)
                                             show(b.n)
                                             return

                                         routine start()
                                             return
                                         """,
            expectedErrorSubstring: "which may be the same Cell as 'me'");
    }

    [Fact]
    public void Analyze_LinkReadThroughViewing_IsReadOnlyAndCopiesOut()
    {
        AssertHasError(source: Prelude + """
                                         entity Child
                                             parent: Modifying[Node]?

                                         routine peek(c: Viewing[Child]) -> S64
                                             var kept = c.parent
                                             when c.parent
                                                 is None => return 0
                                                 else as p =>
                                                     p.id = 5
                                                     return p.id

                                         routine start()
                                             return
                                         """,
            expectedErrorSubstring: "which only reads the value");
    }

    [Fact]
    public void Analyze_LinkCopiedOutOfViewing_Analyzes()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Child
                                             parent: Modifying[Node]?

                                         routine parent_id(c: Viewing[Child]) -> S64
                                             var kept = c.parent
                                             when kept
                                                 is None => return 0
                                                 else as p => return p.id

                                         routine start()
                                             return
                                         """);
    }
}
