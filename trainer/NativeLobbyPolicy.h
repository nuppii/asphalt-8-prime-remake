#ifndef ASPHALT_NATIVE_LOBBY_POLICY_H
#define ASPHALT_NATIVE_LOBBY_POLICY_H
#include "NativeLobbyUi.h"
/* UI-thread only; call for every current GS, before any LocalGS filter.
 * Supports ONLY native private Wi-Fi host Fill with AI. Other policy bits
 * remain untouched and are not represented as enforced by this adapter.
 * On a new ready host room, saved NUI_FILL_AI wins. Later shared edits win
 * simultaneous native changes. Otherwise manual native changes update that
 * one shared bit and return1: caller should persist through its existing
 * Request(NUI_REQUEST_POLICY,NUI_FIELD_POLICY) path. Return0 means no such
 * shared-state update, not proof that a room/race/network request succeeded.
 * No requestSeq/status/bridgeFlags writes; no raw native field modifications.
 * Uses original E99D10 callback, retaining its real button shared pair.
 */
unsigned __cdecl NativeLobbyPolicyTick(unsigned module,void *currentScreen,NativeUiState *state);
void __cdecl NativeLobbyPolicyReset(void);
#endif
