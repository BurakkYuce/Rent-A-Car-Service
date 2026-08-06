# Modül 07 — Raporlar (11 ekran)

**Özet:** 0 ✅ TAM · 7 🟡 KISMİ · 2 ❌ YOK · 1 ❓ DOĞRULANAMADI · 1 PARA — OPUS'A DEVİR

**Yöntem notu (K2 hesaplama):** Canlı profildeki "alan" sayımı (header) ASP.NET WebForms platform
gürültüsünü içeriyor (`MaskedEditExtender*_ClientState` gizli alanları, adı belirsiz postback butonları
`Button2/Button5/Up_ButtonN/btnSaveLayout`, oturum/yetki bayrakları `Islem_Turu`/`Kll_Sube_Degistiremez`/
`Durum`). Bunlar kullanıcıya görünen gerçek filtre değil — her ekranda tekrarlanan iskelet. K2'yi bu
gürültüyü çıkarıp **ayrı işlevli gerçek filtre kavramı** sayısına göre hesapladım (örn. `Tarih1`+
`MaskedEditExtenderTarih1_ClientState` = tek kavram "başlangıç tarihi"). Bu, ham alan sayısına göre daha
temsilcidir ve her ekranda tutarlı uygulandı. K3 her zaman canlı `kolon` sayımına karşı hesaplandı.

**Özel kontrol (talimat gereği):** `/raporlar/kasa-banka` ve `/raporlar/servis-ozet` (menüde değil) bu
modülün 11 ekranından **HİÇBİRİNE** karşılık gelmiyor — kasa-banka bir nakit/banka defteri, servis-ozet
tamamlanmış servislerin işçilik maliyet özeti; bu modülde ne kasa/banka defteri ne de tamamlanmış-servis-
maliyeti konulu bir canlı ekran var (`periyodik_servis_raporu.aspx` KM'ye göre YAKLAŞAN bakım uyarısı,
farklı bir konu). Dolayısıyla bu ikisi için `⚠ ERİŞİLEMEZ` bu modülde işaretlenmedi.

---

## arac_genel_durumu_grafik.aspx — Tüm Araç Durum Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/filo` (`src/RentACar.Web/Components/Pages/Reports/FiloDoluluk.razor`)
- **kanit:** /raporlar/filo · K2=N/A (canlı alan=0, hiç form/filtre yok — statik grid) · K3=%38 (5/13: Filo~Toplam, Boş Araç~Müsait, Kirada~Kirada, Bakımda~Serviste, Doluluk~Doluluk%)
- **Canlı fazlası:** Şubeler (şube kırılımı — bizde tek toplam tenant-geneli), Dönecekler, Çıkışlar, Çıkacaklar, Giden Rez., Dönüşler, Baf, Satılık
- **Bizde fazlası:** Aktif Kira Sözleşmesi kartı, Pasif satırı, Export (Excel/CSV/PDF — canlıda export yok)
- **Not:** Canlı ekran şube bazlı anlık filo tablosu (13 kolon, rezervasyon/dönüş pipeline dahil, hiç filtresi yok — otomatik yenilenen dashboard). Bizim `/raporlar/filo` tek satır tenant-geneli özet; şube kırılımı ve giriş/çıkış pipeline'ı hiç yok. `/raporlar/arac-durum-takip` gün kırılımlı benzer bir alt-küme sunuyor ama o da şubesiz.

## bos_arac_raporu.aspx — (Boş Araç Raporu — gün bazlı filo sayımı)
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/arac-durum-takip` (`src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`)
- **kanit:** /raporlar/arac-durum-takip · K2=%50 (2/4: Tarih1→from, Tarih2→to) · K3=%60 (3/5: Tarih~Gün, Toplam Araç~Toplam, Toplam Bakım~Bakım)
- **Canlı fazlası:** Ofis (şube) filtresi, Tarih_Listesi kapsam seçici, Toplam Kira kolonu (bizde "Dolu" adıyla farklı isimle var), Toplam Baf kolonu (hiç yok)
- **Bizde fazlası:** Boş kolonu ayrı gösteriliyor (canlıda dolaylı/hesaplanabilir)
- **Not:** Aynı iş (gün bazlı filo durum sayımı, aynı 5-kolon şekli) ama bizde şube filtresi yok ve "Baf" durumu (hasar/kaza tutanaklı araç) hiçbir raporda izlenmiyor.

## bos_km_detay.aspx
- **Durum:** 🟡 KISMİ (düşük güvenle — bkz Not)
- **Bizde:** `/raporlar/km-detay` (`src/RentACar.Web/Components/Pages/Reports/KmDetay.razor`)
- **kanit:** /raporlar/km-detay · K2=%40 (2/5: Tarih1→from, Tarih2→to) · K3=%29 (4/14: Plaka, Çıkış KM~Çıkış, Dönüş KM~Dönüş, Fark~Katedilen)
- **Canlı fazlası:** Tipi, İşlem Türü, Baş. Tarih, Bit. Tarih, Arac Fark, Marka, Vites, Yakıt Türü, Çıkış Ofisi, Dönüş Ofisi
- **Bizde fazlası:** Sözleşme, Limit, Fazla KM, Fazla Bedel
- **Not:** Ad benzerliği tuzağı olabilir — canlı ekran işlem-türü/ofis/araç-nitelik kırılımlı km-fark takibi (muhtemelen boşta-geçen-sürede km/fraud kontrolü); bizimki kira KM aşım faturalama satırı. K3 düşük (%29, sınırda) — gerçek eşleşme belirsiz, temkinli KISMİ verildi.

## doluluk_grafik.aspx — Doluluk Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/doluluk` (`src/RentACar.Web/Components/Pages/Reports/DolulukRaporu.razor`)
- **kanit:** /raporlar/doluluk · K2=%20 (2/10: Tarih1→from, Tarih2→to) · K3=%33 (2/6: Doluluk %~Doluluk, Toplam Araç~Araç Sayısı)
- **Canlı fazlası:** Tarih (gün kırılımlı satır tablosu — bizde tek dönem toplamı), Kira Doluluk / Rez Doluluk ayrımı, Şube/Araç Grubu/Rezervasyon Kaynağı karşılaştırma modu (Karsilastir), Bitiş/Bakım-Baf hariç tutma seçenekleri
- **Bizde fazlası:** Araç-Gün Kapasite / Kira-Gün ara-değer kartları (canlıda görünmüyor)
- **Not:** Canlı ekran çok-boyutlu karşılaştırmalı analiz aracı (şube/grup/kaynak bazında ayrı satırlar + 3 karşılaştırma metriği); bizimki tek dönem tek yüzde kartı. Çekirdek metrik (doluluk %) var, derinlik yok.

## extralar_raporu.aspx — Extralar Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/ek-hizmet` (`src/RentACar.Web/Components/Pages/Reports/EkHizmetRaporu.razor`)
- **kanit:** /raporlar/ek-hizmet · K2=%25 (2/8: Tarih1→from, Tarih2→to) · K3=%11 (2/19: Türü~Ek Hizmet adı, Fiyat~Net/Brüt konsepti)
- **Canlı fazlası:** Kayit No, Baş./Bit. Zaman, Plaka, RA No, Müşteri, Rez. Kaynağı, Kiraya Veren, Teslim Eden, Ek Hizmet Satan (+Log), Tur, Ç. Ofisi, İlk Tahsilat, Döviz, TL Fiyat — yani TÜM satır/işlem-kimlik kolonları
- **Bizde fazlası:** KDV ayrıştırması (Net/KDV/Brüt toplamları), Toplam Miktar
- **Not:** Ciddi granülarite farkı — canlı ekran işlem/satır bazlı detay listesi (kim sattı, hangi plakaya, hangi ofiste, hangi kirada); bizimki hizmet-adına göre TOPLU özet, hiç satıra/plakaya/müşteriye inmiyor. Filtre tarafında da Rapor_Turu(Kira/Rezervasyon)/Icerik/Kime_Ait/Islem_Sube eksik.

## gelir_tablosu.aspx — Gelir Gider Tablosu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/raporlar/karlilik` (`src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`) — en yakın aday (`/raporlar/gelir-gider` de değerlendirildi, dimensional kapsamı daha da düşük: yalnız 2 kolon eşleşiyor, boyut/kırılım yok)
- **kanit:** /raporlar/karlilik · K2=%26 (5/19: Tarih1→from, Tarih2→to, Ofis~sube, Gruplar~grup, Plaka~plaka) · K3=%18 (7/40: Şube, Plaka, Gelir, Gider, Grup, Araç Sayısı~Araç, Sonuç~Net Kâr)
- **Canlı fazlası:** Maliyet kırılımı (Ana Maliyet/Yönetim Maliyet/Toplam Maliyet), Potansiyel gelir, Doluluk, Araç Başı Gelir, Ortalama Fiyat, Kdv Durum modu (Kdvsiz/Kdv Dahil), RentTo ayrımı, SIPP kodu, Rezervasyon Kaynağı kırılımı, Cari Bilgi/Bakiye, Otopark(plaka şubesi) — çok-tablolu (şube+araç+cari) görünüm tek ekranda toplu
- **Bizde fazlası:** Segment boyutu (Karlılık'te var, canlı profilinde görünmüyor)
- **Not:** Kural 5 gereği durum kararı verilmedi (tutar üreten karmaşık çok-boyutlu maliyet/gelir tablosu, KDV modu dahil). Yapısal olarak not edilmesi gereken: en kapsamlı P&L ekranımız (Karlılık) bile canlının 40 kolonunun ancak ~7'sini karşılıyor — maliyet dağılımı (Ana/Yönetim maliyeti) ve potansiyel-gelir kavramı repoda hiç yok.

## genel_rapor.aspx
- **Durum:** ❌ YOK
- **Bizde:** — (eşleşen rota yok)
- **kanit:** — · K2=N/A · K3 hesaplanmadı (kolon kaynağı `yok` — GÜVENSİZ)
- **Canlı fazlası:** Yıllık Raporlar / Kira Raporları / Lokasyon Analizleri sekmeleri, özel alan ekle/düzenle ("Alan Ekle/Düzenle" — rapor-builder), yıl seçici (Years), çoklu tarih-kapsamı modu (Tarih Önemsiz/Başlangıç/Bitiş/Kira Zamanı/İşlem Tarihi), Kira_Durum (Kirada/RentTo/Döndü/İptal), Rez. Kaynağı/Lokasyon listesi, pivot tablo (DataTableJson/DataTableData, "Analizi Başlat"), kayıtlı rapor (ButtonSave/Kaydet, "Tümünü Sil/Tümünü Aktar")
- **Bizde fazlası:** —
- **Not:** Bu, kullanıcının kendi alan/pivot tanımlayabildiği genel bir rapor-oluşturucu (custom report builder) aracı; repomuzda buna benzer hiçbir şey yok. Kolon kaynağı güvensiz olduğundan içerik tam doğrulanamadı, ama form/aksiyon yapısı (25 aksiyon, pivot-özgü alanlar) tek başına eşleşen bir rota olmadığını gösteriyor.

## gunraporu.aspx — Günlük Faaliyet Raporu
- **Durum:** ❓ DOĞRULANAMADI
- **Bizde:** `/raporlar/gunluk` (`src/RentACar.Web/Components/Pages/Reports/GunlukFaaliyet.razor`)
- **kanit:** /raporlar/gunluk · K2=%50 (1/2: Tarih→gun; eksik: Islem_Sube şube filtresi) · K3 hesaplanmadı (kolon kaynağı `yok` — GÜVENSİZ)
- **Canlı fazlası:** Islem_Sube (şube) filtresi
- **Bizde fazlası:** Export (Excel/CSV — canlı profilinde export yok görünüyor)
- **Not:** Başlık ("Günlük Faaliyet Raporu") ve tek-gün tarih filtresi birebir örtüşüyor — güçlü aday, muhtemelen TAM/KISMİ. Ama canlı profilde grid/kart içeriği yakalanamadı (kolon=0, kaynak yok), gerçek metrik seti (kartlar mı, hangi sayılar) doğrulanamıyor; kural 4 gereği resmi kod DOĞRULANAMADI.

## kabis_raporu.aspx — Kabis Raporu
- **Durum:** ❌ YOK
- **Bizde:** — (eşleşen rota yok)
- **kanit:** — · K2=N/A · K3=N/A
- **Canlı fazlası:** Id, RA, Plaka, Zaman, İşlem, Sonuç, Müşteri kolonları; "Kabis Bilgi" arama kutusu; Excel'e Aktar
- **Bizde fazlası:** —
- **Not:** KABİS muhtemelen bir kayıp/çalıntı-araç sorgu sistemi entegrasyon log ekranı — repoda "Kabis"/"KABİS" terimi hiçbir dosyada geçmiyor (kod genelinde arandı, sıfır sonuç). Kimlik/entegrasyon gerektiren erteli-iş kapsamına girebilir.

## periyodik_servis_raporu.aspx — Periyodik Servis Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/periyodik-servis` (`src/RentACar.Web/Components/Pages/Reports/PeriyodikServis.razor`)
- **kanit:** /raporlar/periyodik-servis · K2=%0 (0/4: bizde HİÇ filtre yok; canlıda Araç ara/Otopark/Durum/Uyarı eşiği var) · K3=%33 (4/12: Plaka, Şuanki KM~Güncel KM, Uyarı KM~Sonraki Bakım KM, Kalan KM~Kalan KM)
- **Canlı fazlası:** Marka, Tipi, Model, Yakıt Türü, Vites, Şube, İşlem Tarihi, İşlem KM kolonları; TÜM filtreler (TextBox1 araç arama, Ofis otopark/şube, DropDownListDurum aktif/pasif, Uyari eşik seçimi)
- **Bizde fazlası:** Kaynak (Servis/Tanım ayrımı — hedef KM'nin nereden geldiğini gösterir, canlıda yok), Export (Excel/CSV/PDF — canlı profilinde export yok)
- **Not:** Çekirdek metrik (kalan km, artan sıralı) var ama bizde filtre SIFIR — canlıda 4 filtre var, en kritik eksik Ofis(şube) ve Uyarı-eşiği. Home panosundaki "KM Geçen Bakım" rozeti ve Vade panosu bu raporla aynı sorguyu (`OrtakSorgular`) paylaşıyor, ayrı bir derinlik eklemiyor.

## rezervasyon_kaynak_raporu.aspx — Rezervasyon Kaynak Raporu
- **Durum:** 🟡 KISMİ
- **Bizde:** `/raporlar/rezervasyon-kaynak` (`src/RentACar.Web/Components/Pages/Reports/RezervasyonKaynak.razor`)
- **kanit:** /raporlar/rezervasyon-kaynak · K2=%33 (2/6: Tarih1→from, Tarih2→to) · K3=%80 (4/5: Firma Adı~Kaynak, Genel Toplam~Toplam Ciro, Rez. Gün~Toplam Gün, Rez. Sayısı~Adet)
- **Canlı fazlası:** Döviz kolonu (para birimi kırılımı — bizde tek baz para/₺ topluyoruz), Ofis_Durum + Ofis (şube) filtresi, Gruplar (araç grubu) filtresi, Tarih_Listesi (Kayıt/Çıkış/Dönüş tarihine göre seçim — bizde sabit "başlangıç tarihine göre")
- **Bizde fazlası:** —
- **Not:** Kolon eşleşmesi güçlü (%80) ama şube/araç-grubu filtresi yok ve döviz kırılımı hiç yok — toplamlar tek baz para (₺); canlı ekran muhtemelen çoklu döviz gösteriyor. Genel Toplam bir tutar kolonu olduğundan, sayısal doğruluk teyidi finans-odaklı bir gözden geçirmeyi hak edebilir (yapısal KISMİ kararını değiştirmez).

---

TOPLAM: 11 ekran işlendi
