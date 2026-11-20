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
