using arch_forge_backend.Application.Export;
using Xunit;

namespace arch_forge_backend.Application.Tests.Export;

/// <summary>
/// V5 plan técnico §6.4, ajuste 4. Node names are free text, and these are the characters that let
/// a user break their own document by naming a service the way services are commonly named.
/// </summary>
public class MarkdownEscapeTests
{
    [Fact]
    public void A_pipe_in_a_cell_is_escaped()
    {
        Assert.Equal("Orders \\| Payments", MarkdownEscape.TableCell("Orders | Payments"));
    }

    [Fact]
    public void A_newline_in_a_cell_becomes_a_space()
    {
        Assert.Equal("Orders API", MarkdownEscape.TableCell("Orders\r\nAPI"));
    }

    [Fact]
    public void Brackets_in_a_mermaid_label_become_parentheses()
    {
        Assert.Equal("API (v2)", MarkdownEscape.MermaidLabel("API [v2]"));
    }

    [Fact]
    public void A_quote_in_a_mermaid_label_becomes_an_entity()
    {
        // The label is always emitted quoted, so this is the character that would end it early.
        Assert.Equal("the #quot;fast#quot; path", MarkdownEscape.MermaidLabel("the \"fast\" path"));
    }

    [Fact]
    public void A_pipe_in_a_mermaid_label_is_replaced()
    {
        // A pipe would be read as the start of an edge label.
        Assert.Equal("Orders / Payments", MarkdownEscape.MermaidLabel("Orders | Payments"));
    }

    [Theory]
    [InlineData("Orders API", "OrdersAPI")]
    [InlineData("Órdenes-Núcleo", "rdenesNcleo")]
    [InlineData("API [v2]", "APIv2")]
    public void An_identifier_keeps_only_what_mermaid_accepts(string name, string expected)
    {
        Assert.Equal(expected, MarkdownEscape.MermaidIdentifier(name));
    }

    [Fact]
    public void An_identifier_never_starts_with_a_digit()
    {
        Assert.Equal("n2FA", MarkdownEscape.MermaidIdentifier("2FA"));
    }

    [Fact]
    public void A_name_with_nothing_usable_still_produces_an_identifier()
    {
        // Legal in the product - and an empty identifier is not legal in Mermaid.
        Assert.Equal("n", MarkdownEscape.MermaidIdentifier("¿?¡!"));
    }
}
