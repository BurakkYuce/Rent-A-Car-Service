using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// PR-C — müşteriye verilen sözleşme paylaşım linki.
///
/// <para><b>PLATFORM tablosu — RLS YOK, merkezi tenant query filter YOK</b> (bilinçli:
/// <see cref="ITenantOwned"/> uygulamaz). Sebep zorunluluk: link ANONİM açılıyor, yani istekte cookie
/// yok → tenant context yok → GUC set edilmemiş. Tenant-owned + FORCE RLS bir tabloda token aranırsa
/// sorgu <b>sessizce 0 satır</b> döner ve her link ölür. Aynı sebeple <c>Users.CalendarToken</c> de
/// platform tablosunda yaşıyor (<c>CalendarFeedService</c> faz-1).</para>
///
/// <para><b>Bu tabloda kişisel veri YOKTUR</b> — RLS'siz olmasının bedelini ödemek zorunda kalmasın.
/// Müşteri adı/telefonu/TC'si burada değil; PDF'in kendisi <see cref="SozlesmePdf"/> içinde, o da
/// tenant-owned + RLS ardında. <see cref="SozlesmeNo"/> (RZ-000001) yalnız dosya adı üretmek için ve
/// kişiye bağlanamaz.</para>
///
/// <para><b>Erişim kaydı IP TUTMAZ.</b> IP kişisel veridir ve cevaplanması gereken soru
/// ("müşteri açmadı diyor") için sayaç + son erişim zamanı yeterli.</para>
///
/// <para><b>Süre sınırı YOKTUR</b> (kullanıcı kararı). Güvenlik 32 baytlık CSPRNG token +
/// <see cref="Iptal"/> + "yeni sürüm" ile yönetilir. Linkin iletilebilir olması kabul edilen risktir.</para>
///
/// <para><b><see cref="IAuditable"/></b> — "kim ne zaman sözleşme paylaştı / iptal etti" KVKK
/// açısından izlenmesi gereken bir olay. Denetim izi bu tabloda tutuluyor çünkü alanların tamamı
/// küçük skaler; PDF baytı taşıyan <see cref="SozlesmePdf"/> bilinçli olarak IAuditable DEĞİL
/// (interceptor tüm property'leri JSON'a yazar → denetim izi baytlarla şişerdi).
/// <see cref="Token"/> denetim izinde maskelenir (<c>AuditSaveChangesInterceptor</c>): sır, geçmişe
/// ikinci bir kopya olarak düşmesin.</para>
/// </summary>
public class PaylasimLink : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hangi firmanın linki — faz-2'de <c>SystemTenantContext</c> + GUC bununla açılır.</summary>
    public Guid TenantId { get; set; }

    public Guid RentalId { get; set; }

    /// <summary>32-byte CSPRNG → Base64Url. <b>Global unique</b> (tenant kırılımı YOK): token tek
    /// başına tenant'ı çözmek zorunda, çakışma olasılığı da yok sayılabilir.</summary>
    public string Token { get; set; } = "";

    /// <summary>Dosya adı için (`Sozlesme-RZ-000001.pdf`). Kişisel veri değil.</summary>
    public string SozlesmeNo { get; set; } = "";

    /// <summary>İptal → link 404. Kayıt SİLİNMEZ (sayaç/log durur); PDF ise silinir.</summary>
    public bool Iptal { get; set; }

    public int ErisimSayisi { get; set; }
    public DateTimeOffset? SonErisimUtc { get; set; }
    public DateTimeOffset OlusturmaUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Anlık görüntünün üretildiği zaman — sözleşme sonradan değişirse "bayat" uyarısı buradan.</summary>
    public DateTimeOffset AnlikGoruntuUtc { get; set; }
}
