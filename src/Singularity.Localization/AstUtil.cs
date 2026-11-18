using System.Globalization;
using Linguini.Syntax.Ast;

namespace Singularity.Localization;

/// <summary>Walks over Fluent AST patterns: the base of checks L3, L4, L6 and of the pseudo-locale.</summary>
static class AstUtil
{
    /// <summary>The value (attribute name null) and every attribute pattern of an entity.</summary>
    public static IEnumerable<(string? Attribute, Pattern Pattern)> Patterns(IEntry entry)
    {
        switch (entry)
        {
            case AstMessage m:
                if (m.Value is not null)
                    yield return (null, m.Value);
                foreach (Linguini.Syntax.Ast.Attribute a in m.Attributes)
                    yield return (a.Id.ToString(), a.Value);
                break;
            case AstTerm t:
                yield return (null, t.Value);
                foreach (Linguini.Syntax.Ast.Attribute a in t.Attributes)
                    yield return (a.Id.ToString(), a.Value);
                break;
        }
    }

    public static string IdOf(IEntry entry) => entry switch
    {
        AstMessage m => m.Id.ToString(),
        AstTerm t => "-" + t.Id,
        _ => throw new ArgumentException("not a message or term", nameof(entry)),
    };

    /// <summary>
    /// Variables and functions used by a pattern, selector expressions included: a caller that does not
    /// supply a selector variable cannot drive the selector. `$case` is not counted in terms only: NAME supplies it there.
    /// </summary>
    public static void CollectUsage(Pattern pattern, SortedSet<string> variables, SortedSet<string> functions, bool inTerm = false)
    {
        foreach (IPatternElement e in pattern.Elements)
        {
            if (e is Placeable p)
                CollectExpression(p.Expression, variables, functions, inTerm);
        }
    }

    static void CollectExpression(IExpression x, SortedSet<string> variables, SortedSet<string> functions, bool inTerm)
    {
        switch (x)
        {
            case SelectExpression s:
                CollectExpression(s.Selector, variables, functions, inTerm);
                foreach (Variant v in s.Variants)
                    CollectUsage(v.Value, variables, functions, inTerm);
                break;
            case VariableReference v:
                if (!inTerm || v.Id.ToString() != "case")
                    variables.Add(v.Id.ToString());
                break;
            case FunctionReference f:
                // GENDER only feeds selectors, which may differ between languages.
                if (f.Id.ToString() != "GENDER")
                    functions.Add(f.Id.ToString());
                foreach (IInlineExpression a in f.Arguments.PositionalArgs)
                    CollectExpression(a, variables, functions, inTerm);
                foreach (NamedArgument n in f.Arguments.NamedArgs)
                    CollectExpression(n.Value, variables, functions, inTerm);
                break;
            case TermReference t:
                if (t.Arguments is { } ta)
                {
                    foreach (IInlineExpression a in ta.PositionalArgs)
                        CollectExpression(a, variables, functions, inTerm);
                    foreach (NamedArgument n in ta.NamedArgs)
                        CollectExpression(n.Value, variables, functions, inTerm);
                }
                break;
            case Placeable p:
                CollectExpression(p.Expression, variables, functions, inTerm);
                break;
        }
    }

    /// <summary>Every select expression in a pattern, nested ones included.</summary>
    public static IEnumerable<SelectExpression> Selects(Pattern pattern)
    {
        foreach (IPatternElement e in pattern.Elements)
        {
            if (e is not Placeable p)
                continue;
            foreach (SelectExpression s in SelectsIn(p.Expression))
                yield return s;
        }
    }

    static IEnumerable<SelectExpression> SelectsIn(IExpression x)
    {
        if (x is SelectExpression s)
        {
            yield return s;
            foreach (Variant v in s.Variants)
                foreach (SelectExpression inner in Selects(v.Value))
                    yield return inner;
        }
        else if (x is Placeable p)
        {
            foreach (SelectExpression inner in SelectsIn(p.Expression))
                yield return inner;
        }
    }

    /// <summary>Name of the variable that drives a selector: `$n` or `NUMBER($n, ...)`; null otherwise.</summary>
    public static string? SelectorVariable(IInlineExpression selector) => selector switch
    {
        VariableReference v => v.Id.ToString(),
        FunctionReference { Id: var id } f when id.ToString() == "NUMBER" && f.Arguments.PositionalArgs.Count > 0
            && f.Arguments.PositionalArgs[0] is VariableReference v => v.Id.ToString(),
        _ => null,
    };

    /// <summary>
    /// Longest rendering of a pattern in text elements (graphemes), over all selector variants.
    /// <paramref name="nameLength"/> stands for NAME(...) and term references, 8 for NUMBER (ADR-0018 §3.2 L6).
    /// </summary>
    public static int MaxLength(Pattern pattern, int nameLength)
    {
        int total = 0;
        foreach (IPatternElement e in pattern.Elements)
        {
            switch (e)
            {
                case TextLiteral t:
                    total += Graphemes(t.ToString());
                    break;
                case Placeable p:
                    total += ExpressionLength(p.Expression, nameLength);
                    break;
            }
        }
        return total;
    }

    static int ExpressionLength(IExpression x, int nameLength)
    {
        switch (x)
        {
            case SelectExpression s:
                int best = 0;
                foreach (Variant v in s.Variants)
                    best = Math.Max(best, MaxLength(v.Value, nameLength));
                return best;
            case TextLiteral t:
                return Graphemes(t.ToString());
            case NumberLiteral n:
                return n.ToString().Length;
            case FunctionReference f:
                return f.Id.ToString() switch { "NUMBER" => 8, "DATE" => 20, "NAME" => nameLength, _ => 0 };
            case TermReference:
                return nameLength;
            case Placeable p:
                return ExpressionLength(p.Expression, nameLength);
            default:
                return 0;
        }
    }

    public static int Graphemes(string s) => new StringInfo(s).LengthInTextElements;

    public static bool IsPluralCategory(string key) => key is "zero" or "one" or "two" or "few" or "many" or "other";
}
