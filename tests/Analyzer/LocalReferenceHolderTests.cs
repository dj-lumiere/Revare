using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A local that holds references (a local <c>List[@T]</c>, <c>Dict[K, @T]</c>, <c>@T?</c>, a variant with a reference
/// case) lasts as long as the shortest-lived entity put in it: the build takes the sources of every reference put in
/// it, and using the local after any of them ended is RV-S643. Member variables, globals and collections in storage
/// keep links, checked where they are used.
/// </summary>
public class LocalReferenceHolderTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Node
                                       id: S64

                                   """;

    private static void AssertHasErrorEnded(string body, string holder, string source)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + body,
            expectedErrorSubstring: $"You are using '{holder}', which holds references, but '{source}'");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_HolderUsedWhileEverySourceLives_Analyzes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine fill(into: @List[@Node], n: @Node)
                                               into.add_last(value: n)
                                               return

                                           routine start()
                                               var a = Node(id: 1)
                                               var b = Node(id: 2)
                                               var xs: List[@Node] = [a]
                                               fill(into: xs, n: b)
                                               var byId = Dict[S64, @Node]()
                                               byId[1] = a
                                               var p: @Node? = b
                                               for n in xs
                                                   show(n.id)
                                               show(byId[1].id)
                                               when p
                                                   is None => show("none")
                                                   else as q => show(q.id)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_ListAfterAnElementsBlockEnded_Errors()
    {
        AssertHasErrorEnded(body: """
                          routine start()
                              var a = Node(id: 1)
                              var xs: List[@Node] = [a]
                              if true
                                  var b = Node(id: 2)
                                  xs.add_last(value: b)
                              show(xs.count())
                              return
                          """, holder: "xs", source: "b");
    }

    [Fact]
    public void Analyze_OptionalReferenceAfterDrop_Errors()
    {
        AssertHasErrorEnded(body: """
                          routine start()
                              var c = Node(id: 3)
                              var p: @Node? = c
                              drop c
                              when p
                                  is None => show("none")
                                  else as q => show(q.id)
                              return
                          """, holder: "p", source: "c");
    }

    [Fact]
    public void Analyze_ElementReadOutAfterItsSourceEnded_Errors()
    {
        AssertHasErrorEnded(body: """
                          routine start()
                              var a = Node(id: 1)
                              var ys = List[@Node]()
                              ys.add_last(value: a)
                              var zs: List[@Node] = []
                              for n in ys
                                  zs.add_last(value: n)
                              var first = zs[0]
                              drop a
                              show(first.id)
                              return
                          """, holder: "first", source: "a");
    }

    [Fact]
    public void Analyze_ListFilledByARoutineWithAShortLivedEntity_Errors()
    {
        AssertHasErrorEnded(body: """
                          routine fill(into: @List[@Node], n: @Node)
                              into.add_last(value: n)
                              return

                          routine start()
                              var xs = List[@Node]()
                              if true
                                  var b = Node(id: 2)
                                  fill(into: xs, n: b)
                              show(xs.count())
                              return
                          """, holder: "xs", source: "b");
    }

    [Fact]
    public void Analyze_DictValueAfterItsSourceWasReplaced_Errors()
    {
        AssertHasErrorEnded(body: """
                          routine start()
                              var a = Node(id: 1)
                              var byId = Dict[S64, @Node]()
                              byId[1] = a
                              a = Node(id: 5)
                              show(byId.count())
                              return
                          """, holder: "byId", source: "a");
    }

    [Fact]
    public void Analyze_LocalVariantAfterItsCaseEnded_Errors()
    {
        AssertHasErrorEnded(body: """
                          variant Seen
                              %Node
                              Text

                          routine start()
                              var a = Node(id: 1)
                              var s: Seen = a
                              drop a
                              when s
                                  is %Node as n => show(n.id)
                                  is Text as t => show(t)
                              return
                          """, holder: "s", source: "a");
    }

    [Fact]
    public void Analyze_HolderGivenANewValue_StartsAgain()
    {
        // A new value for the whole local starts from the sources of what it holds now.
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var a = Node(id: 1)
                                               var xs = List[@Node]()
                                               if true
                                                   var b = Node(id: 2)
                                                   xs.add_last(value: b)
                                               xs = [a]
                                               show(xs[0].id)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_HolderDroppedAfterItsSourceEnded_Analyzes()
    {
        // Letting go of the local reads none of its references.
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var xs = List[@Node]()
                                               if true
                                                   var b = Node(id: 2)
                                                   xs.add_last(value: b)
                                               drop xs
                                               return
                                           """);
    }
}
