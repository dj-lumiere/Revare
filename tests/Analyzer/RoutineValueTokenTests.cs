using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A token a call through a routine value gives back comes from the arguments the call handed it, as for a direct call:
/// a routine value bound to a name, a lambda, one read out of a member variable, a list or a global. Ending any of those
/// arguments ends the token (RV-S643), and ending anything else does not. A lambda that would give a token taken from
/// what it captured is RV-S600 where it is written, so the arguments are all a call's result can come from.
/// </summary>
public class RoutineValueTokenTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Box
                                       n: S64

                                   routine pick_second(a: @Box, b: @Box) -> @Box
                                       return b

                                   """;

    private static void AssertHasErrorEndedBy(string body, string ended)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + body,
            expectedErrorSubstring: $"'{ended}', where it was taken from");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_NamedRoutineValueResultAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine start()
                                var f: Callable[(@Box, @Box), @Box] = pick_second
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = f(a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_RoutineValueResultAfterTheOtherArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine start()
                                var f: Callable[(@Box, @Box), @Box] = pick_second
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = f(a, b)
                                drop a
                                show(r.n)
                                return
                            """, ended: "a");
    }

    [Fact]
    public void Analyze_RoutineValueResultAfterSomethingElseEnds_Analyzes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var f: Callable[(@Box, @Box), @Box] = pick_second
                                               var a = Box(n: 1)
                                               var b = Box(n: 2)
                                               var c = Box(n: 3)
                                               var r = f(a, b)
                                               drop c
                                               show(r.n)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_LambdaResultAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine start()
                                var f = (x: @Box, y: @Box) given () => y
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = f(a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_MemberVariableRoutineValueResultAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            entity Holder
                                f: Callable[(@Box, @Box), @Box]

                            routine start()
                                var h = Holder(f: pick_second)
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = h.f(a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_ListElementRoutineValueResultAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine start()
                                var fs = List[Callable[(@Box, @Box), @Box]]()
                                fs.add_last(value: pick_second)
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = fs[0](a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_GlobalRoutineValueResultAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            global chooser: Callable[(@Box, @Box), @Box] = pick_second

                            routine start()
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = chooser(a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_GenericRoutineGivenTokensAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine pick[T](a: T, b: T) -> T
                                return b

                            routine start()
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = pick[@Box](a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }

    [Fact]
    public void Analyze_LambdaGivingAReferenceOnWhatItCaptured_IsCheckedWhereUsed()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var a = Box(n: 1)
                                               var b = Box(n: 2)
                                               var f = (x: @Box) given (b) => b
                                               var r = f(a)
                                               show(r.n)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_GenericRoutineOverTokensAfterAnArgumentEnds_Errors()
    {
        AssertHasErrorEndedBy(body: """
                            routine pick_v[T](a: @T, b: @T) -> @T
                                return b

                            routine start()
                                var a = Box(n: 1)
                                var b = Box(n: 2)
                                var r = pick_v[Box](a, b)
                                drop b
                                show(r.n)
                                return
                            """, ended: "b");
    }
}
