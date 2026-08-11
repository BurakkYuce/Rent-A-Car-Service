using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;

namespace RentACar.Application.GelenEFaturalar;

/// <summary>Tek bir KDV oran kademesi: oran (0..1) + matrah + KDV tutarı.</summary>
public readonly record struct KdvKirilimSatiri(decimal Oran, decimal Matrah, decimal Kdv)
{
    public decimal Brut => Matrah + Kdv;
}

/// <summary>
/// Gelen e-Fatura KDV oran kırılımı — SAF (I/O yok, DB yok) hesap/doğrulama. Test edilebilirlik ve
/// "beklenen değer koddan türetilmesin" ilkesi için servisten ayrı tutulur.
///
/// <para><b>KURUŞ SÖZLEŞMESİ (para disiplini):</b> giderleştirme her oran kademesini AYRI bir gider
/// satırı yapar ve her satırın KDV'si <c>KdvMath.FromNet(matrah, oran)</c> ile SATIR BAZINDA
/// yuvarlanır. Bu yüzden kırılım, belge toplamlarıyla KURUŞ-BİREBİR tutmak ZORUNDADIR:
/// <c>Σ matrah == NetTutar</c> ve <c>Σ round(matrah×oran,2) == KdvTutar</c>. Aksi halde defter,
/// belgenin bir kuruş fazlasını/eksiğini taşırdı → <see cref="ValidationException"/> ile red.
/// Tolerans YOKTUR: uydurulmuş kuruş, sessizce yanlış beyan demektir.</para>
///
/// <para><b>Neden orandan yeniden hesap:</b> kırılımın KDV kolonu kullanıcı girdisidir; onu deftere
/// olduğu gibi taşımak "matrah 1000 / KDV 999" gibi bir belgeyi de postlardı. Orandan hesaplayıp
/// belgeyle karşılaştırmak, belgenin kendi içinde tutarlı olmasını ZORUNLU kılar.</para>
/// </summary>
public static class GelenEFaturaKdvKirilim
{
    /// <summary>Desteklenen KDV oranları (Türkiye: %20 genel, %10 indirimli, %1, %0 istisna).</summary>
    public static readonly decimal[] Oranlar = [0.20m, 0.10m, 0.01m, 0m];

    private static decimal K(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// ADVERSARIAL (kuruş-altı sızıntısı): kolonlar <c>numeric(19,4)</c> olduğu için 1000,0050 gibi
    /// bir tutar SAKLANABİLİR. Kırılım/gider yolu her şeyi 2 ondalığa yuvarladığından defter
    /// 1000,01 taşır, belge 1000,0050 gösterirdi — belge ile defter kuruş-altı kopar. e-Fatura
    /// tutarları kuruş hassasiyetindedir; 2 ondalıktan fazlasını GİRİŞTE reddet.
    /// </summary>
    private static void KurusOl(decimal v, string alan)
    {
        if (K(v) != v)
            throw new ValidationException($"{alan} kuruş hassasiyetinde olmalıdır (en çok 2 ondalık): {v}.");
    }

    /// <summary>Kayıtta AÇIKÇA girilmiş (matrah veya KDV kolonu null olmayan) kademeler. Değerler
    /// HAM döner (yuvarlanmaz) — kuruş hassasiyeti <see cref="Dogrula"/>'da SINANIR; burada sessizce
    /// yuvarlamak, belgeyle defterin kuruş-altı kopmasını gizlerdi.
    /// Sıra: %20, %10, %1, %0 — giderleştirmede gider satır sırası da budur (deterministik).</summary>
    public static IReadOnlyList<KdvKirilimSatiri> AcikSatirlar(GelenEFatura f)
    {
        var list = new List<KdvKirilimSatiri>(4);
        void Ekle(decimal oran, decimal? matrah, decimal? kdv)
        {
            if (matrah is null && kdv is null) return;
            list.Add(new KdvKirilimSatiri(oran, matrah ?? 0m, kdv ?? 0m));
        }
        Ekle(0.20m, f.Kdv20Matrah, f.Kdv20);
        Ekle(0.10m, f.Kdv10Matrah, f.Kdv10);
        Ekle(0.01m, f.Kdv1Matrah, f.Kdv1);
        Ekle(0m, f.Kdv0Matrah, null); // %0 kademesinin KDV'si tanımı gereği 0
        return list;
    }

    /// <summary>Kırılım GİRİLMİŞ Mİ (en az bir kademe kolonu dolu)?</summary>
    public static bool KirilimVar(GelenEFatura f) => AcikSatirlar(f).Count > 0;

    /// <summary>
    /// Belgenin kendi içinde tutarlılığı: <c>Net + KDV == GenelToplam</c>. Kırılımdan BAĞIMSIZ,
    /// her gelen faturada geçerli olmalı — tutmuyorsa hiçbir kırılım da tutturamaz.
    /// </summary>
    public static void ToplamlariDogrula(decimal net, decimal kdv, decimal genel)
    {
        if (net < 0m || kdv < 0m || genel < 0m) throw new ValidationException("Tutarlar negatif olamaz.");
        KurusOl(net, "Net tutar"); KurusOl(kdv, "KDV tutarı"); KurusOl(genel, "Genel toplam");
        if (K(net) + K(kdv) != K(genel))
            throw new ValidationException(
                $"Belge toplamı tutarsız: net {K(net):N2} + KDV {K(kdv):N2} = {K(net) + K(kdv):N2} ≠ genel toplam {K(genel):N2}.");
    }

    /// <summary>
    /// Kırılımı doğrular. Kırılım YOKSA sessizce geçer (opsiyonel alan). Varsa:
    /// (1) her kademe negatif olamaz, (2) her kademenin KDV'si <c>round(matrah×oran,2)</c> olmalı,
    /// (3) <c>Σ matrah == NetTutar</c>, (4) <c>Σ KDV == KdvTutar</c>.
    /// </summary>
    public static void Dogrula(GelenEFatura f)
    {
        var satirlar = AcikSatirlar(f);
        if (satirlar.Count == 0) return;

        foreach (var s in satirlar)
        {
            if (s.Matrah < 0m || s.Kdv < 0m)
                throw new ValidationException($"%{s.Oran * 100:0.##} kademesinde negatif tutar olamaz.");
            KurusOl(s.Matrah, $"%{s.Oran * 100:0.##} matrahı");
            KurusOl(s.Kdv, $"%{s.Oran * 100:0.##} KDV'si");
            var beklenen = KdvMath.FromNet(s.Matrah, s.Oran).Kdv;
            if (s.Kdv != beklenen)
                throw new ValidationException(
                    $"%{s.Oran * 100:0.##} kademesi tutarsız: matrah {s.Matrah:N2} için KDV {beklenen:N2} olmalı, {s.Kdv:N2} girilmiş.");
        }

        var matrahTop = satirlar.Sum(s => s.Matrah);
        var kdvTop = satirlar.Sum(s => s.Kdv);
        if (matrahTop != K(f.NetTutar))
            throw new ValidationException(
                $"KDV kırılımı matrah toplamı ({matrahTop:N2}) belge net tutarına ({K(f.NetTutar):N2}) eşit değil.");
        if (kdvTop != K(f.KdvTutar))
            throw new ValidationException(
                $"KDV kırılımı KDV toplamı ({kdvTop:N2}) belge KDV tutarına ({K(f.KdvTutar):N2}) eşit değil.");
    }

    /// <summary>
    /// Giderleştirilecek satırları çözer. Açık kırılım varsa onu (doğrulanmış olarak) döndürür;
    /// yoksa belge toplamlarından TEK oran çözmeye çalışır (<c>round(net×oran,2) == KdvTutar</c>
    /// olan standart oran). Çözülemezse gürültülü red — TAHMİN ETMEZ.
    /// Sıfır tutarlı (matrah 0 ve KDV 0) kademeler elenir; gider satırı üretmezler.
    /// </summary>
    public static IReadOnlyList<KdvKirilimSatiri> Coz(GelenEFatura f)
    {
        ToplamlariDogrula(f.NetTutar, f.KdvTutar, f.GenelToplam);

        IReadOnlyList<KdvKirilimSatiri> satirlar;
        if (KirilimVar(f))
        {
            Dogrula(f);
            satirlar = AcikSatirlar(f);
        }
        else
        {
            var net = K(f.NetTutar);
            var kdv = K(f.KdvTutar);
            var oran = Oranlar.FirstOrDefault(o => KdvMath.FromNet(net, o).Kdv == kdv, -1m);
            if (oran < 0m)
                throw new ValidationException(
                    $"KDV oran kırılımı girilmemiş ve belge toplamından tek oran çözülemedi " +
                    $"(net {net:N2}, KDV {kdv:N2}). Önce %20/%10/%1/%0 kırılımını girin.");
            satirlar = [new KdvKirilimSatiri(oran, net, kdv)];
        }

        var dolu = satirlar.Where(s => s.Matrah != 0m || s.Kdv != 0m).ToList();
        if (dolu.Count == 0)
            throw new ValidationException("Sıfır tutarlı fatura giderleştirilemez.");
        return dolu;
    }
}
