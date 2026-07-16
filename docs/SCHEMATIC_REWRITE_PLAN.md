# Schematic Studio — Profesyonel Seviyeye Çıkarma Planı

> **Hedef**: Bistable projesinin schematic görüntüleme sistemini, Vivado'nun
> schematic viewer'ı seviyesinde profesyonel bir açık kaynak EDA aracı haline
> getirmek. Öncelik: okunabilir, doğru, tutarlı, hızlı route çizimi.

**Doküman tarihi:** 2026-05-21
**Karar verilen yaklaşım (2026-05-21 tarihli, kısmen geçersiz — bkz. §0):**
Saf C# (harici layout/router kütüphanesi yok), feature flag yok (her faz mevcut
implementasyonu temizce değiştirir), geriye dönük uyumluluk hedeflenmiyor, tüm
fazlar (0-7).

---

## 0. Mimari pivot — bu planın güncelliği (2026-07-16 güncellemesi)

> **UYARI:** Aşağıdaki Faz 0-7 yol haritası 2026-05-21'de "saf C# maze router"
> varsayımıyla yazıldı. O tarihten sonra RTL schematic **iki kez backend pivotu**
> yaptı ve bu pivot orijinal faz tablosuna işlenmedi. Bu bölüm, planı gerçek kod
> durumuyla hizalar. Faz metinleri tarihsel kayıt olarak korunuyor; ama "aktif
> mimari" artık aşağıdaki gibidir.

### 0.1 Router backend'i pluggable oldu — saf-C# tek yol değil

`ISchematicRouter` arkasında birden çok backend seçilebilir hale geldi
([SchematicConnectionRouter.cs](../src/Bistable.App/Services/SchematicConnectionRouter.cs)
içindeki `SchematicRoutingEngine` enum'u):

| Engine | Durum | Dosya |
|--------|-------|-------|
| `Elk` | **AKTİF varsayılan** ([SchematicPreviewControl.cs:113-116](../src/Bistable.App/Views/SchematicPreviewControl.cs#L113)) | `Services/Routing/Elk/*`, `SchematicPreviewControl.Elk.cs` |
| `Internal` | Faz 0-5'te yazılan saf-C# maze router — artık RTL'de varsayılan **değil**; karşılaştırma/offline için duruyor | `Services/Routing/MazeRouter.cs`, `HananGrid.cs`, `RectilinearSteinerTree.cs`, `SchematicMazeRouter.cs` |
| `GraphvizDot` | Ara pivot; harici Graphviz'e layout+routing'i birlikte çözdürür | `SchematicPreviewControl.Graphviz.cs` |
| `GraphvizNeato` | Deneysel, önerilmez | `SchematicPreviewControl.Graphviz.cs` |

Referans dokümanı: [SCHEMATIC_ROUTING_BACKENDS.md](SCHEMATIC_ROUTING_BACKENDS.md).

Sonuç: planın "her faz mevcut implementasyonu temizce siler / harici kütüphane
yok" kararı artık geçerli değil. Maze router kodu **silinmedi**, ELK harici bir
layout kernel'i olarak **eklendi**.

### 0.2 Faz durumları — plan tablosu vs. gerçek kod

| Faz | Plandaki iddia | Gerçek kod durumu (2026-07-16) |
|-----|----------------|-------------------------------|
| 0-5 | Tamamlandı (saf-C# maze router hattı üzerinde) | Kod var ama artık **aktif hat değil**; RTL varsayılanı ELK'e taşındı. Maze router fazları tarihsel. |
| 6 — Performans | "henüz başlamadı" | **Fonksiyonel olarak büyük oranda YAPILDI**, ELK hattında farklı isimlerle: async+cancellable layout (`SchematicLayoutService.LayoutAsync` + `LayoutStillRunning`), LRU cache (`GateLevelLayoutCache` + `GateHierarchicalLayoutEngine` fingerprint), viewport culling ([GateSchematicCanvas.cs:479](../src/Bistable.App/Views/GateSchematicCanvas.cs#L479)), Graphviz route geometri cache. Perf ölçüm kaydı: [ELK_ROUTING_PERFORMANCE_ANALYSIS.md](ELK_ROUTING_PERFORMANCE_ANALYSIS.md) (94.8s → 12.6s). |
| 7 — SVG export | "henüz başlamadı" | **Gerçekten yok** — `src/` içinde SVG/export izi yok. Bu madde açık. |

### 0.3 Bu planın kalan geçerliliği

- Faz 6/7 satırlarını "başlamadı" olarak okuma; §0.2 tablosu esas alınmalı.
- RTL schematic mimarisinin güncel doğru kaynağı: bu §0 + `SCHEMATIC_ROUTING_BACKENDS.md`
  + `ELK_ROUTING_PERFORMANCE_ANALYSIS.md`.
- Gate-level viewer'ın durumu ayrı bir hattır: `docs/PHASES/PHASE-6.5.md` ve
  `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md`.
- Aşağıdaki Faz 0-7 metinleri **tarihsel karar/uygulama kaydı** olarak korunuyor.

---

## 1. Mevcut sistem — özet

### 1.1 Dosya haritası

| Sorumluluk | Dosya | Durum |
|------------|-------|-------|
| Schematic UI çizim (immediate-mode) | [src/Bistable.App/Views/SchematicPreviewControl.cs](../src/Bistable.App/Views/SchematicPreviewControl.cs) | 2585 satır, 109 KB — bakım kabusu, parçalanacak |
| Schematic Studio penceresi | [src/Bistable.App/Views/SchematicStudioWindow.cs](../src/Bistable.App/Views/SchematicStudioWindow.cs) | OK |
| Panel/kart yerleşim motoru | [src/Bistable.App/Services/SchematicScopeLayoutEngine.cs](../src/Bistable.App/Services/SchematicScopeLayoutEngine.cs) | Sugiyama yok, sade grid |
| Kart-içi pin sıralama | [src/Bistable.App/Services/SchematicNodeCardLayoutEngine.cs](../src/Bistable.App/Services/SchematicNodeCardLayoutEngine.cs) | Beyan sırası, reorder yok |
| Router wrapper | [src/Bistable.App/Services/SchematicConnectionRouter.cs](../src/Bistable.App/Services/SchematicConnectionRouter.cs) | İçinde ~500 satır ölü kod var (silinecek) |
| Router gerçek implementasyon | [src/Bistable.App/Services/SchematicNetRouter.cs](../src/Bistable.App/Services/SchematicNetRouter.cs) | `GridSchematicRouter`, 4 pass greedy detour |

### 1.2 Routing pipeline (mevcut)

1. `DrawConnectionRoutes` (SchematicPreviewControl.cs:1752) → child layout +
   port anchor'lardan `SchematicConnectionRouteRequest` listesi üretir.
2. `ConnectionRouter.Compute` çağrılır → `GridSchematicRouter` aktiftir.
3. Router şunu yapar:
   - `SchematicGraphBuilder.Build()` ile request'ler `BundleKey`'e göre `SchematicNet`'lere gruplanır.
   - Inline veya Stacked moda göre lane atanır (`BuildInlineLaneSet` / `BuildStackedLaneSet`).
   - Her isteğe 3-bükümlü L/Z path (`BuildInlineRoute` / `BuildStackedRoute` / `BuildPeerLocalRoute`).
   - 4 pass `AvoidObstacles` ile engellerin etrafından dolaşır.
   - `NormalizeOrthogonalPath` ile gereksiz collinear point'leri sıkıştırır.
   - `AddJunctions` (post-hoc fanout split tespiti), `AddBridgeMetadata` (post-hoc crossing).
   - `PlaceLabels` (ring search ile boş yer arar).
4. `DrawScopedConnectionRoute` her segment'i `DrawingContext.DrawLine` ile çizer; bridges arch, junctions dot.

### 1.3 Gözlemlenmiş semptomlar ve kök nedenler

| # | Semptom | Kök neden | Konum |
|---|---------|-----------|-------|
| 1 | Kablolar modül kutusunun altından geçiyor | Obstacle listesi eksik — yalnızca `ChildNodeRects` + `LocalSectionRect`. Current/Parent/ProbeSection obstacle değil. | [SchematicPreviewControl.cs:2072](../src/Bistable.App/Views/SchematicPreviewControl.cs#L2072) |
| 2 | Kablolar birbirine çok yakın geçiyor | Net'ler bağımsız route ediliyor; congestion farkındalığı yok; aynı şeride birden çok net düşüyor | `AvoidObstacles` |
| 3 | Bazı kablolar çizilmiyor / kötü | Greedy detour, 4 pass'te toparlanamayan zincirleme engellerde başarısız | [SchematicNetRouter.cs:287](../src/Bistable.App/Services/SchematicNetRouter.cs#L287) |
| 4 | Aynı pin tarafında "kıvrım demeti" | Port-side spreading yok; çıkış stub'larından hemen sonra aynı X'te bükülüyor | `BuildInlineRoute` |
| 5 | Etiketler görünmüyor | Hardcoded `bool shouldDrawLabel = false` | [SchematicPreviewControl.cs:1860](../src/Bistable.App/Views/SchematicPreviewControl.cs#L1860) |
| 6 | Fanout'lar dağınık | Steiner ağacı yok; aynı net'in N hedefi için N ayrı path | `AddLocalNetRouteRequests` |
| 7 | Gereksiz çapraz geçişler | Sugiyama / crossing minimization yok; child'lar beyan sırasında | `BuildChildRects` |
| 8 | Bridge çorbası | Post-hoc bridge; track assignment yok | `AddBridgeMetadata` |
| 9 | İki paralel router var | `SchematicConnectionRouter` içinde kullanılmayan eski kod | `ComputeInlineRoutes` / `ComputeStackedRoutes` |
| 10 | UI test edilemez halde | 2585 satırlık tek dosya; logic + render + hit-test iç içe | `SchematicPreviewControl.cs` |

### 1.4 İyi yönler — korunacak

- `SchematicNet` / `BundleKey` kavramı doğru tasarlanmış (bus gruplaması).
- Layout → router → renderer ayrımı net (record'lar üzerinden).
- Pan/zoom infrastructure çalışıyor.
- Bridge ve junction metadata zaten model'de.
- Test setleri ([SchematicConnectionRouterTests](../tests/Bistable.Tests/SchematicConnectionRouterTests.cs), [SchematicScopeLayoutEngineTests](../tests/Bistable.Tests/SchematicScopeLayoutEngineTests.cs)) refactor sırasında güvenlik ağı olacak.

### 1.5 Domain modeli (referans)

- `DesignHierarchyNode(InstanceName, ModuleName, HierarchyPath, Children)`
- `DesignModuleDefinition(Metadata, LocalSignals, Instances)`
- `DesignInstanceDefinition(Name, ModuleName, PortConnections)`
- `DesignInstancePortConnection(PortName, SignalName, Direction, PortIndex)`
- `DesignLocalSignal(Name, Width, IsSigned)`

ViewModel tarafı: `HierarchyScopeInstanceViewModel`, `HierarchyScopePortViewModel`, `HierarchyScopeLocalSignalViewModel`, `SignalViewModel`.

---

## 2. Hedeflenen mimari (Vivado seviyesi)

Profesyonel EDA schematic'lerinde standart pipeline:

1. **Placement (yerleşim)** — Hierarchical layered layout, Sugiyama:
   - cycle breaking → rank assignment → crossing minimization (barycenter) → coordinate assignment
   - Port pin'leri rank kenarındaki sıraya göre yeniden sıralanır.
2. **Global routing** — Diyagram routing channel grid'ine bölünür; A* / channel router ile koridor zinciri seçilir; channel kapasitesine bakılır.
3. **Detailed routing / track assignment** — Aynı koridordaki net'lere ayrı track; Steiner ağacı fanout için; junction'lar bilinerek konur.
4. **Post-process** — Bus ribbon, label placement, bridge hops (yalnızca kaçınılmaz crossing'lerde), drag/drop & live re-routing.

Bu mimariyi 8 modüler faz halinde uygulayacağız.

---

## 3. Faz bazlı yol haritası

### Faz 0 — Temizlik, dosya split, obstacle düzeltmesi
**Süre:** 2-3 gün
**Bu fazda ilk şikayetlerin temeli (modül-altı kablo) çözülür.**

**Yapılacaklar:**

1. `SchematicConnectionRouter.cs` içindeki ölü kodu sil:
   - `ComputeInlineRoutes`, `ComputeStackedRoutes`, `AvoidObstacles`,
     `RouteSegmentAroundObstacles`, `FindBlockingObstacle`, `ChooseHorizontalDetourY`,
     `ChooseVerticalDetourX`, ilgili yardımcılar.
   - Sınıf sadece `GridSchematicRouter`'a delege etsin (gerçi onu da Faz 1'de değiştireceğiz).

2. `SchematicPreviewControl.cs` (2585 satır) parçalama:
   - `Views/SchematicPreviewControl.cs` — sadece `Control` türevi, property'ler, event hookup, `Render` orkestrasyonu (~500 satır hedef).
   - `Services/SchematicRenderer.cs` — `DrawConnectionRoutes`, `DrawScopedConnectionRoute`, `DrawRouteBridges`, `DrawRouteJunctions`, modül/kart çizim metotları.
   - `Services/SchematicHitTestService.cs` — `_signalHitTargets`, `_signalReferenceHitTargets`, `_scopeHitTargets`, `_expansionHitTargets` + hit-test metotları.
   - `Services/SchematicViewportController.cs` — pan/zoom state, `ApplyZoomDelta`, `FitToView`, `ResetView`, `FrameActiveScope`, `_viewportPan`, `_viewportZoom`, clamp/overscroll math.
   - `Services/SchematicRoutingRequestBuilder.cs` — `DrawConnectionRoutes`'un içindeki request derleme mantığı (child port anchors → SchematicConnectionRouteRequest list), `AddLocalNetRouteRequests`, `BuildSignalValueLookup`, `ResolveRouteBrush`.

3. `BuildRoutingObstacles` genişletmesi — eksik obstacle'ları ekle:
   ```csharp
   private static IReadOnlyList<Rect> BuildRoutingObstacles(SchematicScopePanelLayout layout)
   {
       List<Rect> obstacles = [];
       obstacles.Add(layout.CurrentNodeRect);            // EKLE
       if (layout.ParentNodeRect is Rect parent)
           obstacles.Add(parent);                          // EKLE
       obstacles.AddRange(layout.ChildNodeRects);
       if (layout.LocalSectionRect is Rect localSection)
           obstacles.Add(localSection);
       obstacles.Add(layout.ProbeSectionRect);           // EKLE
       return obstacles;
   }
   ```
   Ama dikkat: bu obstacle'lar pin çıkışlarını da bloke eder — port stub'larını obstacle'tan tıraşlamak için pin satırı seviyesinde "geçit" tanımla:
   - `SchematicScopePanelLayout`'a `IReadOnlyList<Rect> RoutingObstacles` ve `IReadOnlyList<Rect> PortChannels` ekle.
   - Layout engine, modül kutusunun kenarındaki pin satırları için ince yatay/dikey "geçit" rect'leri üretsin (port'tan ~stubLength + 4 px dışarı). Obstacle list = solid obstacle - port channels (set difference olarak değil, A*'ta path bu kanaldan girebilir şeklinde).

4. `shouldDrawLabel = false` hardcode'unu kaldır — label çizimi aktif olsun.
   Etiketleri yeni router öncesi de göstermek faydalı, zaten `PlaceLabels` çalışıyor.

5. **Regresyon snapshot testleri** ekle ([tests/Bistable.Tests/SchematicConnectionRouterTests.cs](../tests/Bistable.Tests/SchematicConnectionRouterTests.cs)):
   - `RoutesDoNotCrossCurrentNodeRect`
   - `RoutesDoNotCrossParentNodeRect`
   - `RoutesDoNotCrossProbeSectionRect`
   - `PortChannelAllowsExitFromPinSide`
   - `FanoutNetSharesTrunkSegment` (Faz 2 hazırlık, şimdilik `Skip`)
   - `ParallelNetsKeepMinimumLaneSpacing`

**Tamamlama kriteri:** Tüm testler yeşil, `samples/tiny_cpu` ve `samples/bus_fabric` örneklerinde elle bakınca hiçbir kablo modül/parent/probe rect'inin içinden geçmiyor.

---

### Faz 1 — Congestion-aware maze router
**Süre:** 4-5 gün
**Çıktı:** `GridSchematicRouter` tamamen değiştirilir, `MazeRouter` ile yerinden silinir.

**Yapılacaklar:**

1. Yeni dosya `Services/Routing/HananGrid.cs`:
   - Input: panel rect, obstacle rect'leri, pin noktaları.
   - Output: non-uniform routing grid. Her obstacle kenarından ve pin'den geçen X/Y çizgileri grid hatları olur. Düğümler bu hatların kesişimi.
   - Avantaj: 1000x1000 px panel için ~1M cell'lik uniform grid yerine ~10K node. A* hızı korunur.
   - Cell'ler: `(int X, int Y) GridCell`, koordinat dönüşümü için `ToWorld(GridCell)` ve `FromWorld(Point)`.

2. `Services/Routing/MazeRouter.cs`:
   - A* implementasyonu, Manhattan heuristic.
   - Cost function:
     ```
     g(n) = sum of segment lengths
     + bend_penalty * (bend count)        // bend_penalty = 4 (her köşe pahalı)
     + crossing_penalty * (other net crossings) // 8
     + congestion(cell)                    // önceki net'lerin kullandığı cell'lerde artar
     - prefer_channel_bonus                // koridor merkezlerinde tercih
     ```
   - Obstacle'ları "blocked cell" olarak işaretle; port channel'lar açık.
   - Path reconstruction → ortogonal Manhattan path (sadece 90° dönüş).

3. `Services/Routing/CongestionMap.cs`:
   - Grid cell'i başına kaç net geçtiğini sayar.
   - `MazeRouter`, başarılı her route'tan sonra `Increment(cells)` çağırır.
   - Sonraki route'lar bu cell'lerde +cost görür.

4. **Net ordering**: yüksek fanout net'ler önce, sonra distance, sonra dummy alfabetik. Yeni `Services/Routing/NetOrderPolicy.cs`.

5. **Rip-up & reroute fallback**: Bir net hedefe ulaşamazsa, en yüksek congestion path'i sil ve yeniden dene. Bir kez (sonsuz loop önlemi).

6. `SchematicNetRouter.cs` (yani `GridSchematicRouter` ve eski lane assignment kodu) **tamamen sil**. Yerine yeni `MazeRouter`.
   - `ISchematicRouter` interface'i kalır (DI/test için).
   - `SchematicConnectionRouter` artık doğrudan `MazeRouter`'a delege eder.

7. Mevcut test setini yeni router'a göre güncelle. Önemli olan **davranışsal kontrat**: input/output lane separation, no-backtracking, peer side corridor, bridges, junctions.
   - Bazı eski testler implementation-spesifik koordinatlar bekleyebilir; bunlar daha esnek hale getirilir (topoloji testi: "input route, current rect ile child rect arasında bir X'ten geçer" gibi).

**Tamamlama kriteri:** Aynı `tiny_cpu`/`bus_fabric` örneğinde elle bakınca kablo çakışması yok, tüm net'ler çizili, modüllerin altından geçen yok. Router'ın render süresi 100 net altında <50ms.

---

### Faz 2 — Steiner trees + bus ribbon rendering
**Süre:** 3-4 gün

**Yapılacaklar:**

1. `Services/Routing/RectilinearSteinerTree.cs`:
   - Algoritma: **Sequential Edge-Based** veya basit **Hwang's heuristic**.
   - Input: bir net'in tüm pin'leri (source + multiple destinations).
   - Output: Tree edge listesi (Steiner point'ler dahil).
   - Pseudo:
     ```
     1. Pin'leri Hanan grid'ine yerleştir.
     2. MST hesapla.
     3. Her MST kenarı için L-path'ten birini seç (crossing minimize).
     4. Crossing point'leri Steiner node olarak ekle.
     ```

2. Yeni `SchematicConnectionRoute` rekoruna `Tree` field'ı ekle veya `Junctions`'ı doğal ağaç düğümleriyle besle. Tercih: `Junctions` zaten model'de, doğal Steiner point'ler oraya doğrudan yazılır.

3. Router pipeline değişikliği:
   - `BuildBundleRoutes()` artık bir bundle için tek bir Steiner tree döndürür.
   - Her tree edge maze router ile route edilir, ama trunk segment'leri shared. `MazeRouter.Compute()` artık tek path değil, **tree edge sequence** alır ve sıralı (trunk önce) route eder.

4. **Bus ribbon rendering** (`SchematicRenderer.DrawScopedConnectionRoute`):
   - `width > 1` ise: ana hat + 3 px ofsetli paralel ikinci çizgi → "ribbon" görünümü.
   - Bus tap marker: ribbon'dan bireysel bit ayrıldığı noktada `/` slash (Vivado/KiCad konvansiyonu).

5. **Bus expand/collapse UI**:
   - `Bus[7:0]` üzerine tıklanınca bireysel bit'ler ayrı route olur (8 paralel net gibi).
   - Tekrar tıklanınca toplanır.
   - State: `HashSet<string> _expandedBusKeys` `SchematicPreviewControl`'da.

**Tamamlama kriteri:** Fanout > 2 olan net'ler tek trunk + branch'li çiziliyor. `samples/bus_fabric` örneğinde bus'lar ribbon olarak görünüyor, tap işaretleri doğru noktada.

---

### Faz 3 — Hierarchical layered placement (Sugiyama)
**Süre:** 5-7 gün
**Bu fazda Vivado-benzeri "sol→sağ data flow" görünümü gelir.**

**Yapılacaklar:**

1. `SchematicScopeLayoutEngine.cs` **tamamen yeniden yazılır**. Eski `BuildChildRects` / `BuildChildRectsBelow` kalmaz.

2. Yeni `Services/Layout/HierarchicalLayoutEngine.cs`:
   - **Step 1 — Connectivity graph**: child instance'lar düğüm, port connection'lar yönlü kenar. Boundary input port'lar virtual "source" node, output port'lar virtual "sink" node.
   - **Step 2 — Cycle breaking**: feedback edges'i geçici olarak reverse et (DFS ile back-edge tespiti).
   - **Step 3 — Rank assignment**: longest-path algorithm. Source rank=0, her hedef = max(predecessor.rank) + 1.
   - **Step 4 — Crossing minimization**: barycenter heuristic, 6-10 iterasyon (yukarı/aşağı dolaş). Her rank'taki node'lar komşu rank'tan gelen pin'lerin ortalamasına göre sıralanır.
   - **Step 5 — Coordinate assignment**: Brandes-Köpf'un sade hali. Her rank'a X koordinatı (sabit), her rank içinde Y koordinatı (sıralama * step).

3. **Port pin reordering** (`SchematicNodeCardLayoutEngine` genişletmesi):
   - Yeni parametre `IReadOnlyList<string> PreferredInputOrder`.
   - Bu sıralama, yandaki rank'taki node'lardan gelen kablo Y koordinatlarına göre layout engine'in verdiği hint.
   - Default fallback: beyan sırası.

4. **Expanded scope handling**: Mevcut sistemde scope expand edilince inline çiziliyor. Hierarchical layout'ta bu, "iç içe rank gruplaması" oluyor:
   - Expanded scope = composite node, içinde alt-rank'lar var.
   - Recursive: alt seviye Sugiyama parent'ın hücresi içinde çalışır.
   - Bu kısım karmaşık olabilir — eğer karmaşıklık yüksek çıkarsa expanded scope için klasik centered grid kalır (her parent kendi alt-sayfası).

5. Test ekle ([tests/Bistable.Tests/HierarchicalLayoutEngineTests.cs](../tests/Bistable.Tests/HierarchicalLayoutEngineTests.cs) - yeni):
   - `LinearChainPlacesNodesLeftToRight`
   - `FeedbackEdgeDoesNotCauseLoop`
   - `CrossingMinimizationReducesCrossings`
   - `PortReorderReflectsConnectionOrder`

**Tamamlama kriteri:** `samples/tiny_cpu` örneğinde modüller mantıklı bir veri akış sırasında. Crossing sayısı eski layout'a göre %50+ azalmış (manuel sayım veya test).

---

### Faz 4 — Track assignment & port-side spreading
**Süre:** 3-4 gün

**Yapılacaklar:**

1. `Services/Routing/TrackAssigner.cs`:
   - Maze router'dan çıkan path'leri inceler.
   - Aynı koridorda (aynı X şeridinde veya aynı Y şeridinde) paralel giden net'ler için ayrı **track** (offset).
   - Binary packing: her koridor için "kullanılmış offset" listesi tutulur, yeni net en yakın boş offset'e yerleştirilir.
   - Track spacing: 6-10 px (compact/normal).

2. **Port-side spreading**:
   - Aynı modül kenarındaki pin'lerden çıkan kablolar, pin'den çıkar çıkmaz birbirinden ayrılsın.
   - Modül kenarına yapışan kısa "fan-out wedge" bölgesi (pin sırasıyla 0..N → 0..N+stubLength).
   - Maze router'a "soft repulsion" ekle: aynı kenara paralel yatay segment'ler için cell başına ek cost.

3. **Bridge minimization**:
   - Track assignment'tan sonra `AddBridgeMetadata` yeniden çalıştırılır.
   - Beklenti: crossing sayısı çok azalmış. Kalan crossing'lerde bridge çiziliyor.

**Tamamlama kriteri:** Yan yana giden paralel net'ler düzgün aralıkla; pin'den çıkışta kabarcık olmadan; bridge sayısı eski sürüme göre %70+ azalmış.

---

### Faz 5 — UI cila ve etkileşim
**Süre:** 3-4 gün

**Yapılacaklar:**

1. **Label placement** (eski `PlaceLabels` yerine):
   - `Services/Layout/LabelPlacer.cs`.
   - Penalty-based: label, route üzerinde 5-7 candidate slot, her slot için cost (obstacle overlap, other label overlap, distance from route midpoint).
   - Greedy: yüksek fanout net'in label'i önce yerleşir, sonrakileri etrafında konum bulur.

2. **Net highlight on hover**:
   - Mouse'un üzerinde durduğu route'un `BundleKey`'i tutulur.
   - Render'da o BundleKey'in tüm route'ları parlatılır; label görünür.
   - Hit test: route segment'lerine `IsPointOnSegment(point, tolerance=4px)`.

3. **Probe markers**:
   - Trace'i izlenen sinyaller route üzerinde küçük renkli daire/çubuk göstersin.
   - Click → waveform'a sıçra (mevcut `AddSelectedWaveformCommand`).

4. **Mini-map**:
   - Sağ alt köşede 200x150 px küçük overview.
   - Mevcut viewport bir dikdörtgenle vurgulu.
   - Click/drag ile viewport oynat.

5. **Drag-to-pan inertia**:
   - Mouse bırakıldığında son birkaç frame'in velocity'siyle pan devam etsin, friction ile yavaşlasın (60 fps animation timer).

6. **LOD (Level-of-Detail) rendering**:
   - `_viewportZoom < 0.5`: kablo width 1px, label yok, junction radius 1.5px.
   - `_viewportZoom < 0.3`: bus ribbon kapanır, tek çizgi.
   - `_viewportZoom > 1.5`: pin label ve signal value popover göster.

7. **Theme record**:
   - `Services/SchematicTheme.cs` — tüm renkler, kalınlıklar, font'lar bir `record` içinde.
   - `SchematicPreviewControl`'daki hardcoded `SolidColorBrush.Parse("#...")` çağrıları → `Theme.X`.
   - Light/dark theme switch hazır.

**Tamamlama kriteri:** Hover net highlight, mini-map çalışıyor, inertia akıcı, dark/light theme switchlenebilir.

---

### Faz 6 — Performans & ölçeklenebilirlik
**Süre:** 2-3 gün

**Yapılacaklar:**

1. **Routing cache**:
   - `SchematicPreviewControl`'da `_cachedRoutes` ve `_routingInputHash`.
   - Input hash değişmedikçe route hesaplama atlanır.
   - Hash'e dahil: panel rect, child rect'leri, port anchor'lar, connection list, compact flag.

2. **Async routing**:
   - Routing `Task.Run` arka plan thread'inde.
   - UI önce eski cached route'larla çizer, async tamamlanınca `Dispatcher.UIThread.Post` ile invalidate.
   - Cancellation token ile eski hesap iptal.

3. **Viewport virtualization**:
   - Render sırasında her route segment'i viewport ile intersect testle.
   - Viewport dışındaki segment'ler `DrawLine` çağırılmaz (DrawingContext atlanır).
   - 1000+ net'li tasarımlarda büyük kazanç.

4. **Profiling baseline**:
   - `samples/tiny_cpu`'yu 500 net'e büyütülmüş bir senaryo ekle (test asset).
   - BenchmarkDotNet ile router compute süresini ölç.
   - Hedef: 500 net <500ms first render, <100ms incremental.

**Tamamlama kriteri:** 500-net benchmark hedeflere ulaşıyor; UI thread 60fps korunuyor.

---

### Faz 7 — SVG export & interop
**Süre:** 2-3 gün

**Yapılacaklar:**

1. **SVG export**:
   - `Services/Export/SchematicSvgExporter.cs`.
   - Tüm route, modül kutusu, label, junction, bridge → SVG element'ları.
   - Theme renkleri inline veya SVG `<style>`.
   - `File → Export → SVG...` menü item.

2. **Yosys JSON compat (opsiyonel)**:
   - netlistsvg'nin kullandığı format okuma/yazma.
   - Diğer tool'lardan üretilmiş schematic'leri Bistable'da görüntüleme.
   - Reverse: Bistable'ın schematic'ini netlistsvg JSON olarak dışa aktarma.

**Tamamlama kriteri:** SVG export çalışıyor, başka SVG viewer'da açıldığında doğru görünüyor.

---

## 4. Risk & dikkat noktaları

1. **Faz 1 maze router'ın performansı**: A* sıkı tutulmazsa büyük tasarımlarda yavaş olabilir. Hanan grid kullanımı kritik; uniform grid kabul edilmez.

2. **Faz 3 Sugiyama + expanded scopes**: recursive rank assignment karmaşıktır. Eğer 3-4 günde oturmazsa, expanded scope'lar için klasik grid layout fallback bırak; bu görsel olarak fena değil.

3. **Faz 1'de eski testler kırılır**: Bazı testler eski router'ın koordinatlarına bağlı. Refactor'da bunları topoloji-bazlı assertion'lara çevir ("input lane current rect ile child rect arasındadır" yerine "X koordinatı 580 ile 760 arasında" gibi spesifik şartlar yumuşatılır).

4. **Avalonia immediate-mode render performansı**: `DrawingContext` küçük çağrılarda overhead'li. Faz 6'da virtualization şart. Geometry batching de düşünülebilir (`StreamGeometry` ile birleşik path).

5. **Sample test coverage**: `samples/` altındaki tüm örnekler (alu, counter, hierarchy, tiny_cpu, bus_fabric) her faz sonu manuel kontrol edilmeli.

---

## 5. Yardımcı bilgiler

### 5.1 Komutlar

```bash
# Build
dotnet build Bistable.slnx

# Test
dotnet test Bistable.slnx --no-build

# Run app
dotnet run --project src/Bistable.App/Bistable.App.csproj

# Run a specific sample
# (önce app'i aç, sonra File > Open Sample > tiny_cpu)
```

### 5.2 Önemli sample'lar

- `samples/tiny_cpu/tiny_cpu.bistable.json` — orta karmaşıklıkta hiyerarşik CPU (alu, registers, control, status flags)
- `samples/bus_fabric/bus_fabric.bistable.json` — bus routing testi
- `samples/hierarchy/hierarchy.bistable.json` — derin hierarchy
- `samples/alu/alu.bistable.json` — düz tek modül

### 5.3 Test setleri

- [SchematicConnectionRouterTests.cs](../tests/Bistable.Tests/SchematicConnectionRouterTests.cs) — 8 mevcut test
- [SchematicScopeLayoutEngineTests.cs](../tests/Bistable.Tests/SchematicScopeLayoutEngineTests.cs)
- [SchematicNodeCardLayoutEngineTests.cs](../tests/Bistable.Tests/SchematicNodeCardLayoutEngineTests.cs)

### 5.4 Toplam tahmin

| Faz | Süre |
|-----|------|
| 0 — Temizlik & dosya split | 2-3 g |
| 1 — Maze router | 4-5 g |
| 2 — Steiner + bus ribbon | 3-4 g |
| 3 — Layered placement | 5-7 g |
| 4 — Track assignment | 3-4 g |
| 5 — UI cila | 3-4 g |
| 6 — Performans | 2-3 g |
| 7 — SVG export | 2-3 g |
| **Toplam** | **24-33 gün** |

---

## 6. İlerleme kaydı

Her faz tamamlandığında bu dosyaya bir altbölüm ekle:

```
### Faz N — Tamamlandı (YYYY-MM-DD)
- Ne yapıldı, ne yapılmadı
- Karşılaşılan engeller
- Bir sonraki faza taşınacak teknik borç
```

### Faz 0 — Tamamlandı (2026-05-21)

**Yapılanlar:**
- `SchematicConnectionRouter.cs` içindeki ölü kod (`ComputeInlineRoutes`,
  `ComputeStackedRoutes`, `AvoidObstacles` ve ~12 yardımcı metot) silindi.
  Sınıf artık sadece `GridSchematicRouter`'a delege ediyor. `SchematicConnectionBundle`
  record'u da kaldırıldı (kullanılmıyordu).
- `shouldDrawLabel = false` hardcode'u kaldırıldı; artık sadece bundle primary
  route'lar (`route.IsBundlePrimary && route.LabelBounds.Width > 0`) için label
  çiziliyor — N-fanout net için tek label.
- `BuildRoutingObstacles` genişletildi: `CurrentNodeRect`, `ParentNodeRect`,
  `ProbeSectionRect` artık obstacle listesinde. Modül/probe section altından geçen
  kablo bug'ı bu sayede çözüldü.
- `SchematicPreviewControl.cs` (2585 satır) partial class olarak 5 dosyaya
  bölündü:
  - `SchematicPreviewControl.cs` (749 satır) — properties, brushes, ctor, public
    API, `Render`, pointer event'ler, collection change handlers, helper'lar,
    record'lar, event arg sınıfları.
  - `SchematicPreviewControl.Viewport.cs` (151 satır) — pan/zoom math.
  - `SchematicPreviewControl.Rendering.cs` (1144 satır) — tüm `Draw*` metotları,
    layout helper'ları (`BuildEffectiveScopeLayout`, `BuildNestedScopeLayout`,
    `OrderChildScopesForLayout` vb.), text/badge helper'lar.
  - `SchematicPreviewControl.Routing.cs` (465 satır) — `DrawConnectionRoutes`,
    request derleme, `BuildRoutingObstacles`, route brush, segment çizim,
    bridges/junctions, route label.
  - `SchematicPreviewControl.HitTest.cs` (99 satır) — hit-test, handler'lar,
    `DistanceToSegment`.
- 4 yeni regresyon testi eklendi (artık 80 test geçiyor, eski 76 + yeni 4):
  - `RoutesDoNotCrossCurrentNodeRectInterior`
  - `RoutesDoNotCrossParentNodeRectInterior`
  - `RoutesDoNotCrossProbeSectionInterior`
  - `BundlePrimaryRouteCarriesLabelBounds`
  - Yardımcı: `AssertRouteAvoidsObstacleInterior` — segment midpoint'lerini
    `Deflate`'lenmiş rect'in dışında olduğunu kontrol eder.

**Karşılaşılan engeller:** Yok. Build temiz (0 warning, 0 error). Tüm 80 test
geçti.

**Faz 1'e taşınan teknik borç / notlar:**
- `Rendering.cs` 1144 satır — büyük ama mantıklı bir grup (tüm çizim). Faz 5'te
  tema/LOD refactor sırasında daha da bölünebilir.
- `HitTestScope` metodu kullanılmıyor görünüyor (`SchematicPreviewControl.HitTest.cs:74`).
  İleri faz UI etkileşim eklenirken kullanılır veya silinir.
- Obstacle muafiyet: router `inflated.Contains(start) || inflated.Contains(end)`
  ile pin çıkışını skip ediyor — bu Faz 0'da yeterli. Faz 1'de A* için
  pin "approach cell" konsepti gelecek.
- Greedy detour hâlâ yerinde (Faz 1'de `MazeRouter` ile tamamen değişecek).
  Modül altından geçme bug'ı temel obstacle'larla zaten çözüldü; geri kalan
  iyileştirmeler Faz 1+ konusu.

### Faz 1 — Tamamlandı (2026-05-21)

**Yapılanlar:**

- Yeni `Services/Routing/` klasörü açıldı, 4 yeni sınıf:
  - `HananGrid.cs`: non-uniform routing grid (obstacle kenarlarının buffer
    uzaklığındaki X/Y hatları + pin koordinatları + panel sınırı + 18 px
    fallback hatları). Densify ile çok yakın hatlar birleştiriliyor (MinSpacing=1.5).
  - `CongestionMap.cs`: grid cell'i başına net sayım (long-packed key dict).
  - `MazeRouter.cs`: **A\*** path finding, Manhattan heuristic, priority queue.
    Cost = distance + bend penalty (8) + crossing (placeholder) + congestion
    (cell visit count × 6). Edge blocked test obstacle.Inflate(7) interior'una
    karşı. Source/target pin sahibi obstacle exempt mekanizması.
  - `SchematicMazeRouter.cs`: `ISchematicRouter` implementasyonu. Orchestration:
    bundle gruplaması, fanout-azalan + span-artan + key sıralaması, grid kurulum,
    her route için A* çağrısı + congestion update, junction/bridge tespiti, label
    yerleşimi.
- Eski `SchematicNetRouter.cs` (GridSchematicRouter ve lane-based logic) tamamen
  silindi.
- `SchematicConnectionRouter.cs` artık `SchematicMazeRouter`'a delege ediyor;
  ayrıca `ISchematicRouter` / `ISchematicLayoutEngine` / `SchematicNet`
  interface ve type'ları buraya taşındı.
- Junction detection geliştirildi: aynı bundle'da paylaşılan source/target
  pin'leri de junction olarak işaretleniyor (fanout için doğru davranış).
- `BuildGrid` obstacle.X/Right/Y/Bottom yerine sadece buffer offset hatlarını
  ekliyor (11 px compact, 14 px normal). Bu sayede A* yan duvarda yürümüyor.
- Fallback step hatları obstacle'ın buffer bölgesine düşerse skip ediliyor
  (`IsInsideObstacleBufferX/Y` filtresi).
- 8 testte güncelleme: lane-spesifik koordinat assertion'ları (eski router'ın
  3-elbow topolojisini varsayan) topology-based assertion'lara çevrildi:
  `AssertManhattanOrthogonal`, source/target preservation, fanout için shared
  source pin, output route directional check, label width check.
- `OrthogonalCrossingsProduceBridgeMetadata` testi yeniden setup edildi:
  küçük modüller arasında zorla kesişecek bir yatay + bir dikey route.

**Karşılaşılan engeller:**

- Pin-on-obstacle escape: pin (cardRect.X, midY) obstacle kenarında, A*
  exempt logic'i ile çözüldü.
- Hanan grid'de obstacle kenar hatlarının route tarafından kullanılması:
  buffer-only grid + obstacle inflation ile çözüldü.
- Fanout routes A* tarafından bağımsız çiziliyor, interior shared point yok.
  Source pin'in kendisini junction olarak işaretleyerek çözüldü (Faz 2'de
  Steiner tree ile daha temiz olacak).

**Durum:**
- Build: temiz (0 warning, 0 error).
- Test: 80/80 geçti. Routing test seti tamamen yeni router üzerinden çalışıyor.

**Faz 2'ye taşınan teknik borç / notlar:**

- Junction tespiti şu an "shared start/end point" üzerinden. Steiner tree ile
  trunk + branch yapısı kurulunca, junction'lar doğal olarak gelir.
- Bridge tespiti A* path'leri nadiren kesişir (congestion sayesinde). Bridge
  metadata mekanizması var ama frequent değil. Faz 2'de Steiner tree ile
  bridges'i daha akıllı yönetebiliriz.
- `MazeRouter.FindPath` cognitive complexity yüksek (analyzer warning). Faz 2'de
  helper'lara bölünebilir.
- A* state space: (col, row) - direction state space'i değil, yani optimal değil
  ama yeterince iyi. Faz 4 detailed routing sırasında yeniden değerlendirilir.
- ObstacleProximityMargin = 10, GridBufferCompact = 14, GridBufferNormal = 18, CongestionWeight = 12.
  Faz 5'te güncellendi; daha geniş clearance ve daha agresif congestion spreading.

### Faz 2 — Tamamlandı (2026-05-22)

**Yapılanlar:**
- `Services/Routing/RectilinearSteinerTree.cs`: Prim MST + BFS order (trunk-first fanout routing)
- `SchematicMazeRouter.cs`: `RouteSteinerFanout` — fanout > 1 nets için Steiner trunk paylaşımı
- `SchematicPreviewControl.Routing.cs`: Bus ribbon two-pass rendering (`DrawBusRibbonSegment`, `DrawBusTapMarker`)
- 7 yeni test (5 Steiner + 2 TrackAssigner).

### Faz 3 — Tamamlandı (2026-05-22)

**Yapılanlar:**
- `Services/Layout/HierarchicalLayoutEngine.cs`: Sugiyama 3-aşamalı yerleşim (cycle-break, Kahn rank, barycenter min.)
- `SchematicPreviewControl.Rendering.cs`: `OrderChildScopesForLayout` → `HierarchicalLayoutEngine.OrderForLayout` delegasyonu
- 7 yeni test (`HierarchicalLayoutEngineTests.cs`)
- `InternalsVisibleTo` eklendi Bistable.App.csproj'a

### Faz 4 — Tamamlandı (2026-05-22)

**Yapılanlar:**
- `Services/Routing/TrackAssigner.cs`: Band-tabanlı paralel segment tespiti ve ±offset ayrıştırması
- `SchematicMazeRouter.cs`: TrackAssigner entegrasyonu pipeline sonunda
- 2 yeni test (TrackAssigner)

### Faz 5 — Tamamlandı (2026-05-22)

**Yapılanlar:**
- `Services/SchematicTheme.cs`: Dark/Light preset'li `SchematicTheme` record (20 brush + 2 statik preset)
- `SchematicPreviewControl.cs`: `PaletteProperty` StyledProperty + `Palette` accessor; tüm hardcoded brush → `Palette.X`
- `OnPointerMoved`: `_hoveredSignalName` güncelleme + hover net highlight (`PushOpacity(0.22)` ile dim)
- `OnPointerExited`: hover temizleme
- LOD rendering: `_viewportZoom < 0.5` → ince çizgiler, küçük junction; `< 0.3` → bus ribbon kapanır
- Visual fixes: ObstacleProximityMargin 7→10, CongestionWeight 6→12, GridBuffer 11/14→14/18

**Atlanan Faz 5 özellikler (sonraki fazlara):**
- Mini-map (200×150 px overview + viewport dikdörtgeni)
- Drag-to-pan inertia
- Probe markers

### Faz 6 — Plan-dışı, ELK hattında büyük oranda tamamlandı (bkz. §0.2)

Bu faz orijinal maze-router hattında hiç başlamadı; ancak performans hedefleri
RTL'nin taşındığı ELK backend'inde farklı isimlerle karşılandı: async +
cancellable layout (`SchematicLayoutService`), LRU layout cache
(`GateLevelLayoutCache`), viewport culling (`GateSchematicCanvas`), Graphviz
route geometri cache. Ölçüm: [ELK_ROUTING_PERFORMANCE_ANALYSIS.md](ELK_ROUTING_PERFORMANCE_ANALYSIS.md).

### Faz 7 — SVG export: henüz yok (gerçekten başlanmadı)

`src/` içinde SVG/export kodu bulunmuyor. Bu madde hâlâ açık.

---

## 7. Devam talimatları (sonraki session için)

**Bu dokümanı sonraki session'da Claude'a şu şekilde sun:**

> "Schematic'in yeniden yazılması için planlama yapmıştık.
> `docs/SCHEMATIC_REWRITE_PLAN.md` dosyasını oku ve Faz 0'dan başla."

Claude bu dosyayı okuduğunda:
- Mimari karar geçmişi, dosya haritası, faz detayları hazır.
- Hangi dosyaları parçalayacağı, hangi obstacle'ları ekleyeceği belli.
- Test ekleme listesi hazır.

Doğrudan Faz 0'a girip dosya parçalama + obstacle düzeltmesi + test ekleme ile başlayabilir.
