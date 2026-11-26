using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>@property</c> routine is read without parentheses (<c>c.kg</c>). It is a member routine with no parameters and
/// a result (RV-S717) that changes nothing in <c>me</c> (RV-S718), its type has no member variable of the same name
/// (RV-S719), it is never called with parentheses (RV-S720) or assigned (RV-S721), and a protocol's <c>@property</c> is
/// filled by a <c>@property</c> or a member variable, a plain routine by a plain routine (RV-S722).
/// </summary>
public class PropertyTests
{
    private static AnalysisResult Analyze(string declarations, string body)
    {
        return AnalyzeSa(source: $"""
                                  import IO/Console

                                  {declarations}

                                  routine start()
                                  {body}
                                      return
                                  """);
    }

    private static void AssertHasError(string declarations, string body, SemanticDiagnosticCode code)
    {
        AnalysisResult result = Analyze(declarations: declarations, body: body);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == code);
        Assert.All(collection: result.Errors, action: e => Assert.Equal(expected: code, actual: e.Code));
    }

    private const string Cow = """
                               record Cow
                                   weight: S64
                               """;

    [Fact]
    public void Analyze_PropertyReadWithoutParentheses_Analyzes()
    {
        AnalysisResult result = Analyze(declarations: Cow + """


                                                            @property
                                                            routine Cow.kg() -> S64
                                                                return me.weight
                                                            """,
            body: "    var c = Cow(weight: 3)\n    show(c.kg + 1)");
        Assert.True(condition: result.Errors.Count == 0,
            userMessage: string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message)));
    }

    [Fact]
    public void Analyze_PropertyWithParameter_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.heavier(than: S64) -> Bool
                                              return me.weight > than
                                          """,
            body: "    show(1)",
            code: SemanticDiagnosticCode.PropertyShape);

    [Fact]
    public void Analyze_PropertyWithoutResult_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.moo()
                                              return
                                          """,
            body: "    show(1)",
            code: SemanticDiagnosticCode.PropertyShape);

    [Fact]
    public void Analyze_PropertyChangingMe_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.grow() -> S64
                                              me.weight += 1
                                              return me.weight
                                          """,
            body: "    show(1)",
            code: SemanticDiagnosticCode.PropertyChangesMe);

    [Fact]
    public void Analyze_PropertyNamedLikeMemberVariable_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.weight() -> S64
                                              return 3
                                          """,
            body: "    show(1)",
            code: SemanticDiagnosticCode.PropertyNameTaken);

    [Fact]
    public void Analyze_PropertyCalledWithParentheses_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.kg() -> S64
                                              return me.weight
                                          """,
            body: "    var c = Cow(weight: 3)\n    show(c.kg())",
            code: SemanticDiagnosticCode.PropertyCalled);

    [Fact]
    public void Analyze_PropertyAssigned_Reports() =>
        AssertHasError(declarations: Cow + """


                                          @property
                                          routine Cow.kg() -> S64
                                              return me.weight
                                          """,
            body: "    var c = Cow(weight: 3)\n    c.kg = 4\n    show(c.weight)",
            code: SemanticDiagnosticCode.PropertyWritten);

    [Fact]
    public void Analyze_ProtocolPropertyFilledByPlainRoutine_Reports() =>
        AssertHasError(declarations: """
                                     protocol Weighed
                                         @property
                                         routine Me.kg() -> S64

                                     record Cow obeys Weighed
                                         weight: S64

                                     routine Cow.kg() -> S64
                                         return me.weight
                                     """,
            body: "    show(1)",
            code: SemanticDiagnosticCode.PropertyConformanceMismatch);

    [Fact]
    public void Analyze_ProtocolPropertyFilledByMemberVariable_Analyzes()
    {
        AnalysisResult result = Analyze(declarations: """
                                                      protocol Weighed
                                                          @property
                                                          routine Me.kg() -> S64

                                                      record Cow obeys Weighed
                                                          kg: S64

                                                      routine heaviest[T](a: T, b: T) -> S64
                                                      needs T obeys Weighed
                                                          return a.kg.max(b.kg)
                                                      """,
            body: "    show(heaviest(a: Cow(kg: 3), b: Cow(kg: 5)))");
        Assert.True(condition: result.Errors.Count == 0,
            userMessage: string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message)));
    }
}
