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
/// <item><c>POST donem-fatura</c> ← <c>/finans/donem-fatura</c> · <see cref="DonemTahsilatService.KesVeTahsilEtDetayAsync"/> (E18/E19)</item>
/// <item><c>POST dis-hizmet</c> ← <c>/finans/dis-hizmet</c> · <see cref="DisHizmetService.CreateAsync"/> (E33)</item>
/// <item><c>POST dis-hizmet/{id}/iptal</c> ← <c>/finans/dis-hizmet-iptal</c> · <see cref="DisHizmetService.IptalEtAsync"/> (E34, FinanceReverse)</item>
/// <item><c>POST depozito/al</c> ← <c>/depozito/al</c> · <see cref="DepozitoService.AlAsync"/> (E09)</item>
/// <item><c>POST depozito/irat</c> ← <c>/depozito/irat</c> · <see cref="DepozitoService.IratAsync"/> (E12)</item>
/// </list>
///
/// <para><b>İzin:</b> grup <see cref="Permission.FinanceWrite"/> (Blazor <c>/finans</c> ve <c>/depozito</c> grupları
/// gibi); iptal ayrıca <see cref="Permission.FinanceReverse"/> (Blazor'daki dar izin). Servisler aynı izni
/// ikinci kez doğrular (çift savunma).</para>
///
/// <para><b>Idempotency (docs/api/idempotency-envanteri.md):</b> anahtarlı satırlar (E01, E02, E09, E12, E33)
/// <see cref="IdempotencyBasligi.ZorunluAnahtar"/> ile — başlıksız istek 400 (anahtarsız çağrı her seferinde
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
public static class FinansApi
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

    public const string OncedenAlinanTahsilat = "Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.";

    // ------------------------------------------------------------------ uçlar

    public static RouteGroupBuilder MapFinansApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/finans")
            .RequirePermission(Permission.FinanceWrite)
            .WithTags("Finans");

        g.MapGet("/hesaplar", Hesaplar);
        g.MapPost("/tahsilat", Tahsilat)
            // 409 mukerrer gövdesi belgelenir: SPA `mevcut` (aynı anahtarla zaten yazılmış kayıt) tipini buradan alır.
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/odeme", Odeme);
        g.MapPost("/fatura", Fatura);
        g.MapPost("/donem-fatura", DonemFatura);
        g.MapPost("/dis-hizmet", DisHizmet);
        g.MapPost("/dis-hizmet/{id:guid}/iptal", DisHizmetIptal)
            .RequirePermission(Permission.FinanceReverse); // Blazor: grup FinanceWrite + dar FinanceReverse
        g.MapPost("/depozito/al", DepozitoAl);
        g.MapPost("/depozito/irat", DepozitoIrat);
        return g;
    }

    private static async Task<Ok<IReadOnlyList<FinansHesapOgesi>>> Hesaplar(
        string? tur, FinancialAccountService hesaplar, CancellationToken ct)
    {
        LedgerAccountType? filtre = string.IsNullOrWhiteSpace(tur) ? null : Hesap(tur, "tur");
        var liste = (await hesaplar.ListActiveAsync(ct))
            .Select(h => (Hesap: h, Tur: HesapCozucu.TuruCoz(h.Tur)))
            .Where(x => filtre is null || x.Tur == filtre)
            .Select(x => new FinansHesapOgesi(x.Hesap.Id, HesapEtiketi(x.Hesap, x.Tur), x.Hesap.Kod, x.Hesap.Ad,
                x.Tur?.ToString(), string.IsNullOrWhiteSpace(x.Hesap.Doviz) ? null : x.Hesap.Doviz))
            .ToList();
        return TypedResults.Ok<IReadOnlyList<FinansHesapOgesi>>(liste);
    }

    private static async Task<Ok<FinansIslemYaniti>> Tahsilat(
        TahsilatIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, KurCozucu kurCozucu,
        CancellationToken ct)
    {
        // Deterministik anahtar kiraya özgüdür (kira + bakiye + işlem sayısı); kirasız ya da boş gelmesi istemci hatası.
        if (istek.TahsilatAnahtar is { } ta && (ta == Guid.Empty || istek.KiraId is null))
            throw new ValidationException("Tahsilat anahtarı yalnız kira tahsilatında (kiraId ile) gönderilir.", "tahsilatAnahtar");
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http, deterministik: istek.TahsilatAnahtar);

        var (girdi, kira) = await NakitGirdisiAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, tahsilat: true, kiralar, ct);
        // 5. tur LOW-3: açık kurun kuralları (elle giriş kilidi, pozitiflik, TRY'de kur=1) anahtar/mükerrer
        // kontrolünden ÖNCE: TRY'de kur≠1 tekrarı "farklı içerik" 409'u değil, yazılabilir olmayan istek olarak 400.
        if (istek.Kur is not null)
            await kurCozucu.CozAsync(girdi.Doviz, girdi.Kur, girdi.Tarih, ct);
        if (istek.TahsilatAnahtar is { } gelen)
        {
            // F4.4 adversarial HIGH-1: ÖNCE bu anahtarla yazılmış kayıt aranır. Kaybolan yanıttan sonraki DOĞRU
            // tekrar (aynı anahtar) yeniden hesaplamada "kayıt değişti" alıyor, kullanıcı yeni anahtarla İKİNCİ
            // tahsilatı yazıyordu. Kayıt varsa 409 "zaten kaydedildi" + mevcut (tekrar denemeye yönlendirmez).
            // Yalnız BU kiranın tahsilatıysa bildirilir (kapsam kapısı NakitGirdisiAsync'te geçildi; başka kiranın
            // anahtarı bilgi sızdırmaz, aşağıdaki yeniden hesaplamada "ait değil" 409'u alır).
            await ZatenKaydedildiyseAsync(gelen, kira!, girdi, kasa, kurCozucu, ct);
            await TahsilatAnahtariGuncelAsync(gelen, kira!, kasa, ct);
        }
        girdi.IslemAnahtari = anahtar;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.CollectAsync(girdi, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> Odeme(
        OdemeIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        var (girdi, _) = await NakitGirdisiAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, tahsilat: false, kiralar, ct);
        girdi.IslemAnahtari = anahtar;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.PayAsync(girdi, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> Fatura(
        FaturaKesIstegi istek, InvoiceService faturalar, RentalService kiralar, CancellationToken ct)
    {
        // Yapısal (E15): kira başına fatura + fark sırası; ikinci çağrı servisten 400. Başlık kullanılmaz.
        TutarSiniri(istek.Otv, "otv");
        TutarSiniri(istek.TevkifatTutar, "tevkifatTutar");
        TutarSiniri(istek.DamgaVergisi, "damgaVergisi");
        var kira = await KiraKapsamdaAsync(kiralar, istek.KiraId, ct);
        var vergi = new InvoiceTaxInfo(istek.Otv, istek.TevkifatOran, istek.TevkifatTutar, istek.DamgaVergisi,
            istek.IadeMi, istek.ManuelMi);
        return TypedResults.Ok(new FinansIslemYaniti(await faturalar.CreateFromRentalAsync(kira.Id, vergi: vergi, ct: ct)));
    }

    private static async Task<Ok<DonemFaturaYaniti>> DonemFatura(
        DonemFaturaIstegi istek, DonemTahsilatService donem, RentalService kiralar, CancellationToken ct)
    {
        // Yapısal + deterministik (E18/E19): fatura Kesildi → mevcut id; tahsilat RowKey(kira, sıra). Başlık kullanılmaz.
        if (istek.DonemSira < 1) throw new ValidationException("Dönem sırası 1 ya da daha büyük olmalıdır.", "donemSira");
        var hesap = istek.Tahsilat ? Hesap(istek.Hesap, "hesap") : LedgerAccountType.Kasa; // tahsilatsızda kullanılmaz
        var kira = await KiraKapsamdaAsync(kiralar, istek.KiraId, ct);
        var (faturaId, yazildi) = await donem.KesVeTahsilEtDetayAsync(kira.Id, istek.DonemSira, istek.Tahsilat, hesap, ct);
        return TypedResults.Ok(new DonemFaturaYaniti(faturaId, yazildi,
            istek.Tahsilat && !yazildi ? OncedenAlinanTahsilat : null));
    }

    private static async Task<Ok<FinansIslemYaniti>> DisHizmet(
        DisHizmetIstegi istek, HttpContext http, DisHizmetService svc, RentalService kiralar, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        if (istek.CariId == Guid.Empty) throw new ValidationException("Tedarikçi cari seçilmelidir.", "cariId");
        if (string.IsNullOrWhiteSpace(istek.AlinanHizmet)) throw new ValidationException("Alınan hizmet zorunludur.", "alinanHizmet");
        Metin(istek.AlinanHizmet, 256, "alinanHizmet");
        Metin(istek.HizmetAlinanFirma, 256, "hizmetAlinanFirma");
        Metin(istek.KomisyonFaturaNo, 64, "komisyonFaturaNo");
        Metin(istek.Aciklama, 512, "aciklama");
        Tutar(istek.HizmetBedeli, "hizmetBedeli");
        if (istek.KomisyonOran is < 0m or > 100m)
            throw new ValidationException("Tedarikçi komisyon oranı 0 ile 100 arasında olmalıdır (%).", "komisyonOran");
        var doviz = Doviz(istek.Doviz);
        Kur(istek.Kur);
        BazSiniri(istek.HizmetBedeli, istek.Kur, "hizmetBedeli");
        await KiraKapsamdaAsync(kiralar, istek.KiraId, ct); // alan hatası + kapsam (servis de denetler)

        var id = await svc.CreateAsync(new DisHizmetInput
        {
            RentalId = istek.KiraId,
            FaturaKesilecekCariId = istek.CariId,
            AlinanHizmet = istek.AlinanHizmet,
            HizmetBedeli = istek.HizmetBedeli,
            TedarikciKomisyonOran = istek.KomisyonOran ?? 0m,
            HizmetAlinanFirma = istek.HizmetAlinanFirma,
            KomisyonFaturaNo = istek.KomisyonFaturaNo,
            Doviz = doviz,
            Kur = istek.Kur,
            Aciklama = istek.Aciklama,
            IslemAnahtari = anahtar,
        }, ct);
        return TypedResults.Ok(new FinansIslemYaniti(id));
    }

    private static async Task<NoContent> DisHizmetIptal(Guid id, DisHizmetService svc, CancellationToken ct)
    {
        // Yapısal (E34): Durum kilit içinde yeniden denetlenir; ikinci iptal servisten 400. Kapsam serviste.
        await svc.IptalEtAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<FinansIslemYaniti>> DepozitoAl(
        DepozitoAlIstegi istek, HttpContext http, DepozitoService depozito, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        Cari(istek.CariId);
        Tutar(istek.Tutar);
        var hesap = Hesap(istek.Hesap, "hesap");
        var doviz = Doviz(istek.Doviz);
        Kur(istek.Kur);
        BazSiniri(istek.Tutar, istek.Kur);
        var id = await depozito.AlAsync(istek.CariId, istek.Tutar, hesap, doviz, istek.Kur,
            tarih: null, islemAnahtari: anahtar, hesapId: istek.HesapId, ct: ct);
        return TypedResults.Ok(new FinansIslemYaniti(id));
    }

    private static async Task<Ok<FinansIslemYaniti>> DepozitoIrat(
        DepozitoIratIstegi istek, HttpContext http, DepozitoService depozito, RentalService kiralar, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        Cari(istek.CariId);
        Tutar(istek.Tutar);
        var doviz = Doviz(istek.Doviz);
        Kur(istek.Kur);
        BazSiniri(istek.Tutar, istek.Kur);
        Metin(istek.Aciklama, 512, "aciklama");
        // Kira atfı başka şubenin aracına gelir yazmasın: kapsam kapısı. Kira–cari eşleşmesini repo çiti zorlar.
        if (istek.KiraId is { } kiraId) await KiraKapsamdaAsync(kiralar, kiraId, ct);
        var id = await depozito.IratAsync(istek.CariId, istek.Tutar, doviz, istek.Kur, istek.KiraId,
            tarih: null, islemAnahtari: anahtar, aciklama: istek.Aciklama, ct: ct);
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
    private static async Task<(CashInput Girdi, RentalContract? Kira)> NakitGirdisiAsync(
        Guid cariId, Guid? kiraId, decimal tutar, string? hesap, string? doviz, decimal? kur, Guid? hesapId,
        string? kanal, string? aciklama, DateTimeOffset? tarih, bool tahsilat, RentalService kiralar, CancellationToken ct)
    {
        Cari(cariId);
        Tutar(tutar);
        var hesapTuru = Hesap(hesap, "hesap");
        var dovizKodu = Doviz(doviz);
        Kur(kur);
        BazSiniri(tutar, kur);
        Metin(aciklama, 512, "aciklama");
        if (CashKanal.TryNormalize(kanal) is null)
            throw new ValidationException($"Geçersiz kanal: '{kanal}'. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        Alanli("tarih", () => TarihPolitikasi.ParaTarihi(tarih, "İşlem"));

        RentalContract? kira = null;
        if (kiraId is { } kid)
        {
            kira = await KiraKapsamdaAsync(kiralar, kid, ct);
            if (kira.MusteriId != cariId)
                throw new ValidationException("Kira işlemi yalnız kiranın müşterisi (cari) adına yapılabilir.", "cariId");
            if (tahsilat && kira.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kirada tahsilat yapılmaz.", "kiraId");
        }

        return (new CashInput
        {
            CariId = cariId,
            RentalId = kiraId,
            Tutar = tutar,
            Doviz = dovizKodu,
            Kur = kur,               // boş → servisteki KurCozucu (TRY=1; döviz sabit kur/TCMB; yoksa red)
            Aciklama = aciklama,
            Hesap = hesapTuru,
            HesapId = hesapId,
            Kanal = kanal,           // boş → servis "Masaüstü"
            // boş → servis "şimdi". Low-B: DB'ye giden DateTimeOffset UTC olmalı (DEVIR §5) — "+03:00" ofsetli tarih
            // Npgsql timestamptz yazımında 500 veriyordu (ofset≠0 reddi). Anı değiştirmez, yalnız ofseti 0 yapar.
            Tarih = tarih?.ToUniversalTime(),
        }, kira);
    }

    /// <summary>
    /// F4.4a adversarial MEDIUM-3: DTO'dan gelen deterministik <c>tahsilatAnahtar</c> SUNUCUDA yeniden hesaplanır —
    /// okunan kira + güncel bakiye + güncel işlem sayısı (<see cref="TahsilatAnahtar.Uret"/>). Eşit değilse 409
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
    public const string BayatAnahtarMesaji =
        "Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu kiraya " +
        "ait değil; tahsilat yazılmadı. Güncel bakiyeyi kontrol edip tutarı yeniden girin.";

    private static async Task TahsilatAnahtariGuncelAsync(Guid gelen, RentalContract kira, CashService kasa, CancellationToken ct)
    {
        var sayilar = await kasa.GetRentalIslemSayilariAsync([kira.Id], ct);
        var islemSayisi = sayilar.TryGetValue(kira.Id, out var n) ? n : 0;
        var sade = decimal.Parse(kira.Bakiye.ToString("0.############################", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        if (gelen != TahsilatAnahtar.Uret(kira.Id, kira.Bakiye, islemSayisi)
            && gelen != TahsilatAnahtar.Uret(kira.Id, sade, islemSayisi))
            throw new MukerrerIslemException(BayatAnahtarMesaji);
    }

    /// <summary>Kaybolan yanıttan sonraki tekrar: aynı <c>tahsilatAnahtar</c> ile bu kiraya yazılmış tahsilat.</summary>
    public const string ZatenKaydedildiMesaji =
        "Bu tahsilat zaten kaydedildi (No {0}, {1} {2}); yeni tahsilat yazılmadı.";

    /// <summary>
    /// 3. tur M-A: aynı anahtarla yazılmış kayıt gelen istekten FARKLI (tutar/döviz/hesap) — iki sekme/iki kullanıcı
    /// aynı ekranı açıp biri tahsil etti. İkinci kişinin tutarı YAZILMADI; "zaten kaydedildi" demek (ve formu
    /// silmek) kasiyere parasını kaydedildi sandırırdı.
    /// </summary>
    public const string BaskaTahsilatYazildiMesaji =
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
    /// Kur boşsa sunucunun o an çözeceği kur (<see cref="KurCozucu"/>; TRY=1) karşılaştırılır; çözülemezse güvenli
    /// taraf "aynı değil". Kur <c>numeric(19,6)</c> saklandığı için karşılaştırma 6 haneye yuvarlanmış değerle.
    /// Açıklama boş/boşluk = yok; kenar boşlukları yok sayılır. Kanal servisle aynı kuralla normalize edilir.</para>
    /// </summary>
    private static async Task ZatenKaydedildiyseAsync(
        Guid anahtar, RentalContract kira, CashInput gelen, CashService kasa, KurCozucu kurCozucu, CancellationToken ct)
    {
        if (await kasa.IslemAnahtariylaBulAsync(anahtar, ct) is not { } t
            || t.RentalId != kira.Id || t.Tip != CashTransactionType.Tahsilat)
            return;
        var gelenDoviz = KurService.NormalizeKodStrict(gelen.Doviz);
        var ayni = t.Amount.Amount == gelen.Tutar
                   && string.Equals(t.Amount.Currency, gelenDoviz, StringComparison.OrdinalIgnoreCase)
                   && t.KarsiHesap == gelen.Hesap
                   && t.HesapId == (gelen.HesapId is { } h && h != Guid.Empty ? h : null)
                   && string.Equals(AciklamaNorm(t.Aciklama), AciklamaNorm(gelen.Aciklama), StringComparison.Ordinal)
                   && string.Equals(t.Kanal ?? CashKanal.Masaustu, CashKanal.TryNormalize(gelen.Kanal), StringComparison.Ordinal)
                   && AyniTarih(t.Tarih, gelen.Tarih)
                   && await AyniKurAsync(t.Amount.Rate, gelenDoviz, gelen, kurCozucu, ct);
        var mevcutTutar = t.Amount.Amount.ToString("N2", Tr);
        var mesaj = ayni
            ? string.Format(Tr, ZatenKaydedildiMesaji, t.No, mevcutTutar, t.Amount.Currency)
            : string.Format(Tr, BaskaTahsilatYazildiMesaji, t.No, mevcutTutar, t.Amount.Currency,
                gelen.Tutar.ToString("N2", Tr), gelenDoviz);
        throw new MukerrerIslemException(mesaj, new MevcutIslem(t.Id, t.No, t.Amount.Amount, t.Amount.Currency, ayni));
    }

    /// <summary>5. tur LOW-3: açık işlem tarihi kayıttakiyle aynı mı. Boş tarih = "şimdi" (servis yazım anını koyar) —
    /// tekrarın kendisinden bilinemez, içerik farkı sayılmaz. PG <c>timestamptz</c> mikrosaniye tutar (.NET 100 ns):
    /// karşılaştırma mikrosaniyeye kırpılmış değerle.</summary>
    private static bool AyniTarih(DateTimeOffset kayit, DateTimeOffset? gelen)
        => gelen is not { } g
           || kayit.UtcTicks / TimeSpan.TicksPerMicrosecond == g.UtcTicks / TimeSpan.TicksPerMicrosecond;

    private static string? AciklamaNorm(string? a) => string.IsNullOrWhiteSpace(a) ? null : a.Trim();

    /// <summary>L-1: gelen isteğin kuru (açık ya da o an çözülecek) kayıttaki kurla aynı mı (6 hane).</summary>
    private static async Task<bool> AyniKurAsync(
        decimal kayitKuru, string doviz, CashInput gelen, KurCozucu kurCozucu, CancellationToken ct)
    {
        decimal kur;
        if (gelen.Kur is { } acik) kur = acik;
        else
        {
            try { kur = await kurCozucu.CozAsync(doviz, null, gelen.Tarih, ct); }
            catch (ValidationException) { return false; } // çözülemeyen kur: güvenli taraf (form silinmez)
        }
        return Math.Round(kur, 6, MidpointRounding.AwayFromZero) == Math.Round(kayitKuru, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Kira var mı ve çağıranın şube kapsamında mı (<see cref="RentalService.GetAsync"/> → 403).</summary>
    private static async Task<RentalContract> KiraKapsamdaAsync(RentalService kiralar, Guid kiraId, CancellationToken ct)
    {
        if (kiraId == Guid.Empty) throw new ValidationException("Kira seçilmelidir.", "kiraId");
        return await kiralar.GetAsync(kiraId, ct)
               ?? throw new ValidationException("Kira sözleşmesi bulunamadı.", "kiraId");
    }

    private static void Cari(Guid cariId)
    {
        if (cariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.", "cariId");
    }

    /// <summary><c>numeric(19,4)</c>: 15 tam basamak — tutar ve baz (tutar × kur) bunun altında kalmalı.</summary>
    internal const decimal TutarUstSiniri = 1_000_000_000_000_000m;
    /// <summary><c>numeric(19,6)</c>: 13 tam basamak — kur kolonları.</summary>
    internal const decimal KurUstSiniri = 10_000_000_000_000m;

    /// <summary>Pozitif, kolonlara sığan (F4.4a adversarial MEDIUM-1: taşma 500 üretiyordu) ve 4 ondalığa
    /// yuvarlanınca sıfır kalmayan tutar (L2: 0,00004 kabul edilip 0 tutarlı belge yazılıyor, belge no tüketiyordu).</summary>
    private static void Tutar(decimal tutar, string alan = "tutar")
    {
        if (tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.", alan);
        if (tutar >= TutarUstSiniri) throw new ValidationException("Tutar çok büyük.", alan);
        if (Math.Round(tutar, 4, MidpointRounding.AwayFromZero) == 0m)
            throw new ValidationException("Tutar en az 0,0001 olmalıdır.", alan);
    }

    /// <summary>İsteğe bağlı vergi/belge tutarı: kolon (<c>numeric(19,4)</c>) sınırı; negatiflik servis kuralı.</summary>
    private static void TutarSiniri(decimal? tutar, string alan)
    {
        if (tutar is { } t && Math.Abs(t) >= TutarUstSiniri) throw new ValidationException("Tutar çok büyük.", alan);
    }

    /// <summary>Açık kur yalnız pozitif olabilir ve kolona sığmalı; TRY'de kur ≠ 1 reddi ve "elle kur kilidi"
    /// servisteki KurCozucu'da (Blazor yolu da kapansın diye).</summary>
    private static void Kur(decimal? kur)
    {
        if (kur is <= 0m) throw new ValidationException("Kur pozitif olmalıdır (boş = otomatik).", "kur");
        if (kur >= KurUstSiniri) throw new ValidationException("Kur çok büyük.", "kur");
    }

    /// <summary>Açık kurla baz tutar (tutar × kur) kira Tahsilat/Bakiye kolonuna (<c>numeric(19,4)</c>) sığmalı.
    /// Tutar ve kur ayrı ayrı sınırlı olduğundan çarpım decimal'da taşmaz. Otomatik kurda kalan uç durumları
    /// /api/ui hata eşlemesindeki 22003 → 400 ağı karşılar.</summary>
    private static void BazSiniri(decimal tutar, decimal? kur, string alan = "tutar")
    {
        if (kur is { } k && tutar * k >= TutarUstSiniri) throw new ValidationException("Tutar × kur çok büyük.", alan);
    }

    /// <summary>Metin kolonun uzunluğunu aşmasın (EF yapılandırmasındaki <c>HasMaxLength</c>).</summary>
    private static void Metin(string? deger, int enFazla, string alan)
    {
        if (deger is { Length: var n } && n > enFazla)
            throw new ValidationException($"En çok {enFazla} karakter olabilir.", alan);
    }

    /// <summary>Boş → TRY (Blazor formlarının varsayılanı); aksi halde ISO koda indirgenir, biçimsizse alan hatası.</summary>
    private static string Doviz(string? doviz)
    {
        if (string.IsNullOrWhiteSpace(doviz)) return "TRY";
        string kod = "";
        Alanli("doviz", () => kod = KurService.NormalizeKodStrict(doviz));
        return kod;
    }

    /// <summary>"Kasa" | "Banka" — başka her değer (boş dahil) alan hatası; sessizce Kasa'ya DÜŞMEZ.</summary>
    private static LedgerAccountType Hesap(string? hesap, string alan)
        => hesap?.Trim() switch
        {
            { } h when string.Equals(h, "Kasa", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Kasa,
            { } h when string.Equals(h, "Banka", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Banka,
            _ => throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.", alan),
        };

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler — yetki, mükerrer — korunur).</summary>
    private static void Alanli(string alan, Action dogrula)
    {
        try { dogrula(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }

    /// <summary>"Kasa · Merkez Kasa (MRK · TRY)" — Blazor <c>HesapSecici</c> ile aynı biçim.</summary>
    private static string HesapEtiketi(FinancialAccount h, LedgerAccountType? tur)
    {
        var ad = string.IsNullOrWhiteSpace(h.Ad) ? h.Kod : h.Ad;
        var kuyruk = string.IsNullOrWhiteSpace(h.Doviz) ? $" ({h.Kod})" : $" ({h.Kod} · {h.Doviz})";
        var onek = tur?.ToString() ?? (string.IsNullOrWhiteSpace(h.Tur) ? null : h.Tur.Trim());
        return onek is null ? ad + kuyruk : $"{onek} · {ad}{kuyruk}";
    }
}
