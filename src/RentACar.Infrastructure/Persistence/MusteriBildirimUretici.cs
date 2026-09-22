using Microsoft.EntityFrameworkCore;
using RentACar.Application.Notifications;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Müşteriye giden hatırlatmaları üretir + kuyrukta kalan mesajları yeniden dener (scheduler işi).
///
/// <para>Üretici deseni (repo kuralı): servis DEĞİL, <c>RunAsync(db, tenant, now, …)</c> — yetki
/// yüzeyi büyümez, job doğrudan-context yolundan tenant'a kapsanmış <c>db</c> ile çağırır.</para>
///
/// <para><b>Zaman dilimi:</b> "yarın çıkacaklar" ve "bugün dönecekler" TAKVİM günüdür ve müşterinin
/// takvimi yereldir — UTC gününe göre hesaplamak, akşam 21:00'den sonraki çıkışları bir gün kaydırırdı.
/// Bu yüzden gün sınırları İstanbul saatinde bulunup UTC'ye çevrilir — sorguya giden sınır DAİMA
/// Offset=0'dır (Npgsql timestamptz'ye başka ofset yazmaz; bkz. <c>VadeBildirimJobSaatDilimiTests</c>).</para>
///
/// <para><b>İdempotency:</b> anahtar GÜN bileşeni taşır (<c>iade-hatirlatma:{id}:2026-08-17</c>).
/// Gün olmadan, 3 günlük bir kiralamanın hatırlatması ilk gün gönderilip sonraki günler "zaten var"
/// diye atlanırdı; günle birlikte her takvim günü için en fazla bir mesaj çıkar.</para>
/// </summary>
public static class MusteriBildirimUretici
{
    /// <summary>Kaç mesaj kuyruğa alındı/gönderildi (yeni + yeniden denenen).</summary>
    public static async Task<int> RunAsync(
        AppDbContext db, Guid tenantId, DateTimeOffset now, TimeZoneInfo tz,
        RentACar.Application.Common.ISecretProtector secrets,
        RentACar.Application.Integrations.IEmailSender eposta,
        RentACar.Application.Integrations.ISmsService sms,
        CancellationToken ct = default)
    {
        // Servis job'ın elindeki context üzerine kurulur (bkz. DogrudanMesajRepolari): şablon çözümü,
        // idempotency, deneme sayacı ve izin kuralı TEK yerde kalır — kopyalanmaz.
        var bildirim = new MusteriBildirimService(
            new DogrudanMesajRepository(db, tenantId),
            new RentACar.Application.Integrations.BildirimKanaliService(
                new DogrudanAyarRepository(db), secrets, eposta, sms),
            NullCurrentUser.Instance);

        // Gün sınırı İSTANBUL'da hesaplanır, DB'ye giden an UTC'dir. Npgsql 6+ timestamptz
        // parametresine yalnız Offset=0 yazar; eskiden sınırlar yerel ofsetle (+03:00) kurulup
        // sorguya veriliyordu → her koşuda ArgumentException, hatırlatmalar hiç üretilmedi.
        now = now.ToUniversalTime();
        var bugun = TenantGun.Gun(now, tz);
        var bugunBas = GunBasiUtc(bugun, tz);
        var bugunBit = GunBasiUtc(bugun.AddDays(1), tz);
        var yarinBas = bugunBit;
        var yarinBit = GunBasiUtc(bugun.AddDays(2), tz);
        var gunEtiketi = bugun.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var sayac = 0;
        sayac += await TeslimHatirlatmaAsync(db, bildirim, yarinBas, yarinBit, gunEtiketi, ct);
        sayac += await IadeHatirlatmaAsync(db, bildirim, bugunBas, bugunBit, gunEtiketi, ct);
        sayac += await KuyruktaYenidenDeneAsync(db, bildirim, now, ct);
        return sayac;
    }

    /// <summary>Yerel takvim gününün 00:00'ı → UTC an (Offset=0). Ofset o GECE YARISI için çözülür
    /// (<c>OperasyonOzetUretici</c> ile aynı yol); dilimde yaz saati olsa bile gün sınırı kaymaz.</summary>
    private static DateTimeOffset GunBasiUtc(DateOnly gun, TimeZoneInfo tz)
    {
        var yerelBas = gun.ToDateTime(TimeOnly.MinValue); // Kind=Unspecified → tz'nin duvar saati
        return new DateTimeOffset(yerelBas, tz.GetUtcOffset(yerelBas)).ToUniversalTime();
    }

    /// <summary>Yarın aracını teslim alacak müşterilere hatırlatma (rezervasyonlar).</summary>
    private static async Task<int> TeslimHatirlatmaAsync(
        AppDbContext db, MusteriBildirimService bildirim,
        DateTimeOffset bas, DateTimeOffset bit, string gun, CancellationToken ct)
    {
        var adaylar = await (
            from r in db.Reservations.AsNoTracking()
            where r.Durum == ReservationStatus.Rezerv && r.BasTar >= bas && r.BasTar < bit
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            select new Aday(
                r.Id, r.ReservationNo, r.BasTar, r.BitTar,
                c.Ad, c.Unvan, c.Email, c.CepTel, c.MailIzin, c.SmsIzin,
                v != null ? v.Plaka : null, v != null ? (v.Marka ?? "") + " " + (v.Tip ?? "") : null))
            .ToListAsync(ct);

        return await GonderAsync(bildirim, adaylar, MesajTuru.TeslimHatirlatma,
            anahtarOnek: "teslim-hatirlatma", gun, kaynakTur: "Rezervasyon", ct);
    }

    /// <summary>Bugün aracını iade edecek müşterilere hatırlatma (açık kira sözleşmeleri).</summary>
    private static async Task<int> IadeHatirlatmaAsync(
        AppDbContext db, MusteriBildirimService bildirim,
        DateTimeOffset bas, DateTimeOffset bit, string gun, CancellationToken ct)
    {
        var adaylar = await (
            from k in db.Rentals.AsNoTracking()
            where k.Durum == RentalStatus.Kirada && k.BitTar >= bas && k.BitTar < bit
            join c in db.Customers.AsNoTracking() on k.MusteriId equals c.Id
            join v in db.Vehicles.AsNoTracking() on k.VehicleId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            select new Aday(
                k.Id, k.SozlesmeNo, k.BasTar, k.BitTar,
                c.Ad, c.Unvan, c.Email, c.CepTel, c.MailIzin, c.SmsIzin,
                v != null ? v.Plaka : null, v != null ? (v.Marka ?? "") + " " + (v.Tip ?? "") : null))
            .ToListAsync(ct);

        return await GonderAsync(bildirim, adaylar, MesajTuru.IadeHatirlatma,
            anahtarOnek: "iade-hatirlatma", gun, kaynakTur: "Kira", ct);
    }

    private static async Task<int> GonderAsync(
        MusteriBildirimService bildirim, IReadOnlyList<Aday> adaylar, MesajTuru tur,
        string anahtarOnek, string gun, string kaynakTur, CancellationToken ct)
    {
        var sayac = 0;
        foreach (var a in adaylar)
        {
            var degerler = a.Degerler();

            if (!string.IsNullOrWhiteSpace(a.Email))
            {
                await bildirim.GonderAsync(new MesajIstegi(
                    tur, MesajKanal.Eposta, a.Email!, $"{anahtarOnek}:{a.Id:N}:{gun}",
                    degerler, kaynakTur, a.Id),
                    // KVKK: hatırlatma pazarlama DEĞİL, kurulmuş sözleşmenin ifasına ilişkin bilgidir.
                    // Yine de müşteri o kanalı AÇIKÇA kapattıysa (MailIzin=false) saygı gösterilir;
                    // "belirtilmemiş" (null) kapalı sayılmaz — sözleşme ilişkisi zaten var.
                    izinVar: a.MailIzin != false, ct);
                sayac++;
            }

            if (!string.IsNullOrWhiteSpace(a.Telefon))
            {
                await bildirim.GonderAsync(new MesajIstegi(
                    tur, MesajKanal.Sms, a.Telefon!, $"{anahtarOnek}-sms:{a.Id:N}:{gun}",
                    degerler, kaynakTur, a.Id),
                    izinVar: a.SmsIzin != false, ct);
                sayac++;
            }
        }
        return sayac;
    }

    /// <summary>Kuyrukta kalanları yeniden dener (geçici SMTP/SMS hatası, sonradan tanımlanan şablon).</summary>
    private static async Task<int> KuyruktaYenidenDeneAsync(
        AppDbContext db, MusteriBildirimService bildirim, DateTimeOffset now, CancellationToken ct)
    {
        var bekleyen = await Repositories.MesajRepository.KuyruktakilerAsync(
            db, now, MusteriBildirimService.MaxDeneme, ct);
        foreach (var m in bekleyen) await bildirim.DeneAsync(m, ct);
        return bekleyen.Count;
    }

    /// <summary>Sorgu projeksiyonu — müşteri/araç bilgisi mesaj yer tutucularına dönüşür.</summary>
    private sealed record Aday(
        Guid Id, string No, DateTimeOffset BasTar, DateTimeOffset BitTar,
        string? Ad, string? Unvan, string? Email, string? Telefon,
        bool? MailIzin, bool? SmsIzin, string? Plaka, string? Arac)
    {
        public Dictionary<string, string?> Degerler() => new()
        {
            ["MusteriAd"] = !string.IsNullOrWhiteSpace(Unvan) ? Unvan : Ad,
            ["No"] = No,
            ["Plaka"] = Plaka,
            ["Arac"] = Arac?.Trim(),
            ["CikisTarih"] = BasTar.ToString("dd.MM.yyyy HH:mm"),
            ["DonusTarih"] = BitTar.ToString("dd.MM.yyyy HH:mm"),
        };
    }
}
