using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A call whose type argument comes only from its result (<c>n.to()</c>, <c>ask()</c>) takes it from where the value
/// goes. The expected type flows inward: from a declared variable type, an assignment, a compound assignment, a
/// parameter and a return type, into both operands of an operator whose routine takes and gives <c>Me</c>, across a
/// comparison, and down a chain of member calls that give <c>Me</c> back. It never comes from later uses of a variable.
/// </summary>
public class ExpectedTypeFlowTests
{
    private static string Program(string body) => $$"""
                                                   import IO/Console

                                                   routine half(x: B64) -> B64
                                                       return x / 2.0

                                                   routine widened(n: S64) -> B64
                                                       return n.to()

                                                   routine pick[T](x: T) -> T
                                                       return x

                                                   routine B64.positive() -> B64?
                                                       if me > 0.0
                                                           return me
                                                       return none

                                                   routine start()
                                                       var n: S64 = 9
                                                       var a: B64 = 0.0
                                                   {{string.Join(separator: "\n", values: body.Split(separator: '\n').Select(selector: line => "    " + line))}}
                                                       show(a)
                                                       return
                                                   """;

    private static void AssertBuilds(string body)
    {
        AnalysisResult result = AnalyzeSa(source: Program(body: body));
        Assert.True(condition: result.Errors.Count == 0,
            userMessage: string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message)));
    }

    private static SemanticError SingleError(string body, SemanticDiagnosticCode code)
    {
        AnalysisResult result = AnalyzeSa(source: Program(body: body));
        return Assert.Single(collection: result.Errors, predicate: e => e.Code == code);
    }

    /// <summary>The line this rule was written for: a compound assignment into a B128, an operator, and a chain of
    /// routines that give back B128.</summary>
    [Fact]
    public void Analyze_CompoundAssignmentThroughOperatorAndChain_Builds()
    {
        AssertBuilds(body: """
                           var result: B128 = 0.0
                           for i in S64(1) to 10
                               result += i.to() * i.to().sqrt().sin()
                           show(result)
                           """);
    }

    [Fact]
    public void Analyze_Binding_Builds() => AssertBuilds(body: "a = n.to()\nvar b: B64 = n.to()\nshow(b)");

    [Fact]
    public void Analyze_Assignment_Builds() => AssertBuilds(body: "a = n.to()");

    [Fact]
    public void Analyze_CompoundAssignment_Builds() => AssertBuilds(body: "a += n.to()");

    [Fact]
    public void Analyze_OperatorOperands_Build() => AssertBuilds(body: "a = n.to() * n.to() + a");

    [Fact]
    public void Analyze_Negation_Builds() => AssertBuilds(body: "a = -n.to()");

    /// <summary>The right side of an operator expects what the left side's operator routine takes.</summary>
    [Fact]
    public void Analyze_RightOperandFromLeft_Builds() => AssertBuilds(body: "show(a * n.to())");

    [Fact]
    public void Analyze_ComparisonLeftFromRight_Builds() => AssertBuilds(body: "if n.to() < a\n    show(\"less\")");

    [Fact]
    public void Analyze_ComparisonRightFromLeft_Builds() => AssertBuilds(body: "if a == n.to()\n    show(\"equal\")");

    [Fact]
    public void Analyze_ChainOfMeRoutines_Builds() => AssertBuilds(body: "a = n.to().sqrt().abs()");

    [Fact]
    public void Analyze_Argument_Builds() => AssertBuilds(body: "a = half(x: n.to())");

    [Fact]
    public void Analyze_Return_Builds() => AssertBuilds(body: "a = widened(n: n)");

    /// <summary>A generic free routine whose type parameter only its result names takes it the same way.</summary>
    [Fact]
    public void Analyze_FreeResultOnlyGenericInOperator_Builds() => AssertBuilds(body: "var q: S64 = ask() + 1\nshow(q)");

    /// <summary>A variable's type never comes from where it is used later.</summary>
    [Fact]
    public void Analyze_UntypedVariable_StaysAmbiguous()
    {
        SemanticError error = SingleError(body: "var z = n.to()\na = z", code: SemanticDiagnosticCode.ResultTypeArgumentUnknown);
        Assert.Contains(expectedSubstring: "The builder looks at a written variable type", actualString: error.Message);
    }

    /// <summary>A bare number on the other side of a comparison has no type of its own to give.</summary>
    [Fact]
    public void Analyze_ComparisonWithBareNumber_StaysAmbiguous()
    {
        AnalysisResult result = AnalyzeSa(source: Program(body: "if n.to() < 5\n    show(\"x\")"));
        Assert.Single(collection: result.Errors);
        Assert.Equal(expected: SemanticDiagnosticCode.ResultTypeArgumentUnknown, actual: result.Errors[index: 0].Code);
    }

    /// <summary>A call on the result that gives back another type stops the expected type, and the message says
    /// which call.</summary>
    [Fact]
    public void Analyze_ChainThroughOtherType_SaysWhichCall()
    {
        SemanticError error = SingleError(body: "var k: U64 = n.to().count()",
            code: SemanticDiagnosticCode.ResultTypeArgumentUnknown);
        Assert.Contains(expectedSubstring: "'.count()' on its result doesn't give back the type", actualString: error.Message);
    }

    /// <summary>A free generic call with nothing to give its type keeps RV-S161 and names the places looked at.</summary>
    [Fact]
    public void Analyze_FreeResultOnlyGenericAlone_NamesPlaces()
    {
        SemanticError error = SingleError(body: "var r = ask() + 1", code: SemanticDiagnosticCode.CannotInferTypeArgument);
        Assert.Contains(expectedSubstring: "'ask()', which takes its type argument 'T' from where the result goes",
            actualString: error.Message);
    }

    /// <summary>The arguments bind a type parameter first: <c>pick(x: n)</c> is an S64 whatever the variable is.</summary>
    [Fact]
    public void Analyze_ArgumentsWinOverExpectedType()
    {
        SingleError(body: "var e: B64 = pick(x: n)", code: SemanticDiagnosticCode.VariableInitializerTypeMismatch);
    }

    /// <summary>With nothing expected of the whole, the left operand takes the right one's type when its operator
    /// routine takes that type.</summary>
    [Fact]
    public void Analyze_LeftOperandFromRight_Builds() => AssertBuilds(body: "var x: B128 = 2.0\nshow(n.to() * x)");

    /// <summary>A chain whose receiver takes the right operand's type.</summary>
    [Fact]
    public void Analyze_LeftChainFromRight_Builds() => AssertBuilds(body: "var x: B128 = 2.0\nshow(n.to().sqrt() + x)");

    [Fact]
    public void Analyze_ChainedComparison_Builds() =>
        AssertBuilds(body: "var b: B64 = 10.0\nif 0.0 <= n.to() <= b\n    show(\"inside\")");

    /// <summary>A bare number gives a chained comparison no type.</summary>
    [Fact]
    public void Analyze_ChainedComparisonOfNumbers_StaysAmbiguous()
    {
        SingleError(body: "if 0 <= n.to() <= 10\n    show(\"x\")", code: SemanticDiagnosticCode.ResultTypeArgumentUnknown);
    }

    /// <summary>A call that gives `Me?` hands a `T?` expectation on to its receiver as `T`.</summary>
    [Fact]
    public void Analyze_MaybeChain_Builds() => AssertBuilds(body: "var p: B64? = n.to().positive()\nshow(p)");

    /// <summary>An operator routine that fits more than one way gives no type, and the message names its
    /// routines.</summary>
    [Fact]
    public void Analyze_SeveralOperatorRoutines_NamesThem()
    {
        SemanticError error = SingleError(body: "show(a + n.to())", code: SemanticDiagnosticCode.ResultTypeArgumentUnknown);
        Assert.Contains(expectedSubstring: "'B64.add' fits more than one way here (add(you: B64) -> B64, add(you: C128) -> C128)",
            actualString: error.Message);
    }
}
