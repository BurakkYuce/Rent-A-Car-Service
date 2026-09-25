using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DisHizmetler;
using RentACar.Application.EkHizmetler;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.Integrations;
using RentACar.Application.Kur;
using RentACar.Application.Penalties;
using RentACar.Application.Personnel;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Reporting;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Components.Pages.Bookings.KiraFormPaneller;
using RentACar.Web.Finance;
using RentACar.Web.Identity;
using RentACar.Web.Reports;

namespace RentACar.Web.Api.Kira;

/// <summary>
/// <c>/api/ui/v1/kiralar/*</c> — kira sözleşmesi JSON uçları (F4.1). <b>İş mantığı YOK:</b> her uç mevcut
/// servisi çağırır (RentalService, KiraHesapService, RentalAddOnService, SozlesmePaylasimService…); servisler
/// tek doğruluk kaynağıdır. Blazor uçları (<c>BookingEndpoints</c>) F4.6 kesişine kadar DEĞİŞMEDEN yaşar.
///
/// <para><b>İzin haritası</b> (Blazor ile aynı):
/// <list type="bullet">
/// <item>Okumalar (liste, detay, fatura/ceza/dış hizmet/kaynak rezervasyon alt kayıtları): OperationsWrite
/// <b>veya</b> FinanceWrite — Blazor <c>/kiralar</c> sayfası da iki rolü de açıyor (Muhasebe tahsilat için okur).</item>
/// <item>Yazmalar + canlı hesap + müsait araç + dönem planı + paylaşım: OperationsWrite (Blazor <c>/kiralar</c> grubu).</item>
/// <item>İptal: ayrıca OperationsDelete (Blazor <c>/kiralar/cancel</c> dar izni).</item>
/// <item>Karne özeti: ayrıca FinanceWrite (Blazor formunda doluluk kutusu yalnız finans yetkisinde).</item>
/// </list></para>
///
/// <para><b>Şube kapsamı:</b> kimlikli her uç ÖNCE <see cref="RentalService.GetAsync"/>'ten geçer (kapsam dışı →
/// <see cref="YetkiYokException"/> → 403 <c>yetki_yok</c>; yok/başka kiracı → 404). Alt kayıt okumaları
/// (InvoiceService/PenaltyService/DisHizmetService "by rental" yöntemleri kapsam guard'sız) bu kapıdan
/// geçmeden ÇAĞRILMAZ. Servislerin kendi guard'ları ikinci savunma olarak aynen çalışır.</para>
///
/// <para><b>Çift gönderim:</b> bu fazdaki kira işlemleri defter yazmaz ve anahtar almaz; hepsi YAPISAL korunur
/// (aynı araç/tarihe ikinci kira → 409 <c>cakisma</c> [exclusion constraint]; teslim/dönüş/iptal/provizyon
/// durum geçişi → 400; uzatma "yeni bitiş mevcut bitişten sonra" → 400). İstisna: ek hizmet eklemede servis
/// anahtar desteklemez — Blazor'la AYNI davranış (iki tık iki kalem); SPA düğmeyi istek boyunca kilitler.</para>
/// </summary>
public static class KiraApi
{
    private const string Kok = UiApiExtensions.V1 + "/kiralar";

    public static RouteGroupBuilder MapKiraApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/kiralar")
            .WithTags("Kira")
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);

        // ---- okumalar (OW veya FW)
        g.MapGet("", Liste).AlanlariEsle(ListeKurallari);
        g.MapGet("/ozet", Ozet);
        g.MapGet("/filtre-secenekleri", FiltreSecenekleri);
        g.MapGet("/{id:guid}", Detay);
        g.MapGet("/{id:guid}/faturalar", Faturalar);
        g.MapGet("/{id:guid}/cezalar", CezalarVeHgs);
        g.MapGet("/{id:guid}/dis-hizmetler", DisHizmetler);
        g.MapGet("/{id:guid}/kaynak-rezervasyon", KaynakRezervasyon);
        g.MapGet("/{id:guid}/karne-ozeti", KarneOzeti).RequirePermission(Permission.FinanceWrite);
        g.MapGet("/{id:guid}/musteri-ozet", MusteriOzeti); // F4.3b — maskeli cari özeti (okuma kapısı)

        // ---- OperationsWrite (Blazor /kiralar grubu)
        var ow = g.MapGroup("").RequirePermission(Permission.OperationsWrite);
        ow.MapGet("/form-varsayilanlari", FormVarsayilanlari);
        ow.MapGet("/hesapla", Hesapla).AlanlariEsle(HesaplaKurallari);
        ow.MapGet("/musait-arac", MusaitArac);
        ow.MapGet("/ek-hizmet-katalogu", EkHizmetKatalogu); // F4.3b — matris (fiyat/KDV; tutar hesapla'dan)
        ow.MapGet("/{id:guid}/donus-hesapla", DonusHesapla);
        ow.MapGet("/{id:guid}/donem-plani", DonemPlani);
        ow.MapGet("/{id:guid}/paylasim", PaylasimDurumu);

        ow.MapPost("", Olustur).AlanlariEsle(OlusturKurallari);
        ow.MapPost("/musteri", MusteriOlustur).AlanlariEsle(MusteriKurallari);
        ow.MapPut("/{id:guid}", Guncelle).AlanlariEsle(GuncelleKurallari);
        ow.MapPost("/{id:guid}/teslim", Teslim).AlanlariEsle(TeslimKurallari);
        ow.MapPost("/{id:guid}/donus", Donus).AlanlariEsle(DonusKurallari);
        ow.MapPost("/{id:guid}/uzat", Uzat).AlanlariEsle(UzatKurallari);
        ow.MapPost("/{id:guid}/iptal", Iptal).RequirePermission(Permission.OperationsDelete);
        ow.MapPost("/{id:guid}/provizyon/al", ProvizyonAl).AlanlariEsle(ProvizyonKurallari);
        ow.MapPost("/{id:guid}/provizyon/kapat", ProvizyonKapat).AlanlariEsle(ProvizyonKurallari);
        ow.MapPost("/{id:guid}/ek-hizmetler", EkHizmetEkle).AlanlariEsle(EkHizmetKurallari);
        ow.MapDelete("/{id:guid}/ek-hizmetler/{kalemId:guid}", EkHizmetSil);
        ow.MapPost("/{id:guid}/paylasim", Paylas);
        ow.MapPost("/{id:guid}/paylasim/yeni-surum", PaylasimYeniSurum);
        ow.MapDelete("/{id:guid}/paylasim", PaylasimIptal);
        return g;
    }

    // ================================================================== ortak

    /// <summary>Üst kayıt kapısı: şube kapsamlı okuma (kapsam dışı → YetkiYokException → 403).</summary>
    private static Task<RentalContract?> KapsamliAsync(RentalService kiralar, Guid id, CancellationToken ct)
        => kiralar.GetAsync(id, ct);

    private static ProblemHttpResult Bulunamadi(string detay = "Kira sözleşmesi bulunamadı.")
        => TypedResults.Problem(detail: detay, statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı");

    /// <summary>İşlem sonrası güncel sözleşme (servisin yazdığı değerler; hesap yok).</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Guncel(
        RentalService kiralar, IBookingRepository depo, Guid id, CancellationToken ct)
    {
        var surum = await depo.RentalSurumuAsync(id, ct); // alanlardan ÖNCE (bkz. Surum)
        return await kiralar.GetAsync(id, ct) is { } c ? TypedResults.Ok(KiraSozlesmesiDto.From(c, surum)) : Bulunamadi();
    }

    private static bool Izin(HttpContext http, Permission p) => AuthExtensions.HasPermission(http.User, p);

    /// <summary>F4.1 adversarial L2: gövdede eksik zorunlu alan → 400 <c>errors[alan]</c> (sessiz 0 değil).</summary>
    private static T Zorunlu<T>(T? deger, string alan, string etiket) where T : struct
        => deger ?? throw new ValidationException($"{etiket} zorunludur.", alan);

    /// <summary>Yakıt ölçeği 0–12 — TEK iç ölçek (<see cref="RentACar.Domain.Common.YakitOlcegi"/>, Karar (3)).
    /// Servis aynı sınırı ayrıca zorlar; burada alan adıyla 400 <c>errors[alan]</c> üretmek için.</summary>
    public const int FormYakitEnFazla = RentACar.Domain.Common.YakitOlcegi.EnFazla;

    private static int Yakit(int? deger, string alan, string etiket)
    {
        var y = Zorunlu(deger, alan, etiket);
        if (y is < 0 or > FormYakitEnFazla)
            throw new ValidationException($"{etiket} 0-{FormYakitEnFazla} aralığında olmalıdır.", alan);
        return y;
    }

    /// <summary>Hızlı tahsilat verisi — Blazor pano/liste formuyla BİREBİR: anahtar
    /// <see cref="TahsilatAnahtar.Uret"/>(kira, bakiye, işlem sayısı), döviz kira dövizi (normalize), tutar bakiye.</summary>
    internal static TahsilatBilgisi TahsilatVerisi(Guid rentalId, Guid cariId, decimal bakiye, string? doviz, int islemSayisi)
        => new(TahsilatAnahtar.Uret(rentalId, bakiye, islemSayisi), cariId, rentalId, KurService.NormalizeKod(doviz),
            Math.Round(bakiye, 2, MidpointRounding.AwayFromZero));

    // ================================================================== liste

    /// <summary>Sıralama beyaz listesi (RentalRow alanları). <c>sirala</c> yoksa servisin sırası korunur
    /// (oluşturma zamanı, yeniden eskiye — Blazor listesi ile aynı).</summary>
    private static readonly SiralamaHaritasi<RentalRow> Harita = SiralamaHaritasi<RentalRow>
        .Olustur(r => r.Id)
        .Alan("sozlesmeNo", r => r.SozlesmeNo)
        // KVKK: sıralama da GÖRÜNEN ada göre (anonim carinin gerçek adı sıradan sızmasın).
        .Alan("musteri", r => r.MusteriAnonimAd ? MusteriGorunumu.AnonimAdEtiketi : r.MusteriAd)
        .Alan("plaka", r => r.Plaka)
        .Alan("basTar", r => r.BasTar)
        .Alan("bitTar", r => r.BitTar)
        .Alan("vadeTar", r => r.VadeTar)
        .Alan("gun", r => r.Gun)
        .Alan("tutar", r => r.Tutar)
        .Alan("bakiye", r => r.Bakiye)
        .Alan("durum", r => r.Durum)
        .Alan("kaynak", r => r.Kaynak)
        .Alan("cikisOfisi", r => r.CikisOfisi)
        .Alan("donusOfisi", r => r.DonusOfisi);

    private static readonly (string, string)[] ListeKurallari = [("Geçersiz sıralama alanı", "sirala")];

    /// <summary>Blazor RentalList süzgeçleri (FAZ-46 dahil). Tarihler takvim günü; aralık İstanbul gününe göre.</summary>
    public sealed class KiraListeFiltresi
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        /// <summary>Durum adı: <c>Kirada</c> | <c>Tamamlandi</c> | <c>Iptal</c> (yanıttaki <c>durum</c> ile aynı metin).</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "fatura")] public bool? Fatura { get; set; }
        /// <summary>Tarih aralığının uygulandığı alan: <c>Baslangic</c> (varsayılan) | <c>Bitis</c> | <c>Islem</c> | <c>Vade</c>.</summary>
        [FromQuery(Name = "tarihTuru")] public string? TarihTuru { get; set; }
        [FromQuery(Name = "basMin")] public DateOnly? BasMin { get; set; }
        [FromQuery(Name = "basMax")] public DateOnly? BasMax { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
        /// <summary>Ofis filtresi yönü: <c>Cikis</c> | <c>Donus</c> (boş = ikisi).</summary>
        [FromQuery(Name = "ofisDurum")] public string? OfisDurum { get; set; }
        [FromQuery(Name = "sahip")] public string? Sahip { get; set; }
        [FromQuery(Name = "grup")] public string? Grup { get; set; }
        [FromQuery(Name = "kaynak")] public string? Kaynak { get; set; }
        [FromQuery(Name = "personelId")] public Guid? PersonelId { get; set; }

        /// <summary>RentalList.razor'un filtre kurulumu (Kapsam'ı servis AYRICA zorlar — çağıran genişletemez).</summary>
        public RentalFilter ToFilter() => new()
        {
            Query = KiraOlusturIstegi.Nz(Q),
            Durum = Ad<RentalStatus>(Durum, "durum"),
            Faturali = Fatura,
            BaslangicMin = BasMin is { } mn ? GunBasi(mn) : null,
            // F4.1 adversarial L7: gün sonu "< ertesi gün 00:00" (repo <= uygular; timestamptz µs hassasiyetinde
            // ertesi gün − 1 µs ≡ < ertesi gün). Eski AddSeconds(-1) 23:59:59.xxx kayıtlarını dışarıda bırakıyordu.
            BaslangicMax = BasMax is { } mx ? GunBasi(mx.AddDays(1)).AddMicroseconds(-1) : null,
            Ofis = KiraOlusturIstegi.Nz(Ofis),
            TarihTuru = Ad<TarihListesiTuru>(TarihTuru, "tarihTuru"),
            OfisDurum = Ad<OfisDurumu>(OfisDurum, "ofisDurum"),
            SahipGrup = KiraOlusturIstegi.Nz(Sahip),
            AracGrubu = KiraOlusturIstegi.Nz(Grup),
            RezKaynak = KiraOlusturIstegi.Nz(Kaynak),
            PersonelId = PersonelId,
        };

        /// <summary>Enum ADI (büyük/küçük harf duyarsız); sayı ya da tanımsız ad 400 (sessizce "filtre yok"a düşmez).</summary>
        private static T? Ad<T>(string? deger, string alan) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(deger)) return null;
            var d = deger.Trim();
            if (!char.IsDigit(d[0]) && d[0] != '-' && Enum.TryParse<T>(d, ignoreCase: true, out var v) && Enum.IsDefined(v))
                return v;
            throw new ValidationException(
                $"Geçersiz {alan} değeri. İzin verilenler: {string.Join(", ", Enum.GetNames<T>())}.", alan);
        }

        /// <summary>Takvim gününün İstanbul gece yarısı, UTC olarak (Npgsql timestamptz yalnız offset 0 kabul eder).</summary>
        private static DateTimeOffset GunBasi(DateOnly gun)
        {
            var yerel = gun.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(yerel, TenantGun.Dilim.GetUtcOffset(yerel)).ToUniversalTime();
        }
    }

    private static async Task<Ok<Sayfa<KiraListeSatiri>>> Liste(
        HttpContext http, RentalService kiralar, CashService kasa, [AsParameters] KiraListeFiltresi f,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var istek = new ListeIstegi(sayfa ?? 1, boyut ?? 50, sirala);
        var satirlar = await kiralar.SearchAsync(f.ToFilter(), ct);
        // Beyaz liste KAYIT sayılmadan önce (bilinmeyen alan boşa sorgu olmadan 400).
        IEnumerable<RentalRow> sirali = istek.Sirala is null ? satirlar : Harita.Uygula(satirlar.AsQueryable(), istek.Sirala);
        var toplam = satirlar.Count;
        var sayfaSatirlari = istek.Atla >= toplam ? [] : sirali.Skip((int)istek.Atla).Take(istek.Boyut).ToList();

        // "Tahsil Et": yalnız FinanceWrite ve tahsil edilebilir satır (RentalList.razor ile aynı koşul).
        var islemSayilari = new Dictionary<Guid, int>();
        var finans = Izin(http, Permission.FinanceWrite);
        var tahsilEdilebilir = finans
            ? sayfaSatirlari.Where(r => r.Bakiye > 0m && r.Durum != RentalStatus.Iptal).Select(r => r.Id).ToList()
            : [];
        if (tahsilEdilebilir.Count > 0)
            islemSayilari = await kasa.GetRentalIslemSayilariAsync(tahsilEdilebilir, ct);
        var tahsilSet = tahsilEdilebilir.ToHashSet();

        var kayitlar = sayfaSatirlari.Select(r => new KiraListeSatiri(
            r.Id, r.SozlesmeNo, r.MusteriId, MusteriGorunumu.ListeAdi(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.BasTar, r.BitTar, r.VadeTar, r.Gun,
            r.HediyeGun, r.FaturalananGun, r.Tutar, r.Bakiye, r.Doviz, r.Kaynak, r.CikisOfisi, r.DonusOfisi,
            r.Provizyon, r.Depozito, r.KomisyonOran, r.KomisyonTutar, r.OnayKodu, r.ProjeAdi, r.AssistFirma,
            r.OzelSoforBilgisi, r.Durum.ToString(), r.Faturali,
            tahsilSet.Contains(r.Id)
                ? TahsilatVerisi(r.Id, r.MusteriId, r.Bakiye, r.Doviz, islemSayilari.GetValueOrDefault(r.Id))
                : null)).ToList();
        return TypedResults.Ok(new Sayfa<KiraListeSatiri>(kayitlar, toplam, istek.Sayfa, istek.Boyut));
    }

    private static async Task<Ok<KiraListeOzeti>> Ozet(
        RentalService kiralar, [AsParameters] KiraListeFiltresi f, CancellationToken ct)
    {
        var satirlar = await kiralar.SearchAsync(f.ToFilter(), ct);
        return TypedResults.Ok(new KiraListeOzeti(
            satirlar.Count, satirlar.Count(r => r.Durum == RentalStatus.Kirada), satirlar.Count(r => !r.Faturali)));
    }

    /// <summary>Sahip/grup öneri listeleri — RentalList.razor gibi araç kartlarından TÜRETİLİR (master yok).</summary>
    private static async Task<Ok<KiraFiltreSecenekleri>> FiltreSecenekleri(VehicleService araclar, CancellationToken ct)
    {
        var liste = await araclar.ListAsync(ct);
        static List<string> Ayikla(IEnumerable<string?> degerler) => [.. degerler
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Create(new CultureInfo("tr-TR"), false))];
        return TypedResults.Ok(new KiraFiltreSecenekleri(Ayikla(liste.Select(v => v.AracSahibi)), Ayikla(liste.Select(v => v.Grup))));
    }

    // ================================================================== detay + alt kayıtlar

    private static async Task<Results<Ok<KiraDetayYaniti>, ProblemHttpResult>> Detay(
        Guid id, HttpContext http, RentalService kiralar, CustomerService musteriler, VehicleService araclar,
        BranchService subeler, PersonelService personeller, RentalAddOnService ekler,
        SozlesmePaylasimService paylasim, KurService kurlar, IBookingRepository depo, PenaltyService cezalar,
        CashService kasa,
        CancellationToken ct)
    {
        // F4.3 adversarial F2: sürüm alanlardan ÖNCE okunur — arada yazım olursa istemcinin sürümü alanlarından
        // ESKİ olur ve sonraki PUT güvenli tarafta (409) kalır; tersi bayat alanı "güncel" gösterirdi.
        var surum = await depo.RentalSurumuAsync(id, ct);
        var c = await KapsamliAsync(kiralar, id, ct);
        if (c is null) return Bulunamadi();

        var yetki = new KiraYetkileri(
            Izin(http, Permission.OperationsWrite), Izin(http, Permission.OperationsDelete), Izin(http, Permission.FinanceWrite));

        // Müşteri: yalnız görünen ad (TC/ehliyet gibi şifreli alanlar YANITA GİRMEZ).
        var musteri = await musteriler.GetAsync(c.MusteriId, ct);
        var ikinci = c.IkinciSurucuId is Guid ik ? await musteriler.GetAsync(ik, ct) : null;

        // Araç kartı: KiraForm gibi şube kapsamlı araç listesinden (görünmeyen araçta kart boş, 403 değil).
        var arac = (await araclar.ListAsync(ct)).FirstOrDefault(v => v.Id == c.VehicleId);

        // Personel adları: PII'sız seçim listesi; yetkisiz rolde nazikçe boş (KiraForm'daki desen).
        IReadOnlyList<PersonelSecim> personel = [];
        try { personel = await personeller.ListForSelectAsync(ct); }
        catch (YetkiYokException) { /* ad çözülmez */ }
        string? PersonelAd(Guid? pid) => pid is Guid p && personel.FirstOrDefault(x => x.Id == p) is { } ps ? $"{ps.Ad} {ps.Soyad}" : null;

        var islemSube = c.CikisSubeId is Guid sid ? (await subeler.GetAsync(sid, ct))?.Ad : null;
        var kalemler = (await ekler.ListAsync(c.Id, ct)).Select(EkHizmetDto).ToList();

        KiraDovizBilgisi? doviz = null;
        var kod = KurService.NormalizeKod(c.Doviz);
        if (kod != "TRY")
        {
            try
            {
                var kur = await kurlar.GetRateAsync(kod, ct: ct);
                doviz = new KiraDovizBilgisi(kod, kur, c.GenelToplam * kur);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                doviz = new KiraDovizBilgisi(kod, null, null); // bilgi: güncel kur yok
            }
        }

        KiraPaylasimBari? bar = null;
        if (yetki.Operasyon)
        {
            PaylasimDurum? durum = null;
            try { durum = await paylasim.DurumAsync(c.Id, ct); }
            catch (ValidationException) { durum = null; }
            // PII tek kuraldan (MusteriGorunumu): KVKK Anonim* bayrakları ön-doldurmayı ve hitabı da kapsar.
            bar = new KiraPaylasimBari(Link(durum), MusteriGorunumu.Telefon(musteri), MusteriGorunumu.Eposta(musteri),
                $"Kira Sözleşmesi {c.SozlesmeNo}", MusteriGorunumu.PaylasimMesaji(c, musteri));
        }

        // F4.3b: gösterim toplamları SUNUCUDA (SPA toplama yapmaz). Cezalar kapsam kapısından SONRA okunur.
        var cezaToplam = (await cezalar.ListByRentalAsync(c.Id, ct))
            .Where(p => p.Durum != CezaDurum.Iptal).Sum(p => p.Tutar);
        var toplamlar = new KiraToplamlari(kalemler.Sum(k => k.Toplam), cezaToplam);

        // F4.4 sabit panel tahsilatı: deterministik anahtar SUNUCUDA (liste/pano ile aynı üretim). Yalnız
        // FinanceWrite ve iptal olmayan kira (FinansApi iptal kiraya tahsilatı zaten reddeder).
        TahsilatBilgisi? tahsilat = null;
        if (yetki.Finans && c.Durum != RentalStatus.Iptal)
        {
            var sayilar = await kasa.GetRentalIslemSayilariAsync([c.Id], ct);
            tahsilat = TahsilatVerisi(c.Id, c.MusteriId, c.Bakiye, c.Doviz, sayilar.GetValueOrDefault(c.Id));
        }

        return TypedResults.Ok(new KiraDetayYaniti(
            KiraSozlesmesiDto.From(c, surum),
            new KiraTarafDto(c.MusteriId, MusteriGorunumu.TarafAdi(musteri)),
            ikinci is null ? null : new KiraTarafDto(ikinci.Id, MusteriGorunumu.TarafAdi(ikinci)),
            arac is null ? null : new KiraAracDto(arac.Id, arac.Plaka, arac.Marka, arac.Tip, arac.ModelYili,
                arac.Vites?.ToString(), arac.Yakit?.ToString(), arac.Grup, arac.Segment, arac.Km, arac.Sube, arac.Konum),
            islemSube,
            PersonelAd(c.TeslimAlanPersonelId),
            PersonelAd(c.TeslimEdenPersonelId),
            kalemler,
            doviz,
            bar,
            yetki,
            toplamlar,
            tahsilat));
    }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// F4.3b — Müşteri sekmesinin salt-okunur cari özeti. Üst kayıt kapısından geçer (kapsam dışı 403, yok/başka
    /// kiracı 404); müşteri kiranın kendi <c>MusteriId</c>'sinden okunur (istemci başka cari soramaz). PII: bkz.
    /// <see cref="KiraMusteriOzeti"/> ve <see cref="MusteriGorunumu"/> (TEK kural: maske + KVKK Anonim* bayrakları).
    /// </summary>
    private static async Task<Results<Ok<KiraMusteriOzeti>, ProblemHttpResult>> MusteriOzeti(
        Guid id, RentalService kiralar, CustomerService musteriler, CancellationToken ct)
    {
        var c = await KapsamliAsync(kiralar, id, ct);
        if (c is null) return Bulunamadi();
        var m = await musteriler.GetAsync(c.MusteriId, ct);
        if (m is null) return Bulunamadi("Müşteri bulunamadı.");
        return TypedResults.Ok(MusteriGorunumu.Ozet(m));
    }

    private static EkHizmetKalemiDto EkHizmetDto(RentalAddOn a)
        => new(a.Id, a.EkHizmetTanimId, a.Ad, a.Miktar, a.BirimNetFiyat, a.KdvOrani, a.NetTutar, a.KdvTutar, a.Toplam);

    private static KiraPaylasimLinki? Link(PaylasimDurum? d)
        => d is null ? null : new KiraPaylasimLinki("/sozlesme/" + d.Token, d.ErisimSayisi, d.SonErisimUtc,
            d.OlusturmaUtc, d.AnlikGoruntuUtc, d.Bayat);

    private static async Task<Results<Ok<IReadOnlyList<KiraFaturaDto>>, ProblemHttpResult>> Faturalar(
        Guid id, RentalService kiralar, InvoiceService faturalar, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var liste = await faturalar.ListByRentalAsync(id, ct); // kapsam kapısından SONRA (servis guard'sız)
        return TypedResults.Ok<IReadOnlyList<KiraFaturaDto>>(liste.Select(f => new KiraFaturaDto(
            f.Id, f.No, f.Tarih, f.NetTutar, f.KdvTutar, f.GenelToplam, f.Currency, f.Durum.ToString(),
            f.IadeMi ? "İade" : f.KaynakKiraId is not null ? "Fark" : "Kira")).ToList());
    }

    private static async Task<Results<Ok<KiraCezaHgsYaniti>, ProblemHttpResult>> CezalarVeHgs(
        Guid id, RentalService kiralar, PenaltyService cezalar, VehicleService araclar, IHgsService hgs, CancellationToken ct)
    {
        var c = await KapsamliAsync(kiralar, id, ct);
        if (c is null) return Bulunamadi();
        var liste = (await cezalar.ListByRentalAsync(id, ct))
            .Select(p => new KiraCezaDto(p.Id, p.No, p.CezaTuru, p.Tutar, p.Kalan, p.Durum.ToString(), p.TebligTarihi)).ToList();
        // HGS: kira dönemi (gerçek dönüş varsa o) — KiraForm ile aynı; plaka görünür araç listesinden.
        var plaka = (await araclar.ListAsync(ct)).FirstOrDefault(v => v.Id == c.VehicleId)?.Plaka;
        IReadOnlyList<HgsGecisDto> gecisler = [];
        if (!string.IsNullOrWhiteSpace(plaka))
            gecisler = (await hgs.GetCrossingsAsync(plaka, c.BasTar, c.GercekDonusTar ?? c.BitTar, ct))
                .Select(g => new HgsGecisDto(g.Zaman, g.Gecis, g.Tutar)).ToList();
        return TypedResults.Ok(new KiraCezaHgsYaniti(liste, gecisler));
    }

    private static async Task<Results<Ok<IReadOnlyList<KiraDisHizmetDto>>, ProblemHttpResult>> DisHizmetler(
        Guid id, RentalService kiralar, DisHizmetService disHizmetler, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var liste = await disHizmetler.ListForRentalAsync(id, ct); // kapsam kapısından SONRA (servis guard'sız)
        return TypedResults.Ok<IReadOnlyList<KiraDisHizmetDto>>(liste.Select(KiraDisHizmetDto.From).ToList());
    }

    private static async Task<Results<Ok<IReadOnlyList<KiraDonemDto>>, ProblemHttpResult>> DonemPlani(
        Guid id, RentalService kiralar, FaturaDonemPlanService donemPlan, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var liste = await donemPlan.PreviewAsync(id, ct); // plansız kira → boş liste
        return TypedResults.Ok<IReadOnlyList<KiraDonemDto>>(liste.Select(KiraDonemDto.From).ToList());
    }

    private static async Task<Results<Ok<KaynakRezervasyonYaniti>, ProblemHttpResult>> KaynakRezervasyon(
        Guid id, RentalService kiralar, ReservationService rezervasyonlar, CancellationToken ct)
    {
        var c = await KapsamliAsync(kiralar, id, ct);
        if (c is null) return Bulunamadi();
        if (c.ReservationId is not Guid rezId) return TypedResults.Ok(new KaynakRezervasyonYaniti(null));
        var r = await rezervasyonlar.GetAsync(rezId, ct); // rezervasyonun KENDİ kapsam guard'ı da çalışır
        return TypedResults.Ok(new KaynakRezervasyonYaniti(r is null ? null : new KaynakRezervasyonDto(
            r.Id, r.ReservationNo, r.Durum.ToString(), r.BasTar, r.BitTar, r.Kaynak, r.TalepTuru,
            r.OtaKiraBedeli, r.OtaDropBedeli, r.OtaBebekKoltugu, r.OtaNavigasyon, r.OtaLcf, r.OtaCdw, r.OtaScdw, r.OtaEkSurucu)));
    }

    private static async Task<Results<Ok<KarneOzetiDto>, ProblemHttpResult>> KarneOzeti(
        Guid id, RentalService kiralar, ReportService raporlar, CancellationToken ct)
    {
        var c = await KapsamliAsync(kiralar, id, ct);
        if (c is null) return Bulunamadi();
        var karne = await raporlar.GetAracKarneAsync(c.VehicleId, ct: ct);
        return TypedResults.Ok(new KarneOzetiDto(c.VehicleId, karne?.Kpi.DolulukYuzde));
    }

    // ================================================================== form yardımcıları

    private static async Task<Ok<KiraFormVarsayilanlari>> FormVarsayilanlari(FormVarsayilanCozucu varsayilanlar, CancellationToken ct)
        => TypedResults.Ok(new KiraFormVarsayilanlari(
            await varsayilanlar.CikisYakitAsync(ct),
            await varsayilanlar.FiyatTuruAsync(ct),
            KiraFormVm.FiyatTurleri, KiraFormVm.KiralamaTurleri, KiraFormVm.FaturalamaTipleri,
            KiraFormVm.Dovizler, KiraFormVm.OdemeSekilleri,
            DateTimeOffset.UtcNow.AddYears(1)));

    private static readonly (string, string)[] HesaplaKurallari = [("Ek hizmet biçimi", "ek")];

    /// <summary>
    /// Canlı hesap — Blazor <c>GET /kiralar/hesapla</c> ile AYNI motor (<see cref="KiraHesapService"/> →
    /// PricingService/RentalQuoteEngine); persist SIFIR. <c>ok:false</c> (kullanıcı yazarken nazik geri bildirim)
    /// 200 ile döner — Blazor sözleşmesi. Servis istisnası: YetkiYok 403, doğrulama 400.
    /// <c>ek</c> biçimi <c>tanimId:miktar,tanimId:miktar</c> (miktar nokta ondalık); bozuk çift 400 (Blazor sessiz atlar).
    /// </summary>
    private static async Task<Ok<KiraHesapSonuc>> Hesapla(
        KiraHesapService hesap, RentalService kiralar, DateTimeOffset basTar, DateTimeOffset bitTar, Guid? vehicleId,
        decimal? gunlukUcret, string? fiyatTuru, string? doviz, string? cikisOfisi, string? donusOfisi, decimal? dropUcreti,
        string? ek, Guid? rentalId, Guid? musteriId, string? kampanyaKodu, Guid? ikinciSurucuId, CancellationToken ct)
    {
        // Üst kayıt kapısı: servis rentalId kapsamını yalnız fiyatlama BAŞARILIYSA denetler; uç her durumda önce
        // denetler (kapsam dışı → 403). Bulunamayan kira servis sözleşmesiyle aynı: tahsilatsız hesap.
        if (rentalId is Guid rid) await KapsamliAsync(kiralar, rid, ct);
        try
        {
            return TypedResults.Ok(await hesap.HesaplaAsync(new KiraHesapIstek(
                // Low-B: oluşturma ucu UTC'ye çevirdiği için önizleme de AYNI anı UTC ile hesaplar (önizleme == kayıt).
                VehicleId: vehicleId, BasTar: basTar.ToUniversalTime(), BitTar: bitTar.ToUniversalTime(), GunlukUcret: gunlukUcret,
                FiyatTuru: fiyatTuru, Doviz: doviz, CikisOfisi: cikisOfisi,
                MusteriId: musteriId, KampanyaKodu: kampanyaKodu, IkinciSurucuId: ikinciSurucuId,
                DonusOfisi: donusOfisi, DropUcreti: dropUcreti,
                EkHizmetler: EkSecimCoz(ek), RentalId: rentalId), ct));
        }
        catch (OverflowException)
        {
            throw new ValidationException("Girilen değerler hesaplanamayacak kadar büyük.");
        }
    }

    /// <summary>"tanimId:miktar,…" → seçim listesi. Blazor'dan farklı olarak bozuk çift SESSİZ atlanmaz (400).</summary>
    internal static IReadOnlyList<KiraHesapEkHizmet> EkSecimCoz(string? ek)
    {
        if (string.IsNullOrWhiteSpace(ek)) return [];
        var liste = new List<KiraHesapEkHizmet>();
        foreach (var parca in ek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = parca.IndexOf(':');
            if (i <= 0 || !Guid.TryParse(parca[..i], out var id)
                || !decimal.TryParse(parca[(i + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out var miktar))
                throw new ValidationException("Ek hizmet biçimi geçersiz: 'tanimId:miktar' çiftleri virgülle ayrılmalı (nokta ondalık).");
            liste.Add(new KiraHesapEkHizmet(id, miktar));
        }
        return liste;
    }

    /// <summary>Dönüş canlı önizlemesi — <see cref="RentalService.PreviewReturnAsync"/> (ReturnMath; persist yok).</summary>
    private static async Task<Results<Ok<KiraDonusOnizleme>, ProblemHttpResult>> DonusHesapla(
        Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus, int? kmHediye,
        RentalService kiralar, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (donusYakit is < 0 or > FormYakitEnFazla) // nazik önizleme sözleşmesi: ok:false (Blazor ile aynı)
            return TypedResults.Ok(KiraDonusOnizleme.Hatali($"Dönüş yakıt 0-{FormYakitEnFazla} aralığında olmalıdır."));
        return TypedResults.Ok(await kiralar.PreviewReturnAsync(id, donusKm, donusYakit, gercekDonus.ToUniversalTime(), kmHediye ?? 0, ct));
    }

    /// <summary>Ek hizmet kataloğu üst sınırı (kira başına kalem sınırı 50; katalog makul bir tavanla kesilir).</summary>
    public const int EkHizmetKatalogSiniri = 200;

    /// <summary>
    /// F4.3b — Blazor ek hizmet matrisinin satırları: AKTİF tanımlar, ad sırasıyla; SYS-* sistem ücret satırları
    /// HARİÇ (manuel seçilemez — Blazor matriste gösterip kayıtta reddediyordu). Yalnız gösterim: birim NET + KDV
    /// oranı; satır tutarı ve toplam <c>hesapla</c>'dan gelir (UI formül taşımaz).
    /// </summary>
    private static async Task<Ok<KiraEkHizmetKatalogu>> EkHizmetKatalogu(EkHizmetTanimService tanimlar, CancellationToken ct)
    {
        var liste = (await tanimlar.ListActiveAsync(ct))
            .Where(t => !SistemKalemi(t.Kod))
            .OrderBy(t => t.Ad, StringComparer.Create(Tr, false)).ThenBy(t => t.Kod, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new KiraEkHizmetKatalogu(
            liste.Take(EkHizmetKatalogSiniri)
                .Select(t => new EkHizmetKatalogOgesi(t.Id, t.Kod, t.Ad, t.BirimUcret, t.KdvOrani, t.Aciklama, t.MaxGun))
                .ToList(),
            liste.Count));
    }

    /// <summary>Müsait araçlar (Blazor <c>/kiralar/musait-arac</c>): takvim günleri UTC gün başına çevrilir (aynı kural).</summary>
    private static async Task<Ok<IReadOnlyList<MusaitAracDto>>> MusaitArac(
        AvailabilityService musaitlik, DateOnly? vfrom, DateOnly? vto, string? vgrup, CancellationToken ct)
    {
        if (vfrom is not { } bas || vto is not { } bit || bit <= bas)
            throw new ValidationException("Geçerli bir müsaitlik aralığı girin (bitiş > başlangıç).", "vto");
        var araclar = await musaitlik.FindAvailableAsync(
            new DateTimeOffset(bas.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(bit.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            string.IsNullOrWhiteSpace(vgrup) ? null : vgrup, ct: ct);
        return TypedResults.Ok<IReadOnlyList<MusaitAracDto>>(araclar.Select(v => new MusaitAracDto(
            v.Id, v.Plaka, v.Marka, v.Tip, v.ModelYili, v.Vites?.ToString(), v.Yakit?.ToString(),
            v.Grup, v.Segment, v.Km, v.Sube, v.Konum)).ToList());
    }

    // ================================================================== yazmalar

    private const int EnFazlaEkKalem = 50;

    /// <summary>Lim (BookingMath.Kirp) etiketleri + giriş guard'ları → gövde alanı.</summary>
    private static readonly (string, string)[] OrtakKiraKurallari =
    [
        ("2. sürücü ya kayıtlı cariden", "ikinciSurucuId"),
        ("2. sürücü müşteriyle aynı olamaz", "ikinciSurucuId"),
        ("2. sürücü (cari) bulunamadı", "ikinciSurucuId"),
        ("2. sürücü adı en fazla", "ikinciSurucuSerbestAd"),
        ("2. sürücü soyadı en fazla", "ikinciSurucuSerbestSoyad"),
        ("2. sürücü telefonu en fazla", "ikinciSurucuSerbestTel"),
        ("2. sürücü ehliyet sınıfı en fazla", "ikinciSurucuSerbestEhliyetSinifi"),
        ("Drop ücreti negatif olamaz", "dropUcreti"),
        ("Özel KDV oranı", "ozelKdvOran"),
        ("Net fiyat modlu kirada özel KDV", "ozelKdvOran"),
        ("Damga vergisi negatif", "damgaVergisi"),
        ("Findeks puanı negatif", "manuelFindexPuan"),
        ("Çıkış ofisi en fazla", "cikisOfisi"),
        ("Dönüş ofisi en fazla", "donusOfisi"),
        ("Açıklama en fazla", "aciklama"),
        ("Kaynak en fazla", "kaynak"),
        ("Uyarı açıklama en fazla", "uyariAciklama"),
        ("Özel fatura açıklaması en fazla", "ozelFaturaAciklama"),
        ("Uçuş no en fazla", "ucusNo"),
        ("Provizyon no en fazla", "provizyonNo"),
        ("Onay kodu en fazla", "onayKodu"),
        ("Firma kodu en fazla", "firmaKodu"),
        ("Proje adı en fazla", "projeAdi"),
        ("Özel kod en fazla", "ozelKod"),
        ("Talep türü en fazla", "talepTuru"),
        ("Geldiği birim en fazla", "geldigiBirim"),
        ("Kefil bilgisi en fazla", "kefilBilgisi"),
        ("Assist firma en fazla", "assistFirma"),
        ("Özel şoför bilgisi en fazla", "ozelSoforBilgisi"),
        ("Ek koşullar en fazla", "ekKosullar"),
        ("Lastik durumu (çıkış) en fazla", "aksLastikCikis"),
        ("Lastik durumu (dönüş) en fazla", "aksLastikDonus"),
        ("Ödeme şekli en fazla", "odemeSekli"),
        ("Kiralama türü en fazla", "kiralamaTuru"),
        ("Faturalama tipi en fazla", "faturalamaTipi"),
    ];

    private static readonly (string, string)[] OlusturKurallari =
    [
        ("Müşteri seçilmelidir", "musteriId"),
        ("Araç seçilmelidir", "vehicleId"),
        ("Bitiş tarihi başlangıçtan sonra", "bitTar"),
        ("Kira başlangıcı en fazla", "basTar"),
        ("Günlük ücret negatif", "gunlukUcret"),
        ("Risk limiti aşıldı", "riskOnay"),
        ("Risk onayı yalnız", "riskOnay"),
        ("Kampanya kodu", "kampanyaKodu"),
        ("Kira süresi en fazla", "bitTar"),
        ("Müşteri bulunamadı", "musteriId"),
        ("Araç bulunamadı", "vehicleId"),
        ("Çıkış ofisi zorunludur", "cikisOfisi"),
        ("Seçilen ek hizmet", "ekHizmetler"),
        ("Aynı ek hizmet", "ekHizmetler"),
        ("En fazla 50 ek hizmet", "ekHizmetler"),
        .. OrtakKiraKurallari,
    ];

    private static readonly (string, string)[] GuncelleKurallari =
    [
        ("KM limit negatif", "kmLimit"),
        ("Aşım ücretleri negatif", "fazlaKmUcret"),
        ("Tamamlanmış kirada aşım parametreleri", "kmLimit"),
        ("Tamamlanmış kirada 2. sürücü", "ikinciSurucuId"),
        ("Tamamlanmış kirada ofisler", "cikisOfisi"),
        ("Tamamlanmış kirada drop ücreti", "dropUcreti"),
        ("Faturalanmış kirada drop ücreti", "dropUcreti"),
        .. OrtakKiraKurallari,
    ];

    private static readonly (string, string)[] TeslimKurallari =
        [("Çıkış KM negatif", "cikisKm"), ("Çıkış yakıt", "cikisYakit")];

    private static readonly (string, string)[] DonusKurallari =
    [
        ("Dönüş KM", "donusKm"),
        ("Dönüş yakıt", "donusYakit"),
        ("Gerçek dönüş tarihi", "gercekDonus"),
        ("KM farkı gerçekçi değil", "donusKm"),
        ("Dönüş tarihi başlangıçtan önce", "gercekDonus"),
        ("KM hediye", "kmHediye"),
        ("Bitiş sebebi", "bitisSebebi"),
        ("Teslim alan personel", "teslimAlanPersonelId"),
    ];

    private static readonly (string, string)[] UzatKurallari =
    [
        ("Yeni bitiş tarihi", "yeniBitTar"),
        ("Uzatma en az 1 gün", "yeniBitTar"),
        ("Kira süresi en fazla", "yeniBitTar"),
    ];

    private static readonly (string, string)[] ProvizyonKurallari =
    [
        ("Kapama tutarı negatif", "kapamaTutar"),
        ("Önce Fiyat sekmesinde provizyon", "provizyon"),
    ];

    private static readonly (string, string)[] EkHizmetKurallari =
    [
        ("Miktar", "miktar"),
        ("Ek hizmet tanımı bulunamadı", "ekHizmetTanimId"),
        ("Sistem ücret kalemi", "ekHizmetTanimId"),
    ];

    private static readonly (string, string)[] MusteriKurallari =
    [
        ("Müşteri seçin ya da yeni müşteri", "ad"),
        ("Bireysel cari için Ad", "ad"),
        ("TC Kimlik No geçersiz", "tcKimlik"),
        ("Kurumsal/Servis cari için Ünvan", "unvan"),
        ("E-posta adresi geçersiz", "email"),
        ("Doğum tarihi", "dogumTarihi"),
        ("Ehliyet tarihi", "ehliyetTarihi"),
    ];

    /// <summary>
    /// Yeni kira — Blazor <c>/kiralar/create</c> akışı: ek hizmet tanımları kira AÇILMADAN doğrulanır; kira
    /// <see cref="RentalService.CreateDirectAsync"/> ile açılır; kalemler aynı <see cref="RentalAddOnService.AddAsync"/>
    /// yolundan (tanım snapshot + RentalTotals). Kalem eklemesi yarıda kalırsa kira DURUR ve <c>uyari</c> döner.
    /// </summary>
    private static async Task<Created<KiraOlusturYaniti>> Olustur(
        KiraOlusturIstegi istek, RentalService kiralar, RentalAddOnService ekler, EkHizmetTanimService ekTanimlar,
        ICustomerRepository musteriDeposu, IVehicleRepository aracDeposu, CancellationToken ct)
    {
        var secim = (istek.EkHizmetler ?? []).Select(e => (e.TanimId, Miktar: e.Miktar is > 0m ? e.Miktar.Value : 1m)).ToList();
        if (secim.Count > EnFazlaEkKalem)
            throw new ValidationException($"En fazla {EnFazlaEkKalem} ek hizmet seçilebilir.");
        if (secim.Select(s => s.TanimId).Distinct().Count() != secim.Count)
            throw new ValidationException("Aynı ek hizmet birden fazla seçilemez (miktarı artırın).");
        if (secim.Count > 0)
        {
            var tanimlar = (await ekTanimlar.ListActiveAsync(ct)).ToDictionary(t => t.Id, t => t.Kod);
            if (secim.Any(e => !tanimlar.ContainsKey(e.TanimId)))
                throw new ValidationException("Seçilen ek hizmet tanımı bulunamadı (silinmiş/pasif olabilir).");
            // SYS-* satırları yalnız FeeLineService yazar; servis manuel eklemeyi kira AÇILDIKTAN sonra reddederdi
            // (yarım kayıt + uyarı) — kira açılmadan önce temiz red.
            if (secim.Any(e => SistemKalemi(tanimlar[e.TanimId])))
                throw new ValidationException("Seçilen ek hizmet sistem ücret kalemi; manuel seçilemez (otomatik hesaplanır).");
        }

        Sinirlar.Olustur(istek); // F4.1 adversarial L3: numeric(19,4)/metin taşması 500 yerine 400 + alan
        // F4.1 adversarial L5: müşteri ve araç bu kiracıda VAR olmalı (RLS kapsamlı okuma). Rentals.MusteriId /
        // VehicleId'de FK yok — başka kiracının ya da hiç olmayan kimlikle kira yazılabiliyordu. Kontrol UÇTA:
        // servis düzeyine almak, sentetik kimlikle kira kuran ~50 mevcut testi değiştirmeyi gerektiriyor (açık iş).
        if (await musteriDeposu.FindAsync(istek.MusteriId, ct) is null)
            throw new ValidationException("Müşteri bulunamadı.", "musteriId");
        if (await aracDeposu.FindAsync(istek.VehicleId, ct) is null)
            throw new ValidationException("Araç bulunamadı.", "vehicleId");
        var id = await kiralar.CreateDirectAsync(istek.ToInput(), ct);

        string? uyari = null;
        try
        {
            foreach (var (tanimId, miktar) in secim)
                await ekler.AddAsync(id, tanimId, miktar, ct: ct);
        }
        catch (ValidationException ex)
        {
            uyari = $"Kira açıldı ancak ek hizmet eklenemedi: {ex.Message}";
        }
        // Çıkış ofisinin kapsamı servis GİRİŞİNDE denetlenir (F4.1 adversarial M1): buraya gelen kira oturumun
        // kapsamındadır; kapsam dışı ofis 403, hiçbir şey yazılmaz.
        var no = (await kiralar.GetAsync(id, ct))?.SozlesmeNo ?? "";
        return TypedResults.Created($"{Kok}/{id}", new KiraOlusturYaniti(id, no, uyari));
    }

    /// <summary>Hızlı müşteri — Blazor <c>OlusturYeniCariAsync</c> ile aynı eşleme; PII CustomerService'te
    /// şifrelenir, TC benzersizliği 409 <c>cakisma</c>. Yanıtta YALNIZ kimlik + etiket.</summary>
    private static async Task<Ok<MusteriHizliYaniti>> MusteriOlustur(
        MusteriHizliIstegi istek, CustomerService musteriler, CancellationToken ct)
    {
        Sinirlar.Musteri(istek); // F4.1 L3: kolon uzunlukları (varchar taşması 500 yerine 400 + alan)
        var ad = KiraOlusturIstegi.Nz(istek.Ad);
        var unvan = KiraOlusturIstegi.Nz(istek.Unvan);
        var soyad = KiraOlusturIstegi.Nz(istek.Soyad);
        if (ad is null && unvan is null)
            throw new ValidationException("Müşteri seçin ya da yeni müşteri bilgilerini girin (en az Ad veya Ünvan).");
        var id = await musteriler.CreateAsync(new CustomerInput
        {
            Tip = unvan is null ? CariType.Bireysel : CariType.Kurumsal,
            Ad = ad,
            Soyad = soyad,
            Unvan = unvan,
            TcKimlik = KiraOlusturIstegi.Nz(istek.TcKimlik),
            CepTel = KiraOlusturIstegi.Nz(istek.CepTel),
            Email = KiraOlusturIstegi.Nz(istek.Email),
            Il = KiraOlusturIstegi.Nz(istek.Il),
            Ilce = KiraOlusturIstegi.Nz(istek.Ilce),
            DogumTarihi = istek.DogumTarihi?.ToUniversalTime(), // Low-B: DB'ye UTC
            EhliyetNo = KiraOlusturIstegi.Nz(istek.EhliyetNo),
            EhliyetSinifi = KiraOlusturIstegi.Nz(istek.EhliyetSinifi),
            EhliyetTarihi = istek.EhliyetTarihi?.ToUniversalTime(),
            EhliyetYeri = KiraOlusturIstegi.Nz(istek.EhliyetYeri),
        }, ct);
        return TypedResults.Ok(new MusteriHizliYaniti(id, unvan ?? $"{ad} {soyad}".Trim()));
    }

    /// <summary>Açık kira güncelleme — <see cref="RentalService.UpdateOpenAsync"/> (whitelist tip; para/tarih yok).</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Guncelle(
        Guid id, KiraGuncelleIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (string.IsNullOrWhiteSpace(istek.Surum)) // F4.3 adversarial F2: tam değiştirme sürümsüz yapılamaz
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        Sinirlar.Guncelle(istek); // F4.1 L3
        if (!await kiralar.UpdateOpenAsync(id, istek.ToInput(), ct)) return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Teslim(
        Guid id, TeslimIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (!await kiralar.DeliverAsync(id, Zorunlu(istek.CikisKm, "cikisKm", "Çıkış KM"),
                Yakit(istek.CikisYakit, "cikisYakit", "Çıkış yakıt"), ct))
            return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Donus(
        Guid id, DonusIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (!await kiralar.ReturnAsync(id, Zorunlu(istek.DonusKm, "donusKm", "Dönüş KM"),
                Yakit(istek.DonusYakit, "donusYakit", "Dönüş yakıt"), Zorunlu(istek.GercekDonus, "gercekDonus", "Gerçek dönüş tarihi").ToUniversalTime(), // Low-B: DB'ye UTC
                istek.KmHediye ?? 0, istek.BitisSebebi, istek.TeslimAlanPersonelId, ct))
            return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Uzat(
        Guid id, UzatIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (!await kiralar.ExtendAsync(id, Zorunlu(istek.YeniBitTar, "yeniBitTar", "Yeni bitiş tarihi").ToUniversalTime(), ct)) // Low-B: DB'ye UTC
            return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Iptal(
        Guid id, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (!await kiralar.CancelAsync(id, ct)) return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    /// <summary>Manuel provizyon (POS'suz, deftere yazmaz). Kapsam kapısı servisteki kaynak-kuralı okumasından
    /// ÖNCE çalışır — kapsam dışı kiranın kaynak kuralı hata metniyle sızmaz.</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> ProvizyonAl(
        Guid id, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        if (!await kiralar.ProvizyonAlAsync(id, ct)) return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> ProvizyonKapat(
        Guid id, ProvizyonKapatIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        Sinirlar.Tutar(istek.KapamaTutar, "kapamaTutar", "Kapama tutarı"); // F4.1 L3
        if (!await kiralar.ProvizyonKapatAsync(id, istek.KapamaTutar, istek.Iade, ct)) return Bulunamadi();
        return await Guncel(kiralar, depo, id, ct);
    }

    /// <summary>Ek hizmet kalemleri + güncel toplamlar (ekleme/silme sonrası SPA ikisini birden yeniler).</summary>
    public sealed record KiraEkHizmetYaniti(IReadOnlyList<EkHizmetKalemiDto> Kalemler, KiraSozlesmesiDto Kira);

    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> EkHizmetYaniti(
        RentalService kiralar, IBookingRepository depo, RentalAddOnService ekler, Guid id, CancellationToken ct)
    {
        var surum = await depo.RentalSurumuAsync(id, ct);
        var c = await kiralar.GetAsync(id, ct);
        if (c is null) return Bulunamadi();
        var kalemler = (await ekler.ListAsync(id, ct)).Select(EkHizmetDto).ToList();
        return TypedResults.Ok(new KiraEkHizmetYaniti(kalemler, KiraSozlesmesiDto.From(c, surum)));
    }

    /// <summary>
    /// Low-B: <c>Idempotency-Key</c> ZORUNLU (kira tutarını değiştiren para yüzeyi; anahtarsız çift gönderim iki
    /// kalem yazıyordu). Anahtar UUIDv5(tenant|user|başlık); aynı anahtarla ikinci istek 409 <c>mukerrer</c> +
    /// <c>mevcut</c> (bu kiranın kalemiyse; <c>ayniIcerik</c> tanım + miktar). Sıra: kapsam → anahtar → iş kuralları.
    /// </summary>
    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> EkHizmetEkle(
        Guid id, EkHizmetEkleIstegi istek, HttpContext http, RentalService kiralar, IBookingRepository depo,
        RentalAddOnService ekler, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var tanim = Zorunlu(istek.EkHizmetTanimId, "ekHizmetTanimId", "Ek hizmet");
        var miktar = Zorunlu(istek.Miktar, "miktar", "Miktar");
        var anahtar = RentACar.Web.Common.IdempotencyBasligi.ZorunluAnahtar(http);
        await ekler.AddAsync(id, tanim, miktar, ct: ct, islemAnahtari: anahtar);
        return await EkHizmetYaniti(kiralar, depo, ekler, id, ct);
    }

    private static bool SistemKalemi(string? kod) => kod?.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Kalem YALNIZ rotadaki kiraya aitse silinir (başka kiranın kalem kimliği bu rotadan silinemez).
    /// <b>SYS-* sistem ücret satırı (genç/ek sürücü, drop) bu uçtan silinemez:</b> servis manuel EKLEMEYİ zaten
    /// reddediyor; silme serbest kalsaydı ücret tek tıkla düşerdi (para kaçağı). Servis RemoveAsync'e guard
    /// konmadı çünkü FeeLineService yeniden fiyatlamada aynı yöntemi kullanıyor. Blazor formu bugün bu satırı
    /// silebiliyor (F4.6'da Blazor ekranı kalkınca kapanır).
    /// </summary>
    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> EkHizmetSil(
        Guid id, Guid kalemId, RentalService kiralar, IBookingRepository depo, RentalAddOnService ekler,
        EkHizmetTanimService ekTanimlar, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var kalem = (await ekler.ListAsync(id, ct)).FirstOrDefault(k => k.Id == kalemId);
        if (kalem is null) return Bulunamadi("Ek hizmet kalemi bulunamadı.");
        if (SistemKalemi((await ekTanimlar.GetAsync(kalem.EkHizmetTanimId, ct))?.Kod))
            throw new ValidationException("Sistem ücret kalemi manuel silinemez (sözleşme koşulları değişince otomatik güncellenir).");
        if (!await ekler.RemoveAsync(kalemId, ct)) return Bulunamadi("Ek hizmet kalemi bulunamadı.");
        return await EkHizmetYaniti(kiralar, depo, ekler, id, ct);
    }

    // ================================================================== paylaşım

    /// <summary>Paylaşım linki durumu. <c>Link = null</c> → aktif link yok.</summary>
    public sealed record KiraPaylasimYaniti(KiraPaylasimLinki? Link);

    public sealed record KiraPaylasimIptalYaniti(bool IptalEdildi);

    private static async Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> PaylasimDurumu(
        Guid id, RentalService kiralar, SozlesmePaylasimService paylasim, CancellationToken ct)
    {
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        return TypedResults.Ok(new KiraPaylasimYaniti(Link(await paylasim.DurumAsync(id, ct))));
    }

    /// <summary>Paylaş: aktif link varsa AYNISI döner. Anlık görüntü Blazor ucuyla aynı zincirden
    /// (SozlesmeService → PdfExportService.Contract — personelin bastığı nüsha ile aynı renderer).</summary>
    private static Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> Paylas(
        Guid id, RentalService kiralar, SozlesmeService sozlesme, PdfExportService pdf, SozlesmePaylasimService paylasim,
        CancellationToken ct)
        => PaylasimIsle(id, kiralar, sozlesme, pdf, ct, (no, bytes) => paylasim.PaylasAsync(id, no, bytes, ct));

    /// <summary>Yeni sürüm: ESKİ token ölür, YENİ token + tazelenmiş anlık görüntü.</summary>
    private static Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> PaylasimYeniSurum(
        Guid id, RentalService kiralar, SozlesmeService sozlesme, PdfExportService pdf, SozlesmePaylasimService paylasim,
        CancellationToken ct)
        => PaylasimIsle(id, kiralar, sozlesme, pdf, ct, (no, bytes) => paylasim.YeniSurumAsync(id, no, bytes, ct));

    private static async Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> PaylasimIsle(
        Guid id, RentalService kiralar, SozlesmeService sozlesme, PdfExportService pdf, CancellationToken ct,
        Func<string, byte[], Task<PaylasimDurum>> islem)
    {
        // SozlesmeService.GetAsync şube kapsamı UYGULAMAZ → önce kapsam kapısı.
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        var s = await sozlesme.GetAsync(id, ct);
        if (s is null) return Bulunamadi();
        return TypedResults.Ok(new KiraPaylasimYaniti(Link(await islem(s.SozlesmeNo, pdf.Contract(s)))));
    }

    private static async Task<Results<Ok<KiraPaylasimIptalYaniti>, ProblemHttpResult>> PaylasimIptal(
        Guid id, RentalService kiralar, SozlesmePaylasimService paylasim, CancellationToken ct)
    {
        // Servis kira kapsamı denetlemez (yalnız izin) → kapsam kapısı burada ZORUNLU.
        if (await KapsamliAsync(kiralar, id, ct) is null) return Bulunamadi();
        return TypedResults.Ok(new KiraPaylasimIptalYaniti(await paylasim.IptalEtAsync(id, ct)));
    }
}
