using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Common;
using RentACar.Web.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// <see cref="ValidationErrorMiddleware"/> — sayfa render'ındaki doğrulama hatası 500 OLMAMALI.
///
/// <para><b>Neden var (canlı hata, 2026-08-26):</b> operatör şube kapsamı dışındaki bir kirayı
/// açınca <c>RentalService.GetAsync</c> → <c>BranchScope.RequireInScope</c> ValidationException
/// fırlatıyor; <c>KiraForm.OnInitializedAsync</c> bunu yakalamıyor ve kullanıcı <b>500 Internal
/// Server Error</b> görüyordu. POST uçları yakalıyordu, sayfa yolu yakalamıyordu.</para>
/// </summary>
public sealed class DogrulamaHatasiMiddlewareTests
{
    private static ValidationErrorMiddleware Mw() => new(NullLogger<ValidationErrorMiddleware>.Instance);

    private static DefaultHttpContext Ctx(string path, string? accept = null)
    {
        var c = new DefaultHttpContext();
        c.Request.Path = path;
        c.Response.Body = new MemoryStream();
        if (accept is not null) c.Request.Headers.Accept = accept;
        return c;
    }

    [Fact]
    public async Task Sayfa_yolunda_hata_500_DEGIL_yonlendirme_uretir()
    {
        var ctx = Ctx("/kiralar/9f1c0a2e-0000-0000-0000-000000000001");
        await Mw().InvokeAsync(ctx, _ => throw new ValidationException("Bu kayıt şube kapsamınız dışında."));

        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
        var target = ctx.Response.Headers.Location.ToString();
        // F13 sonrası güvenlik: yeni arayüzün Panel'i + hata KODU; mesaj metni URL'de taşınmaz (içerik sahteciliği).
        Assert.Equal("/app/panel?hata=dogrulama", target);
    }

    [Fact]
    public async Task Yetki_reddi_kendi_koduyla_yonlenir()
    {
        var ctx = Ctx("/raporlar/export/filo");
        await Mw().InvokeAsync(ctx, _ => throw new NoPermissionException("Bu rapor için yetkiniz yok."));
        Assert.Equal("/app/panel?hata=yetki_yok", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task JSON_bekleyen_cagirana_YONLENDIRME_degil_400_doner()
    {
        // fetch ile çağıran uçlar 302'yi izleyip HTML alır ve sessizce bozulurdu.
        var ctx = Ctx("/kiralar/musteri-olustur", accept: "application/json");
        await Mw().InvokeAsync(ctx, _ => throw new ValidationException("TC kimlik geçersiz."));

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.StartsWith("application/json", ctx.Response.ContentType!, StringComparison.Ordinal);

        ctx.Response.Body.Position = 0;
        // Ham metinde ARAMA yapılmaz: JsonSerializer ASCII-dışı karakterleri \u00E7 gibi kaçışlar
        // (geçerli JSON, JS doğru çözer). Kaçışlama biçimi uygulama detayı — DEĞER doğrulanır.
        using var document = await System.Text.Json.JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("TC kimlik geçersiz.", document.RootElement.GetProperty("hata").GetString());
    }

    /// <summary>
    /// <c>DefaultHttpContext</c>'in varsayılan yanıt özelliği <c>HasStarted</c>'ı hep false döner
    /// (set edilemez), bu yüzden o dalı test etmek için kendi özelliğimizi takıyoruz.
    /// </summary>
    private sealed class StartedResponse : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = 200;
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }

    [Fact]
    public async Task Govde_yazilmaya_baslamissa_mudahale_etmez()
    {
        // Yarım HTML'e yönlendirme eklenemez; istisna yukarı çıkmalı (çerçeve kendi yolunu izler).
        var ctx = Ctx("/kiralar");
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponse());

        await Assert.ThrowsAsync<ValidationException>(
            () => Mw().InvokeAsync(ctx, _ => throw new ValidationException("geç kalan hata")));
    }

    [Fact]
    public async Task Hata_yoksa_dokunmaz()
    {
        var ctx = Ctx("/kiralar");
        var ran = false;
        await Mw().InvokeAsync(ctx, _ => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.True(string.IsNullOrEmpty(ctx.Response.Headers.Location));
    }

    [Fact]
    public async Task Diger_istisnalar_YAKALANMAZ()
    {
        // Yalnız ValidationException kullanıcı hatasıdır; gerçek arıza 500 olarak kalmalı,
        // aksi halde bu middleware gerçek bugları "işlem yapılamadı" diye gizlerdi.
        var ctx = Ctx("/kiralar");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mw().InvokeAsync(ctx, _ => throw new InvalidOperationException("gerçek arıza")));
    }

    [Fact]
    public async Task Mesaj_metni_URL_ye_hic_tasinmaz()
    {
        var ctx = Ctx("/kiralar");
        await Mw().InvokeAsync(ctx, _ => throw new ValidationException(new string('x', 500)));

        var target = ctx.Response.Headers.Location.ToString();
        Assert.Equal("/app/panel?hata=dogrulama", target);
    }
}
