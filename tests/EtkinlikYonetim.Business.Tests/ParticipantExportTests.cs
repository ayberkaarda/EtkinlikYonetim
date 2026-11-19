using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>The CSV and HTML exports of a participant list: safe for spreadsheets and browsers, masked, without ticket codes.</summary>
public sealed partial class ParticipantExportTests
{
    private const string NationalId = "10000000146";
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 18, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 3, 9);

    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly Mock<IParticipantRepository> _participants = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingAudit _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Csv_HasAByteOrderMarkSemicolonsQuotedFieldsAndCrLf()
    {
        var bytes = ParticipantExport.ToCsv(List(Row("Deniz Örnek", "deniz@example.com")));

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal(
            "\"Ad Soyad\";\"T.C. Kimlik No (maskeli)\";\"E-posta\";\"Durum\";\"Giriş zamanı\"\r\n"
            + "\"Deniz Örnek\";\"100******46\";\"deniz@example.com\";\"Giriş yapıldı\";\"02.03.2026 21:00\"\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    /// <summary>A spreadsheet must never run a value as a formula; quotes inside a value are doubled.</summary>
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\";\"y\")", "\"'=HYPERLINK(\"\"http://x\"\";\"\"y\"\")\"")]
    [InlineData("+90", "\"'+90\"")]
    [InlineData("-1+1", "\"'-1+1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("satır\nsonu", "\"satır\nsonu\"")]
    [InlineData("Ali =1", "\"Ali =1\"")]
    [InlineData("", "\"\"")]
    public void CsvField_IsTextNeverAFormula(string value, string expected) => Assert.Equal(expected, ParticipantExport.CsvField(value));

    [Fact]
    public void Csv_OfAnEmptyList_IsTheHeaderOnly() =>
        Assert.Equal(
            "\"Ad Soyad\";\"T.C. Kimlik No (maskeli)\";\"E-posta\";\"Durum\";\"Giriş zamanı\"\r\n",
            Encoding.UTF8.GetString(ParticipantExport.ToCsv(List())[3..]));

    /// <summary>
    /// The page has no script, allows nothing but its own style block (pinned by hash), and every value is
    /// encoded: markup in a name or e-mail address stays text.
    /// </summary>
    [Fact]
    public void Html_EncodesEveryValueAndRunsNothing()
    {
        var html = Encoding.UTF8.GetString(ParticipantExport.ToHtml(List(
            Row("<script>alert(1)</script>", "\"onmouseover=alert(1) x@example.com"),
            Row("Ece & 'Taslak'", "ece@example.com"))));

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover=alert", html.Replace("&quot;onmouseover=alert", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Ece &amp; &#39;Taslak&#39;", html, StringComparison.Ordinal);
        Assert.Contains("100******46", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NationalId, html, StringComparison.Ordinal);

        var csp = CspPattern().Match(html);
        Assert.True(csp.Success);
        Assert.Equal("default-src 'none'; style-src 'sha256-{0}'; base-uri 'none'; form-action 'none'", csp.Groups[1].Value.Replace(csp.Groups[2].Value, "{0}", StringComparison.Ordinal));
        var style = StylePattern().Match(html).Groups[1].Value;
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style))), csp.Groups[2].Value);
        Assert.Equal(1, Regex.Count(html, "<style>"));
        Assert.DoesNotContain(" on", TagsOnly(html), StringComparison.Ordinal);
        Assert.DoesNotContain("src=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_OfAnEmptyListOrACancelledEvent_SaysSo()
    {
        var html = Encoding.UTF8.GetString(ParticipantExport.ToHtml(List() with { EventCancelled = true }));

        Assert.Contains("Bu etkinlikte kayıtlı katılımcı yok.", html, StringComparison.Ordinal);
        Assert.Contains("İptal edildi", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table>", html, StringComparison.Ordinal);
        Assert.Contains("Kayıtlı: 0, giriş yapan: 0, bekleme listesi: 0", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_ByAUser_IsDenied()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().ExportCsvAsync(Sessions.User, 1, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().ExportHtmlAsync(Sessions.User, 1, Ct));

        _events.VerifyNoOtherCalls();
        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Export_UnknownEvent_IsReported()
    {
        _events.Setup(e => e.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync((EventSummary?)null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().ExportCsvAsync(Sessions.Admin, 9, Ct));

        Assert.Equal(EventService.EventNotFound, error.Message);
        Assert.Empty(_audit.Events);
    }

    /// <summary>
    /// The export holds what the admin list holds, with the check-in time in local time and counts by
    /// state; the file name carries only the event's id and date. It is audited without any content.
    /// </summary>
    [Fact]
    public async Task Export_MapsTheRegistrationsAndNamesTheFileWithoutTheEventsText()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3"));
        _events.Setup(e => e.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventSummary(4, "..\\..\\Konser <b>", Day, 3, 2, 1));
        _participants.Setup(p => p.GetByEventAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new EventParticipant(1, "Deniz Örnek", NationalId, "deniz@example.com", CheckedInAt: new DateTime(2026, 3, 9, 17, 5, 0, DateTimeKind.Utc)),
            new EventParticipant(2, null, "20000000001", null),
            new EventParticipant(3, "Ece Taslak", "10000000214", "ece@example.com", RegistrationStatus.Waiting, 1),
        ]);

        var csv = await Service().ExportCsvAsync(Sessions.Admin, 4, Ct);
        var html = await Service().ExportHtmlAsync(Sessions.Admin, 4, Ct);

        Assert.Equal("katilimcilar-4-20260309.csv", csv.SuggestedName);
        Assert.Equal("katilimcilar-4-20260309.html", html.SuggestedName);
        var lines = Encoding.UTF8.GetString(csv.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "\"Deniz Örnek\";\"100******46\";\"deniz@example.com\";\"Giriş yapıldı\";\"09.03.2026 20:05\"",
                "\"Ad Bilinmiyor\";\"200******01\";\"E-posta Yok\";\"Giriş yapılmadı\";\"\"",
                "\"Ece Taslak\";\"100******14\";\"ece@example.com\";\"Bekleme listesi, sıra: 1\";\"\"",
            ],
            lines[1..]);
        Assert.Contains("Kayıtlı: 2, giriş yapan: 1, bekleme listesi: 1", Encoding.UTF8.GetString(html.Content), StringComparison.Ordinal);
        Assert.Contains("..\\..\\Konser &lt;b&gt;", Encoding.UTF8.GetString(html.Content), StringComparison.Ordinal);
        Assert.Equal([(AuditEvent.ParticipantListExported, UserRole.Admin), (AuditEvent.ParticipantListExported, (UserRole?)UserRole.Admin)], _audit.Events);
    }

    /// <summary>Neither format has a place for a ticket code: the exported rows do not carry one.</summary>
    [Fact]
    public void ExportRow_HasNoTicketCode() =>
        Assert.DoesNotContain(typeof(ExportRow).GetProperties(), p => p.Name.Contains("Ticket", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Code", StringComparison.OrdinalIgnoreCase));

    private static ExportRow Row(string name, string email) =>
        new(name, "100******46", email, "Giriş yapıldı", "02.03.2026 21:00", RegistrationStatus.Active);

    private static ParticipantList List(params ExportRow[] rows) => new("Bahar Konseri", Day, false, rows, Now);

    private static string TagsOnly(string html) => string.Concat(TagPattern().Matches(html).Select(m => m.Value));

    private ParticipantExportService Service() => ServiceFactory.Export(_events.Object, _participants.Object, _time, _audit);

    [GeneratedRegex("<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]*'sha256-([A-Za-z0-9+/=]+)'[^\"]*)\">")]
    private static partial Regex CspPattern();

    [GeneratedRegex("<style>(.*?)</style>", RegexOptions.Singleline)]
    private static partial Regex StylePattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
