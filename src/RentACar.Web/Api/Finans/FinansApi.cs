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
        g.MapPost("/tahsilat", Tahsilat);
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
        TahsilatIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, CancellationToken ct)
    {
        // Deterministik anahtar kiraya özgüdür (kira + bakiye + işlem sayısı); kirasız ya da boş gelmesi istemci hatası.
        if (istek.TahsilatAnahtar is { } ta && (ta == Guid.Empty || istek.KiraId is null))
            throw new ValidationException("Tahsilat anahtarı yalnız kira tahsilatında (kiraId ile) gönderilir.", "tahsilatAnahtar");
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http, deterministik: istek.TahsilatAnahtar);

        var girdi = await NakitGirdisiAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, tahsilat: true, kiralar, ct);
        girdi.IslemAnahtari = anahtar;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.CollectAsync(girdi, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> Odeme(
        OdemeIstegi istek, HttpContext http, CashService kasa, RentalService kiralar, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        var girdi = await NakitGirdisiAsync(istek.CariId, istek.KiraId, istek.Tutar, istek.Hesap, istek.Doviz,
            istek.Kur, istek.HesapId, istek.Kanal, istek.Aciklama, istek.Tarih, tahsilat: false, kiralar, ct);
        girdi.IslemAnahtari = anahtar;
        return TypedResults.Ok(new FinansIslemYaniti(await kasa.PayAsync(girdi, ct)));
    }

    private static async Task<Ok<FinansIslemYaniti>> Fatura(
        FaturaKesIstegi istek, InvoiceService faturalar, RentalService kiralar, CancellationToken ct)
    {
        // Yapısal (E15): kira başına fatura + fark sırası; ikinci çağrı servisten 400. Başlık kullanılmaz.
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
        if (istek.HizmetBedeli <= 0m) throw new ValidationException("Hizmet bedeli pozitif olmalıdır.", "hizmetBedeli");
        if (istek.KomisyonOran is < 0m or > 100m)
            throw new ValidationException("Tedarikçi komisyon oranı 0 ile 100 arasında olmalıdır (%).", "komisyonOran");
        var doviz = Doviz(istek.Doviz);
        Kur(istek.Kur);
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
    private static async Task<CashInput> NakitGirdisiAsync(
        Guid cariId, Guid? kiraId, decimal tutar, string? hesap, string? doviz, decimal? kur, Guid? hesapId,
        string? kanal, string? aciklama, DateTimeOffset? tarih, bool tahsilat, RentalService kiralar, CancellationToken ct)
    {
        Cari(cariId);
        Tutar(tutar);
        var hesapTuru = Hesap(hesap, "hesap");
        var dovizKodu = Doviz(doviz);
        Kur(kur);
        if (CashKanal.TryNormalize(kanal) is null)
            throw new ValidationException($"Geçersiz kanal: '{kanal}'. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.", "kanal");
        Alanli("tarih", () => TarihPolitikasi.ParaTarihi(tarih, "İşlem"));

        if (kiraId is { } kid)
        {
            var kira = await KiraKapsamdaAsync(kiralar, kid, ct);
            if (kira.MusteriId != cariId)
                throw new ValidationException("Kira işlemi yalnız kiranın müşterisi (cari) adına yapılabilir.", "cariId");
            if (tahsilat && kira.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kirada tahsilat yapılmaz.", "kiraId");
        }

        return new CashInput
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
            Tarih = tarih,           // boş → servis "şimdi"
        };
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

    private static void Tutar(decimal tutar)
    {
        if (tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.", "tutar");
    }

    /// <summary>Açık kur yalnız pozitif olabilir; "elle kur kilidi" gibi firma kuralları servisteki KurCozucu'da.</summary>
    private static void Kur(decimal? kur)
    {
        if (kur is <= 0m) throw new ValidationException("Kur pozitif olmalıdır (boş = otomatik).", "kur");
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
