# TürevRent Parite — Yönetici Özeti

**Tarama:** 2026-08-06 · canlı `turev2.turevrac.com` · **salt-okuma** (156 ekran GET ile indirildi;
üretim sistemine tek bir yazma isteği gönderilmedi) · eşleştirme 9 ajan + kanıt zorunluluğu ·
tüm `✅` iddiaları elle doğrulandı.

## 1. Hüküm

**Canlı sistemin 156 ekranından 6'sı bizde tam, 28'i kısmi, 48'i yok; 57 ekran para sınıfı olduğu
için ayrı incelendi.** Ama bu tablo tek başına yanıltıcı: eksiklerin büyük kısmı *ekranın hiç
olmaması* değil, **on yılda birikmiş alan derinliğinin bizde olmaması**. Çekirdek iş akışı
(araç → müşteri → rezervasyon → kira → dönüş → fatura → tahsilat) uçtan uca çalışıyor.

| Durum | Adet | Okuma |
|---|---:|---|
| ✅ Tam | 6 | Basit tanım/master ekranları; bazılarında bizde canlıdan **fazla** alan var |
| 🟡 Kısmi | 28 | Ekran var, derinlik sığ — eksik alanlar modül dosyalarında tek tek listeli |
| ❌ Yok | 48 | Gerçek boşluk (bir kısmı bilinçli tasarım farkı) |
| 💰 Para | 57 | Tutar doğruluğu ayrı incelendi (bkz. §3) |
| ⚠ Erişilemez | 5 | **Kod yazılmış, menüde yok** — kullanıcı ulaşamıyor |
| 🩹 Canlıda bozuk | 8 | Canlı 500/302 veriyor; bize parite borcu yazılmadı |
| ❓ Doğrulanamadı | 4 | Kanıt yetersizdi; tahmin yürütülmedi |

## 2. Bu taramada bulunan ve DÜZELTİLEN hatalar

| Bulgu | Etki | Durum |
|---|---|---|
| **Gün eşiği 2,9 idi, canlıda 3,0** | Kalan süre 2sa54dk–2sa59dk arasındayken müşteriye **bir gün fazla** faturalanıyordu | ✅ PR #141 — canlının kaynağından kalibre edildi, 10 test |
| **`/baf` operatör rolünde HTTP 500** | Operatör menüde gördüğü sayfayı açamıyordu; aynı hata sınıfı ikinci kez tekrarlamış | ✅ PR #140 — doğru metoda geçirildi, guard gevşetilmedi |
| **`SearchTests` zaman bombası** | Sabit tarih geçmişe düşünce suite kod değişmeden kırmızıya döndü | ✅ PR #140 — now-göreli yapıldı |

Gün eşiği hatası **iki rollü duman testi ve canlı kaynak okuması olmasa bulunamazdı**: tek rolle
tarayan biri 500'ü görmez (seed kullanıcı Admin), kayıt bazlı kalibrasyon deneyen biri de canlıda
kira geçmişi olmadığı için hiçbir şey ölçemezdi.

## 3. Para paritesi

Canlı hesapta **kira geçmişi yok** (26 araç, 25 müşteri, 99 ceza var ama `kira_listesi` tek satır ve
tutarları sıfır), fiyat sorgusu da GET ile parametre kabul etmiyor. Bu yüzden kalibrasyon
"geçmiş sözleşmeleri karşılaştır" yerine **canlının kendi formüllerini kaynağından okumak** üzerinden
yapıldı — daha güçlü bir oracle çıktı:

- **Gün hesabı:** canlı `Hizmet_Gun_Bul` + `calculateTimeDifference`; eşik `Saat_Farki_Hesap = 3`,
  kalan süre **dakika duyarlı**. → Bizde düzeltildi, 10 testle kilitlendi.
- **Fatura toplamı:** canlı `Genel_Toplam_Islem` **satır bazında** yuvarlayıp topluyor, toplam
  üzerinden yeniden KDV hesaplamıyor. → Bizim kuralımızla **uyumlu**.
- **ÖTV:** canlıda KDV'si **sabit %20** gömülü, alanlar **3 ondalık** biçimli. → Bizde ÖTV domain'de
  var ama fatura formunda hiç sorulmuyar; karşılıksız.

## 4. En büyük 5 açık

| # | Açık | Desen | Not |
|---|---|---|---|
| 1 | **Fatura ekranı derinliği** — canlıda 100 alan, bizde 6 (vergi dairesi, tevkifat, ÖTV, entegratör, çoklu döviz yok) | D5 | e-Fatura entegrasyonu ayrıca kimlik gerektiriyor (D8) |
| 2 | **Banka hesapları arası virman yapısal olarak imkânsız** — `LedgerAccountType` yalnız Kasa/Banka kovası tutuyor, IBAN bazlı hesap yok | D5 | Ayrıca virman `CashTransaction` yazmadığı için geçmiş virman **hiçbir ekranda listelenemiyor** |
| 3 | **Ayarlar derinliği** — canlıda 148 alan (SMS şablonları, ~45 iş-kuralı anahtarı), bizde ~%8 | D2 | Çoğu bağımsız küçük anahtar; artımlı kapatılabilir |
| 4 | **Rezervasyon ekranı** — canlıda ~400 alan, bizde 16 | D6 | **Bilinçli tasarım**: derinlik "Kiraya Çevir"den sonra geliyor. Karar kullanıcının |
| 5 | **CRM / anket / şikayet / hukuk derinliği** — canlının sözleşme-bağlı 8 soruluk anketi, dönüş-bağlı şikayet akışı bizde jenerik | D7 | Ad benzer, iş farklı — ajanlar bunları eşleştirmedi |

## 5. Bugün 10 dakikada kapanabilecekler

**`⚠ ERİŞİLEMEZ` 5 satırın tamamı iki rotaya işaret ediyor:** `/raporlar/kasa-banka` ve
`/raporlar/servis-ozet`. İkisi de **yazılmış, test edilmiş, çalışıyor** ama `MainLayout.razor`'daki
Raporlar grubuna eklenmemiş — kullanıcı ulaşamıyor. Tek satırlık nav eklemesi (desen **D9**).

## 6. Kullanıcıdan beklenen kararlar

1. **Rezervasyon derinliği** (§4-4): canlının 400 alanlı rezervasyon ekranı taklit edilsin mi, yoksa
   mevcut "ince rezervasyon + zengin kira" tasarımı korunsun mu?
2. **Banka hesap modeli** (§4-2): IBAN bazlı hesap defteri açılsın mı? Açılırsa `LedgerAccountType`
   ve mevcut virman akışı değişir — para yolu, adversarial inceleme gerektirir.
3. **Entegrasyon bağımlı ekranlar** (D8): e-Fatura/GİB, KABİS, e-Devlet ceza sorgu, XML broker, POS —
   hepsi kimlik/credential istiyor. Hangileri açılacak?
4. **Fatura alan derinliği** (§4-1): tevkifat/ÖTV/vergi dairesi alanları e-Fatura'dan bağımsız olarak
   şimdi eklensin mi?

## 7. Bu belgenin sınırları

- Ölçüm **yerel seed veriyle** yapıldı; canlının veri zenginliği farklı.
- `❓ DOĞRULANAMADI` 4 satırda kanıt yetersizdi — "yok" demedik, tahmin de etmedik.
- Canlıda bozuk 8 ekran (500/302) karşılaştırılamadı; ikisi iki ay önceki taramada da bozuktu.
- Ayrıntı ve **her satırın kanıtı** modül dosyalarında: `01-arac-filo.md` … `08-sistem.md`.
