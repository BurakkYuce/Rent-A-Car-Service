using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Pricing;

/// <summary>Kaydedilmiş maliyet teklifi kalıcılığı (FAZ-74).</summary>
public interface IMaliyetTeklifiRepository
{
    Task<IReadOnlyList<MaliyetTeklifi>> SearchAsync(MaliyetTeklifiFilter filtre, CancellationToken ct = default);
    Task<MaliyetTeklifi?> FindAsync(Guid id, CancellationToken ct = default);
    /// <summary>Kaydı yazar ve boşluksuz <c>MT-</c> numarasını AYNI transaction'da tahsis eder.</summary>
    Task CreateAsync(MaliyetTeklifi row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<MaliyetTeklifi> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Teklif arama filtresi (canlı <c>maliyet_hesaplama_ara.aspx</c>). Boş alan = kısıt yok.</summary>
public sealed class MaliyetTeklifiFilter
{
    /// <summary>Başlık/kayıt no/açıklama içinde geçen metin (büyük-küçük harf duyarsız).</summary>
    public string? Metin { get; set; }
    public string? Plaka { get; set; }
    public Guid? CariId { get; set; }
    public DateTimeOffset? TarihMin { get; set; }
    public DateTimeOffset? TarihMax { get; set; }
    /// <summary>Aylık net teklif alt sınırı (araç başına).</summary>
    public decimal? FiyatMin { get; set; }
    /// <summary>Aylık net teklif üst sınırı (araç başına).</summary>
    public decimal? FiyatMax { get; set; }
}

/// <summary>
/// Teklif kaydetme girdisi: künye + hesap girdisi. Sonuç alanları BURADA YOK — servis hesabı
/// <see cref="MaliyetHesapService"/> ile kendisi yapar. İstemciden gelen sonuç kabul edilseydi
/// girdiyle tutmayan (elle düzenlenmiş) bir teklif kaydedilebilirdi.
/// </summary>
public sealed class MaliyetTeklifiInput
{
    public string? Baslik { get; set; }
    public string? Plaka { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public Guid? CariId { get; set; }
    public Guid? HazirlayanId { get; set; }
    public string? Aciklama { get; set; }
    public MaliyetHesapInput Girdi { get; set; } = new();
}

/// <summary>Liste başlığı özeti — <b>araç-başı DEĞİL, filo</b> toplamları (adet dahil).</summary>
public sealed record MaliyetTeklifiOzet(int Adet, int AracAdet, decimal FiloAylikNet, decimal FiloKdvli);

/// <summary>
/// Kaydedilmiş maliyet teklifi servisi (FAZ-74).
///
/// <para><b>DEFTERE YAZMAZ.</b> Teklif bir planlama belgesidir; gelir/gider/cari bakiye
/// raporlarına hiçbir satır eklemez. Yetki <see cref="Permission.FinanceWrite"/> — maliyet/kâr
/// marjı ticari sır sayılır ve operasyon rolüne açılmaz; okuma <see cref="Permission.ViewReports"/>
/// ile de mümkündür (yazan rol okuyabilmeli).</para>
/// </summary>
public sealed class MaliyetTeklifiService(IMaliyetTeklifiRepository repository, ICurrentUser currentUser)
{
    private readonly IMaliyetTeklifiRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<IReadOnlyList<MaliyetTeklifi>> SearchAsync(
        MaliyetTeklifiFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.SearchAsync(filtre ?? new MaliyetTeklifiFilter(), ct);
    }

    public async Task<MaliyetTeklifi?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.FindAsync(id, ct);
    }

    public static MaliyetTeklifiOzet Ozet(IEnumerable<MaliyetTeklifi> satirlar)
    {
        var l = satirlar.ToList();
        return new MaliyetTeklifiOzet(
            l.Count,
            l.Sum(x => x.AracSayisi),
            l.Sum(x => x.FiloTeklifAylikNet),
            l.Sum(x => x.FiloTeklifKdvli));
    }

    public async Task<Guid> CreateAsync(MaliyetTeklifiInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var row = new MaliyetTeklifi();
        Uygula(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>Teklifi yeniden hesaplayıp SNAPSHOT'ı baştan yazar (girdi ile sonuç asla ayrışmaz).</summary>
    public async Task<bool> UpdateAsync(Guid id, MaliyetTeklifiInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var kopya = new MaliyetTeklifi();
        Uygula(kopya, input);   // doğrulama + hesap ÖNCE: geçersiz girdide mevcut satıra dokunulmaz
        return await _repository.UpdateAsync(id, r =>
        {
            Kopyala(kopya, r);
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return await _repository.DeleteAsync(id, ct);
    }

    /// <summary>Kayıtlı snapshot'tan hesap girdisini geri kurar (teklifi forma yükleme / kopyalama).</summary>
    public static MaliyetHesapInput GirdiyeCevir(MaliyetTeklifi t) => new()
    {
        AlisBedeli = t.AlisBedeli,
        ResidualYuzde = t.ResidualYuzde,
        SureAy = t.SureAy,
        FaizOran = t.FaizOran,
        KkdfOran = t.KkdfOran,
        BsmvOran = t.BsmvOran,
        DamgaOran = t.DamgaOran,
        KarMarji = t.KarMarji,
        KdvOran = t.KdvOran,
        EnflasyonOran = t.EnflasyonOran,
        KrediHesaplamaSekli = t.KrediHesaplamaSekli,
        AracSayisi = t.AracSayisi,
        KaskoYillik = t.KaskoYillik,
        TrafikSigortasiYillik = t.TrafikSigortasiYillik,
        MtvYillik = t.MtvYillik,
        BakimYillik = t.BakimYillik,
        LastikYillik = t.LastikYillik,
        LastikKisYillik = t.LastikKisYillik,
        AracTakipYillik = t.AracTakipYillik,
        TescilPlakaYillik = t.TescilPlakaYillik,
        MuayeneEmisyonYillik = t.MuayeneEmisyonYillik,
        YedekAracYillik = t.YedekAracYillik,
        YonetimGideriAylik = t.YonetimGideriAylik,
        AylikGider = t.AylikGider,
        BankaDosyaDigerMasraf = t.BankaDosyaDigerMasraf
    };

    /// <summary>Snapshot'tan kalem dökümünü yeniden üretir (kayıtlı girdiden — yeniden fiyatlama değil).</summary>
    public static IReadOnlyList<MaliyetGiderKalem> Kalemler(MaliyetTeklifi t)
    {
        try { return MaliyetHesapService.Hesapla(GirdiyeCevir(t)).Kalemler; }
        catch (ValidationException) { return []; }   // eski/geçersiz snapshot listeyi patlatmasın
    }

    private static void Uygula(MaliyetTeklifi row, MaliyetTeklifiInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Baslik)) throw new ValidationException("Teklif başlığı zorunludur.");
        var tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        // Teklif GELECEK tarihli olamaz: "planlama belgesi" ileri tarihe atılırsa arama/dönem
        // süzgeçleri sessizce yanlış kovaya düşer.
        TarihPolitikasi.ParaTarihi(tarih, "Teklif");

        // Hesap BURADA yapılır — Rotatif reddi dahil tüm doğrulamalar tek kapıdan geçer.
        var sonuc = MaliyetHesapService.Hesapla(n.Girdi);
        var g = n.Girdi;

        row.Baslik = n.Baslik.Trim();
        row.Plaka = Trim(n.Plaka);
        row.Tarih = tarih;
        row.CariId = n.CariId;
        row.HazirlayanId = n.HazirlayanId;
        row.Aciklama = Trim(n.Aciklama);

        row.AlisBedeli = g.AlisBedeli;
        row.ResidualYuzde = g.ResidualYuzde;
        row.SureAy = g.SureAy;
        row.FaizOran = g.FaizOran;
        row.KkdfOran = g.KkdfOran;
        row.BsmvOran = g.BsmvOran;
        row.DamgaOran = g.DamgaOran;
        row.KarMarji = g.KarMarji;
        row.KdvOran = g.KdvOran;
        row.EnflasyonOran = g.EnflasyonOran;
        row.KrediHesaplamaSekli = g.KrediHesaplamaSekli;
        row.AracSayisi = g.AracSayisi;

        row.KaskoYillik = g.KaskoYillik;
        row.TrafikSigortasiYillik = g.TrafikSigortasiYillik;
        row.MtvYillik = g.MtvYillik;
        row.BakimYillik = g.BakimYillik;
        row.LastikYillik = g.LastikYillik;
        row.LastikKisYillik = g.LastikKisYillik;
        row.AracTakipYillik = g.AracTakipYillik;
        row.TescilPlakaYillik = g.TescilPlakaYillik;
        row.MuayeneEmisyonYillik = g.MuayeneEmisyonYillik;
        row.YedekAracYillik = g.YedekAracYillik;
        row.YonetimGideriAylik = g.YonetimGideriAylik;
        row.AylikGider = g.AylikGider;
        row.BankaDosyaDigerMasraf = g.BankaDosyaDigerMasraf;

        row.ResidualDeger = sonuc.ResidualDeger;
        row.NetAmortisman = sonuc.NetAmortisman;
        row.FinansmanFaiz = sonuc.FinansmanFaiz;
        row.FinansmanVergi = sonuc.FinansmanVergi;
        row.Damga = sonuc.Damga;
        row.ToplamGider = sonuc.ToplamGider;
        row.ToplamMaliyet = sonuc.ToplamMaliyet;
        row.BasaBasAylik = sonuc.BasaBasAylik;
        row.Kar = sonuc.Kar;
        row.TeklifNet = sonuc.TeklifNet;
        row.TeklifAylikNet = sonuc.TeklifAylikNet;
        row.TeklifKdvli = sonuc.TeklifKdvli;
    }

    /// <summary>KayitNo / Id / audit HARİÇ tüm alanları taşır (numara güncellemede DEĞİŞMEZ).</summary>
    private static void Kopyala(MaliyetTeklifi k, MaliyetTeklifi r)
    {
        r.Baslik = k.Baslik; r.Plaka = k.Plaka; r.Tarih = k.Tarih;
        r.CariId = k.CariId; r.HazirlayanId = k.HazirlayanId; r.Aciklama = k.Aciklama;

        r.AlisBedeli = k.AlisBedeli; r.ResidualYuzde = k.ResidualYuzde; r.SureAy = k.SureAy;
        r.FaizOran = k.FaizOran; r.KkdfOran = k.KkdfOran; r.BsmvOran = k.BsmvOran;
        r.DamgaOran = k.DamgaOran; r.KarMarji = k.KarMarji; r.KdvOran = k.KdvOran;
        r.EnflasyonOran = k.EnflasyonOran; r.KrediHesaplamaSekli = k.KrediHesaplamaSekli;
        r.AracSayisi = k.AracSayisi;

        r.KaskoYillik = k.KaskoYillik; r.TrafikSigortasiYillik = k.TrafikSigortasiYillik;
        r.MtvYillik = k.MtvYillik; r.BakimYillik = k.BakimYillik;
        r.LastikYillik = k.LastikYillik; r.LastikKisYillik = k.LastikKisYillik;
        r.AracTakipYillik = k.AracTakipYillik; r.TescilPlakaYillik = k.TescilPlakaYillik;
        r.MuayeneEmisyonYillik = k.MuayeneEmisyonYillik; r.YedekAracYillik = k.YedekAracYillik;
        r.YonetimGideriAylik = k.YonetimGideriAylik; r.AylikGider = k.AylikGider;
        r.BankaDosyaDigerMasraf = k.BankaDosyaDigerMasraf;

        r.ResidualDeger = k.ResidualDeger; r.NetAmortisman = k.NetAmortisman;
        r.FinansmanFaiz = k.FinansmanFaiz; r.FinansmanVergi = k.FinansmanVergi;
        r.Damga = k.Damga; r.ToplamGider = k.ToplamGider; r.ToplamMaliyet = k.ToplamMaliyet;
        r.BasaBasAylik = k.BasaBasAylik; r.Kar = k.Kar; r.TeklifNet = k.TeklifNet;
        r.TeklifAylikNet = k.TeklifAylikNet; r.TeklifKdvli = k.TeklifKdvli;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
