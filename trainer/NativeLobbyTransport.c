#include "NativeLobbyTransport.h"
typedef unsigned U;
typedef unsigned char Byte;
typedef void *(__fastcall *PointerMethod)(void *,void *);
typedef U (__fastcall *ResultMethod)(void *,void *);
void _ReadWriteBarrier(void);
#pragma intrinsic(_ReadWriteBarrier)
static void *Address(U module,U va){return(void*)(module+va-0x400000U);}
static void *Virtual(void *object,U offset){return *(void**)(*(U*)object+offset);}
static U Dword(void *object,U offset){return *(U*)((Byte*)object+offset);}
static U Word(void *object,U offset){return *(unsigned short*)((Byte*)object+offset);}
static void *Pointer(void *object,U offset){return *(void**)((Byte*)object+offset);}
static U Type(U module,void *object,U preferred){return object&&*(U*)object==(U)Address(module,preferred);}

/* NetworkManager's hosting implementation uses its secondary interface at +4.
 * Its callback and server fields are at complete-object +E8 and +EC. */
__declspec(dllexport) U __cdecl NativeSoloBotRace(U module)
{
    void *manager = *(void **)Address(module, 0x22511bc);
    void *server;
    U begin, end, limit, human = 0, ai = 0;
    if (!Type(module, manager, 0x213a030)) return 0;
    if (!Type(module, Pointer(manager, 0xe8), 0x216a4e4)) return 0;
    server = Pointer(manager, 0xec);
    if (!Type(module, server, 0x2141dac)) return 0;
    if (Dword(server, 0x104) != 1 || Dword(server, 0x108) != 1 ||
        Dword(server, 0x120) != 0 || *((Byte *)server + 0x168) != 1) return 0;
    begin = Dword(server, 0xc);
    end = Dword(server, 0x10);
    limit = Dword(server, 0x14);
    if (!begin || ((begin | end | limit) & 3) || end < begin || limit < end ||
        end - begin < 8 || end - begin > 12 * 4) return 0;
    for (; begin < end; begin += 4) {
        void *racer = *(void **)begin;
        if (!Type(module, racer, 0x2119464)) return 0;
        if (*((Byte *)racer + 0x2290) != 1) continue;
        if (*((Byte *)racer + 0x22f9)) ++ai;
        else ++human;
    }
    return human == 1 && ai != 0;
}

static U SameUser(void *a,void *b){
    U i;for(i=0;i<16;i+=4)if(Dword(a,0xe8+i)!=Dword(b,0xe8+i))return 0;return 1;
}
/* Actual Room GetName is virtual+8=1355900 -> this+1C. Virtual0
 * returns the distinct Room+4 identity string and MUST NOT be used here. */
static U Name(void *room,char *out){
    U *text=(U*)((PointerMethod)Virtual(room,8))(room,0);U length,capacity,n,i;const char *bytes;
    if(!text)return 0;length=text[4];capacity=text[5];
    if(length>4096||capacity<length)return 0;
    bytes=capacity<16?(const char*)text:(const char*)text[0];if(!bytes)return 0;
    n=length<63?length:63;
    if(n<length)while(n&&(((Byte)bytes[n]&0xc0)==0x80))--n;
    for(i=0;i<n;++i)out[i]=bytes[i];out[n]=0;return 1;
}
/* Native Room IsFull1511780 uses exactly (end-begin)/4+1 owner.
 * These are real lobby users; native FillAI previews are not participants. */
static U PlayerCount(void *room,U capacity,U *players){
    U begin=Dword(room,0x64),end=Dword(room,0x68),limit=Dword(room,0x6c),others,i;
    if(end<begin||limit<end||((end-begin)&3)||((limit-begin)&3))return 0;
    others=(end-begin)/4;if(others>11||others+1>capacity)return 0;
    if(others&&!begin)return 0;
    for(i=0;i<others;++i)if(!((void**)begin)[i])return 0;
    *players=others+1;return 1;
}

U __cdecl NativeReadRoster(U module, void *screen, NativeRoster *roster)
{
    void *lobby, *room, **guests;
    U mode, i, j;
    if (!Type(module, screen, 0x214ac78) || !*((Byte *)screen + 0x284)) return 0;
    mode = Dword(screen, 0x280);
    if (mode != 2 && mode != 5) return 0;
    lobby = Pointer(screen, 0x270);
    if (!Type(module, lobby, 0x216a474)) return 0;
    room = ((PointerMethod)Virtual(lobby, 0x44))(lobby, 0);
    if (!Type(module, room, 0x216a510)) return 0;
    roster->capacity = Dword(room, 0x5c);
    if (roster->capacity < 2 || roster->capacity > NATIVE_ROSTER_CAPACITY ||
        !PlayerCount(room, roster->capacity, &roster->humans)) return 0;
    roster->roomLow = Dword(room, 0x50);
    roster->roomHigh = Dword(room, 0x54);
    roster->fill = *((Byte *)screen + 0x288) != 0;
    guests = (void **)Pointer(room, 0x64);
    for (i = 0; i < roster->humans; ++i) {
        void *user = i == 0 ? Pointer(room, 0x58) : guests[i - 1];
        U length, storage;
        const char *name;
        if (!Type(module, user, 0x216a568)) return 0;
        for (j = 0; j < 4; ++j)
            roster->users[i].identity[j] = Dword(user, 0xe8 + j * 4);
        /* User vtable slot 0 (1511EA0) returns the profile name at +A0. */
        length = Dword(user, 0xb0);
        storage = Dword(user, 0xb4);
        name = storage < 16 ? (const char *)user + 0xa0 : Pointer(user, 0xa0);
        j = 0;
        if (name && length <= storage && length <= 4096) {
            for (; j < length && j < 24 && name[j]; ++j) roster->users[i].name[j] = name[j];
            if (j < length) while (j && ((Byte)name[j] & 0xc0) == 0x80) --j;
        }
        roster->users[i].name[j] = 0;
    }
    return 1;
}

static U SameIdentity(const U *left, const U *right)
{
    return left[0] == right[0] && left[1] == right[1] &&
           left[2] == right[2] && left[3] == right[3];
}

__declspec(dllexport) U __cdecl NativeAssignRoster(
    const NativeRoster *roster, NativeRosterSlots *slots, U *sources)
{
    U i, j, assigned = 1, occupied = 1;
    if (!(roster->roomLow | roster->roomHigh) || !roster->humans ||
        roster->capacity > NATIVE_ROSTER_CAPACITY || roster->humans > roster->capacity)
        return 0;
    for (i = 0; i < roster->humans; ++i) {
        const U *id = roster->users[i].identity;
        if (!(id[0] | id[1] | id[2] | id[3])) return 0;
        for (j = 0; j < i; ++j)
            if (SameIdentity(id, roster->users[j].identity)) return 0;
    }
    for (i = 0; i < NATIVE_ROSTER_CAPACITY; ++i) sources[i] = 0;
    sources[0] = 1;

    if (slots->roomLow == roster->roomLow && slots->roomHigh == roster->roomHigh &&
        (slots->occupied & 1) && SameIdentity(slots->identities[0], roster->users[0].identity)) {
        for (i = 1; i < roster->capacity; ++i) {
            if (!(slots->occupied & (1U << i))) continue;
            for (j = 1; j < roster->humans; ++j) {
                if ((assigned & (1U << j)) ||
                    !SameIdentity(slots->identities[i], roster->users[j].identity)) continue;
                sources[i] = j + 1;
                occupied |= 1U << i;
                assigned |= 1U << j;
                break;
            }
        }
    }
    for (i = 1, j = 1; i < roster->humans; ++i) {
        if (assigned & (1U << i)) continue;
        while (occupied & (1U << j)) ++j;
        sources[j] = i + 1;
        occupied |= 1U << j;
    }
    slots->roomLow = roster->roomLow;
    slots->roomHigh = roster->roomHigh;
    slots->occupied = occupied;
    for (i = 0; i < roster->capacity; ++i)
        if (sources[i])
            for (j = 0; j < 4; ++j)
                slots->identities[i][j] = roster->users[sources[i] - 1].identity[j];
    return 1;
}
static void ClearObservation(NativeNetworkState *n){
    U i;
    n->hostAlive=0;n->hostIdLow=0;n->hostIdHigh=0;n->hostLobbyPort=0;n->hostDiscoveryPort=0;
    n->hostPlayers=0;n->hostCapacity=0;n->hostPrivate=0;
    for(i=0;i<sizeof(n->hostName);++i)n->hostName[i]=0;
    n->clientConnected=0;n->clientIdLow=0;n->clientIdHigh=0;n->clientLobbyPort=0;
}
void __cdecl NativeTransportTick(U module,void *screen,NativeUiState *ui){
    NativeNetworkState *n;U sequence,capacity,players,lobbyPort,low,high;void *lobby,*room,*owner,*local,*transport;
    if(!module||!ui||ui->magic!=NATIVE_UI_MAGIC||ui->version!=NATIVE_UI_VERSION||ui->size<sizeof(*ui))return;
    n=NativeNetwork(ui);
    if(!n->magic){
        /* Native allocation starts zeroed. Publish only the extension header;
         * do not reset pending commands owned by the managed/LAN bridge. */
        n->version=NATIVE_NETWORK_VERSION;n->size=sizeof(*n);_ReadWriteBarrier();
        *(volatile U*)&n->magic=NATIVE_NETWORK_MAGIC;
    }
    if(n->magic!=NATIVE_NETWORK_MAGIC||n->version!=NATIVE_NETWORK_VERSION||n->size!=sizeof(*n))return;
    sequence=n->hostRevision;if(sequence&1)++sequence;++sequence;
    *(volatile U*)&n->hostRevision=sequence;*(volatile U*)&n->hostEndRevision=sequence;_ReadWriteBarrier();
    ClearObservation(n);
    if(!Type(module,screen,0x214ac78))goto done;
    lobby=Pointer(screen,0x270);if(!Type(module,lobby,0x216a474))goto done;
    /* Public GetRoom150ECA0: borrowed pointer, no arguments/plain RET. */
    room=((PointerMethod)Virtual(lobby,0x44))(lobby,0);
    if(!Type(module,room,0x216a510))goto done;
    owner=Pointer(room,0x58);local=Pointer(lobby,8);
    if(!Type(module,owner,0x216a568)||!Type(module,local,0x216a568))goto done;
    capacity=Dword(room,0x5c);if(capacity<2||capacity>12||!PlayerCount(room,capacity,&players))goto done;
    lobbyPort=Word(room,0x78);low=Dword(room,0x50);high=Dword(room,0x54);
    if(!lobbyPort||!(low|high))goto done;
    if(*((Byte*)screen+0x284)){
        if(!SameUser(owner,local)||!Name(room,n->hostName))goto done;
        n->hostIdLow=low;n->hostIdHigh=high;n->hostLobbyPort=lobbyPort;n->hostDiscoveryPort=6529;
        n->hostPlayers=players;n->hostCapacity=capacity;n->hostPrivate=(ui->policyBits&NUI_PRIVATE)?1U:0U;
        n->hostAlive=1;
    }else{
        /* State5 is set only after server reply flag2D and result20==0 at
         * 150E095..150E0AF. A30860 adds a current underlying transport check.
         * UI navigation/GetRoom alone cannot prove an accepted connection. */
        if(Dword(lobby,0x10)!=5||!*((Byte*)lobby+0x2d)||Dword(lobby,0x20)!=0||SameUser(owner,local))goto done;
        transport=Pointer(lobby,0x70);if(!Type(module,transport,0x216a364))goto done;
        if(((ResultMethod)Virtual(transport,0x10))(transport,0)!=0)goto done;
        n->clientIdLow=low;n->clientIdHigh=high;n->clientLobbyPort=lobbyPort;n->clientConnected=1;
    }
done:
    _ReadWriteBarrier();++sequence;*(volatile U*)&n->hostEndRevision=sequence;
    _ReadWriteBarrier();*(volatile U*)&n->hostRevision=sequence;
}
