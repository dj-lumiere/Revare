using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>for</c> over an <c>IndexEmittable</c> value (a <c>List</c>) counts its positions and takes the token at each
/// one. The loop is checked as the walk it stands for: a change to the list's shape inside it is RV-S625, and the
/// element it hands out is a token on the list's element.
/// </summary>
public class ForIndexWalkTests
{
    /// <summary>Adding to the list a loop goes through is RV-S625, and the loop variable is not counted again as a
    /// name holding an element (no RV-S640 beside it).</summary>
    [Fact]
    public void Analyze_AddingToTheWalkedList_ReportsReshapingOnce()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var xs: List[S64] = [1, 2]
                                                      for x in xs
                                                          xs.remove_last()
                                                      return
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReshapingDuringIteration);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.HeldElementOfAliasReshaped);
    }

    /// <summary>The element of a list a routine is handed is the caller's: the routine may give it back.</summary>
    [Fact]
    public void Analyze_ReturningTheElementOfAHandedList_Analyzes()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Pt
                                                      x: S64

                                                  routine first(xs: @List[Pt]) -> @Pt
                                                      for p in xs
                                                          return p
                                                      throw AbsentValueError()
                                                  """);
        Assert.True(condition: result.Errors.Count == 0,
            userMessage: string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message)));
    }

    /// <summary>A read token on a value of a type parameter has the conversions the parameter has: none, so
    /// <c>x.to[S64]()</c> is RV-S646 as it is on <c>x: T</c>.</summary>
    [Fact]
    public void Analyze_ConversionThroughATokenOnATypeParameter_Reports()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine sum_all[T](xs: %List[T]) -> S64
                                                  needs T obeys Integral
                                                      var t: S64 = 0
                                                      for x in xs
                                                          t += x.to[S64]()
                                                      return t
                                                  """);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.SpecializationNotFound);
    }
}
