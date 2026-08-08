using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.RateMatrices;

/// <summary>
/// Tarife matrisi master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// fiyat yapılandırmasıdır → <see cref="Permission.OperationsWrite"/>. Açılır liste/fiyat motoru
/// okuması (<see cref="ListActiveAsync"/>) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// Saf fiyat-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class RateMatrixService(IRateMatrixRepository repository, ICurrentUser currentUser)
{
    private readonly IRateMatrixRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RateMatrix>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Fiyat motoru / açılır liste kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<RateMatrix>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<RateMatrix?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    /// <summary>Fiyat ÇÖZÜMLEME: kanal/şube/grup/tarih/gün-sayısı için matristen günlük+toplam fiyat.
    /// Yetki gerektirmez (salt fiyat okuma; ListActive gibi). Eşleşme yoksa null. Deftere dokunmaz.
    /// YALNIZ tarife-matris ekranının fiyat-sorgu paneli (RateMatrixList) + test-oracle kullanır;
    /// booking/kira fiyat akışının üretim çözümleyicisi <c>RentalQuoteEngine.SelectMatrix</c>'tir.</summary>
    public async Task<RateMatrisSonuc?> CozumleAsync(RateMatrisSorgu sorgu, CancellationToken ct = default)
        => RateMatrisCozumleme.Coz(await _repository.ListActiveAsync(ct), sorgu);

    public async Task<Guid> CreateAsync(RateMatrixInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife matrisi zaten var.");

        var row = new RateMatrix();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, RateMatrixInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife matrisi zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    /// <summary>
    /// FAZ-31 — bir Rezervasyon Kaynağının (<see cref="RateMatrix.Kanal"/>) tarife satırlarını
    /// TOPLU siler.
    ///
    /// <para><b>YALNIZ <see cref="TarifeOnayDurumu.Bekliyor"/> SİLİNİR.</b> Başka bir durum
    /// istenirse gürültülü red — sessizce daraltmak, kullanıcının "onaylıları da sildim" sanmasına
    /// yol açardı. Çit güvenlik kararıdır: fiyat motoru (<c>RentalQuoteEngine.RowMatches</c>,
    /// <c>RateMatrisCozumleme</c>) YALNIZ <c>Onayli</c> satırları kullanır; dolayısıyla bu işlem
    /// hiçbir kirada/rezervasyonda O AN kullanılan bir tarifeyi kaybettiremez. Onaylı satırların
    /// toplu silinmesi ayrı bir karar konusudur ve bu fazda AÇILMAMIŞTIR.</para>
    ///
    /// <para><b>Yetki <see cref="Permission.ManageUsers"/></b> — tek-satır silmeden (OperationsWrite)
    /// bilinçli olarak DAHA DAR: toplu silmenin etki alanı farklı. Web tarafında da aynı çit var
    /// (import grubu ManageUsers + sayfa Admin) — çift savunma.</para>
    ///
    /// <para><b>KANALIN TÜM ŞUBELERİ silinir</b> — şube kırılımı yoktur. Ekranda şube filtresi
    /// açıkken silme butonu GİZLENİR (adversarial M1): aksi hâlde kullanıcı "gördüğümü siliyorum"
    /// sanırken ekranda hiç görünmeyen başka şubelerin satırları da giderdi.</para>
    ///
    /// <para>Kanal eşleşmesi <see cref="StringComparison.OrdinalIgnoreCase"/> ile BELLEKTE yapılır:
    /// (a) motorun kanal eşleşmesi de aynı comparer'ı kullanır; (b) SQL <c>lower()</c>/<c>ILIKE</c>
    /// veritabanı collation'ına bağlıdır (PG "İstanbul"→"istanbul", .NET→"i̇stanbul") ve yerelde
    /// yeşil/CI'da kırmızı davranış üretirdi. Tek fark: burada <c>Kanal</c> ayrıca TRIM edilir,
    /// motor etmez — yani silme, motorun eşleyemeyeceği baştaki/sondaki boşluklu bir satırı da
    /// aday sayar. Kayıp riski YOK: aday kümesi zaten yalnız <c>Bekliyor</c> satırlardır ve motor
    /// onları hiç kullanmaz (yazma yolları da Kanal'ı trim ediyor; bu yalnız artık temizliğidir).</para>
    ///
    /// <para><b>Kanalsız (null) satırlar ASLA silinmez:</b> onlar kanal-agnostik taban tarifedir,
    /// bir kaynağa ait değildir.</para>
    /// </summary>
    /// <returns>Silinen satır sayısı.</returns>
    public async Task<int> DeleteByKanalAsync(string? kanal, TarifeOnayDurumu sadeceDurum,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);

        if (sadeceDurum != TarifeOnayDurumu.Bekliyor)
            throw new ValidationException(
                "Toplu silme yalnız 'Bekliyor' durumundaki tarife satırları için yapılabilir; onaylı tarifeler bu yolla silinemez.");

        if (string.IsNullOrWhiteSpace(kanal))
            throw new ValidationException("Silinecek rezervasyon kaynağı (kanal) seçilmelidir.");

        var hedefler = (await _repository.ListAsync(ct))
            .Where(r => SilmeAdayi(r, kanal))
            .Select(r => r.Id)
            .ToList();

        return await _repository.DeleteManyAsync(hedefler, ct);
    }

    /// <summary>
    /// Toplu silme adaylığı: kanalı eşleşen VE onaylanmamış satır. Kanalsız satır (kanal-agnostik
    /// taban tarife) hiçbir kaynağa ait değildir → aday DEĞİL.
    ///
    /// <para><b>SAF ve PUBLIC:</b> onay adımındaki "N satır silinecek" sayısı da bu yüklemle
    /// hesaplanır. Ekran ayrı bir sayım yazsaydı kural değiştiğinde iki sayı sessizce ayrışır,
    /// kullanıcı yanlış sayıyı onaylardı. Saf olduğu için ekstra bir DB okuması da gerekmez —
    /// sayfa zaten elindeki listeyi kullanır.</para>
    /// </summary>
    public static bool SilmeAdayi(RateMatrix r, string? kanal)
        => !string.IsNullOrWhiteSpace(kanal)
           && r.OnayDurumu == TarifeOnayDurumu.Bekliyor
           && r.Kanal is not null
           && string.Equals(r.Kanal.Trim(), kanal.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Validate(RateMatrixInput n)
    {
        // 0/negatif "max gün" hiçbir kirayı kapsamaz → satır ölü doğar; sınırsız için null kullanılır.
        if (n.KiraSuresi is <= 0) throw new ValidationException("Max kira kapsamı pozitif olmalıdır.");
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Tarife kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Tarife kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Tarife adı zorunludur.");

        Pos(n.Gun1, "Gün 1 fiyatı"); Pos(n.Gun2, "Gün 2 fiyatı"); Pos(n.Gun3, "Gün 3 fiyatı");
        Pos(n.Gun4, "Gün 4 fiyatı"); Pos(n.Gun5, "Gün 5 fiyatı"); Pos(n.Gun6, "Gün 6 fiyatı");
        Pos(n.Gun7, "Gün 7 fiyatı");
        Pos(n.GunHaftalik, "Haftalık kademe (8-29 gün) fiyatı"); Pos(n.GunAylik, "Aylık kademe (30+ gün) fiyatı");
        if (n.MaxEsneklik is < 0m or > 100m)
            throw new ValidationException("Esneklik (indirim) oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.BasTar is { } b && n.BitTar is { } t && t < b)
            throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.");
    }

    private static void Pos(decimal? v, string label)
    {
        if (v is < 0m) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static RateMatrixInput Normalize(RateMatrixInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Kanal = TrimOrNull(input.Kanal),
        Sube = TrimOrNull(input.Sube),
        Lokasyon = TrimOrNull(input.Lokasyon),
        // FAZ-70 — Normalize YENİ nesne kurar: buraya eklenmeyen alan sessizce kaybolur.
        Turu = TrimOrNull(input.Turu),
        KiraSuresi = input.KiraSuresi,
        AracGrupKod = string.IsNullOrWhiteSpace(input.AracGrupKod) ? null : input.AracGrupKod.Trim().ToUpperInvariant(),
        ParaBirimi = string.IsNullOrWhiteSpace(input.ParaBirimi) ? null : input.ParaBirimi.Trim().ToUpperInvariant(),
        BasTar = input.BasTar,
        BitTar = input.BitTar,
        Gun1 = input.Gun1, Gun2 = input.Gun2, Gun3 = input.Gun3, Gun4 = input.Gun4,
        Gun5 = input.Gun5, Gun6 = input.Gun6, Gun7 = input.Gun7,
        GunHaftalik = input.GunHaftalik, GunAylik = input.GunAylik,
        MaxEsneklik = input.MaxEsneklik,
        OnayDurumu = input.OnayDurumu,
        Onaylayan = TrimOrNull(input.Onaylayan),
        OnayZaman = input.OnayZaman,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(RateMatrix row, RateMatrixInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.Aciklama = n.Aciklama;
        row.Kanal = n.Kanal;
        row.Sube = n.Sube;
        row.Lokasyon = n.Lokasyon;
        row.Turu = n.Turu;
        row.KiraSuresi = n.KiraSuresi;
        row.AracGrupKod = n.AracGrupKod;
        row.ParaBirimi = n.ParaBirimi;
        row.BasTar = n.BasTar;
        row.BitTar = n.BitTar;
        row.Gun1 = n.Gun1; row.Gun2 = n.Gun2; row.Gun3 = n.Gun3; row.Gun4 = n.Gun4;
        row.Gun5 = n.Gun5; row.Gun6 = n.Gun6; row.Gun7 = n.Gun7;
        row.GunHaftalik = n.GunHaftalik; row.GunAylik = n.GunAylik;
        row.MaxEsneklik = n.MaxEsneklik;
        row.OnayDurumu = n.OnayDurumu;
        row.Onaylayan = n.Onaylayan;
        row.OnayZaman = n.OnayZaman;
        row.Aktif = n.Aktif;
    }
}
