using RentACar.Domain.Entities;

namespace RentACar.Application.PublicSite;

/// <summary>PR-8: halka açık site talebi (lead) kalıcılığı.</summary>
public interface IPublicBookingRequestRepository
{
    Task AddAsync(PublicBookingRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default);

    /// <summary>PR-17: filtreli + sayfalı liste. Eski sınırsız <see cref="ListAsync"/> yalnız
    /// dönüştürme akışı için duruyor; ekran bu metodu kullanır.</summary>
    Task<(IReadOnlyList<PublicBookingRequest> Satirlar, int Toplam)> PagedAsync(
        PublicBookingRequestDurum? status, string? search, int page, int size, CancellationToken ct = default);

    /// <summary>PR-17: nav sayacı + Home KPI — `Yeni` durumdaki talep sayısı.</summary>
    Task<int> NewCountAsync(CancellationToken ct = default);

    /// <summary>PR-17: en eski `Yeni` talebin oluşma zamanı (KPI: "en eskisi 3 gündür bekliyor"). Yoksa null.</summary>
    Task<DateTimeOffset?> OldestNewAsync(CancellationToken ct = default);

    Task<PublicBookingRequest?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// ATOMİK claim: <c>Durum</c> TERMİNAL DEĞİLSE hedef duruma çeker. TEK SQL UPDATE — iki personel
    /// aynı anda "Dönüştür"e basarsa yalnız BİRİ true alır (yarış güvenli). Etkilenen satır 0 ise
    /// talep zaten kapanmıştır.
    ///
    /// <para><b>PR-17'de yüklem GENİŞLETİLDİ:</b> eskiden <c>Durum == Yeni</c> idi. Ara durumlar
    /// (<c>Iletisimde</c>/<c>TeklifVerildi</c>) eklenince bu yüklem, üzerinde çalışılan bir talebi
    /// dönüştürülemez hale getiriyordu — personel "İletişimde" işaretlediği anda lead kilitleniyordu.
    /// Kural artık <c>TalepDurumu.Terminal</c> ile TEK yerden geliyor.</para>
    /// </summary>
    Task<bool> TryClaimAsync(Guid id, PublicBookingRequestDurum target, CancellationToken ct = default);

    /// <summary>PR-17: aktif durumlar arası geçiş (Yeni→İletişimde→Teklif) ve Kayıp işaretleme.
    /// Terminal durumda olan satırı DEĞİŞTİRMEZ (0 döner) — atomik, oku-kontrol-yaz değil.</summary>
    Task<bool> ChangeStatusAsync(Guid id, PublicBookingRequestDurum target, CancellationToken ct = default);

    /// <summary>PR-17: talebi kendine ata / atamayı kaldır (<paramref name="userId"/> null).</summary>
    Task<bool> AssignAsync(Guid id, Guid? userId, string? name, CancellationToken ct = default);

    /// <summary>PR-17: takip notu ekler (notlar SİLİNMEZ — geçmiş kanıttır).</summary>
    Task AddNoteAsync(TalepNotu not, CancellationToken ct = default);

    /// <summary>PR-17: bir talebin notları, en yeni önce.</summary>
    Task<IReadOnlyList<TalepNotu>> NotesAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>PR-17: liste ekranındaki "N not" göstergesi — talep başına not sayısı (tek sorgu).</summary>
    Task<Dictionary<Guid, int>> NoteCountsAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default);

    /// <summary>Claim SONRASI rezervasyon id'sini yazar (dönüştürme başarıyla tamamlandığında).</summary>
    Task SetConvertedReservationAsync(Guid id, Guid reservationId, CancellationToken ct = default);

    /// <summary>
    /// Claim'i GERİ ALIR (<c>DonusenReservationId=null</c> + durum <paramref name="previousStatus"/>) —
    /// dönüştürmenin Cari/Rezervasyon aşaması patlarsa satır "Donustu ama rezervasyonsuz" YARIM
    /// kalmasın, personel tekrar deneyebilsin.
    ///
    /// <para>PR-17: eskiden koşulsuz <c>Durum=Yeni</c> yazıyordu; ara durumlar eklenince bu, personelin
    /// "İletişimde/Teklif verildi" ilerlemesini SESSİZCE siliyordu. Artık claim ÖNCESİ durum geri
    /// yazılıyor.</para>
    /// </summary>
    Task ReleaseClaimAsync(Guid id, PublicBookingRequestDurum previousStatus, CancellationToken ct = default);

    /// <summary>Telefonla mevcut Cari arama (dönüştürmede yeniden müşteri yaratmamak için). Normalize
    /// edilmiş (yalnız rakam) karşılaştırma — "0555 111 22 33" ile "05551112233" AYNI sayılır.</summary>
    Task<Guid?> FindCustomerIdByPhoneAsync(string phone, CancellationToken ct = default);
}
