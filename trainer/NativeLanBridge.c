#include "NativeLanBridge.h"
#include "NativeNetworkState.h"
typedef unsigned U;
typedef struct Vector { void **b, **e, **cap; } Vector;
typedef Vector *(__fastcall *Enumerate)(void *,void *,Vector *);
typedef void *(__fastcall *GetName)(void *,void *);
typedef int (__fastcall *Flag)(void *,void *);
typedef void (__fastcall *DestroyVector)(Vector *,void *);
typedef void (__fastcall *Click)(void *,void *,void *,void *);
static struct Pending { U module,ticks,seen,active,direct,directory,awaiting,low,high,port; unsigned char destination[128]; char query[64]; } gLan;
static void *At(U m,U va){return(void*)(m+va-0x400000U);}
static void *Virtual(void*p,U s){return *(void**)(*(U*)p+s);}
static U Length(const char*p,U cap){U n=0;while(n<cap&&p[n])++n;return n;}
static void Copy(char*d,const char*s,U cap){U n=0;while(n+1<cap&&s[n]){d[n]=s[n];++n;}d[n]=0;}
static int ValidState(NativeUiState*s){return s&&s->magic==NATIVE_UI_MAGIC&&s->version==NATIVE_UI_VERSION&&s->size==sizeof(*s);}
static void Status(NativeUiState*s,U code,const char*text){s->statusCode=code;Copy(s->statusText,text,sizeof(s->statusText));}
static int SelectScreen(U m,void*s){return s&&*(U*)s==(U)At(m,0x214db14)&&*(void**)At(m,0x225147c)==s;}
static char Lower(char c){return c>='A'&&c<='Z'?(char)(c+32):c;}
static void Retain(void*control){volatile long *strong=(volatile long*)((char*)control+4);__asm{mov eax,strong
    lock inc dword ptr[eax]
}}
static int ExactName(U m,void*room){
    U *s,n,i;const char *data;
    if(!room||*(U*)room!=(U)At(m,0x216a510))return 0;
    s=(U*)((GetName)Virtual(room,8))(room,0);if(!s)return 0;
    n=Length(gLan.query,sizeof(gLan.query));if(s[4]!=n||s[5]<n)return 0;
    data=s[5]<16?(const char*)s:(const char*)s[0];if(!data)return 0;
    for(i=0;i<n;++i)if(Lower(data[i])!=Lower(gLan.query[i]))return 0;
    return 1;
}
/* Native row retains its room and Join widget; never use a Finder room after
 * freeing its temporary vector. Match the copied native ID against rows. */
static U JoinRow(U m,void*screen,U low,U high){
    U *b=*(U**)((char*)screen+0x278),*e=*(U**)((char*)screen+0x27c),n,i;
    if((U)e<(U)b||(((U)e-(U)b)&3))return 0;n=((U)e-(U)b)/4;if(n>1024)return 0;
    for(i=0;i<n;++i){void *row=(void*)b[i],*r,*button,*control;
        if(!row||*(void**)((char*)row+0x64)!=screen)continue;
        r=*(void**)((char*)row+0x68);
        if(!r||*(U*)r!=(U)At(m,0x216a510))continue;
        if(*(U*)((char*)r+0x50)!=low||*(U*)((char*)r+0x54)!=high)continue;
        button=*(void**)((char*)row+0x44);control=*(void**)((char*)row+0x48);
        if(!button||!control||*(void**)((char*)button+0x14)!=r)continue;
        Retain(control);((Click)At(m,0xef03e0))(screen,0,button,control);return 1;
    }return 0;
}
void __cdecl NativeLanCancel(void){gLan.active=0;}
/* Explicit IPv4 only. The supported native socket constructor uses AF_INET.
 * Decimal octets only; no DNS lookup, shorthand, scope, port or hostname. */
__declspec(dllexport) U __cdecl NativeLanIPv4(const char *text,unsigned char *destination){
    U part,n,value,i=0;unsigned char octets[4];
    if(!text||!destination)return 0;
    for(part=0;part<4;++part){
        value=0;n=0;
        while(text[i]>='0'&&text[i]<='9'&&n<3){value=value*10+(U)(text[i]-'0');++n;++i;}
        if(!n||value>255||(part<3?text[i]!='.':text[i]!=0))return 0;
        octets[part]=(unsigned char)value;if(part<3)++i;
    }
    if(!octets[0]||octets[0]>=224)return 0;
    for(i=0;i<128;++i)destination[i]=0;
    destination[0]=2;destination[2]=0x19;destination[3]=0x81;
    for(i=0;i<4;++i)destination[4+i]=octets[i];return 1;
}
/* Replaces only Finder's periodic discovery call. All other discoverers keep
 * their original destination, including host announcements and room refresh.
 * Source evidence: preferred VA 150CE75, 14FD410 -> v18(Discoverer+28).
 */
__declspec(dllexport) void __cdecl NativeLanDispatch(U module,void *finder,NativeUiState *state){
    typedef void (__fastcall *Discover)(void*,void*);
    typedef void (__fastcall *Direct)(void*,void*,const void*);
    void *discoverer=*(void**)((char*)finder+4),*screen;
    screen=*(void**)At(module,0x225147c);
    if(gLan.active&&!gLan.awaiting&&gLan.direct&&gLan.module==module&&ValidState(state)&&
       SelectScreen(module,screen)&&*(void**)((char*)screen+0x264)==finder&&
       discoverer&&*(U*)discoverer==(U)At(module,0x2169be0)){
        ((Direct)Virtual(discoverer,0x18))(discoverer,0,gLan.destination);
    }else ((Discover)Virtual(discoverer,0x1c))(discoverer,0);
}
static U Begin(U module,void*local,NativeUiState*state,U direct,const char *query){
    void *button,*control;U n;
    if(!ValidState(state))return 0;
    gLan.active=0;n=Length(query,sizeof(state->query));
    if(!n||n==sizeof(state->query)){Status(state,201,"Enter a room name or an IPv4 address first.");return 0;}
    if(direct&&!NativeLanIPv4(query,gLan.destination)){Status(state,210,"Enter four decimal IPv4 octets, without a port or hostname.");return 0;}
    if(!local||*(U*)local!=(U)At(module,0x2145bac)){Status(state,202,"Open the native Create / Join menu first.");return 0;}
    button=*(void**)((char*)local+0x26c);control=*(void**)((char*)local+0x270);
    if(!button||!control){Status(state,203,"Native LAN browser is not ready.");return 0;}
    Copy(gLan.query,query,sizeof(gLan.query));gLan.module=module;gLan.ticks=0;gLan.seen=0;gLan.direct=direct!=0;gLan.directory=direct==2;gLan.awaiting=0;gLan.active=1;
    if(gLan.directory){NativeNetworkState *network=NativeNetwork(state);gLan.low=network->joinIdLow;gLan.high=network->joinIdHigh;gLan.port=network->joinLobbyPort;network->joinPhase=NJ_DISCOVERING;}
    Status(state,204,direct?"Opening native browser; querying the explicit IPv4 host.":"Opening native LAN browser; looking for an exact room name.");
    Retain(control);((Click)At(module,0xdc76e0))(local,0,button,control);return 1;
}
U __cdecl NativeLanBegin(U module,void*local,NativeUiState*state){return Begin(module,local,state,0,state->query);}
__declspec(dllexport) U __cdecl NativeLanBeginDirect(U module,void*local,NativeUiState*state){return Begin(module,local,state,1,state->query);}
static int NetworkValid(NativeNetworkState *n){return n->magic==NATIVE_NETWORK_MAGIC&&n->version==NATIVE_NETWORK_VERSION&&n->size==sizeof(*n);}
static void JoinFailed(NativeUiState *state,const char *message){gLan.active=0;if(gLan.directory)NativeNetwork(state)->joinPhase=NJ_FAILED;Status(state,211,message);}
void __cdecl NativeLanTick(U module,void*screen,NativeUiState*state){
    void *finder;Vector v;U b,e,c,n,i,matches=0,low=0,high=0;NativeNetworkState *network;
    if(!ValidState(state))return;
    network=NativeNetwork(state);
    if(NetworkValid(network)&&network->joinSeq!=network->joinAck){
        U sequence=network->joinSeq;
        if(state->page!=NUI_FIND||network->joinPhase!=NJ_QUEUED||!(network->joinIdLow|network->joinIdHigh)||network->joinLobbyPort<1||network->joinLobbyPort>65535){
            network->joinPhase=NJ_FAILED;Status(state,211,"Native join request is stale or invalid. Search again.");
        }else if(!Begin(module,screen,state,2,network->joinAddress))network->joinPhase=NJ_FAILED;
        network->joinAck=sequence;
    }
    if(!gLan.active)return;
    if(!ValidState(state)||module!=gLan.module){gLan.active=0;return;}
    if(gLan.awaiting){
        if(NetworkValid(network)&&network->clientConnected&&network->clientIdLow==gLan.low&&network->clientIdHigh==gLan.high&&network->clientLobbyPort==gLan.port){
            gLan.active=0;network->joinPhase=NJ_CONNECTED;Status(state,212,"Native lobby connection confirmed. Race synchronization is not established.");return;
        }
        if(++gLan.ticks>7200||*(U*)screen==(U)At(module,0x2145bac))JoinFailed(state,"Native lobby connection was not confirmed. Retry from the room list.");
        return;
    }
    if(++gLan.ticks>900){gLan.active=0;if(gLan.directory)network->joinPhase=NJ_FAILED;Status(state,205,"Native search ended without a matching room. Retry or use the browser.");return;}
    if(!SelectScreen(module,screen)){
        if(gLan.seen){gLan.active=0;if(gLan.directory)network->joinPhase=NJ_FAILED;Status(state,206,"Native search stopped after leaving the browser.");}
        return;
    }
    gLan.seen=1;
    if(gLan.ticks%15)return;
    finder=*(void**)((char*)screen+0x264);if(!finder)return;
    v.b=0;v.e=0;v.cap=0;((Enumerate)Virtual(finder,8))(finder,0,&v);
    b=(U)v.b;e=(U)v.e;c=(U)v.cap;
    if(e<b||c<e||((e-b)&3)||((e-b)/4)>1024){
        ((DestroyVector)At(module,0x4eaa00))(&v,0);gLan.active=0;
        if(gLan.directory)network->joinPhase=NJ_FAILED;
        Status(state,207,"Native LAN discovery returned an unexpected list; use the native browser.");return;
    }
    n=(e-b)/4;
    for(i=0;i<n;++i){void *r=v.b[i];
        if(gLan.direct){
            U j;int match=1;
            if(!r||*(U*)r!=(U)At(module,0x216a510)||*(unsigned short*)((char*)r+0x80)!=2)continue;
            for(j=0;j<4;++j)if(*((unsigned char*)r+0x84+j)!=gLan.destination[4+j])match=0;
            if(!match)continue;
            if(gLan.directory&&(*(U*)((char*)r+0x50)!=gLan.low||*(U*)((char*)r+0x54)!=gLan.high||*(unsigned short*)((char*)r+0x78)!=gLan.port))continue;
        }
        else if(!ExactName(module,r))continue;
        if(((Flag)Virtual(r,0x20))(r,0))continue;
        ++matches;low=*(U*)((char*)r+0x50);high=*(U*)((char*)r+0x54);
    }
    ((DestroyVector)At(module,0x4eaa00))(&v,0);
    if(matches>1){gLan.active=0;if(gLan.directory)network->joinPhase=NJ_FAILED;Status(state,208,"Several native rooms match. Select one in the browser.");return;}
    if(matches==1&&JoinRow(module,screen,low,high)){
        if(gLan.directory){gLan.awaiting=1;gLan.ticks=0;network->joinPhase=NJ_DISPATCHED;}else gLan.active=0;
        Status(state,209,"Native join request dispatched. Connection is not yet confirmed.");
    }
}
