# SIRA — hangi faz, hangi sırayla

`README.md` fazları **modüle göre** listeler (nerede olduğunu bulmak için). Bu dosya **çalışma
sırasını** verir: bağımlılıklara göre topolojik sıralanmış, aynı seviyede ucuz ve risksiz olan öne
alınmış hâli. Yukarıdan aşağı uygula.

## Sıralama kuralı

1. **Önkoşulu olmayan** fazlar önce (topolojik seviye).
2. Aynı seviyede: **FAZ-00** → adversarial gerektirmeyen → karar beklemeyen → ucuz olan.
3. `⚠ KARAR` işaretli fazlar teknik olarak başlanabilir ama **kullanıcı/Opus kararı verilmeden
   kapanmaz** — sırada beklemesinler diye kararları önden toplamak en verimlisi.
4. `🔒 ADV` işaretli fazlar para yolu: Critical/High/Medium adversarial bulgu kalmadan commit yok.

| # | Faz | Ad | Desen | Efor | Kümülatif | İşaret | Önkoşul |
|---:|---|---|---|---:|---:|---|---|
| 1 | [FAZ-00](FAZ-00-bedava-kazanclar.md) | Bedava Kazançlar (wire-in borcu) | D3 — veri var, ekr | 0.5g | 0.5g | — | — |
| 2 | [FAZ-83](FAZ-83-sifre-degistir-self-service.md) | Şifre Değiştirme (Self-Service) | D2 — kural taşıyan | 0.5g | 1.0g | — | — |
| 3 | [FAZ-85](FAZ-85-web-rezervasyon-kolon-derinligi.md) | Web (Acente) Rezervasyonları: Kolon Derinliğ | D3 — liste/arama ( | 0.5g | 1.5g | — | — |
| 4 | [FAZ-25](FAZ-25-rez-sartlari.md) | Rez Şartları (Yeni Küçük Dikey) | D1 — yeni tenant-o | 1.0g | 2.5g | — | — |
| 5 | [FAZ-26](FAZ-26-otomatik-servisler-log.md) | Otomatik Servisler Günlük Log | D2 — yeni küçük ta | 1.0g | 3.5g | — | — |
| 6 | [FAZ-62](FAZ-62-genel-borc-alacak-filtre.md) | Genel Borç/Alacak: Filtre + Ayrı Kolon Derin | D3 | 1.0g | 4.5g | — | — |
| 7 | [FAZ-63](FAZ-63-gider-ara-tanimlama.md) | Gider Arama Filtresi + Gider Tanımlama Mikro | D3 (gider_ara) + D | 1.0g | 5.5g | — | — |
| 8 | [FAZ-65](FAZ-65-hesap-extresi-filtre.md) | Hesap Ekstresi: Filtre/Görünüm-Modu Paneli | D3 | 1.0g | 6.5g | — | — |
| 9 | [FAZ-80](FAZ-80-ayarlar-derinlik-1-ekhizmet-belge.md) | Ayarlar Derinlik PR-1: Ek Hizmet Açıklama/Ma | D2 — kural taşıyan | 1.0g | 7.5g | — | — |
| 10 | [FAZ-20](FAZ-20-sozluk-derinlik-paketi.md) | Basit Sözlük Derinlik Paketi (Araç Grubu + D | D2 — kural taşıyan | 1.5g | 9.0g | — | — |
| 11 | [FAZ-21](FAZ-21-filo-kiralama-derinlik.md) | Filo Kiralama Derinlik + Liste/Arama | D2 (alan derinliği | 1.5g | 10.5g | — | — |
| 12 | [FAZ-52](FAZ-52-fatura-detay-listesi.md) | Fatura Detay Listesi (yeni ekran) | D3 (veri var, görü | 1.5g | 12.0g | — | — |
| 13 | [FAZ-59](FAZ-59-cari-virman-derinlik.md) | Cari Virman: Bilgi Alanları + Toplu Geçmiş L | D2 (bilgi alanları | 1.5g | 13.5g | — | — |
| 14 | [FAZ-61](FAZ-61-extre-ozeti.md) | Extre Özeti (yeni ekran, satır seviyesi) | D4 | 1.5g | 15.0g | — | — |
| 15 | [FAZ-68](FAZ-68-tahsilat-raporu-satir-modu.md) | Tahsilat Raporu: Sözleşme-Satırı Mutabakat M | D4 | 1.5g | 16.5g | — | — |
| 16 | [FAZ-78](FAZ-78-ek-hizmet-raporu-derinligi.md) | Ek Hizmet Raporu Derinliği (Satır-Bazlı Deta | D3 | 1.5g | 18.0g | — | — |
| 17 | [FAZ-81](FAZ-81-ayarlar-derinlik-2-renk-kodlari.md) | Ayarlar Derinlik PR-2: Görsel Tema Renk Kodl | D2 — kural taşıyan | 1.5g | 19.5g | — | — |
| 18 | [FAZ-27](FAZ-27-karsilastirmali-durum-analizi.md) | Karşılaştırmalı Durum Analizi (Yeni Pivot Ra | D4 (kataloğun 1g t | 2.0g | 21.5g | — | — |
| 19 | [FAZ-28](FAZ-28-detayli-arac-listesi.md) | Detaylı Araç Listesi (Konsolide Grid) | D3 (konsolidasyon  | 2.0g | 23.5g | — | — |
| 20 | [FAZ-70](FAZ-70-coklu-secim-kapsam-alanlari.md) | Çoklu-Seçim Kapsam Alanları (Broker Yasaklar | D2 | 2.0g | 25.5g | — | — |
| 21 | [FAZ-72](FAZ-72-tarife-grubu-ve-teminat-alanlari.md) | Tarife Grubu Master + Tarife Teminat/Görünür | D2 | 2.0g | 27.5g | — | — |
| 22 | [FAZ-23](FAZ-23-sube-derinlik.md) | Şube Derinlik Paketi | D2 (büyük derinlik | 3.0g | 30.5g | ‼ RİSK | — |
| 23 | [FAZ-43](FAZ-43-donus-bagli-sikayet.md) | Teslim/Dönüş-Bağlı Şikayet Değerlendirme (Ye | D7 — yeni dikey | 3.0g | 33.5g | — | — |
| 24 | [FAZ-45](FAZ-45-personel-vardiya-raporu.md) | Personel Çalışma/Vardiya Raporu (Yeni Dikey) | D7 — yeni dikey | 3.0g | 36.5g | — | — |
| 25 | [FAZ-77](FAZ-77-filo-doluluk-grafik-derinligi.md) | Filo & Doluluk Grafik Derinliği (Şube Kırılı | D4 | 3.0g | 39.5g | — | — |
| 26 | [FAZ-14](FAZ-14-regulasyon-kismi-odeme.md) | Regülasyon Kısmi Ödeme Genişletmesi (MTV + M | D2 — kural taşıyan | 3.5g | 43.0g | ‼ RİSK | — |
| 27 | [FAZ-22](FAZ-22-lokasyon-drop-derinlik.md) | Lokasyon × Drop Derinlik Paketi | D2 (büyük derinlik | 3.5g | 46.5g | — | — |
| 28 | [FAZ-44](FAZ-44-assistans-talep-takibi.md) | Assistans Talep Takibi (Yol Yardım Mesajları | D7 — yeni dikey | 3.5g | 50.0g | — | — |
| 29 | [FAZ-19](FAZ-19-filo-plan-bos-arac.md) | Filo Plan Yönetimi (Yeni Dikey) + Boş Araç L | D7 (yeni dikey) +  | 4.0g | 54.0g | — | — |
| 30 | [FAZ-40](FAZ-40-cari-personel-alan-derinlik.md) | Cari & Personel Alan Derinliği | D1 (alan ekleme, × | 4.0g | 58.0g | — | — |
| 31 | [FAZ-42](FAZ-42-sozlesme-bagli-anket.md) | Sözleşme-Bağlı Çıkış/Dönüş Anketi (Yeni Dike | D7 — yeni dikey | 4.0g | 62.0g | — | — |
| 32 | [FAZ-75](FAZ-75-sigorta-yuzeyleri-derinligi.md) | Sigorta Yüzeyleri Derinliği (Gider Filtresi  | D3 (`sigorta_gider | 4.0g | 66.0g | — | — |
| 33 | [FAZ-76](FAZ-76-rapor-filtre-derinlik-serisi.md) | Rapor Filtre Derinliği Serisi (5 Rapor Sayfa | D3 (filtre/kolon d | 4.0g | 70.0g | — | — |
| 34 | [FAZ-66](FAZ-66-musteri-taksit-kredi-takip.md) | Müşteri Taksit Takibi (yeni dikey) + AracKre | D7 (yeni dikey) +  | 4.25g | 74.2g | — | — |
| 35 | [FAZ-24](FAZ-24-rez-kaynak-tedarikci-oranlari.md) | Rez. Kaynağı × Tedarikçi Oranları | D2 (yapısal alan+b | 0.75g | 75.0g | ⚠ KARAR | — |
| 36 | [FAZ-31](FAZ-31-xml-fiyat-aktar-canli-izgara.md) | XML Fiyat Aktar: Canlı Izgara + Toplu Silme | D3 (canlı ızgara g | 1.0g | 76.0g | ⚠ KARAR ‼ RİSK | — |
| 37 | [FAZ-10](FAZ-10-arac-kayit-alan-zenginlestirme.md) | Araç Kayıt: Alan Zenginleştirmesi | D1 — basit alan ek | 1.5g | 77.5g | ⚠ KARAR | — |
| 38 | [FAZ-29](FAZ-29-toplu-gider-tahsilat.md) | Toplu Gider + Toplu Tahsilat (Tek-Cari Modu) | D5 — defter yazıyo | 1.5g | 79.0g | ⚠ KARAR ‼ RİSK | — |
| 39 | [FAZ-30](FAZ-30-otomatik-tahsilat-manuel-tetik.md) | Otomatik Tahsilat (Manuel Tetikleme Ekranı) | D5-bitişik (yeni a | 1.5g | 80.5g | ⚠ KARAR ‼ RİSK | — |
| 40 | [FAZ-71](FAZ-71-tarife-km-kademesi.md) | Tarife Km Kademesi (Gün-Kademesi Bazlı KM Li | D2 — **PARA — Opus | 1.5g | 82.0g | ⚠ KARAR | — |
| 41 | [FAZ-13](FAZ-13-arac-kredisi-zenginlestirme.md) | Araç Kredisi Zenginleştirme | D2 (kredi özet ala | 2.0g | 84.0g | ⚠ KARAR | — |
| 42 | [FAZ-41](FAZ-41-hukuk-crm-segment-derinlik.md) | Hukuk Dosyası & CRM Segment Derinliği | D1+D3 (Hukuk alan+ | 2.25g | 86.2g | ⚠ KARAR | — |
| 43 | [FAZ-15](FAZ-15-arac-sigorta-zeyil.md) | Araç Sigorta Zeyil Alt-Sistemi | D5 — para hareketi | 2.5g | 88.8g | ⚠ KARAR | — |
| 44 | [FAZ-82](FAZ-82-ayarlar-derinlik-3-fiyat-is-kurallari.md) | Ayarlar Derinlik PR-3: Fiyat/Muhasebe Parame | D2 — kural taşıyan | 2.5g | 91.2g | ⚠ KARAR | — |
| 45 | [FAZ-18](FAZ-18-arac-satis-baf-derinlik.md) | Araç Satış + Baf: Alan/Filtre Zenginleştirme | D1 (alan ekleme) + | 3.0g | 94.2g | ⚠ KARAR | — |
| 46 | [FAZ-49](FAZ-49-rezervasyon-kaynagi-kural-matrisi.md) | Rezervasyon Kaynağı Kural Matrisi | D2 — kural taşıyan | 3.0g | 97.2g | ⚠ KARAR ‼ RİSK | — |
| 47 | [FAZ-79](FAZ-79-karlilik-gelir-tablosu-genisleme.md) | Karlılık/Gelir Tablosu Çok-Boyutlu Genişleme | D4 — **PARA — Opus | 3.0g | 100.2g | ⚠ KARAR | — |
| 48 | [FAZ-12](FAZ-12-arac-durum-raporlari-derinlik.md) | Araç Durum/Günlük Durum/Gelir-Gider Raporlar | D4 — rapor (agrega | 3.5g | 103.8g | ⚠ KARAR | — |
| 49 | [FAZ-16](FAZ-16-servis-kaydi-derinlik.md) | Servis Kaydı: Kaza/Fatura/Ödeme Derinliği +  | D5 — para hareketi | 4.0g | 107.8g | ⚠ KARAR ‼ RİSK | — |
| 50 | [FAZ-47](FAZ-47-kiralama-megaform-derinlik.md) | Kiralama Mega-Form: Şube/Teslim/Ödeme/Bakiye | D6 — mega-form dev | 4.0g | 111.8g | ⚠ KARAR ‼ RİSK | — |
| 51 | [FAZ-48](FAZ-48-musaitlik-rezervasyon-filtre-derinlik.md) | Müsaitlik & Rezervasyon Filtre+Alan Derinliğ | D3 (×2 ekran grubu | 4.0g | 115.8g | ⚠ KARAR | — |
| 52 | [FAZ-73](FAZ-73-fiyat-motoru-yuzey-genislemeleri.md) | Fiyat Motoru Yüzey Genişletmeleri (Broker Mü | D3 (`broker_musait | 4.0g | 119.8g | ⚠ KARAR | — |
| 53 | [FAZ-74](FAZ-74-maliyet-hesaplama-derinligi.md) | Maliyet Hesaplama Derinliği (Kalem-Bazlı Gir | D2 (`maliyet_hesap | 4.0g | 123.8g | ⚠ KARAR | — |
| 54 | [FAZ-46](FAZ-46-kira-listesi-kiralama-kurallari-derinlik.md) | Kira Listesi & Kiralama Kuralları Derinliği | D3 (kira listesi k | 4.5g | 128.2g | ⚠ KARAR | — |
| 55 | [FAZ-84](FAZ-84-mobil-odeme-kanal-filtresi.md) | Mobil/Tablet Tahsilat: Kanal Filtresi (Yapıs | D3 (yapısal kısım) | 0.5g | 128.8g | ⚠ KARAR 🔒 ADV | — |
| 56 | [FAZ-56](FAZ-56-bakiye-duzeltme.md) | Bakiye Düzeltme (yeni ekran) | D5 (PARA — Opus) | 2.0g | 130.8g | ⚠ KARAR 🔒 ADV ‼ RİSK | — |
| 57 | [FAZ-54](FAZ-54-fatura-listesi-toplu-faturalama.md) | Fatura Listesi: Filtre/Kolon + Toplu Fatural | D3 (filtre/kolon)  | 3.0g | 133.8g | ⚠ KARAR 🔒 ADV | — |
| 58 | [FAZ-51](FAZ-51-fatura-bilgi-para-alanlari.md) | Manuel Fatura Formu: Bilgi Alanları + Vergi/ | D1/D2 (bilgi alanl | 3.5g | 137.2g | ⚠ KARAR 🔒 ADV | — |
| 59 | [FAZ-60](FAZ-60-ceza-trafik-cezasi-derinlik.md) | Trafik Cezası Derinliği: Filtre/Kolon + Çok- | D3 (filtre/kolon)  | 4.0g | 141.2g | ⚠ KARAR 🔒 ADV | — |
| 60 | [FAZ-50](FAZ-50-hesap-bazli-kasa-banka-defteri.md) | Hesap-bazlı (FinancialAccount) Kasa/Banka De | D5 — para/model te | 6.0g | 147.2g | ⚠ KARAR 🔒 ADV ‼ RİSK | — |
| 61 | [FAZ-58](FAZ-58-virman-gecmisi.md) | Virman Geçmişi Listelenebilirliği (Kasa/Bank | D4 | 1.0g | 148.2g | — | FAZ-50 |
| 62 | [FAZ-57](FAZ-57-banka-kasa-hareket-listeleri.md) | Kasa/Banka Hareket Listeleri: Hesap-bazlı Fi | D3 | 2.5g | 150.8g | — | FAZ-50 |
| 63 | [FAZ-11](FAZ-11-arac-gorunurluk-ekranlari.md) | Araç Görünürlük Ekranları: Aksiyon Konsolu + | D3 — liste/arama ( | 3.5g | 154.2g | — | FAZ-10 |
| 64 | [FAZ-17](FAZ-17-arac-siparis-cari-fk-fiyat.md) | Araç Sipariş: Cari-FK + Çok-Katmanlı Fiyat + | D2 (Cari-FK + fiya | 2.5g | 156.8g | ⚠ KARAR | FAZ-13, FAZ-13 |
| 65 | [FAZ-67](FAZ-67-nakit-islem-bagimsiz-ekran.md) | Nakit İşlem: Bağımsız Giriş Ekranı + Arama/F | D3 | 1.5g | 158.2g | 🔒 ADV | FAZ-50, FAZ-50, FAZ-50 |
| 66 | [FAZ-64](FAZ-64-gider-islemleri-derinlik.md) | Gider İşlemleri: Bilgi Alanları + Kısmi Ödem | D3 (bilgi alanları | 2.5g | 160.8g | ⚠ KARAR 🔒 ADV | FAZ-50 |
| 67 | [FAZ-53](FAZ-53-fatura-donem-kdv-raporu.md) | Fatura Raporları Derinliği (Dönem + KDV) | D4 (rapor genişlet | 3.5g | 164.2g | — | FAZ-55 |
| 68 | [FAZ-55](FAZ-55-gelen-e-fatura-kdv-kirilim-yansitma.md) | Gelen e-Fatura: KDV Oran Kırılımı + Gider Ba | D2 (oran kırılımı/ | 3.5g | 167.8g | ⚠ KARAR 🔒 ADV ‼ RİSK | FAZ-53, FAZ-53 |


## Okuma notları

- **Toplam:** 68 faz · 167.8 gün.
- **Seviye 0** (60 faz): önkoşulsuz, hemen başlanabilir.
  **Seviye 1** (6 faz) ve **seviye 2+** (2 faz) önündeki fazı bekler.
- **Karar beklemeyen 39 faz** (86.2 gün) hiçbir şey
  sormadan sürülebilir. Kalanı için önce kararlar toplanmalı.
- **`FAZ-50` kritik düğüm:** 5 fazın önkoşulu, yüksek riskli ve içinde bir karar var (geçmiş defter
  kayıtlarına backfill mi, null-toleranslı okuma mı). Finans kolunun yarısı ona bağlı — sıraya
  girmeden önce o karar verilmeli.
- Bu sıra **öneri**, kanun değil. Bir fazı öne almak isterseniz tek kısıt: `Önkoşul` sütunundaki
  fazlar önce bitmiş olmalı.
