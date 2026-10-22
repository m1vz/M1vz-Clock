using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using M1vClock.App.Localization;

namespace M1vClock.App.Services.Reports;

public static class ReportRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private const string Style =
        ":root{color-scheme:light dark;--bg:#f7f7f8;--card:#fff;--text:#1d1d1f;--muted:#6e6e73;--line:#e3e3e8}" +
        "@media(prefers-color-scheme:dark){:root{--bg:#161618;--card:#202023;--text:#f5f5f7;--muted:#a1a1a6;--line:#38383c}}" +
        "body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 -apple-system,'Segoe UI',system-ui,sans-serif}" +
        "main{max-width:820px;margin:0 auto;padding:32px 16px}" +
        "h1{font-size:28px;margin:0 0 4px}h2{font-size:18px;margin:0 0 12px}" +
        ".sub{color:var(--muted);margin:0 0 24px}" +
        "section{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px 20px;margin:0 0 16px}" +
        "dl{display:grid;grid-template-columns:minmax(140px,240px) 1fr;gap:6px 16px;margin:0}" +
        "dt{color:var(--muted)}dd{margin:0;word-break:break-word}" +
        ".note{color:var(--muted);margin:10px 0 0}footer{color:var(--muted);font-size:13px;margin-top:24px}";

    public static string Html(ReportDocument document, IReadOnlySet<string> include)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(include);
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"").Append(Escape(Loc.Culture.Name)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        html.Append("<title>").Append(Escape(Loc.Get("Diag.Report.Title"))).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n<main>\n");
        html.Append("<h1>").Append(Escape(Loc.Get("Diag.Report.Title"))).Append("</h1>\n");
        html.Append("<p class=\"sub\">").Append(Escape(Header(document))).Append("</p>\n");
        foreach (var section in document.Sections.Where(s => include.Contains(s.Id) && !s.IsEmpty))
        {
            html.Append("<section>\n<h2>").Append(Escape(section.Title)).Append("</h2>\n");
            if (section.Rows.Count > 0)
            {
                html.Append("<dl>\n");
                foreach (var row in section.Rows)
                {
                    html.Append("<dt>").Append(Escape(row.Label)).Append("</dt><dd>").Append(Escape(row.Value)).Append("</dd>\n");
                }

                html.Append("</dl>\n");
            }

            foreach (var note in section.Notes)
            {
                html.Append("<p class=\"note\">").Append(Escape(note)).Append("</p>\n");
            }

            html.Append("</section>\n");
        }

        html.Append("<footer>").Append(Escape(Loc.Get("Diag.Report.Footer"))).Append("</footer>\n</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    public static string Text(ReportDocument document, IReadOnlySet<string> include)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(include);
        var text = new StringBuilder();
        text.AppendLine(Loc.Get("Diag.Report.Title"));
        text.AppendLine(Header(document));
        foreach (var section in document.Sections.Where(s => include.Contains(s.Id) && !s.IsEmpty))
        {
            text.AppendLine();
            text.Append("## ").AppendLine(section.Title);
            foreach (var row in section.Rows)
            {
                text.Append("- ").Append(row.Label).Append(": ").AppendLine(row.Value);
            }

            foreach (var note in section.Notes)
            {
                text.AppendLine(note);
            }
        }

        text.AppendLine();
        text.AppendLine(Loc.Get("Diag.Report.Footer"));
        return text.ToString();
    }

    private static string Escape(string text) => Encoder.Encode(text);

    public static string Header(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var line = Loc.Format("Diag.Report.Generated", document.At.ToLocalTime().ToString("g", Loc.Culture), document.AppVersion);
        return string.IsNullOrWhiteSpace(document.ComputerName) ? line : $"{line} · {document.ComputerName}";
    }

    public static string FileName(DateTimeOffset at) => Loc.Format("Diag.Report.File.Name", at.ToLocalTime().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture));
}
