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
/// BİLİNÇLİ SINIR (A3b-B2): drop ücreti SÖZLEŞMEDEKİ DonusOfisi'nden hesaplanır — fiili dönüş başka
/// ofise olursa (ReturnAsync ofis almaz) sonradan tahakkuk YOLU YOK; operatör dönüşten ÖNCE DonusOfisi'ni
/// günceller (senkron satırı üretir), unutulursa telafi normal ek-hizmet kalemidir. Açık iş: dönüş
/// akışına fiili-ofis alanı (FAZ 6 kapanış notlarına taşındı).
/// </summary>
public sealed class FeeLineService(
    IBookingRepository bookings,
    IVehicleRepository vehicles,
    IVehicleGroupRepository groups,
    ICustomerRepository customers,
    IEkHizmetTanimRepository tanimlar,
    RentalAddOnService addOns,
    IRentalAddOnRepository addOnRepo,
    Common.ITenantCache cache,
    DropTanimlari.IDropTanimRepository dropTanimlar,
    Finance.KdvVarsayilan kdvVarsayilan)
{
    public const string GencSurucuKod = "SYS-GENC-SURUCU";
    public const string EkSurucuKod = "SYS-EK-SURUCU";
    public const string DropKod = "SYS-DROP"; // FAZ 3.A3b (sistem tanımı KDV'si tenant varsayılanından — A6)

    /// <summary>Saf hesap: sistem ücret satırları (+ bilgi notları). Deftere/DB'ye dokunmaz.</summary>
    public static IReadOnlyList<SistemUcretSatiri> HesaplaSaf(
        VehicleGroup? grup, int gun, DateTimeOffset basTar, DateTimeOffset? dogumTarihi,
        bool ikinciSurucuVar, string? doviz, List<string> notlar, decimal? dropUcretNet = null)
    {
        var satirlar = new List<SistemUcretSatiri>();
        if (gun <= 0) return satirlar;

        if (FxMi(doviz))
        {
            if (grup?.GencSurucuUcretGunluk is > 0m || grup?.EkSurucuUcretGunluk is > 0m || dropUcretNet is > 0m)
                notlar.Add("Dövizli kirada otomatik ücret kalemleri uygulanmaz (kalemler TL) — gerekiyorsa manuel ekleyin.");
            return satirlar;
        }

        // FAZ 3.A3b: drop (farklı ofise bırakma) — TEK SEFERLİK satır (Miktar=1; gün ile ölçeklenmez).
        if (dropUcretNet is > 0m)
            satirlar.Add(new SistemUcretSatiri(DropKod, "Drop (farklı ofise bırakma) ücreti",
                dropUcretNet.Value, 1, null));

        if (grup is null) return satirlar;

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

    /// <summary>
    /// Drop ücreti çözümü (FAZ 3.A3b + FAZ-22 özgüllük). MANUEL override
    /// (BookingInput/RentalContract.DropUcreti, NET) operatör talimatıdır — koşuldan bağımsız
    /// uygulanır. Otomatik eşleşme: çıkış ≠ dönüş ofisi VE <c>DropTanim.Lokasyon == DonusOfisi</c>.
    ///
    /// <para><b>ÖZGÜLLÜK MERDİVENİ (en özgül SON SÖZDÜR — 0/pasifse rota ÜCRETSİZDİR, daha genel
    /// satırın ücretine sessizce düşülmez; A3b-B1 kuralının bir basamak yukarısı):</b>
    /// <list type="number">
    ///   <item><b>Çıkış LOKASYONU</b> eşleşen ve FİYAT KARARI TAŞIYAN satır (<c>Ucret</c> null değil).
    ///     <c>Ucret=null</c> satır "fiyat hakkında görüş bildirmiyor" demektir (DropTanim aslen bir
    ///     karşılama/iletişim matrisidir) — bir iletişim notu satırı gerçek ücreti SUSTURAMAZ.</item>
    ///   <item><b>Çıkış ŞUBESİ</b> eşleşen satır (mevcut A3b-B1 davranışı, semantiği DEĞİŞMEDİ:
    ///     Ucret null/0/pasif → ücretsiz).</item>
    ///   <item>Kalanlar — Sube sırasıyla deterministik fallback.</item>
    /// </list></para>
    ///
    /// <para><b>DARALTMA ASLA ARTIRMAZ (adversarial H2).</b> <c>CikisLokasyon</c>/<c>MinGun</c>
    /// koşulları basamağın İÇİNDE değerlendirilir, aday havuzundan ELEYEREK değil. Elenerek
    /// yapılsaydı: şubeye özel 300'lük satıra MinGun eklenince satır havuzdan düşer, fallback
    /// devreye girer ve müşteri BAŞKA şubenin 1000'lik ücretini öderdi — yani bir daraltma
    /// kuralı ücreti 700 TL ARTIRIRDI. Doğru davranış: çıkış şubesinin satırı varsa o şube
    /// "yapılandırılmış" sayılır; koşulu tutmuyorsa rota ÜCRETSİZDİR.</para>
    ///
    /// <para><paramref name="gun"/> ZORUNLU: MinGun koşulu buna bakar. Opsiyonel bırakılsaydı bir
    /// çağıran farkında olmadan koşulu atlar ve tutar sapardı.</para>
    /// </summary>
    public async Task<decimal?> DropUcretCozAsync(
        string? cikisOfisi, string? donusOfisi, decimal? manuelOverride, int gun,
        CancellationToken ct = default)
    {
        if (manuelOverride is > 0m) return manuelOverride;
        // Adversarial A3b-B5: AÇIK 0 = MUAFİYET (operatör talimatı) — tanım ücreti bastırılır.
        // null = otomatik (form boş alanı null yollar; 0 bilinçli yazılır).
        if (manuelOverride == 0m) return null;
        if (string.IsNullOrWhiteSpace(cikisOfisi) || string.IsNullOrWhiteSpace(donusOfisi)) return null;
        var cikis = cikisOfisi.Trim();
        var donus = donusOfisi.Trim();
        if (string.Equals(cikis, donus, StringComparison.OrdinalIgnoreCase)) return null;

        // NOT: karşılaştırıcı OrdinalIgnoreCase — Türkçe İ/ı için tam doğru değil ama ofis adları
        // açılır listeden geldiği için pratikte aynı metin. MEVCUT davranış budur; TurkishText'e
        // geçmek yeni eşleşmeler doğurup ücret uygulanmayan rotalarda ücret başlatabilir →
        // ayrı ve incelemeli bir değişiklik olmalı (AÇIK İŞ).
        static bool Es(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        var adaylar = (await dropTanimlar.ListAsync(ct))
            .Where(t => Es(t.Lokasyon, donus))
            .ToList();
        if (adaylar.Count == 0) return null;

        // Daraltma koşulları — basamağın İÇİNDE kullanılır, havuzu elemek için DEĞİL.
        bool Kosul(Domain.Entities.DropTanim t)
            => (t.CikisLokasyon is null || Es(t.CikisLokasyon, cikis))
            && (t.MinGun is not int min || gun >= min);

        // 1) ÇIKIŞ LOKASYONU basamağı — yalnız fiyat kararı taşıyan (Ucret != null) satırlar.
        //    Şubesi DE eşleşen satır önce (iki alan da aynı kira alanına bakar → daha özgül).
        var lokAdaylar = adaylar
            .Where(t => t.CikisLokasyon is not null && t.Ucret is not null && Kosul(t))
            .OrderByDescending(t => Es(t.Sube, cikis))
            .ThenBy(t => t.Sube, StringComparer.Ordinal)
            .ToList();
        if (lokAdaylar.Count > 0) return Gecerli(lokAdaylar[0]);

        // 2) ÇIKIŞ ŞUBESİ basamağı — şube "yapılandırılmış"sa SON SÖZ ONUNDUR. Koşulu tutan satır
        //    yoksa rota ÜCRETSİZDİR (başka şubenin ücretine düşülmez).
        var subeSatirlari = adaylar.Where(t => Es(t.Sube, cikis)).ToList();
        if (subeSatirlari.Count > 0)
        {
            // FİYAT KARARI TAŞIYAN satır önce (adversarial H3): benzersizlik artık CikisLokasyon'u
            // da kapsadığı için aynı (dönüş, şube) çiftinde birden çok satır olabilir; bir iletişim
            // notu satırı (Ucret=null) gerçek ücreti susturmamalı. Tek satırlı eski veride bu
            // sıralama etkisizdir → eski semantik (null/0 → ücretsiz) aynen korunur.
            var uygun = subeSatirlari.Where(Kosul)
                .OrderByDescending(t => t.Ucret is not null)
                .ThenBy(t => t.Sube, StringComparer.Ordinal).FirstOrDefault();
            return uygun is null ? null : Gecerli(uygun);
        }

        // 3) Fallback — çıkış şubesine ait hiç satır yokken.
        return adaylar.Where(t => Kosul(t) && t.Aktif && t.Ucret is > 0m)
            .OrderBy(t => t.Sube, StringComparer.Ordinal)
            .FirstOrDefault()?.Ucret;

        static decimal? Gecerli(Domain.Entities.DropTanim t)
            => t.Aktif && t.Ucret is > 0m ? t.Ucret : null;
    }

    /// <summary>Kayıt yolu: sözleşmeye sistem ücret satırlarını ekler (kira create + rez→kira dönüşümü
    /// SONRASI çağrılır). İDEMPOTENT — mevcut sistem-tanımlı satır tekrar eklenmez.</summary>
    public async Task ApplyContractFeesAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return;

        var grup = await GrupCozAsync(c.VehicleId, ct);
        var dogum = grup is null ? null : (await customers.FindAsync(c.MusteriId, ct))?.DogumTarihi;
        var drop = await DropUcretCozAsync(c.CikisOfisi, c.DonusOfisi, c.DropUcreti, c.Gun, ct);

        var notlar = new List<string>(); // kayıt yolunda notlar sessiz (önizleme aynı notları gösterir)
        var satirlar = HesaplaSaf(grup, c.Gun, c.BasTar, dogum, c.IkinciSurucuId is not null, c.Doviz, notlar, drop);
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
        var drop = await DropUcretCozAsync(c.CikisOfisi, c.DonusOfisi, c.DropUcreti, c.Gun, ct);
        var notlar = new List<string>();
        var beklenen = HesaplaSaf(grup, c.Gun, c.BasTar, dogum, c.IkinciSurucuId is not null, c.Doviz, notlar, drop);

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
        return (t?.Id, t?.KdvOrani ?? await kdvVarsayilan.OranAsync(ct)); // A6: tanım yoksa tenant varsayılanı
    }

    private async Task<EkHizmetTanim> GetOrCreateTanimAsync(string kod, string ad, CancellationToken ct)
    {
        var t = (await tanimlar.ListAsync(ct))
            .FirstOrDefault(x => string.Equals(x.Kod, kod, StringComparison.OrdinalIgnoreCase));
        if (t is not null) return t;
        t = new EkHizmetTanim { Kod = kod, Ad = ad, BirimUcret = 0m, KdvOrani = await kdvVarsayilan.OranAsync(ct), Aktif = true }; // A6
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
