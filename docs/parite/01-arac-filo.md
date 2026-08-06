# Modül: 01-arac-filo (25 ekran)

**Durum dağılımı:** ✅ TAM=0 · 🟡 KISMİ=7 · ❌ YOK=4 · ⚠ ERİŞİLEMEZ=0 · 🩹 CANLI BOZUK=0 · ❓ DOĞRULANAMADI=0 · PARA=14

Not (yöntem): Liste-tipi ekranlarda K2 paydası "Filtre adayları" listesinden anlamlı alan sayısı (hidden
MaskedEditExtender ClientState eşleri ve jenerik submit butonları tekilleştirilip çıkarılmıştır); form-tipi
ekranlarda (kolon kaynağı `yok`) "Form alanları" listesinden aynı yöntemle. K3 yalnız kolon kaynağı `dom_dx`/`dom_th`
olan ekranlarda hesaplanmıştır. Para üreten ekranlarda (kredi/sipariş/satış/mtv/muayene/servis/sigorta/gelir-gider)
kural 5 uygulanmış: durum kararı verilmedi, kanıt dolduruldu, `PARA — OPUS'A DEVİR` yazıldı.

---

## arac_durum_takip.aspx — TürevRent (Araç Durum Takip, gün kırılımı)
- **Durum:** 🟡 KISMİ
- **Bizde:** /raporlar/arac-durum-takip (`src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`)
- **kanit:** /raporlar/arac-durum-takip · K2=%18 (2/11) · K3=%43 (3/7)
- **Canlı fazlası:**
  - Grain farklı: canlı satır=ARAÇ (Plaka/SIPP bazlı, seçilen aralıkta toplam Gün/Bakım Gün/Baf Gün/Boş Gün/Potansiyel); bizde satır=GÜN (seçilen aralıktaki her gün için filo-geneli Dolu/Bakım/Boş sayısı). Aynı temel veri (araç×gün doluluk durumu) ama transpoze — "hangi araç en çok boşta kaldı" bizde ekrandan çıkarılamaz.
  - **Baf Gün** kovası bizde hiç yok (Dolu/Bakım/Boş üçlü; Baf ayrı sayılmıyor — Dolu içine mi karışıyor belirsiz).
  - **Potansiyel** (olası kira geliri) kolonu yok.
  - Filtreler eksik: Ofis_Durum/Ofis (şube), Arac_Sahip (araç sahibi), Gruplar, Otopark, DropDownListGrup (SIPP/Grup toggle), RentTo, Plaka.
- **Bizde fazlası:** Toplam araç sayısı (Toplam kolonu) — canlıda yok.
- **Not:** K2 eşiğin (%30) altında ama alttaki veri kavramı (araç×gün doluluk) aynı, sadece pivotu farklı; bu yüzden ❌ YOK değil KISMİ işaretlendi. Sayılar şeffaf verildi, okuyucu farklı karar verebilir.

## arac_gelir_gider_tablosu.aspx — Araç Gelir/Gider Tablosu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /raporlar/karlilik (`src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`) + /raporlar/filo-analiz (`src/RentACar.Web/Components/Pages/Reports/FiloAnaliz.razor`) + /raporlar/ek-hizmet (`src/RentACar.Web/Components/Pages/Reports/EkHizmetRaporu.razor`)
- **kanit:** /raporlar/karlilik · K2=%25 (2/8, Tarih1/Tarih2↔from/to + Grubu↔grup boyutu) · K3=%14 (9/64, Kira Gelir↔Gelir, Toplam Kazanç↔NetKar, Doluluk↔DolulukYuzde, Marka/Tipi/Yakıt/Vites/Grup/Şube gibi tekil eşleşmeler)
- **Canlı fazlası:** Canlı 5 farklı Excel-export modu var (Şube/Grup/Araç/Detay/Hizmet listesi — tek ekranda 5 kırılım seviyesi); bizde bu 3 ayrı rapora (Karlilik boyut=sube/grup/araç, FiloAnaliz, EkHizmetRaporu) bölünmüş, hiçbiri canlının ~25 ek-hizmet-kolonunu (CDW, LCF, Genç Sürücü, Bebek Koltuğu, Wifi, Kış Lastiği, Paket1-6, SCDW…) tek satırda araç bazında göstermiyor — EkHizmetRaporu bunları AD bazında pivotluyor (satır=hizmet türü), canlı ARAÇ bazında pivotluyor (sütun=hizmet türü). Ort. Kira/Hizmet Oran/Çalış. Araç gibi işletme KPI'ları da yok.
- **Bizde fazlası:** ROI%, Km Maliyet, Sınıf Endeks, Yaş(ay) gibi FiloAnaliz KPI'ları canlıda bu ekranda yok (başka canlı ekranlarda olabilir, bu modülde değerlendirilmedi).
- **Not:** Para/gelir-gider üreten çok-boyutlu rapor; hangi bizdeki rapor(lar)ın canlı 5-modlu export'un GERÇEK dengi sayılacağı muhasebe/para uzmanlığı gerektiriyor → OPUS'A DEVİR.

## arac_guncel_durum.aspx — Araç Güncel Durum
- **Durum:** ❌ YOK
- **Bizde:** /arac-durum (`src/RentACar.Web/Components/Pages/Fleet/FleetStatus.razor`) — en yakın aday, eşleşme yetersiz
- **kanit:** /arac-durum · K2=%27 (6/22: Plaka≈q, Grubu≈grup, Durum≈durum, Marka≈marka, Vites_Turu≈vites, Yakit_Turu≈yakit) · K3=%12 (8/68)
- **Canlı fazlası:** Canlıdaki asıl iş = satır-içi AKSİYON konsolu: her satırda **Kirala / Servis / Baf** doğrudan işlem butonları + aktif kira/servis/baf özet kolonları (Söz.No, Ad/Soyad, Cep Tel, Rez Müşteri, Kira Kalan, Dosya No…) — 68 kolonun büyük kısmı bu üç iş akışının canlı özeti. Bizim /arac-durum salt-okunur bir durum panosu (60 sn otomatik yenilenen), satırdan doğrudan kira/servis/baf başlatma YOK — link yalnız /vehicles/{id}'ye gidiyor. Ofis/Pasif_Sebep/Arac_Status/Kar_Lastigi/Web_Rez_Kapat/Ofis_Rez_Kapat/HGS_OGS/GPS filtreleri de yok.
- **Bizde fazlası:** —
- **Not:** AJAN-TALIMATI'nın rule-3 örneği tam bu çifti işaret ediyor ("Arac_Guncel_Durum ile /arac-durum benzer adlı ama farklı iş olabilir") — doğrulandı: biri aksiyon konsolu, diğeri pasif durum panosu, farklı iş.

## arac_gunluk_durum.aspx — Araç Günlük Durum
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /raporlar/karlilik (`src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`) — en yakın aday, zayıf
- **kanit:** /raporlar/karlilik · K2=%0 (0/6: btnSaveLayout,Button4,Plaka,Grubu,Arac_Sahibi,Ofis — hiçbiri Karlilik'in from/to/sube/grup/plaka filtreleriyle birebir değil çünkü canlı burada tarih aralığı YOK, o gün/o an içindir) · K3=%17 (2/12: Plaka, Araç Grubu)
- **Canlı fazlası:** Canlı ekran ARAÇ × GÜN bazında Günlük Kira/Günlük Hizmet/Günlük Toplam gösteriyor (aktif kiraların o günkü günlük gelir kesitleri); bizdeki hiçbir rapor bu per-araç-per-gün gelir kesitini üretmiyor (Karlilik dönem toplamı verir, günlük değil).
- **Not:** Gelir üreten ekran; canlı grain'i (araç-günlük gelir) hiçbir bizdeki route'ta yok — Opus bu boşluğun yeni bir rapor mu gerektirdiğine karar vermeli.

## arac_kayit.aspx — Araç Kayıt
- **Durum:** 🟡 KISMİ
- **Bizde:** /vehicles/{Id:guid} (`src/RentACar.Web/Components/Pages/Vehicles/VehicleEdit.razor`) + /araclar/{Id:guid} (`src/RentACar.Web/Components/Pages/Details/VehicleDetail.razor`)
- **kanit:** /vehicles/{Id:guid} · K2=%30 (46/152, alan= toplam sayımı) · K3=%11 (4/35 — embedded sekme-grid'leri VehicleDetail'deki farklı grid'lerle kabaca örtüşüyor)
- **Canlı fazlası (eksik alanlar, tek tek):**
  - Segment/SIPP dışı: TSRB_Marka_Kodu/Tip_Kodu, Alt_Grup_Adi, Entegrasyon_Kodu, Teyp_Kodu
  - Sigorta/Trafik/Kasko takip bloğu TAMAMEN yok burada (Trafik_Bas_Tar/Tar/Firma/Acenta/Pol_No, Kasko_Bas_Tar/Tar/Firma/Acenta/Pol_No/Kodu/Bedeli, Sigorta_Takip_Disarda) — bizde bu bilgi ayrı /regulasyon route'unda, vehicle-edit formuna gömülü değil
  - Muayene_Bas_Tar/Tar aynı şekilde ayrı /regulasyon'da, burada yok
  - Periyodik bakım/lastik/km-tespit geçmiş alt-kayıtları yok: Son_Per_No/Km/Tar, Km_Tespit_No/Km/Tar, Son Lastik No/Km/Tar (TextBox6/7/8)
  - HGS_Firmasi, Takip_Marka/No (GPS takip cihazı), Kontak_Kapat, UTTS var ama OGS numarası (OgsNo alanımız var ama live'daki ayrı "HGS/OGS Varmı" durum select'i yok)
  - Finansal döviz/kur alanları yok: Alim_Bedeli_Kur, Alis_Euro, Arac_2_Fiyat_Kur, SimdiKur, AylikMaliyetDoviz
  - Sahip_Grup, Arac_Sahibi_No, Arac_Sahibi2 (araç sahibi kırılımı) — bizde tek "aracSahibi" text alanı
  - Kredi_Firma, Kapatma_Tarih, Rehin_Durumu(var, ama live'da ayrı alan), Hizmet_Assist_Firma, Cikmasi_Planan_Tarih, Arac_Satis_KM, Aciklama (genel açıklama alanı yok), Pasif_Sebep (pasif etme sebebi select'i yok — sadece Durum enum var)
  - Konum (lokasyon) alanı yok
- **Bizde fazlası:** Vitrin Adedi (web sitesi gösterim sayısı), Kira Km Limiti ayrı alan olarak var (live'da "Dış Tedarik Km Limiti" adıyla benzer ama farklı bağlamda), fotoğraf yönetimi (canlıda bu ekranda yok).
- **Not:** Canlı, sigorta/muayene/servis geçmişini SEKME olarak aynı ekrana gömüyor; bizde bunlar ayrı route'lara (regulasyon/servisler) bölünmüş — mimari fark, veri büyük ölçüde başka yerde MEVCUT ama bu ekranın K3'ü düşük kalıyor.

## arac_kredi.aspx — Araç Kredisi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /arac-kredi (`src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`)
- **kanit:** /arac-kredi · K2=%20 (3/15: Tutar≈krediTutari, Taksit_Adeti≈taksitSayisi, Ilk_Vade_Tarihi≈baslangicTarihi) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası:** Kredi bir **Cari**ye bağlı (Musteri_No/Ad — kredi veren/ilişkili cari araması); bizde sadece özgür-metin "Banka" adı. Faiz_Toplam (tutar olarak, bizde sadece oran var), Son_Vade_Gunu (ayın kaçı vade), Son_Taksit_Tutari (küsürat farkı için ayrı son taksit tutarı), Bu_Ay_Toplam_Taksit, Toplam_Kredi_Borcu (özet kartlar) yok. Taksitleri toplu iptal etme ("Taksitleri İptal Et") ve toplu-kaydet ayrı aksiyonlar var.
- **Bizde fazlası:** Taksit ödeme doğrudan GERÇEK gider+defter kaydı üretiyor (Finansman); canlı profilinde defter entegrasyonu görünmüyor (yalnız takip).
- **Not:** Para/kredi ekranı — cari-bağlama ve faiz/vade modeli farkının doğruluğu Opus'ta değerlendirilmeli.

## arac_kredi_listesi.aspx — Araç Kredi Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /arac-kredi (`src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`)
- **kanit:** /arac-kredi · K2=%17 (1/6: Plaka/Arac aramaya karşı yok — sadece Musteri_No/Ad_Soyad cari araması eşleşmiyor çünkü bizde kredi cariye değil bankaya bağlı; Tarih_Listesi/Tarih1/Tarih2 aralık filtresi de yok) · K3=%40 (2/5: Kredi ID↔No, Kredi Tutarı↔Kredi)
- **Canlı fazlası:** Cari Bilgi ve Araç Bilgisi (Plaka/Arac) ile arama filtreleri yok; Dosya No kolonu yok.
- **Not:** Aynı /arac-kredi route'una eşleşiyor; para kümesi olduğu için karar Opus'a.

## arac_listesi.aspx — Araç Listesi
- **Durum:** 🟡 KISMİ
- **Bizde:** /vehicles (`src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`)
- **kanit:** /vehicles · K2=%25 (3/12: TextBox1≈q, Grubu≈grup, DropDownListDurum≈durum) · K3=%35 (17/49)
- **Canlı fazlası (eksik filtreler, tek tek):** Arac_Sahibi (Hepsi/Bizim/Dış Araç), Uyari_Lastik (lastik uyarı KM göster toggle), ArTarih_Listesi (Çıkması Planan/Çıkış/Giriş/Çalışma Tarihleri/Filo Bulunanlar tarih TİPİ seçimi) + tarih aralığı, Grup_Turu (Araç Grubu/SIPP toggle), Analiz + Analiz_Tarih, Baslik_Koy, Lokasyon_Ara, Ofis (bizde şube filtresi yok, sadece grup/durum/plaka).
- **Canlı fazlası (eksik kolonlar, tek tek):** Lokasyon, Araç Sahip Grup, Araç Sahibi-2, Takip No, Teyp Kodu, Alış Bedeli, Açıklama, Filo Gir./Çık. Tar., Çık. Plan. Tarih, Sözleşme No, Servis/Baf/Satış (bayrak kolonları), Kasko Bedeli, Lastik Uyarı KM, Kredi Kuruluşu/Son Tarih, İlk Tescil Tarihi, Alınan Firma, Grup Açıklama, Yedek Anahtar, HGS/OGS (durum), Ruhsat Tarihi, Lastik Bilgisi, 2. El Değeri, Son Yakıt (yakıt seviyesi — Yakıt Türü ile karıştırılmasın), Seyrüsefer, Z-İzni, Statü, Araç Belge No, Kar Lastiği (kolon olarak; alan var ama listede gösterilmiyor).
- **Bizde fazlası:** Segment, Motor Gücü, Kasa Tipi, Son Bakım KM kolonları — canlıda bu listede yok (başka ekranlarda olabilir). "Modele göre grupla" görünümü canlıda karşılığı yok.
- **Not:** Aynı temel iş (araç listesi/kayıt gezinme); filtre ve kolon zenginliği bizde önemli ölçüde daha dar.

## arac_mtv_islemleri.aspx — Araç MTV işlemleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`, MTV bölümü)
- **kanit:** /regulasyon · K2=%21 (4/19: Bandrol_Donemleri≈donem, Tutar≈tutar, Plaka/Arac_Bilgisi≈vehicleId, Odeme_Turu≈hesap-select) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası:** Tutar_Doviz/Tutar_Kur (döviz tutarı), Kalan (kısmi ödeme bakiyesi), Evrak_No, Islem_Yapan, Aciklama, Odeme_Tarihi (ayrı ödeme tarihi — bizde Öde butonu = anlık); Kasa_Kodu/Hesap_No (spesifik kasa/IBAN seçimi — bizde sadece Kasa/Banka ikili seçim).
- **Bizde fazlası:** **Vade** alanı zorunlu (bizde MTV'nin vade tarihi takip ediliyor — canlı profilinde ayrı bir vade/due-date alanı YOK, muhtemelen dönem bazlı zımni vade).
- **Not:** Vergi/gider kaydı üreten ekran; kısmi ödeme (Kalan) modelinin bizde yokluğu para akışı açısından önemli — Opus değerlendirsin.

## arac_muayene_islemleri.aspx — Araç Muayene İşlemleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`, Muayene bölümü)
- **kanit:** /regulasyon · K2=%28 (5/18: vehicleId≈Plaka/Arac_Bilgisi, muayeneTarihi≈Tarih, bitis≈Muayene_Bit_Tar, ucret≈Tutar, ceza(ödeme formunda)≈Ceza_Tutari) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası:** Islem_KM, Evrak_No, Islem_Yapan, Kalan (bakiye), Odeme_Tarihi ayrı, Kasa_Kodu/Hesap_No detay seçimi, Aciklama.
- **Not:** Ceza_Tutari alanı bizde de var ama ödeme anında girilir (ana kayıtta değil) — akış farkı Opus'a bırakıldı.

## arac_plan_yonetim.aspx — TürevRent (Araç Plan Yönetimi)
- **Durum:** ❌ YOK
- **Bizde:** — (aday bulunamadı)
- **kanit:** — · K2=%0 (0/20) · kolon kaynağı `yok`
- **Canlı fazlası:** SIPP/Araç Grubu bazında hedef filo adedini Artır/Azalt işlemiyle planlama (kapasite planlama ekranı). Kod tabanında "hedef adet/filo planı/fleet plan" kavramına dair hiçbir eşleşme bulunamadı (`grep` boş sonuç).
- **Not:** Envanterde bu işlevi karşılayan bir route yok; en yakın olabilecek /arac-siparis (tedarik siparişi) FARKLI bir iş (gerçek satın alma siparişi, hedef-adet planlaması değil).

## arac_rac_takvim.aspx — Çalışma Takvimi
- **Durum:** 🟡 KISMİ
- **Bizde:** /takvim (`src/RentACar.Web/Components/Pages/Bookings/ReservationCalendar.razor`)
- **kanit:** /takvim · K2=%25 (1/4: Tarih1≈ay-parametresi) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası:** AracGrubu (araç grubu filtresi), Bolge1/listBox/ASPxButton1 (çoklu bölge/şube seçimi), SearchBox (arama kutusu) — bizde /takvim'de grup/bölge filtresi hiç yok (Operatör rolünde örtük şube-kapsamı var ama seçilebilir bir alan değil).
- **Bizde fazlası:** Rezervasyon (R) / Kira (K) ayrımı renkli gösterim; canlı profilinde bu ayrımın var olup olmadığı görünmüyor (kolon kaynağı yok).
- **Not:** K2 %30 eşiğinin altında ama iki ekranın MEKANİZMASI aynı: satır=araç, sütun=gün, hücre=doluluk göstergesi (Gantt-tipi çalışma takvimi) — isim farklı olsa da iş aynı görünüyor, bu yüzden KISMİ (grup/bölge filtre eksikliğiyle) işaretlendi; ❌ YOK'a çevirmek isteyen bir insan gözden geçirici sayılarla haklı olabilir.

## arac_satis.aspx — Araç Satış İşlemleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /satislar (`src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`)
- **kanit:** /satislar · K2=%26 (6/23: Musteri_No≈aliciCariId, Satis_Fiyati≈satisNet, Doviz≈doviz, Satis_KDV_Orani≈kdvOrani, Noter_Bilgisi≈noterNo, Aciklama1≈aciklama) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası:** Kiraya_Verme (checkbox), Liste_Fiyati+Liste_Doviz (talep/hedef fiyat — satış fiyatından AYRI), Ilan_Km/Satis_Km, Satis_Kanali/Satis_Noktasi/Uygulanan_Kampanya (satış kanalı/kampanya), İhale bloğu (Ihale_Firmasi/Tarihi/Sayisi), Satisi_Verildi (devir işlemi durumu — ayrı), Yevmiye_Numarasi, Aciklama2.
- **Bizde fazlası:** Kur alanı (otomatik TCMB/sabit kur çözümü) — canlı profilinde bu otomasyon görünmüyor.
- **Not:** Satış fiyatı/KDV/kur üreten ekran; hedef-fiyat vs satış-fiyatı ayrımının bizde yokluğu (yalnız satisNet var) parasal önemde — Opus değerlendirsin.

## arac_satis_ara.aspx — Satıştaki Araçlar
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /satislar (`src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`)
- **kanit:** /satislar · K2=%0 (0/8 — bizde /satislar sayfasında hiç filtre formu yok, sadece düz liste) · K3=%17 (6/36)
- **Canlı fazlası:** Tarih aralığı filtresi (Tarih_Listesi/Tarih1/Tarih2), Satisi_Verildi/Hedef_Fiyat_Turu/Hedef_Fiyat/Durumx/Plaka/Ofis filtreleri — bizde HİÇBİRİ yok. Kolonlarda da Kasko/Trafik poliçe bilgisi, İhale bilgisi, Kredi Firma, Geçen Süre gibi çoğu alan yok.
- **Not:** Para/liste ekranı; filtre yokluğu tek başına önemli ama karar Opus'a bırakıldı (kural 5).

## arac_satis_bedeli.aspx — TürevRent (Araç Satış Bedeli)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** ❓ belirsiz — en yakın adaylar /raporlar/karlilik, /raporlar/filo-analiz
- **kanit:** /raporlar/karlilik · K2=%0 (0/6, RentTo/Ofis_Durum kavramları bizde tanımsız) · K3=%7 (2/27: Plaka, Marka gibi zayıf tekil eşleşmeler)
- **Canlı fazlası:** "RentTo" kavramı (muhtemelen alt-kiralama/ortak filo partneri) bizde HİÇ tanımlı değil — kod tabanında `grep`'le "RentTo" karşılığı bulunamadı. Kazanç/Gider/Bakiye per-plaka/SIPP kırılımı ve Ofis_Durum (İşlem Ofisi/Çıkış Ofisi ayrımı) net değil.
- **Not:** Ekranın gerçek işlevi (canlı başlığı jenerik "TürevRent") tam belirlenemedi — "RentTo" alan-anlamı doğrulanamadan sağlıklı bir eşleşme iddia edilemez; hem PARA hem belirsizlik nedeniyle Opus'a devredildi.

## arac_servis_islemleri.aspx — Gider işlemi (Araç Servis/Bakım)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /servisler (`src/RentACar.Web/Components/Pages/ServiceRecords/ServiceRecordList.razor`)
- **kanit:** /servisler · K2=%11 (6/55: vehicleId≈Plaka/Arac_Bilgisi, tip≈CheckBoxPeriyodik/Hasar/Mekanik/Bakim, atolyeAdi≈Servis_Yeri, girisKm≈Islem_KM, hasarSorumlu≈DropDownKimOdeyecek, kusurOrani≈Kusur_Durumu) · K3=%0 (0/7 — canlı grid Açıklama/Birim Fiyat/Toplam Fiyat/İndirim/Tutar/KDV/Genel Toplam bir FİYATLANDIRMA alt-tablosu; bizde "servisler" listesinde bu satır kalemleri görünmüyor, ayrı POST uçlarında)
- **Canlı fazlası:** İşçilik kırılımı (Kaporta/Boya/Trim/Elektrik/Mekanik/Şase işçiliği — 6 ayrı alan, bizde tek "Kalem" satırı); tam kaza/hasar detay bloğu (Beyan_Turu, Karsi_Plaka, Karsi_Trafik_Sigortasi, Kaza_Tarihi, Kaza_Sorumlusu, Hasar_Dosya_No, Deger_Kaybi); fatura bloğu (Fatura_Bilgisi/Tarih/No/Tutar/KDV/Genel Toplam); ödeme bloğu (Odeme_Tarihi/Odeme/Odeme_Doviz/Odeme_Kur/Odeme_Turu/Kasa_Kodu/Hesap_No); Islem_KM ayrı Cikis/Donus KM+Yakıt granülü (bizde girisKm/cikisKm var, yakıt seviyesi yok).
- **Bizde fazlası:** Rücu Yansıtma (cariye kusur×maliyet yansıtma) tek butonla; canlıda "Yansitma_Cari/Yansitma_Tutar" alanları var ama akış farklı görünüyor.
- **Not:** 114 alanlı çok karmaşık canlı ekran; bizdeki sadeleştirilmiş servis kaydı yalnız çekirdek iş akışını (aç/servise al/tamamla/iptal/yansıt) kapsıyor — tutar/KDV/işçilik detayının doğruluğu Opus'ta.

## arac_sigorta_islemleri.aspx — Araç Sigorta İşlemleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/RegulationList.razor`, Sigorta bölümü)
- **kanit:** /regulasyon · K2=%20 (7/35: vehicleId≈Plaka/Arac_Bilgisi, tip≈Gider_Adi, baslangic≈Sigorta_Bas_Tar, bitis≈Sigorta_Bit_Tar, prim≈Tutar, doviz≈Odeme_Doviz, policeNo≈Evrak_No, firma≈Sigorta_Firmasi) · K3=%0 (0/9 — canlı grid Zeyil No/Tarih/Tanzim/Değer/Bürüt/Net/Fon-Vergi/Tipi/Neden TAMAMEN zeyil (poliçe zeyli/ek) alt-tablosu; bizde zeyil kaydı yok)
- **Canlı fazlası:** **Zeyil (poliçe eki) yönetimi tamamen yok** — canlıda ayrı Zeyil Listele/Yeni Zeyil/Zeyil Kayıt Sil aksiyonları + 9 kolonlu zeyil geçmişi var; bizde ödeme formunda tek bir "zeyil ek prim" sayısı var, kalıcı zeyil kaydı/geçmişi yok. Arac_Degeri/IMM_Degeri/Aksesuar_Degeri (sigorta değer tabanı) yok. Kalan (bakiye) yok.
- **Bizde fazlası:** Yabancı para poliçede kur zorunlu-doğrulama (boşsa TCMB/sabit kur otomatik, bulunamazsa red) — canlı profilinde bu güvenlik davranışı görünmüyor.
- **Not:** Zeyil alt-sisteminin tamamen yokluğu önemli bir fonksiyonel boşluk; para+sigorta karması olduğu için karar Opus'a.

## arac_siparis.aspx — TürevRent (Araç Sipariş/Tedarik)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /arac-siparis (`src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`)
- **kanit:** /arac-siparis · K2=%29 (8/28: Musteri_No≈tedarikci, Tarih≈siparisTarihi, Temin_Tarihi≈beklenenTeslim, MarkaT≈marka, Arac_Tipi≈tip, Arac_Grubu≈grup, Oneri_Adet/Onay_Adet≈adet, Onay_Fiyat/Liste_Fiyat≈birimFiyat) · K3=%23 (5/22)
- **Canlı fazlası:** Tedarikçi bir **Cari** kaydı (arama+seç); bizde özgür-metin ComboBox. Dosya_No, Imza_Tarih, Satis_Temsilci/Ozel_Temsilci (temsilci bilgisi), Versiyon/Opsiyon/Renk/Ic_Renk (araç spesifikasyon detayı), Durum (Bekliyor/Onaylı/Ret — bizde Bekliyor/Onaylandi/Iptal farklı isimlendirme+akış), Kaynak_Tip (ÖzMal), Satis_Tipi (Sıfır/2.El), Piyasa_Fiyat/Ops_Fiyat/Filo_Fiyat (bizde tek birimFiyat var, çok-fiyat-katmanı yok), TSBKayit_No (geçici plaka/TSB entegrasyonu), Kredi_No (siparişin krediyle bağlanması).
- **Bizde fazlası:** Doğrudan Onayla/Teslim Al/İptal durum-akışı butonları (canlıda "İşlem Listesi/Dosyayı Kaydet/Sil/Geçici Plaka Oluştur" farklı bir akış).
- **Not:** Fiyat onayı üreten ekran (Liste/Piyasa/Ops/Filo/Onay fiyat katmanları) — çok katmanlı fiyat modelinin bizde tek alana sıkıştırılmış olması parasal karar niteliğinde, Opus'a bırakıldı.

## arac_siparis_detay_listesi.aspx — TürevRent (Sipariş Detay Listesi)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /arac-siparis (`src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`)
- **kanit:** /arac-siparis · K2=%0 (0/6 — bizde filtre formu yok) · K3=%19 (4/21: ID≈No, Marka≈Marka, Yakıt Türü≈—, Onaylanan≈Durum gibi zayıf eşleşmeler)
- **Canlı fazlası:** Musteri_No/Ad_Soyad (cari arama), Tarih aralığı, Plaka/Arac arama filtreleri yok; Teklif Tarih/Temin Tarih/Liste-Piyasa-Ops-Filo-Onay Fiyat çok-katmanlı kolonlar yok.
- **Not:** arac_siparis.aspx ile aynı /arac-siparis route'una eşleşiyor, farklı bir DETAY/liste görünümü; para kümesi, karar Opus'a.

## arac_siparis_listesi.aspx — TürevRent (Sipariş Listesi, sade)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /arac-siparis (`src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`)
- **kanit:** /arac-siparis · K2=%0 (0/6 — filtre yok) · K3=%50 (2/4: ID≈No, Dosya No≈—; Cari Bilgi≈Tedarikçi kısmi)
- **Canlı fazlası:** Musteri_No/Ad_Soyad/Tarih aralığı/Plaka arama filtreleri yok.
- **Not:** En dar canlı varyant (4 kolon); yine de para kümesi olduğundan karar Opus'a.

## baf_ara.aspx — Baf Listesi
- **Durum:** ❌ YOK
- **Bizde:** /baf (`src/RentACar.Web/Components/Pages/Baflar/BafList.razor`) — CRUD var, ARAMA/FİLTRE yok
- **kanit:** /baf · K2=%0 (0/9: Kullanici, Plaka, Durumx, Lokasyon, Kullanim_Amaci, Tarih_Listesi, Tarih_Aralik, Ofis_Sec, Ofis — hiçbiri bizde yok) · K3=%12 (2/17: No≈No, Durum≈Durum)
- **Canlı fazlası:** Bu ekranın TEK işi arama/filtreleme; bizde /baf düz bir liste (filtre formu yok). Kullanıcı(personel)/Plaka/Durum/Lokasyon(Aynı Ofis-Farklı Ofis)/Kullanım Amacı/Tarih aralığı/Ofis seçimi ile arama tamamen yok.
- **Not:** Aynı entity'nin (Baf) CRUD'u /baf'ta var (bkz. baf_islemleri satırı) ama bu spesifik ARAMA yeteneği hiç yok → K2=%0, net ❌ YOK.

## baf_islemleri.aspx — Baf İşlemi
- **Durum:** 🟡 KISMİ
- **Bizde:** /baf (`src/RentACar.Web/Components/Pages/Baflar/BafList.razor`)
- **kanit:** /baf · K2=%44 (7/16: personelId≈Musteri_No/Ad/Soyad, vehicleId≈Plaka/Marka/Tipi/Model, cikisTarihi≈Cikis_Tarihi, cikisKm≈Cikis_KM, cikisYakit≈Cikis_Yakit, sube≈Cikis_Sube, aciklama≈Aciklama) · kolon kaynağı `yok` → K3 hesaplanmadı
- **Canlı fazlası (eksik alanlar, tek tek):** Kullanim_Amaci (kullanım amacı select — Araç Ayırma/Dönüşü/Teslimatı/Yıkama vb. 11 seçenek) YOK; Onaylayan (onay iş akışı) YOK; Kiraya_Ver (checkbox) YOK; DropDownListDurum (manuel durum override) YOK; Cikis_Saat/Donus_Saat (saat granülü — bizde sadece tarih) YOK; **dönüş bloğu eksik**: Donus_Tar/Donus_Ofisi/Donus_Yakit YOK (bizde "Teslim Al" formunda sadece donusKm var — dönüş tarihi/ofisi/yakıtı hiç yakalanmıyor, farklı ofise teslim senaryosu takip edilemez).
- **Bizde fazlası:** —
- **Not:** Çekirdek çıkış-akışı (personel+araç+km+yakıt+şube) örtüşüyor ama dönüş tarafı (tarih/ofis/yakıt) ve iş-akışı onayı ciddi eksik.

## bos_arac_listesi.aspx — Boş Araçlar
- **Durum:** 🟡 KISMİ
- **Bizde:** /musaitlik (`src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`)
- **kanit:** /musaitlik · K2=%40 (2/5: Grubu≈grup, Ofis≈sube) · K3=%17 (4/24: Plaka, Marka, Grup, Şube)
- **Canlı fazlası:** Boştaki Süresi (ne kadar süredir boşta), Tipi/Yılı/Yakıt Türü/Vites/Grup Özel Kod/Yakıt(seviye)/Renk/SIPP/Lokasyon/Son Km/Kar Lastiği/Temizlik/Müşteri(son kullanan)/Baş.Tar./Açıklama/Tescil/Ruhsat tarihleri kolonları yok; **doğrudan Kirala/Rezerve aksiyonu** yok (bizde sadece genel /rezervasyonlar linki). TextBox1(Plaka arama) ve Atanan_Plaka (checkbox) filtreleri yok.
- **Bizde fazlası:** Tarih aralığı (from/to) ZORUNLU arama parametresi — canlı ekran tarih aralığı istemeden "şu an boşta olanlar" mantığıyla çalışıyor; bizdeki araç ileriye dönük müsaitlik sorgusu, canlı ise anlık boşta-liste. Fiyat teklifi (Günlük/Toplam/Döviz) gösterimi canlıda bu ekranda yok.
- **Not:** Aynı temel amaç (kiralanabilir boş araç bulma) ama UX mekaniği farklı (anlık snapshot vs tarih-aralığı arama); kolon zenginliği bizde çok daha dar.

## servis_rezervasyon.aspx — Servis Rezervasyonları
- **Durum:** ❌ YOK
- **Bizde:** — (aday bulunamadı)
- **kanit:** — · kolon kaynağı `dom_dx`, K3 hesaplanamadı (bizde eşleşen route yok) · alan=0 (canlıda form alanı da yakalanmamış, muhtemelen salt grid)
- **Canlı fazlası:** Servis için ÖNCEDEN REZERVASYON (Kayıt No, Durum, Baş.Tarih, Bit.Tarih, Plaka) — planlanan ama henüz başlamamış servis randevusu takibi. Bizde /servisler yalnız Açık/Serviste/Tamamlandı durumlarını biliyor; "rezerve edilmiş ama henüz açılmamış" ayrı bir durum/liste yok (`grep`'le "ServisRezervasyon" bulunamadı).
- **Not:** Net eşleşen route yok; /servisler'in ServisDurum enum'ında bu ön-rezervasyon aşaması yer almıyor.

## servis_tanim_tablosu.aspx — Periyodik Bakım Km Listesi
- **Durum:** 🟡 KISMİ
- **Bizde:** /servis-tanimlari (`src/RentACar.Web/Components/Pages/ServisTanimlari/ServisTanimList.razor`)
- **kanit:** /servis-tanimlari · K2=%50 (1/2: TxtPeriyodik_KM≈bakimKm) · K3=%33 (2/6: Açıklama≈Aciklama, Periyodik KM≈BakimKm)
- **Canlı fazlası:** Canlı tablo satırları filodaki GERÇEK Marka+Tipi+Yakıt Türü+Vites kombinasyonlarından otomatik türüyor (her kombinasyon için tek satır, KM alanı inline düzenlenir); bu 4 kolon (Marka/Tipi/Yakıt Türü/Vites) bizde YOK.
- **Bizde fazlası:** Kod (serbest kod alanı), Aktif/Pasif bayrağı, silme aksiyonu — canlıda bu ekranda yok (satırlar silinmiyor, sadece KM güncelleniyor).
- **Not:** Mimari fark: canlı = filodan türetilen otomatik matris + tek alan güncelleme; bizde = elle oluşturulan bağımsız kod tablosu (AracTipi serbest metin, gerçek Marka/Yakıt/Vites kombinasyonuna yapısal olarak bağlı değil).

---

TOPLAM: 25 ekran işlendi
