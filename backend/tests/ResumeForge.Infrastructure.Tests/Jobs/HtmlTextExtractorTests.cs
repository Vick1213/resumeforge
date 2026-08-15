using ResumeForge.Infrastructure.Jobs;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Jobs;

/// <summary>Tests for <see cref="HtmlTextExtractor"/>, purely against fixture HTML strings (no network).</summary>
public sealed class HtmlTextExtractorTests
{
    [Fact]
    public void ExtractReadableText_strips_script_style_nav_and_footer_blocks()
    {
        const string html = """
            <html><head><style>body { color: red; }</style></head>
            <body>
            <nav>Home | About | Contact</nav>
            <script>trackPageView();</script>
            <main><h1>Senior Backend Engineer</h1><p>We build distributed systems.</p></main>
            <footer>Copyright 2026</footer>
            </body></html>
            """;

        var text = HtmlTextExtractor.ExtractReadableText(html);

        text.ShouldContain("Senior Backend Engineer");
        text.ShouldContain("We build distributed systems.");
        text.ShouldNotContain("Home | About | Contact");
        text.ShouldNotContain("trackPageView");
        text.ShouldNotContain("color: red");
        text.ShouldNotContain("Copyright 2026");
    }

    [Fact]
    public void ExtractReadableText_decodes_html_entities()
    {
        const string html = "<p>Bachelor&rsquo;s degree in CS &amp; 5+ years&nbsp;experience &lt;required&gt;</p>";

        var text = HtmlTextExtractor.ExtractReadableText(html);

        text.ShouldContain("Bachelor’s degree in CS & 5+ years experience <required>");
    }

    [Fact]
    public void ExtractReadableText_collapses_whitespace_and_trims()
    {
        const string html = "<div>   \n\n  Hello   \t World  \n  </div>";

        var text = HtmlTextExtractor.ExtractReadableText(html);

        text.ShouldBe("Hello World");
    }

    [Fact]
    public void ExtractReadableText_strips_html_comments()
    {
        const string html = "<p>Visible text.</p><!-- an internal note that should not leak -->";

        var text = HtmlTextExtractor.ExtractReadableText(html);

        text.ShouldContain("Visible text.");
        text.ShouldNotContain("internal note");
    }

    [Fact]
    public void ExtractMetadata_reads_a_schema_org_JobPosting_json_ld_block()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org/",
              "@type": "JobPosting",
              "title": "Senior Backend Engineer",
              "hiringOrganization": { "@type": "Organization", "name": "Acme Corp" },
              "jobLocation": {
                "@type": "Place",
                "address": { "@type": "PostalAddress", "addressLocality": "Seattle", "addressRegion": "WA" }
              }
            }
            </script>
            </head><body></body></html>
            """;

        var metadata = HtmlTextExtractor.ExtractMetadata(html);

        metadata.Title.ShouldBe("Senior Backend Engineer");
        metadata.Company.ShouldBe("Acme Corp");
        metadata.Location.ShouldBe("Seattle, WA");
    }

    [Fact]
    public void ExtractMetadata_handles_a_json_ld_array_containing_a_JobPosting()
    {
        const string html = """
            <script type="application/ld+json">
            [
              { "@type": "BreadcrumbList" },
              { "@type": "JobPosting", "title": "Platform Engineer", "hiringOrganization": { "name": "Widget Co" } }
            ]
            </script>
            """;

        var metadata = HtmlTextExtractor.ExtractMetadata(html);

        metadata.Title.ShouldBe("Platform Engineer");
        metadata.Company.ShouldBe("Widget Co");
    }

    [Fact]
    public void ExtractMetadata_falls_back_to_open_graph_tags_when_no_json_ld_is_present()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="Staff Engineer, Platform">
            <meta property="og:site_name" content="Example Inc">
            </head><body></body></html>
            """;

        var metadata = HtmlTextExtractor.ExtractMetadata(html);

        metadata.Title.ShouldBe("Staff Engineer, Platform");
        metadata.Company.ShouldBe("Example Inc");
        metadata.Location.ShouldBeNull();
    }

    [Fact]
    public void ExtractMetadata_ignores_malformed_json_ld_without_throwing()
    {
        const string html = """<script type="application/ld+json">{ this is not valid json </script>""";

        var metadata = HtmlTextExtractor.ExtractMetadata(html);

        metadata.Title.ShouldBeNull();
        metadata.Company.ShouldBeNull();
    }

    [Fact]
    public void ExtractMetadata_returns_all_nulls_when_nothing_is_present()
    {
        var metadata = HtmlTextExtractor.ExtractMetadata("<html><body><p>Just a plain page.</p></body></html>");

        metadata.Title.ShouldBeNull();
        metadata.Company.ShouldBeNull();
        metadata.Location.ShouldBeNull();
    }
}
