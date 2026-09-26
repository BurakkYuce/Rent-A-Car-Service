using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// Rezervasyon kaynağı master tanımı — <see cref="MasterDefinitionService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("reservation-sources") tabandan gelir;
/// burada <see cref="ReservationSourceInput"/> (kod, ad, aktif) üçlüsüne + FAZ-24 tedarikçi/oran
/// alanlarına açılır.
///
/// <para><b>ORANLAR HESABA GİRMEZ (FAZ-24 kapsam çiti):</b> Kira/Hizmet/Drop oranları burada
/// yalnız SAKLANIR ve kopyalanır. Fiyat motoru, komisyon veya karlılık hesabı bu alanları
/// OKUMAZ — "hangi hesaba, ne zaman, geçmiş kayıtlara etkisiyle" girecekleri ayrı bir para
/// incelemesinin konusudur. Bu çit bilinçlidir: bir oranı sessizce hesaba bağlamak, kullanıcı
/// alanı "not" sanıp doldurduğunda faturayı değiştirirdi.</para>
///
/// <para><b>FAZ-49 kural matrisi:</b> alanlar iki sınıfa ayrılır — KURAL bayrakları (Uzatamaz,
/// RezTarihleriDegisemez, ProvizyonYok, KmSinirsiz, AyniYonDrop, MaxGun) rezervasyon/kira
/// akışında <see cref="ReservationSourceRule"/> ile GERÇEKTEN uygulanır; geri kalan tutar/oran/işaret
/// alanları BİLGİdir ve hiçbir hesaba girmez (KARARLAR.md FAZ-49). Ayrım entity'de alan alan
/// yazılıdır; oranların fiyata dokunmadığı kırılgan regresyon testiyle kilitlidir.</para>
/// </summary>
public sealed class ReservationSourceService(IReservationSourceRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterDefinitionService<ReservationSource>(repository, currentUser, cache, "reservation-sources", "rezervasyon kaynağı")
{
    private readonly IReservationSourceRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;

    /// <summary>Oran alanı üst sınırı — yüzde alanı numeric(5,2); 100'ün üstü tedarikçi oranı iş
    /// olarak anlamsız, negatif ise işaret hatası. DB kısıtından ÖNCE anlaşılır mesajla reddedilir.</summary>
    private const decimal RateMax = 100m;

    public Task<Guid> CreateAsync(ReservationSourceInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct, e => Extra(e, input));

    public Task<bool> UpdateAsync(Guid id, ReservationSourceInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct, e => Extra(e, input));

    /// <summary>F11.1a — full replacement with optimistic concurrency (409 <c>cakisma</c> on a stale version).
    /// The same extra-field hook as the create path (copy-constructor trap).</summary>
    public Task<bool> UpdateAsync(Guid id, ReservationSourceInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, expectedVersion, ct, e => Extra(e, input));

    /// <summary>
    /// "Aşağıya Yansıt": seçili kaynağın 3 oranını diğer <b>AKTİF</b> kaynaklara kopyalar.
    ///
    /// <para><b>KAPSAM ÇİTİ:</b> yalnız <c>RezervasyonKaynaklari</c> tablosuna yazar. Hiçbir
    /// rezervasyon/fatura/defter kaydına dokunmaz — kayıtlı belgelerin oranı geçmişe dönük
    /// DEĞİŞMEZ. Pasif kaynaklar bilinçli DIŞARIDA: pasif kayıt tarihsel bir tanımdır, toplu
    /// işlem onu diriltmemeli.</para>
    ///
    /// <para>Kaynağın kendisi de dışarıda (kendine kopyalamak anlamsız). Boş oran da kopyalanır —
    /// "hepsini temizle" meşru bir işlemdir; yalnız dolu olanları kopyalamak sessizce kısmi
    /// sonuç verirdi.</para>
    /// </summary>
    /// <returns>Güncellenen satır sayısı.</returns>
    public async Task<int> ReflectRatesAsync(Guid sourceId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var source = await _repository.FindAsync(sourceId, ct)
            ?? throw new ValidationException("Kaynak bulunamadı.");

        var count = await _repository.ReflectRatesAsync(
            sourceId, source.KiraOrani, source.HizmetOrani, source.DropOrani, ct);
        _cache.Invalidate("reservation-sources");
        return count;
    }

    /// <summary>Kod/Ad/Aktif dışındaki alanlar — create ve update yollarının İKİSİNDE de aynı
    /// kanca kullanılır (yalnız birine yazmak alanı sessizce düşürürdü).</summary>
    private static void Extra(ReservationSource e, ReservationSourceInput input)
    {
        e.Tedarikci = Text(input.Tedarikci, 128, "Tedarikçi");
        e.KiraOrani = Rate(input.KiraOrani, "Kira oranı");
        e.HizmetOrani = Rate(input.HizmetOrani, "Hizmet oranı");
        e.DropOrani = Rate(input.DropOrani, "Drop oranı");

        // ---- FAZ-49 kural matrisi ----------------------------------------------------------
        e.KaynakGrubu = input.KaynakGrubu;

        // KURAL bayrakları — rezervasyon/kira akışında GERÇEKTEN uygulanır (RezKaynakKural).
        e.Uzatamaz = input.Uzatamaz;
        e.RezTarihleriDegisemez = input.RezTarihleriDegisemez;
        e.ProvizyonYok = input.ProvizyonYok;
        e.KmSinirsiz = input.KmSinirsiz;
        e.AyniYonDrop = input.AyniYonDrop;
        e.MaxGun = MaxDays(input.MaxGun);

        // BİLGİ alanları — hiçbir fiyat/komisyon/defter hesabına girmez (KARARLAR.md FAZ-49).
        e.MaliyetYansitma = input.MaliyetYansitma;
        e.MatrisErken = input.MatrisErken;
        e.MatrisGecikme = input.MatrisGecikme;
        e.MatrisIptal = input.MatrisIptal;
        e.MatrisNoShow = input.MatrisNoShow;
        e.MatrisUzatma = input.MatrisUzatma;

        e.SigortaKaynakNo = Text(input.SigortaKaynakNo, 64, "Sigorta kaynak no");
        e.DropKaynakNo = Text(input.DropKaynakNo, 64, "Drop kaynak no");
        e.ProvizyonSecenek = Text(input.ProvizyonSecenek, 64, "Provizyon seçeneği");
        e.MuafiyatSecenek = Text(input.MuafiyatSecenek, 64, "Muafiyet seçeneği");

        e.ScdwDahil = input.ScdwDahil;
        e.CdwDahil = input.CdwDahil;
        e.LcfDahil = input.LcfDahil;
        e.PaiDahil = input.PaiDahil;

        e.BebekKoltugu = Amount(input.BebekKoltugu, "Bebek koltuğu tutarı");
        e.Navigasyon = Amount(input.Navigasyon, "Navigasyon tutarı");
        e.EkSurucu = Amount(input.EkSurucu, "Ek sürücü tutarı");
        e.Wifi = Amount(input.Wifi, "Wifi tutarı");

        e.KomisyonOrani = Rate(input.KomisyonOrani, "Komisyon oranı");
        e.OnOdemeOrani = Rate(input.OnOdemeOrani, "Ön ödeme oranı");
        e.IndirimOrani = Rate(input.IndirimOrani, "İndirim oranı");
        e.PuanOrani = Rate(input.PuanOrani, "Puan oranı");

        e.MailAdres = Text(input.MailAdres, 256, "Mail adresi");
        e.OtomatikMailGitme = input.OtomatikMailGitme;
        e.RiskAnalizYapma = input.RiskAnalizYapma;
        e.SubeGor = input.SubeGor;
        e.AcenteFiyatDegistir = input.AcenteFiyatDegistir;
        e.Gizle = input.Gizle;
        e.SadeceMusteriOdeme = input.SadeceMusteriOdeme;
    }

    private static decimal? Rate(decimal? value, string alan)
    {
        if (value is not { } o) return null;
        if (o < 0m) throw new ValidationException($"{alan} negatif olamaz.");
        if (o > RateMax) throw new ValidationException($"{alan} en çok %{RateMax:0} olabilir.");
        return decimal.Round(o, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Ek hizmet varsayılan TUTARI (bilgi): negatif reddedilir, 4 haneye yuvarlanır
    /// (kolon numeric(19,4) — DB kısıtı yerine anlaşılır mesaj).</summary>
    private static decimal? Amount(decimal? value, string alan)
    {
        if (value is not { } t) return null;
        if (t < 0m) throw new ValidationException($"{alan} negatif olamaz.");
        if (t > 9_999_999m) throw new ValidationException($"{alan} gerçekçi değil.");
        return decimal.Round(t, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>KURAL alanı: 0 "sınır yok" DEĞİL, "hiç kiralanamaz" olurdu → 0 ve negatif reddedilir;
    /// sınır yoksa alan BOŞ bırakılır (null). Üst sınır 3650 gün (anti-typo).</summary>
    private static int? MaxDays(int? value)
    {
        if (value is not { } g) return null;
        if (g <= 0) throw new ValidationException("En fazla gün 0 veya negatif olamaz; sınır yoksa alanı boş bırakın.");
        if (g > 3650) throw new ValidationException("En fazla gün 3650'yi aşamaz.");
        return g;
    }

    /// <summary>Serbest metin: trim + boş→null + aşımda temiz red (DB varchar taşması 500 yerine).</summary>
    private static string? Text(string? s, int max, string alan)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Length > max) throw new ValidationException($"{alan} en çok {max} karakter olabilir.");
        return t;
    }
}
