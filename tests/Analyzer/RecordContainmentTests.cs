using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for record containment.
/// </summary>
public class RecordContainmentTests
{
    #region Valid Record MemberVariables (no errors expected)

    /// <summary>
    /// Verifies semantic analysis behavior for record with primitive member variables without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithPrimitiveMemberVariables_NoErrors()
    {
        string source = """
                        record Point
                            x: S32
                            y: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for record with record member variable without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithRecordMemberVariable_NoErrors()
    {
        string source = """
                        record Inner
                            value: S32
                        record Outer
                            inner: Inner
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for record with choice member variable without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithChoiceMemberVariable_NoErrors()
    {
        string source = """
                        choice Color
                            RED
                            GREEN
                            BLUE
                        record Pixel
                            x: S32
                            y: S32
                            color: Color
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for generic record with type parameter without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_GenericRecordWithTypeParameter_NoErrors()
    {
        // Generic type parameters are validated at instantiation time, not definition time
        string source = """
                        record Container[T]
                            value: T
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for generic record multiple type params without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_GenericRecordMultipleTypeParams_NoErrors()
    {
        string source = """
                        record Pair[K, V]
                            key: K
                            value: V
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for record with retained field without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithRetainedField_NoErrors()
    {
        string source = """
                        entity Node
                            value: S32
                        record Handle
                            ref: Retained[Node]
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for record with shared field without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithSharedField_NoErrors()
    {
        string source = """
                        entity Node
                            value: S32
                        record Handle
                            ref: Guarded[Node]
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for record with hijacked field without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithHijackedField_NoErrors()
    {
        string source = """
                        entity Node
                            value: S32
                        record RawHandle
                            ptr: Hijacked[Node]
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for nested record with retained field without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_NestedRecordWithRetainedField_NoErrors()
    {
        string source = """
                        entity Node
                            value: S32
                        record Inner
                            ref: Retained[Node]
                        record Outer
                            inner: Inner
                            count: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    #endregion

    #region Invalid Record MemberVariables (errors expected)

    /// <summary>
    /// Verifies a record MAY contain an entity-typed field. Entities are reference (pointer-shaped)
    /// types, so the field stores a reference — only the scoped access tokens (Viewing/Modifying)
    /// are rejected as record members (see the token tests below).
    /// </summary>
    [Fact]
    public void Analyze_RecordWithEntityReferenceField_NoError()
    {
        string source = """
                        entity User
                            name: Text
                        record BadRecord
                            user: User
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    /// <summary>
    /// A record may hold a link to an entity.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithReadLinkField_Builds()
    {
        // A record holds links to entities: a read link is copied with the record.
        string source = """
                        entity Node
                            value: S32
                        record Pointer
                            view: %Node
                        """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    /// <summary>
    /// A record may hold a write link to an entity.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithWriteLinkField_Builds()
    {
        string source = """
                        entity Node
                            value: S32
                        record Pointer
                            writer: @Node
                        """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    #endregion

    #region Multiple token fields both rejected

    /// <summary>
    /// A reference to a record kept in a record is reported for each member variable.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithReferenceToRecord_ReportsBoth()
    {
        // A record is a value with no identity, so nothing points at one from storage (RV-S661).
        string source = """
                        record Point
                            x: S32
                        record Pair
                            reader: %Point
                            writer: @Point
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Equal(expected: 2, actual: result.Errors.Count(predicate: e =>
            e.Code == SemanticDiagnosticCode.RecordReferenceKept));
    }

    #endregion

    #region Variant field in record

    /// <summary>
    /// Verifies that a record can contain a variant-typed field.
    /// </summary>
    [Fact]
    public void Analyze_RecordWithVariantField_NoError()
    {
        string source = """
                        variant Status
                            S32
                            Text

                        record Result
                            status: Status
                            code: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordContainsNonValueType);
    }

    #endregion
}
