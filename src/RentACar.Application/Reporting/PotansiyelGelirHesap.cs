using RentACar.Application.RateMatrices;
using RentACar.Domain.Entities;

namespace RentACar.Application.Reporting;

/// <summary>
/// FAZ-79 — "Potansiyel gelir" (canlı <c>gelir_tablosu.aspx</c>) SAF hesabı.
///
/// <para><b>KARARLAR.md / FAZ-79 kullanıcı kararı:</b> fiyat kaynağı <b>tarife matrisi</b>
/// (<see cref="RateMatrix"/>) — eski <c>RateCard</c> KULLANILMAZ, çünkü yeni tarifeler oraya
/// girilmediği için potansiyel rakam zamanla gerçeklikten koparaydı.</para>
///
/// <para><b>Tanım:</b> potansiyel gelir = <i>dönem içi sahiplik günü</i> × <i>onaylı tarife matrisinin
/// günlük liste fiyatı</i>. Yani "araç dönem boyunca kesintisiz kiralansaydı liste fiyatından ne
/// kazanırdı". Gerçekleşen gelirle mutabık olması BEKLENMEZ; bu bir kapasite referansıdır ve
/// <b>deftere hiçbir şey yazmaz, P&amp;L toplamına katılmaz</b>.</para>
///
/// <para><b>Satır seçimi PAYLAŞILIR:</b> aday eleme/deterministik seçim <see cref="RateMatrisCozumleme"/>
/// ile yapılır (Onaylı + Aktif + kapsam + tarih). İkinci bir tarife-seçim kuralı yazılmaz — iki
/// çözümleyici zamanla ayrışır ve rapor, motorun kullanmadığı bir fiyatı "potansiyel" diye gösterirdi.</para>
///
/// <para><b>Bilinçli daraltmalar:</b> (a) <c>Kanal = null</c> sorulur → yalnız kanal-agnostik BAZ tarife
/// aday olur (acenteye özel indirimli satır potansiyeli çarpıtmasın); (b) <c>ParaBirimi = null</c>
/// sorulur → yalnız varsayılan-döviz satırları aday (yabancı döviz tarifesi kur çevrimi olmadan TL
/// P&amp;L'in yanına yazılamaz); (c) kademe olarak <b>uzun dönem</b> alınır (motorun
/// <c>ResolveTierRate</c> önceliğiyle aynı: 30+ gün → <c>GunAylik ?? GunHaftalik</c>, yoksa Gün-7) —
/// "kesintisiz kiralık" varsayımının dürüst fiyatı budur, tek-günlük liste fiyatı potansiyeli şişirirdi.</para>
/// </summary>
public static class PotansiyelGelirHesap
{
    /// <summary>Aracın grubu/şubesi için geçerli günlük liste fiyatı. Eşleşen onaylı tarife yoksa null
    /// (rapor "Hesaplanmadı" gösterir; 0 DÖNMEZ — 0 "potansiyel yok" gibi okunurdu).</summary>
    public static decimal? GunlukTarife(
        IReadOnlyList<RateMatrix> satirlar, string? grupKod, string? sube, DateTimeOffset tarih)
    {
        if (satirlar.Count == 0 || string.IsNullOrWhiteSpace(grupKod)) return null;

        // Gün-7 kademesiyle sorulur: uzun-dönem kademesi tanımsız tarifelerde motorun düştüğü kademe budur.
        var sonuc = RateMatrisCozumleme.Coz(satirlar,
            new RateMatrisSorgu(Kanal: null, Sube: sube, AracGrupKod: grupKod, Tarih: tarih, GunSayisi: 7));
        if (sonuc is null) return null;

        var kazanan = satirlar.FirstOrDefault(r => r.Id == sonuc.Id);
        // Uzun-dönem kademesi tanımlıysa o kazanır (motor 30+ günde AYNI önceliği uygular).
        var gunluk = (kazanan?.GunAylik ?? kazanan?.GunHaftalik) ?? sonuc.GunlukFiyat;
        return gunluk > 0m ? gunluk : null;
    }

    /// <summary>Potansiyel gelir = günlük tarife × dönem-içi sahiplik günü. Tarife yoksa ya da gün 0 ise
    /// null (uydurma 0 yazılmaz).</summary>
    public static decimal? Hesapla(decimal? gunlukTarife, int donemSahiplikGun)
        => gunlukTarife is > 0m && donemSahiplikGun > 0
            ? decimal.Round(gunlukTarife.Value * donemSahiplikGun, 2, MidpointRounding.AwayFromZero)
            : null;
}
