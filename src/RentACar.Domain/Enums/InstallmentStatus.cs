namespace RentACar.Domain.Enums;

/// <summary>
/// Müşteri taksitinin SAKLANAN durumu (FAZ-66).
///
/// <para><b>"Gecikti" BİLİNÇLİ OLARAK YOK.</b> Gecikme vade + bugünün fonksiyonudur; kolona
/// yazılsaydı gece yarısı bayatlar ve kimse güncellemediği için rapor yanlış gösterirdi
/// (bir job'a bağımlı "doğru" veri). <see cref="Entities.MusteriTaksit.Gecikti"/> okuma anında
/// TÜRETİLİR.</para>
/// </summary>
public enum InstallmentStatus
{
    Bekliyor = 0,
    Odendi = 1
}
