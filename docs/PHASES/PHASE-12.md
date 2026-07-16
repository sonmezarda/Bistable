# Faz 12 — Çekirdek Sağlık: Ayrıştırma, Temizlik, Performans

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §4, §5](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P1/P2 — Faz 14'ün (extension) ön şartı, genel sürdürülebilirlik
**Önkoşul:** sert önkoşul yok; P0/P1 kullanıcı değeri fazlarından sonra
**Hedef:** Rapordaki kod-kalitesi ve performans bulgularını kapatmak; extension
API'sinin üzerine kurulacağı servis sınırlarını çıkarmak.

**Faz kapısı (kabul):**
- `MainWindowViewModel` ≤ ~1.500 satır; worker/hedef/trace yaşam döngüsü
  `SimulationSessionController`'da, testli.
- `ElkGraphBuilder` bölünmüş; primitive endpoint tanımları **tek kaynak**
  (top-scope collector'ları ile inner-compound switch'i aynı tanımdan türetiliyor;
  "iki yerde güncelle" hatası sınıfı ölüyor).
- Pasif router backend'leri karara bağlanmış: silindi **veya** varsayılan
  derleme dışı deneysel projeye taşındı (~4.460 satır ana gövdeden çıkmış).
- Flaky zamanlama testleri karantinada (ayrı seri koleksiyon/kategori) —
  paralel tam koşu **her zaman** yeşil.
- Metin-ölçüm önbelleği ölçülmüş kazançla yerinde; tam test paketi yeşil.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P12-1 | `SimulationSessionController` çıkarımı: build/eval/tick/run/reset/probe yaşam döngüsü + operasyon koordinatörü sahipliği; VM yalnız bağlar (handoff'un 4. kalemi) | 3 g |
| P12-2 | `ProjectService` / `TraceService` çıkarımı (yükleme, VCD, sinyal listeleri) | 2 g |
| P12-3 | `ElkGraphBuilder` bölme: `ElkNodeFactory` / `ElkEndpointCollector` / `ElkGraphPruner` / `ElkIds`+`ElkSignalKeys`; **endpoint meta-tablosu** (primitive → üretir/tüketir port anahtarları) tek veri, iki dünya ondan türer | 3 g |
| P12-4 | `TempFolder` → `VerilatorTempInliner` yeniden adlandırma + `Ast/Passes/` klasörü (Faz 7'nin `CombinationalProjector`'ı ile yan yana) | 0,5 g |
| P12-5 | Ölü backend kararı ve icrası: Graphviz dot/neato + Maze ailesi (öneri: sil; git geçmişi korur) + `SCHEMATIC_ROUTING_BACKENDS.md` güncelle | 1 g |
| P12-6 | Çizim performansı: (metin,punto)→genişlik LRU önbelleği; `EllipsizeToWidth` ikili arama; `static ConnectionRouter` → örnek/DI | 1 g |
| P12-7 | Flaky karantina: `ElkRunnerCancellation*`, `SimulationWorkerClientCancellation*`, `GateSchematicPerformance*` → `[Trait("Category","Timing")]` + seri koleksiyon + yük-toleranslı bütçe | 0,5 g |
| P12-8 | Artımlı VCD index/tailer (handoff kalemi 2) + dalga formu tutma sınırı — canlı döngünün uzun koşularda nefesi | 2 g |
| P12-9 | RTL şematik "Issue 4 Stage 2" (MemoryWritePrimitive — write addr/data/we'nin RAM sembolüne gömülmesi): endpoint meta-tablosu (P12-3) üstünde artık tek yerde tanımlanarak | 1,5 g |

**Toplam tahmin:** ~14,5 gün (bağımsız kalemler; ara sürümlerle parça parça gemilenebilir)

## Kod dokunuş noktaları

- `App/ViewModels/MainWindowViewModel.cs` (4.400 → hedef ≤1.500)
- `App/Services/Routing/Elk/ElkGraphBuilder.cs` (3.369 → 4 dosya)
- Silinen/taşınan: `SchematicPreviewControl.Graphviz.cs`, `Routing/SchematicMazeRouter.cs`,
  `MazeRouter.cs`, `HananGrid.cs`, `GraphvizNeatoSchematicRouter.cs`, `RectilinearSteinerTree.cs`
- `Core/Design/Ast/TempFolder.cs` → `Ast/Passes/VerilatorTempInliner.cs`
- `App/Views/SchematicPreviewControl.Symbols.cs` (ölçüm önbelleği)

## Riskler / notlar

- VM ayrıştırması davranışı değiştirmemeli — karakterizasyon testleri önce
  (mevcut 846'lık ağ büyük oranda yeterli; boşluklar UI-test ile kapatılır).
- Backend silme geri döndürülebilir (git); "deneysel proje" seçeneği yalnız
  sahip Graphviz karşılaştırmasını sürdürmek isterse.
