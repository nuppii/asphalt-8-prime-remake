#include "NativeLanBridge.h"
typedef unsigned U;
typedef struct Vector { void **b, **e, **cap; } Vector;
typedef Vector *(__fastcall *Enumerate)(void *,void *,Vector *);
typedef void *(__fastcall *GetName)(void *,void *);
typedef int (__fastcall *Flag)(void *,void *);
typedef void (__fastcall *DestroyVector)(Vector *,void *);
typedef void (__fastcall *Click)(void *,void *,void *,void *);
static struct Pending { U module,ticks,seen,active; char query[64]; } gLan;
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
    s=(U*)((GetName)Virtual(room,0))(room,0);if(!s)return 0;
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
U __cdecl NativeLanBegin(U module,void*local,NativeUiState*state){
    void *button,*control;U n;
    if(!ValidState(state))return 0;
    gLan.active=0;n=Length(state->query,sizeof(state->query));
    if(!n||n==sizeof(state->query)){Status(state,201,"Enter an exact LAN room name first.");return 0;}
    if(!local||*(U*)local!=(U)At(module,0x2145bac)){Status(state,202,"Open the native Create / Join menu first.");return 0;}
    button=*(void**)((char*)local+0x26c);control=*(void**)((char*)local+0x270);
    if(!button||!control){Status(state,203,"Native LAN browser is not ready.");return 0;}
    Copy(gLan.query,state->query,sizeof(gLan.query));gLan.module=module;gLan.ticks=0;gLan.seen=0;gLan.active=1;
    Status(state,204,"Opening native LAN browser; looking for an exact room name.");
    Retain(control);((Click)At(module,0xdc76e0))(local,0,button,control);return 1;
}
void __cdecl NativeLanTick(U module,void*screen,NativeUiState*state){
    void *finder;Vector v;U b,e,c,n,i,matches=0,low=0,high=0;
    if(!gLan.active)return;
    if(!ValidState(state)||module!=gLan.module){gLan.active=0;return;}
    if(++gLan.ticks>900){gLan.active=0;Status(state,205,"LAN search ended. Select a room in the native browser or retry.");return;}
    if(!SelectScreen(module,screen)){
        if(gLan.seen){gLan.active=0;Status(state,206,"LAN search stopped after leaving the native browser.");}
        return;
    }
    gLan.seen=1;
    if(gLan.ticks%15)return;
    finder=*(void**)((char*)screen+0x264);if(!finder)return;
    v.b=0;v.e=0;v.cap=0;((Enumerate)Virtual(finder,8))(finder,0,&v);
    b=(U)v.b;e=(U)v.e;c=(U)v.cap;
    if(e<b||c<e||((e-b)&3)||((e-b)/4)>1024){
        ((DestroyVector)At(module,0x4eaa00))(&v,0);gLan.active=0;
        Status(state,207,"Native LAN discovery returned an unexpected list; use the native browser.");return;
    }
    n=(e-b)/4;
    for(i=0;i<n;++i){void *r=v.b[i];
        if(!ExactName(module,r))continue;
        if(((Flag)Virtual(r,0x20))(r,0))continue;
        ++matches;low=*(U*)((char*)r+0x50);high=*(U*)((char*)r+0x54);
    }
    ((DestroyVector)At(module,0x4eaa00))(&v,0);
    if(matches>1){gLan.active=0;Status(state,208,"Several LAN rooms have that name. Select one in the native browser.");return;}
    if(matches==1&&JoinRow(module,screen,low,high)){
        gLan.active=0;Status(state,209,"Native join request dispatched. Connection is not yet confirmed.");
    }
}
