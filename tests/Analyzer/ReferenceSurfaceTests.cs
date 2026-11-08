using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The surface of references: <c>%T</c> reads and <c>@T</c> writes, written in any type position. An entity becomes a
/// reference where one is expected, a second name for it is a write reference, a reference kept in storage is a link
/// (with or without <c>?</c>), and the builder's own names for them are not written by a program.
/// </summary>
public class ReferenceSurfaceTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Node
                                       id: S64

                                   """;

    [Fact]
    public void Analyze_SignsInEveryTypePosition_Analyze()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Holder
                                               link: @Node
                                               maybe: %Node?
                                               links: List[@Node?]

                                           routine pick(a: @Node, b: @Node) -> @Node
                                               return b

                                           routine start()
                                               var a = Node(id: 1)
                                               var b = Node(id: 2)
                                               var f: Callable[(@Node, @Node), @Node] = pick
                                               var fs = List[Callable[(@Node, @Node), @Node]]()
                                               fs.add_last(value: pick)
                                               var r: %Node = f(a, b)
                                               var h = Holder(link: a, maybe: none, links: [])
                                               h.links.add_last(value: b)
                                               show(r.id)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_ReferenceWrittenByName_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine total(n: Viewing[Node]) -> S64
                                                                       return n.id

                                                                   routine start()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "written `%Node`");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ReferenceWrittenByName);
    }

    [Fact]
    public void Analyze_ViewCallInProgram_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine total(n: %Node) -> S64
                                                                       return n.id

                                                                   routine start()
                                                                       var a = Node(id: 1)
                                                                       show(total(n: a.view()))
                                                                       return
                                                                   """,
            expectedErrorSubstring: "becomes a read reference by itself");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ReferenceVerbWritten);
    }

    [Fact]
    public void Analyze_NoneIntoLinkWithoutQuestionMark_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           entity Hunter
                                               target: @Node

                                           routine start()
                                               var h = Hunter(target: none)
                                               return
                                           """,
            expectedErrorSubstring: "link written without `?`, which always names an entity");
    }

    [Fact]
    public void Analyze_DestructWithoutDangerous_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   entity Tag
                                                                       name: Text

                                                                   routine Tag.destruct()
                                                                       return

                                                                   routine start()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "without `dangerous`");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.DestroyNotDangerous);
    }

    [Fact]
    public void Analyze_DangerousDestructAndDrop_Analyze()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Tag
                                               name: Text

                                           dangerous routine Tag.destruct()
                                               show(me.name)
                                               return

                                           routine start()
                                               var t = Tag(name: "a")
                                               drop t
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_StealOfReference_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine keep(n: Node)
                                                                       return

                                                                   routine start()
                                                                       var a = Node(id: 1)
                                                                       var b = a
                                                                       keep(n: steal b)
                                                                       return
                                                                   """,
            expectedErrorSubstring: "Point `steal` at where the original is held");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.StealWithoutHolding);
    }

    [Fact]
    public void Analyze_ReferenceForHandOverParameter_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine keep(n: Node)
                                                                       return

                                                                   routine start()
                                                                       var a = Node(id: 1)
                                                                       var b = a
                                                                       keep(n: b)
                                                                       return
                                                                   """,
            expectedErrorSubstring: "holds no original to give");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ReferenceForHandOver);
    }

    [Fact]
    public void Analyze_BlockOnEntity_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var a = Node(id: 1)
                                                                       modifying a as m
                                                                           m.id += 1
                                                                       return
                                                                   """,
            expectedErrorSubstring: "is an entity: use it directly");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.AccessBlockNotNeeded);
    }

    [Fact]
    public void SurfaceText_WritesSigns()
    {
        Assert.Equal(expected: "%Node, @List[S64], List[@Node?], %(Node?)",
            actual: RevareRules.Instance.SurfaceText(
                text: "Viewing[Node], System.Modifying[List[S64]], List[Optional[Modifying[Node]]], Viewing[Node?]"));
    }
}
