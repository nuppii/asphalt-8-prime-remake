#include "NativeLobbyPolicy.h"
typedef unsigned U;
typedef unsigned char Byte;
typedef void *(__fastcall *GetRoom)(void *,void *);
typedef void (__fastcall *Click)(void *,void *,void *,void *);
/* Preferred VA constants refer only to original Asphalt 8 4.8.0.7 x86.
 * Callback evidence: E99D8F CMP BYTE screen+288; E99D9E SETE CL;
 * E99DA1 MOV BYTE screen+288,CL; E99E8E Lobby virtual+60 publishes metadata;
 * E99E95 synchronous refresh; E99ED4..E99F21 releases by-value pair,RET8.
 */
static struct Observation {
    U active,module,roomLow,roomHigh,desired,native;
    void *screen,*room;
    NativeUiState *state;
} gPolicy;
static void *Address(U module,U preferred){return(void*)(module+preferred-0x400000U);}
static void *Virtual(void *object,U offset){return *(void**)(*(U*)object+offset);}
static void Retain(void *control){volatile long *strong=(volatile long*)((Byte*)control+4);
    __asm { mov eax,strong
        lock inc dword ptr[eax]
    }
}
void __cdecl NativeLobbyPolicyReset(void){gPolicy.active=0;}
/* A single UI-thread observation chooses exactly one direction. Desired wins
 * on room entry and if both controls changed since the preceding observation.
 * 1=dispatch original native toggle,2=copy native change to shared,0=idle.
 */
static U Decide(U desired,U native,U fresh){
    if(fresh||desired!=gPolicy.desired)return desired!=native?1U:0U;
    return native!=gPolicy.native&&native!=desired?2U:0U;
}
U __cdecl NativeLobbyPolicyTick(U module,void *screen,NativeUiState *state){
    void *lobby,*room,*button,*control;U mode,desired,native,fresh,action,low,high,changed=0;
    if(!module||!screen||!state||state->magic!=NATIVE_UI_MAGIC||state->version!=NATIVE_UI_VERSION||state->size<sizeof(*state)){
        NativeLobbyPolicyReset();return 0;
    }
    if(*(U*)screen!=(U)Address(module,0x214ac78)){
        NativeLobbyPolicyReset();return 0;
    }
    if(!*((Byte*)screen+0x284)){NativeLobbyPolicyReset();return 0;}
    mode=*(U*)((Byte*)screen+0x280);if(mode!=2&&mode!=5)return 0;
    lobby=*(void**)((Byte*)screen+0x270);
    if(!lobby||*(U*)lobby!=(U)Address(module,0x216a474))return 0;
    room=((GetRoom)Virtual(lobby,0x44))(lobby,0);
    if(!room||*(U*)room!=(U)Address(module,0x216a510))return 0;
    button=*(void**)((Byte*)screen+0x378);control=*(void**)((Byte*)screen+0x37c);
    if(!button||!control||!*((Byte*)button+0x44))return 0;
    low=*(U*)((Byte*)room+0x50);high=*(U*)((Byte*)room+0x54);
    desired=(state->policyBits&NUI_FILL_AI)?1U:0U;
    native=*((Byte*)screen+0x288)?1U:0U;
    fresh=!gPolicy.active||gPolicy.module!=module||gPolicy.screen!=screen||gPolicy.state!=state||gPolicy.room!=room||gPolicy.roomLow!=low||gPolicy.roomHigh!=high;
    action=Decide(desired,native,fresh);
    if(action==1){
        /* The real callback consumes this retained by-value copy. Never
         * manufacture button userdata or write native room/UI bytes. */
        Retain(control);((Click)Address(module,0xe99d10))(screen,0,button,control);
        native=*((Byte*)screen+0x288)?1U:0U;
    }else if(action==2){
        if(native)state->policyBits|=NUI_FILL_AI;else state->policyBits&=~NUI_FILL_AI;
        desired=native;changed=1;
    }
    /* Each desired edit/room entry is dispatched once. Native rejection is
     * not retried every frame; do not flood analytics or toggle repeatedly.
     * A later explicit shared edit or a newly entered room can retry. */
    gPolicy.active=1;gPolicy.module=module;gPolicy.screen=screen;gPolicy.state=state;gPolicy.room=room;
    gPolicy.roomLow=low;gPolicy.roomHigh=high;gPolicy.desired=desired;gPolicy.native=native;
    return changed;
}
