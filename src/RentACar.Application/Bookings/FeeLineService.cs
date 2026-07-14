using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.RentalAddOns;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;

namespace RentACar.Application.Bookings;

/// <summary>Sistem ücret satırı — SAF hesap çıktısı (önizleme VE kayıt aynı listeden geçer).</summary>
public sealed record SistemUcretSatiri(string TanimKod, string Ad, decimal BirimNet, int Gun, string? Not);

/// <summary>
/// Sözleşme ücret kalemleri (FAZ 3.A3a): genç sürücü + ek (2.) sürücü ücretleri SİSTEM-üretimi
/// RentalAddOn satırı olarak. KURAL A KORUNUR — BaseGross'a DOKUNULMAZ (ReturnMath/fark/iade
/// patlama yarıçapı); ücretler matrah-dışı ayrı satır, faturada ayrı kalem, fark/iade akışına
/// add-on olarak otomatik katılır. Saf hesap (<see cref="HesaplaSaf"/>) KiraHesapService
/// önizlemesiyle PAYLAŞILIR → önizleme == kayıt. Sistem tanımları Kod-idempotent
/// (SYS-GENC-SURUCU / SYS-EK-SURUCU; KDV varsayılan 0.20 — tanımdan okunur, operatör tanımda
/// değiştirirse önizleme ve kayıt birlikte değişir). Satır idempotency'si tanım-bazlı: aynı
/// sistem tanımından satırı olan kiraya tekrar eklenmez (rez→kira dönüşümü / çift çağrı güvenli).
/// FX kirada otomatik ücret ATLANIR (add-on kalemleri TL; FX+addon fatura guard'ı kilitlemesin) —
/// önizlemeye not düşülür. Genç sürücü yaşı Customer.DogumTarihi'nden (kira BAŞLANGICINDA tam yıl);
/// doğum tarihi kayıtlı değilse ÜCRET YOK + not (tahmin yapılmaz).
/// </summary>
public sealed class FeeLineService(
    IBookingRepository bookings,
    IVehicleRepository vehicles,
    IVehicleGroupRepository groups,
    ICustomerRepository customers,
    IEkHizmetTanimRepository tanimlar,
    RentalAddOnService addOns,
    IRentalAddOnRepository addOnRepo,
    Common.ITenantCache cache)
{
    public const string GencSurucuKod = "SYS-GENC-SURUCU";
    public const string EkSurucuKod = "SYS-EK-SURUCU";
    public const decimal VarsayilanKdv = 0.20m;

    /// <summary>Saf hesap: sistem ücret satırları (+ bilgi notları). Deftere/DB'ye dokunmaz.</summary>
    public static IReadOnlyList<SistemUcretSatiri> HesaplaSaf(
        VehicleGroup? grup, int gun, DateTimeOffset basTar, DateTimeOffset? dogumTarihi,
        bool ikinciSurucuVar, string? doviz, List<string> notlar)
    {
        var satirlar = new List<SistemUcretSatiri>();
        if (grup is null || gun <= 0) return satirlar;

        if (FxMi(doviz))
        {
            if (grup.GencSurucuUcretGunluk is > 0m || grup.EkSurucuUcretGunluk is > 0m)
                notlar.Add("Dövizli kirada otomatik sürücü ücretleri uygulanmaz (ücret kalemleri TL) — gerekiyorsa manuel ekleyin.");
            return satirlar;
        }

        if (grup.GencSurucuUcretGunluk is > 0m && grup.GencSurucuYas is > 0)
        {
            if (dogumTarihi is null)
                notlar.Add("Doğum tarihi kayıtlı değil — genç sürücü ücreti değerlendirilemedi (tahmin yapılmaz).");
            else if (Yas(dogumTarihi.Value, basTar) is var yas && yas < grup.GencSurucuYas.Value)
                satirlar.Add(new SistemUcretSatiri(GencSurucuKod, "Genç sürücü ücreti",
                    grup.GencSurucuUcretGunluk.Value, gun, $"Sürücü yaşı {yas} < eşik {grup.GencSurucuYas}."));
        }

        if (grup.EkSurucuUcretGunluk is > 0m && ikinciSurucuVar)
            satirlar.Add(new SistemUcretSatiri(EkSurucuKod, "Ek sürücü ücreti",
                grup.EkSurucuUcretGunluk.Value, gun, null));

        return satirlar;
    }

    /// <summary>Add-on kalemleri TL varsayımı — "FX mi" kararı sistemin TEK doğruluk kaynağından
    /// (KurService.NormalizeKod: TL/TRY/TRL/₺/Türk Lirası → TRY). Adversarial A3a-B1: ayrı bir
    /// alias listesi tutmak "₺" gibi TL-eş anlamlılarda ücretin sessizce atlanmasına yol açıyordu.</summary>
    public static bool FxMi(string? doviz)
        => RentACar.Application.Kur.KurService.NormalizeKod(doviz) != "TRY";

    /// <summary>Tam yıl yaş (tarih anında; doğum günü gelmediyse yıl tamamlanmamış sayılır).</summary>
    public static int Yas(DateTimeOffset dogum, DateTimeOffset tarih)
    {
        var y = tarih.Year - dogum.Year;
        if (tarih.Month < dogum.Month || (tarih.Month == dogum.Month && tarih.Day < dogum.Day)) y--;
        return Math.Max(0, y);
    }

    /// <summary>Kirada aracın grubunu (varsa) çözer — önizleme ve kayıt aynı çözümü kullanır.</summary>
    public async Task<VehicleGroup?> GrupCozAsync(Guid vehicleId, CancellationToken ct = default)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var kod = vehicle?.Grup?.Trim();
        if (string.IsNullOrWhiteSpace(kod)) return null;
        return (await groups.ListActiveAsync(ct))
            .FirstOrDefault(g => string.Equals(g.Kod, kod, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Kayıt yolu: sözleşmeye sistem ücret satırlarını ekler (kira create + rez→kira dönüşümü
    /// SONRASI çağrılır). İDEMPOTENT — mevcut sistem-tanımlı satır tekrar eklenmez.</summary>
    public async Task ApplyContractFeesAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return;

        var grup = await GrupCozAsync(c.VehicleId, ct);
        if (grup is null) return;
        var dogum = (await customers.FindAsync(c.MusteriId, ct))?.DogumTarihi;

        var notlar = new List<string>(); // kayıt yolunda notlar sessiz (önizleme aynı notları gösterir)
        var satirlar = HesaplaSaf(grup, c.Gun, c.BasTar, dogum, c.IkinciSurucuId is not null, c.Doviz, notlar);
        if (satirlar.Count == 0) return;

        var mevcut = await addOns.ListAsync(rentalId, ct);
        foreach (var s in satirlar)
        {
            var tanim = await GetOrCreateTanimAsync(s.TanimKod, s.Ad, ct);
            if (mevcut.Any(a => a.EkHizmetTanimId == tanim.Id)) continue; // idempotent (dönüşüm/çift çağrı)
            await addOns.AddAsync(rentalId, tanim.Id, s.Gun, birimNetOverride: s.BirimNet, sistem: true, ct: ct);
        }
    }

    /// <summary>Yaşam-döngüsü senkronu (adversarial A3a B2/B3): AÇIK (Kirada) ve FATURALANMAMIŞ kirada
    /// sistem ücret satırlarını sözleşmenin GÜNCEL hâline eşitler — 2. sürücü kaldırıldıysa satır kalkar,
    /// eklendiyse gelir; gün/birim değiştiyse (uzatma / grup fiyat değişimi) satır yeniden ölçeklenir.
    /// FATURALANMIŞ kirada DOKUNULMAZ (defter snapshot'ı; fatura-sonrası ücret değişikliği fark akışının
    /// işi — bilinçli). NOT: sistem tanımının Aktif bayrağı ücreti KAPATMAZ — aç/kapa anahtarı grup
    /// alanlarıdır (GencSurucuUcretGunluk/EkSurucuUcretGunluk null/0); tanım yalnız KDV oranı taşır.</summary>
    public async Task SyncContractFeesAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null || c.Durum != Domain.Enums.RentalStatus.Kirada) return;
        if (await addOnRepo.IsRentalInvoicedAsync(rentalId, ct)) return;

        var grup = await GrupCozAsync(c.VehicleId, ct);
        var dogum = grup is null ? null : (await customers.FindAsync(c.MusteriId, ct))?.DogumTarihi;
        var notlar = new List<string>();
        IReadOnlyList<SistemUcretSatiri> beklenen = grup is null
            ? []
            : HesaplaSaf(grup, c.Gun, c.BasTar, dogum, c.IkinciSurucuId is not null, c.Doviz, notlar);

        var sysTanimlar = (await tanimlar.ListAsync(ct))
            .Where(t => t.Kod.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(t => t.Id, t => t.Kod);
        foreach (var a in (await addOns.ListAsync(rentalId, ct))
                 .Where(a => sysTanimlar.ContainsKey(a.EkHizmetTanimId)))
        {
            var kod = sysTanimlar[a.EkHizmetTanimId];
            var b = beklenen.FirstOrDefault(x => string.Equals(x.TanimKod, kod, StringComparison.OrdinalIgnoreCase));
            if (b is null || b.Gun != a.Miktar || b.BirimNet != a.BirimNetFiyat)
                await addOns.RemoveAsync(a.Id, ct); // kalkan/ölçeği değişen satır — aşağıda yeniden yazılır
        }
        await ApplyContractFeesAsync(rentalId, ct); // eksikleri ekle (idempotent)
    }

    /// <summary>Önizleme için sistem tanımının KDV oranı (tanım henüz yoksa kayıtta kullanılacak varsayılan).</summary>
    public async Task<(Guid? TanimId, decimal KdvOrani)> TanimBilgiAsync(string kod, CancellationToken ct = default)
    {
        var t = (await tanimlar.ListAsync(ct))
            .FirstOrDefault(x => string.Equals(x.Kod, kod, StringComparison.OrdinalIgnoreCase));
        return (t?.Id, t?.KdvOrani ?? VarsayilanKdv);
    }

    private async Task<EkHizmetTanim> GetOrCreateTanimAsync(string kod, string ad, CancellationToken ct)
    {
        var t = (await tanimlar.ListAsync(ct))
            .FirstOrDefault(x => string.Equals(x.Kod, kod, StringComparison.OrdinalIgnoreCase));
        if (t is not null) return t;
        t = new EkHizmetTanim { Kod = kod, Ad = ad, BirimUcret = 0m, KdvOrani = VarsayilanKdv, Aktif = true };
        try { await tanimlar.CreateAsync(t, ct); cache.Invalidate("ekhizmet"); return t; }
        catch // eşzamanlı yaratma yarışı: (TenantId, Kod) unique — kazananı oku
        {
            var kazanan = (await tanimlar.ListAsync(ct))
                .FirstOrDefault(x => string.Equals(x.Kod, kod, StringComparison.OrdinalIgnoreCase));
            if (kazanan is null) throw;
            return kazanan;
        }
    }
}
