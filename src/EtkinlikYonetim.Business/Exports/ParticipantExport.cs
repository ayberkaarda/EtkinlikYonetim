using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Exports;

/// <summary>One participant of an exported list: what the admin list shows, plus the local check-in time.</summary>
public sealed record ExportRow(string FullName, string MaskedNationalId, string Email, string State, string CheckedInAt, RegistrationStatus Status);

/// <summary>
/// The participant and check-in list of one event as it is exported. National ids are masked and ticket
/// codes are not part of it at all: an exported file leaves the application's control.
/// </summary>
public sealed record ParticipantList(
    string EventName,
    DateOnly EventDate,
    bool EventCancelled,
    IReadOnlyList<ExportRow> Rows,
    DateTimeOffset CreatedAt)
{
    public int Seats => Rows.Count(r => r.Status == RegistrationStatus.Active);

    public int CheckedIn => Rows.Count(r => r.Status == RegistrationStatus.Active && r.CheckedInAt.Length > 0);

    public int Waiting => Rows.Count(r => r.Status == RegistrationStatus.Waiting);
}

/// <summary>A file to save: a suggested name and its content.</summary>
public sealed record ExportFile(string SuggestedName, byte[] Content);

/// <summary>Writes a <see cref="ParticipantList"/> as CSV or as a printable HTML page.</summary>
public static class ParticipantExport
{
    internal static readonly string[] Headers = ["Ad Soyad", "T.C. Kimlik No (maskeli)", "E-posta", "Durum", "Giriş zamanı"];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Static and self-contained: the page loads nothing, runs nothing and allows only this style block,
    // pinned by its hash in the Content-Security-Policy.
    private const string Style =
        "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#000;background:#fff}"
        + "h1{font-size:20px;margin:0 0 4px}p{margin:2px 0 12px}"
        + "table{border-collapse:collapse;width:100%;font-size:13px}"
        + "th,td{border:1px solid #444;padding:4px 6px;text-align:left;vertical-align:top}"
        + "th{background:#e8e8e8}@media print{body{margin:0}th{background:#fff}}";

    /// <summary>
    /// CSV for spreadsheets: UTF-8 with a byte order mark (so Excel reads Turkish letters), ";" between the
    /// fields (the Turkish list separator), every field quoted, and a leading "'" before any field that a
    /// spreadsheet would run as a formula (=, +, -, @, tab, carriage return).
    /// </summary>
    public static byte[] ToCsv(ParticipantList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var text = new StringBuilder();
        AppendCsvLine(text, Headers);
        foreach (var row in list.Rows)
        {
            AppendCsvLine(text, [row.FullName, row.MaskedNationalId, row.Email, row.State, row.CheckedInAt]);
        }

        return WithByteOrderMark(text.ToString());
    }

    /// <summary>
    /// A printable HTML page: no script, a Content-Security-Policy that allows nothing but its own style
    /// block, and every value HTML encoded.
    /// </summary>
    public static byte[] ToHtml(ParticipantList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var title = "Katılımcı Listesi - " + list.EventName;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\r\n<html lang=\"tr\">\r\n<head>\r\n<meta charset=\"utf-8\">\r\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src '")
            .Append(StyleHash)
            .Append("'; base-uri 'none'; form-action 'none'\">\r\n")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">\r\n")
            .Append("<title>").Append(Html(title)).Append("</title>\r\n")
            .Append("<style>").Append(Style).Append("</style>\r\n</head>\r\n<body>\r\n")
            .Append("<h1>").Append(Html(title)).Append("</h1>\r\n")
            .Append("<p>")
            .Append(Html(list.EventDate.ToString("dd.MM.yyyy", Turkish)))
            .Append(list.EventCancelled ? " - İptal edildi" : string.Empty)
            .Append(Html(string.Create(Turkish, $" - Kayıtlı: {list.Seats}, giriş yapan: {list.CheckedIn}, bekleme listesi: {list.Waiting}")))
            .Append(" - Oluşturma: ").Append(Html(list.CreatedAt.ToString("dd.MM.yyyy HH:mm", Turkish)))
            .Append("</p>\r\n");

        if (list.Rows.Count == 0)
        {
            html.Append("<p>Bu etkinlikte kayıtlı katılımcı yok.</p>\r\n");
        }
        else
        {
            html.Append("<table>\r\n<thead><tr><th>#</th>");
            foreach (var header in Headers)
            {
                html.Append("<th>").Append(Html(header)).Append("</th>");
            }

            html.Append("</tr></thead>\r\n<tbody>\r\n");
            var number = 0;
            foreach (var row in list.Rows)
            {
                html.Append("<tr><td>").Append((++number).ToString(CultureInfo.InvariantCulture)).Append("</td>");
                foreach (var value in new[] { row.FullName, row.MaskedNationalId, row.Email, row.State, row.CheckedInAt })
                {
                    html.Append("<td>").Append(Html(value)).Append("</td>");
                }

                html.Append("</tr>\r\n");
            }

            html.Append("</tbody>\r\n</table>\r\n");
        }

        html.Append("</body>\r\n</html>\r\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html.ToString());
    }

    /// <summary>The CSP source of <see cref="Style"/>: "sha256-" and the Base64 of its SHA-256.</summary>
    internal static string StyleHash { get; } = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Style)));

    /// <summary>A field as a spreadsheet must take it: text, never a formula, quoted.</summary>
    internal static string CsvField(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendCsvLine(StringBuilder text, IEnumerable<string> fields) =>
        text.AppendJoin(';', fields.Select(CsvField)).Append("\r\n");

    private static byte[] WithByteOrderMark(string csv)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
