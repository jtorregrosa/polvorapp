using System.Text;
using System.Text.Encodings.Web;

namespace PolvorApp.SharedKernel.Email;

/// <summary>
/// The minimal HTML alternative of a plain-text email (NFR-11): one paragraph per blank-line block,
/// every line HTML-encoded, and a line that is exactly one of the email's links becomes an anchor. No
/// styles, images, remote content or tracking.
/// </summary>
public static class PlainTextHtml
{
    public static string Render(string text, IReadOnlyCollection<Uri> links)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(links);
        var anchors = links.Select(link => link.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        var html = new StringBuilder("<!doctype html><html><body>");
        foreach (var paragraph in text.Split("\n\n"))
        {
            html.Append("<p>");
            var lines = paragraph.Split('\n').Select(line => anchors.Contains(line)
                ? $"<a href=\"{HtmlEncoder.Default.Encode(line)}\">{HtmlEncoder.Default.Encode(line)}</a>"
                : HtmlEncoder.Default.Encode(line));
            html.Append(string.Join("<br>", lines)).Append("</p>");
        }

        return html.Append("</body></html>").ToString();
    }
}
