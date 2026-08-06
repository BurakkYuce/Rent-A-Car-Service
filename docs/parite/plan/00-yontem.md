# EKLEME PLANI AJANI TALİMATI (KOŞU D)

Parite taraması "ne eksik"i belgeledi. Senin işin **"ne yapılmalı"**: her eksik ekrana bir
**desen kodu**, bir **eylem cümlesi** ve bir **eforu** yazmak.

## Girdiler

| Ne | Nerede |
|---|---|
| Eksik ekranlar (senin modülün) | `~/turev-parite-2026-08/plan/eksikler.json` — `modul` alanına göre filtrele |
| Ekran ekran ayrıntı (kanıt, eksik alan listeleri) | `/Users/burak/Desktop/demo-apps/demo/docs/parite/<modul>.md` |
| **Desen kataloğu (D1–D9)** | `/Users/burak/Desktop/demo-apps/demo/docs/parite/10-ekleme-desenleri.md` |
| Mimari reçete | `/Users/burak/Desktop/demo-apps/demo/CLAUDE.md` §5 |
| Bizim kod | `/Users/burak/Desktop/demo-apps/demo/src/` |

## Desen seçimi — katalogtan biri OLMAK ZORUNDA

`D1` basit sözlük master · `D2` kural taşıyan master · `D3` liste/arama (yeni tablo YOK) ·
`D4` rapor (agrega) · `D5` para hareketi yazan · `D6` mega form · `D7` yeni dikey ·
`D8` entegrasyon bağımlı (**kod yazılmaz, kimlik gerekir**) · `D9` erişilebilirlik düzeltmesi.

Seçerken:
- **Veri zaten var, yalnız görünmüyor mu?** → D3. (En ucuz sınıf; önce bunları ara.)
- Yeni tablo gerekiyor ama iş kuralı yok mu? → D1. Kural/bağlama var mı? → D2.
- Defter/tutar YAZIYOR mu? → D5 (adversarial inceleme zorunlu).
- Dış sistem kimliği olmadan imkânsız mı? → D8, efor "bloke".
- Kod var, yalnız menüde/erişimde eksikse → D9.

## Her ekran için üreteceğin satır

```markdown
### <ekran>.aspx
- **desen:** D3
- **eylem:** Mevcut `InvoiceLine` repository'sine kira/rezervasyon bağlamlı satır listesi +
  46 kolonluk grid + cari/tarih/plaka filtresi + Excel export ucu eklenir; yeni tablo YOK.
- **dokunulacak:** `src/.../InvoiceLine*`, yeni `Components/Pages/Finance/FaturaDetayListesi.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok            ← ya da: "D8 — e-Fatura entegratör kimliği gerekir"
```

Kurallar:
1. **Eylem cümlesi somut olacak.** "Alanlar eklenir" değil; hangi alanlar, nereye, hangi ekrana.
2. **Dokunulacak dosyaları gerçekten bak ve yaz.** Tahmin etme; yoksa "yeni dosya" de.
3. **Efor tek birim:** saat ya da gün. Katalogdaki maliyetleri taban al, sapıyorsan sebebini yaz.
4. **D8 ise efor yazma, "bloke — <hangi kimlik>" yaz.** Bu ekranlar iş planına girmez.
5. **PARA durumlu ekranlarda tutar doğruluğuna KARAR VERME** — yapısal eylemi yine yaz
   (ör. "eksik alanlar eklenir"), tutar/formül kararını `PARA — Opus` diye işaretle.
6. Bir ekran gerçekten **yapılmamalıysa** (bilinçli tasarım farkı) bunu yaz: `desen: YAPILMAZ` +
   gerekçe. Örnek: canlının kullanıcı-bazlı 100 menü checkbox'ı — bizde rol+yetki modeli var,
   taklit edilmesi mimari gerileme olur.
7. **Birleştirilebilenleri birleştir:** aynı deseni paylaşan 5 sözlük ekranı tek PR'a girer;
   bunu `**gruplama:** <PR adı>` satırıyla belirt.

## Çıktı

`~/turev-parite-2026-08/plan/<modul>-plan.md`. Dosyanın başına: ekran sayısı, desen dağılımı,
**toplam efor** (bloke olanlar hariç, ayrıca belirt). Sonuna `TOPLAM: <n> ekran planlandı`.
