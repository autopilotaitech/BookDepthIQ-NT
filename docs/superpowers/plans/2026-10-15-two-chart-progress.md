# Execution ledger — plan: 2026-10-15-two-chart-workspace.md

Branch: codex/two-chart-workspace. Owner explicitly directed implementation on2026-10-15.
Baseline254 tests pass. No application launch, live install change or orders authorized/performed.
Ruling: use the explicitly requested new branch in the existing checkout; do not create an additional worktree without consent.
Pre-flight: Tasks1→3→4 share ChartBar/ChartStore/ChartViewport; Task2→3 share chart protocol; Task5 extends chart-only subscriptions. Order protocol remains independent.
Task1: core tests RED (missing model) → GREEN. Bounded index/revision/generation store, frame defaults and viewport math implemented; manual eviction regression also RED→GREEN.
Tasks2/3: independent native AddOn and background client implemented. Native C#6 compile succeeds. Isolated native chunk/unsubscribe and ephemeral-port subscribe/reload tests pass. Equal requests share one app cache/subscription; native Last does not add candle volume.
Task4: existing Build tree wrapped intact;32/68 main split, saved height ratio, pane menus, independent frames, collapse/maximize, native renderer, settings and navigation implemented. Headless rendering saved/viewed at dist/chart-native-test.png. Narrow-width and wide-tape guards pass.
Task5: chart-only OR adapter and optional SMA/EMA/volume implemented. Exact completed30second opening bar required; HTF disabled; DST historical endpoint regressions RED→GREEN. Native indicator parity remains unverified.
Task6: local development package1.13.0-beta.1 includes both AddOns; README/protocol/build notes updated. No push, merge, published release or running installation changes.

Independent read-only review found five important issues. Fixed: explicit reload generation (socket regression); eviction drift (viewport regression); study settings invalidation (bitmap regression); shared OR settings refresh/config-key invalidation; DST endpoints (two failing-before-fix fixtures). OR builds now run on one background task and cache empty results, not a dispatcher scan every33ms.
Ruling: PAX history uses one bounded28calendar-day request instead of adaptive14→28; simpler native lifecycle, at the cost of extra initial OR history. Retains up to75000 native30second bars for corrections rather than only summaries; bounded but higher memory/rebuild cost.
Ruling: this is a development build, not completion of every release acceptance item. Native parity/provider reconnect/performance measurements are not available from isolated tests. Crosshair linking, persistent manual viewport state, diagnostic raw-tick replay ring and detailed latency telemetry remain outstanding and are documented in docs/CHART-WORKSPACE.md. No HFT claim.
Review declined native parity, timestamp ambiguity at fall DST, provider disconnect, callback latency, high-DPI accessibility, packaging and vendor-indicator parity. Packaging is checked locally; the other native/desktop gates remain explicitly unverified, not passed by assumption.
