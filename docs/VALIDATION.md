# Validation notes

## Observed checks

| Check | Observation |
| --- | --- |
| Native and managed build | MSVC x86 grid image and Framework C# trainer compile successfully |
| English UI | Game language switched to English and retained after restart; trainer categories, controls and statuses translated |
| Saved settings migration | Earlier French labels are normalized on load for enabled tools and favorite destinations |
| Career class C | Class D Audi R8 e-tron started the Iceland Infection event |
| Required-car series | Class D Dodge Dart GT started the Renault Clio-restricted Temple event |
| Career class B | Dodge Dart GT started the Great Wall event |
| Custom race HUD | Recent starts displayed 5 laps and 40 racers |
| Career toggle | Disabling restored Clio-only/class B selection restrictions in the open garage; enabling restored alternative garage selection |

These observations are local evidence, not exhaustive coverage. Some checks preceded the English-only string changes. Translating the interface did not change native patch offsets or emitted hook code.

## Regression checklist for future changes

- Verify both enabled and disabled behavior, including a fresh process.
- Reopen every category and favorites to confirm state consistency.
- Start races on several layouts and with several owned cars.
- Test each custom race override independently, together and disabled.
- Exercise 61-racer starts and full race completion on multiple tracks.
- Compare MAX/PRO values with a saved baseline for cars with different PRO ceilings, including the Dodge; enter a race and restore the original tuning after disabling.
- Verify paid upgrades and purchases change the expected currency exactly once.
- Verify sale refunds match receipts, including insufficient funds and missing receipts.
- Recheck all Moto Blitz season and per-event gates.
- Treat remote-event and missing-catalogue paths as separately unsupported until observed working.

When reporting a crash, specify whether it happened during selection, loading, grid preparation, racing, upgrading or shutdown. Include the last actions and active tool list. Do not claim a whole category is verified from a successful signature check alone.
