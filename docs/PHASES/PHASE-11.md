# Faz 11 — Testbench Akışı: Tek Tık Derle & Koştur

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H3](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P1
**Önkoşul:** Faz 9 (yumuşak — derleme boru hattı olgunlaşmış olmalı)
**Hedef:** Kullanıcı kendi SystemVerilog testbench'ini projeye ekler; tek tıkla
derlenir ve koşar. `verilator`/`make` komutu yazmak yok. `$display` çıktısı
panele akar, `$finish`/çıkış kodu pass/fail olarak yüzeye çıkar.

**Faz kapısı (kabul):**
- Proje şemasında `testbenches: [{ name, top, sources, timeoutSec }]`.
- "Run TB" düğmesi: derle → koştur → canlı stdout/stderr paneli → özet
  (pass/fail, süre); `$dumpfile` üretilmişse VCD dalga formuna tek tık link.
- `riscv_single_cycle` örneğine `tb_riscv_smoke.sv` eklenir (programı koşturur,
  `x1..x5` değerlerini `assert` eder) ve entegrasyon testi bunu uçtan uca koşturur.
- Derleme hataları Faz 9'un tanılama paneline `dosya:satır` ile düşer.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P11-1 | Şema: `ProjectConfiguration.Testbenches` (+ JSON (de)serileştirme, doğrulama) | 0,5 g |
| P11-2 | `TestbenchRunner` servisi: `verilator --binary` (Verilator 5 ana-üreteci) ile bağımsız sim yürütülebiliri; build klasörü `.bistable/tb/<name>/`; iptal + zaman aşımı (mevcut süreç-yönetim disiplini) | 2 g |
| P11-3 | Çıktı paneli: canlı akış (satır tamponlu), `$finish`/exit-code yakalama, pass/fail rozeti, süre; VCD artefakt keşfi | 1,5 g |
| P11-4 | UI bağlama: proje ağacında TB listesi, Run/Stop komutları — **mantık `TestbenchRunner`'da**, ViewModel yalnız bağlar (Faz 12 disiplini) | 1 g |
| P11-5 | Örnek TB + entegrasyon testi (gerçek Verilator ile derle-koş-doğrula) | 1 g |

**Toplam tahmin:** ~6 gün

## Kod dokunuş noktaları

- `src/Bistable.Core/Projects/ProjectConfiguration*` — şema
- **Yeni:** `src/Bistable.App/Services/TestbenchRunner.cs` (veya `Bistable.Verilator` içinde
  `TestbenchBuilder` + App tarafında koşturucu — derleme mantığı Verilator projesine yakışır)
- `samples/riscv_single_cycle/tb/tb_riscv_smoke.sv`
- Testler: `TestbenchRunnerTests` (birim: argüman üretimi/parse), entegrasyon (gerçek koşu)

## Riskler / notlar

- `--binary` Verilator ≥5.0 ister — sürüm denetimi + anlaşılır hata mesajı.
- Sonsuz döngülü TB: zaman aşımı zorunlu, süreç ağacı öldürme mevcut altyapıyla.
- İleri iterasyon (kapsam dışı): TB içinden dalga formunu canlı izleme,
  UVM-benzeri raporlama.
