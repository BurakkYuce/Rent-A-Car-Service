using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// Rezervasyon kaynağı master tanımı — <see cref="MasterTanimService{T}"/> ince alt sınıfı (O12d): doğrulama,
/// kod benzersizliği, CRUD, OperationsWrite guard ve liste cache ("reservation-sources") tabandan gelir;
/// burada <see cref="ReservationSourceInput"/> (kod, ad, aktif) üçlüsüne + FAZ-24 tedarikçi/oran
/// alanlarına açılır.
///
/// <para><b>ORANLAR HESABA GİRMEZ (FAZ-24 kapsam çiti):</b> Kira/Hizmet/Drop oranları burada
/// yalnız SAKLANIR ve kopyalanır. Fiyat motoru, komisyon veya karlılık hesabı bu alanları
/// OKUMAZ — "hangi hesaba, ne zaman, geçmiş kayıtlara etkisiyle" girecekleri ayrı bir para
/// incelemesinin konusudur. Bu çit bilinçlidir: bir oranı sessizce hesaba bağlamak, kullanıcı
/// alanı "not" sanıp doldurduğunda faturayı değiştirirdi.</para>
/// </summary>
public sealed class ReservationSourceService(IReservationSourceRepository repository, ICurrentUser currentUser, ITenantCache cache)
    : MasterTanimService<ReservationSource>(repository, currentUser, cache, "reservation-sources", "rezervasyon kaynağı")
{
    private readonly IReservationSourceRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;

    /// <summary>Oran alanı üst sınırı — yüzde alanı numeric(5,2); 100'ün üstü tedarikçi oranı iş
    /// olarak anlamsız, negatif ise işaret hatası. DB kısıtından ÖNCE anlaşılır mesajla reddedilir.</summary>
    private const decimal OranMax = 100m;

    public Task<Guid> CreateAsync(ReservationSourceInput input, CancellationToken ct = default)
        => CreateCoreAsync(input.Kod, input.Ad, input.Aktif, ct, e => Ek(e, input));

    public Task<bool> UpdateAsync(Guid id, ReservationSourceInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input.Kod, input.Ad, input.Aktif, ct, e => Ek(e, input));

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
    public async Task<int> OranlariYansitAsync(Guid kaynakId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var kaynak = await _repository.FindAsync(kaynakId, ct)
            ?? throw new ValidationException("Kaynak bulunamadı.");

        var adet = await _repository.OranlariYansitAsync(
            kaynakId, kaynak.KiraOrani, kaynak.HizmetOrani, kaynak.DropOrani, ct);
        _cache.Invalidate("reservation-sources");
        return adet;
    }

    /// <summary>Kod/Ad/Aktif dışındaki alanlar — create ve update yollarının İKİSİNDE de aynı
    /// kanca kullanılır (yalnız birine yazmak alanı sessizce düşürürdü).</summary>
    private static void Ek(ReservationSource e, ReservationSourceInput input)
    {
        e.Tedarikci = string.IsNullOrWhiteSpace(input.Tedarikci) ? null : input.Tedarikci.Trim();
        e.KiraOrani = Oran(input.KiraOrani, "Kira oranı");
        e.HizmetOrani = Oran(input.HizmetOrani, "Hizmet oranı");
        e.DropOrani = Oran(input.DropOrani, "Drop oranı");
    }

    private static decimal? Oran(decimal? deger, string alan)
    {
        if (deger is not { } o) return null;
        if (o < 0m) throw new ValidationException($"{alan} negatif olamaz.");
        if (o > OranMax) throw new ValidationException($"{alan} en çok %{OranMax:0} olabilir.");
        return decimal.Round(o, 2, MidpointRounding.AwayFromZero);
    }
}
