namespace RentACar.Domain.Common;

/// <summary>
/// Belge türü — değeri AYNI ZAMANDA numaradaki 2 haneli tip kodudur.
///
/// <para><b>Kodlar KALICIDIR.</b> Bir kod kesilmiş bir belgenin numarasına yazıldığı andan itibaren
/// değiştirilemez: numara mali kayıtta, PDF'te, paylaşım linkinde ve müşteriye giden mesajda geçer.
/// Değerler bu yüzden AÇIK yazılır (yeniden sıralamaya bağışık) ve <c>BelgeNoTests</c> ile
/// sözleşmeye bağlanır.</para>
///
/// <para>Neden enum (sabit sınıf değil): değerin kendisi tip kodu olduğu için ikinci bir eşleme
/// tablosu gerekmez, derleyici tip güvenliği ve <c>switch</c> tamlığı verir.</para>
///
/// <para><b>11 ve 12 ayrı:</b> eski düzende <c>DamageFile</c> ve <c>Baf</c> AYRI sayaçlar kullanıp
/// İKİSİ de <c>BAF-</c> ön ekini üretiyordu — aynı görünen iki numara. Ayrı tip kodu bu çakışmayı
/// yapısal olarak bitirir.</para>
///
/// <para><b>18-20 REZERVE:</b> yeni tür gerekirse önce bu boşluklar doldurulur, 21+'ye geçilmez.
/// Adaylar: iade faturası, depozito iratı, dönem kapanış belgesi.</para>
/// </summary>
// Ad neden "BelgeTuru" DEĞİL: Domain.Enums.BelgeTuru zaten var (PDF şablon türü:
// sözleşme/fatura/makbuz) ve ikisi farklı kavramlar — biri numara tipi, diğeri şablon tipi.
public enum DocumentNoType
{
    KiraSozlesmesi = 1,
    Rezervasyon = 2,
    Teklif = 3,
    /// <summary>Fatura numarası GİB formatındadır (<see cref="DocumentNo.FormatInvoice"/>) — bu kod
    /// yalnızca tür kimliği olarak durur, 13 haneli desende KULLANILMAZ.</summary>
    Fatura = 4,
    Tahsilat = 5,
    Tediye = 6,
    Gider = 7,
    Ceza = 8,
    ServisKaydi = 9,
    DisHizmet = 10,
    HasarDosyasi = 11,
    Baf = 12,
    AracSiparis = 13,
    AracSatis = 14,
    AracKredi = 15,
    FiloKiralama = 16,
    MaliyetTeklifi = 17,
}
