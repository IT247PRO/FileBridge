using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FileBridge.Admin.TagHelpers;

/// <summary>
/// &lt;page-header title="..." description="..."&gt;optional action buttons&lt;/page-header&gt;
/// Renders the page's title/description on the left and any child content (buttons, links,
/// small forms) as a right-aligned actions row, so every view shares one heading layout.
/// </summary>
[HtmlTargetElement("page-header")]
public sealed class PageHeaderTagHelper : TagHelper
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var actions = await output.GetChildContentAsync();

        output.TagName = "header";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "fb-page-header");

        output.Content.AppendHtml("<div class=\"fb-page-header-text\">");
        output.Content.AppendHtml("<h1>").Append(Title).AppendHtml("</h1>");
        if (!string.IsNullOrWhiteSpace(Description))
        {
            output.Content.AppendHtml("<p class=\"muted\">").Append(Description).AppendHtml("</p>");
        }
        output.Content.AppendHtml("</div>");

        if (!actions.IsEmptyOrWhiteSpace)
        {
            output.Content.AppendHtml("<div class=\"fb-page-header-actions\">").AppendHtml(actions).AppendHtml("</div>");
        }
    }
}
