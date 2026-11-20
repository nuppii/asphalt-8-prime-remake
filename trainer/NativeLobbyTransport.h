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
#endif
