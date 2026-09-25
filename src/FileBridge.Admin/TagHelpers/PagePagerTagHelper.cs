using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Primitives;
using System.Text;

namespace FileBridge.Admin.TagHelpers;

/// <summary>
/// &lt;page-pager total="@ViewBag.Total" page="@ViewBag.Page" page-size="50"&gt;&lt;/page-pager&gt;
/// Renders a "N total row(s), page X of Y" summary plus Previous/Next links that preserve every
/// existing query-string parameter (filters like jobId/status/entity) except "page" itself.
/// </summary>
[HtmlTargetElement("page-pager")]
public sealed class PagePagerTagHelper : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "fb-pager");
        output.Attributes.SetAttribute("aria-label", "Pagination");

        var totalPages = Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));

        output.Content.AppendHtml("<span class=\"fb-pager-summary\">")
            .Append($"{Total} total row(s), page {Page} of {totalPages}")
            .AppendHtml("</span>");

        output.Content.AppendHtml(" <span class=\"fb-pager-links\">");
        AppendLink(output, Page - 1, "« Previous", Page > 1);
        output.Content.AppendHtml(" ");
        AppendLink(output, Page + 1, "Next »", Page < totalPages);
        output.Content.AppendHtml("</span>");
    }

    private void AppendLink(TagHelperOutput output, int targetPage, string text, bool enabled)
    {
        if (!enabled)
        {
            output.Content.AppendHtml("<span class=\"muted\">").Append(text).AppendHtml("</span>");
            return;
        }
        output.Content.AppendHtml("<a class=\"fb-textlink\" href=\"").AppendHtml(BuildUrl(targetPage)).AppendHtml("\">").Append(text).AppendHtml("</a>");
    }

    private string BuildUrl(int targetPage)
    {
        var query = new Dictionary<string, StringValues>();
        foreach (var kv in ViewContext.HttpContext.Request.Query) query[kv.Key] = kv.Value;
        query["page"] = targetPage.ToString();

        var sb = new StringBuilder(ViewContext.HttpContext.Request.Path.Value);
        sb.Append('?');
        var first = true;
        foreach (var (key, values) in query)
        {
            foreach (var value in values)
            {
                if (!first) sb.Append('&');
                sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value ?? ""));
                first = false;
            }
        }
        return sb.ToString();
    }
}
