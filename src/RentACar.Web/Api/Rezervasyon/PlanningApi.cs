using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Availability;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// <c>/api/ui/v1/takvim</c> ve <c>/api/ui/v1/musaitlik</c> — salt-okunur planlama uçları (F5.1). İzin: OperationsWrite
/// (Blazor <c>[Authorize(Policy="izin:OperationsWrite")]</c> ile aynı). Şube kapsamı servislerde
/// (<see cref="CalendarService"/>, <see cref="AvailabilityService"/>, <see cref="VehicleService"/>).
/// <para><b>Hesap SUNUCUDA</b> (UI formül taşımaz): takvim ızgarası (gün × araç, kira önceliği), müsaitlik penceresi
/// (<see cref="AvailabilityService.Window"/>; gün+saat İSTANBUL saatidir, çakışma sorgusu ve yanıttaki
/// <c>pencereBas/Bit</c> gerçek UTC an — F5.1 adversarial L4), yaş, km limiti yönü, boşta gün, broker çiti, döviz süzgeci ve grup
/// başına fiyat (<see cref="RentalQuoteEngine"/> — persist SIFIR, yalnız gösterim).</para>
/// </summary>
public static class PlanningApi
{
    public static RouteGroupBuilder MapPlanningApi(this RouteGroupBuilder v1)
    {
        var calendar = v1.MapGroup("/takvim").WithTags("Takvim").RequirePermission(Permission.OperationsWrite);
        calendar.MapGet("", Calendar).MapFields([("Geçersiz ay", "ay")]);
        calendar.MapGet("/secenekler", CalendarOptions);

        var available = v1.MapGroup("/musaitlik").WithTags("Müsaitlik").RequirePermission(Permission.OperationsWrite);
        available.MapGet("", Availability).MapFields([("Bitiş tarihi başlangıçtan sonra", "bitGun")]);
        available.MapGet("/secenekler", AvailabilityOptions);
        return v1;
    }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly StringComparer TrOrder = StringComparer.Create(Tr, false);

    // ================================================================== takvim

    /// <summary>Takvimde çizilen en fazla araç (Blazor ile aynı tavan; fazlası filtreyle daraltılır).</summary>
    public const int CalendarVehicleLimit = 200;

    /// <summary>
    /// Ay ızgarası. Gün hücresi = o İSTANBUL gününe (yarı-açık [gün başı, ertesi gün başı)) değen doluluk: <c>Kira</c>
    /// (aktif kira) rezervasyonu (<c>Rezervasyon</c>: Rezerv/Onaylı) ezer; boş gün <c>null</c>. Blazor ekranı UTC günü
    /// kullanıyordu; yeni arayüz kayıtları gerçek İstanbul anıyla yazdığı için gün sınırı İstanbul'dur (<c>TenantGun</c>).
    /// </summary>
    private static async Task<Ok<TakvimYaniti>> Calendar(
        CalendarService takvim, VehicleService araclar, string? ay, string? plaka, string? grup, string? sube,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TenantDay.Slice).DateTime);
        DateOnly first;
        if (string.IsNullOrWhiteSpace(ay))
            first = new DateOnly(today.Year, today.Month, 1);
        else if (DateOnly.TryParseExact(ay.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
                 && a.Year is >= 2000 and <= 2100)
            first = a;
        else
            throw new ValidationException("Geçersiz ay; biçim yyyy-MM olmalıdır (ör. 2026-09).", "ay");

        var dayCount = DateTime.DaysInMonth(first.Year, first.Month);
        var page = await araclar.SearchAsync(new VehicleFilter
        {
            Query = F5Shared.Nz(plaka),
            Grup = F5Shared.Nz(grup),
            Sube = F5Shared.Nz(sube),
            Page = 1,
            PageSize = CalendarVehicleLimit,
        }, ct);

        var dayStarts = Enumerable.Range(0, dayCount + 1).Select(i => F5Shared.DayStartUtc(first.AddDays(i))).ToArray();
        var spans = await takvim.GetOccupancyAsync(dayStarts[0], dayStarts[^1], ct);
        var grid = new Dictionary<Guid, string?[]>();
        foreach (var s in spans)
        {
            if (!grid.TryGetValue(s.VehicleId, out var gunler))
                grid[s.VehicleId] = gunler = new string?[dayCount];
            for (var d = 0; d < dayCount; d++)
                if (s.Bas < dayStarts[d + 1] && dayStarts[d] < s.Bit && (s.Tip == "Kira" || gunler[d] is null))
                    gunler[d] = s.Tip;
        }

        var rows = page.Items.Select(v => new TakvimAraci(
            v.Id, v.Plaka, grid.TryGetValue(v.Id, out var g) ? g : new string?[dayCount])).ToList();
        return TypedResults.Ok(new TakvimYaniti(
            first.ToString("yyyy-MM", CultureInfo.InvariantCulture), dayCount,
            first.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
            first.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
            rows, page.Total));
    }

    /// <summary>Takvim süzgeç önerileri: şube master (aktif) + araç kartlarında geçen grup adları (Blazor ile aynı kaynaklar).</summary>
    private static async Task<Ok<TakvimSecenekleri>> CalendarOptions(
        BranchService subeler, VehicleService araclar, CancellationToken ct)
    {
        var s = (await subeler.ListActiveAsync(ct)).Select(b => b.Ad).ToList();
        var g = (await araclar.ListAsync(ct)).Select(v => v.Grup)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, TrOrder).ToList();
        return TypedResults.Ok(new TakvimSecenekleri(s, g));
    }

    // ================================================================== müsaitlik

    /// <summary>Blazor MusaitlikArama süzgeçleri (FAZ-48/73).</summary>
    public sealed class MusaitlikSorgusu
    {
        /// <summary>Başlangıç günü (zorunlu).</summary>
        [FromQuery(Name = "basGun")] public DateOnly? BasGun { get; set; }
        /// <summary>Bitiş günü — <c>gun</c> verilmediyse zorunlu.</summary>
        [FromQuery(Name = "bitGun")] public DateOnly? BitGun { get; set; }
        /// <summary>Gün sayısı (1–365): doluysa bitiş gününün yerine geçer.</summary>
        [FromQuery(Name = "gun")] public int? Gun { get; set; }
        [FromQuery(Name = "basSaat")] public TimeOnly? BasSaat { get; set; }
        [FromQuery(Name = "bitSaat")] public TimeOnly? BitSaat { get; set; }
        [FromQuery(Name = "grup")] public string? Grup { get; set; }
        /// <summary>Şube (yalnız kapsamsız kullanıcıda uygulanır; operatör daima kendi şubesini görür).</summary>
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        /// <summary>Rezervasyon kaynağı: fiyat motoruna kanal (yalnız aktif kaynak eşleşirse) + broker yasağı çiti.</summary>
        [FromQuery(Name = "rezKaynak")] public string? RezKaynak { get; set; }
        /// <summary>Teklif dövizi süzgeci (fiyatı değiştirmez; kur çevirimi YOK).</summary>
        [FromQuery(Name = "doviz")] public string? Doviz { get; set; }
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
    }

    private static async Task<Ok<MusaitlikYaniti>> Availability(
        [AsParameters] MusaitlikSorgusu s, AvailabilityService musaitlik, RentalQuoteEngine motor,
        VehicleGroupService gruplar, ReservationSourceService kaynaklar, BrokerBanService brokerYasaklari,
        CancellationToken ct)
    {
        if (s.BasGun is null)
            throw new ValidationException("Başlangıç günü zorunludur.", "basGun");
        if (s.Gun is { } gn && gn is < 1 or > 365)
            throw new ValidationException("Gün sayısı 1 ile 365 arasında olmalıdır.", "gun");
        RentalLimits.Text(s.Plaka, 32, "plaka", "Plaka");
        var window = AvailabilityService.Window(s.BasGun, s.BitGun, s.Gun, s.BasSaat, s.BitSaat)
            ?? throw new ValidationException("Bitiş tarihi ya da gün sayısından birini girin.", "bitGun");
        var (from, to) = window;
        // F5.1 adversarial L4: gün+saat İSTANBUL niyetidir. Çakışma sorgusu gerçek UTC anla koşar; fiyat motoru, broker
        // çiti ve kira bağlantısı takvim-günü konvansiyonunda (Blazor ile aynı) duvar değerleriyle kalır.
        var (fromUtc, toUtc) = (F5Shared.LocalToUtc(from), F5Shared.LocalToUtc(to));
        var group = F5Shared.Nz(s.Grup);
        var branch = F5Shared.Nz(s.Sube);
        var resSource = F5Shared.Nz(s.RezKaynak);

        var list = await musaitlik.FindAvailableAsync(fromUtc, toUtc, group, branch, ct); // kapsam serviste
        // Plaka süzgeci LİSTE ÜZERİNDE (müsaitlik sorgusunun kapsam kuralları dokunulmadan); terim normalize.
        var p = new string((s.Plaka ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (p.Length > 0)
            list = list.Where(v => v.Plaka.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();

        var groupMap = (await gruplar.ListActiveAsync(ct))
            .Where(g => !string.IsNullOrWhiteSpace(g.Ad))
            .GroupBy(g => g.Ad, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var activeSources = await kaynaklar.ListActiveAsync(ct);

        // Grup başına fiyat — Blazor ile aynı motor çağrısı; geçersiz grup/tarih → fiyat yok (null).
        var channel = ChannelResolver.Resolve(resSource, activeSources);
        var price = new Dictionary<string, MusaitlikFiyati>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in list.Select(v => v.Grup).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var q = await motor.QuoteAsync(new QuoteRequest
                { AracGrupKod = g!, BasTar = from, BitTar = to, Sube = branch, Kanal = channel, SigortaUrunKodlari = [] }, ct);
                price[g!] = new MusaitlikFiyati(q.GunlukUcret, q.GenelToplam, q.ParaBirimi);
            }
            catch (ValidationException) { /* fiyat gösterilmez */ }
        }

        // Broker çiti (FAZ-73): seçili kaynağa KAPALI gruptaki araçlar düşer; fiyata dokunmaz.
        var excluded = 0;
        var reason = new List<string>();
        if (resSource is not null)
        {
            var bans = await brokerYasaklari.ListActiveAsync(ct);
            if (bans.Count > 0)
            {
                var day = BookingMath.ComputeDays(from, to);
                var remaining = new List<Vehicle>(list.Count);
                foreach (var v in list)
                {
                    var block = BrokerAvailability.Block(bans, resSource, v.Grup, v.Sube, day, from);
                    if (block is null) { remaining.Add(v); continue; }
                    excluded++;
                    if (!reason.Contains(block.Kod, StringComparer.Ordinal)) reason.Add(block.Kod);
                }
                list = remaining;
            }
        }

        // Döviz süzgeci: teklif dövizi motordan; fiyatı hesaplanamayan grup da gizlenir (dövizi bilinmiyor).
        var currency = F5Shared.Nz(s.Doviz);
        if (currency is not null)
            list = list.Where(v => v.Grup is not null && price.TryGetValue(v.Grup, out var f)
                                     && string.Equals(f.ParaBirimi, currency, StringComparison.OrdinalIgnoreCase)).ToList();

        var last = await musaitlik.LastUsageAsync(list.Select(v => v.Id).ToList(), ct);
        var now = DateTimeOffset.UtcNow;
        VehicleGroup? Group(string? name) => name is not null && groupMap.TryGetValue(name, out var g) ? g : null;
        var rows = list.Select(v =>
        {
            var gr = Group(v.Grup);
            var sk = last.GetValueOrDefault(v.Id);
            return new MusaitlikSatiri(
                v.Id, v.Plaka, v.Marka, v.Tip, v.ModelYili,
                v.ModelYili is int my ? Math.Max(0, now.Year - my) : null,
                v.Yakit?.ToString(), v.Vites?.ToString(), v.Renk, v.Sipp, v.Grup, v.Sube,
                v.KiraKmLimiti ?? gr?.GunlukKmLimiti, // araç kaydı grup varsayılanını EZER (kira formundaki yön)
                gr?.SurucuMinYas, gr?.EhliyetMinYil, gr?.Provizyon, F5Shared.Nz(gr?.ProvizyonDoviz),
                v.KarLastigi, v.Temizlik, v.OzelKod1, v.Km,
                sk is null ? null : AvailabilityService.IdleDays(sk.SonDonus, now),
                sk is null ? null : sk.AnonimAd ? CustomerView.AnonymousNameLabel : sk.MusteriAd,
                v.Grup is not null && price.TryGetValue(v.Grup, out var f) ? f : null);
        }).ToList();

        // Kirala bağlantısı ÇÖZÜLMÜŞ pencereden (gün-modunda bitiş alanı boştur) — kira formu sorgu sözleşmesi.
        var query = new KiralaSorgusu(
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), group);
        return TypedResults.Ok(new MusaitlikYaniti(fromUtc, toUtc, rows, excluded, reason, query)); // gerçek pencere (UTC)
    }

    /// <summary>Müsaitlik süzgeç seçenekleri. Dövizler AKTİF tarifelerden (teklif dövizinin tek kaynağı).</summary>
    private static async Task<Ok<MusaitlikSecenekleri>> AvailabilityOptions(
        BranchService subeler, VehicleGroupService gruplar, ReservationSourceService kaynaklar, RateMatrixService tarifeler,
        CancellationToken ct)
    {
        var currencies = (await tarifeler.ListActiveAsync(ct))
            .Select(m => string.IsNullOrWhiteSpace(m.ParaBirimi) ? "TRY" : m.ParaBirimi!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return TypedResults.Ok(new MusaitlikSecenekleri(
            (await subeler.ListActiveAsync(ct)).Select(b => b.Ad).ToList(),
            (await gruplar.ListActiveAsync(ct)).Select(g => g.Ad).ToList(),
            (await kaynaklar.ListActiveAsync(ct)).Select(k => k.Ad).ToList(),
            currencies));
    }
}

// ---------------------------------------------------------------- yanıtlar

/// <summary>Takvim satırı: <c>Gunler[i]</c> = ayın (i+1). günü: <c>"Kira"</c> | <c>"Rezervasyon"</c> | <c>null</c>.</summary>
public sealed record TakvimAraci(Guid Id, string Plaka, IReadOnlyList<string?> Gunler);

/// <summary><c>AracToplam</c>: süzgece uyan araç sayısı; <see cref="PlanningApi.CalendarVehicleLimit"/>'ni aşarsa satırlar kesilmiştir.</summary>
public sealed record TakvimYaniti(string Ay, int GunSayisi, string OncekiAy, string SonrakiAy, IReadOnlyList<TakvimAraci> Araclar, int AracToplam);

public sealed record TakvimSecenekleri(IReadOnlyList<string> Subeler, IReadOnlyList<string> Gruplar);

public sealed record MusaitlikFiyati(decimal Gunluk, decimal Toplam, string ParaBirimi);

public sealed record MusaitlikSatiri(
    Guid Id, string Plaka, string? Marka, string? Tip, int? ModelYili, int? Yas, string? Yakit, string? Vites,
    string? Renk, string? Sipp, string? Grup, string? Sube, int? KmLimiti, int? MinSurucuYas, int? MinEhliyetYil,
    decimal? Provizyon, string? ProvizyonDoviz, bool KarLastigi, bool Temizlik, string? OzelKod1, int Km,
    int? BostaGun, string? SonMusteri, MusaitlikFiyati? Fiyat);

/// <summary>Kira formu sorgu sözleşmesi (<c>?varac=&amp;vfrom=&amp;vto=&amp;vgrup=</c>) — araç kimliği satırdan eklenir.</summary>
public sealed record KiralaSorgusu(string Vfrom, string Vto, string? Vgrup);

public sealed record MusaitlikYaniti(
    DateTimeOffset PencereBas, DateTimeOffset PencereBit, IReadOnlyList<MusaitlikSatiri> Araclar,
    int BrokerElenen, IReadOnlyList<string> BrokerGerekce, KiralaSorgusu KiralaSorgusu);

public sealed record MusaitlikSecenekleri(
    IReadOnlyList<string> Subeler, IReadOnlyList<string> Gruplar, IReadOnlyList<string> Kaynaklar, IReadOnlyList<string> Dovizler);
