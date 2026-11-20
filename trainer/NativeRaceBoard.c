#include "NativeRaceBoard.h"

typedef unsigned U;
typedef unsigned char Byte;
typedef unsigned __int64 (__stdcall *TickCount)(void);
typedef void *(__fastcall *MappedConnection)(const void *, void *, const void *);
void _ReadWriteBarrier(void);
#pragma intrinsic(_ReadWriteBarrier)

static NativeRoster roomRoster;
static void *At(U module,U va){return (void *)(module+va-0x400000U);}
static U Word(const void *object,U offset){return *(const U *)((const Byte *)object+offset);}
static const void *Pointer(const void *object,U offset){return *(const void *const *)((const Byte *)object+offset);}
static int Type(U module,const void *object,U va){return object&&Word(object,0)==(U)At(module,va);}
static int SameUser(const U *a,const U *b){return a[0]==b[0]&&a[1]==b[1]&&a[2]==b[2]&&a[3]==b[3];}
static U Tick(U module){return (U)(*(TickCount *)At(module,0x1c84078))();}

__declspec(dllexport) void __cdecl NativeRememberRaceRoster(const NativeRoster *roster)
{
    if(roster->humans&&roster->humans<=NATIVE_ROSTER_CAPACITY)roomRoster=*roster;
}

__declspec(dllexport) void __cdecl NativeCaptureRaceBoard(U module,NativeUiState *ui,void *mode)
{
    NativeRaceBoard *board;
    const void *manager,*server;
    const void *const *ordered;
    U begin,end,i,j,count,sequence;
    if(!module||!ui||ui->magic!=NATIVE_UI_MAGIC||!mode)return;
    if(!Type(module,mode,0x212a368)&&!Type(module,mode,0x2122144)&&!Type(module,mode,0x214f37c))return;
    manager=Pointer(At(module,0x22511bc),0);
    if(!Type(module,manager,0x213a030)||!Type(module,Pointer(manager,0xe8),0x216a4e4))return;
    server=Pointer(manager,0xec);
    if(!Type(module,server,0x2141dac)||!roomRoster.humans||!*((const Byte *)server+0x168))return;
    begin=Word(mode,0x18);end=Word(mode,0x1c);
    if(!begin||end<=begin||((begin|end)&3)||end-begin>NATIVE_ROSTER_CAPACITY*4)return;
    ordered=(const void *const *)begin;count=(end-begin)/4;
    for(i=0;i<count;++i)if(!Type(module,ordered[i],0x2119464))return;

    board=(NativeRaceBoard *)((Byte *)ui+NATIVE_RACE_BOARD_OFFSET);
    sequence=(board->revision|1U);
    board->revision=sequence;board->endRevision=sequence;
    _ReadWriteBarrier();
    board->magic=NATIVE_RACE_BOARD_MAGIC;
    board->roomLow=roomRoster.roomLow;board->roomHigh=roomRoster.roomHigh;
    board->count=count;
    board->seenAt=Tick(module);
    for(i=0;i<NATIVE_ROSTER_CAPACITY;++i){
        NativeRaceRow *row=&board->rows[i];
        const Byte *racer;
        const U *id;
        U letter;
        for(letter=0;letter<sizeof(row->name);++letter)row->name[letter]=0;
        if(i>=count){
            for(j=0;j<4;++j)row->identity[j]=0;
            row->kind=0;row->ping=-1;
            continue;
        }
        racer=(const Byte *)ordered[i];id=(const U *)(racer+0x2380);
        row->ping=-1;
        /* D73C28 counts human drivers when this byte is zero. The next
         * byte was set for both human and bot rows in the live race. */
        row->kind=racer[0x22f8]?RACE_ROW_AI:RACE_ROW_GUEST;
        for(j=0;j<4;++j)row->identity[j]=id[j];
        if(row->kind==RACE_ROW_AI)continue;
        for(j=0;j<roomRoster.humans;++j){
            if(!SameUser(id,roomRoster.users[j].identity))continue;
            for(letter=0;letter<sizeof(row->name)-1&&roomRoster.users[j].name[letter];++letter)
                row->name[letter]=roomRoster.users[j].name[letter];
            if(j==0)row->kind=RACE_ROW_HOST;
            break;
        }
        if(row->kind==RACE_ROW_GUEST){
            /* ServerRacer+23E0 is a race timer. D74270 looks up the actual
             * network transport using its D6 service-to-racer association. */
            const void *transport=((MappedConnection)At(module,0xd74270))(server,0,racer);
            const Byte *peer=Type(module,transport,0x2119738)?(const Byte *)Pointer(transport,0x23e0):0;
            if(peer&&peer[0x2b9]&&SameUser(id,(const U *)(peer+0x2a8))){
                U sample=Word(transport,0x21b8);
                if(sample&&sample<=60000)row->ping=(int)sample;
            }
        }
    }
    _ReadWriteBarrier();
    board->endRevision=sequence+1;board->revision=sequence+1;
}

static int Snapshot(NativeRaceBoard *destination,const NativeRaceBoard *source,U now)
{
    U revision=source->revision;
    U row,hosts=0;
    if((revision&1)||source->magic!=NATIVE_RACE_BOARD_MAGIC||!source->count||
        source->count>NATIVE_ROSTER_CAPACITY||now-source->seenAt>2000)return 0;
    _ReadWriteBarrier();
    *destination=*source;
    _ReadWriteBarrier();
    if(revision!=source->revision||revision!=source->endRevision||
        !destination->count||destination->count>NATIVE_ROSTER_CAPACITY)return 0;
    for(row=0;row<destination->count;++row)if(destination->rows[row].kind==RACE_ROW_HOST)++hosts;
    return hosts==1;
}
static char *Append(char *out,const char *text,U length)
{
    while(length--&&*text)*out++=*text++;
    return out;
}
static char *Name(char *out,const char *name)
{
    U i;
    if(!*name)return Append(out,"Driver",6);
    for(i=0;i<31&&name[i];++i){
        Byte ch=(Byte)name[i];
        *out++=ch<32||ch==127?' ':(char)ch;
    }
    return out;
}
static char *Number(char *out,U value)
{
    char digits[10];U count=0;
    do{digits[count++]=(char)('0'+value%10);value/=10;}while(value);
    while(count)*out++=digits[--count];
    return out;
}
__declspec(dllexport) const char *__cdecl NativeRaceText(U module,NativeUiState *ui)
{
    NativeRaceBoard board;
    static char text[640];
    char *out=text;
    U i,now;
    if(!module||!ui||Word(ui,NATIVE_RACE_TAB_OFFSET)!=1)return 0;
    now=Tick(module);
    if(!Snapshot(&board,(const NativeRaceBoard *)((const Byte *)ui+NATIVE_RACE_BOARD_OFFSET),now)){
        typedef void *(__cdecl *CurrentClient)(void);
        if(!Snapshot(&board,(const NativeRaceBoard *)((const Byte *)ui+NATIVE_RACE_GUEST_BOARD_OFFSET),now)||
           board.roomLow!=Word(ui,0x10a8)||board.roomHigh!=Word(ui,0x10ac)||
           !((CurrentClient)At(module,0xbaf5e0))())return 0;
    }
    out=Append(out,"RACE STANDINGS   |   PING TO HOST\n",40);
    for(i=0;i<board.count;++i){
        const NativeRaceRow *row=&board.rows[i];
        out=Number(out,i+1);out=Append(out,".  ",3);
        if(row->kind==RACE_ROW_AI)out=Append(out,"AI",2);
        else{
            out=Name(out,row->name);
            if(row->kind==RACE_ROW_HOST)out=Append(out,"  |  HOST",9);
            else if(row->ping<0||row->ping>60000)out=Append(out,"  |  -- ms",10);
            else{out=Append(out,"  |  ",5);out=Number(out,(U)row->ping);out=Append(out," ms",3);}
        }
        *out++='\n';
    }
    *out=0;
    return text;
}
