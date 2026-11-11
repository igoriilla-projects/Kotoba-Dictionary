# Swift FSRS v5.0.0

Source: https://github.com/open-spaced-repetition/swift-fsrs/releases/tag/v5.0.0
Release commit prefix: `129e4dc`.

Vendored from the release's raw source, preserving its MIT license. Only the
algorithm, basic short-term scheduler, models, defaults and helpers needed by
YARXI are included. The upstream facade, long-term scheduler, rescheduling
utilities and random generator are excluded. The sole source modification is
`FSRSAlgorithm.applyFuzz`: it always rounds the interval, matching upstream's
`enableFuzz = false` branch. YARXI requires deterministic scheduling and uses
the basic scheduler directly through `ReviewScheduler` in the same module;
the release's scheduler API is internal and cannot be called through its public
Swift Package product. There is no network dependency at build or runtime.
