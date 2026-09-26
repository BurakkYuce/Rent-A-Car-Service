namespace RentACar.Domain.Enums;

/// <summary>
/// Servis kaydı durum akışı: (Rezerve →) Açık → Serviste → Tamamlandi (veya Iptal).
/// </summary>
public enum ServiceStatus
{
    Acik = 0,
    Serviste = 1,
    Tamamlandi = 2,
    Iptal = 3,

    /// <summary>
    /// FAZ-16 (servis_rezervasyon.aspx): PLANLANMIŞ randevu — kayıt açıldı ama araç henüz servise
    /// girmedi. Değeri 4 seçildi; mevcut satırların int karşılıkları DEĞİŞMEZ (0-3 aynı kalır).
    /// <para><b>Rapor kuralı:</b> gerçekleşmemiş bir servis aracı "bakımda" göstermemeli —
    /// bakım-günü / son-servis sorgularında <see cref="Iptal"/> ile BİRLİKTE dışlanır. Araç
    /// müsaitliği zaten <c>Acik||Serviste</c> sorularına bakar, Rezerve oraya girmez.</para>
    /// </summary>
    Rezerve = 4
}
