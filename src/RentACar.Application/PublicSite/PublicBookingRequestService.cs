using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.PublicSite;

/// <summary>Anonim ziyaretçinin gönderdiği talep girdisi.</summary>
public sealed class PublicBookingRequestInput
{
    public string AdSoyad { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public string? Email { get; set; }
    /// <summary>PR-14: talebin geldiği ilan (vitrin artık ilan bazlı). Başlık ve fiyat BURADAN
    /// GELMEZ — servis onları ilandan sunucu tarafında çözer (form değerleri güvenilmez).</summary>
    public Guid? IlanId { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? Sube { get; set; }
    public string? Not { get; set; }

    /// <summary>HONEYPOT — CSS ile gizli, gerçek kullanıcı ASLA doldurmaz. Doluysa istek sessizce
    /// yok sayılır (bot'a ipucu vermemek için "başarılı" görünür ama DB'ye YAZILMAZ).</summary>
    public string? Website { get; set; }
}

/// <summary>
/// PR-8: halka açık site rezervasyon TALEBİ (lead). <see cref="CreateAsync"/> repo'daki İLK anonim
/// YAZMA yoludur — bilinçli guard'sız; izolasyon RLS, kötüye kullanım koruması honeypot + rate-limit
/// (PublicSite pipeline'ında). Talep GERÇEK rezervasyon DEĞİLDİR: `ReservationService.CreateAsync`
/// `OperationsWrite` + var olan `MusteriId` ister — anonim ziyaretçi bunları sağlayamaz.
///
/// EŞZAMANLILIK — CLAIM/RELEASE (bkz. plan PRE-CODE VERIFY sonucu): `CustomerRepository`/
/// `ReservationService` her biri KENDİ context'iyle bağımsız commit atıyor, tek DB transaction
/// uygulanabilir değil (para mantığını kopyalamadan). Bunun yerine: (1) atomik koşullu UPDATE ile
/// claim, (2) Cari+Rezervasyon, (3) hata olursa claim GERİ ALINIR — satır "Donustu ama rezervasyonsuz"
/// YARIM kalmaz. Adım 2-3 arasında process çökerse kalan tek durum staff ekranında UYARI olarak görünür.
/// </summary>
public sealed class PublicBookingRequestService(
    IPublicBookingRequestRepository repository,
    CustomerService customers,
    ReservationService reservations,
    WebSite.IWebIlanRepository ilanlar, // PR-14: fiyat/başlık snapshot'ı SUNUCUDAN çözülür
    Finance.KdvVarsayilan kdv,          // PR-14: ilan fiyatı KDV dahilse ERP'nin beklediği NET'e çevrilir
    ICurrentUser currentUser)
{
    // ---- Public (GUARD'SIZ — anonim ziyaretçi) ----

    /// <summary>Talep oluşturur. Honeypot doluysa HİÇBİR ŞEY yazmaz ama normal başarı döner.</summary>
    public async Task CreateAsync(PublicBookingRequestInput input, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(input.Website)) return; // honeypot: bot — sessiz yut

        var adSoyad = (input.AdSoyad ?? string.Empty).Trim();
        var telefon = (input.Telefon ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(adSoyad)) throw new ValidationException("Ad soyad zorunludur.");
        if (string.IsNullOrWhiteSpace(telefon)) throw new ValidationException("Telefon zorunludur.");
        if (input.BitTar <= input.BasTar) throw new ValidationException("İade tarihi alış tarihinden sonra olmalıdır.");

        // PR-14: ilanı SUNUCUDAN çöz. Başlık ve fiyat snapshot'ı buradan alınır — ziyaretçinin
        // gönderdiği değerlere GÜVENİLMEZ (artık sözleşme fiyatına akıyorlar). İlan yayından
        // kalkmışsa null döner: talep yine oluşur (müşteri kaybedilmez), yalnız fiyat taşınmaz
        // ve personel dönüştürürken fiyatı kendi girer.
        var ilan = input.IlanId is { } ilanId ? await ilanlar.FindAsync(ilanId, ct) : null;

        await repository.AddAsync(new PublicBookingRequest
        {
            AdSoyad = adSoyad,
            Telefon = telefon,
            Email = TrimOrNull(input.Email),
            IlanId = ilan?.Ilan.Id,
            // Başlık/fiyat SUNUCUDAN — ziyaretçinin gönderdiği değere güvenilmez (bu snapshot
            // artık sözleşme fiyatına akıyor).
            IlanBaslik = ilan?.Ilan.Baslik,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            Sube = TrimOrNull(input.Sube),
            Not = TrimOrNull(input.Not),
            GosterilenGunlukUcretKdvDahil = ilan?.Ilan.GunlukFiyat,
            GosterilenKdvDahil = ilan?.Ilan.KdvDahil,
            Durum = PublicBookingRequestDurum.Yeni,
        }, ct);
    }

    // ---- Staff (OperationsWrite) ----

    public Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.ListAsync(ct);
    }

    public async Task ReddetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        if (!await repository.TryClaimAsync(id, PublicBookingRequestDurum.Reddedildi, ct))
            throw new ValidationException("Bu talep zaten işlenmiş.");
    }

    /// <summary>
    /// Talebi gerçek Cari+Rezervasyon'a dönüştürür. <paramref name="vehicleId"/> ZORUNLU: talep yalnız
    /// GRUP taşır, `BookingMath.Validate` somut araç ister — personel dönüştürürken aracı seçer.
    /// Telefonu eşleşen Cari varsa YENİDEN YARATILMAZ (mükerrer müşteri kaydı olmasın).
    /// </summary>
    public async Task<Guid> DonusturAsync(Guid id, Guid vehicleId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);

        var talep = await repository.FindAsync(id, ct)
            ?? throw new ValidationException("Talep bulunamadı.");

        // (1) ATOMİK CLAIM — iki personel aynı anda tıklarsa yalnız biri geçer.
        if (!await repository.TryClaimAsync(id, PublicBookingRequestDurum.Donustu, ct))
            throw new ValidationException("Bu talep zaten işlenmiş.");

        try
        {
            // (2) Cari: telefonla bul, yoksa yarat.
            var cariId = await repository.FindCustomerIdByPhoneAsync(talep.Telefon, ct)
                ?? await customers.CreateAsync(new CustomerInput
                {
                    Tip = CariType.Bireysel,
                    Ad = talep.AdSoyad,
                    CepTel = talep.Telefon,
                    Email = talep.Email,
                    Kaynak = KaynakWeb,
                }, ct);

            var reservationId = await reservations.CreateAsync(new BookingInput
            {
                MusteriId = cariId,
                VehicleId = vehicleId,
                BasTar = talep.BasTar,
                BitTar = talep.BitTar,
                CikisOfisi = talep.Sube,
                DonusOfisi = talep.Sube,
                Kaynak = KaynakWeb, // MasterDataSeeder'da ZATEN var — yeni seed gerekmez
                Aciklama = TalepNotu(talep),
                // PR-14 — MÜŞTERİNİN GÖRDÜĞÜ FİYAT SÖZLEŞMEYE GEÇER.
                // Vitrin fiyatı artık motordan DEĞİL ilandan geliyor; bu satır olmasaydı
                // `PricingService` GunlukUcret=0 görüp tarifeden çözmeye çalışır, tenant tarife
                // girmediği için 0 kalırdı → müşteri sitede 1.500 ₺ görür, sözleşmede 0 yazardı.
                GunlukUcret = await NetGunlukAsync(talep, ct),
                // `FiyatTuru` KESİNLİKLE "Otomatik" GÖNDERİLMEZ: PricingService o değerde manuel
                // fiyatı ZORLA SIFIRLAR (`if (otomatik) input.GunlukUcret = 0m`) ve yukarıdaki
                // fiyat sessizce çöpe giderdi.
            }, ct);

            await repository.SetDonusenReservationAsync(id, reservationId, ct);
            return reservationId;
        }
        catch
        {
            // (3) Cari/Rezervasyon aşaması patladı → claim'i GERİ AL; personel tekrar deneyebilsin.
            await repository.ReleaseClaimAsync(id, ct);
            throw;
        }
    }

    /// <summary>`ReservationSource` seed'inde ZATEN var (MasterDataSeeder) — raporlamada kaynak atfı.</summary>
    private const string KaynakWeb = "Web";

    /// <summary>
    /// PR-14: müşterinin sitede gördüğü fiyatı ERP'nin beklediği NET'e çevirir.
    ///
    /// ERP zincirinin TAMAMI net çalışır (KDV fatura aşamasında eklenir); ilan fiyatı ise KDV
    /// DAHİL olabilir. Brüt rakamı olduğu gibi geçirmek sözleşmeyi KDV oranı kadar şişirirdi.
    /// Fiyat yoksa (doğrudan forma gelen talep) 0 döner → mevcut davranış korunur, motor devreye girer.
    /// </summary>
    private async Task<decimal> NetGunlukAsync(PublicBookingRequest t, CancellationToken ct)
    {
        if (t.GosterilenGunlukUcretKdvDahil is not { } gosterilen || gosterilen <= 0m) return 0m;
        if (t.GosterilenKdvDahil == false) return gosterilen; // zaten net
        var oran = await kdv.OranAsync(ct);
        return oran > 0m ? Math.Round(gosterilen / (1m + oran), 2, MidpointRounding.AwayFromZero) : gosterilen;
    }

    private static string TalepNotu(PublicBookingRequest t)
    {
        var parcalar = new List<string> { "Site talebinden dönüştürüldü." };
        if (!string.IsNullOrWhiteSpace(t.IlanBaslik)) parcalar.Add($"Talep edilen araç: {t.IlanBaslik}.");
        else if (!string.IsNullOrWhiteSpace(t.AracGrupKod)) parcalar.Add($"Talep edilen grup: {t.AracGrupKod}."); // PR-14 öncesi eski talepler
        if (t.GosterilenGunlukUcretKdvDahil is { } f)
            parcalar.Add($"Sitede gösterilen günlük fiyat ({(t.GosterilenKdvDahil == false ? "KDV hariç" : "KDV dahil")}): {f:N2}.");
        if (!string.IsNullOrWhiteSpace(t.Not)) parcalar.Add($"Müşteri notu: {t.Not}");
        return string.Join(" ", parcalar);
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
