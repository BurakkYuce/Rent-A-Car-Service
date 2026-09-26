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
/// <see cref="NoPermissionException"/> → 403 <c>yetki_yok</c>; yok/başka kiracı → 404). Alt kayıt okumaları
/// (InvoiceService/PenaltyService/DisHizmetService "by rental" yöntemleri kapsam guard'sız) bu kapıdan
/// geçmeden ÇAĞRILMAZ. Servislerin kendi guard'ları ikinci savunma olarak aynen çalışır.</para>
///
/// <para><b>Çift gönderim:</b> bu fazdaki kira işlemleri defter yazmaz ve anahtar almaz; hepsi YAPISAL korunur
/// (aynı araç/tarihe ikinci kira → 409 <c>cakisma</c> [exclusion constraint]; teslim/dönüş/iptal/provizyon
/// durum geçişi → 400; uzatma "yeni bitiş mevcut bitişten sonra" → 400). İstisna: ek hizmet eklemede servis
/// anahtar desteklemez — Blazor'la AYNI davranış (iki tık iki kalem); SPA düğmeyi istek boyunca kilitler.</para>
/// </summary>
public static class RentalApi
{
    private const string Root = UiApiExtensions.V1 + "/kiralar";

    public static RouteGroupBuilder MapRentalApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/kiralar")
            .WithTags("Kira")
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);

        // ---- okumalar (OW veya FW)
        g.MapGet("", GetList).MapFields(ListRules);
        g.MapGet("/ozet", Summary);
        g.MapGet("/filtre-secenekleri", FilterOptions);
        g.MapGet("/{id:guid}", Detail);
        g.MapGet("/{id:guid}/faturalar", Invoices);
        g.MapGet("/{id:guid}/cezalar", PenaltiesAndHgs);
        g.MapGet("/{id:guid}/dis-hizmetler", OutsourcedServices);
        g.MapGet("/{id:guid}/kaynak-rezervasyon", SourceReservation);
        g.MapGet("/{id:guid}/karne-ozeti", ScorecardSummary).RequirePermission(Permission.FinanceWrite);
        g.MapGet("/{id:guid}/musteri-ozet", CustomerSummary); // F4.3b — maskeli cari özeti (okuma kapısı)

        // ---- OperationsWrite (Blazor /kiralar grubu)
        var ow = g.MapGroup("").RequirePermission(Permission.OperationsWrite);
        ow.MapGet("/form-varsayilanlari", FormDefaults);
        ow.MapGet("/hesapla", Calculate).MapFields(CalculateRules);
        ow.MapGet("/musait-arac", AvailableVehicle);
        ow.MapGet("/ek-hizmet-katalogu", AddOnCatalog); // F4.3b — matris (fiyat/KDV; tutar hesapla'dan)
        ow.MapGet("/{id:guid}/donus-hesapla", CalculateReturn);
        ow.MapGet("/{id:guid}/donem-plani", PeriodPlan);
        ow.MapGet("/{id:guid}/paylasim", ShareStatus);

        ow.MapPost("", Create).MapFields(CreateRules);
        ow.MapPost("/musteri", CreateCustomer).MapFields(CustomerRules);
        ow.MapPut("/{id:guid}", Update).MapFields(UpdateRules);
        ow.MapPost("/{id:guid}/teslim", Delivery).MapFields(DeliveryRules);
        ow.MapPost("/{id:guid}/donus", Return).MapFields(ReturnRules);
        ow.MapPost("/{id:guid}/uzat", Extend).MapFields(ExtendRules);
        ow.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        ow.MapPost("/{id:guid}/provizyon/al", TakePreAuth).MapFields(PreAuthRules);
        ow.MapPost("/{id:guid}/provizyon/kapat", ClosePreAuth).MapFields(PreAuthRules);
        ow.MapPost("/{id:guid}/ek-hizmetler", AddAddOn).MapFields(AddOnRules);
        ow.MapDelete("/{id:guid}/ek-hizmetler/{kalemId:guid}", DeleteAddOn);
        ow.MapPost("/{id:guid}/paylasim", Share);
        ow.MapPost("/{id:guid}/paylasim/yeni-surum", ShareNewVersion);
        ow.MapDelete("/{id:guid}/paylasim", CancelShare);
        return g;
    }

    // ================================================================== ortak

    /// <summary>Üst kayıt kapısı: şube kapsamlı okuma (kapsam dışı → YetkiYokException → 403).</summary>
    private static Task<RentalContract?> ComprehensiveAsync(RentalService rentals, Guid id, CancellationToken ct)
        => rentals.GetAsync(id, ct);

    private static ProblemHttpResult NotFoundProblem(string detail = "Kira sözleşmesi bulunamadı.")
        => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı");

    /// <summary>İşlem sonrası güncel sözleşme (servisin yazdığı değerler; hesap yok).</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Current(
        RentalService rentals, IBookingRepository store, Guid id, CancellationToken ct)
    {
        var version = await store.RentalVersionAsync(id, ct); // alanlardan ÖNCE (bkz. Surum)
        return await rentals.GetAsync(id, ct) is { } c ? TypedResults.Ok(KiraSozlesmesiDto.From(c, version)) : NotFoundProblem();
    }

    private static bool HasPermission(HttpContext http, Permission p) => AuthExtensions.HasPermission(http.User, p);

    /// <summary>F4.1 adversarial L2: gövdede eksik zorunlu alan → 400 <c>errors[alan]</c> (sessiz 0 değil).</summary>
    private static T Required<T>(T? value, string alan, string label) where T : struct
        => value ?? throw new ValidationException($"{label} zorunludur.", alan);

    /// <summary>Yakıt ölçeği 0–12 — TEK iç ölçek (<see cref="RentACar.Domain.Common.FuelScale"/>, Karar (3)).
    /// Servis aynı sınırı ayrıca zorlar; burada alan adıyla 400 <c>errors[alan]</c> üretmek için.</summary>
    public const int FormMaxFuel = RentACar.Domain.Common.FuelScale.Max;

    private static int Fuel(int? value, string alan, string label)
    {
        var y = Required(value, alan, label);
        if (y is < 0 or > FormMaxFuel)
            throw new ValidationException($"{label} 0-{FormMaxFuel} aralığında olmalıdır.", alan);
        return y;
    }

    /// <summary>Hızlı tahsilat verisi — Blazor pano/liste formuyla BİREBİR: anahtar
    /// <see cref="CollectionKey.Generate"/>(kira, bakiye, işlem sayısı), döviz kira dövizi (normalize), tutar bakiye.</summary>
    internal static TahsilatBilgisi CollectionData(Guid rentalId, Guid customerId, decimal balance, string? currency, int transactionCount)
        => new(CollectionKey.Generate(rentalId, balance, transactionCount), customerId, rentalId, ExchangeRateService.NormalizeCode(currency),
            Math.Round(balance, 2, MidpointRounding.AwayFromZero));

    // ================================================================== liste

    /// <summary>Sıralama beyaz listesi (RentalRow alanları). <c>sirala</c> yoksa servisin sırası korunur
    /// (oluşturma zamanı, yeniden eskiye — Blazor listesi ile aynı).</summary>
    private static readonly SortFieldMap<RentalRow> Map = SortFieldMap<RentalRow>
        .Create(r => r.Id)
        .Alan("sozlesmeNo", r => r.SozlesmeNo)
        // KVKK: sıralama da GÖRÜNEN ada göre (anonim carinin gerçek adı sıradan sızmasın).
        .Alan("musteri", r => r.MusteriAnonimAd ? CustomerView.AnonymousNameLabel : r.MusteriAd)
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

    private static readonly (string, string)[] ListRules = [("Geçersiz sıralama alanı", "sirala")];

    /// <summary>Blazor RentalList süzgeçleri (FAZ-46 dahil). Tarihler takvim günü; aralık İstanbul gününe göre.</summary>
    public sealed class RentalListFilter
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
            Durum = Name<RentalStatus>(Durum, "durum"),
            Faturali = Fatura,
            BaslangicMin = BasMin is { } mn ? DayStart(mn) : null,
            // F4.1 adversarial L7: gün sonu "< ertesi gün 00:00" (repo <= uygular; timestamptz µs hassasiyetinde
            // ertesi gün − 1 µs ≡ < ertesi gün). Eski AddSeconds(-1) 23:59:59.xxx kayıtlarını dışarıda bırakıyordu.
            BaslangicMax = BasMax is { } mx ? DayStart(mx.AddDays(1)).AddMicroseconds(-1) : null,
            Ofis = KiraOlusturIstegi.Nz(Ofis),
            TarihTuru = Name<DateListType>(TarihTuru, "tarihTuru"),
            OfisDurum = Name<OfficeStatus>(OfisDurum, "ofisDurum"),
            SahipGrup = KiraOlusturIstegi.Nz(Sahip),
            AracGrubu = KiraOlusturIstegi.Nz(Grup),
            RezKaynak = KiraOlusturIstegi.Nz(Kaynak),
            PersonelId = PersonelId,
        };

        /// <summary>Enum ADI (büyük/küçük harf duyarsız); sayı ya da tanımsız ad 400 (sessizce "filtre yok"a düşmez).</summary>
        private static T? Name<T>(string? value, string alan) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var d = value.Trim();
            if (!char.IsDigit(d[0]) && d[0] != '-' && Enum.TryParse<T>(d, ignoreCase: true, out var v) && Enum.IsDefined(v))
                return v;
            throw new ValidationException(
                $"Geçersiz {alan} değeri. İzin verilenler: {string.Join(", ", Enum.GetNames<T>())}.", alan);
        }

        /// <summary>Takvim gününün İstanbul gece yarısı, UTC olarak (Npgsql timestamptz yalnız offset 0 kabul eder).</summary>
        private static DateTimeOffset DayStart(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, TenantDay.Slice.GetUtcOffset(local)).ToUniversalTime();
        }
    }

    private static async Task<Ok<Sayfa<KiraListeSatiri>>> GetList(
        HttpContext http, RentalService kiralar, CashService kasa, [AsParameters] RentalListFilter f,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var request = new ListeIstegi(sayfa ?? 1, boyut ?? 50, sirala);
        var rows = await kiralar.SearchAsync(f.ToFilter(), ct);
        // Beyaz liste KAYIT sayılmadan önce (bilinmeyen alan boşa sorgu olmadan 400).
        IEnumerable<RentalRow> sorted = request.Sirala is null ? rows : Map.Apply(rows.AsQueryable(), request.Sirala);
        var total = rows.Count;
        var pageRows = request.Atla >= total ? [] : sorted.Skip((int)request.Atla).Take(request.Boyut).ToList();

        // "Tahsil Et": yalnız FinanceWrite ve tahsil edilebilir satır (RentalList.razor ile aynı koşul).
        var transactionCounts = new Dictionary<Guid, int>();
        var finance = HasPermission(http, Permission.FinanceWrite);
        var collectible = finance
            ? pageRows.Where(r => r.Bakiye > 0m && r.Durum != RentalStatus.Iptal).Select(r => r.Id).ToList()
            : [];
        if (collectible.Count > 0)
            transactionCounts = await kasa.GetRentalTransactionCountsAsync(collectible, ct);
        var collectSet = collectible.ToHashSet();

        var records = pageRows.Select(r => new KiraListeSatiri(
            r.Id, r.SozlesmeNo, r.MusteriId, CustomerView.ListName(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.BasTar, r.BitTar, r.VadeTar, r.Gun,
            r.HediyeGun, r.FaturalananGun, r.Tutar, r.Bakiye, r.Doviz, r.Kaynak, r.CikisOfisi, r.DonusOfisi,
            r.Provizyon, r.Depozito, r.KomisyonOran, r.KomisyonTutar, r.OnayKodu, r.ProjeAdi, r.AssistFirma,
            r.OzelSoforBilgisi, r.Durum.ToString(), r.Faturali,
            collectSet.Contains(r.Id)
                ? CollectionData(r.Id, r.MusteriId, r.Bakiye, r.Doviz, transactionCounts.GetValueOrDefault(r.Id))
                : null)).ToList();
        return TypedResults.Ok(new Sayfa<KiraListeSatiri>(records, total, request.Sayfa, request.Boyut));
    }

    private static async Task<Ok<KiraListeOzeti>> Summary(
        RentalService kiralar, [AsParameters] RentalListFilter f, CancellationToken ct)
    {
        var rows = await kiralar.SearchAsync(f.ToFilter(), ct);
        return TypedResults.Ok(new KiraListeOzeti(
            rows.Count, rows.Count(r => r.Durum == RentalStatus.Kirada), rows.Count(r => !r.Faturali)));
    }

    /// <summary>Sahip/grup öneri listeleri — RentalList.razor gibi araç kartlarından TÜRETİLİR (master yok).</summary>
    private static async Task<Ok<KiraFiltreSecenekleri>> FilterOptions(VehicleService araclar, CancellationToken ct)
    {
        var list = await araclar.ListAsync(ct);
        static List<string> Extract(IEnumerable<string?> values) => [.. values
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Create(new CultureInfo("tr-TR"), false))];
        return TypedResults.Ok(new KiraFiltreSecenekleri(Extract(list.Select(v => v.AracSahibi)), Extract(list.Select(v => v.Grup))));
    }

    // ================================================================== detay + alt kayıtlar

    private static async Task<Results<Ok<KiraDetayYaniti>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, RentalService kiralar, CustomerService musteriler, VehicleService araclar,
        BranchService subeler, PersonnelService personeller, RentalAddOnService ekler,
        ContractShareService paylasim, ExchangeRateService kurlar, IBookingRepository depo, PenaltyService cezalar,
        CashService kasa,
        CancellationToken ct)
    {
        // F4.3 adversarial F2: sürüm alanlardan ÖNCE okunur — arada yazım olursa istemcinin sürümü alanlarından
        // ESKİ olur ve sonraki PUT güvenli tarafta (409) kalır; tersi bayat alanı "güncel" gösterirdi.
        var version = await depo.RentalVersionAsync(id, ct);
        var c = await ComprehensiveAsync(kiralar, id, ct);
        if (c is null) return NotFoundProblem();

        var permission = new KiraYetkileri(
            HasPermission(http, Permission.OperationsWrite), HasPermission(http, Permission.OperationsDelete), HasPermission(http, Permission.FinanceWrite));

        // Müşteri: yalnız görünen ad (TC/ehliyet gibi şifreli alanlar YANITA GİRMEZ).
        var customer = await musteriler.GetAsync(c.MusteriId, ct);
        var second = c.IkinciSurucuId is Guid ik ? await musteriler.GetAsync(ik, ct) : null;

        // Araç kartı: KiraForm gibi şube kapsamlı araç listesinden (görünmeyen araçta kart boş, 403 değil).
        var vehicle = (await araclar.ListAsync(ct)).FirstOrDefault(v => v.Id == c.VehicleId);

        // Personel adları: PII'sız seçim listesi; yetkisiz rolde nazikçe boş (KiraForm'daki desen).
        IReadOnlyList<PersonelSecim> staff = [];
        try { staff = await personeller.ListForSelectAsync(ct); }
        catch (NoPermissionException) { /* ad çözülmez */ }
        string? StaffName(Guid? pid) => pid is Guid p && staff.FirstOrDefault(x => x.Id == p) is { } ps ? $"{ps.Ad} {ps.Soyad}" : null;

        var operationBranch = c.CikisSubeId is Guid sid ? (await subeler.GetAsync(sid, ct))?.Ad : null;
        var items = (await ekler.ListAsync(c.Id, ct)).Select(AddOnDto).ToList();

        KiraDovizBilgisi? currency = null;
        var code = ExchangeRateService.NormalizeCode(c.Doviz);
        if (code != "TRY")
        {
            try
            {
                var exchangeRate = await kurlar.GetRateAsync(code, ct: ct);
                currency = new KiraDovizBilgisi(code, exchangeRate, c.GenelToplam * exchangeRate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                currency = new KiraDovizBilgisi(code, null, null); // bilgi: güncel kur yok
            }
        }

        KiraPaylasimBari? bar = null;
        if (permission.Operasyon)
        {
            PaylasimDurum? status = null;
            try { status = await paylasim.StatusAsync(c.Id, ct); }
            catch (ValidationException) { status = null; }
            // PII tek kuraldan (MusteriGorunumu): KVKK Anonim* bayrakları ön-doldurmayı ve hitabı da kapsar.
            bar = new KiraPaylasimBari(Link(status), CustomerView.Phone(customer), CustomerView.Email(customer),
                $"Kira Sözleşmesi {c.SozlesmeNo}", CustomerView.ShareMessage(c, customer));
        }

        // F4.3b: gösterim toplamları SUNUCUDA (SPA toplama yapmaz). Cezalar kapsam kapısından SONRA okunur.
        var penaltyTotal = (await cezalar.ListByRentalAsync(c.Id, ct))
            .Where(p => p.Durum != PenaltyStatus.Iptal).Sum(p => p.Tutar);
        var totals = new KiraToplamlari(items.Sum(k => k.Toplam), penaltyTotal);

        // F4.4 sabit panel tahsilatı: deterministik anahtar SUNUCUDA (liste/pano ile aynı üretim). Yalnız
        // FinanceWrite ve iptal olmayan kira (FinansApi iptal kiraya tahsilatı zaten reddeder).
        TahsilatBilgisi? collection = null;
        if (permission.Finans && c.Durum != RentalStatus.Iptal)
        {
            var numbers = await kasa.GetRentalTransactionCountsAsync([c.Id], ct);
            collection = CollectionData(c.Id, c.MusteriId, c.Bakiye, c.Doviz, numbers.GetValueOrDefault(c.Id));
        }

        return TypedResults.Ok(new KiraDetayYaniti(
            KiraSozlesmesiDto.From(c, version),
            new KiraTarafDto(c.MusteriId, CustomerView.TarafAdi(customer)),
            second is null ? null : new KiraTarafDto(second.Id, CustomerView.TarafAdi(second)),
            vehicle is null ? null : new KiraAracDto(vehicle.Id, vehicle.Plaka, vehicle.Marka, vehicle.Tip, vehicle.ModelYili,
                vehicle.Vites?.ToString(), vehicle.Yakit?.ToString(), vehicle.Grup, vehicle.Segment, vehicle.Km, vehicle.Sube, vehicle.Konum),
            operationBranch,
            StaffName(c.TeslimAlanPersonelId),
            StaffName(c.TeslimEdenPersonelId),
            items,
            currency,
            bar,
            permission,
            totals,
            collection));
    }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// F4.3b — Müşteri sekmesinin salt-okunur cari özeti. Üst kayıt kapısından geçer (kapsam dışı 403, yok/başka
    /// kiracı 404); müşteri kiranın kendi <c>MusteriId</c>'sinden okunur (istemci başka cari soramaz). PII: bkz.
    /// <see cref="KiraMusteriOzeti"/> ve <see cref="CustomerView"/> (TEK kural: maske + KVKK Anonim* bayrakları).
    /// </summary>
    private static async Task<Results<Ok<KiraMusteriOzeti>, ProblemHttpResult>> CustomerSummary(
        Guid id, RentalService kiralar, CustomerService musteriler, CancellationToken ct)
    {
        var c = await ComprehensiveAsync(kiralar, id, ct);
        if (c is null) return NotFoundProblem();
        var m = await musteriler.GetAsync(c.MusteriId, ct);
        if (m is null) return NotFoundProblem("Müşteri bulunamadı.");
        return TypedResults.Ok(CustomerView.Summary(m));
    }

    private static EkHizmetKalemiDto AddOnDto(RentalAddOn a)
        => new(a.Id, a.EkHizmetTanimId, a.Ad, a.Miktar, a.BirimNetFiyat, a.KdvOrani, a.NetTutar, a.KdvTutar, a.Toplam);

    private static KiraPaylasimLinki? Link(PaylasimDurum? d)
        => d is null ? null : new KiraPaylasimLinki("/sozlesme/" + d.Token, d.ErisimSayisi, d.SonErisimUtc,
            d.OlusturmaUtc, d.AnlikGoruntuUtc, d.Bayat);

    private static async Task<Results<Ok<IReadOnlyList<KiraFaturaDto>>, ProblemHttpResult>> Invoices(
        Guid id, RentalService kiralar, InvoiceService faturalar, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        var list = await faturalar.ListByRentalAsync(id, ct); // kapsam kapısından SONRA (servis guard'sız)
        return TypedResults.Ok<IReadOnlyList<KiraFaturaDto>>(list.Select(f => new KiraFaturaDto(
            f.Id, f.No, f.Tarih, f.NetTutar, f.KdvTutar, f.GenelToplam, f.Currency, f.Durum.ToString(),
            f.IadeMi ? "İade" : f.KaynakKiraId is not null ? "Fark" : "Kira")).ToList());
    }

    private static async Task<Results<Ok<KiraCezaHgsYaniti>, ProblemHttpResult>> PenaltiesAndHgs(
        Guid id, RentalService kiralar, PenaltyService cezalar, VehicleService araclar, IHgsService hgs, CancellationToken ct)
    {
        var c = await ComprehensiveAsync(kiralar, id, ct);
        if (c is null) return NotFoundProblem();
        var list = (await cezalar.ListByRentalAsync(id, ct))
            .Select(p => new KiraCezaDto(p.Id, p.No, p.CezaTuru, p.Tutar, p.Kalan, p.Durum.ToString(), p.TebligTarihi)).ToList();
        // HGS: kira dönemi (gerçek dönüş varsa o) — KiraForm ile aynı; plaka görünür araç listesinden.
        var plate = (await araclar.ListAsync(ct)).FirstOrDefault(v => v.Id == c.VehicleId)?.Plaka;
        IReadOnlyList<HgsGecisDto> passages = [];
        if (!string.IsNullOrWhiteSpace(plate))
            passages = (await hgs.GetCrossingsAsync(plate, c.BasTar, c.GercekDonusTar ?? c.BitTar, ct))
                .Select(g => new HgsGecisDto(g.Zaman, g.Gecis, g.Tutar)).ToList();
        return TypedResults.Ok(new KiraCezaHgsYaniti(list, passages));
    }

    private static async Task<Results<Ok<IReadOnlyList<KiraDisHizmetDto>>, ProblemHttpResult>> OutsourcedServices(
        Guid id, RentalService kiralar, OutsourcedServiceService disHizmetler, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        var list = await disHizmetler.ListForRentalAsync(id, ct); // kapsam kapısından SONRA (servis guard'sız)
        return TypedResults.Ok<IReadOnlyList<KiraDisHizmetDto>>(list.Select(KiraDisHizmetDto.From).ToList());
    }

    private static async Task<Results<Ok<IReadOnlyList<KiraDonemDto>>, ProblemHttpResult>> PeriodPlan(
        Guid id, RentalService kiralar, InvoicePeriodPlanService donemPlan, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        var list = await donemPlan.PreviewAsync(id, ct); // plansız kira → boş liste
        return TypedResults.Ok<IReadOnlyList<KiraDonemDto>>(list.Select(KiraDonemDto.From).ToList());
    }

    private static async Task<Results<Ok<KaynakRezervasyonYaniti>, ProblemHttpResult>> SourceReservation(
        Guid id, RentalService kiralar, ReservationService rezervasyonlar, CancellationToken ct)
    {
        var c = await ComprehensiveAsync(kiralar, id, ct);
        if (c is null) return NotFoundProblem();
        if (c.ReservationId is not Guid resId) return TypedResults.Ok(new KaynakRezervasyonYaniti(null));
        var r = await rezervasyonlar.GetAsync(resId, ct); // rezervasyonun KENDİ kapsam guard'ı da çalışır
        return TypedResults.Ok(new KaynakRezervasyonYaniti(r is null ? null : new KaynakRezervasyonDto(
            r.Id, r.ReservationNo, r.Durum.ToString(), r.BasTar, r.BitTar, r.Kaynak, r.TalepTuru,
            r.OtaKiraBedeli, r.OtaDropBedeli, r.OtaBebekKoltugu, r.OtaNavigasyon, r.OtaLcf, r.OtaCdw, r.OtaScdw, r.OtaEkSurucu)));
    }

    private static async Task<Results<Ok<KarneOzetiDto>, ProblemHttpResult>> ScorecardSummary(
        Guid id, RentalService kiralar, ReportService raporlar, CancellationToken ct)
    {
        var c = await ComprehensiveAsync(kiralar, id, ct);
        if (c is null) return NotFoundProblem();
        var scorecard = await raporlar.GetVehicleScorecardAsync(c.VehicleId, ct: ct);
        return TypedResults.Ok(new KarneOzetiDto(c.VehicleId, scorecard?.Kpi.DolulukYuzde));
    }

    // ================================================================== form yardımcıları

    private static async Task<Ok<KiraFormVarsayilanlari>> FormDefaults(FormDefaultResolver varsayilanlar, CancellationToken ct)
        => TypedResults.Ok(new KiraFormVarsayilanlari(
            await varsayilanlar.PickupFuelAsync(ct),
            await varsayilanlar.PriceTypeAsync(ct),
            RentalFormVm.PriceTypes, RentalFormVm.RentalTypes, RentalFormVm.InvoicingTypes,
            RentalFormVm.Currencies, RentalFormVm.PaymentMethods,
            DateTimeOffset.UtcNow.AddYears(1)));

    private static readonly (string, string)[] CalculateRules = [("Ek hizmet biçimi", "ek")];

    /// <summary>
    /// Canlı hesap — Blazor <c>GET /kiralar/hesapla</c> ile AYNI motor (<see cref="RentalCalculationService"/> →
    /// PricingService/RentalQuoteEngine); persist SIFIR. <c>ok:false</c> (kullanıcı yazarken nazik geri bildirim)
    /// 200 ile döner — Blazor sözleşmesi. Servis istisnası: YetkiYok 403, doğrulama 400.
    /// <c>ek</c> biçimi <c>tanimId:miktar,tanimId:miktar</c> (miktar nokta ondalık); bozuk çift 400 (Blazor sessiz atlar).
    /// </summary>
    private static async Task<Ok<KiraHesapSonuc>> Calculate(
        RentalCalculationService hesap, RentalService kiralar, DateTimeOffset basTar, DateTimeOffset bitTar, Guid? vehicleId,
        decimal? gunlukUcret, string? fiyatTuru, string? doviz, string? cikisOfisi, string? donusOfisi, decimal? dropUcreti,
        string? ek, Guid? rentalId, Guid? musteriId, string? kampanyaKodu, Guid? ikinciSurucuId, CancellationToken ct)
    {
        // Üst kayıt kapısı: servis rentalId kapsamını yalnız fiyatlama BAŞARILIYSA denetler; uç her durumda önce
        // denetler (kapsam dışı → 403). Bulunamayan kira servis sözleşmesiyle aynı: tahsilatsız hesap.
        if (rentalId is Guid rid) await ComprehensiveAsync(kiralar, rid, ct);
        try
        {
            return TypedResults.Ok(await hesap.CalculateAsync(new KiraHesapIstek(
                // Low-B: oluşturma ucu UTC'ye çevirdiği için önizleme de AYNI anı UTC ile hesaplar (önizleme == kayıt).
                VehicleId: vehicleId, BasTar: basTar.ToUniversalTime(), BitTar: bitTar.ToUniversalTime(), GunlukUcret: gunlukUcret,
                FiyatTuru: fiyatTuru, Doviz: doviz, CikisOfisi: cikisOfisi,
                MusteriId: musteriId, KampanyaKodu: kampanyaKodu, IkinciSurucuId: ikinciSurucuId,
                DonusOfisi: donusOfisi, DropUcreti: dropUcreti,
                EkHizmetler: ResolveExtraSelection(ek), RentalId: rentalId), ct));
        }
        catch (OverflowException)
        {
            throw new ValidationException("Girilen değerler hesaplanamayacak kadar büyük.");
        }
    }

    /// <summary>"tanimId:miktar,…" → seçim listesi. Blazor'dan farklı olarak bozuk çift SESSİZ atlanmaz (400).</summary>
    internal static IReadOnlyList<KiraHesapEkHizmet> ResolveExtraSelection(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) return [];
        var list = new List<KiraHesapEkHizmet>();
        foreach (var part in extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf(':');
            if (i <= 0 || !Guid.TryParse(part[..i], out var id)
                || !decimal.TryParse(part[(i + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
                throw new ValidationException("Ek hizmet biçimi geçersiz: 'tanimId:miktar' çiftleri virgülle ayrılmalı (nokta ondalık).");
            list.Add(new KiraHesapEkHizmet(id, quantity));
        }
        return list;
    }

    /// <summary>Dönüş canlı önizlemesi — <see cref="RentalService.PreviewReturnAsync"/> (ReturnMath; persist yok).</summary>
    private static async Task<Results<Ok<KiraDonusOnizleme>, ProblemHttpResult>> CalculateReturn(
        Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus, int? kmHediye,
        RentalService kiralar, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (donusYakit is < 0 or > FormMaxFuel) // nazik önizleme sözleşmesi: ok:false (Blazor ile aynı)
            return TypedResults.Ok(KiraDonusOnizleme.Invalid($"Dönüş yakıt 0-{FormMaxFuel} aralığında olmalıdır."));
        return TypedResults.Ok(await kiralar.PreviewReturnAsync(id, donusKm, donusYakit, gercekDonus.ToUniversalTime(), kmHediye ?? 0, ct));
    }

    /// <summary>Ek hizmet kataloğu üst sınırı (kira başına kalem sınırı 50; katalog makul bir tavanla kesilir).</summary>
    public const int AddOnCatalogLimit = 200;

    /// <summary>
    /// F4.3b — Blazor ek hizmet matrisinin satırları: AKTİF tanımlar, ad sırasıyla; SYS-* sistem ücret satırları
    /// HARİÇ (manuel seçilemez — Blazor matriste gösterip kayıtta reddediyordu). Yalnız gösterim: birim NET + KDV
    /// oranı; satır tutarı ve toplam <c>hesapla</c>'dan gelir (UI formül taşımaz).
    /// </summary>
    private static async Task<Ok<KiraEkHizmetKatalogu>> AddOnCatalog(AddOnDefinitionService tanimlar, CancellationToken ct)
    {
        var list = (await tanimlar.ListActiveAsync(ct))
            .Where(t => !SystemItem(t.Kod))
            .OrderBy(t => t.Ad, StringComparer.Create(Tr, false)).ThenBy(t => t.Kod, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new KiraEkHizmetKatalogu(
            list.Take(AddOnCatalogLimit)
                .Select(t => new EkHizmetKatalogOgesi(t.Id, t.Kod, t.Ad, t.BirimUcret, t.KdvOrani, t.Aciklama, t.MaxGun))
                .ToList(),
            list.Count));
    }

    /// <summary>Müsait araçlar (Blazor <c>/kiralar/musait-arac</c>): takvim günleri UTC gün başına çevrilir (aynı kural).</summary>
    private static async Task<Ok<IReadOnlyList<MusaitAracDto>>> AvailableVehicle(
        AvailabilityService musaitlik, DateOnly? vfrom, DateOnly? vto, string? vgrup, CancellationToken ct)
    {
        if (vfrom is not { } start || vto is not { } bit || bit <= start)
            throw new ValidationException("Geçerli bir müsaitlik aralığı girin (bitiş > başlangıç).", "vto");
        var vehicles = await musaitlik.FindAvailableAsync(
            new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(bit.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            string.IsNullOrWhiteSpace(vgrup) ? null : vgrup, ct: ct);
        return TypedResults.Ok<IReadOnlyList<MusaitAracDto>>(vehicles.Select(v => new MusaitAracDto(
            v.Id, v.Plaka, v.Marka, v.Tip, v.ModelYili, v.Vites?.ToString(), v.Yakit?.ToString(),
            v.Grup, v.Segment, v.Km, v.Sube, v.Konum)).ToList());
    }

    // ================================================================== yazmalar

    private const int MaxExtraItems = 50;

    /// <summary>Lim (BookingMath.Kirp) etiketleri + giriş guard'ları → gövde alanı.</summary>
    private static readonly (string, string)[] SharedRentalRules =
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

    private static readonly (string, string)[] CreateRules =
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
        .. SharedRentalRules,
    ];

    private static readonly (string, string)[] UpdateRules =
    [
        ("KM limit negatif", "kmLimit"),
        ("Aşım ücretleri negatif", "fazlaKmUcret"),
        ("Tamamlanmış kirada aşım parametreleri", "kmLimit"),
        ("Tamamlanmış kirada 2. sürücü", "ikinciSurucuId"),
        ("Tamamlanmış kirada ofisler", "cikisOfisi"),
        ("Tamamlanmış kirada drop ücreti", "dropUcreti"),
        ("Faturalanmış kirada drop ücreti", "dropUcreti"),
        .. SharedRentalRules,
    ];

    private static readonly (string, string)[] DeliveryRules =
        [("Çıkış KM negatif", "cikisKm"), ("Çıkış yakıt", "cikisYakit")];

    private static readonly (string, string)[] ReturnRules =
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

    private static readonly (string, string)[] ExtendRules =
    [
        ("Yeni bitiş tarihi", "yeniBitTar"),
        ("Uzatma en az 1 gün", "yeniBitTar"),
        ("Kira süresi en fazla", "yeniBitTar"),
    ];

    private static readonly (string, string)[] PreAuthRules =
    [
        ("Kapama tutarı negatif", "kapamaTutar"),
        ("Önce Fiyat sekmesinde provizyon", "provizyon"),
    ];

    private static readonly (string, string)[] AddOnRules =
    [
        ("Miktar", "miktar"),
        ("Ek hizmet tanımı bulunamadı", "ekHizmetTanimId"),
        ("Sistem ücret kalemi", "ekHizmetTanimId"),
    ];

    private static readonly (string, string)[] CustomerRules =
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
    private static async Task<Created<KiraOlusturYaniti>> Create(
        KiraOlusturIstegi istek, RentalService kiralar, RentalAddOnService ekler, AddOnDefinitionService ekTanimlar,
        CancellationToken ct)
    {
        var selection = (istek.EkHizmetler ?? []).Select(e => (e.TanimId, Miktar: e.Miktar is > 0m ? e.Miktar.Value : 1m)).ToList();
        if (selection.Count > MaxExtraItems)
            throw new ValidationException($"En fazla {MaxExtraItems} ek hizmet seçilebilir.");
        if (selection.Select(s => s.TanimId).Distinct().Count() != selection.Count)
            throw new ValidationException("Aynı ek hizmet birden fazla seçilemez (miktarı artırın).");
        if (selection.Count > 0)
        {
            var definitions = (await ekTanimlar.ListActiveAsync(ct)).ToDictionary(t => t.Id, t => t.Kod);
            if (selection.Any(e => !definitions.ContainsKey(e.TanimId)))
                throw new ValidationException("Seçilen ek hizmet tanımı bulunamadı (silinmiş/pasif olabilir).");
            // SYS-* satırları yalnız FeeLineService yazar; servis manuel eklemeyi kira AÇILDIKTAN sonra reddederdi
            // (yarım kayıt + uyarı) — kira açılmadan önce temiz red.
            if (selection.Any(e => SystemItem(definitions[e.TanimId])))
                throw new ValidationException("Seçilen ek hizmet sistem ücret kalemi; manuel seçilemez (otomatik hesaplanır).");
        }

        RentalLimits.Create(istek); // F4.1 adversarial L3: numeric(19,4)/metin taşması 500 yerine 400 + alan
        // F4.1 adversarial L5 (müşteri/araç bu kiracıda VAR olmalı) artık RentalService.CreateDirectAsync
        // girişinde — tüm yollar (harici API, Blazor, bu uç) aynı kuraldan geçer.
        var id = await kiralar.CreateDirectAsync(istek.ToInput(), ct);

        string? warning = null;
        try
        {
            foreach (var (definitionId, quantity) in selection)
                await ekler.AddAsync(id, definitionId, quantity, ct: ct);
        }
        catch (ValidationException ex)
        {
            warning = $"Kira açıldı ancak ek hizmet eklenemedi: {ex.Message}";
        }
        // Çıkış ofisinin kapsamı servis GİRİŞİNDE denetlenir (F4.1 adversarial M1): buraya gelen kira oturumun
        // kapsamındadır; kapsam dışı ofis 403, hiçbir şey yazılmaz.
        var no = (await kiralar.GetAsync(id, ct))?.SozlesmeNo ?? "";
        return TypedResults.Created($"{Root}/{id}", new KiraOlusturYaniti(id, no, warning));
    }

    /// <summary>Hızlı müşteri — Blazor <c>OlusturYeniCariAsync</c> ile aynı eşleme; PII CustomerService'te
    /// şifrelenir, TC benzersizliği 409 <c>cakisma</c>. Yanıtta YALNIZ kimlik + etiket.</summary>
    private static async Task<Ok<MusteriHizliYaniti>> CreateCustomer(
        MusteriHizliIstegi istek, CustomerService musteriler, CancellationToken ct)
    {
        RentalLimits.Customer(istek); // F4.1 L3: kolon uzunlukları (varchar taşması 500 yerine 400 + alan)
        var name = KiraOlusturIstegi.Nz(istek.Ad);
        var title = KiraOlusturIstegi.Nz(istek.Unvan);
        var soyad = KiraOlusturIstegi.Nz(istek.Soyad);
        if (name is null && title is null)
            throw new ValidationException("Müşteri seçin ya da yeni müşteri bilgilerini girin (en az Ad veya Ünvan).");
        var id = await musteriler.CreateAsync(new CustomerInput
        {
            Tip = title is null ? CustomerType.Bireysel : CustomerType.Kurumsal,
            Ad = name,
            Soyad = soyad,
            Unvan = title,
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
        return TypedResults.Ok(new MusteriHizliYaniti(id, title ?? $"{name} {soyad}".Trim()));
    }

    /// <summary>Açık kira güncelleme — <see cref="RentalService.UpdateOpenAsync"/> (whitelist tip; para/tarih yok).</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Update(
        Guid id, KiraGuncelleIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (string.IsNullOrWhiteSpace(istek.Surum)) // F4.3 adversarial F2: tam değiştirme sürümsüz yapılamaz
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        RentalLimits.Update(istek); // F4.1 L3
        if (!await kiralar.UpdateOpenAsync(id, istek.ToInput(), ct)) return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Delivery(
        Guid id, TeslimIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (!await kiralar.DeliverAsync(id, Required(istek.CikisKm, "cikisKm", "Çıkış KM"),
                Fuel(istek.CikisYakit, "cikisYakit", "Çıkış yakıt"), ct))
            return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Return(
        Guid id, DonusIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (!await kiralar.ReturnAsync(id, Required(istek.DonusKm, "donusKm", "Dönüş KM"),
                Fuel(istek.DonusYakit, "donusYakit", "Dönüş yakıt"), Required(istek.GercekDonus, "gercekDonus", "Gerçek dönüş tarihi").ToUniversalTime(), // Low-B: DB'ye UTC
                istek.KmHediye ?? 0, istek.BitisSebebi, istek.TeslimAlanPersonelId, ct))
            return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Extend(
        Guid id, UzatIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (!await kiralar.ExtendAsync(id, Required(istek.YeniBitTar, "yeniBitTar", "Yeni bitiş tarihi").ToUniversalTime(), ct)) // Low-B: DB'ye UTC
            return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> Cancel(
        Guid id, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (!await kiralar.CancelAsync(id, ct)) return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    /// <summary>Manuel provizyon (POS'suz, deftere yazmaz). Kapsam kapısı servisteki kaynak-kuralı okumasından
    /// ÖNCE çalışır — kapsam dışı kiranın kaynak kuralı hata metniyle sızmaz.</summary>
    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> TakePreAuth(
        Guid id, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        if (!await kiralar.TakePreAuthAsync(id, ct)) return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    private static async Task<Results<Ok<KiraSozlesmesiDto>, ProblemHttpResult>> ClosePreAuth(
        Guid id, ProvizyonKapatIstegi istek, RentalService kiralar, IBookingRepository depo, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        RentalLimits.Amount(istek.KapamaTutar, "kapamaTutar", "Kapama tutarı"); // F4.1 L3
        if (!await kiralar.ClosePreAuthAsync(id, istek.KapamaTutar, istek.Iade, ct)) return NotFoundProblem();
        return await Current(kiralar, depo, id, ct);
    }

    /// <summary>Ek hizmet kalemleri + güncel toplamlar (ekleme/silme sonrası SPA ikisini birden yeniler).</summary>
    public sealed record KiraEkHizmetYaniti(IReadOnlyList<EkHizmetKalemiDto> Kalemler, KiraSozlesmesiDto Kira);

    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> AddOnResponse(
        RentalService rentals, IBookingRepository store, RentalAddOnService attachments, Guid id, CancellationToken ct)
    {
        var version = await store.RentalVersionAsync(id, ct);
        var c = await rentals.GetAsync(id, ct);
        if (c is null) return NotFoundProblem();
        var items = (await attachments.ListAsync(id, ct)).Select(AddOnDto).ToList();
        return TypedResults.Ok(new KiraEkHizmetYaniti(items, KiraSozlesmesiDto.From(c, version)));
    }

    /// <summary>
    /// Low-B: <c>Idempotency-Key</c> ZORUNLU (kira tutarını değiştiren para yüzeyi; anahtarsız çift gönderim iki
    /// kalem yazıyordu). Anahtar UUIDv5(tenant|user|başlık); aynı anahtarla ikinci istek 409 <c>mukerrer</c> +
    /// <c>mevcut</c> (bu kiranın kalemiyse; <c>ayniIcerik</c> tanım + miktar). Sıra: kapsam → anahtar → iş kuralları.
    /// </summary>
    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> AddAddOn(
        Guid id, EkHizmetEkleIstegi istek, HttpContext http, RentalService kiralar, IBookingRepository depo,
        RentalAddOnService ekler, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        var definition = Required(istek.EkHizmetTanimId, "ekHizmetTanimId", "Ek hizmet");
        var quantity = Required(istek.Miktar, "miktar", "Miktar");
        var key = RentACar.Web.Common.IdempotencyHeader.RequiredKey(http);
        await ekler.AddAsync(id, definition, quantity, ct: ct, operationKey: key);
        return await AddOnResponse(kiralar, depo, ekler, id, ct);
    }

    private static bool SystemItem(string? code) => code?.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Kalem YALNIZ rotadaki kiraya aitse silinir (başka kiranın kalem kimliği bu rotadan silinemez).
    /// <b>SYS-* sistem ücret satırı (genç/ek sürücü, drop) bu uçtan silinemez:</b> servis manuel EKLEMEYİ zaten
    /// reddediyor; silme serbest kalsaydı ücret tek tıkla düşerdi (para kaçağı). Servis RemoveAsync'e guard
    /// konmadı çünkü FeeLineService yeniden fiyatlamada aynı yöntemi kullanıyor. Blazor formu bugün bu satırı
    /// silebiliyor (F4.6'da Blazor ekranı kalkınca kapanır).
    /// </summary>
    private static async Task<Results<Ok<KiraEkHizmetYaniti>, ProblemHttpResult>> DeleteAddOn(
        Guid id, Guid kalemId, RentalService kiralar, IBookingRepository depo, RentalAddOnService ekler,
        AddOnDefinitionService ekTanimlar, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        var item = (await ekler.ListAsync(id, ct)).FirstOrDefault(k => k.Id == kalemId);
        if (item is null) return NotFoundProblem("Ek hizmet kalemi bulunamadı.");
        if (SystemItem((await ekTanimlar.GetAsync(item.EkHizmetTanimId, ct))?.Kod))
            throw new ValidationException("Sistem ücret kalemi manuel silinemez (sözleşme koşulları değişince otomatik güncellenir).");
        if (!await ekler.RemoveAsync(kalemId, ct)) return NotFoundProblem("Ek hizmet kalemi bulunamadı.");
        return await AddOnResponse(kiralar, depo, ekler, id, ct);
    }

    // ================================================================== paylaşım

    /// <summary>Paylaşım linki durumu. <c>Link = null</c> → aktif link yok.</summary>
    public sealed record KiraPaylasimYaniti(KiraPaylasimLinki? Link);

    public sealed record KiraPaylasimIptalYaniti(bool IptalEdildi);

    private static async Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> ShareStatus(
        Guid id, RentalService kiralar, ContractShareService paylasim, CancellationToken ct)
    {
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        return TypedResults.Ok(new KiraPaylasimYaniti(Link(await paylasim.StatusAsync(id, ct))));
    }

    /// <summary>Paylaş: aktif link varsa AYNISI döner. Anlık görüntü Blazor ucuyla aynı zincirden
    /// (SozlesmeService → PdfExportService.Contract — personelin bastığı nüsha ile aynı renderer).</summary>
    private static Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> Share(
        Guid id, RentalService kiralar, ContractService sozlesme, PdfExportService pdf, ContractShareService paylasim,
        CancellationToken ct)
        => ProcessShare(id, kiralar, sozlesme, pdf, ct, (no, bytes) => paylasim.ShareAsync(id, no, bytes, ct));

    /// <summary>Yeni sürüm: ESKİ token ölür, YENİ token + tazelenmiş anlık görüntü.</summary>
    private static Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> ShareNewVersion(
        Guid id, RentalService kiralar, ContractService sozlesme, PdfExportService pdf, ContractShareService paylasim,
        CancellationToken ct)
        => ProcessShare(id, kiralar, sozlesme, pdf, ct, (no, bytes) => paylasim.NewVersionAsync(id, no, bytes, ct));

    private static async Task<Results<Ok<KiraPaylasimYaniti>, ProblemHttpResult>> ProcessShare(
        Guid id, RentalService rentals, ContractService contract, PdfExportService pdf, CancellationToken ct,
        Func<string, byte[], Task<PaylasimDurum>> operation)
    {
        // SozlesmeService.GetAsync şube kapsamı UYGULAMAZ → önce kapsam kapısı.
        if (await ComprehensiveAsync(rentals, id, ct) is null) return NotFoundProblem();
        var s = await contract.GetAsync(id, ct);
        if (s is null) return NotFoundProblem();
        return TypedResults.Ok(new KiraPaylasimYaniti(Link(await operation(s.SozlesmeNo, pdf.Contract(s)))));
    }

    private static async Task<Results<Ok<KiraPaylasimIptalYaniti>, ProblemHttpResult>> CancelShare(
        Guid id, RentalService kiralar, ContractShareService paylasim, CancellationToken ct)
    {
        // Servis kira kapsamı denetlemez (yalnız izin) → kapsam kapısı burada ZORUNLU.
        if (await ComprehensiveAsync(kiralar, id, ct) is null) return NotFoundProblem();
        return TypedResults.Ok(new KiraPaylasimIptalYaniti(await paylasim.CancelAsync(id, ct)));
    }
}
