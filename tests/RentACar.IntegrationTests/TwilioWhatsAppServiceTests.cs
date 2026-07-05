using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Web.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>TwilioWhatsAppService istek şekli — SAF (sahte HttpMessageHandler, networksüz).</summary>
public sealed class TwilioWhatsAppServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Req;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.Created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Req = r;
            Body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h);
    }

    private static IConfiguration Config(bool creds = true) => new ConfigurationBuilder().AddInMemoryCollection(
        creds ? new Dictionary<string, string?>
        {
            ["Twilio:AccountSid"] = "AC123",
            ["Twilio:AuthToken"] = "tok456",
            ["Twilio:WhatsAppFrom"] = "+14155238886",
            ["Twilio:Templates:operasyon_ozet"] = "HX789",
        }
        : new Dictionary<string, string?>()).Build();

    [Fact]
    public async Task Dogru_istek_sekli_ve_201_true()
    {
        var h = new CapturingHandler();
        var svc = new TwilioWhatsAppService(new FakeFactory(h), Config(), NullLogger<TwilioWhatsAppService>.Instance);

        var ok = await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "2", ["4"] = "2100" });

        Assert.True(ok);
        Assert.Equal(HttpMethod.Post, h.Req!.Method);
        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/AC123/Messages.json", h.Req.RequestUri!.ToString());
        Assert.Equal("Basic", h.Req.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("AC123:tok456")), h.Req.Headers.Authorization.Parameter);
        Assert.Contains("From=whatsapp", h.Body);
        Assert.Contains("905321112233", h.Body);       // To (url-encoded, rakamlar sağlam)
        Assert.Contains("ContentSid=HX789", h.Body);
        Assert.Contains("ContentVariables", h.Body);
    }

    [Fact]
    public async Task Config_eksikse_HTTP_YOK_false()
    {
        var h = new CapturingHandler();
        var svc = new TwilioWhatsAppService(new FakeFactory(h), Config(creds: false), NullLogger<TwilioWhatsAppService>.Instance);

        var ok = await svc.SendTemplateAsync("+90532", "operasyon_ozet", new Dictionary<string, string>());

        Assert.False(ok);
        Assert.Null(h.Req); // hiç HTTP yapılmadı
    }

    [Fact]
    public async Task Twilio_4xx_false_patlamaz()
    {
        var h = new CapturingHandler { Status = HttpStatusCode.BadRequest };
        var svc = new TwilioWhatsAppService(new FakeFactory(h), Config(), NullLogger<TwilioWhatsAppService>.Instance);

        var ok = await svc.SendTemplateAsync("+90532", "operasyon_ozet", new Dictionary<string, string> { ["1"] = "1" });

        Assert.False(ok);
    }
}
