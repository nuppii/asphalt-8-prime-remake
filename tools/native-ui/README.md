# Native Asphalt 8 UI resource tooling

These tools reuse installed Asphalt 8 4.8.0 native GIN layouts and artwork. They
generate local resources from the user's installation; no game artwork, original
resource files, or generated full resources belong in the repository.

Python 3.8+ is required. No third-party dependencies are needed.

```powershell
python tools/native-ui/test_bgin.py --gui 'D:\Asphalt 8 Prime Remake\Data\gui' -v
python tools/native-ui/build_native_ui.py --gui 'D:\Asphalt 8 Prime Remake\Data\gui' --output 'C:\Asphalt8Build\native-ui-generated'
```

The builder refuses to overwrite its installed input. It creates
`multiplayer_menu.bgin` and a build manifest in the output directory. Installing
that file, native callback binding, and visual testing are separate integration
steps. The builder neither changes the game nor starts it.

## Current presentation changes

The default build clones the existing Create Room poster into a third card in
the same native horizontal row. It preserves the original poster sprites,
pressed-button overlay, font, layout properties and native button class. The
new title reads `ROOM SETTINGS` and its names are:

- `offline_room_settings_card`
- `offline_room_settings_button`
- `offline_settings_spacer`

The original Create/Join names remain unchanged. A native controller must locate
the new button and register its callback. **The generated button has no callback
on its own. Its layout and hitbox have not yet been observed in-game.** The row
has three native poster children after mutation; final native sizing must be
verified at the game's supported resolutions.

`--rules-panel` also creates an experimental panel with six original native
checkbox widgets: mutators, cheats, owned cars, ignore class, fill bots and
private room. It clones `windows_contents/windows8_checkbox_simple`, preserves
the native on/off artwork, and gives each checkbox an `offline_<rule>_checkbox`
name. Its parent is `offline_room_rules_panel` inside `center_mp_done`. Native
placement, state binding and input callbacks remain required. Do not ship that
experimental layout without inspecting it in-game.

`clone_native_row()` provides the same structural cloning operation for existing
native list rows or other controls. Call it with an unambiguous native source
node and target parent. It does not implement list scrolling or game behavior.
For dynamic room/player lists, the runtime can instantiate the original
`template_room_row` and `template_room_player_data` instead of embedding a fixed
number of rows in the file.

## Serialization recovered from installed resources

The parser follows explicit serialized counts and lengths. It never scans
strings heuristically to infer children.

| Field | Encoding |
|---|---|
| Signature | 8 bytes `aa 1a 0c 00 30 a5 56 94` |
| Namespace | u16 byte length, byte string |
| Directory length field | u32, observed value `8 * record_count + 4` |
| Record count | u16 |
| Directory entries | repeated u32 name hash, u32 absolute node offset |
| Directory trailer | u16 zero, u16 repeated record count |
| Node | u32 type hash, u32 payload byte length, payload |
| Node payload | counted name, 20 transform bytes, u16 property count, properties, u16 child count, recursive child nodes |
| Property header | u32 key hash, u16 kind, u16 array count |
| Kind 2 | count doubles |
| Kind 4 | count records of int32 value, u32 type hash, u16 byte length and associated bytes |
| Kind 8 | count length-prefixed resource strings |
| Kind 0x400 | count length-prefixed text strings |
| Kind 0x80 | count vectors, each u8 dimension and dimension float32 components |

Property type meanings beyond their observed representations remain partly
inferred. Their original payload bytes, floating-point bit patterns, enum data,
record hashes, ordering and transform bytes are preserved exactly. New child
names use counted strings; the tool does not invent top-level native name
hashes. Every ancestor's payload length and every following directory offset
is recalculated when encoding.

The parser deliberately rejects unsupported signatures, property types,
directory variants, gaps, trailing bytes and inconsistent node boundaries.
The three intended input resources roundtrip byte-for-byte. A wider local
inventory roundtripped 83 of 87 GUI files; `sprites.bgin` has additional property
types and three unrelated files have other directory trailers. Those variants
are outside the current mutation scope.

## Validation and remaining integration

Tests validate original roundtrips, length-changing node/text mutations, native
button/checkbox class and sprite preservation, structural reparse of generated
data, unchanged sibling resource bytes and rejection of corrupt input. These
are resource format tests, not a claim of native UI or network behavior.

Before release, the runtime integrator must load the generated resource through
the original game resource manager; bind each new callback; observe focus,
layout and hitboxes; synchronize checkbox state with authoritative room rules;
and validate map selection, launch and native race networking separately.

Use original `background_posters_templates` map resources and original
multiplayer native screen controllers. No WinForms window, replacement raster
artwork or invented GIN class is involved in these generated controls.


## Standalone original-style on-screen keyboard

Use the builder with `--keyboard-only` and the same `--gui` and `--output`
arguments. This generates only `offline_lobby_ui.bgin`, leaving the original
multiplayer resource unchanged. Native factory path:

`offline_lobby_ui/screen_multiplayer_private_local`

The retained root name and original directory hash avoid guessing the native
name-hash algorithm. Namespace and filename are `offline_lobby_ui`; the
single-record directory has correctly rebuilt count and absolute offset.

| Widget name | Native type | Role |
|---|---|---|
| offline_input_value | ButtonWidget `0x7d332216` | Display text; runtime should ignore clicks/disable input |
| offline_key_A through offline_key_Z | ButtonWidget `0x7d332216` | Uppercase letters |
| offline_key_0 through offline_key_9 | ButtonWidget `0x7d332216` | Digits |
| offline_key_SHIFT | ButtonWidget `0x7d332216` | Runtime toggles upper/lowercase and updates letter labels |
| offline_key_PASTE | ButtonWidget `0x7d332216` | Runtime reads and validates clipboard text |
| offline_key_DASH, DOT, UNDERSCORE, COLON, SLASH | ButtonWidget `0x7d332216` | Insert `-`, `.`, `_`, `:`, `/` respectively |
| offline_key_SPACE | ButtonWidget `0x7d332216` | Append space |
| offline_key_BACKSPACE | ButtonWidget `0x7d332216` | Delete final character |
| offline_key_CLEAR | ButtonWidget `0x7d332216` | Clear the entire input |
| offline_key_CANCEL | ButtonWidget `0x7d332216` | Cancel and close |
| offline_key_CONFIRM | ButtonWidget `0x7d332216` | Submit and close |

All 49 controls clone the original transfer-code screen's paste_button,
including normal/pressed orange artwork and the original small white font.
The frame clones the original fixed-size container, requesting width 90% and height 90% through observed numeric
properties `0x7766bd8a` and `0xb6490172` (stored as 90.0). The copied poster
aspect constraint `0x94de0460` is disabled (0.0); retaining it forces the
frame height to the old poster ratio and clips taller menus.
Original vertical/horizontal containers arrange the display, four alphanumeric
rows, a punctuation/case/paste row and an action row. Button padding is zeroed
through its existing four-double property.
These resource-level dimensions require final measured native layout testing.

For an empty native vertical panel, use `--panel-only` instead. Output is
`offline_lobby_panel.bgin`, native factory path
`offline_lobby_panel/screen_multiplayer_private_local`. It contains the plain
root, `offline_panel_frame` (original fixed-size class, requested 90% in each axis with aspect lock disabled), and
`offline_panel_rows` (original VerticalContainer class `0xde8925ba`) with no
children. Add runtime-created native buttons to **offline_panel_rows**, then
attach the complete panel root to the active screen's plain `center` overlay.
Adding multiple buttons directly to an overlay stacks them at the same position;
the real vertical parent supplies their layout. Runtime owns visibility,
button lifetime, callbacks and final measurement. This output adds no menu-entry
changes and retains the original root name/hash pair.

Root placement, input context, maximum length, password masking, callback
lifetime and keyboard closure belong to the native runtime integration. This
keyboard initially displays uppercase letters. Runtime handles case switching,
clipboard input and punctuation insertion; generating the resource does not bind
these actions. Full international text input is outside this template.
The tests validate its directory, all 49 native
buttons, original normal/pressed assets and unchanged source documents.


### Frame sizing and deployment

Both generated standalone frames now use 90% width, 90% height and no poster
aspect lock. Runtime investigation identified that the original aspect-enabled
poster forced height to width times 0.2. At the observed native viewport height
of 67 units, 90% provides about 60 units: thirteen 4-unit rows fit, leaving room
for ordinary spacing. Longer social menus still require runtime pagination or
scrolling; the resource does not guarantee arbitrary row counts or resolutions.
The fresh generated files must be reloaded before testing their serialized
settings. Native runtime code can apply the same verified geometry explicitly
when existing factory caches retain the previous resource.

Build locally from the user's installed assets with either command:

```powershell
python tools/native-ui/build_native_ui.py --gui 'D:\Asphalt 8 Prime Remake\Data\gui' --output "$env:TEMP\asphalt-native-ui" --panel-only
python tools/native-ui/build_native_ui.py --gui 'D:\Asphalt 8 Prime Remake\Data\gui' --output "$env:TEMP\asphalt-native-ui" --keyboard-only
```

After closing the game, the local installer/runtime integrator can back up any
existing `Data/gui/offline_lobby_panel.bgin` and `offline_lobby_ui.bgin`, then
copy the newly generated files of those names into `Data/gui`. Restart the game
so its resource manager loads the new files. The builder deliberately only
writes to its separate output directory and performs no installation itself.
Do not commit or redistribute the generated BGIN files.
