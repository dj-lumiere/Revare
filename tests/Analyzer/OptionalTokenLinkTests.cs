using Builder.Diagnostics;
using Builder.Verification.Results;
using TypeModel.Types;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A link is an optional token on an entity (<c>@Node?</c> / <c>%Node?</c>). A member variable, a
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
    public void Analyze_LinkWithoutQuestionMarkInMemberVariable_Builds()
    {
        // `parent: @Node` is a link that always names an entity: a use after it ended fails when it runs.
        AssertAnalyzesSa(source: Prelude + """
                                           entity Child
                                               parent: @Node

                                           routine start()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_LinkWithoutQuestionMarkInList_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Holder
                                               nodes: List[%Node]

                                           routine start()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_LinksInMemberVariableListAndLocal_Analyze()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Child
                                             parent: @Node?
                                             seen: List[%Node?]

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
                                             parent: @Node?
                                             reader: %Node?

                                         routine start()
                                             var a = Node(id: 1)
                                             var c = Child(parent: a, reader: none)
                                             var m = a
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
                                           routine describe(link: %Node?) -> S64
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
                                               parent: @Node?

                                           routine Child.parent_link() -> @Node?
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
                                             other: @Cell?

                                         routine Cell.swap_box()
                                             var b = me.box
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
                                             parent: @Node?

                                         routine peek(c: %Child) -> S64
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
                                             parent: @Node?

                                         routine parent_id(c: %Child) -> S64
                                             var kept = c.parent
                                             when kept
                                                 is None => return 0
                                                 else as p => return p.id

                                         routine start()
                                             return
                                         """);
    }

    [Fact]
    public void Analyze_LinkEquality_PointsToIdentity()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var a = Node(id: 1)
                                               var x: @Node? = a
                                               var y: @Node? = a
                                               show(x == y)
                                               return
                                           """,
            expectedErrorSubstring: "'==='");
    }

    [Fact]
    public void Analyze_LinkIdentityAndSetKey_Analyze()
    {
        AssertAnalyzes(source: Prelude + """
                                         routine start()
                                             var a = Node(id: 1)
                                             var x: @Node? = a
                                             var y: @Node? = a
                                             var seen = Set[@Node?]()
                                             drop seen.add(x)
                                             var score = Dict[%Node?, S64]()
                                             show(x === y)
                                             show(x !== y)
                                             return
                                         """);
    }

    [Fact]
    public void Analyze_ElementAndLoopLinksThroughViewing_AreReadOnly()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine poke(links: %List[@Node?])
                                               for link in links
                                                   when link
                                                       is None => nothing
                                                       else as n => n.id = 98
                                               return

                                           routine start()
                                               return
                                           """,
            expectedErrorSubstring: "which only reads the value");
        AssertHasErrorSa(source: Prelude + """
                                           routine poke(links: %Dict[S64, @Node?])
                                               for (k, link) in links
                                                   when link
                                                       is None => nothing
                                                       else as n => n.id = 97
                                               return

                                           routine start()
                                               return
                                           """,
            expectedErrorSubstring: "which only reads the value");
    }

    [Fact]
    public void Analyze_BareLinkUse_ReadsTheEntity()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Child
                                             parent: @Node?

                                         routine start()
                                             var a = Node(id: 1)
                                             var c = Child(parent: none)
                                             c.parent = a
                                             c.parent.id = 2
                                             show(c.parent.id)
                                             show(try c.parent.id)
                                             return
                                         """);
    }

    [Fact]
    public void Analyze_GenericLinkMemberVariable_Analyzes()
    {
        AssertAnalyzes(source: Prelude + """
                                         entity Wrap[T]
                                             link: @T?

                                         routine start()
                                             var a = Node(id: 1)
                                             var w = Wrap[Node](link: none)
                                             w.link = a
                                             return
                                         """);
    }

    /// <summary>A generic entity's link written without <c>?</c> stays one in each instance: it never takes none.</summary>
    [Fact]
    public void Analyze_GenericRequiredLinkInInstance_RefusesNone()
    {
        AssertHasErrorSa(source: Prelude + """
                                           entity Wrap[T]
                                               link: @T

                                           routine start()
                                               var a = Node(id: 1)
                                               var w = Wrap[Node](link: a)
                                               w.link = none
                                               return
                                           """,
            expectedErrorSubstring: "It is written without `?`");
    }

    /// <summary>A name that holds a reference takes a link's reference when assigned one, as its declaration does.</summary>
    [Fact]
    public void Analyze_LinkAssignedToReferenceLocal_Analyzes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity GNode
                                               id: S64
                                               out: List[@GNode]

                                           routine start()
                                               var xs = List[GNode]()
                                               xs.add_last(value: GNode(id: 0, out: []))
                                               xs[0].out.add_last(value: xs[0])
                                               var cur: @GNode = xs[0]
                                               cur = cur.out[0]
                                               show(cur.id)
                                               return
                                           """);
    }
}
