# 03-cari-crm — Eşleştirme Raporu

**Modül:** 03-cari-crm (12 ekran — müşteri/CRM/personel/hukuk/anket)

## Özet
- ✅ TAM: 0
- 🟡 KISMİ: 2
- ❌ YOK: 8
- ⚠ ERİŞİLEMEZ: 0
- 🩹 CANLI BOZUK: 0
- ❓ DOĞRULANAMADI: 0
- PARA — OPUS'A DEVİR: 2

## Metodoloji notu (şeffaflık için)
- **K2 (alan örtüşmesi)** paydası: profildeki "Form alanları" listesinden salt-UI submit butonları (etiketi `— ?` olan `ButtonX`/`btnSaveLayout` vb. — zaten "Aksiyonlar" bölümünde ayrıca sayılıyor) ve bir görünür tarih alanının eşlik eden `MaskedEditExtenderX_ClientState` gizli ikizleri (aynı mantıksal alanın DOM artığı) çıkarılarak "gerçek iş anlamı olan alan" kümesi kuruldu. Bu, örnek şablondaki büyük payda değerleriyle (`41/56` gibi) tutarlı; ham `alan=N` sayısını değil, mantıksal alan sayısını kullandım.
- **K3 (kolon örtüşmesi)**: yalnız `liste` tipi ekranlarda hesaplandı. `form`/`diğer` tipi ekranlarda (musteri_kayit, personel_kayit, hukuk_birimi, personel_calisma_grafigi) canlı tarafta hiç grid yok (kolon=0) — bu, Kural 4'ün hedeflediği "kolon kaynağı güvensiz/başarısız" durumundan farklı bir kategori (yapısal olarak grid kavramı yok). Bu 4 ekranda K3 alanına `yok (form ekranı, grid kavramı yok)` yazdım ve durum kararını salt K2'den verdim; K2 kendisi DOM tabanlı, güvenilir. Para içermeyen bu ekranlardan ikisi (musteri_kayit KISMİ, personel_kayit/personel_calisma_grafigi YOK) net sayısal K2 farkı gösteriyor — bu ayrımı ❓'ya toplayıp kaybetmek yerine kanıtlı bıraktım.
- Bu modülde "Kolon kaynağı: yok" hiçbir `liste` tipi ekranda görülmedi (hepsi `dom_dx` — güvenilir); dolayısıyla Kural 4'ün asıl hedeflediği senaryo (grid var ama kaynağı güvensiz) bu modülde fiilen oluşmadı.
- Durum eşiği tabloya harfiyen uyuldu: K2<%30 → ❌ YOK (K3 ne olursa olsun); K2≥%30 → 🟡 KISMİ; ✅ TAM için ayrıca K3≥%80 şartı.

---

## anket_listesi.aspx — Anket Listesi
- **Durum:** ❌ YOK
- **Bizde:** /anketler (`src/RentACar.Web/Components/Pages/Crm/AnketList.razor`)
- **kanit:** /anketler · K2=%0 (0/7) · K3=%3 (1/36)
- **Canlı fazlası:** Filtre: Cari, Anket Türü (Yapılmayan/Yapılan), Durum (Dönüş/Çıkış), Tarih Listesi+aralık, Çıkış Ofisi — hiçbiri bizde yok. Kolon: ID, Anket Yapan, Cari Bilgi, Telefon, Çıkış Ofisi, Kiraya Veren, Baş./Bit. Tarih, Plaka, Söz. No, Sonuç, **Soru1-8/Cevap1-8/Açıklama1-8 (24 ayrı soru-cevap kolonu)** — bizde hiç yok. Excel export yok bizde.
- **Bizde fazlası:** Kaynak (anketin geldiği kanal — canlının bu ekranında yok), tekil serbest Yorum alanı.
- **Not:** Canlı ekran, belirli bir kira sözleşmesine (Söz No/Plaka) bağlı **8 sorulu yapılandırılmış** çıkış/dönüş anketi; bizim `Anket` varlığı ise sözleşmeden bağımsız, tek puan+yorum'luk genel geri bildirim kaydı. İsim aynı ("anket") ama iş modeli farklı — Kural 3'ün tam örneği.

## hukuk_birimi.aspx — Hukuk Birimi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /hukuk (`src/RentACar.Web/Components/Pages/Legal/HukukList.razor`, create/update formu)
- **kanit:** /hukuk · K2=%47 (7/15) · K3=yok (form ekranı, grid kavramı yok; ayrıca canlı profilinde "Kolon kaynağı: yok" işareti var — kolon karşılaştırması zaten yapılmadı)
- **Canlı fazlası:** Fatura_No_Temp, Avukat-2 (ad/telefon/mail — bizde tek `Avukat` alanı var, 2. avukat yok), Avukat-1 Telefon/Mail (bizde avukat için iletişim alanı yok), **Tahsilat** (tahsil edilen tutar — bizde `HukukDosya`'da hiç yok).
- **Bizde fazlası:** `Tur` (HukukTuru enum — Dava/İcra vb., canlının bu formda karşılığı yok), `Aktif` bayrağı.
- **Not:** `HukukDosya.Tutar` kod yorumunda açıkça "bilgilendirme amaçlı, deftere postlamaz" deniyor; canlıda ayrıca **Tahsilat** (kısmi ödeme) ve dolaylı **Kalan** bakiyesi var — bizde bu iki alan hiç modellenmemiş, yani kısmi tahsilat durumu hiç tutulamıyor. Tutar/tahsilat alanı olduğu için Kural 5 gereği karar Opus'a devredildi.

## hukuk_islem_listesi.aspx — Hukuk İşlem Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** /hukuk (`src/RentACar.Web/Components/Pages/Legal/HukukList.razor`, liste bölümü — aynı sayfa)
- **kanit:** /hukuk · K2=%0 (0/6) · K3=%56 (5/9)
- **Canlı fazlası:** Filtre: Ad Soyad, Tarih aralığı, Fatura No, Dosya No — bizde hiçbir filtre/arama yok (liste ham, filtresiz). Kolon: **Tahsilat**, **Kalan** (bkz. hukuk_birimi notu — bizde bu iki alan hiç yok), Müşteri adı kolon olarak gösterilmiyor (CariId var ama listede isim yok). Excel export yok bizde (envanter: /hukuk export=hayır).
- **Bizde fazlası:** —
- **Not:** Aynı `HukukDosya` varlığının liste görünümü; para alanı (Tutar/Tahsilat/Kalan) içerdiği için Kural 5 gereği Opus'a devredildi. Filtre tarafı sıfır örtüşme (K2=%0) — liste kullanışlılığı canlıya göre zayıf, ayrıca not edildi.

## musteri_crm.aspx — Müşteri CRM
- **Durum:** ❌ YOK
- **Bizde:** /crm (`src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor`)
- **kanit:** /crm · K2=%0 (0/6) · K3=%25 (3/12)
- **Canlı fazlası:** Filtre: Tarih Listesi (Baş./Bit. Tarih), Kiralama Adeti eşiği, Rez. Kaynağı, Çıkış Şube — hiçbiri yok (bizim sayfada filtre/arama arayüzü hiç yok, statik tam-tenant tablo). Kolon: Müşteri Mail, Müşteri Tel, Ortalama Kira Bedeli, Ortalama KM, Doğ.Tar, İlk Kira Zamanı, Hizmet Bedeli — yok. Excel export yok bizde.
- **Bizde fazlası:** `Segment` sınıflandırma kolonu (canlının bu ekranında yok, hesaplanmış); ayrı bir "Personel Çalışma (BAF)" tablosu (araç tahsis sayısı — canlının bu ekranıyla ilgisiz, farklı bir metrik).
- **Not:** Kavramsal olarak "müşteri bazlı kira istatistiği" ortak ama filtre tarafı tamamen sıfır ve canlının zengin ortalama/iletişim kolonları hiç yok.

## musteri_genel_liste.aspx — Cari Listesi
- **Durum:** ❌ YOK
- **Bizde:** /cariler (`src/RentACar.Web/Components/Pages/Customers/CustomerList.razor`)
- **kanit:** /cariler · K2=%25 (2/8) · K3=%12 (5/41)
- **Canlı fazlası:** Filtre: İşlem Tarihi aralığı, Soyad (ayrı alan), Hepsi. Kolon (41'den 36'sı yok): Tel2/Tel3, Mail Adresi, Adres/İlçe/Şehir, İşlem Şube, Bakiye Şube, Önem, Özel Kod, A-Musteri, Uyarı Tarih, Yetkili/Yetkili Tel/Mail (+1,2,3 = toplam 4 set), Müşteri Temsilcisi, Doğum Tarihi, Pasaport No, Vade Gün, Uyarı Nedeni, Entegrasyon Kodu — bunların çoğu **veri modelimizde var ama bu liste görünümünde kolon olarak gösterilmiyor** (bkz. Not).
- **Bizde fazlası:** Kaynak, Kira (adet), Ciro, Son Kira, Excel+CSV+PDF export (canlı sadece Excel).
- **Not:** Önemli nüans: Müşteri Temsilcisi, Doğum Tarihi, Pasaport No, Vade Gün, Uyarı Nedeni, Yetkili kişiler (`Kisiler` — değişken sayıda) alanların hepsi **veritabanında/`CustomerEdit.razor` formunda mevcut**, sadece bu liste ekranının grid'inde sütun olarak yüzeye çıkmıyor. Ayrıca Bakiye alanı `/cariler/{id}/detay` sayfasında var ama bu liste grid'inde yok. Yani gerçek veri kapsamı bu K3 yüzdesinden daha iyi; ancak bu ekranın kendisi (grid+filtre) canlıya göre çok daha yalın — Kural mekanik eşiği (K2<%30) gereği YOK.

## musteri_kayit.aspx — Müşteri Kayıt
- **Durum:** 🟡 KISMİ
- **Bizde:** /cariler/{Id:guid} (`src/RentACar.Web/Components/Pages/Customers/CustomerEdit.razor`)
- **kanit:** /cariler/{Id:guid} · K2=%48 (42/88) · K3=yok (form ekranı, grid kavramı yok; canlı profilinde de "Kolon kaynağı: yok" işareti var)
- **Canlı fazlası (eşleşmeyen mantıksal alanlar, tek tek):** TC_Dogrulama (doğrulama checkbox), Ulke (genel ikamet ülkesi — Ehliyet_Ulke'den ayrı), Tel2 (3. telefon), Adres (Ev Adresi — entity'de var ama formda YOK), Ozel_Kod, Entegrasyon_Kodu, Aciklama, Fatura_Adres_Farkli (evet/hayır toggle), DropDownListRisk_Izin, Bayi_Komisyon, Fatura_Tek_Satir, Dogum_Gunu_Takip, Musteri_Karti_Anonim, Kira_Anonim, Rezervasyon_Anonim, Fatura_Anonim, Cari_Anonim, Ceza_Anonim (6 ayrı KVKK-anonimleştirme bayrağı), Dogum_Yeri, Pasaport_Tar, Pasaport_Yer, Kurumsal_No, Sifre (portal şifresi), Uyari (serbest metin uyarı — Uyari_Nedeni'nden ayrı), Web_Indirim, Kara_Zamani, Islem_Sube (cari hiç şubeye bağlı değil), Bakiye_Gor, Tevkifat_Kodu, Arac_Verilmez, Yas_Ehliyet_Serbest, Fatura_Kiralayan_Isim, Sadece, Merkez_Kurumsal, Broker, Findex_Zorunlu, Is_Adresi, Is_Telefonu, Firma_ID (bağlı firma arama), Kayitli_Il, Kayitli_Ilce, Mahalle_Koy, Seri_No, Cilt_No, Aile_Sira, Sira_No.
- **Bizde fazlası:** Tevkifat Oranı (%, canlıda bu formda sadece kod var, oran yok), KVKK Onay + KVKK Onay Tarihi, Ek Adres, `Kisiler` (değişken sayıda yetkili kişi — canlının bu formunda görünmüyor ama musteri_genel_liste'nin Yetkili1-3 kolonlarıyla aynı işi görüyor).
- **Not:** 88 mantıksal alandan 42'si eşleşiyor (%48) — orta düzey parite. En büyük boşluk: adres/iletişim detayları (Adres formda hiç yok), KVKK-anonimleştirme bayrakları (6 adet), şube bağlantısı ve nüfus-kaydı detayları (Kayıtlı İl/İlçe/Mahalle/Cilt/Sıra No).

## musteri_listesi.aspx — Cari Liste (Analizi)
- **Durum:** 🟡 KISMİ
- **Bizde:** /cariler (`src/RentACar.Web/Components/Pages/Customers/CustomerList.razor`)
- **kanit:** /cariler · K2=%40 (2/5) · K3=%32 (7/22)
- **Canlı fazlası:** Filtre: Durum (Aktif/Pasif/Hepsi) — bizde aktif/pasif filtresi yok. Kolon: Tel2, Tel3, Mail Adresi, Toplam Gün, Ortalama Günlük, Ortalama Km, Önem, Özel Kod, Bakiye, İlk Kira Tar., Toplam Hizmet, Pasaport No, Ülke.
- **Bizde fazlası:** Tür (Bireysel/Kurumsal/Servis rozet — canlı bu ekranda yok), Kaynak, Excel+CSV+PDF export.
- **Not:** Aynı canlı ekran ailesi (musteri_genel_liste ile birlikte) — bizde tek bir `/cariler` rotasına karşılık geliyor. Bu daha "sade" analiz listesi (Toplam Kira/Hizmet/Bakiye/Km) canlının 41-kolonluk tam listesinden farklı; bizim liste ile örtüşmesi (K3=%32) musteri_genel_liste'ye göre (K3=%12) daha yüksek çünkü karşılaştırılan alan seti daha küçük ve temel (Ad, Tel, TC, Kira Adet, Ciro, Uyarı) zaten örtüşüyor. Bakiye kolonu burada da grid'de yok (yalnız `/cariler/{id}/detay`'da).

## musterigelenmesajlar.aspx — Assistans Hizmetleri
- **Durum:** ❌ YOK
- **Bizde:** — (eşleşen rota yok)
- **kanit:** — (kod tabanında "assistans", "gelen mesaj", "yol yardım talebi", "yedek lastik", "araç hareket" terimleriyle grep yapıldı; tek isabet `CoverageProductType.YolYardim` — bu bir sigorta paketi seçeneği enum değeri, talep/mesaj takip ekranı değil) · K2=%0 (0/N, kıyaslanacak rota yok)
- **Canlı fazlası:** Tüm ekran — Mesaj, Kira Kaydı, Plaka, Ad/Soyad, Cep Tel, Zaman, Sözleşme No, Sebep, Yedek Lastik, Araç Hareket kolonları; Plaka+Tarih filtreleri.
- **Bizde fazlası:** —
- **Not:** Bu, kirada olan araçtan/müşteriden gelen yol-yardım tipi talep-mesaj takip ekranı (03-bizim-envanter.tsv'de "mesaj"/"assist" terimiyle de hiç isabet yok). Bilinçli KVKK düşürme değil — sistemimizde bu işlevin hiçbir izi yok.

## personel_calisma_grafigi.aspx — Personel Çalışma Tablosu
- **Durum:** ❌ YOK
- **Bizde:** /crm (`src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor` — "Personel Çalışma (BAF)" bölümü, isim benzerliği var)
- **kanit:** /crm · K2=%0 (0/2) · K3=yok (canlı ekran tipi "diğer/grafik", kolon kaynağı yok)
- **Canlı fazlası:** Filtre: Şube (OfisX), Tarih — bizde hiç yok. Canlı ekran şube+tarih bazlı personel çalışma/vardiya tablosu (grafik tipi).
- **Bizde fazlası:** "Tahsis Sayısı" (personel başına araç/BAF ataması sayısı) — tamamen farklı bir metrik.
- **Not:** İsim çok benzer ("Personel Çalışma") ama iş tamamen farklı: canlı = şube/tarihe göre personel çalışma/vardiya grafiği; bizimki = personelin araç tahsis (BAF) sayısı, tarih/şube filtresi yok. Kural 3'ün klasik örneği — isim benzerliği eşleşme sayılmadı.

## personel_kayit.aspx — Personel
- **Durum:** ❌ YOK
- **Bizde:** /personel (`src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor`, create/update formu)
- **kanit:** /personel · K2=%26 (8/31) · K3=yok (form ekranı, grid kavramı yok; canlı profilinde de "Kolon kaynağı: yok" işareti var)
- **Canlı fazlası:** Adres (Ev Adresi), Ev_Telefonu, Is_Telefonu, Referans, Aciklama, S_Sinif (Sürücü Sınıfı), S_Verilis_Tar/Yer, Cep_Tel, Mail_Adresi, Dogum_Tar/Yeri, Baba_Adi, Anne_Adi, Kimlik_No, Il, Ilce, Mahalle, Cilt_no, Aile_Sira_No, Sira_No, Kan_Grubu, **Gorev_Tanim** (görev/rol — İç Ekip/Yönetici/Operasyon vb., bizde hiç yok), Rac_Tablet_No.
- **Bizde fazlası:** `Kod` (sicil no — canlı alan listesinde doğrudan karşılığı yok), `Maas` (maaş — canlı formda alan olarak görünmüyor; canlıda "Maaş Ekle" ayrı bir buton/akış, muhtemelen ayrı modal — profilde alan olarak yakalanmamış).
- **Not:** 31 mantıksal alandan 8'i eşleşiyor (%26, eşik %30'un altında). En büyük boşluklar: iletişim/adres/nüfus-kaydı bilgileri (canlıda tam, bizde hiç yok) ve **Görev/Rol** (Gorev_Tanim) — personelin unvanı/departmanı bizde hiç tutulmuyor. `Maas` alanı canlının bu form profilinde görünmüyor ama muhtemelen ayrı "Maaş Ekle" akışında — bizim `/personel` formu maaşı direkt aynı formda tutuyor; bu bir para/PII alanı, ileride ayrı bir bordro ekranı taranırsa Opus'a devredilmeli, ama bu ekranın kendi profili üzerinden %26 çıkan yapısal boşluk zaten YOK kararını tek başına belirliyor.

## personel_listesi.aspx — Personel Listesi
- **Durum:** ❌ YOK
- **Bizde:** /personel (`src/RentACar.Web/Components/Pages/Personnel/PersonelList.razor`, liste bölümü — aynı sayfa)
- **kanit:** /personel · K2=%0 (0/2) · K3=%43 (3/7)
- **Canlı fazlası:** Filtre: Personel Ara (metin), Aktif (checkbox) — bizde hiçbir arama/filtre yok (liste her zaman tam ve filtresiz). Kolon: Cep Tel, Mail Adresi, Tc Kimlik No (şifreli tutulsa da listede kolon olarak yok), Rac Tablet.
- **Bizde fazlası:** İşe Giriş tarihi, Durum (Aktif/Pasif) kolonu (canlıda bu bir filtre, kolon değil).
- **Not:** Filtre tarafı sıfır örtüşme (arama kutusu/aktif filtresi hiç yok) K2=%0 → mekanik eşik gereği YOK; kolon tarafında (Ad/Soyad/Şube) kısmi örtüşme var ama Kural tablosu K2'yi belirleyici sayıyor.

## sikayet_listesi.aspx — TürevRent (Şikayet Listesi)
- **Durum:** ❌ YOK
- **Bizde:** /sikayetler (`src/RentACar.Web/Components/Pages/Crm/SikayetList.razor`)
- **kanit:** /sikayetler · K2=%0 (0/4) · K3=%20 (3/15)
- **Canlı fazlası:** Filtre: Müşteri, Ofis, Şikayet Yeri (Kira/Rezervasyon), Şikayet Kanalı — hiçbiri bizde yok. Kolon: Kayit No, Plaka, Turu, Cep Tel, Belge No, Puan, **Cevap** (Cozum'la kısmen eşleşiyor sayıldı), Teslim Alan, Teslim Eden, Özet, Çıkış Ofisi.
- **Bizde fazlası:** —
- **Not:** Canlı ekran, araç **teslim/dönüş** sürecine sıkı bağlı bir şikayet-değerlendirme akışı (Plaka, Teslim Alan/Eden, Puan, Çıkış Ofisi ile); bizim `Sikayet` varlığı ise sözleşmeden bağımsız genel bir şikayet-bileti kaydı (Konu/Detay/Durum/Çözüm). Filtre tarafı sıfır, kolon tarafı da düşük — isim aynı ama iş modeli farklı.

---

TOPLAM: 12 ekran işlendi
