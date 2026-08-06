# Modül 06 — Fiyat & Sigorta (13 ekran)

**Durum dağılımı:** PARA — OPUS'A DEVİR: 11 · 🟡 KISMİ: 1 · ❌ YOK: 1 · ✅ TAM: 0 · ⚠ ERİŞİLEMEZ: 0 · 🩹 CANLI BOZUK: 0 · ❓ DOĞRULANAMADI: 0

**Not (metodoloji):** Bu modülün 11/13 ekranı tarife/fiyat/kampanya/sigorta-ücret/maliyet sınıfı → kural 5 gereği durum kararı **PARA — OPUS'A DEVİR** olarak sabitlendi (tutar/oran doğruluğu değerlendirilmedi). Asıl iş burada canlı tarife/kural YAPISININ (kademe, kırılım, kolon, kimlik) bizim `/tarife-matris`, `/tarifeler`, `/kira-kurallari`, `/sigorta-urunleri`, `/maliyet-hesapla`, `/doluluk-kurallari`, `/broker-yasaklari` ekranlarıyla karşılaştırılması — bu yüzden PARA satırlarında da `kanit:` alanı dolduruldu ve **Canlı fazlası / Bizde fazlası** alanları asıl bulguyu taşıyor. Bazı canlı profillerde tekrarlayan DevExpress alan çiftleri (aynı etiket için `dxCurrencyEditN` + `txtN`, veya `Chk*`+`Max*`+`*Aciklama`+`*_En` dörtlüsü) tek "kavram" olarak sayıldı; ham `alan=` sayacı bu tekrarları içerdiğinden K2 bazı yerlerde kavram-bazlı (raw sayaçtan farklı) hesaplanmış ve öyle işaretlendi.

---

## broker_musaitlik_listesi.aspx — Broker Araç Müsait Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/musaitlik` (`src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`)
- **kanit:** /musaitlik · K2=%12 (3/26) · K3=%29 (5/17)
- **Canlı fazlası:** SIPP kodu · Drop Bedeli · Provizyon · Km Limiti · Yaş (min sürücü yaşı) · Ehliyet (min ehliyet süresi) · Sube ID (ayrı kolon, Sube Adı'ndan bağımsız) · Bilgi · Rez ID · Doviz seçici (arama formunda) · Analiz (Evet/Hayır) · Rez_Kaynak filtresi · Kira_Gun (gün sayısı serbest metin) · Zaman ("rezervasyon alınma tarihi")
- **Bizde fazlası:** Plaka/Marka bazlı satır (canlı grup/SIPP bazlı toplu satır gösteriyor, araç bazlı değil)
- **Not:** Canlı ekran broker/kanal-yöneticisi (channel manager) beslemesi için grup-bazlı fiyat+kısıt (SIPP/drop/km/yaş/ehliyet) listesi; bizim `/musaitlik` yalnız tarih+grup+şube ile araç bazlı basit müsaitlik+fiyat araması. Broker'a özgü kısıt kolonlarının hiçbiri yok.

## broker_yasaklari.aspx — TürevRent - Broker Yasakları
- **Durum:** 🟡 KISMİ
- **Bizde:** `/broker-yasaklari` (`src/RentACar.Web/Components/Pages/BrokerYasaklari/BrokerYasakList.razor`)
- **kanit:** /broker-yasaklari · K2=%35 (6/17) · K3=❓ (kolon kaynağı yok)
- **Canlı fazlası:** Çoklu Araç Grubu seçimi (`AracGrubu`+`listBox2` — bir yasak kuralına BİRDEN FAZLA grup eklenebiliyor) · Çoklu Şube/Bölge seçimi (`Bolge1`+`listBox` — aynı şekilde çoklu)
- **Bizde fazlası:** Kod/Ad/Açıklama alanları (canlıda görünmüyor, kayıt `Kayit_No` ile anonim) · `TumSatisKapali` ayrı toggle
- **Not:** Çekirdek kavram (kaynak × araç grubu × bölge × tarih × min-gün yasağı) eşleşiyor. Ama canlı bir kuralda AYNI ANDA birden çok araç grubu/şube seçebiliyor (liste kutusu), bizim `AracGrupKod`/`Bolge` tek değerli — aynı işi yapmak için N ayrı kayıt gerekir.

## doluluk_algoritma.aspx — TürevRent (Doluluk/Surge Algoritması)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/doluluk-kurallari` (`src/RentACar.Web/Components/Pages/DolulukFiyat/DolulukFiyatList.razor`)
- **kanit:** /doluluk-kurallari · K2=%42 (10/24, kavram-bazlı) · K3=❓ (kolon kaynağı yok)
- **Canlı fazlası:** SABİT 10 kademe (`Oran_10`..`Oran_100`, her %10 doluluk dilimi için AYRI bir oran alanı — tek kayıtta 10 farklı eşik×çarpan tanımlanıyor) · `Sadece_Kendi_Subeleri` bayrağı
- **Bizde fazlası:** `AracGrupKod` bazında ayrı kural (canlı algoritma şube/bölge bazlı, araç grubu bazlı değil görünüyor) · `GecerlilikBas/Bit` tarih penceresi (canlıda yok)
- **Not:** YAPISAL fark: canlı TEK kayıtta 10 sabit doluluk-dilimi × oran matrisi tutuyor (`Oran_10..Oran_100`); bizim `DolulukFiyatKural` HER kural = TEK eşik + TEK çarpan (kod içi tavan %50 zaten var — CLAUDE.md'de not edilmiş). Yani canlının "10 kademeli tek eğri" modeli yerine bizde "çok satırlı, her satır bir eşik" modeli var — eşdeğer sonuca ulaşılabilir ama veri modeli birebir değil.

## fiyat_grup_tanimlama.aspx — Tarife Grubu
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `—` (karşılık bulunamadı)
- **kanit:** — · K2=%0 (0/8) · K3=%0 (0/3, kaynak dom_th güvenilir)
- **Canlı fazlası:** Tarife Grup Adı + Tarife Oranı + o gruba özel Kullanıcı Adı/Şifre (üçlü: isimlendirilmiş tarife grubu + oran + dış-sistem kimlik bilgisi)
- **Bizde fazlası:** —
- **Not:** Repo genelinde grep edildi — "tarife grubu" adında ayrı bir kimlik/oran/credential kavramı YOK. En yakın "kanal" alanlarımız (`RateMatrix.Kanal`, `RentalRule.Kanal`) serbest metin, kimlik bilgisi taşımıyor. Gerçek bir boşluk.

## fiyat_kampanya_yonetimi.aspx — (başlık yanıltıcı: "Broker Yasakları" — gerçek işlev Kampanya Yönetimi)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/kira-kurallari` (`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`) — kısmi/dolaylı
- **kanit:** /kira-kurallari · K2=❓ (profil neredeyse tamamen UI-iskeleti; 3 alandan ikisi buton/hidden) · K3=— (kolon yok)
- **Canlı fazlası:** "Kampanya Yenile" aksiyonu (ayrı bir kampanya listesi/detay ekranına yönlendiriyor olmalı, extractor yakalayamadı)
- **Bizde fazlası:** —
- **Not:** Profil çok sığ (alan=3, satır=0) — büyük olasılıkla gerçek grid/detay bir DevExpress callback ile geliyor ve statik HTML çıkarımına yakalanmadı. Düşük güven; gerçek işlevi `kampanya_ara.aspx` ile örtüşüyor olabilir.

## kampanya_ara.aspx — TürevRent - Kampanya Ara
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/kira-kurallari` (`src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`)
- **kanit:** /kira-kurallari · K2=%0 (0/6) · K3=— (kolon yok)
- **Canlı fazlası:** Kural Adı ile arama · Tarih Tipi filtresi (Talep Tarihi / Rezervasyon Tarihi) · Durum yaşam-döngüsü: **Planlandı / Aktif / Pasif / Taslak / İptal (5 durum)** · tarih aralığı filtresi
- **Bizde fazlası:** —
- **Not:** `/kira-kurallari`'nde HİÇBİR arama/filtre formu yok (yalnız oluştur + düz liste) → K2=0. Daha önemlisi: canlı kampanyalarda 5 durumlu yaşam döngüsü var; bizim `RentalRule.Aktif` yalnız **Aktif/Pasif (2 durum)** — Planlandı/Taslak/İptal durumları hiç modellenmemiş.

## maliyet_hesaplama.aspx — Filo Maliyet Hesaplama (TCO/teklif formu)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/maliyet-hesapla` (`src/RentACar.Web/Components/Pages/Pricing/MaliyetHesaplama.razor`)
- **kanit:** /maliyet-hesapla · K2=%18 (13/72) · K3=— (kolon yok, form ekranı)
- **Canlı fazlası:** Gider kategorileri TEK TEK itemize: Kasko, Trafik Sigortası, MTV, Bakım (+1 bakım bedeli), Lastik (+kış lastiği), Araç Takip Sistemi, Trafik Tescil/Plaka Masrafları, Muayene-Egzoz Emisyon, Yedek Araç (+yıllık), Yönetim Gideri (+aylık), Enflasyon (+yıllık) — her biri kendi yıllık/birim alanıyla · **Kredi Hesaplama Şekli: Eşit Taksitli / Rotatif seçimi** · Müşteri (Cari_Kod) + Hazırlayan alanı (teklifi bir cariye ve kullanıcıya bağlıyor) · Araç Sayısı (filo adedi) · Banka Dosya ve Diğer Masrafları ayrı alan
- **Bizde fazlası:** —
- **Not:** Kod içi doc-yorumu zaten dürüst: `MaliyetHesapInput` "MAKUL VARSAYIM modeli — canlı kuruş paritesi DEĞİL". Canlı ~15 ayrı gider kalemini tek tek toplarken bizim tek `AylikGider` alanına sıkıştırıyoruz; canlı "Rotatif" kredi tipini de destekliyor, bizde tek faiz modeli var. Ayrıca bizde persist/cari bağlama yok (salt hesap makinesi).

## maliyet_hesaplama_ara.aspx — Kayıtlı Maliyet Hesaplamaları Arama
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `—`
- **kanit:** — · K2=%0 (0/9) · K3=%0 (0/5, kaynak dom_dx güvenilir)
- **Canlı fazlası:** Kayıtlı teklif başlığı + tarih + plaka + araç fiyatı ile ARAMA/listeleme (yani canlıda maliyet hesaplamaları KALICI kayıt)
- **Bizde fazlası:** —
- **Not:** Bizim `/maliyet-hesapla` durumsuz bir hesap makinesi (query-string girdi/çıktı, hiçbir kayıt persist edilmiyor, `Kayit_No` yok). Canlıda olduğu gibi geçmiş hesaplamaları arayıp geri çağırma mekanizması hiç yok — yapısal eksik.

## sigorta_gider_ara.aspx — Sigorta Gider İşlem Listesi
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/giderler` (`src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`)
- **kanit:** /giderler · K2=%6 (1/16) · K3=%30 (6/20)
- **Canlı fazlası:** Sözleşme No + Kira Müşteri kolonları (gideri ilgili kira sözleşmesine/müşterisine izlenebilir kılıyor) · Hazır Açıklama (kanonik açıklama şablonları) · Kalan (kısmi ödenmiş tutarın bakiyesi) · Vergi (Toplam'dan ayrı ayrı kolon) · Marka/Tipi/Yakıt/Vites/Model (araç detay kolonları join'li) · Ofis filtresi · "Sat_Aktif_Sigorta" (sadece aktif sigortalı satış) checkbox filtresi
- **Bizde fazlası:** —
- **Not:** `/giderler` genel amaçlı gider listesi — sigorta'ya özgü filtre (Gider_Adi: Kasko/Trafik) ve sözleşme/müşteri izlenebilirlik kolonları yok. Kolon örtüşmesi (Tür↔Gider Adı, Tarih, Toplam↔TL Toplam, Ödeme) sınırda %30.

## sigorta_muayene.aspx — Sigorta & Muayene & Kasko Raporu
- **Durum:** ❌ YOK
- **Bizde:** `—` (en yakın: `/vade` + `/regulasyon`, ikisi de yetersiz)
- **kanit:** /vade · K2=%9 (1/11) · K3=%8 (2/24)
- **Canlı fazlası:** Araç Belge No · Araç Şasi No · Motor No · Araç Sahibi (isim) · Araç Kimde · **Z-İzni bitiş tarihi** · **Seyrüsefer bitiş tarihi** · Trafik Poliçe No + Trafik Acenta (Kasko'dan AYRI izleniyor) · Kasko Poliçe No + Kasko Acenta (ayrı) · Sigorta Firma / Kasko Firma (ayrı) · Marka/Tipi/Yılı/Yakıt/Vites/Şube/Grup (araç master join'li) · Arac_Sahibi filtresi (Bizim/Dış Araç) · Turu filtresi (Muayene/Trafik/Kasko/Z-İzni/Seyrüsefer)
- **Bizde fazlası:** Kalan Gün (canlıda yok, biz hesaplıyoruz) · Durum bucket rozeti (Geçmiş/≤7g/≤30g)
- **Not:** Canlı ekran kapsamlı bir ARAÇ REGÜLASYON MASTER raporu (belge/şase/motor no, sahiplik, Z-İzni+Seyrüsefer gibi bizde HİÇ modellenmemiş belge türleri dahil); bizim `/vade` sadece basit "Tür/Bitiş/Kalan Gün/Durum" uyarı listesi, `/regulasyon` da tip başına ayrı küçük tablo ama araç master bilgisiyle birleştirilmiş değil ve Poliçe No listede hiç görünmüyor. K2/K3 ikisi de %30 eşiğinin altında.

## sigorta_tarife_listesi.aspx — TürevRent - Sigorta Tarifeleri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/sigorta-urunleri` (`src/RentACar.Web/Components/Pages/CoverageProducts/CoverageProductList.razor`)
- **kanit:** /sigorta-urunleri · K2=%37 (11/30, kavram-bazlı — ham `alan=114` çok tekrarlı Chk/Max/Açıklama/EN dörtlüsünden şişmiş) · K3=— (kolon yok, form ekranı)
- **Canlı fazlası:** Bebek Koltuğu · Navigasyon · Wifi · Çocuk Koltuğu (Bebek Koltuğu'ndan ayrı) · Şarj Cihazı · Adrese Teslim · Kış Lastiği · Üyelik Bedeli · İptal Bedeli · Ek Hizmet 1-2 (adsız genel slot) · **Km Paket 1-4 (kilometre-bazlı paket kademeleri — bizde hiç yok)** · her ürün için ayrı TR/EN açıklama+tanım çifti · "Listeleme Yöntemi: Sadece Parktakiler / Tüm Gruplar"
- **Bizde fazlası:** Döviz alanı ürün bazında (canlıda tek global `Doviz` seçici, ürün bazlı değil) · `Zorunlu` bayrağı (canlıda checkbox = "aktif/pasif" gibi, "zorunlu teminat" ayrı bir kavram değil)
- **Not:** Temel teminat türleri (SCDW/LCF/PAI/IMM/Muafiyet/YolYardım/GençSürücü/MaxGüvence/SuperMini) `CoverageProductType` enum'unda eşleşiyor. Ama canlının "Km Paket 1-4" (kilometre paketi fiyatlaması) kavramı bizde YOK; "Ek Sürücü" ücreti de bu katalogda değil, CLAUDE.md'ye göre bizde ayrı bir mekanizmayla (`RentalAddOn` sistem satırları / `FeeLineService`) yönetiliyor — mimari olarak farklı yerde.

## tarifeler.aspx — TürevRent - Tarifeler
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/tarifeler` (`src/RentACar.Web/Components/Pages/Pricing/RateCardList.razor`)
- **kanit:** /tarifeler · K2=%20 (8/40) · K3=— (kolon yok, form ekranı)
- **Canlı fazlası:** **Gün-kademesi başına KM limiti + KM aşım ücreti** (`Gun1..Gun6` her biri kendi `KmN`+`KmN_Ucret` çiftiyle — kademe hem gün hem km bazlı) · SCDW_Dahil / Mini_Hasar_Dahil / Hırsızlık_Dahil / SCDW_Zorunlu (teminat dahil/zorunlu bayrakları TARİFE SATIRINA gömülü) · `Gosterme` (gizle) checkbox · `Tarife_Grubu` seçici (-/Dönem)
- **Bizde fazlası:** Döviz seçici (TRY/USD/EUR) — canlı tarifeler.aspx'te görünmüyor (muhtemelen ayrı bir global ayar)
- **Not:** En kritik yapısal fark: canlıda her gün-kademesinin KENDİ km limiti + km-aşım-ücreti var (7 kademe × 3 alan); bizim `RateCard` tek `GunlukUcret` + tek `MinGun/MaxGun` aralığı — km-bazlı kademeleme hiç yok. Teminat dahil/zorunlu bayrakları da bizde tarife satırından ayrı (`/sigorta-urunleri` + kira formunda seçim), canlıda tarife satırına gömülü.

## tarifeler_xml.aspx — TürevRent (XML Tarife / Tarife Matrisi)
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/tarife-matris` (`src/RentACar.Web/Components/Pages/RateMatrices/RateMatrixList.razor`)
- **kanit:** /tarife-matris · K2=%56 (14/25, kavram-bazlı) · K3=— (kolon yok, form ekranı)
- **Canlı fazlası:** `Turu` (Fiyat / Kampanya ayrımı — aynı matris satırının türünü belirtiyor, bizde ayrı bir alan yok) · `Kira_Suresi` ("Max Kira Kapsamı" — tarifenin geçerli olduğu maksimum kiralama süresi sınırı) · `Ozel_Lkasyon` çoklu-seçim (liste kutusu) · Onay durumu ayrı iki eksen: `Onay` (Onaylı/Bekliyor) + `Drm` (Aktif/Pasif) — bizde tek `OnayDurumu` (Bekliyor/Onayli) enum'u + ayrı `Aktif` bool, işlevsel eşdeğer ama 2 alan yerine bizde de 2 alan var, örtüşüyor · Log Takip aksiyonu
- **Bizde fazlası:** `GunHaftalik`/`GunAylik` (8-29 / 30+ gün kademeleri — canlı XML tarife ekranında bu iki alan görünmüyor; entity yorumunda "FAZ 3.A1 additive" olarak zaten NOT edilmiş bir bizim-eklentisi)
- **Not:** Bu ekran zaten kod içinde belgelenmiş (`RateMatrix.cs` docstring: "canlı TürevRent XML Tarife / tarifeler_xml karşılığı") — en güçlü yapısal eşleşme bu modülde. Kanal↔Rez_Kaynagi, Şube↔Islem_Sube, Lokasyon↔Ozel_Lkasyon, Bas/Bit Tar, Gün1-7, MaxEsneklik↔Max_Esneklik (kod yorumunda "fiyat GİRDİSİ DEĞİL, salt-görünüm" olarak bilinçli kısıtlanmış), Onaylayan/OnayZaman hepsi birebir örtüşüyor. Eksikler: `Turu` (Fiyat/Kampanya ayrımı) ve `Kira_Suresi` (max kapsam sınırı) bizde yok.

---

TOPLAM: 13 ekran işlendi
