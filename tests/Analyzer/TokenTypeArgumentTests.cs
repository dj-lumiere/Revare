using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A token (<c>%T</c> / <c>@T</c>) is never a type argument (RV-S601): a container, a <c>Optional</c>, a
/// tuple, a variant or a generic type holding one could keep it after the place it points at is gone. A routine may
/// return <c>Optional</c> of a token (an iterator's <c>emit</c> under <c>try</c>), and the caller unpacks it with
/// <c>when</c> where it gets it. A lambda that captures a token is bound to it and is never returned or kept (RV-S606). The first five cases are the analogs of
/// Rust #164250 that read a dead entity before.
/// </summary>
public class TokenTypeArgumentTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Cell
                                       n: S64

                                   """;

    private static void AssertHasErrorS601(string source, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenMemberVariableNotAllowed);
    }

    [Fact]
    public void Analyze_EntityFieldListOfReferences_KeepsLinks()
    {
        // A list a member variable keeps holds links, which may be kept anywhere, even moved out by a destruct.
        AssertAnalyzesSa(source: Prelude + """
                                           entity Q
                                               held: List[%Cell]

                                           routine start()
                                               var l = Cell(n: 42)
                                               var q = Q(held: List[%Cell]())
                                               q.held.add_last(value: l)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_ListOfReferencesParameter_KeepsLinks()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine fill(xs: @List[%Cell])
                                               var l = Cell(n: 42)
                                               xs.add_last(value: l)
                                               return

                                           routine start()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_MaybeOfTokenInVariable_IsALinkThatReadsNone()
    {
        // An optional token on an entity kept in a name is a link: once `l` ends, `when m` gives none.
        AssertAnalyzes(source: Prelude + """
                                        routine start()
                                            var m: Optional[%Cell] = none
                                            if true
                                                var l = Cell(n: 42)
                                                m = l
                                            when m
                                                is None =>
                                                    show("none")
                                                else as v =>
                                                    show(f"{v.n}")
                                            return
                                        """);
    }

    [Fact]
    public void Analyze_LocalListOfReferences_KeepsLinks()
    {
        // A collection keeps a reference as a link: a use after the entity ended fails when it runs.
        AssertAnalyzesSa(source: """
                                 import IO/Console

                                 entity Cell
                                     n: S64

                                 routine start()
                                     var xs = List[%Cell]()
                                     if true
                                         var l = Cell(n: 42)
                                         xs.add_last(value: l)
                                     when try xs[0].n
                                         is None => show("ended")
                                         else as n => show(n)
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_LambdaCapturingToken_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine make() -> Callable[(), S64]
                                                                       var l = Cell(n: 42)
                                                                       var v = l
                                                                       return () given (v) => v.n

                                                                   routine start()
                                                                       var f = make()
                                                                       show(f"{f()}")
                                                                       return
                                                                   """,
            expectedErrorSubstring: "You capture 'v' in this lambda");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.LambdaCaptureToken);
    }

    [Fact]
    public void Analyze_InferredListLiteralOfTokens_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var l = Cell(n: 42)
                                            var v: %Cell = l
                                            var xs = [v]
                                            return
                                        """,
            expected: "You use `List[Viewing[Cell]]` for this value");
    }

    [Fact]
    public void Analyze_TupleHoldingToken_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var l = Cell(n: 42)
                                            var v: %Cell = l
                                            var t = (v, 1)
                                            return
                                        """,
            expected: "You use `Tuple[Viewing[Cell], S64]` for this value");
    }

    [Fact]
    public void Analyze_UserGenericTypeWithTokenArgument_Errors()
    {
        AssertHasErrorS601(source: """
                                        entity Box[T]
                                            v: T

                                        routine start()
                                            var l = Cell(n: 42)
                                            var r: %Cell = l
                                            var b = Box(v: r)
                                            return
                                        """,
            expected: "You use `Box[Viewing[Cell]]` for this value");
    }

    [Fact]
    public void Analyze_VariantCaseHoldingReference_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   variant Held
                                                                       %Cell
                                                                       S64

                                                                   routine start()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "as payload for variant case");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenVariantPayloadNotAllowed);
    }

    [Fact]
    public void Analyze_TryOfTokenUnpackedWithWhen_Analyzes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var cells = [Cell(n: 1), Cell(n: 2)]
                                               when try cells.view_at(index: 0)
                                                   is None =>
                                                       show("none")
                                                   else as c =>
                                                       show(f"{c.n}")
                                               for pair in cells.enumerate()
                                                   show(f"{pair.item0}")
                                               for (i, c) in cells.enumerate()
                                                   show(f"{i} {c.n}")
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_TryOfTokenBoundToName_IsALink()
    {
        // What `try` gives on a routine returning a token on an entity, kept in a name, is a link to the element.
        AssertAnalyzes(source: Prelude + """
                                         routine start()
                                             var cells = [Cell(n: 1)]
                                             var m = try cells.view_at(index: 0)
                                             return
                                         """);
    }
}
