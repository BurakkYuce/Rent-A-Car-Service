# Yol Haritası — faz faz

Parite taramasında bulunan her eksik, **tek başına gönderilebilir bir faza** bölündü. Her faz dosyası
kendi kendine yeter: amaç, kanıt, adım adım yapılacaklar (dosya yollu), migration + RLS notu, test
(bağımsız oracle nereden geliyor), exit kriterleri.

| | |
|---|---|
| **Faz sayısı** | 68 |
| **Toplam efor** | ~168 gün (≈ 34 iş-haftası, tek geliştirici) |
| **Zorunlu adversarial inceleme** | 21 faz (para yolu) |
| **Yüksek riskli** | 12 faz |
| **Bağımlılığı olan** | 12 faz |
| **Bloke (kimlik gerekir)** | bkz. [`BLOKE.md`](BLOKE.md) |
| **Yapılmaz (gerekçeli karar)** | 12 tam + 1 kısmi → [`YAPILMAZ.md`](YAPILMAZ.md) |

> **Efor notu:** parite raporundaki plan-seviyesi tahmin ~152 gündü; fazlara bölerken 168'e çıktı.
> Fark çoğunlukla finans temelinden (`FAZ-50` 5 → 6 gün, geriye dönük veri kararı dahil) ve
> gruplama sırasında ortaya çıkan ek adımlardan geliyor. Tahminler ilk 3-5 faz bittikten sonra
> yeniden kalibre edilmeli.

## Sıra

**Hangi fazı hangi sırayla yapacağın:** [`SIRA.md`](SIRA.md) — bağımlılıklara göre topolojik
sıralanmış 68 faz, kümülatif eforuyla. Aşağıdaki tablo ise **modüle göre** gruplu (nerede olduğunu
bulmak için).

## Nereden başlanır

1. **[FAZ-00](FAZ-00-bedava-kazanclar.md)** — saatler sürer, migration yok. İçinde bir de **rapor
   doğruluğu** düzeltmesi var: araç kredisi taksitleri bugün hiçbir araca atfedilmiyor.
2. Sonra **düşük riskli D3 fazları** (veri var, ekrana bağlanmamış).
3. **Para fazlarına** (D5) ancak bağımlı oldukları temel fazlar bittikten sonra girilir —
   özellikle `FAZ-50` beş ayrı fazın önkoşulu.

## Tüm fazlar


### Faz 00 — bedava kazançlar  ·  1 faz  ·  0.50 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [00](FAZ-00-bedava-kazanclar.md) | Bedava Kazançlar (wire-in borcu) | D3 — veri var, ekrana bağl | ~0,5 gün (saatler) | düşük — migration yok, | yok — her şeyden önce yapılabilir |

### Araç & Filo  ·  10 faz  ·  30.00 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [10](FAZ-10-arac-kayit-alan-zenginlestirme.md) | Araç Kayıt: Alan Zenginleştirmesi | D1 — basit alan ekleme | 1,5 gün | düşük — additive nulla | yok |
| [11](FAZ-11-arac-gorunurluk-ekranlari.md) | Araç Görünürlük Ekranları: Aksiyon Konsolu + Liste + | D3 — liste/arama (yeni tab | 3,5 gün (2 + 1 + 0 | düşük — filtre/kolon/a | FAZ-10 (`PasifSebep`/`Konum`/`Taki |
| [12](FAZ-12-arac-durum-raporlari-derinlik.md) | Araç Durum/Günlük Durum/Gelir-Gider Raporları Derinl | D4 — rapor (agrega) | 3,5 gün (1 + 1 + 1 | orta — Bölüm A/B'de ya | yok |
| [13](FAZ-13-arac-kredisi-zenginlestirme.md) | Araç Kredisi Zenginleştirme | D2 (kredi özet alanları) + | 2 gün (1,5 + 0,5) | orta — `AracKredi.Taks | yok |
| [14](FAZ-14-regulasyon-kismi-odeme.md) | Regülasyon Kısmi Ödeme Genişletmesi (MTV + Muayene + | D2 — kural taşıyan master  | 3,5 gün (1,5 + 1 + | yüksek (Bölüm A/B — me | yok |
| [15](FAZ-15-arac-sigorta-zeyil.md) | Araç Sigorta Zeyil Alt-Sistemi | D5 — para hareketi/defter  | 2,5 gün | orta — yeni tablo + RL | yok |
| [16](FAZ-16-servis-kaydi-derinlik.md) | Servis Kaydı: Kaza/Fatura/Ödeme Derinliği + Rezervas | D5 — para hareketi/defter  | 4 gün (3 + 1) | yüksek — fatura/ödeme  | yok |
| [17](FAZ-17-arac-siparis-cari-fk-fiyat.md) | Araç Sipariş: Cari-FK + Çok-Katmanlı Fiyat + Filtre | D2 (Cari-FK + fiyat katman | 2,5 gün | orta — çok-katmanlı fi | FAZ-13 (soft — `KrediNo` alanı ara |
| [18](FAZ-18-arac-satis-baf-derinlik.md) | Araç Satış + Baf: Alan/Filtre Zenginleştirmesi | D1 (alan ekleme) + D3 (fil | 3 gün (1,5 + 1,5) | orta — `VehicleSale` D | yok |
| [19](FAZ-19-filo-plan-bos-arac.md) | Filo Plan Yönetimi (Yeni Dikey) + Boş Araç Listesi Z | D7 (yeni dikey) + D3 (list | 4 gün (3 + 1) | düşük — para yok, yeni | yok |

### Tanım / Master  ·  12 faz  ·  20.25 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [20](FAZ-20-sozluk-derinlik-paketi.md) | Basit Sözlük Derinlik Paketi (Araç Grubu + Döviz + H | D2 — kural taşıyan master  | 1,5 gün (plan 1,75 | düşük — migration var  | yok |
| [21](FAZ-21-filo-kiralama-derinlik.md) | Filo Kiralama Derinlik + Liste/Arama | D2 (alan derinliği) + D3 ( | 1,5 gün | düşük — `FiloKiralama` | yok |
| [22](FAZ-22-lokasyon-drop-derinlik.md) | Lokasyon × Drop Derinlik Paketi | D2 (büyük derinlik — 24+ y | 3,5 gün (Lokasyon  | orta — `DropTanim`'in  | yok (dış faz bağımlılığı yok; faz- |
| [23](FAZ-23-sube-derinlik.md) | Şube Derinlik Paketi | D2 (büyük derinlik — 20+ y | 3 gün | yüksek — birleştirme a | yok |
| [24](FAZ-24-rez-kaynak-tedarikci-oranlari.md) | Rez. Kaynağı × Tedarikçi Oranları | D2 (yapısal alan+buton isk | 0,75 gün | düşük (bu fazda) — ala | yok |
| [25](FAZ-25-rez-sartlari.md) | Rez Şartları (Yeni Küçük Dikey) | D1 — yeni tenant-owned tab | 1 gün | düşük — para/model değ | yok |
| [26](FAZ-26-otomatik-servisler-log.md) | Otomatik Servisler Günlük Log | D2 — yeni küçük tablo + me | 1 gün | düşük — yalnız log yaz | yok |
| [27](FAZ-27-karsilastirmali-durum-analizi.md) | Karşılaştırmalı Durum Analizi (Yeni Pivot Rapor) | D4 (kataloğun 1g tabanının | 2 gün | düşük — **P&L değil**, | yok |
| [28](FAZ-28-detayli-arac-listesi.md) | Detaylı Araç Listesi (Konsolide Grid) | D3 (konsolidasyon — mevcut | 2 gün | düşük-orta — çoğu kolo | yok |
| [29](FAZ-29-toplu-gider-tahsilat.md) | Toplu Gider + Toplu Tahsilat (Tek-Cari Modu) | D5 — defter yazıyor | 1,5 gün (yapısal); | yüksek (para/model) —  | yok |
| [30](FAZ-30-otomatik-tahsilat-manuel-tetik.md) | Otomatik Tahsilat (Manuel Tetikleme Ekranı) | D5-bitişik (yeni arama+tet | 1,5 gün (yapısal a | yüksek (para) — seçili | yok |
| [31](FAZ-31-xml-fiyat-aktar-canli-izgara.md) | XML Fiyat Aktar: Canlı Izgara + Toplu Silme | D3 (canlı ızgara görüntüle | 1 gün (yapısal); t | orta-yüksek — toplu si | yok |

### Cari · CRM · Kira  ·  10 faz  ·  35.25 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [40](FAZ-40-cari-personel-alan-derinlik.md) | Cari & Personel Alan Derinliği | D1 (alan ekleme, ×2 entity | 4 gün | düşük — para/defter ma | yok |
| [41](FAZ-41-hukuk-crm-segment-derinlik.md) | Hukuk Dosyası & CRM Segment Derinliği | D1+D3 (Hukuk alan+liste) + | 2,25 gün | orta — Hukuk'ta yeni ` | yok (Tahsilat/Kalan'ın defter-post |
| [42](FAZ-42-sozlesme-bagli-anket.md) | Sözleşme-Bağlı Çıkış/Dönüş Anketi (Yeni Dikey) | D7 — yeni dikey | 4 gün | orta — yeni child-tabl | yok |
| [43](FAZ-43-donus-bagli-sikayet.md) | Teslim/Dönüş-Bağlı Şikayet Değerlendirme (Yeni Dikey | D7 — yeni dikey | 3 gün | düşük — mevcut `Sikaye | yok |
| [44](FAZ-44-assistans-talep-takibi.md) | Assistans Talep Takibi (Yol Yardım Mesajları, Yeni D | D7 — yeni dikey | 3,5 gün | orta — tamamen yeni en | yok |
| [45](FAZ-45-personel-vardiya-raporu.md) | Personel Çalışma/Vardiya Raporu (Yeni Dikey) | D7 — yeni dikey | 3 gün | orta — tamamen yeni en | yok |
| [46](FAZ-46-kira-listesi-kiralama-kurallari-derinlik.md) | Kira Listesi & Kiralama Kuralları Derinliği | D3 (kira listesi kolon+fil | 4,5 gün (= kira_li | orta — kira listesi ta | yok (kira_listesi'nde Vade/Fatural |
| [47](FAZ-47-kiralama-megaform-derinlik.md) | Kiralama Mega-Form: Şube/Teslim/Ödeme/Bakiye/2.Sürüc | D6 — mega-form devamı | 4 gün (yapısal mad | yüksek — `RentalContra | madde 5 (çok-taraflı bakiye dağıtı |
| [48](FAZ-48-musaitlik-rezervasyon-filtre-derinlik.md) | Müsaitlik & Rezervasyon Filtre+Alan Derinliği | D3 (×2 ekran grubu: müsait | 4 gün (= müsaitlik | düşük — para hesaplama | yok (çok-taraflı bakiye/komisyon k |
| [49](FAZ-49-rezervasyon-kaynagi-kural-matrisi.md) | Rezervasyon Kaynağı Kural Matrisi | D2 — kural taşıyan master  | 3 gün (D2 yapısal  | yüksek — kural matrisi | komisyon/bakiye formülü Opus karar |

### Finans  ·  19 faz  ·  46.25 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [50](FAZ-50-hesap-bazli-kasa-banka-defteri.md) | Hesap-bazlı (FinancialAccount) Kasa/Banka Defteri [T | D5 — para/model temeli (de | 6 gün (5 gün TEMEL | yüksek — mevcut Kasa/B | yok (öncelikli — FAZ-56/57/58/59/6 |
| [51](FAZ-51-fatura-bilgi-para-alanlari.md) | Manuel Fatura Formu: Bilgi Alanları + Vergi/Belge Al | D1/D2 (bilgi alanları) + D | 3,5 gün (1 gün bil | orta — (a) kısmı risks | yok |
| [52](FAZ-52-fatura-detay-listesi.md) | Fatura Detay Listesi (yeni ekran) | D3 (veri var, görünmüyor — | 1,5 gün | düşük — salt-okunur ra | yok |
| [53](FAZ-53-fatura-donem-kdv-raporu.md) | Fatura Raporları Derinliği (Dönem + KDV) | D4 (rapor genişletme) | 3,5 gün (1,5 gün f | düşük — salt-okunur ra | KDV raporunun Alış-KDV kısmı FAZ-5 |
| [54](FAZ-54-fatura-listesi-toplu-faturalama.md) | Fatura Listesi: Filtre/Kolon + Toplu Faturalama | D3 (filtre/kolon) + D5 (to | 3 gün (1 gün filtr | orta — (a) risksiz; (b | yok |
| [55](FAZ-55-gelen-e-fatura-kdv-kirilim-yansitma.md) | Gelen e-Fatura: KDV Oran Kırılımı + Gider Bağlama +  | D2 (oran kırılımı/bağlama) | 3,5 gün (1,5 gün o | yüksek — (b) `GelenEFa | (a) `kdv_raporu.aspx`'in Alış-KDV  |
| [56](FAZ-56-bakiye-duzeltme.md) | Bakiye Düzeltme (yeni ekran) | D5 (PARA — Opus) | 2 gün | yüksek — tek-taraflı g | yok |
| [57](FAZ-57-banka-kasa-hareket-listeleri.md) | Kasa/Banka Hareket Listeleri: Hesap-bazlı Filtre Der | D3 | 2,5 gün (1 gün ban | düşük — salt-okunur fi | FAZ-50 (TEMEL PR-A — hesap-bazlı ` |
| [58](FAZ-58-virman-gecmisi.md) | Virman Geçmişi Listelenebilirliği (Kasa/Banka Virman | D4 | 1 gün | düşük — yeni sorgu, me | FAZ-50 (hesap adının gösterilmesi  |
| [59](FAZ-59-cari-virman-derinlik.md) | Cari Virman: Bilgi Alanları + Toplu Geçmiş Listesi | D2 (bilgi alanları) + D3 ( | 1,5 gün (1 gün car | düşük — mevcut dengeli | yok |
| [60](FAZ-60-ceza-trafik-cezasi-derinlik.md) | Trafik Cezası Derinliği: Filtre/Kolon + Çok-satır +  | D3 (filtre/kolon) + D2 (ço | 4 gün (0,5 gün fil | orta — kısmi ödeme "Ka | yok |
| [61](FAZ-61-extre-ozeti.md) | Extre Özeti (yeni ekran, satır seviyesi) | D4 | 1,5 gün | düşük — salt-okunur ye | yok |
| [62](FAZ-62-genel-borc-alacak-filtre.md) | Genel Borç/Alacak: Filtre + Ayrı Kolon Derinliği | D3 | 1 gün (mutabakat h | düşük — salt-okunur fi | yok |
| [63](FAZ-63-gider-ara-tanimlama.md) | Gider Arama Filtresi + Gider Tanımlama Mikro Düzeltm | D3 (gider_ara) + D1 mikro  | 1 gün (+10 dakika  | düşük — filtre + salt- | yok |
| [64](FAZ-64-gider-islemleri-derinlik.md) | Gider İşlemleri: Bilgi Alanları + Kısmi Ödeme Takibi | D3 (bilgi alanları) + D2/P | 2,5 gün (1 gün bil | orta — kısmi ödemenin  | Spesifik kasa/banka hesabı seçimi  |
| [65](FAZ-65-hesap-extresi-filtre.md) | Hesap Ekstresi: Filtre/Görünüm-Modu Paneli | D3 | 1 gün | düşük — mevcut çekirde | yok |
| [66](FAZ-66-musteri-taksit-kredi-takip.md) | Müşteri Taksit Takibi (yeni dikey) + AracKredi Wire- | D7 (yeni dikey) + D1 mikro | 4,25 gün (4 gün ye | orta — yeni entity/tab | yok |
| [67](FAZ-67-nakit-islem-bagimsiz-ekran.md) | Nakit İşlem: Bağımsız Giriş Ekranı + Arama/Filtre | D3 | 1,5 gün (1 gün nak | düşük — mevcut para-ya | Kasa_Kodu (spesifik hesap seçimi)  |
| [68](FAZ-68-tahsilat-raporu-satir-modu.md) | Tahsilat Raporu: Sözleşme-Satırı Mutabakat Modu | D4 | 1,5 gün ("Mail At" | düşük — salt-okunur ra | yok |

### Fiyat · Tarife · Raporlar  ·  10 faz  ·  29.00 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [70](FAZ-70-coklu-secim-kapsam-alanlari.md) | Çoklu-Seçim Kapsam Alanları (Broker Yasakları + Tari | D2 | 2 gün (`broker_yas | düşük (additive alanla | yok |
| [71](FAZ-71-tarife-km-kademesi.md) | Tarife Km Kademesi (Gün-Kademesi Bazlı KM Limiti + A | D2 — **PARA — Opus** (fiya | 1,5 gün | orta — fiyat motorunun | yok |
| [72](FAZ-72-tarife-grubu-ve-teminat-alanlari.md) | Tarife Grubu Master + Tarife Teminat/Görünürlük Alan | D2 | 2 gün (`fiyat_grup | düşük (additive master | yok (broker-auth kullanım senaryos |
| [73](FAZ-73-fiyat-motoru-yuzey-genislemeleri.md) | Fiyat Motoru Yüzey Genişletmeleri (Broker Müsaitlik  | D3 (`broker_musaitlik_list | 4 gün (`broker_mus | orta — `kampanya_ara.a | yok |
| [74](FAZ-74-maliyet-hesaplama-derinligi.md) | Maliyet Hesaplama Derinliği (Kalem-Bazlı Girdi + Kay | D2 (`maliyet_hesaplama.asp | 4 gün (`maliyet_he | orta — Rotatif kredi h | yok |
| [75](FAZ-75-sigorta-yuzeyleri-derinligi.md) | Sigorta Yüzeyleri Derinliği (Gider Filtresi + Muayen | D3 (`sigorta_gider_ara.asp | 4 gün (`sigorta_gi | düşük — additive alanl | yok |
| [76](FAZ-76-rapor-filtre-derinlik-serisi.md) | Rapor Filtre Derinliği Serisi (5 Rapor Sayfası) | D3 (filtre/kolon derinliği | 4 gün (`bos_arac_r | orta — `periyodik_serv | yok (önce canlı-tarama doğrulaması |
| [77](FAZ-77-filo-doluluk-grafik-derinligi.md) | Filo & Doluluk Grafik Derinliği (Şube Kırılımı + Gün | D4 | 3 gün (`arac_genel | düşük — envanter/dolul | yok |
| [78](FAZ-78-ek-hizmet-raporu-derinligi.md) | Ek Hizmet Raporu Derinliği (Satır-Bazlı Detay + Pers | D3 | 1,5 gün | düşük — additive kolon | yok |
| [79](FAZ-79-karlilik-gelir-tablosu-genisleme.md) | Karlılık/Gelir Tablosu Çok-Boyutlu Genişleme | D4 — **PARA — Opus** (refe | 3 gün | orta — modülün en büyü | yok (yapısal olarak) |

### Sistem  ·  6 faz  ·  6.50 gün

| Faz | Ad | Desen | Efor | Risk | Bağımlılık |
|---|---|---|---|---|---|
| [80](FAZ-80-ayarlar-derinlik-1-ekhizmet-belge.md) | Ayarlar Derinlik PR-1: Ek Hizmet Açıklama/Max-Gün +  | D2 — kural taşıyan master | 1 gün (Grup 1: 0,5 | düşük — yalnız bilgi/k | yok |
| [81](FAZ-81-ayarlar-derinlik-2-renk-kodlari.md) | Ayarlar Derinlik PR-2: Görsel Tema Renk Kodları | D2 — kural taşıyan master | 1,5 gün | düşük-orta — kod değiş | yok |
| [82](FAZ-82-ayarlar-derinlik-3-fiyat-is-kurallari.md) | Ayarlar Derinlik PR-3: Fiyat/Muhasebe Parametreleri  | D2 — kural taşıyan master  | 2,5 gün (Grup 3: 1 | orta — Grup 4'teki gua | Grup 3'ün eşik/varsayılan-değer ka |
| [83](FAZ-83-sifre-degistir-self-service.md) | Şifre Değiştirme (Self-Service) | D2 — kural taşıyan master | 0,5 gün | düşük — ama yetki-yüks | yok |
| [84](FAZ-84-mobil-odeme-kanal-filtresi.md) | Mobil/Tablet Tahsilat: Kanal Filtresi (Yapısal) | D3 (yapısal kısım) — kanal | 0,5 gün (yapısal:  | orta — para-tutan tabl | **PARA — Opus onayı** (kanal etike |
| [85](FAZ-85-web-rezervasyon-kolon-derinligi.md) | Web (Acente) Rezervasyonları: Kolon Derinliği | D3 — liste/arama (yeni tab | 0,5 gün | düşük — salt görüntüle | yok |


## Faz dosyası okuma kılavuzu

- **Desen** → nasıl yapılacağının reçetesi: [`../parite/10-ekleme-desenleri.md`](../parite/10-ekleme-desenleri.md)
- **Kanıt** → o eksiğin canlıda nerede görüldüğü (ölçüm/dosya:satır)
- **Migration** → gerekiyorsa **RLS bloğu ELLE eklenir** (EF üretmez — CLAUDE.md §5)
- **Zorunlu: adversarial inceleme** satırı varsa: Critical/High/Medium bulgu kalmadan commit yok
- **PARA — Opus/kullanıcı kararı** işaretleri: yapısal iş yapılabilir ama tutar/formül kararı
  verilmeden tamamlanmaz

## Kaynaklar

- Ne eksik, kanıtıyla: [`../parite/YONETICI-OZETI.md`](../parite/YONETICI-OZETI.md)
- Ekran ekran ayrıntı: [`../parite/plan/`](../parite/plan/)
- Para formülleri (canlının kaynağından): [`../parite/11-para-kalibrasyon.md`](../parite/11-para-kalibrasyon.md)
