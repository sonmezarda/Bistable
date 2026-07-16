# Faz 7 — Şematik Dürüstlüğü: `always_comb` Decode + Coverage Sözleşmesi

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §2, §6](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P0 — tüm vizyonun temeli
**Önkoşul:** yok (ilk faz)
**Durum (2026-07-16):** **Tamamlandı.** Uygulama, otomatik kapılar ve sahibin
düzeltilmiş riscv decoder/top görsel kabulü tamamlandı.
**Hedef:** Şematik, tasarımın *dürüst* resmi olsun: `always_comb` blokları
primitive'lere çözülür; çözülemeyen HER uç nokta coverage'da `Unsupported`
olarak listelenir. Üçüncü durum (sessiz görünmezlik) kalmaz.

**Faz kapısı (kabul):**
- `riscv_single_cycle` şematiğinde `u_alu.zero` çıkışının en az bir tüketici
  kenarı var (branch-resolve mux'una gider) — otomatik endpoint testi.
- Yeni sözleşme testi: her örnek için "AST'deki her comb/seq sürücü hedefi ya
  grafikte kenarlı ya `Unsupported(kind)` kayıtlı" — üçüncü durum test başarısızlığı.
- Tüm örnekler (arnicomp, tiny_cpu, bus_fabric, memory_demo, riscv) yeni
  sözleşme altında `SilentMissCount == 0`; `Unsupported` listesi gözden geçirilmiş.
- Sahip görsel kabulü: riscv decoder/top açılımında kontrol mantığı (mux'lar)
  görünür ve bağlı.

## Uygulama sonucu (2026-07-16)

- `CombinationalProjector`, `TempFolder.Fold` sonrasında reader zincirine eklendi.
  Begin/Assign/If/Case, son-atama semantiği, default taşıma, latch riski,
  sabit-olmayan case etiketi ve 128-seviye derinlik sınırı testlerle kilitli.
- Bit-dilimli procedural hedefler (örn. arnicomp `ctrl.ce`, `mar_d[7:0]`)
  per-bit sembolik durumla birleştiriliyor; yalnız bütün bus tam tanımlıysa tek
  sentetik `ContAssignAst` üretiliyor. Böylece seçim semantiği korunuyor ve aynı
  bus için kozmetik çoklu sürücü oluşturulmuyor.
- Coverage'a `CombinationalTarget` ve `CombinationalRead` endpoint'leri eklendi.
  Projector'dan geçmemiş blok, latch riski, çözülemeyen okuma ve çok-atamalı
  sequential gövde açık `Unsupported` oluyor.
- Otomatik `u_alu.zero` regresyonu gerçek ELK giriş grafiğinde ALU child-output
  portundan `branch_taken` mux girişine tüketici kenarını doğruluyor.
- Örnek kapısı yeşil: beş örnekte `SilentMissCount == 0` ve her comb/seq sürücü
  endpoint'i Routed/Unsupported (yalnız `__V*` için IntentionalOmission).
  İncelenen Unsupported özeti: arnicomp 5, tiny_cpu 6, bus_fabric 3,
  memory_demo 0, riscv 8. Bunların 20'si bilinen çok-atamalı sequential
  `FindPrimaryAssign` sınırı; riscv'deki kalan 2 kayıt mevcut concat/joiner
  çözülemeyen primitive girişleri. `always_comb` kaynaklı Unsupported kalmadı.
- Golden diff semantik olarak doğrulandı: mevcut node/port/edge çıkarılmadı;
  `marl_i` açılımına 9 node, 33 port ve 25 edge eklendi. Üç üst-seviye golden'da
  yalnız yeni iç primitive varlığını belirten `expandable` etiketi eklendi.
- İlk görsel kabul turunda bildirilen başlık/pin çakışması, sentetik ifade
  adlarının okunabilirliği ve yoğun mux geometrisi düzeltildi: genişletilebilir
  modüller artık ayrı bir başlık satırı ayırır; sentetik primitive'ler yalnızca
  işlem adını gösterip tam hedef adını hover tooltip'inde sunar; mux gövdesi,
  giriş aralığı, başlık ve canlı-değer rozeti yoğun fan-in için ölçeklenir.
- Sahip, düzeltilmiş riscv decoder/top görünümünü kabul etti; Faz 7 kapandı.

## Tasarım: "statement → hedef-başına ifade projeksiyonu"

Decoder'ı büyütmek yerine **AST seviyesinde bir geçiş** eklenir
(`Core/Design/Ast/Passes/CombinationalProjector.cs`): her `CombinationalBlockAst`
gövdesi, hedef sinyal başına tek bir ifade ağacına indirgenir ve **sentetik
`ContAssignAst`'lere** dönüştürülür. Böylece mevcut `DecodeContAssign`
makinesi (mux/gate/arith/inverter/splitter üretimi, `MaterializeExpression`)
hiç değişmeden yeniden kullanılır.

Algoritma (sembolik yürütme):
1. Blok gövdesini sırayla yürüt; `hedef → güncel ifade` haritası tut
   (son atama kazanır — SV semantiği).
2. `AssignAst` → haritayı güncelle.
3. `IfAst` → then/else alt-haritalarını hesapla; her hedef için
   `CondExpr(cond, thenExpr, elseExpr)` birleştir. Dal atamamışsa üst haritadaki
   değer (default) kullanılır.
4. `CaseAst` → arm'ları zincirlenmiş `CondExpr(selector == label_k, …)` olarak
   birleştir (mevcut decoder zincir-ternary'yi mux'a zaten çözüyor);
   sabit-olmayan label → o hedef projeksiyon dışı.
5. Blok sonunda **tam tanımlı** hedefler → sentetik `ContAssignAst`.
   Kısmî tanımlı (latch riski) veya projeksiyon-dışı hedefler → **coverage
   `Unsupported(kind: "CombinationalBlock", reason)`** — asla sessiz düşmez.
6. Geçiş `TempFolder.Fold`'dan (bkz. Faz 12 P12-4 yeniden adlandırma) sonra,
   `SchematicDecoder.Decode`'dan önce çalışır (`VerilatorXmlAstReader` çıkışında).

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P7-1 | `CombinationalProjector` geçişi: Begin/Assign/If sembolik yürütme, hedef haritası | 1,5 g |
| P7-2 | `CaseAst` desteği: sabit label → zincir `CondExpr`; `default` arm; `unique` etiketi bilgi amaçlı | 1 g |
| P7-3 | Kısmî atama (latch riski) tespiti + `Unsupported` diagnostiği; tam-default'lu bloklar (riscv decoder deseni) sorunsuz geçmeli | 0,5 g |
| P7-4 | `SchematicCoverageAnalyzer`: `CombinationalBlocks` endpoint'leri (hedefler + okunan sinyaller) sayıma girer; sözleşme: bağlan-ya-da-listele | 1 g |
| P7-5 | Sözleşme testi (örnek-bazlı): modüldeki her sürücülü sinyal için grafikte kenar VEYA Unsupported kaydı; `u_alu.zero` tüketici-kenarı regresyonu | 1 g |
| P7-6 | Çok sürücülü hedef (comb+seq aynı hedef) → coverage uyarısı; `DecodeSequentialBlock`'un çok-atamalı gövde sınırı (`FindPrimaryAssign`) için de aynı Unsupported yolu denetlenir | 0,5 g |
| P7-7 | Golden yenileme: arnicomp + primitive snapshot'ları (yeni mux düğümleri gelir) — diff gözden geçirilerek | 0,5 g |

**Toplam tahmin:** ~6 gün

## Kod dokunuş noktaları

- **Yeni:** `src/Bistable.Core/Design/Ast/Passes/CombinationalProjector.cs` (+testleri)
- `src/Bistable.Verilator/VerilatorXmlAstReader.cs` — geçiş zincirine ekleme
- `src/Bistable.Core/Design/Schematic/SchematicCoverageReport.cs` / analyzer — comb endpoint'leri
- `SchematicDecoder.cs` — değişiklik minimal (sentetik ContAssign'lar mevcut yoldan akar)
- `ElkGraphBuilder.cs` — **değişiklik yok** (Mux endpoint'leri zaten iki dünyada tanımlı)
- Testler: `CombinationalProjectorTests` (yeni), `SampleCoverageTests` (sözleşme), `ElkGraphBuilder*` regresyonları

## Riskler / notlar

- **Golden churn:** arnicomp grafiğine çok sayıda mux düğümü eklenecek; diff'ler
  tek tek doğrulanmalı (yalnız ekleme beklenir).
- **Graf büyümesi → düzen süresi:** ELK süresi artabilir; ölçüm alınıp Faz 9'un
  artımlı-düzen işine girdi yapılır.
- İfade patlaması (derin if/case iç içe) — `MaxDepth` sınırı + Unsupported fallback.
