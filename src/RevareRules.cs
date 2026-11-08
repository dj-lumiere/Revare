using System.Reflection;
using Builder.Frontends;
using Builder.Tokenizer;
using Builder.Verification;
using Revare.Checks;
using Revare.Lexer;
using TypeModel.Enums;

namespace Revare;

/// <summary>
/// Revare's rules: single ownership checked at build time, memory-unsafe surface behind
/// <c>dangerous</c>/<c>danger</c>, module-level <c>global</c>s only of types threads may share, and fixed-width numeric
/// defaults.
/// </summary>
public sealed class RevareRules : LanguageRules
{
    /// <summary>The one instance the front end registers.</summary>
    public static readonly RevareRules Instance = new();

    private RevareRules()
    {
    }

    public override Language Language => Language.Revare;
    public override string Name => "Revare";
    public override string ToolName => "revare";
    public override string Version => VersionOf(assembly: typeof(RevareRules).Assembly);
    public override string FileExtension => Builder.Frontends.SourceExtensions.Revare;
    public override IReadOnlyList<string> PreviousFileExtensions => Builder.Frontends.SourceExtensions.PreviousRevare;
    public override string ShortName => TypeModel.Realms.Shared;

    public override List<Token> Tokenize(string source, string fileName)
    {
        return new SourceLexer(source: source, fileName: fileName, language: Language.Revare, keywords: RevareKeywords.Table).Tokenize();
    }

    /// <inheritdoc/>
    public override LanguageServerProfile LanguageServer { get; } = new RevareLanguageServer();

    /// <inheritdoc/>
    public override (List<Token> Tokens, List<CommentTrivia> Comments) TokenizeWithComments(string source,
        string fileName)
    {
        var lexer = new SourceLexer(source: source, fileName: fileName, language: Language.Revare, keywords: RevareKeywords.Table);
        return (lexer.Tokenize(), lexer.Comments);
    }

    public override bool AllowsUnsafeCode => true;
    public override bool HasNothingStatement => true;
    public override bool HasModuleGlobals => true;
    public override bool HasTargetDirectives => true;
    public override bool DefaultsToArbitraryPrecision => false;
    public override bool HasDataSizeQuery => true;

    public override bool EntitiesAreShared => false;
    public override bool ChecksOwnership => true;
    public override bool ChecksAccessTokens => true;
    public override bool ChecksReadonly => true;
    public override bool MarksDroppedResults => true;
    public override bool ChecksShapeAtRunTime => false;
    public override bool ZeroIsEmptyPlace => false;

    /// <summary>
    /// Writes the builder's names of the references the way a Revare program spells them: <c>Viewing[Node]</c> reads
    /// <c>%Node</c>, <c>Modifying[Node]</c> reads <c>@Node</c>, and <c>Optional[Modifying[Node]]</c> reads <c>@Node?</c>,
    /// at any depth (<c>List[Modifying[Node]?]</c> reads <c>List[@Node?]</c>).
    /// </summary>
    public override string SurfaceText(string text)
    {
        if (!text.Contains(value: "Viewing[", comparisonType: StringComparison.Ordinal) &&
            !text.Contains(value: "Modifying[", comparisonType: StringComparison.Ordinal))
        {
            return text;
        }

        text = ReplaceWrapper(text: text, name: "Optional[", rewrite: inner =>
            inner.StartsWith(value: "Viewing[", comparisonType: StringComparison.Ordinal) ||
            inner.StartsWith(value: "Modifying[", comparisonType: StringComparison.Ordinal)
                ? inner + "?"
                : null);
        text = ReplaceWrapper(text: text, name: "Viewing[", rewrite: inner => "%" + Signed(inner: inner));
        return ReplaceWrapper(text: text, name: "Modifying[", rewrite: inner => "@" + Signed(inner: inner));
    }

    /// <summary>The type after a reference's sign: an optional or a variant written in place goes in parentheses, since
    /// the sign binds to the type right after it (<c>%(Node?)</c> is a reference to an optional, <c>%Node?</c> an
    /// optional reference).</summary>
    private static string Signed(string inner)
    {
        return inner.EndsWith(value: '?') || inner.Contains(value: " | ") ? $"({inner})" : inner;
    }

    /// <summary>Replaces each <paramref name="name"/><c>...]</c> in <paramref name="text"/> (with any module qualifier
    /// written before it) by what <paramref name="rewrite"/> makes of what it wraps, leaving it as it is where
    /// <paramref name="rewrite"/> gives null.</summary>
    private static string ReplaceWrapper(string text, string name, Func<string, string?> rewrite)
    {
        int from = 0;
        while (true)
        {
            int at = text.IndexOf(value: name, startIndex: from, comparisonType: StringComparison.Ordinal);
            if (at < 0)
            {
                return text;
            }

            from = at + 1;
            if (at > 0 && (char.IsLetterOrDigit(c: text[index: at - 1]) || text[index: at - 1] == '_'))
            {
                continue;
            }

            int open = at + name.Length - 1;
            int close = MatchingBracket(text: text, open: open);
            if (close < 0)
            {
                return text;
            }

            string inner = ReplaceWrapper(text: text[(open + 1)..close], name: name, rewrite: rewrite);
            if (rewrite(arg: inner) is not { } replaced)
            {
                text = text[..(open + 1)] + inner + text[close..];
                continue;
            }

            int start = QualifierStart(text: text, at: at);
            text = text[..start] + replaced + text[(close + 1)..];
            from = start;
        }
    }

    /// <summary>Where the module qualifier written right before the name at <paramref name="at"/> starts
    /// (<c>System.</c>, <c>System/Memory.</c>), or <paramref name="at"/> when there is none.</summary>
    private static int QualifierStart(string text, int at)
    {
        int start = at;
        while (start > 0 && text[index: start - 1] == '.')
        {
            int word = start - 1;
            while (word > 0 && (char.IsLetterOrDigit(c: text[index: word - 1]) || text[index: word - 1] is '_' or '/'))
            {
                word--;
            }

            if (word == start - 1)
            {
                break;
            }

            start = word;
        }

        return start;
    }

    /// <summary>The <c>]</c> that closes the <c>[</c> at <paramref name="open"/>, or -1 when it is unbalanced.</summary>
    private static int MatchingBracket(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[index: i] == '[')
            {
                depth++;
            }
            else if (text[index: i] == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    public override bool RequiresLateinitForDeferredInit => true;
    public override bool RequiresEnterableForUsing => true;
    public override bool RewritesDisplayWrapperArguments => true;

    public override string ScriptVariableAdvice(string name)
    {
        return $"Pass it in as an argument, or declare it as 'global {name}: <Type> = ...'. A global can change, and a " +
               "routine that runs on another thread uses an Unbroken or a Guarded one.";
    }

    public override bool SynthesizeBeforeAnalysis(List<(SyntaxTree.Program Program, string FilePath)> files,
        Action<Builder.Diagnostics.SemanticDiagnosticCode, string, SyntaxTree.SourceLocation> report)
    {
        return Passes.GlobalInitPass.Run(orderedFiles: files, report: report);
    }

    /// <summary>Access tokens are Revare's: their lifetimes are checked here (see
    /// <see cref="TokenLifetimeChecker"/>).</summary>
    internal override IUserBodyCheck CreateUserBodyCheck(DiagnosticReporter report)
    {
        return new TokenLifetimeChecker(report: report);
    }

    /// <summary>The informational version of <paramref name="assembly"/> (its csproj <c>&lt;Version&gt;</c>)
    /// without the <c>+commit</c> suffix.</summary>
    internal static string VersionOf(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                         assembly.GetName().Version?.ToString() ?? "0.0.0";
        int plus = version.IndexOf(value: '+');
        return plus > 0
            ? version[..plus]
            : version;
    }
}
