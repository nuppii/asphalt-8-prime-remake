# Native multiplayer UI resource investigation

Read-only inspection of installed Asphalt 8 4.8.0 assets. No game/repository files changed, no game process launched. The companion WinForms UI is not needed to obtain the original game's visual presentation.

## Native screen inventory

All these resource paths resolve inside `D:/Asphalt 8 Airborne 4.8.0 Offline/Data/gui/multiplayer_menu.bgin` (491392 bytes):

| Native resource path suffix | File offset | Purpose and useful children |
|---|---:|---|
| screen_multiplayer_private_local | 0x45040 | Native Create/Join entry; `create_button`, `join_button`, `center_mp_done`, original top bar |
| screen_mp_private_lobby | 0x1fa07 | Room browser; `room_scroll`, room rows, player list, `room_list_heading` |
| screen_mp_private_lobby_waitroom | 0x30829 | Room lobby; `owner_buttons`, `fill_with_ai_button`, `ready_button`, `guest_buttons`, `game_mode_poster`, `location_poster`, `class_poster`, `player_list` |
| screen_matchmaking_waitroom | 0xa4 | Native vote interface; `track_poster_container`, `mode_poster_container`, `lap_poster_container`, `ready_poster_container`, `option0Container` through `option2Container`, `poster_button`, `time_label`, `player_votes_container` |
| screen_mp_race_countdown_overlay | 0x43340 | Native race countdown overlay |
| template_room_row | 0x4da89 | Browser row: `capacity_label`, `mode_label`, `location_label`, `class_label`, `owner_label`, `join_button`, `room_full_button` |
| template_room_player_data | 0x4bbe9 | Player row: `name_label`, `votes_label`, `player_button`, `car_label`, `emblem`, `waiting_label`, `unavailable_label` |
| template_mp_track_poster | 0x43e0d | Track choice poster |
| template_mp_mode_poster | 0x1e7d8 | Mode choice poster |
| template_laps_poster | 0x4f706 | Lap choice presentation |
| template_laps_number | 0x50f3c | Existing lap-number graphic |
| template_player_votes | 0x1e125 | Per-driver vote display |

Use prefix `multiplayer_menu/` for native resource lookup. Existing UI depicts eight player entries. This is a layout limit, not evidence that the simulation/network supports 60 remote players. Reuse `template_room_player_data` in a scrolling list for larger rooms. Existing lap poster artwork includes the numbers 1, 2 and 3; arbitrary 1..60/default requires dynamic native text or additional native controls, not selecting non-existent numbered artwork.

## Original map art and binding fields

`background_posters_templates.bgin` has 95 native top-level movie templates, all inventoried with original Sprite references in `native-ui-resources.json`. No custom pictures or fabricated map artwork are required.

Examples:

| Template resource | Original artwork reference |
|---|---|
| background_posters_templates/Movie_poster_nevada_bg_000 | /Sprites/Career_Background_01/Movie_poster_nevada_bg |
| background_posters_templates/Movie_poster_iceland_bg_000 | /Sprites/Career_Background_01/Movie_poster_iceland_bg |
| background_posters_templates/Movie_poster_area_x_1_bg_000 | /Sprites/Career_Background_03/Movie_poster_arx_01 |

The vote poster contains `bg_label` as the background movie, `location_label` as title, and `poster_button` as its input target. Its original frame is `/Sprites/UI_Master_Top_Bar/Movie_poster_frame_complete_bg`, title decoration `/Sprites/UI_Master_Top_Bar/Movie_mm_label_yellow_bg`, pressed overlay `/Sprites/UI_Master_Top_Bar/Movie_pressed_button_overlay`. Lobby location/mode/class posters expose `name_label`, `bg_label` and status children.

Important: these are observed widget names and resource references, not proven database column names. `asphaltdb_eventsdef.bjson`, `asphaltdb_generic.bjson` and `asphaltdb_career.bjson` are binary encoded. Recover the selected event's native poster token getter to map real event IDs safely; do not infer route IDs from labels alone. Existing formatting string in the original executable is `background_posters_templates/Movie_poster_%s_bg_%03d`.

All physical artwork is already installed. `sprites.bgin` groups Career_Background_01 through _04 refer to `../gfx2D/textures/Career_Background_XX.tga`; corresponding files exist under `Data/textures_win32/career_background_XX.tga`. The exact runtime redirection was not traced. The files are not ordinary TGA: map atlases contain DDS/DXT5 headers and a raw LZ4 block; DXT5 data for 2048x2048 is 4194304 bytes. Career background 01 decompressed successfully in memory. Main menu and lobby vote textures instead use PVRv3 RGBA8 with custom `jlz4`/`jetc` metadata and LZ4 data. Native loaders already understand both containers; exporting/re-encoding them is unnecessary for reuse.

## Original controls for new rules

`windows_contents/windows8_checkbox_simple` is an existing native checkbox template at file offset 0x736f6. Children: `horizon_label`, `horizon_button_checkbox`. Original off/on artwork: `/Sprites/UI_Ingame/Movie_igm_checker_off` and `/Sprites/UI_Ingame/Movie_igm_checker_on`. Its shipped label uses `/Widgets/MM_White_Text_shadow_small`; replace the label at runtime for allow cheats, mutators, owned cars, ignore class, fill bots and private visibility.

A genuine editable/password field was not identified by resource-name scanning. `windows_contents/input_tranfer_code_screen` contains code-display/paste actions; it is not evidence of a reusable text input. Profile editing and password dialogs need native callback/input API tracing rather than inventing an unsupported GIN widget class. Existing numeric/label/button templates can implement default and 1..60 selection entirely in-game.

## BGIN format evidence

Binary GIN trees, not XML and not encrypted. First eight bytes: `aa 1a 0c 00 30 a5 56 94`. At offset 8 is a u16 namespace length N and at offset 10 the namespace. At `a=10+N` is a u32 directory block length; at a+4 a u16 top-level count; then count records `{u32 nameHash, u32 absoluteFileOffset}`. A node begins `{u32 typeHash, u32 payloadLength, u16 nameLength, nameBytes...}`. Next node boundary matches `offset+8+payloadLength`. Remaining property/child serialization is only partially decoded. Do not insert raw strings or change size without rebuilding offsets/lengths.

## Actionable integration route

1. Route the multiplayer tab into the existing WiFi/local native state. Reuse the existing native state constructors/controllers where possible; they already bind the GIN UI and original assets.
2. Bind native Create/Join/browser callbacks to the room backend. Native layout loading and network transport are separate concerns; displaying a lobby does not validate native racing.
3. Populate room rows and player rows on the game's UI/main thread. Bind kick/promote to native player-row actions, leaving buttons to native top-bar navigation.
4. Use original matchmaking poster containers for actual event/map voting. Populate native map movie and title from the selected native event. Use native checkbox template for new room rule toggles.
5. Keep room state/settings synchronized through a native controller adapter; preserve local unsaved setting edits until authoritative acknowledgement, as done in the companion prototype.
6. Establish and test the native race entity/network bridge before enabling Launch. Public directory registration, invitation UI, lobby handover, race handover and 60-player simulation require separate validation.

## Static native lookup evidence

Original x86 executable preferred base is 0x400000. These addresses are static preferred VAs, not runtime relocated addresses and not automatically safe patch points.

`multiplayer_menu/screen_mp_private_lobby` has a valid push at 0xeef21e. Calls at 0xeef227 -> 0x533850 wrap its path; lookup uses global 0x224ab74, adjusted by +4, and calls 0x145c6c0 at 0xeef243; shared-result handling calls 0x500200 at 0xeef253. This supplies an existing native resource loading example.

Original poster formatting string references occur near preferred VAs 0xf5b80c, 0xf5b966, 0xf5bbdc, 0xf5bd33, 0xf5bfac and 0xf5c106, followed by native formatting helper 0x12a4de0. `multiplayer_menu/template_mp_track_poster` has a raw immediate reference near 0xa0b76d; `m_locationPoster` near 0xe98941. The latter reference boundaries need containing-function disassembly before using them as code hooks.

Original executable SHA-256: `6a5d3933f92809297631f1299c9906ec40fbb9d585a8c77115f321bfe733134c`.
