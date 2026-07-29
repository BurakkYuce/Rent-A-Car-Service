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
    public string? AracGrupKod { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? Sube { get; set; }
    public string? Not { get; set; }
    public decimal? GosterilenGunlukUcretKdvDahil { get; set; }

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

        await repository.AddAsync(new PublicBookingRequest
        {
            AdSoyad = adSoyad,
            Telefon = telefon,
            Email = TrimOrNull(input.Email),
            AracGrupKod = TrimOrNull(input.AracGrupKod),
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            Sube = TrimOrNull(input.Sube),
            Not = TrimOrNull(input.Not),
            GosterilenGunlukUcretKdvDahil = input.GosterilenGunlukUcretKdvDahil,
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

            // Rezervasyon: fiyat motoru GunlukUcret=0'da tarifeden çözer (manuel >0 kazanır kuralı).
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

    private static string TalepNotu(PublicBookingRequest t)
    {
        var parcalar = new List<string> { "Site talebinden dönüştürüldü." };
        if (!string.IsNullOrWhiteSpace(t.AracGrupKod)) parcalar.Add($"Talep edilen grup: {t.AracGrupKod}.");
        if (t.GosterilenGunlukUcretKdvDahil is { } f) parcalar.Add($"Sitede gösterilen günlük fiyat (KDV dahil): {f:N2}.");
        if (!string.IsNullOrWhiteSpace(t.Not)) parcalar.Add($"Müşteri notu: {t.Not}");
        return string.Join(" ", parcalar);
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
