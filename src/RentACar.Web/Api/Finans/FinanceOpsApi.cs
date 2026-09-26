using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.DisHizmetler;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Finance;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Finans;

/// <summary>
/// <c>/api/ui/v1/finans/*</c> (F4.4) — kira formunun SABİT PANELİNDEKİ para işlemleri; F8 (finans ekranları)
/// aynı uçları YENİDEN KULLANIR. Her uç MEVCUT servisi çağırır (iş mantığı kopyalanmaz); Blazor'daki
/// karşılıkları DEĞİŞMEDİ:
/// <list type="table">
/// <item><c>POST tahsilat</c> ← <c>/finans/tahsilat</c> · <see cref="CashService.CollectAsync"/> (envanter E01)</item>
/// <item><c>POST odeme</c> ← <c>/finans/odeme</c> · <see cref="CashService.PayAsync"/> (E02)</item>
/// <item><c>POST fatura</c> ← <c>/finans/fatura</c> · <see cref="InvoiceService.CreateFromRentalAsync"/> (E15)</item>
/// <item><c>POST donem-fatura</c> ← <c>/finans/donem-fatura</c> · <see cref="PeriodCollectionService.IssueAndCollectDetailAsync"/> (E18/E19)</item>
/// <item><c>POST dis-hizmet</c> ← <c>/finans/dis-hizmet</c> · <see cref="OutsourcedServiceService.CreateAsync"/> (E33)</item>
/// <item><c>POST dis-hizmet/{id}/iptal</c> ← <c>/finans/dis-hizmet-iptal</c> · <see cref="OutsourcedServiceService.CancelAsync"/> (E34, FinanceReverse)</item>
/// <item><c>POST depozito/al</c> ← <c>/depozito/al</c> · <see cref="DepositService.GetAsync"/> (E09)</item>
/// <item><c>POST depozito/irat</c> ← <c>/depozito/irat</c> · <see cref="DepositService.ForfeitAsync"/> (E12)</item>
/// </list>
///
/// <para><b>İzin:</b> grup <see cref="Permission.FinanceWrite"/> (Blazor <c>/finans</c> ve <c>/depozito</c> grupları
/// gibi); iptal ayrıca <see cref="Permission.FinanceReverse"/> (Blazor'daki dar izin). Servisler aynı izni
/// ikinci kez doğrular (çift savunma).</para>
///
/// <para><b>Idempotency (docs/api/idempotency-envanteri.md):</b> anahtarlı satırlar (E01, E02, E09, E12, E33)
/// <see cref="IdempotencyHeader.RequiredKey"/> ile — başlıksız istek 400 (anahtarsız çağrı her seferinde
/// YENİ işlemdir; SPA çift yazıma açık kalmasın). Tahsilatta panel/liste DTO'sunun deterministik
/// <c>tahsilatAnahtar</c>'ı başlıktan ÖNCELİKLİDİR (iki sekme/iki kullanıcı aynı anahtara düşer → ikincisi 409).
/// Yapısal satırlar (E15, E18/E19, E34) anahtar kullanmaz; başlık yok sayılır. Çift gönderimin sonucu
/// servisin bugünkü sonucudur — bu katman onu değiştirmez.</para>
///
/// <para><b>Şube kapsamı:</b> kiraya bağlı her işlem kirayı <see cref="RentalService.GetAsync"/> ile okur
/// (tekil kapsam guard'ı → 403 <c>yetki_yok</c>). Fatura, dönem faturası ve kiraya bağlı tahsilat/irat
/// servislerinde kapsam kontrolü YOK; kapı burasıdır. Dış hizmet servisi kapsamı kendisi de denetler.</para>
///
/// <para><b>Alan hataları:</b> biçim/zorunluluk hataları <c>errors[alan]</c> taşır (JSON adıyla: <c>tutar</c>,
/// <c>hesap</c>…); iş kuralı hataları servisten alan'sız (form düzeyi) gelir.</para>
/// </summary>
public static class FinanceOpsApi
{
    // ------------------------------------------------------------------ sözleşme

    /// <summary>Kasa/banka tahsilatı. <c>hesap</c>: "Kasa" | "Banka" (zorunlu — sessizce Kasa'ya düşmez).
    /// <c>kur</c> boş → otomatik (TRY=1; döviz: firma sabit kuru → TCMB; bulunamazsa red). <c>tahsilatAnahtar</c>:
    /// kira panel/liste DTO'sunun deterministik anahtarı (varsa başlıktan önceliklidir; yalnız <c>kiraId</c> ile).</summary>
    public sealed record TahsilatIstegi(
        Guid CariId, decimal Tutar, string? Hesap,
        Guid? KiraId = null, string? Doviz = null, decimal? Kur = null, Guid? HesapId = null,
        string? Kanal = null, string? Aciklama = null, DateTimeOffset? Tarih = null, Guid? TahsilatAnahtar = null);

    /// <summary>Kasa/banka ödemesi (tediye, giden havale). Alanlar tahsilatla aynı; deterministik anahtar yok.</summary>
    public sealed record OdemeIstegi(
        Guid CariId, decimal Tutar, string? Hesap,
        Guid? KiraId = null, string? Doviz = null, decimal? Kur = null, Guid? HesapId = null,
        string? Kanal = null, string? Aciklama = null, DateTimeOffset? Tarih = null);

    /// <summary>Kiradan fatura (fark faturası otomatiği servistedir). Vergi alanları bilgi amaçlı, isteğe bağlı.</summary>
    public sealed record FaturaKesIstegi(
        Guid KiraId, decimal? Otv = null, decimal? TevkifatOran = null, decimal? TevkifatTutar = null,
        decimal? DamgaVergisi = null, bool IadeMi = false, bool ManuelMi = false);

    /// <summary>Dönem faturası kes; <c>tahsilat</c> true ise fatura döviz/kuruyla tahsilat da yazılır
    /// (<c>hesap</c> o zaman zorunlu).</summary>
    public sealed record DonemFaturaIstegi(Guid KiraId, int DonemSira, bool Tahsilat = false, string? Hesap = null);

    /// <summary>B2B dış hizmet alımı (tam defterli). <c>cariId</c> = fatura kesilecek tedarikçi cari.</summary>
    public sealed record DisHizmetIstegi(
        Guid KiraId, Guid CariId, string? AlinanHizmet, decimal HizmetBedeli,
        decimal? KomisyonOran = null, string? HizmetAlinanFirma = null, string? Doviz = null, decimal? Kur = null,
        string? KomisyonFaturaNo = null, string? Aciklama = null);

    /// <summary>Depozito (emanet) al: Borç Kasa/Banka / Alacak Depozito(cari).</summary>
    public sealed record DepozitoAlIstegi(
        Guid CariId, decimal Tutar, string? Hesap, Guid? HesapId = null, string? Doviz = null, decimal? Kur = null);

    /// <summary>Depozito irat: iade edilmeyen depozito GELİR olur (geri alınamaz). <c>kiraId</c> verilirse gelir
    /// o kiranın aracına atfedilir (kira bu carinin olmalı).</summary>
    public sealed record DepozitoIratIstegi(
        Guid CariId, decimal Tutar, Guid? KiraId = null, string? Doviz = null, decimal? Kur = null, string? Aciklama = null);

    /// <summary>Yazılan (ya da sessiz idempotent tekrarda MEVCUT) kaydın kimliği.</summary>
    public sealed record FinansIslemYaniti(Guid Id);

    /// <summary>Dönem faturası sonucu. <c>tahsilatYazildi</c> false iken tahsilat istendiyse <c>bilgi</c> doludur:
    /// bu dönemin tahsilatı daha önce alınmış (envanter E19 — gizlenmez).</summary>
    public sealed record DonemFaturaYaniti(Guid FaturaId, bool TahsilatYazildi, string? Bilgi);

    /// <summary>Kasa/banka hesap seçimi (yalnız aktif). <c>tur</c>: "Kasa" | "Banka" | null (türü belirsiz —
    /// işlemde reddedilir). IBAN/hesap no DÖNMEZ.</summary>
    public sealed record FinansHesapOgesi(Guid Id, string Etiket, string Kod, string Ad, string? Tur, string? Doviz);

    public const string PreviouslyTakenCollection = "Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.";

    // ------------------------------------------------------------------ uçlar

    public static RouteGroupBuilder MapFinanceApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/finans")
            .RequirePermission(Permission.FinanceWrite)
            .WithTags("Finans");

        g.MapGet("/hesaplar", Accounts);
        g.MapPost("/tahsilat", Collection)
            // 409 mukerrer gövdesi belgelenir: SPA `mevcut` (aynı anahtarla zaten yazılmış kayıt) tipini buradan alır.
            .Produces<UiError.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/odeme", Payment);
        g.MapPost("/fatura", IssueInvoice);
        g.MapPost("/donem-fatura", PeriodInvoice);
        g.MapPost("/dis-hizmet", OutsourcedService);
        g.MapPost("/dis-hizmet/{id:guid}/iptal", CancelOutsourcedService)
            .RequirePermission(Permission.FinanceReverse); // Blazor: grup FinanceWrite + dar FinanceReverse
        g.MapPost("/depozito/al", TakeDeposit);
        g.MapPost("/depozito/irat", DepositForfeit);
        return g;
    }

    private static async Task<Ok<IReadOnlyList<FinansHesapOgesi>>> Accounts(
        string? tur, FinancialAccountService hesaplar, CancellationToken ct)
    {
        LedgerAccountType? filter = string.IsNullOrWhiteSpace(tur) ? null : Account(tur, "tur");
        var list = (await hesaplar.ListActiveAsync(ct))
            .Select(h => (Hesap: h, Tur: AccountResolver.ResolveType(h.Tur)))
            .Where(x => filter is null || x.Tur == filter)
            .Select(x => new FinansHesapOgesi(x.Hesap.Id, AccountLabel(x.Hesap, x.Tur), x.Hesap.Kod, x.Hesap.Ad,
                x.Tur?.ToString(), string.IsNullOrWhiteSpace(x.Hesap.Doviz) ? null : x.Hesap.Doviz))
            .ToList();
        return TypedResults.Ok<IReadOnlyList<FinansHesapOgesi>>(list);
    }

    private static async Task<Ok<FinansIslemYaniti>> Collection(
        TahsilatIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, ExchangeRateResolver kurCozucu,
        CancellationToken ct)
    {
        // Deterministik anahtar kiraya özgüdür (kira + bakiye + işlem sayısı); kirasız ya da boş gelmesi istemci hatası.
        if (istek.TahsilatAnahtar is { } ta && (ta == Guid.Empty || istek.KiraId is null))
            throw new ValidationException("Tahsilat anahtarı yalnız kira tahsilatında (kiraId ile) gönderilir.", "tahsilatAnahtar");
        var key = IdempotencyHeader.RequiredKey(http, deterministic: istek.TahsilatAnahtar);

        var (input, rental) = await CashEntryAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, collection: true, kiralar, ct);
        // 5. tur LOW-3: açık kurun kuralları (elle giriş kilidi, pozitiflik, TRY'de kur=1) anahtar/mükerrer
        // kontrolünden ÖNCE: TRY'de kur≠1 tekrarı "farklı içerik" 409'u değil, yazılabilir olmayan istek olarak 400.
        if (istek.Kur is not null)
            await kurCozucu.ResolveAsync(input.Doviz, input.Kur, input.Tarih, ct);
        if (istek.TahsilatAnahtar is { } incoming)
        {
            // F4.4 adversarial HIGH-1: ÖNCE bu anahtarla yazılmış kayıt aranır. Kaybolan yanıttan sonraki DOĞRU
            // tekrar (aynı anahtar) yeniden hesaplamada "kayıt değişti" alıyor, kullanıcı yeni anahtarla İKİNCİ
            // tahsilatı yazıyordu. Kayıt varsa 409 "zaten kaydedildi" + mevcut (tekrar denemeye yönlendirmez).
            // Yalnız BU kiranın tahsilatıysa bildirilir (kapsam kapısı NakitGirdisiAsync'te geçildi; başka kiranın
            // anahtarı bilgi sızdırmaz, aşağıdaki yeniden hesaplamada "ait değil" 409'u alır).
            await IfAlreadySavedAsync(incoming, rental!, input, kasa, kurCozucu, ct);
            await IsCollectionKeyCurrentAsync(incoming, rental!, kasa, ct);
        }
        // F8.1a M1 (R2-L3 sırası, DEVIR §5): otomatik kur çözümü + baz sınırı "bu anahtarla kayıt var mı"dan SONRA —
        // kaybolan yanıttan sonraki tekrar, kur değişmiş olsa bile önce 409 + mevcut alır.
        await FinansHub.FinanceHubApi.ResolvedBaseLimitAsync(kurCozucu, input.Tutar, input.Doviz, input.Kur, input.Tarih, ct);
        input.IslemAnahtari = key;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.CollectAsync(input, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> Payment(
        OdemeIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, ExchangeRateResolver kurCozucu,
        CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        var (input, _) = await CashEntryAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, collection: false, kiralar, ct);
        await FinansHub.FinanceHubApi.ResolvedBaseLimitAsync(kurCozucu, input.Tutar, input.Doviz, input.Kur, input.Tarih, ct); // F8.1a M1
        input.IslemAnahtari = key;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.PayAsync(input, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> IssueInvoice(
        FaturaKesIstegi istek, InvoiceService faturalar, RentalService kiralar, CancellationToken ct)
    {
        // Yapısal (E15): kira başına fatura + fark sırası; ikinci çağrı servisten 400. Başlık kullanılmaz.
        AmountLimit(istek.Otv, "otv");
        AmountLimit(istek.TevkifatTutar, "tevkifatTutar");
        AmountLimit(istek.DamgaVergisi, "damgaVergisi");
        var rental = await IsRentalInScopeAsync(kiralar, istek.KiraId, ct);
        var tax = new InvoiceTaxInfo(istek.Otv, istek.TevkifatOran, istek.TevkifatTutar, istek.DamgaVergisi,
            istek.IadeMi, istek.ManuelMi);
        return TypedResults.Ok(new FinansIslemYaniti(await faturalar.CreateFromRentalAsync(rental.Id, tax: tax, ct: ct)));
    }

    private static async Task<Ok<DonemFaturaYaniti>> PeriodInvoice(
        DonemFaturaIstegi istek, PeriodCollectionService donem, RentalService kiralar, CancellationToken ct)
    {
        // Yapısal + deterministik (E18/E19): fatura Kesildi → mevcut id; tahsilat RowKey(kira, sıra). Başlık kullanılmaz.
        if (istek.DonemSira < 1) throw new ValidationException("Dönem sırası 1 ya da daha büyük olmalıdır.", "donemSira");
        var account = istek.Tahsilat ? Account(istek.Hesap, "hesap") : LedgerAccountType.Kasa; // tahsilatsızda kullanılmaz
        var rental = await IsRentalInScopeAsync(kiralar, istek.KiraId, ct);
        var (invoiceId, written) = await donem.IssueAndCollectDetailAsync(rental.Id, istek.DonemSira, istek.Tahsilat, account, ct);
        return TypedResults.Ok(new DonemFaturaYaniti(invoiceId, written,
            istek.Tahsilat && !written ? PreviouslyTakenCollection : null));
    }

    private static async Task<Ok<FinansIslemYaniti>> OutsourcedService(
        DisHizmetIstegi istek, HttpContext http, OutsourcedServiceService svc, RentalService kiralar, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        if (istek.CariId == Guid.Empty) throw new ValidationException("Tedarikçi cari seçilmelidir.", "cariId");
        if (string.IsNullOrWhiteSpace(istek.AlinanHizmet)) throw new ValidationException("Alınan hizmet zorunludur.", "alinanHizmet");
        Text(istek.AlinanHizmet, 256, "alinanHizmet");
        Text(istek.HizmetAlinanFirma, 256, "hizmetAlinanFirma");
        Text(istek.KomisyonFaturaNo, 64, "komisyonFaturaNo");
        Text(istek.Aciklama, 512, "aciklama");
        Amount(istek.HizmetBedeli, "hizmetBedeli");
        if (istek.KomisyonOran is < 0m or > 100m)
            throw new ValidationException("Tedarikçi komisyon oranı 0 ile 100 arasında olmalıdır (%).", "komisyonOran");
        var currency = NormalizeCurrency(istek.Doviz);
        Setup(istek.Kur);
        BaseLimit(istek.HizmetBedeli, istek.Kur, "hizmetBedeli");
        await IsRentalInScopeAsync(kiralar, istek.KiraId, ct); // alan hatası + kapsam (servis de denetler)

        var id = await svc.CreateAsync(new DisHizmetInput
        {
            RentalId = istek.KiraId,
            FaturaKesilecekCariId = istek.CariId,
            AlinanHizmet = istek.AlinanHizmet,
            HizmetBedeli = istek.HizmetBedeli,
            TedarikciKomisyonOran = istek.KomisyonOran ?? 0m,
            HizmetAlinanFirma = istek.HizmetAlinanFirma,
            KomisyonFaturaNo = istek.KomisyonFaturaNo,
            Doviz = currency,
            Kur = istek.Kur,
            Aciklama = istek.Aciklama,
            IslemAnahtari = key,
        }, ct);
        return TypedResults.Ok(new FinansIslemYaniti(id));
    }

    private static async Task<NoContent> CancelOutsourcedService(Guid id, OutsourcedServiceService svc, CancellationToken ct)
    {
        // Yapısal (E34): Durum kilit içinde yeniden denetlenir; ikinci iptal servisten 400. Kapsam serviste.
        await svc.CancelAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<FinansIslemYaniti>> TakeDeposit(
        DepozitoAlIstegi istek, HttpContext http, DepositService depozito, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        Account(istek.CariId);
        Amount(istek.Tutar);
        var account = Account(istek.Hesap, "hesap");
        var currency = NormalizeCurrency(istek.Doviz);
        Setup(istek.Kur);
        BaseLimit(istek.Tutar, istek.Kur);
        // F8.1a adversarial M1: kur boşsa ÇÖZÜLECEK kura da baz sınırı.
        await FinansHub.FinanceHubApi.ResolvedBaseLimitAsync(kurCozucu, istek.Tutar, currency, istek.Kur, null, ct);
        var id = await depozito.GetAsync(istek.CariId, istek.Tutar, account, currency, istek.Kur,
            date: null, operationKey: key, accountId: istek.HesapId, ct: ct);
        return TypedResults.Ok(new FinansIslemYaniti(id));
    }

    private static async Task<Ok<FinansIslemYaniti>> DepositForfeit(
        DepozitoIratIstegi istek, HttpContext http, DepositService depozito, RentalService kiralar, ExchangeRateResolver kurCozucu,
        CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        Account(istek.CariId);
        Amount(istek.Tutar);
        var currency = NormalizeCurrency(istek.Doviz);
        Setup(istek.Kur);
        BaseLimit(istek.Tutar, istek.Kur);
        await FinansHub.FinanceHubApi.ResolvedBaseLimitAsync(kurCozucu, istek.Tutar, currency, istek.Kur, null, ct); // M1
        Text(istek.Aciklama, 512, "aciklama");
        // Kira atfı başka şubenin aracına gelir yazmasın: kapsam kapısı. Kira–cari eşleşmesini repo çiti zorlar.
        if (istek.KiraId is { } rentalId) await IsRentalInScopeAsync(kiralar, rentalId, ct);
        var id = await depozito.ForfeitAsync(istek.CariId, istek.Tutar, currency, istek.Kur, istek.KiraId,
            date: null, operationKey: key, description: istek.Aciklama, ct: ct);
        return TypedResults.Ok(new FinansIslemYaniti(id));
    }

    // ------------------------------------------------------------------ giriş doğrulaması (alan'lı)

    /// <summary>Tahsilat/ödeme ortak girdisi. Kiraya bağlıysa kira KAPSAMDA olmalı ve cari kiranın müşterisi
    /// olmalı: kira faturası daima kiranın müşterisini borçlandırır (<c>InvoiceService</c>); başka cariye yazılan
    /// kira tahsilatı kiranın Tahsilat/Bakiye alanını düşürürken borçlu cariyi kapatmazdı. Blazor ekranlarının
    /// hepsi (sabit panel, pano, kira listesi) zaten <c>cariId = kira.MusteriId</c> gönderir.
    /// <para>İptal kiraya TAHSİLAT bağlanamaz: Blazor'un üç tahsilat girişi de (sabit panel, pano, kira listesi)
    /// iptal kirada formu göstermez ("İptal edilmiş kirada tahsilat yapılmaz"); kural burada sunucuda da tutulur.
    /// Ödeme (iade) iptal kiraya bağlanabilir — alınmış paranın geri ödenmesi meşrudur.</para></summary>
    private static async Task<(CashInput Girdi, RentalContract? Kira)> CashEntryAsync(
        Guid customerId, Guid? rentalId, decimal amount, string? account, string? currency, decimal? exchangeRate, Guid? accountId,
        string? channel, string? description, DateTimeOffset? date, bool collection, RentalService rentals, CancellationToken ct)
    {
        Account(customerId);
        Amount(amount);
        var accountType = Account(account, "hesap");
        var currencyCode = NormalizeCurrency(currency);
        Setup(exchangeRate);
        BaseLimit(amount, exchangeRate);
        Text(description, 512, "aciklama");
        if (CashKanal.TryNormalize(channel) is null)
            throw new ValidationException($"Geçersiz kanal: '{channel}'. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        WithFields("tarih", () => DatePolicy.MoneyDate(date, "İşlem"));

        RentalContract? rental = null;
        if (rentalId is { } kid)
        {
            rental = await IsRentalInScopeAsync(rentals, kid, ct);
            if (rental.MusteriId != customerId)
                throw new ValidationException("Kira işlemi yalnız kiranın müşterisi (cari) adına yapılabilir.", "cariId");
            if (collection && rental.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kirada tahsilat yapılmaz.", "kiraId");
        }

        return (new CashInput
        {
            CariId = customerId,
            RentalId = rentalId,
            Tutar = amount,
            Doviz = currencyCode,
            Kur = exchangeRate,               // boş → servisteki KurCozucu (TRY=1; döviz sabit kur/TCMB; yoksa red)
            Aciklama = description,
            Hesap = accountType,
            HesapId = accountId,
            Kanal = channel,           // boş → servis "Masaüstü"
            // boş → servis "şimdi". Low-B: DB'ye giden DateTimeOffset UTC olmalı (DEVIR §5) — "+03:00" ofsetli tarih
            // Npgsql timestamptz yazımında 500 veriyordu (ofset≠0 reddi). Anı değiştirmez, yalnız ofseti 0 yapar.
            Tarih = date?.ToUniversalTime(),
        }, rental);
    }

    /// <summary>
    /// F4.4a adversarial MEDIUM-3: DTO'dan gelen deterministik <c>tahsilatAnahtar</c> SUNUCUDA yeniden hesaplanır —
    /// okunan kira + güncel bakiye + güncel işlem sayısı (<see cref="CollectionKey.Generate"/>). Eşit değilse 409
    /// <c>mukerrer</c>: ya ekran açıldıktan sonra kirada tahsilat/ters kayıt/bakiye değişikliği oldu (bayat — ikinci
    /// sekme, çift tık; SPA kaydı yeniden yükler) ya da anahtar bu kiraya ait değil (başka kiranın anahtarı, başka
    /// bir işlemin tahmin edilebilir anahtarı — ör. dönem tahsilatının <c>RowKey</c>'i). Böylece istemci değeri
    /// yalnız "bu kiranın ŞU ANKİ durumunun anahtarı" olabilir; ham değer korunduğu için Blazor pano/kira
    /// listesiyle SPA aynı anahtara düşer (çapraz çift tahsilat koruması sürer).
    /// <para>Bakiye DB'den <c>numeric(19,4)</c> ölçeğiyle ("300.0000") gelir; ondalık sıfırları atılmış biçim
    /// ("300") de kabul edilir — anahtarı üreten taraf bakiyeyi başka kaynaktan (ör. hesaplanmış DTO) alabilir.</para>
    /// </summary>
    /// <summary>Bayat/yabancı anahtar: hiçbir şey yazılmadı. "Tekrar deneyin" DENMEZ (HIGH-1): kullanıcı güncel
    /// bakiyeyi görüp tutarı BİLİNÇLİ yeniden girmeli.</summary>
    public const string StaleKeyMessage =
        "Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu kiraya " +
        "ait değil; tahsilat yazılmadı. Güncel bakiyeyi kontrol edip tutarı yeniden girin.";

    private static async Task IsCollectionKeyCurrentAsync(Guid incoming, RentalContract rental, CashService cash, CancellationToken ct)
    {
        var numbers = await cash.GetRentalTransactionCountsAsync([rental.Id], ct);
        var transactionCount = numbers.TryGetValue(rental.Id, out var n) ? n : 0;
        var sade = decimal.Parse(rental.Bakiye.ToString("0.############################", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        if (incoming != CollectionKey.Generate(rental.Id, rental.Bakiye, transactionCount)
            && incoming != CollectionKey.Generate(rental.Id, sade, transactionCount))
            throw new DuplicateOperationException(StaleKeyMessage);
    }

    /// <summary>Kaybolan yanıttan sonraki tekrar: aynı <c>tahsilatAnahtar</c> ile bu kiraya yazılmış tahsilat.</summary>
    public const string AlreadySavedMessage =
        "Bu tahsilat zaten kaydedildi (No {0}, {1} {2}); yeni tahsilat yazılmadı.";

    /// <summary>
    /// 3. tur M-A: aynı anahtarla yazılmış kayıt gelen istekten FARKLI (tutar/döviz/hesap) — iki sekme/iki kullanıcı
    /// aynı ekranı açıp biri tahsil etti. İkinci kişinin tutarı YAZILMADI; "zaten kaydedildi" demek (ve formu
    /// silmek) kasiyere parasını kaydedildi sandırırdı.
    /// </summary>
    public const string OtherCollectionWrittenMessage =
        "Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No {0}, {1} {2}); girdiğiniz {3} {4} YAZILMADI. " +
        "Güncel bakiyeyi kontrol edin.";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Aynı anahtarla BU KİRAYA yazılmış tahsilat varsa 409. <c>AyniIcerik</c>: kayıt gelen istekle birebir aynı mı
    /// (tutar <c>decimal</c> eşitliği, döviz, hesap türü, spesifik hesap, kur, açıklama, kanal, açık tarih) — aynıysa kaybolan
    /// yanıttan sonraki kendi tekrarı ("zaten kaydedildi"); farklıysa başkasının (ya da içeriği değiştirilmiş) işlemi
    /// ("… YAZILMADI").
    /// <para>F4.4 L-1: kur/açıklama/kanal da karşılaştırılır — yalnız tutar/hesap aynı diye kurunu ya da açıklamasını
    /// değiştirmiş tekrar "zaten kaydedildi" deyip formu silmesin (yazılan kayıt kullanıcının son niyeti DEĞİL).
    /// Kur boşsa sunucunun o an çözeceği kur (<see cref="ExchangeRateResolver"/>; TRY=1) karşılaştırılır; çözülemezse güvenli
    /// taraf "aynı değil". Kur <c>numeric(19,6)</c> saklandığı için karşılaştırma 6 haneye yuvarlanmış değerle.
    /// Açıklama boş/boşluk = yok; kenar boşlukları yok sayılır. Kanal servisle aynı kuralla normalize edilir.</para>
    /// </summary>
    private static async Task IfAlreadySavedAsync(
        Guid key, RentalContract rental, CashInput incoming, CashService cash, ExchangeRateResolver exchangeRateResolver, CancellationToken ct)
    {
        if (await cash.FindByOperationKeyAsync(key, ct) is not { } t
            || t.RentalId != rental.Id || t.Tip != CashTransactionType.Tahsilat)
            return;
        var incomingCurrency = ExchangeRateService.NormalizeCodeStrict(incoming.Doviz);
        var same = t.Amount.Amount == incoming.Tutar
                   && string.Equals(t.Amount.Currency, incomingCurrency, StringComparison.OrdinalIgnoreCase)
                   && t.KarsiHesap == incoming.Hesap
                   && t.HesapId == (incoming.HesapId is { } h && h != Guid.Empty ? h : null)
                   && string.Equals(NormalizeDescription(t.Aciklama), NormalizeDescription(incoming.Aciklama), StringComparison.Ordinal)
                   && string.Equals(t.Kanal ?? CashKanal.Masaustu, CashKanal.TryNormalize(incoming.Kanal), StringComparison.Ordinal)
                   && SameDate(t.Tarih, incoming.Tarih)
                   && await SameRateAsync(t.Amount.Rate, incomingCurrency, incoming, exchangeRateResolver, ct);
        var currentAmount = t.Amount.Amount.ToString("N2", Tr);
        var message = same
            ? string.Format(Tr, AlreadySavedMessage, t.No, currentAmount, t.Amount.Currency)
            : string.Format(Tr, OtherCollectionWrittenMessage, t.No, currentAmount, t.Amount.Currency,
                incoming.Tutar.ToString("N2", Tr), incomingCurrency);
        throw new DuplicateOperationException(message, new MevcutIslem(t.Id, t.No, t.Amount.Amount, t.Amount.Currency, same));
    }

    /// <summary>5. tur LOW-3: açık işlem tarihi kayıttakiyle aynı mı. Boş tarih = "şimdi" (servis yazım anını koyar) —
    /// tekrarın kendisinden bilinemez, içerik farkı sayılmaz. PG <c>timestamptz</c> mikrosaniye tutar (.NET 100 ns):
    /// karşılaştırma mikrosaniyeye kırpılmış değerle.</summary>
    private static bool SameDate(DateTimeOffset record, DateTimeOffset? incoming)
        => incoming is not { } g
           || record.UtcTicks / TimeSpan.TicksPerMicrosecond == g.UtcTicks / TimeSpan.TicksPerMicrosecond;

    internal static string? NormalizeDescription(string? a) => string.IsNullOrWhiteSpace(a) ? null : a.Trim();

    /// <summary>L-1: gelen isteğin kuru (açık ya da o an çözülecek) kayıttaki kurla aynı mı (6 hane).</summary>
    private static async Task<bool> SameRateAsync(
        decimal recordExchangeRate, string currency, CashInput incoming, ExchangeRateResolver exchangeRateResolver, CancellationToken ct)
    {
        decimal exchangeRate;
        if (incoming.Kur is { } open) exchangeRate = open;
        else
        {
            try { exchangeRate = await exchangeRateResolver.ResolveAsync(currency, null, incoming.Tarih, ct); }
            catch (ValidationException) { return false; } // çözülemeyen kur: güvenli taraf (form silinmez)
        }
        return Math.Round(exchangeRate, 6, MidpointRounding.AwayFromZero) == Math.Round(recordExchangeRate, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Kira var mı ve çağıranın şube kapsamında mı (<see cref="RentalService.GetAsync"/> → 403).</summary>
    internal static async Task<RentalContract> IsRentalInScopeAsync(RentalService rentals, Guid rentalId, CancellationToken ct)
    {
        if (rentalId == Guid.Empty) throw new ValidationException("Kira seçilmelidir.", "kiraId");
        return await rentals.GetAsync(rentalId, ct)
               ?? throw new ValidationException("Kira sözleşmesi bulunamadı.", "kiraId");
    }

    internal static void Account(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.", "cariId");
    }

    /// <summary><c>numeric(19,4)</c>: 15 tam basamak — tutar ve baz (tutar × kur) bunun altında kalmalı.</summary>
    internal const decimal AmountUpperLimit = 1_000_000_000_000_000m;
    /// <summary><c>numeric(19,6)</c>: 13 tam basamak — kur kolonları.</summary>
    internal const decimal RateUpperLimit = 10_000_000_000_000m;

    /// <summary>Pozitif, kolonlara sığan (F4.4a adversarial MEDIUM-1: taşma 500 üretiyordu) ve 4 ondalığa
    /// yuvarlanınca sıfır kalmayan tutar (L2: 0,00004 kabul edilip 0 tutarlı belge yazılıyor, belge no tüketiyordu).</summary>
    internal static void Amount(decimal amount, string alan = "tutar")
    {
        if (amount <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.", alan);
        if (amount >= AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", alan);
        if (Math.Round(amount, 4, MidpointRounding.AwayFromZero) == 0m)
            throw new ValidationException("Tutar en az 0,0001 olmalıdır.", alan);
    }

    /// <summary>İsteğe bağlı vergi/belge tutarı: kolon (<c>numeric(19,4)</c>) sınırı; negatiflik servis kuralı.</summary>
    private static void AmountLimit(decimal? amount, string alan)
    {
        if (amount is { } t && Math.Abs(t) >= AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", alan);
    }

    /// <summary>Açık kur yalnız pozitif olabilir ve kolona sığmalı; TRY'de kur ≠ 1 reddi ve "elle kur kilidi"
    /// servisteki KurCozucu'da (Blazor yolu da kapansın diye).</summary>
    internal static void Setup(decimal? exchangeRate)
    {
        if (exchangeRate is <= 0m) throw new ValidationException("Kur pozitif olmalıdır (boş = otomatik).", "kur");
        if (exchangeRate >= RateUpperLimit) throw new ValidationException("Kur çok büyük.", "kur");
    }

    /// <summary>Açık kurla baz tutar (tutar × kur) kira Tahsilat/Bakiye kolonuna (<c>numeric(19,4)</c>) sığmalı.
    /// Tutar ve kur ayrı ayrı sınırlı olduğundan çarpım decimal'da taşmaz. Otomatik kurda kalan uç durumları
    /// /api/ui hata eşlemesindeki 22003 → 400 ağı karşılar.</summary>
    internal static void BaseLimit(decimal amount, decimal? exchangeRate, string alan = "tutar")
    {
        if (exchangeRate is { } k && amount * k >= AmountUpperLimit) throw new ValidationException("Tutar × kur çok büyük.", alan);
    }

    /// <summary>Metin kolonun uzunluğunu aşmasın (EF yapılandırmasındaki <c>HasMaxLength</c>).</summary>
    internal static void Text(string? value, int maximum, string alan)
    {
        if (value is { Length: var n } && n > maximum)
            throw new ValidationException($"En çok {maximum} karakter olabilir.", alan);
    }

    /// <summary>Boş → TRY (Blazor formlarının varsayılanı); aksi halde ISO koda indirgenir, biçimsizse alan hatası.</summary>
    internal static string NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return "TRY";
        string code = "";
        WithFields("doviz", () => code = ExchangeRateService.NormalizeCodeStrict(currency));
        return code;
    }

    /// <summary>"Kasa" | "Banka" — başka her değer (boş dahil) alan hatası; sessizce Kasa'ya DÜŞMEZ.</summary>
    internal static LedgerAccountType Account(string? account, string alan)
        => account?.Trim() switch
        {
            { } h when string.Equals(h, "Kasa", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Kasa,
            { } h when string.Equals(h, "Banka", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Banka,
            _ => throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.", alan),
        };

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler — yetki, mükerrer — korunur).</summary>
    internal static void WithFields(string alan, Action validate)
    {
        try { validate(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }

    /// <summary>"Kasa · Merkez Kasa (MRK · TRY)" — Blazor <c>HesapSecici</c> ile aynı biçim.</summary>
    private static string AccountLabel(FinancialAccount h, LedgerAccountType? type)
    {
        var name = string.IsNullOrWhiteSpace(h.Ad) ? h.Kod : h.Ad;
        var queue = string.IsNullOrWhiteSpace(h.Doviz) ? $" ({h.Kod})" : $" ({h.Kod} · {h.Doviz})";
        var prefix = type?.ToString() ?? (string.IsNullOrWhiteSpace(h.Tur) ? null : h.Tur.Trim());
        return prefix is null ? name + queue : $"{prefix} · {name}{queue}";
    }
}
