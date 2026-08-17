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
/// Bu yüzden gün sınırları İstanbul saatinde bulunup UTC'ye çevrilir.</para>
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

        var yerel = TimeZoneInfo.ConvertTime(now, tz);
        var bugunBas = new DateTimeOffset(yerel.Year, yerel.Month, yerel.Day, 0, 0, 0, yerel.Offset);
        var bugunBit = bugunBas.AddDays(1);
        var yarinBas = bugunBit;
        var yarinBit = yarinBas.AddDays(1);
        var gunEtiketi = yerel.ToString("yyyy-MM-dd");

        var sayac = 0;
        sayac += await TeslimHatirlatmaAsync(db, bildirim, yarinBas, yarinBit, gunEtiketi, ct);
        sayac += await IadeHatirlatmaAsync(db, bildirim, bugunBas, bugunBit, gunEtiketi, ct);
        sayac += await KuyruktaYenidenDeneAsync(db, bildirim, now, ct);
        return sayac;
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
