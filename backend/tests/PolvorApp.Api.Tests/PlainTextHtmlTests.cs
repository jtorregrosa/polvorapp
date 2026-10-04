using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Tests;

/// <summary>The minimal HTML alternative of plain-text emails (NFR-11; add-notifications, design D7).</summary>
public sealed class PlainTextHtmlTests
{
    [Fact]
    public void Paragraphs_lines_and_links_are_rendered_and_everything_else_is_encoded()
    {
        var link = new Uri("https://polvorapp.example/orders?a=1&b=2");

        var html = PlainTextHtml.Render($"Hola, <Persona>:\n\nPrimera línea\n{link.AbsoluteUri}", [link]);

        Assert.Equal(
            "<!doctype html><html><body><p>Hola, &lt;Persona&gt;:</p><p>Primera l&#xED;nea<br>"
            + "<a href=\"https://polvorapp.example/orders?a=1&amp;b=2\">https://polvorapp.example/orders?a=1&amp;b=2</a></p></body></html>",
            html);
    }

    [Fact]
    public void A_link_inside_a_sentence_stays_text()
    {
        var link = new Uri("https://polvorapp.example/");

        var html = PlainTextHtml.Render($"Ver {link.AbsoluteUri} ahora", [link]);

        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }
}
