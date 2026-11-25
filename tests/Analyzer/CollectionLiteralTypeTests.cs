using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A collection literal's elements are held to the type the literal has: the element type a written or expected type
/// gives (RV-S550 for a list, RV-S556 for a set, RV-S554 and RV-S555 for a dict's keys and values), and a literal that
/// builds its own type is held to that type's constraints (RV-S150). A generic type's creator takes its type arguments
/// from the type it goes to.
/// </summary>
public class CollectionLiteralTypeTests
{
    private static AnalysisResult Analyze(string body)
    {
        return AnalyzeSa(source: $$"""
                                   import IO/Console

                                   record Cell[T]
                                   needs RecordType T
                                       value: T
                                       tag: Text

                                   record Loose
                                       f: Callable[(), S64]

                                   routine start()
                                   {{body}}
                                       return
                                   """);
    }

    private static void AssertHasError(string body, SemanticDiagnosticCode code)
    {
        AnalysisResult result = Analyze(body: body);
        // Every error is the one asked for (a set of unhashable records misses both `==` and the hash), so the literal
        // is reported for its element and nothing else.
        Assert.NotEmpty(collection: result.Errors);
        Assert.All(collection: result.Errors, action: e => Assert.Equal(expected: code, actual: e.Code));
    }

    [Fact]
    public void Analyze_ListFirstElementOfOtherType_Reports() =>
        AssertHasError(body: "    var xs: List[Text] = [1]\n    show(xs.count())",
            code: SemanticDiagnosticCode.ListElementTypeMismatch);

    [Fact]
    public void Analyze_SetElementOfOtherType_Reports() =>
        AssertHasError(body: "    var xs: Set[Text] = {\"a\", 2}\n    show(xs.count())",
            code: SemanticDiagnosticCode.SetElementTypeMismatch);

    [Fact]
    public void Analyze_DictKeyOfOtherType_Reports() =>
        AssertHasError(body: "    var d: Dict[Text, S64] = {1: 2}\n    show(d.count())",
            code: SemanticDiagnosticCode.DictKeyTypeMismatch);

    [Fact]
    public void Analyze_DictValueOfOtherType_Reports() =>
        AssertHasError(body: "    var d: Dict[Text, Text] = {\"a\": 2}\n    show(d.count())",
            code: SemanticDiagnosticCode.DictValueTypeMismatch);

    /// <summary>A set the literal builds for itself needs elements that hash, as a written <c>Set[Loose]</c>
    /// does.</summary>
    [Fact]
    public void Analyze_SetOfUnhashableRecords_Reports() =>
        AssertHasError(body: "    var a = Loose(f: () => 1)\n    var s = {a}\n    show(s.count())",
            code: SemanticDiagnosticCode.ProtocolConstraintViolation);

    [Fact]
    public void Analyze_GenericCreatorTakesExpectedTypeArguments_Builds()
    {
        AnalysisResult result = Analyze(body: """
                                                  var one: Cell[U8] = Cell(value: 255, tag: "max")
                                                  var cells: List[Cell[U8]] = [Cell(value: 1, tag: "a")]
                                                  var maybe: List[S64?] = [1, none]
                                                  show(f"{one.value} {cells.count()} {maybe.count()}")
                                              """);
        Assert.True(condition: result.Errors.Count == 0,
            userMessage: string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message)));
    }
}
