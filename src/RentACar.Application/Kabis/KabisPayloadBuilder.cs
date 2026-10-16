using RentACar.Application.Integrations;
using RentACar.Domain.Enums;

namespace RentACar.Application.Kabis;

/// <summary>
/// Kiralama/iade bildirim yükünü kaynaktan kurar (saf, testli). Kimlik numaraları burada ve YALNIZ bellekte çözülür;
/// sonuç kalıcılaşmaz. Eksik zorunlu veri <see cref="Built.Error"/> olarak döner (kalıcı hata: operatör cariyi
/// tamamlamadan tekrar denemek anlamsız).
/// </summary>
public static class KabisPayloadBuilder
{
    /// <param name="Secrets">Bu yükteki kimlik değerleri — hata metnini maskelemek için (<see cref="KabisErrorText"/>).</param>
    public sealed record Built(KabisKiralamaBildirimi? Notice, string? Error, IReadOnlyList<string> Secrets);

    public static Built Rental(KabisRentalSource s, DateTimeOffset deliveryUtc, Func<string?, string?> unprotect)
    {
        var secrets = new List<string>();
        var renter = Person(s.Kiralayan, unprotect, secrets);
        var corporate = s.Kiralayan.Tip != CustomerType.Bireysel;

        KabisKisi? driver = null;
        if (s.IkinciSurucu is { } second) driver = Person(second, unprotect, secrets);
        else if (!string.IsNullOrWhiteSpace(s.MisafirSurucuAd) || !string.IsNullOrWhiteSpace(s.MisafirSurucuSoyad))
            driver = new KabisKisi(Clean(s.MisafirSurucuAd), Clean(s.MisafirSurucuSoyad),
                null, null, null, null, null, null, null, null, null);

        string? error = null;
        if (string.IsNullOrWhiteSpace(s.Plaka)) error = "Araç plakası eksik.";
        else if (corporate)
        {
            if (string.IsNullOrWhiteSpace(s.Kiralayan.Unvan) && string.IsNullOrWhiteSpace(s.Kiralayan.Ad))
                error = "Kurumsal kiracının ünvanı eksik; cari kaydını tamamlayıp yeniden deneyin.";
        }
        else if (string.IsNullOrWhiteSpace(renter.Ad) || string.IsNullOrWhiteSpace(renter.Soyad))
            error = "Kiracının adı ya da soyadı eksik; cari kaydını tamamlayıp yeniden deneyin.";
        else if (string.IsNullOrWhiteSpace(renter.TcKimlik) && string.IsNullOrWhiteSpace(renter.PasaportNo))
            error = "Kiracının T.C. kimlik ya da pasaport numarası eksik; cari kaydını tamamlayıp yeniden deneyin.";
        else if (renter.TcKimlik is { } tc && (tc.Length != 11 || !tc.All(char.IsAsciiDigit)))
            error = "Kiracının T.C. kimlik numarası 11 haneli olmalı; cari kaydını düzeltip yeniden deneyin.";

        if (error is not null) return new Built(null, error, secrets);

        var notice = new KabisKiralamaBildirimi(
            Referans: s.SozlesmeNo,
            Plaka: s.Plaka.Trim(),
            AracMarkaModel: Clean(s.AracMarkaModel),
            // Kurumsal kiracıda kişi alanı GİTMEZ: kamu kurumunda kanun gereği yalnız sözleşme + araç bilgisi;
            // kişiyi temsil eden sürücü varsa Surucu'da gider.
            Kiralayan: corporate ? null : renter,
            Surucu: driver,
            KurumMu: corporate,
            KurumUnvan: corporate ? Clean(s.Kiralayan.Unvan) ?? Clean(s.Kiralayan.Ad) : null,
            KurumVergiNo: corporate ? Clean(s.Kiralayan.VergiNo) : null,
            TeslimUtc: deliveryUtc.ToUniversalTime(),
            PlanlananDonusUtc: s.BitTar.ToUniversalTime(),
            TeslimYeri: Clean(s.CikisOfisi),
            DonusYeri: Clean(s.DonusOfisi));
        if (corporate && Clean(s.Kiralayan.VergiNo) is { } vkn) secrets.Add(vkn);
        return new Built(notice, null, secrets);
    }

    public static KabisIadeBildirimi Return(KabisRentalSource s, string? externalReference, DateTimeOffset returnUtc)
        => new(s.SozlesmeNo, externalReference, s.Plaka.Trim(), returnUtc.ToUniversalTime(),
            Clean(s.DonusOfisi), s.DonusKm);

    private static KabisKisi Person(KabisPartySource p, Func<string?, string?> unprotect, List<string> secrets)
    {
        var tc = Digits(unprotect(p.TcKimlikEnc) ?? p.TcKimlikEski);
        var passport = Clean(unprotect(p.PasaportNoEnc) ?? p.PasaportNoEski);
        var license = Clean(unprotect(p.EhliyetNoEnc) ?? p.EhliyetNoEski);
        foreach (var v in new[] { tc, passport, license })
            if (v is not null) secrets.Add(v);
        var address = string.Join(", ", new[] { p.Adres, p.Ilce, p.Il }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        return new KabisKisi(
            Clean(p.Ad), Clean(p.Soyad), tc, passport, Clean(p.Ulke),
            CalendarDay(p.DogumTarihi), license, Clean(p.EhliyetSinifi), CalendarDay(p.EhliyetTarihi),
            Clean(p.CepTel), address.Length == 0 ? null : address);
    }

    /// <summary>
    /// Takvim günü alanı (doğum/ehliyet tarihi) İstanbul saatinde okunur (Türkiye 2016'dan beri sabit UTC+3): gün
    /// UTC gece yarısı olarak da (SPA tarih seçici) yerel gece yarısı olarak da (eski kayıt) yazılmış olsa AYNI
    /// güne düşer. Saf UTC okuma yerel gece yarısını bir gün geri kaydırırdı (form round-trip dersi).
    /// </summary>
    private static DateOnly? CalendarDay(DateTimeOffset? value)
        => value is { } v ? DateOnly.FromDateTime(v.ToOffset(TimeSpan.FromHours(3)).DateTime) : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>TC'deki boşluk/tire gibi ayırıcılar atılır; rakam dışı karakter kalırsa doğrulama yakalar.</summary>
    private static string? Digits(string? s)
    {
        var c = Clean(s);
        return c is null ? null : string.Concat(c.Where(ch => !char.IsWhiteSpace(ch) && ch != '-'));
    }
}
