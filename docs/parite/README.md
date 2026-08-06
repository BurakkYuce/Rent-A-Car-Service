# TürevRent Canlı Parite — kapsam haritası

> **Tarama:** 2026-08-06, canlı `turev2.turevrac.com` (`yucerent` oturumu), **salt-okuma**.
> 156 ekranın tamamı GET ile indirildi; **üretim sistemine tek bir yazma isteği gönderilmedi**.
> **Kapsam notu:** yalnız ekran/alan **yapısı** belgelendi — kimlik bilgisi, çerez, müşteri verisi
> commit EDİLMEDİ. Ham HTML repo dışında tutuldu.

**Önce buraya bakın:** [`YONETICI-OZETI.md`](YONETICI-OZETI.md) — tek sayfa, karar verdirir.

## Bu klasör nasıl okunur

| Dosya | Ne var |
|---|---|
| [`YONETICI-OZETI.md`](YONETICI-OZETI.md) | Hüküm, düzeltilen hatalar, en büyük 5 açık, beklenen kararlar |
| [`00-ekran-listesi.md`](00-ekran-listesi.md) | 156 ekran × (durum, bizdeki karşılık, alan/kolon sayısı) |
| `01-arac-filo.md` … `08-sistem.md` | Ekran ekran ayrıntı: alanlar, kolonlar, **kanıt**, eksikler |
| [`10-ekleme-desenleri.md`](10-ekleme-desenleri.md) | Eksikleri kapatma reçeteleri (D1–D9) + maliyet |
| `09-export-karsilastirma.md` | Excel/PDF export paritesi (2026-07 taraması) |

## Yöntem — neden bu rapora güvenilebilir

1. **Ham HTML deterministik ayrıştırıldı** (Python): form alanları `ctl00$ContentPlaceHolder1$*`,
   grid kolonları `dxgvHeader`, filtreler, aksiyonlar, export düğmeleri, sekmeler. 23 MB ham veri →
   360 KB kompakt profil. LLM ham HTML görmedi.
2. **Kolon setleri kaynak etiketi taşır** (`dom_dx` / `dom_th` / `kolon_secici` / `yok`). Kaynak
   güvensizse karşılaştırma yapılmadı, `❓ DOĞRULANAMADI` yazıldı — tahmin yürütülmedi.
3. **Eşleştirmede kanıt zorunlu:** her satırda `kanit: <rota> · K2=%<alan örtüşmesi> · K3=%<kolon>`.
   İlgili `.razor` dosyası açılmadan "bizde var" yazılamadı. Örtüşme %30'un altındaysa ad benzerliği
   eşleşme sayılmadı.
4. **Tüm `✅ TAM` iddiaları elle doğrulandı** (paydası küçük olanlar dahil — canlıda gerçekten tek
   alan mı vardı, yoksa ayrıştırıcı mı eksik saydı diye tek tek bakıldı).
5. **İki rollü duman testi** (Admin + Operatör, 112 rota): "HTTP 200" tek başına kanıt sayılmadı.

## Kapsam özeti

| Modül | Ekran | ✅ | 🟡 | ❌ | 💰 Para | ⚠ Erişilemez | 🩹 Canlı bozuk | ❓ |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 01 Araç & Filo | 25 | 0 | 7 | 4 | 14 | 0 | 0 | 0 |
| 02 Tanım/Master | 33 | 6 | 6 | 15 | 6 | 0 | 0 | 0 |
| 03 Cari/CRM/Personel/Hukuk | 12 | 0 | 2 | 8 | 2 | 0 | 0 | 0 |
| 04 Kira/Rezervasyon | 11 | 0 | 2 | 0 | 8 | 0 | 1 | 0 |
| 05a Finans — fatura | 6 | 0 | 0 | 0 | 6 | 0 | 0 | 0 |
| 05b Finans — kasa/banka | 30 | 0 | 2 | 12 | 8 | 5 | 3 | 0 |
| 06 Fiyat/Tarife/Sigorta | 13 | 0 | 1 | 1 | 11 | 0 | 0 | 0 |
| 07 Raporlar | 11 | 0 | 7 | 2 | 1 | 0 | 0 | 1 |
| 08 Sistem/Entegrasyon | 15 | 0 | 1 | 6 | 1 | 0 | 4 | 3 |
| **TOPLAM** | **156** | **6** | **28** | **48** | **57** | **5** | **8** | **4** |

**Yorum:** çekirdek operasyon (araç → müşteri → rezervasyon → kira → dönüş → fatura → tahsilat)
uçtan uca çalışıyor. Eksiklerin çoğu *ekranın hiç olmaması* değil, **alan derinliğinin sığ olması**.
En ucuz kazanç `10-ekleme-desenleri.md` içindeki **D3** (liste/arama — veri var, görünmüyor) ve
**D9** (yazılmış ama menüde olmayan 2 rapor) sınıflarında.

## Erişim yöntemi (gelecek oturumlar için)

- Site auth-gated; login JS-hash'li (`Giris1/KeyTrv`) → **curl ile login olunmuyor**.
- Yöntem: kullanıcının tarayıcı oturum çerezi →
  `curl --compressed -H "Cookie: ASP.NET_SessionId=<çerez>; tl=arac" http://turev2.turevrac.com/<sayfa>.aspx`
- Çerez **süreli** → her oturumda kullanıcıdan yenisi istenir. WebFetch çalışmaz (http→https zorlar).
- **`cikis.aspx` listeden çıkarılmalı** — indirme betiği onu çağırırsa oturumu düşürmeye çalışır.
- Grid kolonları **statik HTML'de var** (`dxgvHeader`) → Playwright'a gerek yok; form sayfalarında
  DevExpress yoktur, yalnız liste ekranlarında vardır.
- Canlıda kalıcı bozuk 3 ekran (`banka_para_islem`, `kira_talep_ara`, `dis_banka_entegrasyon_listesi`)
  iki ayrı taramada da 500 verdi — bunlara parite borcu yazılmaz.
