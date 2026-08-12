namespace RentACar.Domain.Enums;

/// <summary>Defter hesap türü (çift-taraflı muhasebe boyutu).</summary>
public enum LedgerAccountType
{
    Cari = 0,   // müşteri/tedarikçi (AccountRef = CariId)
    Kasa = 1,   // nakit kasa
    Banka = 2,  // banka
    Gelir = 3,  // gelir (satış)
    Kdv = 4,    // KDV
    Gider = 5,  // gider (masraf)
    Depozito = 6, // müşteri depozito/emanet yükümlülüğü (AccountRef = CariId) — roadmap I3
    DonemSonucu = 7, // dönem kâr/zararı (kapanış fişi Gelir/Gider'i buraya kapatır; özkaynak benzeri) — PR-A close-lite
    /// <summary>
    /// FAZ-56 — manuel bakiye düzeltmesinin KARŞI hesabı (özkaynak benzeri, <see cref="DonemSonucu"/> gibi).
    ///
    /// <para><b>Neden ayrı tür (KARARLAR.md FAZ-56):</b> düzeltmeler gerçek gelir/giderle
    /// KARIŞMAMALI. <c>Gelir</c>/<c>Gider</c> hesabına yazmak Araç Karnesi, Kârlılık ve Filo Analiz
    /// raporlarını şişirirdi — CLAUDE.md §6'daki atıf düzeltmesi tam bu sınıf bir hatayı zaten bir
    /// kez temizledi. Raporlar P&amp;L'i yalnız <c>Gelir</c>/<c>Gider</c> türünden topladığı için
    /// bu tür oralara YAPISAL olarak giremez.</para>
    ///
    /// <para>Dönem kapanışı da bu türe DOKUNMAZ: kapanış yalnız P&amp;L'i (Gelir/Gider) kapatır.</para>
    /// </summary>
    MuhasebeDuzeltmesi = 8
}
