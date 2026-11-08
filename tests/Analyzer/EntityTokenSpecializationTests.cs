namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A member of a specialization family (<c>routine Random.next[U64]()</c>) is called through an entity's token: a
/// <c>@Random</c> reaches every member, a <c>%Random</c> only the members that read.
/// </summary>
public class EntityTokenSpecializationTests
{
    [Fact]
    public void ModifyingToken_CallsTheEntitysSpecialization()
    {
        AssertAnalyzesSa(source: """
                                 import Random

                                 routine draw(rng: @Random) -> U64
                                     return rng.next[U64]()

                                 routine start()
                                     var r = make_random(seed: 1)
                                     drop draw(rng: r)
                                     return
                                 """);
    }

    [Fact]
    public void ViewingToken_ChangingSpecialization_NamesModifying()
    {
        AssertHasErrorSa(source: """
                                 import Random

                                 routine peek(rng: %Random) -> U64
                                     return rng.next[U64]()

                                 routine start()
                                     var r = make_random(seed: 1)
                                     drop peek(rng: r)
                                     return
                                 """,
            expectedErrorSubstring: "only reads it. Take a write reference ('Modifying[Random]') to call it.");
    }
}
