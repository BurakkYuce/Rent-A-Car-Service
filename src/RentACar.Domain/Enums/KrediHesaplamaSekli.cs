namespace RentACar.Domain.Enums;

/// <summary>
/// Filo maliyet hesabında finansman yöntemi (FAZ-74; canlı <c>maliyet_hesaplama.aspx</c>).
///
/// <para><b>Rotatif BİLEREK UYGULANMADI.</b> Rotatif (bakiye-azalan / döner) kredinin faiz ve
/// amortisman formülü — hangi dönemde hangi bakiye üzerinden faiz işlediği, ödeme planının nasıl
/// kurulduğu — Eşit Taksitli'nin klonu DEĞİLDİR; ayrı bir finansal-model kararı gerektirir
/// (<c>docs/roadmap/KARARLAR.md</c> → FAZ-74). Karar gelene kadar bu seçim
/// <see cref="RentACar.Domain.Enums"/> düzeyinde TANIMLI ama hesap katmanında <b>güvenli-red</b>
/// ile karşılanır: sessizce Eşit Taksitli formülüyle hesaplamak, kullanıcıya yanlış bir maliyet
/// rakamını doğruymuş gibi göstermek olurdu.</para>
/// </summary>
public enum KrediHesaplamaSekli
{
    /// <summary>Eşit taksitli — mevcut basit/düz faiz modeli (bkz. MaliyetHesapService).</summary>
    EsitTaksitli = 0,

    /// <summary>Rotatif (bakiye-azalan). Formül kararı beklemede → hesap katmanı REDDEDER.</summary>
    Rotatif = 1
}
