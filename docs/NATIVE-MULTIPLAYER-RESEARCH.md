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

## Directed native discovery and Internet endpoint boundary

Static analysis of that same original image identifies a narrow periodic Finder call at preferred VA `0x150CE75`: `8B 4E 04 8B 01 FF 50 1C`. ESI is the Finder. Continuation `0x150CE7D` stores EDI/EBX into its last-send timer and then runs the normal epilogue. No returned EAX/EDX/flags are consumed in that continuation; the wrapper preserves general registers, flags and floating-point state.

The Discoverer vtable is `0x2169BE0`. Default discovery method `0x14FD410` simply invokes its own virtual +0x18 (`0x14FD1C0`) with its default sockaddr at Discoverer+0x28. The directed method takes a pointer to 128-byte sockaddr storage and returns with RET 4. Native room refresh independently uses that method at `0x150E21F` with Room+0x80 and destination port 6529. Directed discovery therefore reuses the original query/filter protocol rather than reconstructing it. Its current socket constructor selects AF_INET; IPv6 STUN support does not establish native IPv6 support.

The directed implementation scopes that call to the explicitly requested active browser Finder. It does not change a default destination, virtual table or global sendto call. Address storage remains owned by the adapter throughout the native callback. Normal discovery resumes after cancellation, expiry, browser exit or join dispatch.

Room+0x80 contains the observed reply sender. The parser `0x14FC890` copies the datagram sender into a discovery record at `0x14FCB97–0x14FCBA8`; GetResult `0x14FCEF0` returns that sockaddr; Finder `0x150C820` passes it to the Room constructor, and `0x15117A0` copies it to Room+0x80. Matching AF_INET and the IPv4 bytes at Room+0x84 prevents unrelated stale LAN rows from being autojoined. Multiple native rooms can share an IP, so ambiguity remains a manual choice. A sender IP is not cryptographic authentication.

Discovery port 6529 is only the first endpoint. Host lobby transport requests port zero and stores its actual bound port at Room+0x78 (`0x150DF64`). Direct native Join `0x150D940` replaces the sender sockaddr's port with that lobby port. After NetworkServer starts, callback `0x150ECE0` stores the bound race port at Room+0x7A (`0x150ED06`) and advertises it. Those separate endpoints and the native start/ready handshake must survive NAT routing before an Internet race can work. No native checkpoint import, transport rebind or pause/resume migration contract has been established by this discovery analysis.
## Native roster and replication limits

Preferred virtual addresses below refer to the original 4.8.0.7 x86 binary. These findings are from inspection of the shipped code.

| Path | Observed limit or dependency |
| --- | --- |
| Host room creation | `0xE9B161–0xE9B1B4` limits the legacy room to eight and applies the selected event's capacity. `0xE9B210` passes the result to the native lobby. |
| Lobby descriptor | Constructor `0x150C680` constructs eight inline guest records at `+0x374`, stride `0x344`. All eight end at `+0x1D94`, the metadata count. Serializer `0x1511560` clamps guest count to eight at `0x15116F2`. Receiver `0x15117A0` reads the count at `+0x370` and iterates the inline records. A variable-size or paged roster needs a separate, versioned payload. |
| Race startup | `0x150DB30` builds the startup user vector once, then calls the host interface. `0xD6D2D1` rejects more than twelve humans, and `0xD716E5` limits the AI fill to a combined total of twelve racers. |
| Server racer layout | The factory at `0x70B460` allocates `0x2670` bytes per racer. `0xD6E640–0xD6E700` writes twelve 20-byte records beginning at racer `+0x2578`; `0xD6EBD0–0xD6EBE0` iterates the same records for replication. The legacy array ends at `+0x2668`. A thirteenth record reaches past the allocation. Whether those records themselves correspond one-for-one with racers has yet to be established. |
| Client race data | `0xA33076` bounds an indexed write to twelve elements at `+0x700`. `0xA34993–0xA349C0` copies twelve 20-byte records from the client race record into a fixed stack buffer. The writers, matching readers, and buffer owners must agree before expanding either range. |
| Admission after launch | The native profile-admission handler at `0x150E90E` reaches admission only in lobby state 3. In startup and running states 6, 8 and 9 it sends result `0x18` at `0x150E94A` and returns. |
| Race joins | `0xD6E0E0` iterates the existing racers and compares each racer's peer identity and endpoint. A room reservation by itself neither registers a new replicated racer nor transfers its controller. |
| Replicated racer registration | The AI creation path calls `0xD71C60` to create the object, then `0xD74940` to register its replicated handle. Registration appends the handle to the peer's vector at `+0x23F0` and creates a two-way link between handle `+0xAD8` and racer `+0x2390`. |
| Peer initialization | `0xD73F10–0xD73FA6` searches the current race roster for the incoming identity and IPv4 or IPv6 endpoint before calling `0xD74270` with that racer. This is another binding that must agree with the admission and replication paths. |

Keep extended lobby admission gated on a compatible native race path. A live handoff requires an acknowledged course and race-state snapshot, agreement on the slot's replicated entity, and a controller switch after the newcomer is ready. The AI must continue using the slot if synchronization fails or the newcomer disconnects before the switch.
