using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FirmaDokumanlar;

/// <summary>Yükleme girdisi. <see cref="Bytes"/> HAM dosya — tür kararı sunucuda verilir.</summary>
public sealed record FirmaDokumanInput(string? Baslik, string? Aciklama, string? DosyaAdi, byte[]? Bytes);

/// <summary>
/// Firmanın kendi PDF dokümanları ("Dokümanlar" ekranı).
///
/// <para><b>Yetki:</b> yükleme/silme <see cref="Permission.OperationsWrite"/>; listeleme/indirme
/// GUARD'SIZ — oturum açmış her personel (muhasebeci de) sahada çıktı alabilmeli. Kapı web
/// tarafındaki <c>RequireAuthorization()</c>, izolasyon RLS. Yeni bir <c>Permission</c> değeri
/// EKLENMEDİ (rol matrisi testleri dalgalanmasın; <c>PlatformBelgeService</c> ile aynı karar).</para>
///
/// <para><b>Sınırlar SERVİSTE</b> — UI'da butonu gizlemek yetmez, uçlara doğrudan POST edilebilir:
/// (1) tenant başına en fazla <see cref="MaxDokuman"/> belge, (2) dosya ≤ 3 MB, (3) yalnız PDF.
/// (1) ayrıca DB'de yuva unique index + CHECK ile yapısal; (3) MAGIC BYTE ile doğrulanır —
/// uzantı da Content-Type de İSTEMCİDEN gelir, ikisi de yalan söyleyebilir.</para>
/// </summary>
public sealed class FirmaDokumanService(IFirmaDokumanRepository repository, ICurrentUser currentUser)
{
    /// <summary>Tenant başına belge üst sınırı (ürün kararı). Yuva numaraları 1..10.</summary>
    public const int MaxDokuman = 10;

    /// <summary>Dosya üst sınırı — <see cref="PdfValidation.MaxBayt"/> (3 MB) ile TEK kaynaktan.</summary>
    public const long MaxBayt = PdfValidation.MaxBayt;

    // ---- Okuma: guard YOK (oturum açmış herkes) ----

    public Task<IReadOnlyList<FirmaDokumanSatiri>> ListeleAsync(CancellationToken ct = default)
        => repository.ListeleAsync(ct);

    /// <summary>İçeriği getirir; başka tenant'ın belgesi ise <c>null</c> (uç 404 döner) — "yok" ile
    /// "yetkisiz" ayırt EDİLMEZ, belgenin varlığı da bilgidir.</summary>
    public Task<FirmaDokumanIcerik?> IndirAsync(Guid id, CancellationToken ct = default)
        => repository.IndirAsync(id, ct);

    // ---- Yazma: OperationsWrite ----

    public async Task<Guid> YukleAsync(FirmaDokumanInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);

        var baslik = (input.Baslik ?? string.Empty).Trim();
        if (baslik.Length == 0) throw new ValidationException("Başlık zorunludur.");

        var bytes = input.Bytes;
        if (bytes is null || bytes.Length == 0) throw new ValidationException("Dosya seçilmedi.");

        // Boyut + PDF magic-byte TEK yerde (PdfValidation). Sıra önemli: boyut hatası "Yalnız PDF
        // yüklenebilir." mesajının arkasında kalmamalı — kullanıcı 40 MB'lık geçerli bir PDF
        // yüklediğinde "PDF değil" demek yanlış yönlendirir.
        if (PdfValidation.Reddet(bytes) is { } hata) throw new ValidationException(hata);

        // Dost mesaj için ÖNCE say. Yarış durumunda bu sayım atlanabilir — asıl güvence
        // repository'deki yuva ataması + DB unique index/CHECK.
        if (await repository.SayAsync(ct) >= MaxDokuman)
            throw new ValidationException(
                $"En fazla {MaxDokuman} doküman saklanabilir. Yeni belge yüklemek için mevcutlardan birini silin.");

        var dokuman = new FirmaDokuman
        {
            Baslik = baslik,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
            DosyaAdi = PdfValidation.GuvenliDosyaAdi(input.DosyaAdi ?? baslik),
            Bytes = bytes,
            Boyut = bytes.LongLength,
            ContentType = "application/pdf", // sabit: magic-byte geçtiyse PDF'tir, istemcinin dediği değil
            YukleyenKullanici = currentUser.UserName,
        };

        return await repository.EkleAsync(dokuman, MaxDokuman, ct);
    }

    public Task<bool> SilAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.SilAsync(id, ct);
    }
}
