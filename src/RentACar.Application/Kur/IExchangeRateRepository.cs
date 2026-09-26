using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>TCMB günlük kur okuması (paylaşımlı/platform KurKayitlari; RLS yok). Yazma TcmbKurService'te.</summary>
public interface IExchangeRateRepository
{
    /// <summary>Bu kod için ≤tarih EN YENİ kur (hafta sonu/tatil fallback). Yoksa null.</summary>
    Task<KurKaydi?> GetAsync(string code, DateTimeOffset date, CancellationToken ct = default);

    /// <summary>≤tarih en yeni GÜNÜN tüm kurları (görüntüleme).</summary>
    Task<IReadOnlyList<KurKaydi>> ListByDateAsync(DateTimeOffset date, CancellationToken ct = default);

    /// <summary>Kayıtlı en yeni kur tarihi (yoksa null).</summary>
    Task<DateTimeOffset?> LatestDateAsync(CancellationToken ct = default);
}
