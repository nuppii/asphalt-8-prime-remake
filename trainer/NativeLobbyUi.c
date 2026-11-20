/* Native Asphalt 8 4.8.0.7 controls. No CRT or imported functions.
 * API evidence: Documents/Codex/asphalt8-multiplayer-work/NATIVE-UI-API.md.
 * Ownership and every engine call stay on the game's UI thread.
 */
#include "NativeLobbyUi.h"
#include "NativeLanBridge.h"
#include "NativeLobbyPolicy.h"
#include "NativeLobbyTransport.h"
#include "NativeNetworkState.h"

typedef unsigned U32;
typedef struct Pair { void *ptr; void *control; } Pair;
/* IEEE754 vector bits avoid floating-point runtime support in this adapter. */
typedef struct Vec2 { U32 x,y; } Vec2;
typedef struct Delegate { void *fn; int adjustment; U32 flags; void *object; } Delegate;
typedef void *(__fastcall *Factory)(Pair *, U32 *);
typedef void *(__fastcall *CastButton)(Pair *, Pair *);
typedef void *(__fastcall *AssignText)(U32 *,void *,const char *);
typedef void (__fastcall *Release)(void *,void *);
typedef Pair *(__fastcall *Lookup)(void *,void *, Pair *, U32 *);
typedef void (__fastcall *PairArg)(void *,void *,Pair *);
typedef void (__fastcall *BoolArg)(void *,void *,int);
typedef void (__fastcall *VecArg)(void *,void *,Vec2 *);
typedef void (__fastcall *TextArg)(void *,void *,U32 *,int);
typedef void (__fastcall *NameArg)(void *,void *,U32 *);
typedef void (__cdecl *BindCall)(void);

enum { A_PAGE=1, A_TOGGLE, A_GRID, A_LAPS, A_EDIT, A_FIND, A_FRIENDS,
       A_KEY, A_SPACE, A_DELETE, A_CANCEL, A_CONFIRM,
       A_SELECT_ROOM,A_JOIN_ROOM,A_SELECT_FRIEND,A_INVITE_FRIEND,
       A_SHIFT,A_PASTE,A_SOCIAL_PAGE,A_REQUEST,A_RESULT_PAGE,A_LAN_JOIN,A_CLEAR,A_RESET_RACE,A_COPY_ID,A_DIRECT_JOIN,A_ROOM_LIST,A_ROSTER_PAGE };
enum { E_ROOM=1,E_QUERY=2,E_NICKNAME=3,E_PASSWORD=4,E_DIRECTORY=5,E_FRIEND_ID=6 };
typedef struct Control {
    Pair visual, button, connection;
    U32 generation, page, action, value, attached, selected;
    char label[192];
} Control;
static U32 gModule,gGeneration,gCount,gKeyboardAttempt,gEdit,gReturnPage;
static void *gRoot;
static NativeUiState *gState;
static Pair gCenter,gKeyboard,gRootPair,gSceneRoot,gHostCenter,gPanel;
static Control gControls[128];
static char gEditText[1024];
static U32 gBuildFailures;
static U32 gKeyboardAttached;
static U32 gNamespacesLoaded;
static U32 gLayoutDirty;
static U32 gLowercase,gResultOffset;
static U32 gLaunchLan;
static U32 gEditEpoch,gCaret,gSelectAll;
static U32 gRoomsExpanded;
enum { ROSTER_SLOTS = 8, ROSTER_PREV, ROSTER_STATUS, ROSTER_NEXT, ROSTER_CONTROLS };
static Pair gRosterList, gRosterPanel, gRosterStock;
static Control gRosterControls[ROSTER_CONTROLS];
static NativeRosterSlots gRosterSlots;
static void *gRosterSource, *gRosterScreen;
static U32 gRosterPage, gRosterCount, gRosterVisible, gRosterNamespace, gRosterAttached;

void * __cdecl memset(void *p,int value,unsigned count);
#pragma function(memset)
void * __cdecl memset(void *p,int value,unsigned count) {
    volatile unsigned char *q=(volatile unsigned char*)p;
    while(count--)*q++=(unsigned char)value;
    return p;
}
void * __cdecl memcpy(void *destination,const void *source,unsigned count);
#pragma function(memcpy)
void * __cdecl memcpy(void *destination,const void *source,unsigned count) {
    volatile unsigned char *out=(volatile unsigned char*)destination;
    const unsigned char *in=(const unsigned char*)source;
    while(count--)*out++=*in++;
    return destination;
}
static void Zero(void *p,U32 n) { volatile unsigned char *q=(volatile unsigned char*)p; while(n--) *q++=0; }
static U32 Len(const char *s,U32 max) { U32 n=0; while(n<max && s[n]) ++n; return n; }
static void Copy(char *d,const char *s,U32 n) { U32 i=0; if(!n)return; while(i+1<n && s[i]) {d[i]=s[i];++i;} if(s[i])while(i && ((unsigned char)s[i]&0xc0)==0x80)--i;d[i]=0; }
static int Equal(const char *a,const char *b) { U32 i=0; while(a[i]&&a[i]==b[i])++i; return a[i]==b[i]; }
static void Append(char *d,const char *s,U32 n) { U32 k=Len(d,n); if(k<n)Copy(d+k,s,n-k); }
static void Number(char *d,U32 v) { char t[12]; U32 i=0,j=0; do {t[i++]=(char)('0'+v%10);v/=10;}while(v); while(i)d[j++]=t[--i];d[j]=0; }
static void *Api(U32 va) {return (void *)(gModule+va-0x400000U);}
static void *Virtual(void *p,U32 slot) {return *(void **)(*(U32*)p+slot);}
static void Text(U32 *r,const char *s) { *r=0; ((AssignText)Api(0x12a4f90))(r,0,s); }
static void DropText(U32 *r) {if(*r)((Release)Api(0x4bcd00))(r,0);}
static void DropPair(Pair *p);
static void Find(void *root,const char *name,Pair *out);
static void Name(void *widget,const char *name) {
    U32 reference;Text(&reference,name);((NameArg)Virtual(widget,0x4c))(widget,0,&reference);DropText(&reference);
}
static void ConfigureFrame(void *root,const char *name,int keyboard) {
    Pair frame;Find(root,name,&frame);
    if(frame.ptr){
        /* Supported FixedSizeContainer layout: percentages, with aspect
         * constraint disabled. These are our generated resource instances. */
        *((unsigned char*)frame.ptr+0xb4)=0;
        *(U32*)((char*)frame.ptr+0xac)=keyboard?0x3f3851ecU:0x3f666666U;
        *(U32*)((char*)frame.ptr+0xb0)=keyboard?0x3f000000U:0x3f666666U;
    }
    DropPair(&frame);
}
static void DropPair(Pair *p) {if(p->control || p->ptr)((Release)Api(0x4ace60))(p,0);Zero(p,sizeof(*p));}
static void Visible(void *p,int v) {if(p)((BoolArg)Virtual(p,0x14))(p,0,v);}
static void Enabled(void *p,int v) {if(p)((BoolArg)Virtual(p,0x80))(p,0,v);}
static void Selection(Control *c,int value) {
    if(c->button.ptr && c->selected!=(U32)value){((BoolArg)Virtual(c->button.ptr,0x114))(c->button.ptr,0,value);c->selected=value;}
}
static void Momentary(void *button) {
    /* Native ButtonWidget: v118 disables automatic toggle-on-click;
     * v114 changes selection and refreshes the original visual state. */
    ((BoolArg)Virtual(button,0x118))(button,0,0);
    ((BoolArg)Virtual(button,0x114))(button,0,0);
}
static void Find(void *root,const char *name,Pair *out) {
    U32 r;Zero(out,sizeof(*out));if(!root)return;
    Text(&r,name);((Lookup)Api(0x1468a70))(root,0,out,&r);DropText(&r);
}
static void Label(Control *c,const char *text) {
    U32 r;if(!c->button.ptr || Equal(c->label,text))return;
    Text(&r,text);((TextArg)Virtual(c->button.ptr,0xb0))(c->button.ptr,0,&r,0);
    DropText(&r);Copy(c->label,text,sizeof(c->label));
}
/* DFA410 has ECX/EDX arguments and one caller-cleaned stack argument. */
static void Bind(Pair *connection,void *signal,Delegate *d) {
    void *fn=Api(0xdfa410);
    __asm {
        mov ecx,connection
        mov edx,signal
        push d
        call fn
        add esp,4
    }
}
static void Request(U32 kind,U32 fields) {
    NativeUiState *s=gState;if(!s)return;
    if(s->requestSeq==s->ackSeq || kind!=NUI_REQUEST_POLICY || s->requestKind!=kind)s->fieldMask=0;
    s->fieldMask|=fields;s->requestKind=kind;
    ++s->requestSeq; /* x86 store ordering: payload precedes publication. */
}
static void Refresh(void);
static void Click(Control *c);
/* Native event owns a by-value shared_ptr<ButtonWidget>. Release it just
 * like DC7500 does, even though we only dispatch an action identifier. */
static void __cdecl OnClick(Control *c,Pair *argument) {
    if(c && c->action==A_ROSTER_PAGE && gRosterPanel.ptr){
        if(c->value && (gRosterPage+1)*ROSTER_SLOTS<gRosterCount)++gRosterPage;
        if(!c->value && gRosterPage)--gRosterPage;
    }else if(c && c->generation==gGeneration && gRoot && gState)Click(c);
    DropPair(argument);
}
static __declspec(naked) void NativeClick(void) {
    __asm {
        push ebp
        mov ebp,esp
        lea eax,[ebp+8]
        push eax
        push ecx
        call OnClick
        add esp,8
        pop ebp
        ret 8
    }
}
static Control *Make(void *parent,const char *path,const char *child,U32 page,U32 action,U32 value,const char *label) {
    Control *c;Pair generic;Delegate d;U32 r;Vec2 size;char name[48],number[12];
    if(gCount>=128)return 0;c=&gControls[gCount];Zero(c,sizeof(*c));
    Text(&r,path);((Factory)Api(0x510bb0))(&c->visual,&r);DropText(&r);
    if(!c->visual.ptr){++gBuildFailures;return 0;}
    Copy(name,"offline_lobby_control_",sizeof(name));Number(number,gCount);Append(name,number,sizeof(name));Name(c->visual.ptr,name);
    Find(c->visual.ptr,child,&generic);
    if(generic.ptr)((CastButton)Api(0x500890))(&c->button,&generic);
    DropPair(&generic);
    if(!c->button.ptr){DropPair(&c->visual);++gBuildFailures;return 0;}
    Momentary(c->button.ptr);
    c->generation=gGeneration;c->page=page;c->action=action;c->value=value;
    Label(c,label);Enabled(c->button.ptr,action!=0);
    size.x=page==NUI_MAIN?0x42200000U:0x42700000U;size.y=0x40800000U;((VecArg)Virtual(c->visual.ptr,0x34))(c->visual.ptr,0,&size);
    if(action){Zero(&d,sizeof(d));d.fn=NativeClick;d.object=c;Bind(&c->connection,(char*)c->button.ptr+0x394,&d);}
    (void)parent;++gCount;return c;
}
static Control *Button(U32 page,U32 action,U32 value,const char *label) {
    return Make(gRoot,"generic_templates/template_bottom_bar_button","tab_button",page,action,value,label);
}
static void DropConnection(Pair *p) {
    /* boost connection tokens here retain only the weak count (+8), as
     * shown by native move/release532EB0. Widgets/signals are released first. */
    void *ctrl=p->control;if(ctrl){volatile long *weak=(volatile long*)((char*)ctrl+8);long old;
        __asm {
            mov eax,weak
            mov ecx,-1
            lock xadd [eax],ecx
            mov old,ecx
        }
        if(old==1){void *fn=Virtual(ctrl,8);((Release)fn)(ctrl,0);}}
    Zero(p,sizeof(*p));
}

static void DropRoster(void)
{
    U32 i;
    gRosterScreen=0;
    if(gRosterAttached){
        Visible(gRosterStock.ptr,1);
        ((PairArg)Virtual(gRosterList.ptr,0xe8))(gRosterList.ptr,0,&gRosterPanel);
    }
    for(i=0;i<ROSTER_CONTROLS;++i)DropPair(&gRosterControls[i].button);
    DropPair(&gRosterPanel);
    for(i=0;i<ROSTER_CONTROLS;++i)DropConnection(&gRosterControls[i].connection);
    DropPair(&gRosterList);DropPair(&gRosterStock);
    Zero(gRosterControls,sizeof(gRosterControls));
    Zero(&gRosterSlots,sizeof(gRosterSlots));
    gRosterSource=0;gRosterPage=0;gRosterVisible=0;gRosterCount=0;gRosterAttached=0;
}

static U32 BuildRoster(void *outer)
{
    U32 text,i;
    Pair row;
    Find(outer,"player_list",&gRosterList);
    if(!gRosterList.ptr)return 0;
    Find(gRosterList.ptr,"Stack Container",&gRosterStock);
    if(!gRosterStock.ptr || *(void**)((char*)gRosterStock.ptr+0x3c)!=gRosterList.ptr)return 0;
    Find(gRosterStock.ptr,"upper_player_row",&row);
    if(!row.ptr)return 0;
    DropPair(&row);
    if(!gRosterNamespace){
        Text(&text,"offline_lobby_roster");((void (__stdcall *)(U32))Api(0xa6a280))(text);
        gRosterNamespace=1;
    }
    Text(&text,"offline_lobby_roster/screen_mp_private_lobby_waitroom");
    ((Factory)Api(0x510bb0))(&gRosterPanel,&text);DropText(&text);
    if(!gRosterPanel.ptr)return 0;
    for(i=0;i<ROSTER_CONTROLS;++i){
        char name[40],num[12];
        Pair generic;
        Control *c=&gRosterControls[i];
        Vec2 limit={0x447a0000U,0x40900000U};
        if(i<ROSTER_SLOTS){Copy(name,"offline_roster_slot_",sizeof(name));Number(num,i);Append(name,num,sizeof(name));}
        else Copy(name,i==ROSTER_PREV?"offline_roster_prev":i==ROSTER_NEXT?"offline_roster_next":"offline_roster_status",sizeof(name));
        Find(gRosterPanel.ptr,name,&generic);
        if(generic.ptr)((CastButton)Api(0x500890))(&c->button,&generic);
        DropPair(&generic);
        if(!c->button.ptr)return 0;
        ((VecArg)Virtual(c->button.ptr,0x30))(c->button.ptr,0,&limit);
        Momentary(c->button.ptr);
        Enabled(c->button.ptr,0);
        if(i==ROSTER_PREV||i==ROSTER_NEXT){
            Delegate d;
            c->action=A_ROSTER_PAGE;c->value=i==ROSTER_NEXT;
            Zero(&d,sizeof(d));d.fn=NativeClick;d.object=c;
            Bind(&c->connection,(char*)c->button.ptr+0x394,&d);
        }
    }
    ((PairArg)Api(0x14684c0))(gRosterList.ptr,0,&gRosterPanel);
    Visible(gRosterStock.ptr,0);
    gRosterAttached=1;
    return 1;
}

/* One page of eight visible cells; hide the unused tail on the final page. */
__declspec(dllexport) U32 __cdecl NativeRosterPage(U32 requested,U32 humans,U32 *page)
{
    U32 count=requested<NATIVE_UI_MAX_RACERS+1?requested:0;
    if(count<humans)count=humans;
    if(*page>=(count+ROSTER_SLOTS-1)/ROSTER_SLOTS)*page=count?(count-1)/ROSTER_SLOTS:0;
    return count;
}

static void TickRoster(U32 module,void *screen,NativeUiState *shared)
{
    NativeRoster roster;
    void *outer;
    U32 i,first,visible,layout=0,newScreen,sources[NATIVE_ROSTER_CAPACITY];
    char label[128],num[12];
    if(shared->magic!=NATIVE_UI_MAGIC||shared->version!=NATIVE_UI_VERSION||shared->size<sizeof(*shared)||
       !shared->grid || shared->grid>NATIVE_UI_MAX_RACERS || !NativeReadRoster(module,screen,&roster)){
        if(gRosterSource)DropRoster();
        return;
    }
    /* WifiRoom stores its waitroom resource at +0x300 (E97044). */
    outer=*(void**)((char*)screen+0x300);
    if(!outer)return;
    newScreen=gRosterSource!=outer||gRosterScreen!=screen;
    if(newScreen)DropRoster();
    if(!NativeAssignRoster(&roster,&gRosterSlots,sources)){
        if(gRosterSource)DropRoster();
        return;
    }
    if(newScreen){
        gRosterSource=outer;gRosterScreen=screen;
        if(!BuildRoster(outer)){DropRoster();gRosterSource=outer;gRosterScreen=screen;return;}
        layout=1;
    }
    if(!gRosterPanel.ptr)return;
    gRosterCount=NativeRosterPage(shared->grid,roster.humans,&gRosterPage);
    first=gRosterPage*ROSTER_SLOTS;
    visible=gRosterCount-first;
    if(visible>ROSTER_SLOTS)visible=ROSTER_SLOTS;
    if(gRosterVisible!=visible){
        for(i=0;i<ROSTER_SLOTS;++i)Visible(gRosterControls[i].button.ptr,i<visible);
        gRosterVisible=visible;layout=1;
    }
    for(i=0;i<visible;++i){
        U32 slot=first+i;
        Number(label,slot+1);Append(label,"  ",sizeof(label));
        if(slot>=roster.capacity)Append(label,"UNAVAILABLE",sizeof(label));
        else if(sources[slot]){
            const char *name=roster.users[sources[slot]-1].name;
            Append(label,*name?name:slot?"PLAYER":"HOST",sizeof(label));
        }
        else Append(label,roster.fill?"AI SLOT":"OPEN SLOT",sizeof(label));
        Label(&gRosterControls[i],label);
    }
    Number(label,first+1);Append(label,"-",sizeof(label));Number(num,first+visible);Append(label,num,sizeof(label));
    Append(label," / ",sizeof(label));Number(num,shared->grid);Append(label,num,sizeof(label));
    if(shared->grid!=roster.capacity){
        Append(label,"   |   ROOM CAPACITY ",sizeof(label));Number(num,roster.capacity);Append(label,num,sizeof(label));
    }
    Label(&gRosterControls[ROSTER_STATUS],label);
    Enabled(gRosterControls[ROSTER_PREV].button.ptr,first!=0);
    Enabled(gRosterControls[ROSTER_NEXT].button.ptr,first+visible<gRosterCount);
    if(layout){
        ((Release)Virtual(gRosterList.ptr,0xa8))(gRosterList.ptr,0);
        ((BoolArg)Virtual(outer,0x88))(outer,0,1);
    }
}

static void ResetRoot(void) {
    U32 i;gRoot=0;++gGeneration;
    Visible(gCenter.ptr,1);
    for(i=0;i<gCount;++i){DropPair(&gControls[i].button);DropPair(&gControls[i].visual);}
    if(gHostCenter.ptr && gKeyboard.ptr && gKeyboardAttached)((PairArg)Virtual(gHostCenter.ptr,0xe8))(gHostCenter.ptr,0,&gKeyboard);
    if(gHostCenter.ptr && gPanel.ptr)((PairArg)Virtual(gHostCenter.ptr,0xe8))(gHostCenter.ptr,0,&gPanel);
    DropPair(&gKeyboard);DropPair(&gCenter);DropPair(&gRootPair);DropPair(&gPanel);
    DropPair(&gHostCenter);DropPair(&gSceneRoot);
    for(i=0;i<gCount;++i)DropConnection(&gControls[i].connection);
    Zero(gControls,sizeof(gControls));gCount=0;gKeyboardAttempt=0;gBuildFailures=0;gEdit=0;gKeyboardAttached=0;gRoomsExpanded=0;
}
static void Build(void) {
    U32 i;
    Button(NUI_MAIN,A_PAGE,NUI_OPTIONS,"ROOM OPTIONS");
    Button(NUI_MAIN,A_PAGE,NUI_FIND,"FIND BY NAME");
    Button(NUI_MAIN,A_SOCIAL_PAGE,NUI_FRIENDS,"FRIENDS");
    Button(NUI_MAIN,A_PAGE,NUI_CONNECTION,"CONNECTION");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_MUTATORS,"");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_CHEATS,"");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_OWNED_CARS,"");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_IGNORE_CLASS,"");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_FILL_AI,"");
    Button(NUI_OPTIONS,A_TOGGLE,NUI_PRIVATE,"");
    Button(NUI_OPTIONS,A_GRID,0,"");Button(NUI_OPTIONS,A_LAPS,0,"");
    Button(NUI_OPTIONS,A_RESET_RACE,0,"RESET GRID AND LAPS");
    Button(NUI_OPTIONS,A_EDIT,E_ROOM,"");Button(NUI_OPTIONS,A_EDIT,E_PASSWORD,"EDIT PRIVATE PASSWORD");
    Button(NUI_OPTIONS,A_PAGE,NUI_MAIN,"BACK");
    Button(NUI_FIND,A_EDIT,E_QUERY,"");Button(NUI_FIND,A_FIND,0,"SEARCH DIRECTORY");
    Button(NUI_FIND,A_LAN_JOIN,0,"JOIN LAN BY NAME");
    Button(NUI_FIND,A_DIRECT_JOIN,0,"JOIN BY IPV4 ADDRESS");
    Button(NUI_FIND,A_ROOM_LIST,0,"ROOMS: SEARCH TO LIST GAMES");
    for(i=0;i<8;++i)Button(NUI_FIND,A_SELECT_ROOM,i,"");
    Button(NUI_FIND,A_RESULT_PAGE,0,"MORE RESULTS");
    Button(NUI_FIND,A_JOIN_ROOM,0,"JOIN SELECTED ROOM");
    Button(NUI_FIND,A_PAGE,NUI_MAIN,"BACK");
    Button(NUI_FRIENDS,A_EDIT,E_NICKNAME,"");
    Button(NUI_FRIENDS,A_COPY_ID,0,"COPY MY PLAYER ID");
    Button(NUI_FRIENDS,A_EDIT,E_FRIEND_ID,"ADD FRIEND BY PLAYER ID");
    Button(NUI_FRIENDS,A_FRIENDS,0,"REFRESH FRIENDS");
    for(i=0;i<8;++i)Button(NUI_FRIENDS,A_SELECT_FRIEND,i,"");
    Button(NUI_FRIENDS,A_RESULT_PAGE,0,"MORE FRIENDS");
    Button(NUI_FRIENDS,A_INVITE_FRIEND,0,"INVITE SELECTED FRIEND");
    Button(NUI_FRIENDS,A_SOCIAL_PAGE,NUI_FRIEND_REQUESTS,"FRIEND REQUESTS");
    Button(NUI_FRIENDS,A_SOCIAL_PAGE,NUI_INVITATIONS,"INVITATIONS");
    Button(NUI_FRIENDS,A_PAGE,NUI_MAIN,"BACK");
    for(i=NUI_FRIEND_REQUESTS;i<=NUI_INVITATIONS;++i){
        U32 j;
        Button(i,A_SOCIAL_PAGE,i,"REFRESH");
        for(j=0;j<8;++j)Button(i,A_SELECT_FRIEND,j,"");
        Button(i,A_RESULT_PAGE,0,"MORE RESULTS");
        Button(i,A_REQUEST,i==NUI_FRIEND_REQUESTS?NUI_REQUEST_ACCEPT_FRIEND:NUI_REQUEST_ACCEPT_INVITE,"ACCEPT SELECTED");
        Button(i,A_REQUEST,i==NUI_FRIEND_REQUESTS?NUI_REQUEST_REJECT_FRIEND:NUI_REQUEST_DISMISS_INVITE,i==NUI_FRIEND_REQUESTS?"REJECT SELECTED":"DISMISS SELECTED");
        Button(i,A_SOCIAL_PAGE,NUI_FRIENDS,"BACK TO FRIENDS");
    }
    Button(NUI_CONNECTION,A_EDIT,E_DIRECTORY,"EDIT DIRECTORY URL");
    Button(NUI_CONNECTION,A_PAGE,NUI_MAIN,"BACK");
    Button(99,0,0,"Room policy bridge pending");
}
static void KeyboardKey(const char *name,U32 action,U32 value) {
    Control *c;Pair generic;Delegate d;char title[16];Vec2 limit;
    if(gCount>=128)return;c=&gControls[gCount];Zero(c,sizeof(*c));
    Find(gKeyboard.ptr,name,&generic);if(generic.ptr)((CastButton)Api(0x500890))(&c->button,&generic);DropPair(&generic);
    if(!c->button.ptr){++gBuildFailures;return;}
    Momentary(c->button.ptr);
    c->generation=gGeneration;c->page=NUI_KEYBOARD;c->action=action;c->value=value;
    limit.x=0x447a0000U;limit.y=action?0x40c00000U:0x40e00000U;
    ((VecArg)Virtual(c->button.ptr,0x30))(c->button.ptr,0,&limit);
    if(action==A_KEY){title[0]=(char)value;title[1]=0;Label(c,title);}
    else if(action==A_SPACE)Label(c,"SPACE");
    else if(action==A_DELETE)Label(c,"BACK");
    else if(action==A_CANCEL)Label(c,"CANCEL");
    else if(action==A_CONFIRM)Label(c,"DONE");
    else if(action==A_SHIFT)Label(c,"CASE: UPPER");
    else if(action==A_PASTE)Label(c,"PASTE");
    else if(action==A_CLEAR)Label(c,"CLEAR");
    if(action){Zero(&d,sizeof(d));d.fn=NativeClick;d.object=c;Bind(&c->connection,(char*)c->button.ptr+0x394,&d);}
    Enabled(c->button.ptr,action!=0);++gCount;
}
static void Keyboard(void) {
    U32 r,i;char name[48];
    if(gKeyboardAttempt)return;gKeyboardAttempt=1;
    Text(&r,"offline_lobby_ui/screen_multiplayer_private_local");
    ((Factory)Api(0x510bb0))(&gKeyboard,&r);DropText(&r);if(!gKeyboard.ptr)return;
    Name(gKeyboard.ptr,"offline_lobby_keyboard_root");
    ConfigureFrame(gKeyboard.ptr,"offline_keyboard_frame",1);
    for(i=0;i<36;++i){Copy(name,"offline_key_",sizeof(name));name[12]=(char)(i<26?'A'+i:'0'+i-26);name[13]=0;KeyboardKey(name,A_KEY,(U32)(unsigned char)name[12]);}
    KeyboardKey("offline_key_SPACE",A_SPACE,0);KeyboardKey("offline_key_BACKSPACE",A_DELETE,0);
    KeyboardKey("offline_key_CANCEL",A_CANCEL,0);KeyboardKey("offline_key_CONFIRM",A_CONFIRM,0);
    KeyboardKey("offline_key_CLEAR",A_CLEAR,0);
    KeyboardKey("offline_key_SHIFT",A_SHIFT,0);KeyboardKey("offline_key_PASTE",A_PASTE,0);
    KeyboardKey("offline_key_DASH",A_KEY,'-');KeyboardKey("offline_key_DOT",A_KEY,'.');
    KeyboardKey("offline_key_UNDERSCORE",A_KEY,'_');KeyboardKey("offline_key_COLON",A_KEY,':');
    KeyboardKey("offline_key_SLASH",A_KEY,'/');
    KeyboardKey("offline_input_value",0,0);
    /* Attach only while editing: hidden native layout children still reserve space. */
}
static char *EditTarget(U32 *capacity) {
    if(gEdit==E_ROOM){*capacity=64;return gState->roomName;}
    if(gEdit==E_QUERY){*capacity=64;return gState->query;}
    if(gEdit==E_PASSWORD){*capacity=64;return gState->password;}
    if(gEdit==E_DIRECTORY){*capacity=1024;return gState->invite;}
    if(gEdit==E_FRIEND_ID){*capacity=64;return gState->query;}
    *capacity=32;return gState->nickname;
}
static U32 PreviousChar(U32 pos) {
    if(pos)do{--pos;}while(pos&&((unsigned char)gEditText[pos]&0xc0)==0x80);
    return pos;
}
static U32 NextChar(U32 pos) {
    if(gEditText[pos])do{++pos;}while(((unsigned char)gEditText[pos]&0xc0)==0x80);
    return pos;
}
static void Erase(U32 first,U32 last) {
    do{gEditText[first++]=gEditText[last];}while(gEditText[last++]);
}
static void InsertCharacter(U32 code) {
    char bytes[4];U32 size,cap,length,i;
    if(code<32||(code>=127&&code<160)||(code>=0xd800&&code<=0xdfff)||code>0x10ffff)return;
    if(code<128){bytes[0]=(char)code;size=1;}
    else if(code<0x800){bytes[0]=(char)(0xc0|(code>>6));size=2;}
    else if(code<0x10000){bytes[0]=(char)(0xe0|(code>>12));size=3;}
    else{bytes[0]=(char)(0xf0|(code>>18));size=4;}
    for(i=1;i<size;++i)bytes[i]=(char)(0x80|((code>>(6*(size-1-i)))&63));
    EditTarget(&cap);length=gSelectAll?0:Len(gEditText,sizeof(gEditText));
    if(length+size>=cap)return;
    if(gSelectAll){gEditText[0]=0;gCaret=0;gSelectAll=0;}
    for(i=length+1;i>gCaret;--i)gEditText[i-1+size]=gEditText[i-1];
    for(i=0;i<size;++i)gEditText[gCaret++]=bytes[i];
}
static void Click(Control *c) {
    U32 cap,field,kind;char *dst;
    switch(c->action){
    case A_PAGE:gState->page=c->value;gResultOffset=0;break;
    case A_SOCIAL_PAGE:gState->page=c->value;gResultOffset=0;gState->friendCount=0;
        Request(c->value==NUI_FRIEND_REQUESTS?NUI_REQUEST_FRIEND_REQUESTS:c->value==NUI_INVITATIONS?NUI_REQUEST_INVITATIONS:NUI_REQUEST_FRIENDS,0);break;
    case A_REQUEST:if(gState->selectedFriend<gState->friendCount && gState->selectedFriend<8)Request(c->value,0);break;
    case A_COPY_ID:Request(NUI_REQUEST_COPY_PLAYER_ID,0);break;
    case A_RESULT_PAGE:gResultOffset=gResultOffset?0:4;gLayoutDirty=1;break;
    case A_TOGGLE:gState->policyBits^=c->value;Request(NUI_REQUEST_POLICY,NUI_FIELD_POLICY);break;
    case A_GRID:gState->grid=gState->grid>=60?0:gState->grid+1;Request(NUI_REQUEST_POLICY,NUI_FIELD_GRID);break;
    case A_LAPS:gState->laps=gState->laps>=60?0:gState->laps+1;Request(NUI_REQUEST_POLICY,NUI_FIELD_LAPS);break;
    case A_RESET_RACE:gState->grid=0;gState->laps=0;Request(NUI_REQUEST_POLICY,NUI_FIELD_GRID|NUI_FIELD_LAPS);break;
    case A_FIND:gResultOffset=0;gRoomsExpanded=1;Request(NUI_REQUEST_FIND,NUI_FIELD_QUERY);break;
    case A_ROOM_LIST:gRoomsExpanded=!gRoomsExpanded;gLayoutDirty=1;break;
    case A_LAN_JOIN:gLaunchLan=1;break;
    case A_DIRECT_JOIN:if(gState->bridgeFlags&NUI_BRIDGE_DIRECT_QUERY)gLaunchLan=2;break;
    case A_FRIENDS:gResultOffset=0;Request(NUI_REQUEST_FRIENDS,0);break;
    case A_SELECT_ROOM:if(c->value<gState->roomCount && c->value<8){gState->selectedRoom=c->value;gRoomsExpanded=0;gLayoutDirty=1;}break;
    case A_JOIN_ROOM:if(gState->selectedRoom<gState->roomCount && gState->selectedRoom<8)Request(NUI_REQUEST_JOIN_SELECTED,0);break;
    case A_SELECT_FRIEND:if(c->value<gState->friendCount && c->value<8)gState->selectedFriend=c->value;break;
    case A_INVITE_FRIEND:if(gState->selectedFriend<gState->friendCount && gState->selectedFriend<8)Request(NUI_REQUEST_FRIEND_INVITE,0);break;
    case A_EDIT:gEdit=c->value;gReturnPage=gState->page;dst=EditTarget(&cap);
        if(gEdit==E_FRIEND_ID)gEditText[0]=0;else Copy(gEditText,dst,sizeof(gEditText));
        gCaret=Len(gEditText,sizeof(gEditText));gSelectAll=0;
        Keyboard();if(gKeyboard.ptr){gState->page=NUI_KEYBOARD;gEditEpoch=gEditEpoch%2047+1;NativeNetwork(gState)->editToken=gEditEpoch;}break;
    case A_KEY:case A_SPACE:{
        U32 key=c->action==A_SPACE?' ':c->value;if(gLowercase && key>='A' && key<='Z')key+=32;
        InsertCharacter(key);break;}
    case A_SHIFT:gLowercase=!gLowercase;break;
    case A_CLEAR:gEditText[0]=0;gCaret=0;gSelectAll=0;break;
    case A_PASTE:Request(NUI_REQUEST_PASTE,gEdit);break;
    case A_DELETE:if(gSelectAll){gEditText[0]=0;gCaret=0;gSelectAll=0;}else if(gCaret){U32 previous=PreviousChar(gCaret);Erase(previous,gCaret);gCaret=previous;}break;
    case A_CANCEL:gState->page=gReturnPage;gEdit=0;NativeNetwork(gState)->editToken=0;break;
    case A_CONFIRM:dst=EditTarget(&cap);Copy(dst,gEditText,cap);
        if(gEdit==E_QUERY){gState->roomCount=0;gState->selectedRoom=~0U;gState->statusCode=0;gState->statusText[0]=0;gRoomsExpanded=0;gResultOffset=0;gLayoutDirty=1;}
        field=gEdit==E_ROOM?NUI_FIELD_ROOM_NAME:gEdit==E_QUERY?NUI_FIELD_QUERY:gEdit==E_PASSWORD?NUI_FIELD_PASSWORD:NUI_FIELD_NICKNAME;
        kind=gEdit==E_DIRECTORY?NUI_REQUEST_DIRECTORY:gEdit==E_FRIEND_ID?NUI_REQUEST_ADD_FRIEND:gEdit==E_ROOM?NUI_REQUEST_ROOM_NAME:gEdit==E_QUERY?NUI_REQUEST_NONE:gEdit==E_PASSWORD?NUI_REQUEST_PASSWORD:NUI_REQUEST_NICKNAME;
        if(kind)Request(kind,field);gState->page=gReturnPage;gEdit=0;NativeNetwork(gState)->editToken=0;break;
    }
    Refresh();
}
static void PhysicalKey(NativeNetworkState *network) {
    U32 sequence=network->keySequence,word,code;
    Control command;
    if(sequence==network->keyAck)return;
    word=network->keyWord;
    network->keyAck=sequence;
    if(!network->editToken||word>>21!=network->editToken||!gEdit||gState->page!=NUI_KEYBOARD)return;
    code=word&0x1fffff;
    switch(code){
    case 0x110000:command.action=A_CONFIRM;break;
    case 0x110001:command.action=A_CANCEL;break;
    case 0x110002:command.action=A_DELETE;break;
    case 0x110003:command.action=A_PASTE;break;
    case 0x110004:gSelectAll=1;return;
    case 0x110005:gCaret=gSelectAll?0:PreviousChar(gCaret);gSelectAll=0;return;
    case 0x110006:gCaret=gSelectAll?Len(gEditText,sizeof(gEditText)):NextChar(gCaret);gSelectAll=0;return;
    case 0x110007:gCaret=0;gSelectAll=0;return;
    case 0x110008:gCaret=Len(gEditText,sizeof(gEditText));gSelectAll=0;return;
    case 0x110009:if(gSelectAll){gEditText[0]=0;gCaret=0;gSelectAll=0;}else Erase(gCaret,NextChar(gCaret));return;
    default:InsertCharacter(code);return;
    }
    Click(&command);
}
static void Refresh(void) {
    U32 i,page=gState->page;char text[192],num[16];Control *c;const char *prefix;
    if(page>NUI_CONNECTION)page=gState->page=NUI_MAIN;
    if(gState->statusCode==200){
        if(page==NUI_KEYBOARD && gEdit){U32 cap;EditTarget(&cap);Copy(gEditText,(const char*)gState->rooms,cap);gCaret=Len(gEditText,sizeof(gEditText));gSelectAll=0;}
        gState->statusCode=0;
    }
    Visible(gCenter.ptr,page==NUI_MAIN);Visible(gKeyboard.ptr,page==NUI_KEYBOARD);Visible(gPanel.ptr,page!=NUI_KEYBOARD);
    if(gKeyboard.ptr && (page==NUI_KEYBOARD)!=gKeyboardAttached){
        if(page==NUI_KEYBOARD)((PairArg)Api(0x14684c0))(gHostCenter.ptr,0,&gKeyboard);
        else ((PairArg)Virtual(gHostCenter.ptr,0xe8))(gHostCenter.ptr,0,&gKeyboard);
        gKeyboardAttached=page==NUI_KEYBOARD;
        gLayoutDirty=1;
    }
    for(i=0;i<gCount;++i){c=&gControls[i];
        if(c->action==A_TOGGLE)Selection(c,(gState->policyBits&c->value)!=0);
        else if(c->action==A_SELECT_ROOM)Selection(c,c->value==gState->selectedRoom && c->value<gState->roomCount);
        else if(c->action==A_SELECT_FRIEND)Selection(c,c->value==gState->selectedFriend && c->value<gState->friendCount);
        if(c->visual.ptr){
            U32 wanted=c->page==page || (c->page==99 && page!=NUI_KEYBOARD && page!=NUI_MAIN);
            if(c->action==A_SELECT_ROOM)wanted=wanted && gRoomsExpanded && c->value<gState->roomCount && c->value>=gResultOffset && c->value<gResultOffset+4 && c->value<8;
            if(c->action==A_SELECT_FRIEND)wanted=wanted && c->value<gState->friendCount && c->value>=gResultOffset && c->value<gResultOffset+4 && c->value<8;
            if(c->action==A_RESULT_PAGE)wanted=wanted && (page!=NUI_FIND||gRoomsExpanded) && (page==NUI_FIND?gState->roomCount:gState->friendCount)>4;
            if(wanted!=c->attached){
                if(wanted)((PairArg)Api(0x14684c0))(gRoot,0,&c->visual);
                else ((PairArg)Virtual(gRoot,0xe8))(gRoot,0,&c->visual);
                c->attached=wanted;
                gLayoutDirty=1;
            }
        }
        if(c->visual.ptr)Visible(c->visual.ptr,c->page==page || (c->page==99 && page!=NUI_KEYBOARD && page!=NUI_MAIN));
        if(c->page==NUI_KEYBOARD){
            if(c->action==A_KEY && c->value>='A' && c->value<='Z'){text[0]=(char)(c->value+(gLowercase?32:0));text[1]=0;Label(c,text);}
            else if(c->action==A_SHIFT)Label(c,gLowercase?"ABC":"abc");
            else if(!c->action){
                U32 cursor=gCaret,start=cursor,end=cursor,j,k=0;
                for(j=0;j<22&&start;j++)start=PreviousChar(start);
                for(j=0;j<14&&gEditText[end];j++)end=NextChar(end);
                if(!gEditText[0])Copy(text,gEdit==E_PASSWORD?"Enter password":"Type or click a key",sizeof(text));
                else{
                    if(start){text[k++]='.';text[k++]='.';}
                    if(gSelectAll)text[k++]='[';
                    for(j=start;j<end;){
                        U32 next=NextChar(j);
                        if(j==cursor&&!gSelectAll){text[k++]='[';text[k++]=']';}
                        if(gEdit==E_PASSWORD){text[k++]='*';j=next;}
                        else while(j<next)text[k++]=gEditText[j++];
                    }
                    if(cursor==end&&!gSelectAll){text[k++]='[';text[k++]=']';}
                    if(gSelectAll)text[k++]=']';
                    if(gEditText[end]){text[k++]='.';text[k++]='.';}
                    text[k]=0;
                }
                Label(c,text);
            }continue;}
        if(c->action==A_SELECT_ROOM || c->action==A_SELECT_FRIEND){U32 count=c->action==A_SELECT_ROOM?gState->roomCount:gState->friendCount;U32 selected=c->action==A_SELECT_ROOM?gState->selectedRoom:gState->selectedFriend;NativeUiEntry *entries=c->action==A_SELECT_ROOM?gState->rooms:gState->friends;
            Visible(c->visual.ptr,c->page==page && c->attached);
            if(c->value<count && c->value<8){Copy(text,selected==c->value?"> ":"",sizeof(text));Append(text,entries[c->value].label,sizeof(text));Label(c,text);}continue;}
        if(c->action==A_JOIN_ROOM){U32 phase=NativeNetwork(gState)->joinPhase;Enabled(c->button.ptr,(phase<NJ_QUEUED||phase>NJ_DISPATCHED)&&gState->statusCode!=307&&(gState->bridgeFlags&NUI_BRIDGE_DISCOVERY) && gState->selectedRoom<gState->roomCount && gState->selectedRoom<8 && (NativeNetwork(gState)->joinableMask&(1U<<gState->selectedRoom)));}
        if(c->action==A_DIRECT_JOIN)Enabled(c->button.ptr,(gState->bridgeFlags&NUI_BRIDGE_DIRECT_QUERY)!=0);
        if(c->action==A_ROOM_LIST)Enabled(c->button.ptr,gState->roomCount>0 && gState->statusCode!=300);
        if(c->action==A_INVITE_FRIEND)Enabled(c->button.ptr,(gState->bridgeFlags&NUI_BRIDGE_FRIENDS) && gState->selectedFriend<gState->friendCount && gState->selectedFriend<8);
        if(c->action==A_REQUEST)Enabled(c->button.ptr,(gState->bridgeFlags&NUI_BRIDGE_FRIENDS) && gState->selectedFriend<gState->friendCount && gState->selectedFriend<8);
        if(c->action==A_COPY_ID)Enabled(c->button.ptr,(gState->bridgeFlags&NUI_BRIDGE_FRIENDS)!=0);
        text[0]=0;
        if(c->action==A_ROOM_LIST){
            if(gState->statusCode==300)Copy(text,"ROOMS: SEARCHING...",sizeof(text));
            else if(gState->roomCount){
                Copy(text,gRoomsExpanded?"[-] ":"[+] ",sizeof(text));
                if(gState->selectedRoom<gState->roomCount && gState->selectedRoom<8)Append(text,gState->rooms[gState->selectedRoom].label,sizeof(text));
                else {Number(num,gState->roomCount);Append(text,num,sizeof(text));Append(text," ROOMS - SELECT A GAME",sizeof(text));}
            }else Copy(text,gState->statusCode==302?"ROOMS: NO MATCHES":gState->statusCode==303?"ROOMS: CONNECTION UNAVAILABLE":gState->statusCode==304?"ROOMS: SEARCH FAILED":"ROOMS: SEARCH TO LIST GAMES",sizeof(text));
        }
        else if(c->action==A_JOIN_ROOM)Copy(text,gState->selectedRoom<gState->roomCount && (!(gState->bridgeFlags&NUI_BRIDGE_DISCOVERY)||gState->selectedRoom>=8||!(NativeNetwork(gState)->joinableMask&(1U<<gState->selectedRoom)))?"ROOM FULL OR NATIVE JOIN UNAVAILABLE":"JOIN SELECTED ROOM",sizeof(text));
        else if(c->action==A_TOGGLE){prefix=c->value==NUI_MUTATORS?"Mutators":c->value==NUI_CHEATS?"Cheats":c->value==NUI_OWNED_CARS?"Owned cars required":c->value==NUI_IGNORE_CLASS?"Ignore class":c->value==NUI_FILL_AI?"Fill empty slots with AI":"Private room";Copy(text,prefix,sizeof(text));Append(text,gState->policyBits&c->value?": ON":": OFF",sizeof(text));}
        else if(c->action==A_GRID || c->action==A_LAPS){U32 value=c->action==A_GRID?gState->grid:gState->laps;Copy(text,c->action==A_GRID?"Grid: ":"Laps: ",sizeof(text));if(!value)Append(text,"Default",sizeof(text));else{Number(num,value);Append(text,num,sizeof(text));}}
        else if(c->action==A_EDIT && c->value<=E_NICKNAME){prefix=c->value==E_ROOM?"Room name: ":c->value==E_QUERY?"Search: ":"Nickname: ";Copy(text,prefix,sizeof(text));Append(text,c->value==E_ROOM?gState->roomName:c->value==E_QUERY?gState->query:gState->nickname,sizeof(text));}
        else if(c->page==99){
            if(gBuildFailures)Copy(text,"Some native controls could not load",sizeof(text));
            else if(gKeyboardAttempt && !gKeyboard.ptr)Copy(text,"Native keyboard resource unavailable",sizeof(text));
            else if(gState->statusText[0])Copy(text,gState->statusText,sizeof(text));
            else if(page==NUI_FRIENDS && !(gState->bridgeFlags&NUI_BRIDGE_FRIENDS))Copy(text,"Friend bridge not connected",sizeof(text));
            else if(page==NUI_FIND && !(gState->bridgeFlags&NUI_BRIDGE_DISCOVERY))Copy(text,"Discovery bridge not connected",sizeof(text));
            else if(!(gState->bridgeFlags&NUI_BRIDGE_POLICY))Copy(text,"Room policy bridge pending",sizeof(text));
            else Copy(text,"Native lobby controls connected",sizeof(text));
        }
        if(text[0])Label(c,text);
    }
    if(gLayoutDirty){
        /* Async results may arrive after footer controls were attached. Rebuild
         * the native child order from the stable control order on changes. */
        for(i=0;i<gCount;++i)if(gControls[i].visual.ptr && gControls[i].attached){
            ((PairArg)Virtual(gRoot,0xe8))(gRoot,0,&gControls[i].visual);
        }
        for(i=0;i<gCount;++i)if(gControls[i].visual.ptr && gControls[i].attached){
            ((PairArg)Api(0x14684c0))(gRoot,0,&gControls[i].visual);
        }
        ((Release)Virtual(gRoot,0xa8))(gRoot,0);
        ((BoolArg)Virtual(gPanel.ptr,0x88))(gPanel.ptr,0,1);
        if(gKeyboardAttached)((BoolArg)Virtual(gKeyboard.ptr,0x88))(gKeyboard.ptr,0,1);
        gLayoutDirty=0;
    }
}
__declspec(dllexport) void __cdecl TickUi(U32 module,void *screen,NativeUiState *shared) {
    void *outer;U32 text;
    if(!module || !screen || !shared)return;
    NativeTransportTick(module,screen,shared);
    NativeLanTick(module,screen,shared);
    if(NativeLobbyPolicyTick(module,screen,shared)){
        gState=shared;Request(NUI_REQUEST_POLICY,NUI_FIELD_POLICY);
    }
    gModule=module;TickRoster(module,screen,shared);
    if(*(U32*)screen!=module+0x1d45bacU){NativeNetwork(shared)->editToken=0;return;}gModule=module;
    if(!shared->magic){Zero(shared,sizeof(*shared));shared->magic=NATIVE_UI_MAGIC;shared->version=NATIVE_UI_VERSION;shared->size=sizeof(*shared);shared->policyBits=NUI_OWNED_CARS|NUI_FILL_AI;Copy(shared->roomName,"Asphalt 8 room",64);}
    if(shared->magic!=NATIVE_UI_MAGIC || shared->version!=NATIVE_UI_VERSION || shared->size<sizeof(*shared))return;
    outer=*(void**)((char*)screen+0x15c);if(!outer)return;gState=shared;
    if(gSceneRoot.ptr!=outer){
        if(!gNamespacesLoaded){
            U32 reference;
            Text(&reference,"offline_lobby_panel");((void (__stdcall *)(U32))Api(0xa6a280))(reference);
            Text(&reference,"offline_lobby_ui");((void (__stdcall *)(U32))Api(0xa6a280))(reference);
            gNamespacesLoaded=1;
        }
        ResetRoot();NativeNetwork(shared)->editToken=0;gSceneRoot.ptr=outer;gSceneRoot.control=*(void**)((char*)screen+0x160);
        if(gSceneRoot.control){volatile long *strong=(volatile long*)((char*)gSceneRoot.control+4);
            __asm {
                mov eax,strong
                lock inc dword ptr [eax]
            }
        }
        Find(outer,"center",&gHostCenter);Find(outer,"center_mp_done",&gCenter);
        Text(&text,"offline_lobby_panel/screen_multiplayer_private_local");
        ((Factory)Api(0x510bb0))(&gPanel,&text);DropText(&text);
        if(!gPanel.ptr || !gHostCenter.ptr){shared->statusCode=100;return;}
        Name(gPanel.ptr,"offline_lobby_panel_root");
        ConfigureFrame(gPanel.ptr,"offline_panel_frame",0);
        Find(gPanel.ptr,"offline_panel_rows",&gRootPair);gRoot=gRootPair.ptr;
        if(!gRoot){shared->statusCode=101;return;}
        ((PairArg)Api(0x14684c0))(gHostCenter.ptr,0,&gPanel);
        shared->page=NUI_MAIN;Build();
    }
    if(gRoot){
        if(shared->statusCode==200)Refresh();
        if(shared->requestKind!=NUI_REQUEST_PASTE||shared->requestSeq==shared->ackSeq)
            PhysicalKey(NativeNetwork(shared));
        Refresh();
    }
    if(gLaunchLan){U32 direct=gLaunchLan==2;gLaunchLan=0;if(direct)NativeLanBeginDirect(module,screen,shared);else NativeLanBegin(module,screen,shared);}
}
