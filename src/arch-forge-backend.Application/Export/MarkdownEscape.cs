using System.Text;

namespace arch_forge_backend.Application.Export;

/// <summary>
/// V5 plan técnico §6.4, ajuste 4. Node names are free text written by the user, and two of them
/// break the document without anybody doing anything unusual: a node called <c>Orders | Payments</c>
/// breaks every Markdown table, and one called <c>API [v2]</c> breaks the Mermaid block.
///
/// <strong>This is a correctness requirement, not a cosmetic one.</strong> It is the only way a
/// user can break their own export, and it is reachable by naming a service the way services are
/// commonly named.
///
/// Three destinations, three sets of dangerous characters. The finding texts written by the engine
/// (<c>Message</c>, <c>Explanation</c>, <c>Suggestion</c>) are deliberately <em>not</em> escaped:
/// they travel in paragraphs rather than cells, and escaping them would break any formatting the
/// analyzer may want to use.
/// </summary>
public static class MarkdownEscape
{
    /// <summary>
    /// A Markdown table cell. The pipe is the only character that ends a cell, and a newline ends
    /// the whole row - both have to go.
    /// </summary>
    public static string TableCell(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            switch (c)
            {
                case '|':
                    builder.Append("\\|");
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The text inside a Mermaid node label. The label is always emitted quoted, so the quote is
    /// the character that has to become an entity; brackets and braces are replaced rather than
    /// escaped because an unbalanced one breaks the parser and no reader would notice the
    /// difference between <c>API [v2]</c> and <c>API (v2)</c> in a diagram.
    /// </summary>
    public static string MermaidLabel(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("#quot;");
                    break;
                case '[':
                case '{':
                    builder.Append('(');
                    break;
                case ']':
                case '}':
                    builder.Append(')');
                    break;
                case '|':
                    builder.Append('/');
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The identifier part of a Mermaid node: <c>[A-Za-z0-9_]</c> only, never empty, never starting
    /// with a digit. Collisions are resolved by <see cref="MermaidEmitter"/>, which is the only
    /// place that can see all the names at once.
    /// </summary>
    public static string MermaidIdentifier(string? name)
    {
        var builder = new StringBuilder();

        foreach (var c in name ?? "")
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' || c == '_')
                builder.Append(c);
        }

        // A name made entirely of accents or symbols is legal in the product and would produce an
        // empty identifier, which is not legal in Mermaid.
        if (builder.Length == 0)
            return "n";

        if (builder[0] is >= '0' and <= '9')
            builder.Insert(0, 'n');

        return builder.ToString();
    }
}
