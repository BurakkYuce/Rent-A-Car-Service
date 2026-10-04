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

/// <summary>PR-17: liste satırı — talep + türetilmiş göstergeler.</summary>
/// <param name="BekleyenGun">Talep AÇIKSA kaç gündür beklediği; kapanmışsa 0 (yanıltmasın).</param>
public sealed record TalepSatiri(PublicBookingRequest Talep, int NotSayisi, int BekleyenGun);

/// <summary>PR-17: liste filtresi. <paramref name="Durum"/> null = tümü.</summary>
public sealed record TalepFiltre(PublicBookingRequestDurum? Durum = null, string? Ara = null,
    int Sayfa = 1, int Boyut = 25);

/// <summary>PR-17: nav sayacı + Home KPI. <paramref name="EnEskiGun"/> null = bekleyen yok.</summary>
public sealed record TalepOzet(int Yeni, int? EnEskiGun);

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
    WebSite.IWebListingRepository listings, // PR-14: fiyat/başlık snapshot'ı SUNUCUDAN çözülür
    Notifications.CustomerNotificationService notification, // talep alındı bildirimi (anonim yol — guard'sız)
    ICurrentUser currentUser)
{
    // ---- Public (GUARD'SIZ — anonim ziyaretçi) ----

    /// <summary>Talep oluşturur. Honeypot doluysa HİÇBİR ŞEY yazmaz ama normal başarı döner.</summary>
    public async Task CreateAsync(PublicBookingRequestInput input, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(input.Website)) return; // honeypot: bot — sessiz yut

        var fullName = (input.AdSoyad ?? string.Empty).Trim();
        var phone = (input.Telefon ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName)) throw new ValidationException("Ad soyad zorunludur.");
        if (string.IsNullOrWhiteSpace(phone)) throw new ValidationException("Telefon zorunludur.");
        if (input.BitTar <= input.BasTar) throw new ValidationException("İade tarihi alış tarihinden sonra olmalıdır.");

        // PR-14: ilanı SUNUCUDAN çöz. Başlık ve fiyat snapshot'ı buradan alınır — ziyaretçinin
        // gönderdiği değerlere GÜVENİLMEZ (artık sözleşme fiyatına akıyorlar). İlan yayından
        // kalkmışsa null döner: talep yine oluşur (müşteri kaybedilmez), yalnız fiyat taşınmaz
        // ve personel dönüştürürken fiyatı kendi girer.
        var listing = input.IlanId is { } listingId ? await listings.FindAsync(listingId, ct) : null;

        var request = new PublicBookingRequest
        {
            AdSoyad = fullName,
            Telefon = phone,
            Email = TrimOrNull(input.Email),
            IlanId = listing?.Ilan.Id,
            // Başlık/fiyat SUNUCUDAN — ziyaretçinin gönderdiği değere güvenilmez (bu snapshot
            // artık sözleşme fiyatına akıyor).
            IlanBaslik = listing?.Ilan.Baslik,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            Sube = TrimOrNull(input.Sube),
            Not = TrimOrNull(input.Not),
            // Adversarial H1 (#361): sitede gösterilen GÜNLÜK EŞDEĞER (gün kademesi: 8–29 haftalık/7, 30+ aylık/30)
            // snapshot'lanır — vitrin aramasıyla AYNI fonksiyon. Eskiden hep 1–7 gün fiyatı saklanıyordu:
            // 10 günlük talepte site 12.857,10 gösterirken rezervasyon 15.000 oluyordu.
            GosterilenGunlukUcretKdvDahil = listing is null
                ? null
                : Fleet.FleetShowcaseService.DailyEquivalent(
                    listing.Ilan, BookingMath.ComputeDays(input.BasTar, input.BitTar)),
            GosterilenKdvDahil = listing?.Ilan.KdvDahil,
            Durum = PublicBookingRequestDurum.Yeni,
        };
        await repository.AddAsync(request, ct);

        // Talebi bırakan ziyaretçiye ALINDI bildirimi. Ayrı bir pazarlama izni ARANMAZ: kişi
        // hizmet talebini kendisi başlattı ve bu mesaj o talebin cevabıdır (sözleşme öncesi
        // iletişim), pazarlama değil. Pazarlama izinleri (İYS/MailIzin) kampanya yollarında okunur.
        //
        // Bildirim HİÇBİR KOŞULDA talebin kaydını düşürmez: gönderim hatası müşterinin formunu
        // reddetmek için sebep değildir. Bu yüzden sonuç yutulmaz ama istisna yukarı sızmaz —
        // durum GidenMesajlar tablosuna yazılır, operatör oradan görür.
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            try
            {
                await notification.GonderAsync(new Notifications.MesajIstegi(
                    Tur: MessageType.TalepAlindi,
                    Kanal: MessageChannel.Eposta,
                    Alici: request.Email!,
                    Anahtar: $"talep-alindi:{request.Id:N}",
                    Degerler: new Dictionary<string, string?>
                    {
                        ["MusteriAd"] = request.AdSoyad,
                        ["Arac"] = request.IlanBaslik,
                        ["CikisTarih"] = request.BasTar.ToString("dd.MM.yyyy HH:mm"),
                        ["DonusTarih"] = request.BitTar.ToString("dd.MM.yyyy HH:mm"),
                        ["CikisOfis"] = request.Sube,
                        ["No"] = request.Id.ToString("N")[..8].ToUpperInvariant(),
                    },
                    KaynakTur: "Talep",
                    KaynakId: request.Id), hasPermission: true, ct);
            }
            catch (Exception)
            {
                // Yutuluyor ve bu bilinçli: anonim yazma yolunda bildirim ikincil bir yan etkidir.
                // Kalıcı iz GidenMesajlar'da; burada loglayacak bir logger da yok (Application katmanı).
            }
        }
    }

    // ---- Staff (OperationsWrite) ----

    public Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.ListAsync(ct);
    }

    /// <summary>
    /// PR-17: ekranın kullandığı filtreli/sayfalı liste. Not sayıları TEK sorguyla getirilir
    /// (satır başına COUNT N+1 üretirdi), yaşlanma gün cinsinden hesaplanır.
    /// </summary>
    public async Task<(IReadOnlyList<TalepSatiri> Satirlar, int Toplam)> ListRequestsAsync(
        TalepFiltre filter, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var size = Math.Clamp(filter.Boyut, 1, 200);
        var (rows, total) = await repository.PagedAsync(
            filter.Durum, filter.Ara, filter.Sayfa, size, ct);

        var noteCounts = await repository.NoteCountsAsync([.. rows.Select(t => t.Id)], ct);
        var now = DateTimeOffset.UtcNow;
        return ([.. rows.Select(t => new TalepSatiri(
            t,
            noteCounts.GetValueOrDefault(t.Id),
            // Yaşlanma YALNIZ açık taleplerde anlamlı: kapanmış bir lead'in "12 gündür bekliyor"
            // yazması yanıltıcı olurdu.
            TalepDurumu.IsActive(t.Durum) ? (int)(now - t.CreatedAtUtc).TotalDays : 0))], total);
    }

    /// <summary>PR-17: nav sayacı + Home KPI. Yetki KONTROL EDİLİR (rakam da bilgidir).</summary>
    public async Task<TalepOzet> SummaryAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var newItem = await repository.NewCountAsync(ct);
        var oldest = newItem == 0 ? null : await repository.OldestNewAsync(ct);
        return new TalepOzet(newItem, oldest is { } e ? (int)(DateTimeOffset.UtcNow - e).TotalDays : null);
    }

    public async Task RejectAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        if (!await repository.TryClaimAsync(id, PublicBookingRequestDurum.Reddedildi, ct))
            throw new ValidationException("Bu talep zaten kapanmış.");
    }

    /// <summary>
    /// PR-17: aktif durumlar arası ilerletme (Yeni → İletişimde → Teklif verildi) ve Kayıp işaretleme.
    ///
    /// <para><b>Guard:</b> TERMİNAL durumdan çıkış YOK. Özellikle <c>Donustu</c>: ortada gerçek bir
    /// rezervasyon varken lead'i "Kayıp" göstermek defterle çelişirdi. Kontrol hem burada (anlaşılır
    /// mesaj) hem repository'nin atomik yükleminde (yarış) var.</para>
    /// </summary>
    public async Task AssignStatusAsync(Guid id, PublicBookingRequestDurum target, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        if (target == PublicBookingRequestDurum.Donustu)
            throw new ValidationException("\"Dönüştü\" durumu elle atanamaz — talebi Dönüştür ile işleyin.");

        var request = await repository.FindAsync(id, ct) ?? throw new ValidationException("Talep bulunamadı.");
        // TEK kontrol yeter: `DonusenReservationId` YALNIZ başarılı bir `Donustu` claim'inden sonra
        // yazılıyor (`SetDonusenReservationAsync`) ve iptalde null'lanıyor → "rezervasyonu var ama
        // durumu terminal değil" hali oluşamaz. Ayrı bir `DonusenReservationId is not null` kontrolü
        // yazılmıştı; testte ULAŞILAMAZ olduğu görüldü (terminal kontrolü her zaman önce tetikliyor)
        // ve ölü kod olarak kaldırıldı.
        if (TalepDurumu.Terminal(request.Durum))
            throw new ValidationException(
                $"Bu talep \"{TalepDurumu.Label(request.Durum)}\" durumunda kapanmış; durumu değiştirilemez."
                + (request.DonusenReservationId is not null ? " (Rezervasyona dönüşmüş.)" : ""));

        if (!await repository.ChangeStatusAsync(id, target, ct))
            throw new ValidationException("Bu talep zaten kapanmış.");
    }

    /// <summary>
    /// PR-17: talebi ÜSTLENME / bırakma. Yalnız KENDİNE atanır — başka kullanıcıya atamak, kullanıcı
    /// listesini (ManageUsers kilidi ardında) bu ekrana taşımayı gerektirirdi ve Operatör rolünde
    /// patlardı (Personel dropdown tuzağının aynısı).
    /// </summary>
    public async Task ClaimAsync(Guid id, bool claim, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var ok = claim
            ? await repository.AssignAsync(id, currentUser.UserId, currentUser.UserName, ct)
            : await repository.AssignAsync(id, null, null, ct);
        if (!ok) throw new ValidationException("Talep bulunamadı.");
    }

    /// <summary>PR-17: takip notu ekler. Notlar SİLİNMEZ (geçmiş kanıttır) → silme metodu yok.</summary>
    public async Task AddNoteAsync(Guid id, string text, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var m = (text ?? "").Trim();
        if (m.Length == 0) throw new ValidationException("Not boş olamaz.");
        if (m.Length > MaxNot) throw new ValidationException($"Not en çok {MaxNot} karakter olabilir.");
        _ = await repository.FindAsync(id, ct) ?? throw new ValidationException("Talep bulunamadı.");

        await repository.AddNoteAsync(new TalepNotu
        {
            TalepId = id, Metin = m, Kullanici = currentUser.UserName,
        }, ct);
    }

    public Task<IReadOnlyList<TalepNotu>> NotesAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.NotesAsync(id, ct);
    }

    /// <summary>Not uzunluk sınırı (kolon 2.000).</summary>
    public const int MaxNot = 2_000;

    /// <summary>
    /// Talebi gerçek Cari+Rezervasyon'a dönüştürür. <paramref name="vehicleId"/> ZORUNLU: talep yalnız
    /// GRUP taşır, `BookingMath.Validate` somut araç ister — personel dönüştürürken aracı seçer.
    /// Telefonu eşleşen Cari varsa YENİDEN YARATILMAZ (mükerrer müşteri kaydı olmasın).
    /// </summary>
    public async Task<Guid> ConvertAsync(Guid id, Guid vehicleId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);

        var request = await repository.FindAsync(id, ct)
            ?? throw new ValidationException("Talep bulunamadı.");
        var previousStatus = request.Durum; // PR-17: claim geri alınırsa BU duruma dönülür (Yeni'ye değil)

        // (1) ATOMİK CLAIM — iki personel aynı anda tıklarsa yalnız biri geçer.
        if (!await repository.TryClaimAsync(id, PublicBookingRequestDurum.Donustu, ct))
            throw new ValidationException("Bu talep zaten işlenmiş.");

        try
        {
            // (2) Cari: telefonla bul, yoksa yarat.
            var customerId = await repository.FindCustomerIdByPhoneAsync(request.Telefon, ct)
                ?? await customers.CreateAsync(new CustomerInput
                {
                    Tip = CustomerType.Bireysel,
                    Ad = request.AdSoyad,
                    CepTel = request.Telefon,
                    Email = request.Email,
                    Kaynak = WebSource,
                }, ct);

            var reservationId = await reservations.CreateAsync(new BookingInput
            {
                MusteriId = customerId,
                VehicleId = vehicleId,
                BasTar = request.BasTar,
                BitTar = request.BitTar,
                CikisOfisi = request.Sube,
                DonusOfisi = request.Sube,
                Kaynak = WebSource, // MasterDataSeeder'da ZATEN var — yeni seed gerekmez
                Aciklama = RequestNote(request),
                // PR-14 — MÜŞTERİNİN GÖRDÜĞÜ FİYAT SÖZLEŞMEYE GEÇER.
                // Vitrin fiyatı artık motordan DEĞİL ilandan geliyor; bu satır olmasaydı
                // `PricingService` GunlukUcret=0 görüp tarifeden çözmeye çalışır, tenant tarife
                // girmediği için 0 kalırdı → müşteri sitede 1.500 ₺ görür, sözleşmede 0 yazardı.
                GunlukUcret = ShownFee(request),
                // Kabul bulgusu d-web-talep-03: KDV modu ilanın gösterim biçiminden gelir — kira zinciri
                // ücreti modla yorumlar (InvoiceService GenelToplam'dan net+KDV ayrıştırır).
                // `FiyatTuru` KESİNLİKLE "Otomatik" GÖNDERİLMEZ: PricingService o değerde manuel
                // fiyatı ZORLA SIFIRLAR (`if (otomatik) input.GunlukUcret = 0m`) ve yukarıdaki
                // fiyat sessizce çöpe giderdi.
                FiyatTuru = ShownPriceType(request),
            }, ct);

            await repository.SetConvertedReservationAsync(id, reservationId, ct);
            return reservationId;
        }
        catch
        {
            // (3) Cari/Rezervasyon aşaması patladı → claim'i GERİ AL; personel tekrar deneyebilsin.
            await repository.ReleaseClaimAsync(id, previousStatus, ct);
            throw;
        }
    }

    /// <summary>`ReservationSource` seed'inde ZATEN var (MasterDataSeeder) — raporlamada kaynak atfı.</summary>
    private const string WebSource = "Web";

    /// <summary>
    /// Müşterinin sitede gördüğü günlük fiyat — DÖNÜŞTÜRÜLMEDEN. Fiyat yoksa (doğrudan forma gelen
    /// talep) 0 döner → mevcut davranış korunur, motor devreye girer.
    ///
    /// <para>Eski hâl (PR-14) "ERP zinciri net çalışır" varsayımıyla KDV dahil fiyatı nete bölüyordu.
    /// Varsayım YANLIŞTI: fiyat türü boş/"KDV Dahil Günlük" kirada ücret BRÜT'tür ve fatura
    /// GenelToplam'dan net+KDV ayrıştırır — 1.500 brüt gösterilen talep 1.250 brüt faturalanıyordu
    /// (kabul bulgusu d-web-talep-03, müşteriye %16,7 eksik fatura). KDV modu artık
    /// <see cref="ShownPriceType"/> ile açıkça taşınır; dönüşümü PricingService yapar.</para>
    /// </summary>
    private static decimal ShownDaily(PublicBookingRequest t)
        => t.GosterilenGunlukUcretKdvDahil is { } shown && shown > 0m ? shown : 0m;

    /// <summary>
    /// Rezervasyona giden ücret alanı, <see cref="ShownPriceType"/> ile birlikte okunur:
    /// KDV dahil ilanda GÜNLÜK brüt (sitedeki toplam = gün × günlük, PricingService aynı çarpımı yapar);
    /// KDV hariç ilanda sitedeki NET TOPLAM (= yuvarla(gün × günlük net), vitrin aramasının gösterdiği rakam).
    ///
    /// <para>Adversarial L1 (#361): KDV hariç ilan eskiden "Günlük" net moduyla gidiyordu — günlük net önce
    /// brüte yuvarlanıp gün ile çarpılınca kuruş birikiyordu (999,99 × 3 → fatura neti 2.999,98; 33,33 × 30 →
    /// 1.000,00). "Toplam" net modunda brüt = yuvarla(net toplam × (1 + oran)) ve faturanın ayrıştırdığı net
    /// sitedeki net toplamın TA KENDİSİ (yuvarlama hatası 0,005/(1+oran) &lt; yarım kuruş). Genel "Günlük" net
    /// modu DEĞİŞTİRİLMEDİ: operatörün girdiği günlük net, uzatmada da aynı günlük brütle çarpılmalı.</para>
    /// </summary>
    private static decimal ShownFee(PublicBookingRequest t)
    {
        var daily = ShownDaily(t);
        if (daily <= 0m || t.GosterilenKdvDahil != false) return daily;
        return Math.Round(daily * BookingMath.ComputeDays(t.BasTar, t.BitTar), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// İlanın KDV gösterimine karşılık gelen kira fiyat türü: KDV dahil ilan → "KDV Dahil Günlük"
    /// (ücret aynen brüt); KDV hariç ilan → "Toplam" (NET TOPLAM modu — PricingService tenant KDV oranıyla
    /// brüte çevirir, günlük brütü türetir ve oranı snapshot'lar; fatura aynı orandan ayrıştırır → faturadaki
    /// net = sitedeki net toplam). Gösterilen fiyat yoksa null (motor/manuel akış eskisi gibi).
    /// </summary>
    private static string? ShownPriceType(PublicBookingRequest t)
    {
        if (ShownDaily(t) <= 0m) return null;
        return t.GosterilenKdvDahil == false ? "Toplam" : "KDV Dahil Günlük";
    }

    private static string RequestNote(PublicBookingRequest t)
    {
        var parts = new List<string> { "Site talebinden dönüştürüldü." };
        if (!string.IsNullOrWhiteSpace(t.IlanBaslik)) parts.Add($"Talep edilen araç: {t.IlanBaslik}.");
        else if (!string.IsNullOrWhiteSpace(t.AracGrupKod)) parts.Add($"Talep edilen grup: {t.AracGrupKod}."); // PR-14 öncesi eski talepler
        if (t.GosterilenGunlukUcretKdvDahil is { } f)
            parts.Add($"Sitede gösterilen günlük fiyat ({(t.GosterilenKdvDahil == false ? "KDV hariç" : "KDV dahil")}): {f:N2}.");
        if (!string.IsNullOrWhiteSpace(t.Not)) parts.Add($"Müşteri notu: {t.Not}");
        return string.Join(" ", parts);
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
