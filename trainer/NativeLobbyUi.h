#ifndef ASPHALT_NATIVE_LOBBY_UI_H
#define ASPHALT_NATIVE_LOBBY_UI_H

/* x86 little-endian shared-memory contract, pack 4, UTF-8 buffers.
 * Managed owner polls requestSeq, snapshots fields, then writes ackSeq.
 * Policy edits coalesce: fieldMask accumulates until the preceding request
 * is acknowledged. Other requests are latest-wins. Never advertise an
 * enforced policy merely because its requested bit is set.
 * TickUi must be called on the game's UI thread, after layout loading.
 */
#define NATIVE_UI_MAGIC 0x3849554eU
#define NATIVE_UI_VERSION 1U
#define NATIVE_UI_STATE_BYTES 3784U
#define NATIVE_UI_MAX_RACERS 60U

enum NativeUiPage { NUI_MAIN=0, NUI_OPTIONS=1, NUI_FIND=2, NUI_FRIENDS=3, NUI_KEYBOARD=4,
    NUI_FRIEND_REQUESTS=5, NUI_INVITATIONS=6, NUI_CONNECTION=7 };
enum NativeUiRequest {
    NUI_REQUEST_NONE=0, NUI_REQUEST_POLICY=1, NUI_REQUEST_FIND=2,
    NUI_REQUEST_FRIENDS=3, NUI_REQUEST_NICKNAME=4,
    NUI_REQUEST_ROOM_NAME=5, NUI_REQUEST_PASSWORD=6,
    NUI_REQUEST_JOIN_SELECTED=7, NUI_REQUEST_FRIEND_INVITE=8,
    NUI_REQUEST_PASTE=9, NUI_REQUEST_DIRECTORY=10,
    NUI_REQUEST_ADD_FRIEND=11, NUI_REQUEST_ACCEPT_FRIEND=12,
    NUI_REQUEST_REJECT_FRIEND=13, NUI_REQUEST_INVITATIONS=14,
    NUI_REQUEST_ACCEPT_INVITE=15, NUI_REQUEST_DISMISS_INVITE=16,
    NUI_REQUEST_FRIEND_REQUESTS=17, NUI_REQUEST_COPY_PLAYER_ID=18
};
enum NativeUiPolicy {
    NUI_MUTATORS=1, NUI_CHEATS=2, NUI_OWNED_CARS=4,
    NUI_IGNORE_CLASS=8, NUI_FILL_AI=16, NUI_PRIVATE=32
};
enum NativeUiField {
    NUI_FIELD_POLICY=1, NUI_FIELD_GRID=2, NUI_FIELD_LAPS=4,
    NUI_FIELD_ROOM_NAME=8, NUI_FIELD_QUERY=16,
    NUI_FIELD_NICKNAME=32, NUI_FIELD_PASSWORD=64
};
enum NativeUiBridge {
    NUI_BRIDGE_POLICY=1, NUI_BRIDGE_DISCOVERY=2,
    NUI_BRIDGE_FRIENDS=4, NUI_BRIDGE_NICKNAME=8,
    NUI_BRIDGE_PRIVATE=16
};
#pragma pack(push,4)
typedef struct NativeUiEntry { char label[80]; char id[64]; } NativeUiEntry;
typedef struct NativeUiState {
    unsigned magic, version, size;
    volatile unsigned requestSeq, ackSeq;
    unsigned requestKind, fieldMask, page;
    unsigned policyBits, grid, laps, bridgeFlags, statusCode;
    char statusText[160];
    char roomName[64];
    char nickname[32];
    char query[64];
    char password[64];
    char invite[1024];
    /* Managed bridge publishes result strings first, counts last. Zero
     * entries means no results; never populate placeholders as online users.
     * Explicit Paste clears roomCount and temporarily uses the first 1024
     * room bytes as keyboard-only text; statusCode 200 publishes completion. */
    unsigned roomCount, selectedRoom, friendCount, selectedFriend;
    unsigned resultsRevision;
    NativeUiEntry rooms[8];
    NativeUiEntry friends[8];
} NativeUiState;
#pragma pack(pop)
typedef char NativeUiStateSizeMustMatch[(sizeof(NativeUiState)==NATIVE_UI_STATE_BYTES)?1:-1];

/* grid/laps: 0 = original/default; 1..60 = requested custom value.
 * bridgeFlags is exclusively written by the managed/native policy bridge.
 * statusText is written by the managed bridge or explicit native LAN action (empty means use
 * truthful built-in status). requestKind and fieldMask describe the latest
 * user action. Name edits commit only when native keyboard Confirm is used.
 */
__declspec(dllexport) void __cdecl TickUi(unsigned module, void *screen, NativeUiState *shared);
#endif
