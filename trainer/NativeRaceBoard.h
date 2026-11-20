#ifndef ASPHALT_NATIVE_RACE_BOARD_H
#define ASPHALT_NATIVE_RACE_BOARD_H
#include "NativeLobbyTransport.h"

#define NATIVE_RACE_BOARD_OFFSET 0x1100U
#define NATIVE_RACE_GUEST_BOARD_OFFSET 0x1400U
#define NATIVE_RACE_TAB_OFFSET 0x10a0U
#define NATIVE_RACE_PROOF_ROOM_OFFSET 0x10b0U
#define NATIVE_RACE_BOARD_MAGIC 0x38444352U
enum NativeRaceRowKind { RACE_ROW_HOST=1, RACE_ROW_GUEST=2, RACE_ROW_AI=3 };
typedef struct NativeRaceRow {
    unsigned identity[4], kind;
    int ping;
    char name[32];
} NativeRaceRow;
typedef struct NativeRaceBoard {
    volatile unsigned revision;
    unsigned magic, roomLow, roomHigh, count, seenAt;
    NativeRaceRow rows[NATIVE_ROSTER_CAPACITY];
    volatile unsigned endRevision;
} NativeRaceBoard;
typedef char NativeRaceBoardSizeMustMatch[(sizeof(NativeRaceBoard)==700)?1:-1];

__declspec(dllexport) void __cdecl NativeRememberRaceRoster(const NativeRoster *roster);
__declspec(dllexport) void __cdecl NativeCaptureRaceBoard(unsigned module,NativeUiState *ui,void *mode);
__declspec(dllexport) const char *__cdecl NativeRaceText(unsigned module,NativeUiState *ui);
#endif
