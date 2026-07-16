# Faz 14 — Platformlaşma: Extension API, UI Modernizasyonu, Dağıtım

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H6](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P3 (uzun vade)
**Önkoşul:** Faz 12 (servis sınırları çıkmış olmalı — API ancak onlardan tanımlanır)
**Hedef:** Bistable'ı "uygulama"dan "platform"a taşımak: üçüncü taraf
extension'lar, modern UI tabanı, sürtünmesiz kurulum.

**Faz kapısı (kabul):**
- Harici bir assembly'den yüklenen **üç örnek extension** çalışıyor:
  (a) şematik overlay sağlayıcı (örn. sinyal-aktivite ısı haritası),
  (b) export sağlayıcı (örn. DOT/JSON), (c) simülasyon gözlemcisi (örn. cycle log).
- `Bistable.Sdk` paketi: kararlı arayüzler + sürümleme politikası + örnek şablon.
- Yeni görünümler XAML ile yazılıyor; en az bir mevcut pencere (pilot:
  `PreferencesWindow`) XAML'e taşınmış.
- elk in-process POC ölçümü rapor edilmiş (Node subprocess'e karşı süre/bellek);
  karar kayıtlı (geç/kal).
- `dotnet publish` profilleri + ilk açılışta bağımlılık denetimi
  (verilator/yosys/node bul-ve-yönlendir UX'i).

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P14-1 | Extension host: `extensions/` klasör keşfi + manifest; `AssemblyLoadContext` izolasyonu; yaşam döngüsü (load/enable/disable), hata karantinası (çöken extension uygulamayı düşürmez) | 3 g |
| P14-2 | API yüzeyi (`Bistable.Sdk`): `ISchematicOverlayProvider`, `ISimulationObserver`, `IExportProvider`, `INetlistImporter`, `IToolPane`; Faz 12 servislerine ince, sürümlü sarmalayıcılar | 3 g |
| P14-3 | Üç örnek extension + şablon repo/`dotnet new` şablonu + geliştirici dokümanı | 2 g |
| P14-4 | XAML politikası: yeni görünümler XAML; pilot taşıma `PreferencesWindow`; stil/tema kaynak sözlüğü çıkarımı | 2 g |
| P14-5 | elk in-process POC: Jint/ClearScript ile elk.js'i süreç-içi koşturma benchmark'ı (RV32 grafiği); karar dokümante | 2 g |
| P14-6 | Dağıtım: publish profilleri (linux-x64/win-x64/osx-arm64), bağımlılık dedektörü + kurulum yönlendirme diyaloğu; sürüm/CHANGELOG düzeni | 2 g |

**Toplam tahmin:** ~14 gün

## Kod dokunuş noktaları

- **Yeni proje:** `src/Bistable.Sdk/` (yalnız arayüzler + DTO'lar; App'e referans YOK)
- **Yeni:** `src/Bistable.App/Extensions/ExtensionHost.cs` + manifest modeli
- `App` içinde overlay/observer/export kancaları (Faz 12 servislerinden)
- `App/Views/PreferencesWindow` → `.axaml`
- Yeni klasör: `extensions/samples/*`

## Riskler / notlar

- API'yi erken dondurma riski: `[Experimental]` işaretiyle başla; 1.0'da sabitle.
- `AssemblyLoadContext` ile Avalonia tip paylaşımı (UI extension'ları) inceliklidir —
  ilk sürümde UI-siz extension türleri (overlay verisi/observer/export) yeterli,
  `IToolPane` ikinci iterasyon olabilir.
- Bu faz kapandığında H6'nın "extension bile destekleyen" cümlesi gerçek olur;
  duyuru/`CONTRIBUTING.md` bu fazın çıktısıyla yazılmalı.
