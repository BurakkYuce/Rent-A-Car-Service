namespace RentACar.Application.Common;

/// <summary>
/// <c>/api/ui</c> liste sözleşmesinin YANIT yarısı (F1.3): o sayfanın kayıtları + filtreye uyan
/// toplam kayıt sayısı + istenen sayfa numarası/boyutu (normalize edilmiş hâliyle).
///
/// <para><b><see cref="PagedResult{T}"/> ile ilişkisi:</b> <c>PagedResult</c> Blazor sayfalarının
/// ve <c>RentACar.Api</c>'nin (<c>PagedResponse</c>) mevcut sözleşmesidir, değişmez. <c>Sayfa&lt;T&gt;</c>
/// yeni <c>/api/ui/v1</c> sözleşmesidir (Türkçe JSON alanları: <c>kayitlar/toplam/sayfaNo/boyut</c>).
/// İkisi aynı dört bilgiyi taşır; mevcut bir <c>PagedResult</c> döndüren repository'yi yeniden
/// yazmadan yeni uca bağlamak için <see cref="SayfaUzantilari.SayfayaCevir{T}(PagedResult{T})"/> köprüsü var.
/// Blazor F13'te silinince <c>PagedResult</c> da emekli edilebilir.</para>
/// </summary>
public sealed record Sayfa<T>(IReadOnlyList<T> Kayitlar, int Toplam, int SayfaNo, int Boyut)
{
    /// <summary>Toplam sayfa sayısı; kayıt yoksa 0. <c>long</c> ara hesap: <c>Toplam + Boyut</c> taşmasın.</summary>
    public int ToplamSayfa => Boyut <= 0 ? 0 : (int)((Toplam + (long)Boyut - 1) / Boyut);

    /// <summary>Kayıtları dönüştürür (entity → DTO); sayfa bilgisi aynen korunur.</summary>
    public Sayfa<TSonuc> Donustur<TSonuc>(Func<T, TSonuc> donustur)
        => new(Kayitlar.Select(donustur).ToList(), Toplam, SayfaNo, Boyut);
}

/// <summary><see cref="Sayfa{T}"/> yardımcıları.</summary>
public static class SayfaUzantilari
{
    /// <summary>Mevcut <see cref="PagedResult{T}"/> → yeni sözleşme köprüsü (alan alan birebir).</summary>
    public static Sayfa<T> SayfayaCevir<T>(this PagedResult<T> sonuc)
        => new(sonuc.Items, sonuc.Total, sonuc.Page, sonuc.PageSize);
}
