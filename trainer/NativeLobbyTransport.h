#ifndef ASPHALT_NATIVE_LOBBY_TRANSPORT_H
#define ASPHALT_NATIVE_LOBBY_TRANSPORT_H
#include "NativeNetworkState.h"
/* Read-only native observation on the primary game/UI thread, before the
 * native UI/LAN helpers. The existing state allocation must include the
 * separately versioned NativeNetworkState extension (4040 bytes total).
 * Writes only extension header and host/client observation fields, protected
 * together by hostRevision/hostEndRevision. Does not write UI/joins/mask.
 * hostAlive means a ready native hosting lobby; clientConnected means an
 * accepted native lobby client at this observation, never a synchronized race.
 * Readers accept only equal, even revision/endRevision snapshots.
 */
void __cdecl NativeTransportTick(unsigned module,void *screen,NativeUiState *ui);

enum { NATIVE_ROSTER_CAPACITY = 12 };
typedef struct NativeRosterUser {
    unsigned identity[4];
    char name[32];
} NativeRosterUser;
typedef struct NativeRoster {
    unsigned capacity, humans, fill, roomLow, roomHigh;
    NativeRosterUser users[NATIVE_ROSTER_CAPACITY];
} NativeRoster;

typedef struct NativeRosterSlots {
    unsigned roomLow, roomHigh, occupied;
    unsigned identities[NATIVE_ROSTER_CAPACITY][4];
} NativeRosterSlots;

unsigned __cdecl NativeReadRoster(unsigned module, void *screen, NativeRoster *roster);
/* Source indexes are one-based, with zero for a free or AI-reserved slot. */
__declspec(dllexport) unsigned __cdecl NativeAssignRoster(
    const NativeRoster *roster, NativeRosterSlots *slots, unsigned *sources);
#endif
