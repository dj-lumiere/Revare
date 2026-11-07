using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A place that stores a value of a protocol (<c>var x: Enemy</c>, <c>List[Enemy]</c>, a member variable, <c>Enemy?</c>)
/// holds a value of any type that obeys it. A Revare entity has one original there too: it goes in with <c>steal</c>, the
/// place is never copied by <c>=</c>, and an element read is a token. A <c>when</c> on such a value needs <c>else</c>,
/// and an <c>is</c> arm names a type that obeys the protocol.
/// </summary>
public class ProtocolValueTests
{
    private const string Prelude = """
                                   protocol Enemy
                                       routine Me.update()
                                       @readonly
                                       routine Me.hp() -> S64

                                   entity Slime
                                   obeys Enemy
                                       life: S64

                                   routine Slime.update()
                                       me.life -= 1
                                       return

                                   @readonly
                                   routine Slime.hp() -> S64
                                       return me.life

                                   entity Boss
                                   obeys Enemy
                                       life: S64

                                   routine Boss.update()
                                       me.life -= 5
                                       return

                                   @readonly
                                   routine Boss.hp() -> S64
                                       return me.life

                                   record Dot
                                       x: S64

                                   protocol Shape
                                       @readonly
                                       routine Me.area() -> S64

                                   record Square
                                   obeys Shape
                                       side: S64

                                   @readonly
                                   routine Square.area() -> S64
                                       return me.side * me.side

                                   """;

    [Fact]
    public void Analyze_StoredEntitiesWithSteal_Pass()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Arena
                                               boss: Enemy
                                               spare: Enemy?

                                           routine start()
                                               var enemies = List[Enemy]()
                                               var s = Slime(life: 3)
                                               enemies.add_last(value: steal s)
                                               enemies.add_last(value: Boss(life: 9))
                                               for e in enemies
                                                   e.update()
                                               var first = enemies[0]
                                               first.update()
                                               var x: Enemy = Boss(life: 4)
                                               var arena = Arena(boss: steal x, spare: none)
                                               arena.spare = Slime(life: 1)
                                               when enemies[1]
                                                   is Boss as b =>
                                                       b.update()
                                                   else =>
                                                       nothing
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_StoredRecordCopies_Pass()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var shapes = List[Shape]()
                                               var sq = Square(side: 2)
                                               shapes.add_last(value: sq)
                                               var one: Shape = sq
                                               var two = one
                                               var total = two.area() + shapes[0].area()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_CopyOfStoredEntityPlace_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var x: Enemy = Slime(life: 1)
                                                                       var y = x
                                                                       return
                                                                   """,
            expectedErrorSubstring: "You are keeping in 'y' an 'Enemy' that 'x' still holds");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
        Assert.DoesNotContain(collection: result.Errors, filter: e => e.Message.Contains(value: "Dispatched"));
    }

    [Fact]
    public void Analyze_EntityIntoStoredListWithoutSteal_IsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var s = Slime(life: 1)
                                               var enemies = List[Enemy]()
                                               enemies.add_last(value: s)
                                               return
                                           """,
            expectedErrorSubstring: "'add_last' keeps what its 'value' is given, and you are passing it a 'Slime'");
    }

    [Fact]
    public void Analyze_EntityIntoStoredVariableAndMemberWithoutSteal_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   entity Arena
                                                                       boss: Enemy

                                                                   routine start()
                                                                       var s = Slime(life: 1)
                                                                       var y: Enemy = s
                                                                       var t = Slime(life: 2)
                                                                       var arena = Arena(boss: t)
                                                                       var u = Slime(life: 3)
                                                                       arena.boss = u
                                                                       return
                                                                   """,
            expectedErrorSubstring: "You are keeping in 'y' a 'Slime' that 's' still holds");
        Assert.Equal(expected: 3,
            actual: result.Errors.Count(predicate: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment));
    }

    [Fact]
    public void Analyze_WhenOnStoredValueWithoutElse_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var x: Enemy = Slime(life: 1)
                                                                       when x
                                                                           is Slime as s =>
                                                                               s.update()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "Add an `else` arm");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Analyze_IsArmOfTypeThatDoesNotObey_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var x: Enemy = Slime(life: 1)
                                                                       when x
                                                                           is Dot as d =>
                                                                               var n = d.x
                                                                           else =>
                                                                               nothing
                                                                       return
                                                                   """,
            expectedErrorSubstring: "'is Dot' can never match here");
        Assert.Single(collection: result.Errors);
    }

    [Fact]
    public void Analyze_TypeParameterValueIntoStoredPlace_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine keep[T](value: T, into: Modifying[List[Enemy]])
                                                                   needs T obeys Enemy
                                                                       into.add_last(value: steal value)
                                                                       return

                                                                   routine start()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "a value of the type parameter 'T' where a value of 'Enemy' is stored");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TypeParameterValueIntoProtocolPlace);
    }

    [Fact]
    public void Analyze_RoutineNamingMeOnStoredValue_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         protocol Ranked
                                                             @readonly
                                                             routine Me.rank() -> S64
                                                             @readonly
                                                             routine Me.outranks(you: Me) -> Bool

                                                         record Card
                                                         obeys Ranked
                                                             n: S64

                                                         @readonly
                                                         routine Card.rank() -> S64
                                                             return me.n

                                                         @readonly
                                                         routine Card.outranks(you: Card) -> Bool
                                                             return me.n > you.n

                                                         routine start()
                                                             var cards = List[Ranked]()
                                                             cards.add_last(value: Card(n: 3))
                                                             var r = cards[0].rank()
                                                             var o = cards[0].outranks(you: cards[0])
                                                             return
                                                         """,
            expectedErrorSubstring: "'outranks' of 'Ranked' takes or gives a value of the type that obeys 'Ranked'");
        Assert.Single(collection: result.Errors);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ProtocolRoutineNotDispatchable);
    }

    [Fact]
    public void Analyze_StoredValueToProtocolParameter_Passes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine finish(e: Enemy)
                                               var h = e.hp()
                                               return

                                           routine look(e: Viewing[Enemy]) -> S64
                                               return e.hp()

                                           routine start()
                                               var enemies = List[Enemy]()
                                               enemies.add_last(value: Slime(life: 3))
                                               var h = look(e: enemies[0])
                                               var x: Enemy = Boss(life: 2)
                                               finish(e: steal x)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_TypeWithTheRoutinesWithoutObeys_Passes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Bat
                                               wings: S64

                                           routine Bat.update()
                                               me.wings -= 1
                                               return

                                           @readonly
                                           routine Bat.hp() -> S64
                                               return me.wings

                                           routine start()
                                               var enemies = List[Enemy]()
                                               enemies.add_last(value: Bat(wings: 2))
                                               for e in enemies
                                                   e.update()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_CompositionProtocolValues_Pass()
    {
        AssertAnalyzesSa(source: """
                                 protocol Moving
                                     routine Me.step()

                                 protocol Hitting
                                     @readonly
                                     routine Me.hit() -> S64

                                 protocol Fighter
                                 obeys Moving, Hitting

                                 entity Knight
                                 obeys Fighter
                                     x: S64

                                 routine Knight.step()
                                     me.x += 1
                                     return

                                 @readonly
                                 routine Knight.hit() -> S64
                                     return 3

                                 routine start()
                                     var fighters = List[Fighter]()
                                     fighters.add_last(value: Knight(x: 0))
                                     for f in fighters
                                         f.step()
                                         var h = f.hit()
                                     return
                                 """);
    }
}
