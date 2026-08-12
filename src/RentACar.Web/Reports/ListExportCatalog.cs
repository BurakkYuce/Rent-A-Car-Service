using RentACar.Application.Bookings;
using RentACar.Application.Legal;
using RentACar.Application.Regulation;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;

namespace RentACar.Web.Reports;

/// <summary>
/// Liste export sütun tanımları — saf, test-edilebilir projeksiyonlar. Her export'un başlıkları + hücre eşlemesi
/// TEK yerde: "sütun değiştir/ekle" burada yapılır. Tam TürevRent sütun setleri docs/parite/09-export-karsilastirma.md'de
/// (genişletme menüsü — şimdi anlamlı alt-küme). KVKK: cari TC/ehliyet/pasaport gibi hassas PII bu
/// ViewReports-gate'li export'a DAHİL EDİLMEZ (kaynakta zaten *Enc kolonlarında şifreli; düz kolonlar prod'da
/// null). Yalnız kurumsal Vergi No dahil. Hassas PII yalnız Personel export'unda (ManageUsers-gate + decrypt) çıkar.
/// </summary>
public static class ListExportCatalog
{
    public static ExportTable Araclar(IReadOnlyList<Vehicle> v) => new(
        "Araclar",
        // İlk 15 kolon geriye-uyum için SABİT sırada; kalanlar (parite derinliği) sona eklendi.
        ["Plaka", "Marka", "Tip", "Detay Tipi", "Grup", "Şube", "Model Yılı", "Renk", "Yakıt", "Vites", "SIPP", "KM", "Durum", "Özel Kod", "Kasa Tipi",
         "Segment", "Filo Durumu", "Şasi No", "Motor No", "Motor Gücü", "Silindir Hacmi", "Ruhsat No", "Tescil Tarihi", "Araç Sahibi",
         "Alım Bedeli", "Alım Tarihi", "Alış Vergisiz", "Alış ÖTV", "Alış KDV", "Aylık Maliyet", "Filo Yön. Maliyeti", "2.El Değer",
         "Filo Giriş", "Filo Çıkış", "HGS No", "OGS No", "Kira KM Limiti", "Son Bakım Tarih", "Son Bakım KM", "Lastik Durumu",
         "Özel Kod 2", "Özel Kod 3", "Özel Kod 4", "Özel Kod 5", "Alım Fatura No", "Alım Firma",
         "Web Rez Kapalı", "Ofis Rez Kapalı", "Z İzni", "UTTS", "Kar Lastiği", "Yedek Anahtar", "Temizlik", "Rehin"],
        v.Select(x => new object?[]
        {
            x.Plaka, x.Marka, x.Tip, x.DetayTipi, x.Grup, x.Sube, x.ModelYili, x.Renk,
            x.Yakit.ToString(), x.Vites?.ToString(), x.Sipp, x.Km, x.Durum.ToString(), x.OzelKod1, x.KasaTipi,
            x.Segment, x.FiloDurum?.ToString(), x.SasiNo, x.MotorNo, x.MotorGucu, x.SilindirHacmi, x.RuhsatNo, D(x.TescilTarihi), x.AracSahibi,
            x.AlimBedeli, D(x.AlimTarihi), x.AlisVergisiz, x.AlisOtv, x.AlisKdv, x.AylikMaliyet, x.FiloYonetimMaliyeti, x.IkinciElDeger,
            D(x.FiloGirisTarih), D(x.FiloCikisTarih), x.HgsNo, x.OgsNo, x.KiraKmLimiti, D(x.SonBakimTarih), x.SonBakimKm, x.LastikDurumu,
            x.OzelKod2, x.OzelKod3, x.OzelKod4, x.OzelKod5, x.AlimFaturaNo, x.AlimYapilanFirma,
            E(x.WebRezKapat), E(x.OfisRezKapat), E(x.ZIzni), E(x.Utts), E(x.KarLastigi), E(x.YedekAnahtar), E(x.Temizlik), E(x.Rehin)
        }).ToList());

    public static ExportTable Cariler(IReadOnlyList<Customer> c) => new(
        "Cariler",
        // KVKK: TC Kimlik BİLİNÇLİ olarak yok (bkz. sınıf özeti). Kurumsal Vergi No dahil.
        ["Ünvan/Ad", "Tip", "Vergi No", "Telefon", "E-posta", "İl", "İlçe", "Kaynak", "Vade Gün",
         "Vergi Dairesi", "GSM2", "Adres", "Sınıf", "Müşteri Temsilcisi", "İYS İzinli", "Fatura Dönemi", "Risk Limiti", "HGS Yansıtma", "Özel Cari Tip"],
        c.Select(x => new object?[]
        {
            x.DisplayName, x.Tip.ToString(), x.VergiNo, x.CepTel, x.Email, x.Il, x.Ilce, x.Kaynak, x.VadeGun,
            x.VergiDairesi, x.Gsm2, x.Adres, x.Sinif, x.MusteriTemsilcisi, E(x.IysIzinli), x.FaturaDonemi, x.RiskLimiti, x.HgsYansitmaTuru, x.OzelCariTip
        }).ToList());

    public static ExportTable Faturalar(IReadOnlyList<Invoice> f, Func<Guid, string?> cariAd) => new(
        "Faturalar",
        // İlk 6 kolon geriye-uyum için SABİT; kalanlar (parite derinliği) sona eklendi.
        ["No", "Tarih", "Net", "KDV", "Toplam", "Durum",
         "Cari", "Vade", "Para", "Kur", "Tür", "Damga Vergisi", "e-Fatura"],
        f.Select(x => new object?[]
        {
            x.No, ExportTarih.Gun(x.Tarih), x.NetTutar, x.KdvTutar, x.GenelToplam, x.Durum.ToString(),
            cariAd(x.CariId), D(x.VadeTarihi), x.Currency, x.Kur, FaturaTuru(x), x.DamgaVergisi,
            x.EFaturaGonderildi ? (x.EFaturaEttn ?? "Gönderildi") : ""
        }).ToList());

    /// <summary>Fatura türü etiketi (iade/manuel/kira/fark/serbest).</summary>
    private static string FaturaTuru(Invoice x) =>
        x.IadeMi ? "İade" : x.ManuelMi ? "Manuel"
        : x.KaynakKiraId != null ? "Kira Fark" : x.RentalId != null ? "Kira" : "Serbest";

    /// <summary>Cari ekstre (hesap ekstresi) — bir carinin defter satırları + yürüyen bakiye (base para).
    /// Cari bakiye = Σ (Borç +, Alacak −); pozitif = müşteri borçlu.
    ///
    /// <para>FAZ-65: <paramref name="devir"/> filtrenin kapsam dışında bıraktığı ÖNCEKİ hareketlerin
    /// net toplamıdır. Sıfırdan farklıysa ilk satır olarak yazılır ve yürüyen bakiye ondan başlar —
    /// aksi hâlde tarih-filtreli bir export'ta bakiye kolonu yanlış olurdu.</para></summary>
    public static ExportTable CariEkstre(IReadOnlyList<AccountLedgerEntry> lines, decimal devir = 0m)
    {
        var rows = new List<object?[]>();
        decimal bakiye = devir;
        if (devir != 0m)
            rows.Add(new object?[] { "", "Devir", "Önceki dönemden devir", devir > 0 ? devir : 0m, devir < 0 ? -devir : 0m, devir });
        foreach (var e in lines.OrderBy(x => x.EntryDateUtc).ThenBy(x => x.SourceType))
        {
            var borc = e.Direction == LedgerDirection.Debit ? e.Amount.AmountInBase : 0m;
            var alacak = e.Direction == LedgerDirection.Credit ? e.Amount.AmountInBase : 0m;
            bakiye += borc - alacak;
            rows.Add(new object?[] { ExportTarih.Gun(e.EntryDateUtc), e.SourceType, e.Description, borc, alacak, bakiye });
        }
        return new ExportTable("Cari Ekstre", ["Tarih", "Kaynak", "Açıklama", "Borç", "Alacak", "Bakiye"], rows);
    }

    /// <summary>FAZ-52 — fatura DETAY (satır) listesi. Tutarlar faturanın kesildiği andaki
    /// değerlerdir; burada yeniden hesaplanmaz. İptal satırları da yazılır (Durum kolonuyla ayrışır)
    /// — ekranda görünen ile indirilen aynı küme olmalı.</summary>
    public static ExportTable FaturaDetaylari(IReadOnlyList<RentACar.Application.Finance.FaturaSatirDto> rows) => new(
        "Fatura Detay",
        ["Fatura No", "Tarih", "Vade", "Durum", "Cari", "Şehir", "Mail", "Vergi No",
         "Açıklama", "Miktar", "Birim Net", "KDV Oranı", "Satır Net", "Satır KDV", "Satır Toplam",
         "Döviz", "Kur", "Plaka", "Sözleşme No", "Çıkış Ofisi", "Rez. Kaynağı"],
        rows.Select(r => new object?[]
        {
            r.FaturaNo, ExportTarih.Gun(r.Tarih), ExportTarih.Gun(r.VadeTarihi),
            r.Iptal ? "İptal" : r.IadeMi ? "İade" : "Geçerli",
            r.CariAd, r.CariSehir, r.CariEmail, r.CariVergiNo,
            r.Aciklama, r.Miktar, r.BirimNetFiyat, r.KdvOrani, r.SatirNet, r.SatirKdv, r.SatirToplam,
            r.Doviz, r.Kur, r.Plaka, r.SozlesmeNo, r.CikisOfisi, r.RezervasyonKaynagi
        }).ToList());

    public static ExportTable Cezalar(IReadOnlyList<Penalty> c) => new(
        "Cezalar",
        // FAZ-60: kısmi ödeme + bilgi alanları eklendi (mevcut 7 kolonun SIRASI korundu).
        ["No", "Ceza Türü", "Tebliğ Tarihi", "Vade", "Tutar", "Durum", "Sebep",
         "Makbuz No", "Ödenen", "Kalan", "Ödeme Tarihi", "Ceza Saati", "Ceza Yeri", "İşlem Şube"],
        c.Select(x => new object?[]
        {
            // Tarihler YEREL GÜN (ham UTC değil) — kullanıcı ekranda gördüğü günü indirsin.
            x.No, x.CezaTuru, ExportTarih.Gun(x.TebligTarihi),
            ExportTarih.Gun(x.VadeTarihi),
            x.Tutar, x.Durum.ToString(), x.Sebep,
            x.MakbuzNo, x.OdenenTutar, x.Kalan,
            ExportTarih.Gun(x.OdenmeTarihi),
            x.Saat, x.Yer, x.IslemSube
        }).ToList());

    public static ExportTable Giderler(IReadOnlyList<Expense> g) => new(
        "Giderler",
        ["No", "Tip", "Tarih", "Şube", "Evrak No", "Net", "KDV Oranı", "KDV", "Genel Toplam", "Döviz", "Ödeme", "Hesap", "Açıklama"],
        g.Select(x => new object?[]
        {
            x.No, x.Tip.ToString(), ExportTarih.Gun(x.Tarih), x.Sube, x.EvrakNo, x.NetTutar, x.KdvOrani,
            x.KdvTutar, x.GenelToplam, x.Currency, x.OdemeYontemi.ToString(), x.KasaBankaHesap.ToString(), x.Aciklama
        }).ToList());

    /// <summary>
    /// FAZ-67 — Cari / Cari Kod / Kanal kolonları eklendi (ekrandaki boşluk export'ta da vardı).
    /// Tarih YEREL GÜN olarak yazılır: ham UTC yazmak, ekranda 01.03 görünen kaydı export'ta
    /// 28.02 yapıyordu (repoda bilinen bir-gün-geri tuzağı).
    /// </summary>
    public static ExportTable NakitIslemler(IReadOnlyList<NakitIslemSatirDto> n) => new(
        "Nakit İşlemler",
        ["No", "Tip", "Tarih", "Cari", "Cari Kod", "Kanal", "Tutar", "Döviz", "Karşı Hesap", "Ters mi", "Açıklama"],
        n.Select(r => new object?[]
        {
            r.Islem.No, r.Islem.Tip.ToString(), ExportTarih.Gun(r.Islem.Tarih),
            r.CariAd, r.CariKod, r.Islem.Kanal,
            r.Islem.Amount.Amount, r.Islem.Amount.Currency,
            r.Islem.KarsiHesap.ToString(), r.Islem.TersKayitMi ? "Evet" : "Hayır", r.Islem.Aciklama
        }).ToList());

    public static ExportTable AracSatislari(IReadOnlyList<VehicleSale> s) => new(
        "Araç Satışları",
        ["No", "Tarih", "Noter No", "Net", "KDV Oranı", "KDV", "Genel Toplam", "Döviz", "Durum", "Açıklama"],
        s.Select(x => new object?[]
        {
            x.No, ExportTarih.Gun(x.Tarih), x.NoterNo, x.SatisNet, x.KdvOrani, x.KdvTutar,
            x.GenelToplam, x.Currency, x.Durum.ToString(), x.Aciklama
        }).ToList());

    /// <summary>
    /// FAZ-17: Dosya No / Cari / İmza Tarihi / temsilci / spesifikasyon / TSB-Kredi ve üç fiyat
    /// katmanı kolonları eklendi — ekrandaki tabloyla aynı bilgi dışarı çıksın (cari ve kredi Id
    /// olarak tutulur, export'a ADLARI yazılır).
    /// <para>Kolon başlıkları katmanların BİLGİ olduğunu söyler: resmi tutar "Birim Fiyat"tır,
    /// Piyasa/Ops/Filo hiçbir toplama girmez.</para>
    /// </summary>
    public static ExportTable AracSiparisleri(IReadOnlyList<AracSiparis> s,
        Func<Guid, string?>? cari = null, Func<Guid, string?>? kredi = null) => new(
        "Araç Siparişleri",
        ["No", "Dosya No", "Tedarikçi", "Cari", "Sipariş Tarihi", "İmza Tarihi", "Beklenen Teslim",
         "Satış Temsilcisi", "Özel Temsilci", "Marka", "Tip", "Grup", "Versiyon", "Opsiyon",
         "Renk", "İç Renk", "Kaynak Tipi", "Satış Tipi", "Adet", "Birim Fiyat (resmi)",
         "Piyasa Fiyat (bilgi)", "Ops Fiyat (bilgi)", "Filo Fiyat (bilgi)", "Döviz",
         "TSB Kayıt No", "Kredi", "Durum", "Açıklama"],
        s.Select(x => new object?[]
        {
            x.No, x.DosyaNo, x.Tedarikci,
            x.TedarikciCariId is Guid c ? cari?.Invoke(c) : null,
            ExportTarih.Gun(x.SiparisTarihi), ExportTarih.Gun(x.ImzaTarih),
            ExportTarih.Gun(x.BeklenenTeslim),
            x.SatisTemsilci, x.OzelTemsilci,
            x.Marka, x.Tip, x.Grup, x.Versiyon, x.Opsiyon, x.Renk, x.IcRenk, x.KaynakTip, x.SatisTipi,
            x.Adet, x.BirimFiyat, x.PiyasaFiyat, x.OpsFiyat, x.FiloFiyat, x.Currency,
            x.TsbKayitNo,
            x.KrediId is Guid k ? kredi?.Invoke(k) : null,
            x.Durum.ToString(), x.Aciklama
        }).ToList());

    /// <summary>FAZ-13: Dosya No / Cari / Araç kolonları eklendi — ekrandaki tabloyla aynı bilgi
    /// dışarı çıksın (cari ve plaka Id olarak tutulur, export'a ADLARI yazılır).</summary>
    public static ExportTable AracKredileri(IReadOnlyList<AracKredi> k,
        Func<Guid, string?>? cari = null, Func<Guid, string?>? plaka = null) => new(
        "Araç Kredileri",
        ["No", "Dosya No", "Banka", "Cari", "Araç", "Kredi Tutarı", "Faiz %", "Taksit", "Ödenen Taksit", "Başlangıç", "Döviz", "Durum", "Açıklama"],
        k.Select(x => new object?[]
        {
            x.No, x.DosyaNo, x.BankaAdi,
            x.CariId is Guid c ? cari?.Invoke(c) : null,
            x.VehicleId is Guid v ? plaka?.Invoke(v) : null,
            x.KrediTutari, x.FaizOran, x.TaksitSayisi, x.OdenenTaksit,
            ExportTarih.Gun(x.BaslangicTarihi), x.Currency, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable Baflar(IReadOnlyList<Baf> b) => new(
        "BAF (Personel Araç Tahsis)",
        ["No", "Çıkış Tarihi", "Çıkış KM", "Çıkış Yakıt", "Dönüş Tarihi", "Dönüş KM", "Dönüş Yakıt", "Şube", "Durum", "Açıklama"],
        b.Select(x => new object?[]
        {
            x.No, ExportTarih.Gun(x.CikisTarihi), x.CikisKm, x.CikisYakit, ExportTarih.Gun(x.DonusTarihi),
            x.DonusKm, x.DonusYakit, x.Sube, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable Kiralar(IReadOnlyList<RentalRow> r) => new(
        "Kiralar",
        ["Sözleşme No", "Müşteri", "Plaka", "Başlangıç", "Bitiş", "Gün", "Tutar", "Bakiye", "Durum", "Faturalı"],
        r.Select(x => new object?[]
        {
            x.SozlesmeNo, x.MusteriAd, x.Plaka, ExportTarih.Gun(x.BasTar), ExportTarih.Gun(x.BitTar),
            x.Gun, x.Tutar, x.Bakiye, x.Durum.ToString(), x.Faturali ? "Evet" : "Hayır"
        }).ToList());

    /// <summary>
    /// Rezervasyon listesi. FAZ-48: müşteri/plaka/kaynak + talep bilgisi kolonları eklendi (ekrandaki
    /// tabloyla aynı bilgi). TARİHLER YEREL GÜN — ham UTC basmak, gece yarısına yakın kayıtlarda
    /// listede görünen günün BİR GÜN GERİSİNİ yazıyordu (repoda bilinen tuzak; yenisi üretilmez).
    /// </summary>
    public static ExportTable Rezervasyonlar(IReadOnlyList<ReservationRow> r) => new(
        "Rezervasyonlar",
        ["Rez No", "Durum", "Müşteri", "Cep Tel", "Plaka", "Başlangıç", "Bitiş", "Çıkış Ofisi", "Dönüş Ofisi",
         "Kaynak", "Talep Türü", "Geldiği Birim", "Proje Adı", "Onay Kodu", "Gün", "Günlük Ücret", "Tutar"],
        r.Select(x => new object?[]
        {
            x.Rez.ReservationNo, x.Rez.Durum.ToString(), x.MusteriAd, x.CepTel, x.Plaka,
            ExportTarih.Gun(x.Rez.BasTar), ExportTarih.Gun(x.Rez.BitTar),
            x.Rez.CikisOfisi, x.Rez.DonusOfisi, x.Rez.Kaynak,
            x.Rez.TalepTuru, x.Rez.GeldigiBirim, x.Rez.ProjeAdi, x.Rez.OnayKodu,
            x.Rez.Gun, x.Rez.GunlukUcret, x.Rez.Tutar
        }).ToList());

    public static ExportTable Lokasyonlar(IReadOnlyList<Location> l) => new(
        "Lokasyonlar",
        ["Kod", "Ad", "Adres", "Telefon", "E-posta", "Çalışma Saatleri", "Teslim Ücreti", "Şube", "Aktif"],
        l.Select(x => new object?[]
        {
            x.Kod, x.Ad, x.Adres, x.Telefon, x.Eposta, x.CalismaSaatleri, x.TeslimUcreti, x.Sube, x.Aktif ? "Evet" : "Hayır"
        }).ToList());

    /// <summary>Drop matrisi. Başlıklar FAZ-22 anlam netleştirmesine göre: "Lokasyon" DÖNÜŞ ofisi,
    /// "Şube" ÇIKIŞ şubesidir. Drop 2 / Karşılama Süresi bilgi alanıdır (hesaba girmez).</summary>
    public static ExportTable DropTanimlari(IReadOnlyList<DropTanim> d) => new(
        "Drop Tanımları",
        ["Dönüş Lokasyonu", "Çıkış Şubesi", "Çıkış Lokasyonu", "Asgari Gün", "Karşılama Şekli",
         "Çalışma Şekli", "Özel İletişim", "Drop Ücreti (net)", "Drop 2 (bilgi)", "Karşılama Süresi (dk)", "Aktif"],
        d.Select(x => new object?[]
        {
            x.Lokasyon, x.Sube, x.CikisLokasyon, x.MinGun, x.KarsilamaSekli, x.CalismaSekli,
            x.OzelIletisim, x.Ucret, x.Drop2, x.ManSuresi, x.Aktif ? "Evet" : "Hayır"
        }).ToList());

    /// <summary>Personel — HASSAS PII (TC + maaş). <paramref name="decrypt"/> cipher'ları çözer (ISecretProtector);
    /// katalog saf kalır (test'te sahte decrypt). Uç ManageUsers (Admin) gate'li + KVKK notu (docs/ops/kvkk-export-notu.md).</summary>
    public static ExportTable Personel(IReadOnlyList<Personel> p, Func<string?, string?> decrypt) => new(
        "Personel",
        ["Kod", "Ad", "Soyad", "TC Kimlik", "İşe Giriş", "İşe Çıkış", "Sürücü Belge No", "Maaş", "Şube", "Durum"],
        p.Select(x => new object?[]
        {
            x.Kod, x.Ad, x.Soyad, decrypt(x.TcKimlikEnc), ExportTarih.Gun(x.IseGiris), ExportTarih.Gun(x.IseCikis),
            x.SurucuBelgeNo, decrypt(x.MaasEnc), x.Sube, x.Aktif ? "Aktif" : "Pasif"
        }).ToList());

    /// <summary>Uzun-dönem (filo) kiralama sözleşmeleri. Plaka/müşteri FK'leri endpoint'te dict ile çözülür
    /// (<paramref name="plaka"/>/<paramref name="musteri"/> resolver; katalog saf kalır, test'te sahte resolver).</summary>
    public static ExportTable FiloKiralamalar(IReadOnlyList<FiloKiralama> f, Func<Guid, string?> plaka, Func<Guid, string?> musteri) => new(
        "Filo Kiralama",
        ["No", "Müşteri", "Plaka", "Başlangıç", "Süre (Ay)", "Aylık Ücret", "KDV Oranı", "Döviz", "Kur", "Toplam KM Limiti", "Damga Vergisi", "Durum", "Açıklama"],
        f.Select(x => new object?[]
        {
            x.No, musteri(x.MusteriId), plaka(x.VehicleId), D(x.BasTar), x.SureAy, x.AylikUcret, x.KdvOrani,
            x.Currency, x.Kur, x.ToplamKmLimiti, x.DamgaVergisi, x.Durum.ToString(), x.Aciklama
        }).ToList());

    /// <summary>Birleşik vade panosu (sigorta/MTV/muayene bitişleri) — en yakına sıralı. Plaka endpoint'te çözülür.</summary>
    public static ExportTable Vadeler(IReadOnlyList<VadeItem> v, Func<Guid, string?> plaka) => new(
        "Vade (Sigorta-MTV-Muayene)",
        ["Plaka", "Tür", "Bitiş", "Kalan Gün", "Durum"],
        v.Select(x => new object?[]
        {
            plaka(x.VehicleId), x.Tur, D(x.Bitis), x.KalanGun, x.Bucket.ToString()
        }).ToList());

    /// <summary>
    /// FAZ-41 — hukuk dosyası listesi (canlı hukuk_islem_listesi.aspx). Müşteri adı satır DTO'sunda
    /// zaten çözülü geldiği için ayrıca resolver almaz.
    ///
    /// <para><b>Tutar/Tahsilat/Kalan BİLGİ kolonudur — muhasebe defterinden gelmez</b> ve hiçbir
    /// mali rapora karışmaz (bkz. HukukDosya.Tahsilat). Kalan entity'nin tek formülünden okunur,
    /// burada yeniden hesaplanmaz.</para>
    /// </summary>
    public static ExportTable HukukDosyalari(IReadOnlyList<HukukDosyaSatirDto> h) => new(
        "Hukuk Dosyalari",
        ["Dosya No", "Müşteri", "Müşteri Tel", "Fatura No", "Tür", "Avukat", "Avukat Tel", "Avukat E-posta",
         "2. Avukat", "2. Avukat Tel", "2. Avukat E-posta",
         "Tutar (bilgi)", "Tahsilat (bilgi)", "Kalan (bilgi)", "Durum", "Tarih", "Aktif", "Açıklama"],
        h.Select(x => new object?[]
        {
            x.Dosya.DosyaNo, x.MusteriAd, x.MusteriTel, x.Dosya.FaturaNoTemp, x.Dosya.Tur.ToString(),
            x.Dosya.Avukat, x.Dosya.AvukatTel, x.Dosya.AvukatMail,
            x.Dosya.Avukat2Ad, x.Dosya.Avukat2Tel, x.Dosya.Avukat2Mail,
            x.Dosya.Tutar, x.Dosya.Tahsilat, x.Dosya.Kalan, x.Dosya.Durum.ToString(),
            D(x.Dosya.Tarih), E(x.Dosya.Aktif), x.Dosya.Aciklama
        }).ToList());

    // Hücre biçimleyiciler (sütun zenginleştirme için): bool → Evet/Hayır, nullable tarih → yyyy-MM-dd.
    private static string E(bool b) => b ? "Evet" : "Hayır";

    /// <summary>
    /// Tarih hücresi — kural (yerel takvim günü) ve gerekçesi <see cref="ExportTarih"/> içinde.
    /// Burada yalnız kısa ad olarak durur; yeni bir "ham UTC" yolu AÇMAYIN.
    /// </summary>
    private static string? D(DateTimeOffset? d) => ExportTarih.Gun(d);
}
