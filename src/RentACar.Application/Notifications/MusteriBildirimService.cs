using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>
/// Müşteriye giden bildirimin TEK giriş noktası: şablonu bulur, yer tutucuları doldurur, kalıcı
/// gönderim kaydını yazar, kanaldan gönderir ve sonucu kaydeder.
///
/// <para><b>Sıralama bilinçli:</b> önce KAYIT, sonra GÖNDERİM. Ters sırada, gönderim başarılı olup
/// kayıt yazılamadığında (yarış/çökme) aynı mesaj bir daha gönderilirdi. Kayıt önce yazılınca
/// benzersiz index ikinci denemeyi zaten reddeder; en kötü senaryo "kayıt var, gönderim yeniden
/// denenecek"tir — müşteriye çift mesaj değil.</para>
///
/// <para><b>Yetki:</b> gönderim bir OPERASYON yan etkisidir, ayrı bir izin kapısı YOKTUR — çağıran
/// akış (rezervasyon oluşturma, job) zaten kendi guard'ından geçmiştir. Şablon YÖNETİMİ ise ayarlar
/// sınıfıdır ve <see cref="Permission.ManageUsers"/> ister.</para>
/// </summary>
public sealed class MusteriBildirimService(
    IMesajRepository repository, BildirimKanaliService kanal, ICurrentUser currentUser)
{
    /// <summary>Kalıcı başarısız sayılmadan önceki deneme hakkı.</summary>
    public const int MaxDeneme = 5;

    // ---- Şablon yönetimi (ayarlar sınıfı → ManageUsers) ----

    public async Task<IReadOnlyList<MesajSablonRow>> SablonListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return await repository.SablonListAsync(ct);
    }

    public async Task SablonKaydetAsync(MesajSablonInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        if (string.IsNullOrWhiteSpace(input.Govde))
            throw new ValidationException("Mesaj gövdesi boş olamaz.");
        if (input.Kanal == MesajKanal.Eposta && string.IsNullOrWhiteSpace(input.Konu))
            throw new ValidationException("E-posta şablonunda konu zorunludur.");
        if (input.Kanal == MesajKanal.Sms && input.Govde.Length > 600)
            throw new ValidationException("SMS gövdesi 600 karakteri aşamaz.");
        await repository.SablonUpsertAsync(input, ct);
    }

    public async Task<IReadOnlyList<GidenMesajRow>> GidenListAsync(
        GidenMesajFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ViewReports);
        return await repository.MesajListAsync(filter, ct);
    }

    // ---- Gönderim ----

    /// <summary>
    /// Bildirimi gönderir (idempotent). <paramref name="izinVar"/> müşterinin o kanaldan iletişim
    /// izni — KVKK/İYS gereği ÇAĞIRAN tarafından çözülür (müşteri kaydından okunur) ve izin yoksa
    /// mesaj hiç oluşturulmaz.
    /// </summary>
    public async Task<MesajSonuc> GonderAsync(MesajIstegi istek, bool izinVar, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(istek.Alici))
            return new MesajSonuc(GidenMesajDurum.Basarisiz, "Alıcı adresi/numarası boş.");
        if (string.IsNullOrWhiteSpace(istek.Anahtar))
            throw new ValidationException("Bildirim idempotency anahtarı boş olamaz.");

        // Zaten bu olay için kayıt var mı? Varsa ve gönderildiyse tekrar GÖNDERİLMEZ.
        var mevcut = await repository.MesajBulAsync(istek.Anahtar, ct);
        if (mevcut is not null)
        {
            if (mevcut.Durum is GidenMesajDurum.Gonderildi or GidenMesajDurum.IzinYok
                || mevcut.DenemeSayisi >= MaxDeneme)
                return new MesajSonuc(mevcut.Durum, mevcut.Hata);
            return await DeneAsync(mevcut, ct);
        }

        if (!izinVar)
        {
            // İzinsiz gönderim YAPILMAZ ama kayıt YAZILIR: "neden gitmedi" sorusunun cevabı ve
            // aynı olay için tekrar tekrar denenmemesi için (terminal durum).
            var izinsiz = Kayit(istek, konu: null, govde: "(izin yok — gönderilmedi)");
            izinsiz.Durum = GidenMesajDurum.IzinYok;
            izinsiz.Hata = "Müşteri bu kanaldan iletişim izni vermemiş (KVKK/İYS).";
            await repository.MesajEkleAsync(izinsiz, ct);
            return new MesajSonuc(GidenMesajDurum.IzinYok, izinsiz.Hata);
        }

        var sablon = await repository.SablonBulAsync(istek.Tur, istek.Kanal, ct);
        var htmlKacis = istek.Kanal == MesajKanal.Eposta;
        var yeni = Kayit(istek,
            konu: sablon is null ? null : SablonDoldur.Doldur(sablon.Konu, istek.Degerler, htmlKacis: false),
            govde: sablon is null ? string.Empty : SablonDoldur.Doldur(sablon.Govde, istek.Degerler, htmlKacis));

        if (sablon is null || !sablon.Aktif)
        {
            // Şablon eksik/kapalı: KUYRUKTA bırakılır, kalıcı başarısız SAYILMAZ. Firma şablonu
            // tanımlayınca yeniden deneme işi mesajı gönderir — aksi halde kayıp mesaj olurdu.
            yeni.Durum = GidenMesajDurum.Kuyrukta;
            yeni.Hata = sablon is null
                ? $"'{istek.Tur}' türü için {istek.Kanal} şablonu tanımlı değil."
                : $"'{istek.Tur}' türü için {istek.Kanal} şablonu kapalı.";
            // Gövde boş kalır — şablon yazıldığında yeniden deneme onu DegerlerJson'dan üretir.
            yeni.Govde = yeni.Hata;
            await repository.MesajEkleAsync(yeni, ct);
            return new MesajSonuc(GidenMesajDurum.Kuyrukta, yeni.Hata);
        }

        if (!await repository.MesajEkleAsync(yeni, ct))
        {
            // Yarış: başka bir çağrı aynı anahtarı yazdı → onun sonucunu döndür, ikinci mesaj GÖNDERME.
            var yarisan = await repository.MesajBulAsync(istek.Anahtar, ct);
            return new MesajSonuc(yarisan?.Durum ?? GidenMesajDurum.Kuyrukta, yarisan?.Hata);
        }

        return await DeneAsync(yeni, ct);
    }

    /// <summary>
    /// Kuyruktaki bir kaydı (yeniden) göndermeyi dener ve sonucu kalıcılaştırır.
    /// Şablon eksikse gövde de eksiktir → önce şablon çözülür.
    /// </summary>
    public async Task<MesajSonuc> DeneAsync(GidenMesaj mesaj, CancellationToken ct = default)
    {
        var tur = Enum.TryParse<MesajTuru>(mesaj.Tur, out var t) ? t : (MesajTuru?)null;
        if (tur is null)
        {
            await repository.MesajGuncelleAsync(mesaj.Id, m =>
            {
                m.Durum = GidenMesajDurum.Basarisiz;
                m.Hata = $"Bilinmeyen mesaj türü: {mesaj.Tur}";
            }, ct);
            return new MesajSonuc(GidenMesajDurum.Basarisiz, $"Bilinmeyen mesaj türü: {mesaj.Tur}");
        }

        var sablon = await repository.SablonBulAsync(tur.Value, mesaj.Kanal, ct);
        if (sablon is null || !sablon.Aktif)
        {
            var hata = $"'{mesaj.Tur}' türü için {mesaj.Kanal} şablonu " + (sablon is null ? "tanımlı değil." : "kapalı.");
            await repository.MesajGuncelleAsync(mesaj.Id, m => m.Hata = hata, ct);
            return new MesajSonuc(GidenMesajDurum.Kuyrukta, hata);
        }

        // Gövde ve konu HER denemede şablondan yeniden üretilir. Şablon olay anında yoktu ve
        // sonradan yazıldıysa mesaj burada doğru içeriğe kavuşur — değerler kayıtta saklandığı için
        // (DegerlerJson) yeniden doldurma mümkündür. Şablon değiştiyse gönderilen metin de güncel
        // olur; "kuyrukta bekleyen mesaj eski metinle gitti" durumu oluşmaz.
        var htmlKacis = mesaj.Kanal == MesajKanal.Eposta;
        var degerler = DegerleriCoz(mesaj.DegerlerJson);
        var konu = SablonDoldur.Doldur(sablon.Konu, degerler, htmlKacis: false);
        var govde = SablonDoldur.Doldur(sablon.Govde, degerler, htmlKacis);
        if (string.IsNullOrWhiteSpace(govde))
        {
            const string bilgi = "Şablon gövdesi boş — gönderilecek içerik üretilemedi.";
            await repository.MesajGuncelleAsync(mesaj.Id, m =>
            {
                m.Durum = GidenMesajDurum.Basarisiz;
                m.Hata = bilgi;
            }, ct);
            return new MesajSonuc(GidenMesajDurum.Basarisiz, bilgi);
        }
        mesaj.Konu = konu;
        mesaj.Govde = govde;

        var (ok, hataMetni) = mesaj.Kanal switch
        {
            MesajKanal.Eposta => await EpostaDeneAsync(mesaj, ct),
            MesajKanal.Sms => (await kanal.SmsGonderAsync(mesaj.Alici, mesaj.Govde, ct), (string?)"SMS gönderilemedi."),
            _ => (false, "Bilinmeyen kanal."),
        };

        var durum = ok ? GidenMesajDurum.Gonderildi : GidenMesajDurum.Kuyrukta;
        var deneme = mesaj.DenemeSayisi + 1;
        if (!ok && deneme >= MaxDeneme) durum = GidenMesajDurum.Basarisiz;

        await repository.MesajGuncelleAsync(mesaj.Id, m =>
        {
            m.Durum = durum;
            m.DenemeSayisi = deneme;
            m.Konu = konu;
            m.Govde = govde; // "ne gönderdik" sorusunun cevabı GERÇEKTEN gönderilen metin olsun
            m.Hata = ok ? null : hataMetni;
            if (ok) m.GonderimUtc = DateTimeOffset.UtcNow;
        }, ct);

        return new MesajSonuc(durum, ok ? null : hataMetni);
    }

    private static IReadOnlyDictionary<string, string?> DegerleriCoz(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                   ?? new Dictionary<string, string?>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Bozuk JSON gönderimi ÇÖKERTMEZ: yer tutucular dolmaz, şablon ham hâliyle gider ve
            // operatör bunu giden mesaj ekranında görür. Kayıp mesajdan iyidir.
            return new Dictionary<string, string?>();
        }
    }

    private async Task<(bool, string?)> EpostaDeneAsync(GidenMesaj mesaj, CancellationToken ct)
    {
        var sonuc = await kanal.EpostaGonderAsync(
            mesaj.Alici, mesaj.Konu ?? string.Empty, mesaj.Govde, SablonDoldur.DuzMetin(mesaj.Govde), ct);
        return (sonuc.Ok, sonuc.Hata);
    }

    private static GidenMesaj Kayit(MesajIstegi istek, string? konu, string govde) => new()
    {
        DegerlerJson = System.Text.Json.JsonSerializer.Serialize(istek.Degerler),
        Anahtar = istek.Anahtar,
        Tur = istek.Tur.ToString(),
        Kanal = istek.Kanal,
        Alici = istek.Alici.Trim(),
        Konu = konu,
        Govde = govde,
        KaynakTur = istek.KaynakTur,
        KaynakId = istek.KaynakId,
    };
}
