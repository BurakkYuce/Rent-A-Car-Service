"""Angular geçiş roadmap'inin sayfa envanteri: docs/roadmap/F*.md içindeki ENVANTER bloklarını üretir.

Her Blazor sayfası (ve sayfanın `<Ad>Paneller/` klasöründeki alt bileşenleri) için rota, satır sayısı,
`@inject` edilen servisler ve POST form hedefleri çıkarılır. Sayfa → faz ataması aşağıdaki FAZLAR
tablosunda AÇIKÇA yazılıdır; bir sayfa atanmamışsa ya da iki faza atanmışsa betik hata verir.

Neden mekanik: faz sırası kilitli (docs/roadmap/README.md). "Bu faz sonraki bir fazın ucunu bekliyor"
sorusu elle değil, servis ve form hedefi kesişiminden cevaplanır.

Kullanım (repo kökünden):
    python3 scripts/roadmap-envanteri.py            # blokları yazar
    python3 scripts/roadmap-envanteri.py --kontrol  # yazmadan fark varsa 1 ile çıkar

Taban: 2026-09-21 (F0.1). Sayfalar faz kesişlerinde silindikçe bu betik yeniden ÇALIŞTIRILMAZ;
listeler geçişin başlangıç envanteridir ve her fazın Exit'i ("faz listesindeki @page = 0") bu listeye bakar.
"""
from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path

KOK = Path(__file__).resolve().parent.parent
BILESENLER = KOK / "src/RentACar.Web/Components"
SAYFALAR = BILESENLER / "Pages"
ROADMAP = KOK / "docs/roadmap"

BASLA = "<!-- ENVANTER:BASLA — scripts/roadmap-envanteri.py üretir; elle düzenlemeyin -->"
BITIR = "<!-- ENVANTER:BITIR -->"

# Sayfa (Pages/'e göreli yol) → faz. Sıra ve kapsam: docs/roadmap/README.md faz tablosu.
FAZLAR: dict[str, list[str]] = {
    "F4": [
        "Home.razor", "Login.razor",
        "Bookings/RentalList.razor", "Bookings/KiraForm.razor", "Print/RentalPrint.razor",
    ],
    "F5": [
        "Bookings/ReservationList.razor", "Bookings/ReservationCalendar.razor",
        "Availability/MusaitlikArama.razor", "RezSartlar/RezSartList.razor",
        "Bookings/QuotationList.razor", "FiloKiralamalar/FiloKiralamaList.razor",
    ],
    "F6": [
        "Vehicles/VehicleList.razor", "Vehicles/VehicleDetayList.razor", "Vehicles/VehicleEdit.razor",
        "Details/VehicleDetail.razor", "Fleet/FleetStatus.razor",
        "AracSiparisleri/AracSiparisList.razor", "AracKredileri/AracKrediList.razor",
        "MusteriTaksitleri/MusteriTaksitList.razor", "Baflar/BafList.razor",
        "DamageFiles/DamageFileList.razor", "FiloPlan/FiloPlanList.razor",
        "VehicleOwners/VehicleOwnerList.razor", "VehicleTypes/VehicleTypeList.razor",
        "VehicleSegments/VehicleSegmentList.razor",
    ],
    "F7": [
        "Customers/CustomerList.razor", "Customers/CustomerEdit.razor", "Details/CustomerDetail.razor",
        "Crm/SikayetList.razor", "Crm/AssistansTalepList.razor", "Legal/HukukList.razor",
        "Crm/AnketList.razor", "Crm/CrmAnaliz.razor",
    ],
    "F8": [
        "Finance/KasaHub.razor", "Finance/NakitIslem.razor", "Finance/BakiyeDuzeltme.razor",
        "Kur/Kurlar.razor", "Finance/InvoiceList.razor", "Finance/InvoiceLineList.razor",
        "Print/InvoicePrint.razor", "GelenEFaturalar/GelenEFaturaList.razor",
        "Expenses/ExpenseList.razor", "Finance/TopluGider.razor", "VehicleSales/VehicleSaleList.razor",
        "Finance/CariVirman.razor", "Finance/Depozito.razor", "Finance/TopluTahsilat.razor",
        "Finance/TekCariToplu.razor", "Finance/OtomatikTahsilat.razor", "Penalties/PenaltyList.razor",
        "Periods/DonemKapanis.razor", "Customers/CustomerStatement.razor",
    ],
    "F9": [
        "ServiceRecords/ServiceRecordList.razor", "ServisTanimlari/ServisTanimList.razor",
        "Regulation/RegulationList.razor", "Regulation/VadePanosu.razor",
        "Pricing/RateCardList.razor", "RateMatrices/RateMatrixList.razor",
        "TarifeGruplari/TarifeGrubuList.razor", "Import/TarifeAktar.razor",
        "CoverageProducts/CoverageProductList.razor", "RentalRules/RentalRuleList.razor",
        "BrokerYasaklari/BrokerYasakList.razor", "Pricing/QuoteCalculator.razor",
        "Pricing/MaliyetHesaplama.razor", "Pricing/MaliyetTeklifiList.razor",
        "EkHizmetler/EkHizmetList.razor",
    ],
    "F10": [f"Reports/{a}.razor" for a in (
        "AracDurumTakip", "AracGunlukDurum", "AracKarne", "CariBakiye", "DolulukRaporu", "EkHizmetRaporu",
        "ExtreOzeti", "FaturaDonem", "FiloAnaliz", "FiloDoluluk", "FinansAnaliz", "GelirGider",
        "GunlukFaaliyet", "Karlilik", "KarsilastirmaliAnaliz", "KasaBankaDefteri", "KdvListesi", "KmDetay",
        "OtomatikServisler", "PeriyodikServis", "PersonelCalismaTablosu", "RezervasyonKaynak", "ServisOzet",
        "SigortaMuayeneRaporu", "TahsilatFatura", "VirmanGecmisi")],
    "F11": [
        # Tanımlar (genel CRUD bileşeni) — 24
        "Accessories/AccessoryList.razor", "Banks/BankList.razor", "Brands/BrandList.razor",
        "CancelReasons/CancelReasonList.razor", "Countries/CountryList.razor",
        "Currencies/CurrencyList.razor", "CustomCodes/CustomCodeList.razor",
        "CustomerGroups/CustomerGroupList.razor", "Departments/DepartmentList.razor",
        "DolulukFiyat/DolulukFiyatList.razor", "DropTanimlari/DropTanimList.razor",
        "ExpenseCategories/ExpenseCategoryList.razor", "FinancialAccounts/FinancialAccountList.razor",
        "FuelKinds/FuelKindList.razor", "HesapKodlari/HesapKoduList.razor",
        "InsuranceCompanies/InsuranceCompanyList.razor", "KdvRates/KdvRateList.razor",
        "Locations/LocationList.razor", "PaymentTypes/PaymentTypeList.razor",
        "PenaltyTypes/PenaltyTypeList.razor", "ReservationSources/ReservationSourceList.razor",
        "TransmissionTypes/TransmissionTypeList.razor", "VehicleColors/VehicleColorList.razor",
        "VehicleGroups/VehicleGroupList.razor",
        # Sistem — 9
        "Branches/BranchList.razor", "Users/UserList.razor", "Personnel/PersonelList.razor",
        "Settings/Ayarlar.razor", "Notifications/MesajSablonlari.razor", "Settings/BelgeSablonList.razor",
        "Authorization/Yetki.razor", "Audit/AuditList.razor", "Import/IceAktar.razor",
        # Web sitesi + blog + gelen talepler — 8
        "WebSite/WebSiteHub.razor", "WebSite/AracEkle.razor", "WebSite/IlanFiyat.razor",
        "WebSite/IlanOzellik.razor", "WebSite/SiteIcerikYonetim.razor", "Blog/BlogList.razor",
        "Blog/BlogOnizleme.razor", "PublicSite/GelenTalepler.razor",
        # Kabuk sayfaları — 6
        "Notifications/BildirimMerkezi.razor", "Calendar/TakvimAbonelik.razor",
        "Documents/FirmaBelgeleri.razor", "Documents/Dokumanlar.razor", "Search/Ara.razor",
        "Users/SifreDegistir.razor",
    ],
    "F12": [
        "Platform/PlatformLogin.razor", "Platform/TenantConsole.razor", "Platform/TenantDetail.razor",
        "Platform/Belgeler.razor",
    ],
    "F13": ["Error.razor", "NotFound.razor", "DogrulamaHatasi.razor", "Yetkisiz.razor"],
}

# Sayfa olmayan kabuk bileşenleri (Components/'e göreli) → (verisine İHTİYAÇ duyulan faz, SİLİNDİĞİ faz).
# MainLayout'un servisleri (rozet sayıları, kiracı durumu) yeni kabuğa F3.2'de gerekir ama Blazor kabuğu
# F13'e kadar yaşar. İhtiyaç fazı ayrı tutulmazsa F3 servisleri "F11'de açılır" görünür — sıra ihlali.
KABUKLAR: dict[str, tuple[str, str]] = {
    "Layout/PlatformLayout.razor": ("F12", "F12"),
    "Layout/MainLayout.razor": ("F3", "F13"),
}

FAZ_SIRASI = list(FAZLAR)
TUM_SIRA = ["F3", *FAZ_SIRASI]  # kabuk ihtiyacı F3'te başlar; F0-F2 sayfa taşımaz

# Çerçeve servisleri uç ihtiyacı doğurmaz.
CERCEVE = {
    "NavigationManager", "IWebHostEnvironment", "IConfiguration", "IJSRuntime", "IHttpContextAccessor",
    "AuthenticationStateProvider", "IAntiforgery", "IServiceProvider",
}

RE_PAGE = re.compile(r'^@page\s+"([^"]+)"', re.M)
RE_INJECT = re.compile(r'^@inject\s+([\w.<>]+)\s+\w+', re.M)
RE_FORM = re.compile(r"<form\b", re.I)
RE_FORMACTION = re.compile(r'\bformaction\s*=\s*"', re.I)
RE_YOL = re.compile(r'"(/[^"@]*(?:@[^"]*)?)"')


def oku(yol: Path) -> str:
    return yol.read_text(encoding="utf-8").lstrip("﻿")


def etiket_sonu(metin: str, bas: int) -> int:
    """`<form` başlangıcından etiketin kapanan `>`'ına kadar; tırnak ve parantez içindeki `>`'ı atlar."""
    derinlik, tirnak, i = 0, False, bas
    while i < len(metin):
        c = metin[i]
        if c == '"' and derinlik == 0:
            tirnak = not tirnak
        elif c == "(":
            derinlik += 1
        elif c == ")":
            derinlik = max(0, derinlik - 1)
        elif c == ">" and not tirnak and derinlik == 0:
            return i
        i += 1
    return len(metin)


def deger(etiket: str, ad: str) -> str | None:
    """Öznitelik değeri; `@( … )` ifadeleri parantez dengesiyle bütün alınır."""
    m = re.search(rf'\b{ad}\s*=\s*"', etiket, re.I)
    if not m:
        return None
    i = m.end()
    if etiket.startswith("@(", i):
        derinlik, j = 0, i + 1
        while j < len(etiket):
            if etiket[j] == "(":
                derinlik += 1
            elif etiket[j] == ")":
                derinlik -= 1
                if derinlik == 0:
                    return etiket[i:j + 1]
            j += 1
        return etiket[i:]
    son = etiket.find('"', i)
    return etiket[i:son if son >= 0 else len(etiket)]


def normalize(yol: str) -> str:
    yol = yol.split("?")[0].split("#")[0]
    return re.sub(r"@\(?[\w.!]+\)?", "{id}", yol)


def yollar(ifade: str) -> list[str]:
    if ifade.startswith("@("):
        return [normalize(p) for p in re.findall(r'"(/[^"]*)"', ifade)]
    return [normalize(ifade)] if ifade.startswith("/") else []


def post_hedefleri(metin: str) -> set[str]:
    hedefler: set[str] = set()
    for m in RE_FORM.finditer(metin):
        etiket = metin[m.start():etiket_sonu(metin, m.start()) + 1]
        if (deger(etiket, "method") or "get").lower() == "post":
            hedefler.update(yollar(deger(etiket, "action") or ""))
    for m in RE_FORMACTION.finditer(metin):
        bas = metin.rfind("<", 0, m.start())
        etiket = metin[bas:etiket_sonu(metin, bas) + 1]
        if (deger(etiket, "formmethod") or "post").lower() == "post":
            hedefler.update(yollar(deger(etiket, "formaction") or ""))
    return hedefler


def analiz(dosya: Path, gorece: str) -> dict:
    alt = dosya.parent / (dosya.stem + "Paneller")
    parcalar = [dosya, *(sorted(alt.glob("*.razor")) if alt.is_dir() else [])]
    tum = "\n".join(oku(p) for p in parcalar)
    return {
        "dosya": gorece,
        "rotalar": RE_PAGE.findall(oku(dosya)),
        "satir": sum(len(oku(p).splitlines()) for p in parcalar),
        "alt": len(parcalar) - 1,
        "servisler": sorted({s.split(".")[-1] for s in RE_INJECT.findall(tum)} - CERCEVE),
        "hedefler": sorted(post_hedefleri(tum)),
    }


def kabuk_ihtiyaclari() -> dict[str, str]:
    """Kabuk servisi → ihtiyaç fazı (en erken)."""
    sonuc: dict[str, str] = {}
    for k, (ihtiyac, _) in sorted(KABUKLAR.items(), key=lambda x: TUM_SIRA.index(x[1][0])):
        for s in analiz(BILESENLER / k, k)["servisler"]:
            sonuc.setdefault(s, ihtiyac)
    return sonuc


def envanter() -> dict[str, list[dict]]:
    atanan = {s: f for f, ss in FAZLAR.items() for s in ss}
    tekrar = [s for f, ss in FAZLAR.items() for s in ss if sum(s in x for x in FAZLAR.values()) > 1]
    sayfalar = {str(p.relative_to(SAYFALAR)): p for p in sorted(SAYFALAR.rglob("*.razor")) if RE_PAGE.search(oku(p))}
    eksik = sorted(set(sayfalar) - set(atanan))
    fazla = sorted(set(atanan) - set(sayfalar))
    if tekrar or eksik or fazla:
        sys.exit(f"Faz ataması tutarsız.\n  iki faza atanmış: {tekrar}\n  atanmamış: {eksik}\n  dosyası yok: {fazla}")
    sonuc: dict[str, list[dict]] = defaultdict(list)
    for s, p in sayfalar.items():
        sonuc[atanan[s]].append(analiz(p, s))
    for k, (_, silinme) in KABUKLAR.items():
        kayit = analiz(BILESENLER / k, k)
        kayit["rotalar"] = ["(kabuk)"]
        sonuc[silinme].append(kayit)
    return sonuc


def blok(faz: str, env: dict[str, list[dict]]) -> str:
    sira = FAZ_SIRASI.index(faz)
    kayitlar = sorted(env.get(faz, []), key=lambda k: k["dosya"])
    onceki_servis: dict[str, str] = {
        s: f for s, f in kabuk_ihtiyaclari().items() if TUM_SIRA.index(f) < TUM_SIRA.index(faz)
    }
    for f in FAZ_SIRASI[:sira]:
        for k in env.get(f, []):
            for s in k["servisler"]:
                onceki_servis.setdefault(s, f)
    hedef_fazlari: dict[str, set[str]] = defaultdict(set)
    for f, ks in env.items():
        for k in ks:
            for h in k["hedefler"]:
                hedef_fazlari[h].add(f)

    satirlar = [BASLA, ""]
    sayfa_sayisi = sum(1 for k in kayitlar if k["rotalar"] != ["(kabuk)"])
    rota_sayisi = sum(len(k["rotalar"]) for k in kayitlar if k["rotalar"] != ["(kabuk)"])
    satirlar += [f"**Sayfa:** {sayfa_sayisi} dosya · {rota_sayisi} `@page` rotası · "
                 f"{sum(k['satir'] for k in kayitlar):,} satır".replace(",", "."), ""]
    satirlar += ["| Dosya (`Components/Pages/`) | Rota | Satır | Servisler | POST hedefi |", "|---|---|---|---|---|"]
    for k in kayitlar:
        alt = f" (+{k['alt']} alt bileşen)" if k["alt"] else ""
        rota = "<br>".join(f"`{r}`" for r in k["rotalar"])
        satirlar.append(f"| `{k['dosya']}`{alt} | {rota} | {k['satir']} | {', '.join(k['servisler']) or '—'} | {len(k['hedefler'])} |")

    yeni = sorted({s for k in kayitlar for s in k["servisler"]} - set(onceki_servis))
    hazir = sorted({s for k in kayitlar for s in k["servisler"]} & set(onceki_servis))
    satirlar += ["", "**Servis ihtiyacı** (her servis, onu ilk kullanan fazda `/api/ui/v1` ucuna kavuşur):", ""]
    satirlar.append(f"- Bu fazda ilk kez ({len(yeni)}): " + (", ".join(f"`{s}`" for s in yeni) or "—"))
    satirlar.append(f"- Önceki fazda açılmış ({len(hazir)}): "
                    + (", ".join(f"`{s}` ({onceki_servis[s]})" for s in hazir) or "—"))

    hedefler = sorted({h for k in kayitlar for h in k["hedefler"]})
    satirlar += ["", f"**Blazor POST uçları** ({len(hedefler)}). Bir uç, onu kullanan son fazın kesişinde silinir:", ""]
    if hedefler:
        satirlar += ["| Uç | Kullanan fazlar | Silineceği kesiş |", "|---|---|---|"]
        for h in hedefler:
            fazlar = sorted(hedef_fazlari[h], key=FAZ_SIRASI.index)
            son = fazlar[-1]
            satirlar.append(f"| `{h}` | {', '.join(fazlar)} | {son}{' (bu faz)' if son == faz else ''} |")
    else:
        satirlar.append("—")
    satirlar += ["", BITIR]
    return "\n".join(satirlar)


def kabuk_blok() -> str:
    """F3.md: kabuğun (MainLayout) veri ihtiyacı — F3.2'de karşılanır, Blazor kabuğu F13'te silinir."""
    satirlar = [BASLA, "", "**Kabuğun veri ihtiyacı** (Blazor `MainLayout`'un `@inject` satırlarından). "
                "Bu servisler yeni kabukta F3.2'de gerekir; uçları burada açılır ve sonraki fazlarda "
                "\"önceki fazda açılmış (F3)\" olarak görünür:", ""]
    for k, (ihtiyac, silinme) in KABUKLAR.items():
        if ihtiyac != "F3":
            continue
        kayit = analiz(BILESENLER / k, k)
        satirlar.append(f"- `{k}` (Blazor'da {silinme}'e kadar yaşar): "
                        + ", ".join(f"`{s}`" for s in kayit["servisler"]))
    satirlar += ["", BITIR]
    return "\n".join(satirlar)


def main() -> None:
    kontrol = "--kontrol" in sys.argv
    env = envanter()
    toplam = sum(len(k["rotalar"]) for ks in env.values() for k in ks if k["rotalar"] != ["(kabuk)"])
    farkli = []
    uretilen = {"F3": kabuk_blok(), **{faz: blok(faz, env) for faz in FAZ_SIRASI}}
    for faz, icerik in uretilen.items():
        dosya = ROADMAP / f"{faz}.md"
        metin = dosya.read_text(encoding="utf-8")
        if BASLA not in metin or BITIR not in metin:
            sys.exit(f"{dosya}: ENVANTER işaretleri yok.")
        yeni = metin[:metin.index(BASLA)] + icerik + metin[metin.index(BITIR) + len(BITIR):]
        if yeni != metin:
            farkli.append(dosya.name)
            if not kontrol:
                dosya.write_text(yeni, encoding="utf-8")
    print(f"{sum(len(v) for v in FAZLAR.values())} sayfa dosyası, {toplam} @page rotası; "
          f"{'fark var: ' if kontrol else 'güncellenen: '}{farkli or '—'}")
    if kontrol and farkli:
        sys.exit(1)


if __name__ == "__main__":
    main()
