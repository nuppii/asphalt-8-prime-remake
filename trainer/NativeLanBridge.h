#ifndef ASPHALT_NATIVE_LAN_BRIDGE_H
#define ASPHALT_NATIVE_LAN_BRIDGE_H
#include "NativeLobbyUi.h"
/* x86 UI-thread only, original Asphalt 4.8.0.7 ABI. No imports/CRT.
 * Begin snapshots query and dispatches the real Local WiFi Join callback.
 * Tick must run on EVERY active GS, before the LocalGS-only UI filter.
 * An exact, ASCII-case-insensitive LAN name must match exactly one native
 * discovered Room. Unicode bytes compare exactly. Duplicate names leave
 * the original room browser in control. No Room pointers survive a call.
 * 900 Tick calls bound the attempt; this is not a wall-clock timeout.
 * Native version/class checks remain intact. A dispatched join is not
 * evidence of connection, race synchronization, or internet discovery.
 * This explicit native action does not write managed room results/requests.
 */
unsigned __cdecl NativeLanBegin(unsigned module, void *localMenu, NativeUiState *state);
void __cdecl NativeLanTick(unsigned module, void *currentScreen, NativeUiState *state);
void __cdecl NativeLanCancel(void);
#endif
