using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kapatma tahsisi (FAZ-29): <b>hangi tahsilat, hangi borç kalemini, ne kadar kapattı.</b>
///
/// <para><b>NEDEN VAR:</b> cari bakiyesi <c>Σ SignedBase</c>'tir; defterde "bu kalem kapandı" diye
/// bir bilgi YOKTUR. Bu kayıt olmadan aynı borç kalemi defalarca kapatılabiliyordu — adversarial
/// inceleme ampirik gösterdi: 100 + 900 = 1000 borçta 100'lük kalem kapatılıp (bakiye 900) aynı
/// kalem yeniden seçilince "tutar bakiyeyi aşmıyor" çiti geçiyor ve ALINMAMIŞ tahsilat yazılıyordu.
/// Bakiye çiti tek başına yetersizdir; asıl çit budur.</para>
///
/// <para><b>PARA YAZMAZ:</b> deftere kayıt POSTLAMAZ. Tahsilatın kendisi <see cref="CashTransaction"/>
/// olarak yazılır; bu tablo yalnız o tahsilatın hangi borç satırlarına DAĞITILDIĞINI belgeler.
/// Cari bakiyesi bu tablodan DEĞİL, defterden hesaplanmaya devam eder — iki gerçek kaynak
/// oluşmaz.</para>
///
/// <para><b>Kısmi kapatma:</b> aynı <see cref="LedgerEntryId"/> için birden çok tahsis olabilir;
/// toplamları o borç satırının baz tutarını AŞAMAZ (servis çiti + testler).</para>
/// </summary>
public class KapatmaTahsis : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kapatılan BORÇ satırı (<see cref="AccountLedgerEntry"/>).</summary>
    public Guid LedgerEntryId { get; set; }

    /// <summary>Kapatmayı yapan tahsilat.</summary>
    public Guid CashTransactionId { get; set; }

    /// <summary>Hangi cariye ait (sorgu kolaylığı; satırdan da türetilebilir).</summary>
    public Guid CariId { get; set; }

    /// <summary>Bu tahsis ile kapatılan tutar — BAZ para. Pozitif.</summary>
    public decimal KapatilanBaz { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
