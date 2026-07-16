# Bistable — Vizyon Boşluk Analizi (Vision Gap Analysis)

**Tarih:** 2026-07-16
**Kapsam:** Tüm kod tabanı (44.174 satır C#, 5 proje) + dokümantasyon, sahibin
6 maddelik vizyonuna karşı denetlendi. Her bulgu kanıta (dosya/satır, ölçüm,
XML çıktısı) dayanır; spekülasyon yoktur.

> **Uygulama güncellemesi (2026-07-16):** Bu analizin §2/§6'da kanıtladığı
> `always_comb`/coverage boşluğu Faz 7'de; §3.1/§5'teki sinyal-başına IPC
> darboğazı Faz 8'de kapatıldı. Tarihsel kök-neden kanıtı aşağıda korunur;
> güncel sıra için `docs/ROADMAP.md` bağlayıcıdır.

**Vizyon (sahibin ifadesiyle):**
1. HDL yazarken **canlı test ortamı** — web geliştirmedeki hot-reload deneyiminin donanım karşılığı.
2. Bir yandan SystemVerilog yazıp bir yandan şematiği görmek; şematik üzerinden **giriş sürmek, çıkışa ve İÇ SİNYALLERE bakmak**, hatanın yerini görmek (Logisim/Digital'in canlılığı × gerçek HDL).
3. Karmaşık işler için testbench yazıp **tek tıkla derleyip koşturmak** (verilator/make komutu yazmadan).
4. Şematiği görmek, **export** almak.
5. **Sentez** alıp sentezli hâli de görmek; canlı giriş/çıkışı sentez netlist'i üzerinde de yapmak.
6. **Profesyonel, yüksek performanslı, açık kaynak, extension destekli** bir araç.

---

## 0. Yönetici Özeti

Proje bugün "**iyi bir statik şematik görüntüleyici + temel simülasyon kabuğu**"
seviyesinde; vizyonun kalbi olan **canlı düzenle-gör-test döngüsü henüz yok** ve
mevcut mimari o döngüye *yaklaşmıyor*, çünkü emek son aylarda döngünün kendisine
değil, döngünün tek bir karesine (şematik çizim kalitesi) harcanmış.

Analiz anındaki en kritik üç bulgu (ilk ikisi artık Faz 7 ile kapalı):

1. **Şematik boru hattı `always_comb` bloklarını tamamen atlıyor.**
   `SchematicDecoder.Decode()` yalnızca `LocalSignals / Instances /
   SequentialBlocks / ContAssigns` işler; `ModuleAst.CombinationalBlocks` alanı
   **hiçbir yerde okunmaz**. Sahibin ekran görüntüsündeki "`zero` hâlâ hiçbir
   yere gitmiyor" şikâyetinin kök nedeni budur: `zero`'nun tüketicisi
   (branch-resolve `always_comb`'u) grafiğe hiç girmiyor. RTL doğru, simülasyon
   doğru — **görselleştirme eksik ve bunu kimse söylemiyor** (bkz. §2).

2. **Coverage sistemi bu körlüğü raporlamıyor.** "Sessiz kayıp yok"
   (`SilentMissCount == 0`) güvencesi yalnızca decoder'ın *baktığı* yapıları
   sayar; `CombinationalBlocks` coverage analizine de girmediği için ne
   `SilentMiss` ne `Unsupported` üretir. "Her kaybı raporlarım" iddiası, en
   yaygın SystemVerilog yapısı için boş. Vizyonun 2. maddesi ("hata nerede,
   anlamak") için bu bir güven krizidir.

3. **Vizyonun 4 maddesinin altyapısı sıfır.** Dosya izleme/otomatik yeniden
   derleme yok (H1), testbench akışı yok (H3), export yok (H4),
   extension/plugin mimarisi yok (H6). Bunlar "eksik özellik" değil, "hiç
   başlanmamış eksen".

Buna karşılık projenin **gerçek ve değerli güçlü yanları** var (bkz. §8):
sağlam worker IPC'si (atomik komut/yanıt, iptal, süreç yaşam döngüsü),
olgun gate-level (Yosys) boru hattı, 846 testlik disiplin, ELK tabanlı
düzen motoru ve çalışan bir RV32I örneği. Vizyona giden yol mevcut kodu
çöpe atmaktan değil, **eksenleri değiştirmekten** geçiyor: çizim cilası →
canlı döngü.

---

## 1. Vizyon ↔ Mevcut Durum Matrisi

| # | Hedef | Durum | Kanıt |
|---|-------|-------|-------|
| 1 | Canlı test ortamı (hot-reload) | **YOK** | Kod tabanında `FileSystemWatcher`/izleme sıfır sonuç; akış: dosyayı elle kaydet → `Build` düğmesi → bekle. Düzenle-gör gecikmesi dakikalar mertebesinde. |
| 2 | Şematikte canlı iç sinyal / hata bulma | **BÜYÜK ORANDA VAR** — canlı döngü eksik | Faz 7 `always_comb` görünürlüğünü/coverage sözleşmesini; Faz 8 tek-frame `ReadSignals` batch kanalını kapattı. Dosya izleme ve artımlı tazeleme Faz 9 kapsamı. |
| 3 | Testbench + tek tık derleme | **YOK** | `testbench` araması sıfır sonuç. Yalnızca üretilen worker (portları sür/oku) var; kullanıcı kendi TB'sini yazamaz/çalıştıramaz. |
| 4 | Şematik export | **YOK** | SVG/PNG/PDF export kodu yok. `SCHEMATIC_REWRITE_PLAN.md` Faz 7 hiç başlamamış tek madde. |
| 5 | Sentez + sentezli canlı test | **BÜYÜK ORANDA VAR** — en olgun eksen | Yosys entegrasyonu, hiyerarşik gate viewer, gate-level worker, RTL-vs-gate karşılaştırma, bus bundle/LOD; Phase 6.5 fiilen bitmiş (resmî kapanış gate'leri açık). |
| 6 | Profesyonel/performanslı/extension'lı açık kaynak | **KISMEN** | Test disiplini ve iptal/işlem yönetimi profesyonel. Ama: 0 XAML (44K LOC'un tüm UI'ı elle C#), 4.400 satır tanrı-ViewModel, ~4.460 satır pasif router backend'i, plugin API'si yok. |

---

## 2. Vaka Çalışması: `zero` neden hâlâ kopuk? — Boru hattının anatomisi

Bu vaka tek bir kablo değil; **boru hattının yapısal körlüğünün** kanıtı.

**Çözüm durumu:** Faz 7 tamamlandı; `u_alu.zero` tüketici kenarı otomatik
regresyonla ve sahibin görsel kabulüyle doğrulandı. Aşağıdaki bölüm kök neden
kaydıdır.

**Gerçekler (2026-07-16'da ölçüldü):**

- RTL doğru: `alu_zero`, top'taki `branch_taken` `always_comb`'unda tüketiliyor;
  native worker testi BEQ'in `alu_zero` üzerinden alındığını kanıtladı
  (11 komutluk program, beklenen tüm register/pc değerleri).
- Verilator XML'inde top modül **37 `contassign` + 11 `always`** içeriyor;
  `alu_zero`'nun tüm tüketimleri `always` bloklarının içinde.
- `VerilatorXmlAstReader` bu blokları `CombinationalBlockAst` olarak parse
  ediyor (AST'de mevcutlar), **ama**:
  - `SchematicDecoder.Decode()` (`src/Bistable.Core/Design/Schematic/SchematicDecoder.cs`)
    `module.CombinationalBlocks` üzerinde **hiç dolaşmıyor** — dosyada
    "Combinational" kelimesi geçmiyor.
  - `SchematicCoverageAnalyzer` da dolaşmıyor — bu blokların hedefleri/girdileri
    endpoint olarak **sayılmıyor** bile.
- Sonuç: `alu_zero`'nun grafikte tüketicisi yok → kablo yok → kullanıcı
  "dangling" görüyor; coverage raporu "her şey yolunda" diyor.

**Yan etkiler (aynı kök nedenden):**

- Foto-4'te şikâyet edilen "ne olduğu belirsiz gate'ler": `assign`'lardan gelen
  parçalar (immediate dilimleyiciler vb.) görünürken, onlara anlam veren
  kontrol akışı (`unique case`) görünmez — bağlamı kopuk parçalar kalıyor.
- Mux/öncelik mantığı, yazmaç-yazma seçicileri, durum makineleri… modern
  SystemVerilog'un ana gövdesi (`always_comb`) şematikte temsil edilmiyor.
- "Silent miss = 0" kapısı bu sınıf için **anlamsız güvence** üretiyor.

**Yapılması gereken (P0):**

1. `SchematicDecoder`'a `DecodeCombinationalBlock` ekle: tek-hedefli
   `if/else` zinciri ve `case` → `MuxPrimitive` (altyapı zaten var: `MuxPrimitive`,
   `MaterializeExpression`, inverter/splitter üretimi hazır). Çok-hedefli bloklar
   için hedef-başına projeksiyon (her hedef için ayrı mux ağacı çıkar).
2. Decode edilemeyen her comb hedefi **coverage'a `Unsupported` olarak yaz** —
   asla sessiz kalma. `SilentMissCount == 0` sözünün kapsamına
   `CombinationalBlocks` da girmeli; mevcut hâliyle test yeşil ama söz boş.
3. Regresyon: `riscv_single_cycle` için "`u_alu.zero` çıkışının grafikte en az
   bir tüketici kenarı var" şeklinde bir endpoint testi.

---

## 3. Mimari Değerlendirme

### 3.1 Süreç toposu

```
Bistable.App (Avalonia, .NET 10)
 ├─ verilator (subprocess) ── --xml-only elaborasyon
 ├─ üretilen C++ worker (subprocess) ── JSON satır-IPC (stdin/stdout)
 ├─ node tools/elk-router/elk-router.js (subprocess) ── elkjs düzen
 └─ yosys (subprocess) ── sentez JSON
```

**Değerlendirme:**

- Worker IPC'si iyi tasarlanmış: komut+yanıt atomik işlem, iptal drain'i,
  idempotent dispose, süreç ağacı öldürme. Bu, canlı döngünün **korunması
  gereken** temelidir.
- **elkjs = Node.js zorunluluğu.** "Kur ve çalıştır" hedefi için üçüncü çalışma
  zamanı (Node) ağır bir bağımlılık; ayrıca RV32 ölçeğinde FastPreview ~2,4 sn,
  register-file genişletmesi ~10 sn (ölçülmüş). Uzun vadede iki seçenek
  değerlendirilmeli: (a) ELK'nin Java orijinalini tek-dosya jlink ile gömmek
  yerine **elk'i WASM/JS-engine ile in-process** çalıştırmak (ClearScript/
  Jint benzeri) veya (b) katmanlı-düzen çekirdeğini C#'ta yazmak (repo'da
  yarım kalmış `HierarchicalLayoutEngine` zaten var). Kısa vadede: subprocess
  kalabilir ama **kalıcı/ısıtılmış tek süreç + istek kuyruğu** olduğu doğrulanmalı.
- **Probe IPC'si (Faz 8 ile kapandı):** protokol v3 `ReadSignals` ile görünür
  frame kümesini tek round-trip'te okuyor; 4.096 üstü istekler parçalanıyor ve
  her yol kendi başarı/hata sonucunu taşıyor.
- VCD akışı: her adımdan sonra tam yeniden parse (handoff notu). Artımlı
  index/tailer olmadan "canlı" dalga formu büyük tasarımda tıkanır.

### 3.2 Katman disiplini

- `Core / Verilator / Yosys / Protocol / App` ayrımı doğru kurulmuş; AST
  (`Bistable.Core.Design.Ast`) backend-bağımsız — bu vizyonun 6. maddesi
  (genişletilebilirlik) için iyi bir çekirdek.
- **İhlal:** şematik *anlamlandırma* mantığının önemli kısmı `App` içindeki
  `ElkGraphBuilder`'da (3.369 satır) yaşıyor: hangi primitivin hangi portu
  üretip tükettiği (producer/consumer anahtarları) UI projesinde. Bu bilgi
  `Core.Design.Schematic`'e inmeli ki (a) test etmek UI'sız mümkün olsun,
  (b) alternatif görselleştiriciler/extension'lar aynı modeli kullanabilsin.

### 3.3 İki paralel şematik dünyası

RTL (`SchematicPreviewControl.*` + `ElkGraphBuilder`) ve gate-level
(`GateSchematicCanvas` + `GateNetlistElkBuilder`) iki ayrı boru hattı. Ayrım
bilinçli (guardrail) ama maliyeti görünür: LOD, etiket çarpışması, bundle,
seçim, tema gibi çözümler gate tarafında olgun, RTL tarafında yok ya da farklı.
Vizyondaki "canlı değerli şematik" her iki dünyada da isteniyor (H2+H5); orta
vadede **paylaşılan bir "canlı-değer overlay + hit-test + LOD" çekirdeği**
çıkarılmalı (render farklı kalabilir, etkileşim modeli ortaklaşmalı).

---

## 4. Kod Kalitesi Bulguları (dosya dosya)

Ölçüm: 44.174 satır C#; en büyük 15 dosya toplamın ~%42'si.

| Dosya | Satır | Bulgu |
|---|---|---|
| `App/ViewModels/MainWindowViewModel.cs` | **4.400** | Tanrı-nesne: proje yükleme, worker yaşam döngüsü, trace, sentez, karşılaştırma, CPU-run, dock durumu, tercihler… Handoff bile "simulation-session controller'a ayrıştırılsın" diyor. Extension API'si (H6) bu yapıyla kurulamaz — davranışlar servisleşmeden dışa açılamaz. |
| `App/Views/MainWindow.cs` | **3.349** | Tüm ana pencere **elle C# ile** kuruluyor; repo genelinde **0 adet .axaml**. Sonuçlar: önizlemesiz UI geliştirme, stil/tema tekrarları, tasarımcı-araç desteği yok, katkıcı bariyeri yüksek. "Profesyonel UI" (H6) için ya XAML'e kademeli geçiş ya da en azından görünüm-kurucu yardımcılarının ciddî ayrıştırılması gerekli. |
| `App/Services/Routing/Elk/ElkGraphBuilder.cs` | **3.369** | Tek dosyada: node üretimi (13+ primitive), port-anahtar sözlüğü, üç ayrı kablolama dünyası (top/inner/grandchild), budama, etiketleme, `ElkNodeIds`, `ElkSignalKey`, telemetri. En az 4 dosyaya bölünmeli (NodeFactory / EndpointCollector / Pruner / Ids-Keys). Inner-compound `switch`'i ile top-scope `Collect*Endpoints` metotları **aynı bilgiyi iki kez** kodluyor — yeni primitive eklemek iki yeri birden güncellemeyi gerektiriyor (bu oturumda ConstantTie/Memory tam bu yüzden unutulmuştu). Primitive-başına tek "endpoint tanımı" (üretir/tüketir listesi) veri olarak tanımlanıp iki dünyada da o veriden türetilmeli. |
| `App/Views/SchematicPreviewControl.Graphviz.cs` | 2.655 | **Pasif backend.** Varsayılan ELK; Graphviz-dot/neato + MazeRouter ailesi toplam **~4.460 satır** taşınan yük. Karar verilmeli: ya "deneysel backend" olarak ayrı derleme birimine izole et, ya sil (git geçmişi zaten koruyor). Bakım yüzeyi ve kavramsal gürültü azaltılmalı. |
| `Core/.../SchematicDecoder.cs` | 1.146 | §2'deki `CombinationalBlocks` körlüğü. Ayrıca `DecodeSequentialBlock` yalnız `FindPrimaryAssign` (tek atama) tanır — çok atamalı `always_ff` gövdeleri sessizce tek FF'e indirgenir; coverage event'i var mı belirsiz, denetlenmeli. |
| `Core/Design/Ast/TempFolder.cs` | 383 | İşlevi iyi (Verilator `__VdfgTmp` geri-katlama) ama **adı yanlış**: "TempFolder" klasör çağrıştırıyor; `VerilatorTempInliner` gibi bir ad + `Design/Ast/Passes/` klasörü. Küçük ama profesyonellik algısı tam bu detaylarda. |
| `App/Views/SchematicPreviewControl.Symbols.cs` | ~700 | `EllipsizeToWidth` karakter-karakter `MeasureLabelWidth` çağırıyor → taşan her etiket için O(n) ölçüm×frame. Binary search ya da ölçüm önbelleği; ayrıca `MeasureLabelWidth`'in kendisi frame başına aynı metinler için tekrar tekrar çağrılıyor — küçük bir LRU (metin,boyut)→genişlik önbelleği tüm çizim yolunu rahatlatır. |
| `App/Views/SchematicPreviewControl.cs` | 1.013 | `static readonly SchematicConnectionRouter ConnectionRouter` — statik paylaşılan mutable servis; çoklu pencere/scope'ta gizli bağımlılık. DI/instance'a alınmalı. |

**Genel desenler:**

- **Kopya endpoint mantığı** (yukarıda) — hata üretmiş, üretmeye devam eder.
- **Sabitlerin dağınıklığı:** WB_*/SRCA_* gibi kontrol kodlamaları örnek RTL'de
  bile iki modülde tekrarlanıyor; C# tarafında da tema/eşik sabitleri
  (`0.55`, `0.90`, buffer px'leri) yer yer literal. Tek `SchematicConstants`.
- **Test disiplini güçlü** (846 birim + snapshot + görsel golden) ama iki
  kalıcı-flaky zamanlama testi (`ElkRunnerCancellation*`, `GateSchematicPerformance*`)
  paralel koşuda düşüyor. Bunlar ya `[Trait("Category","Timing")]` ile ayrı
  seri koleksiyona alınmalı ya eşikler yük-toleranslı yazılmalı — "tam suite
  her zaman yeşil" profesyonel refleksi için şart.

---

## 5. Performans Analizi

| Alan | Ölçüm/Kanıt | Risk | Öneri |
|---|---|---|---|
| ELK düzeni (gate) | RV32 top FastPreview ≈ 2,4 sn; register-file tam açılım ≈ 10 sn (ölçülmüş, dokümante) | Canlı döngüde her kayıtta düzen beklenemez | Artımlı düzen: değişmeyen scope'ların geometrisini koru, yalnız kirli alt-grafı yeniden düzenle. `GateLevelLayoutCache` fikri RTL tarafına da taşınmalı. |
| Probe IPC | Faz 8 ölçümü: 128 tekil = 533,9 ms; batch = 7,7 ms | **Kapandı:** normal görünür frame tek IPC | Protokol v3 + `ReadSignals` + tek `ValuesUpdated` olayı. |
| VCD | Adım sonrası tam reparse | Uzun koşularda süper-lineer maliyet | Artımlı tailer + halka tampon (retention sınırı). |
| Metin ölçümü | `MeasureLabelWidth` frame-başına tekrar | Büyük şemada CPU çizim darboğazı | (metin,punto)→genişlik LRU önbelleği; `EllipsizeToWidth` binary search. |
| Ölü backend yükü | ~4.460 satır | Derleme süresi + zihinsel yük | İzole et/sil (§4). |
| Node süreci | Harici çalışma zamanı | İlk açılış + dağıtım sürtünmesi | Isıtılmış kalıcı süreç garanti; orta vadede in-process alternatif POC. |

---

## 6. Coverage Sisteminin Kör Noktası (ayrı vurgu)

`SchematicCoverageAnalyzer` fikri **doğru ve değerli** (Phase 2.9'un mirası):
"görselleştirilemeyeni raporla". Ancak bugünkü sözleşme eksik:

- Girdiler: `ContAssigns` + `SequentialBlocks` (+ instance pinleri).
- **Girmeyen:** `CombinationalBlocks` → bu blokların hedefleri/okumaları hiçbir
  statüde değil. `SampleCoverageTests`'in `SilentMissCount == 0` kapısı bu
  yüzden `riscv_single_cycle`'da yeşil kalırken kullanıcı ekranda kopuk kablo
  görüyor.

**Sözleşme şöyle güncellenmelı:** "Modül AST'sindeki her sürücü ve her tüketim,
ya bir primitive'e bağlanır ya `Unsupported(kind)` olarak listelenir; üçüncü
durum yoktur." Bu tek cümle, vizyondaki güven ihtiyacının teknik karşılığıdır
ve testle kilitlenebilir (örnek: modüldeki her `output` portunun grafikte
en az bir kenarı ya da bir Unsupported kaydı olmalı).

---

## 7. Hedef Bazlı Yol: Ne Yapılmalı

### H1+H2 — Canlı döngü (projenin varlık sebebi) — **P0**

Hedef deneyim: *dosyayı kaydet → ≤1-2 sn içinde şematik güncel → değerler
canlı → kırmızı/eksik olan yerinde işaretli.*

1. **✅ Comb-decode (Faz 7):** §2'deki güven boşluğu kapandı.
2. **✅ `ReadSignals` batch protokolü (Faz 8):** tek frame = tek IPC.
3. **Dosya izleme + artımlı elaborasyon:** `FileSystemWatcher` → debounce →
   `verilator --xml-only` (yalnız değişen dosya seti) → AST diff → yalnız kirli
   modüllerin şematiği yeniden kurulur (`ElkSchematicEngine` LRU'su zaten
   scope-hash'li; diff ile evlendirilecek altyapı hazır).
4. **Worker'ın sıcak yeniden kullanımı:** port arayüzü değişmediyse mevcut
   worker'la devam; değiştiyse arka planda yeni worker derle, hazır olunca
   değiştir (mevcut cancellation/lifecycle altyapısı bunu taşıyabilir).
5. **Hata yüzeyi:** derleme/elaborasyon hataları editör-satırı referansıyla
   panelde; şematikte ilgili modül "stale" rozetiyle solsun.
6. UI'da minimal bir **kod görüntüleyici/düzenleyici bölmesi** (ilk sürümde
   salt-okunur + harici editör izleme yeter; AvaloniaEdit hazır bileşen).

### H3 — Testbench akışı — **P1**

- Proje şemasına `testbenches: []` (top, dosyalar, ana saat) girdisi.
- `SimulationWorkerBuilder`'ın genelleştirilmesi: kullanıcı TB top'u için de
  worker üret (`$display/$finish` yakala, stdout'u panele akıt).
- Tek tık: "Run TB" → derle+koştur+özet (pass/fail, süre, VCD linki).

### H4 — Export — **P1 (küçük, moral yüksek)**

- ELK geometrisi zaten vektörel: `ElkGraph` → SVG yazıcı (~1 dosyalık servis).
  PNG = mevcut Skia render'ından bitmap kaydı. Eski plan Faz 7 birebir geçerli.

### H5 — Sentez canlı — **P2 (mevcut tabanı parlat)**

- Gate-level zaten en olgun eksen. Eksikler bilinen liste: Phase 6.5 kapanış
  gate'leri (ölçümler + kabul) ve `Expand Cone`. Yeni mimari iş gerektirmiyor.

### H6 — Profesyonellik + Extension — **P2/P3**

- **Önce ayrıştır, sonra aç:** `MainWindowViewModel` → `SimulationSessionController`,
  `ProjectService`, `TraceService`… Extension API'si ancak bu servis sınırlarından
  tanımlanabilir (örn. `ISchematicOverlayProvider`, `ISimulationObserver`,
  `IExportProvider`, `INetlistImporter`).
- UI: yeni görünümler XAML'le; mevcut dev code-behind'ler dokunuldukça taşınır
  (big-bang değil).
- Ölü backend kararı; `TempFolder` yeniden adlandırma; flaky testlerin
  karantinası; `AGENTS.md`'nin bu raporu işaret etmesi.

### Önerilen sıra (özet)

| Öncelik | İş | Neden |
|---|---|---|
| **P0** | Comb-decode + coverage sözleşmesi | Şematik dürüst değilse hiçbir üst özellik anlamlı değil (zero vakası). |
| **P0** | `ReadSignals` batch | Canlı değerlerin ön şartı. |
| **P0** | Watch → incremental elaborate → şematik tazele | Vizyonun 1. cümlesi. |
| **P1** | SVG/PNG export | Küçük iş, görünür değer. |
| **P1** | Testbench akışı | H3 komple. |
| **P1** | VM ayrıştırma (session controller) | H6'nın ve sağlığın önkoşulu. |
| **P2** | Phase 6.5 resmî kapanış + Expand Cone | H5 cilası. |
| **P2** | Ölü backend temizliği, metin-ölçüm önbelleği, flaky karantina | Performans/bakım. |
| **P3** | Extension API + XAML geçişi + elk in-process POC | Uzun vade. |

---

## 8. Güçlü Yanlar (korunmalı)

Dürüst bir analiz eksikle sınırlı kalamaz; şunlar sektör standardının üzerinde:

- **Worker IPC disiplini:** atomik komut/yanıt, iptalin yanıt drain'i, idempotent
  dispose, süreç-ağacı sonlandırma — canlı döngünün taşıyıcı kolonu hazır.
- **Gate-level boru hattı:** hiyerarşi koruyan Yosys akışı, bundle/LOD/etiket
  çarpışma çözümü, 179-node RV32 ölçümleri, yüksek-fanout splitter ağaçları.
- **Test kültürü:** 846 birim + snapshot golden'lar + gerçek-Skia görsel
  regresyon + "her bug'a regresyon testi" kuralı.
- **Coverage fikri:** sözleşmesi genişletilirse (bkz. §6) sektörde az görülen
  bir dürüstlük mekanizması.
- **AST katmanı:** backend-bağımsız IR, TempFolder gibi geçişler — extension
  vizyonunun doğal zemini.

---

## 9. Kapanış

Proje "şematiği güzel çizen araç" hedefine hatırı sayılır yaklaştı; ama vizyon
bu değil. Vizyon bir **döngü**: yaz → gör → sür → anla. Bugün döngünün
"gör" karesi bile eksik (always_comb), "yaz→gör" bağlantısı hiç yok, "sür"
tekil-IPC ile nefes darlığında. İyi haber: döngüyü kurmak için gereken zor
parçalar (worker yaşam döngüsü, elaborasyon, düzen motoru, canlı probe)
zaten inşa edilmiş — eksik olan onları **birbirine bağlayan üç P0 iş**.
Önerim: bir sonraki tüm eforu §7'deki P0 üçlüsüne vermek ve her başka işi
(görsel cila dâhil) bu üçlü bitene dek reddetmek.
