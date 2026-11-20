#ifndef ASPHALT_NATIVE_NETWORK_STATE_H
#define ASPHALT_NATIVE_NETWORK_STATE_H
#include "NativeLobbyUi.h"
/* Separate versioned extension: the original 3784-byte UI ABI stays intact.
 * All addresses are within its existing 0x8000-byte state allocation. */
#define NATIVE_NETWORK_MAGIC 0x38544e4eU
#define NATIVE_NETWORK_VERSION 1U
#define NATIVE_NETWORK_BYTES 256U
#define NATIVE_NETWORK_OFFSET 3784U
#define NATIVE_NETWORK_BUFFER_BYTES 4040U
enum NativeJoinPhase { NJ_IDLE=0,NJ_QUEUED=1,NJ_DISCOVERING=2,NJ_DISPATCHED=3,NJ_CONNECTED=4,NJ_FAILED=5 };
#pragma pack(push,4)
typedef struct NativeNetworkState {
    unsigned magic,version,size;
    unsigned hostRevision,hostAlive,hostIdLow,hostIdHigh,hostLobbyPort,hostDiscoveryPort,hostPlayers,hostCapacity,hostPrivate;
    char hostName[64];
    unsigned clientConnected,clientIdLow,clientIdHigh,clientLobbyPort;
    unsigned joinSeq,joinAck,joinPhase,joinIdLow,joinIdHigh;
    char joinAddress[16],joinName[64];
    unsigned joinableMask,hostEndRevision,joinLobbyPort,reserved[4];
} NativeNetworkState;
#pragma pack(pop)
typedef char NativeNetworkSizeMustMatch[(sizeof(NativeNetworkState)==NATIVE_NETWORK_BYTES)?1:-1];
static NativeNetworkState *NativeNetwork(NativeUiState *ui){return (NativeNetworkState*)((unsigned char*)ui+NATIVE_NETWORK_OFFSET);}
#endif
