#include "NativeNetworkState.h"
#include "NativeRaceBoard.h"

typedef unsigned U;
typedef unsigned char Byte;
typedef unsigned __int64 U64;
typedef struct NativeString { union { char small[16]; const char *data; }; U size, capacity; } NativeString;
typedef struct Proof { U64 time, nonce; Byte tag[16]; } Proof;
typedef struct ProofInput { U room[2], user[4]; U64 time, nonce; } ProofInput;
typedef char ProofSizeMustMatch[(sizeof(Proof)==32&&sizeof(ProofInput)==40)?1:-1];
typedef void (__stdcall *GetFileTime)(U64 *);
typedef void *(__fastcall *AssignString)(NativeString *, void *, const void *, U);
typedef Byte (__fastcall *SendProfile)(void *, void *, void *);
static const char proofField[] = "A8OL-PASSWORD-1";
static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
static U serial;
static struct UsedProof { U64 time, nonce; } used[128];
static volatile long replayLock;
static U usedRoom[2];

static void *At(U module,U va){return (void *)(module+va-0x400000U);}
static U ReadWord(const void *p,U offset){return *(const U *)((const Byte *)p+offset);}
static void CopyBytes(void *out,const void *in,U n)
{
    Byte *d=(Byte *)out;
    const Byte *s=(const Byte *)in;
    U i;
    for(i=0;i<n;++i)d[i]=s[i];
}
static int EqualBytes(const void *left,const void *right,U n)
{
    const Byte *a=(const Byte *)left,*b=(const Byte *)right;
    U i,difference=0;
    for(i=0;i<n;++i)difference|=a[i]^b[i];
    return difference==0;
}
static void EncodeProof(const Proof *proof,char *encoded)
{
    const Byte *bytes=(const Byte *)proof;
    U i,j=0;
    for(i=0;i<sizeof(*proof);i+=3){
        U value=(U)bytes[i]<<16;
        if(i+1<sizeof(*proof))value|=(U)bytes[i+1]<<8;
        if(i+2<sizeof(*proof))value|=bytes[i+2];
        encoded[j++]=alphabet[(value>>18)&63];
        encoded[j++]=alphabet[(value>>12)&63];
        if(i+1<sizeof(*proof))encoded[j++]=alphabet[(value>>6)&63];
        if(i+2<sizeof(*proof))encoded[j++]=alphabet[value&63];
    }
}
static int DecodeProof(const char *encoded,U length,Proof *proof)
{
    Byte *bytes=(Byte *)proof;
    U i,j,bits=0,value=0,p=0;
    if(!encoded||length!=43)return 0;
    for(i=0;i<length;++i){
        for(j=0;j<64&&alphabet[j]!=encoded[i];++j){}
        if(j==64)return 0;
        value=(value<<6)|j;bits+=6;
        if(bits>=8){bits-=8;bytes[p++]=(Byte)(value>>bits);}
    }
    return p==sizeof(*proof)&&(value&3)==0;
}
static U Rotate(U x,U n){return (x>>n)|(x<<(32-n));}
static void ShaBlock(U *h,const Byte *block)
{
    static const U k[64]={
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    U w[64],a,b,c,d,e,f,g,two,t1,t2,i;
    for(i=0;i<16;++i){const Byte *p=block+i*4;w[i]=((U)p[0]<<24)|((U)p[1]<<16)|((U)p[2]<<8)|p[3];}
    for(i=16;i<64;++i){
        U x=w[i-15], y=w[i-2];
        w[i]=w[i-16]+(Rotate(x,7)^Rotate(x,18)^(x>>3))+w[i-7]+(Rotate(y,17)^Rotate(y,19)^(y>>10));
    }
    a=h[0];b=h[1];c=h[2];d=h[3];e=h[4];f=h[5];g=h[6];two=h[7];
    for(i=0;i<64;++i){
        t1=two+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+k[i]+w[i];
        t2=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));
        two=g;g=f;f=e;e=d+t1;d=c;c=b;b=a;a=t1+t2;
    }
    h[0]+=a;h[1]+=b;h[2]+=c;h[3]+=d;h[4]+=e;h[5]+=f;h[6]+=g;h[7]+=two;
}
static void ShaKeyed(const Byte *key,Byte pad,const Byte *data,U length,Byte *out)
{
    U h[8]={0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    Byte block[64];
    U i,bits=(64+length)*8;
    for(i=0;i<64;++i)block[i]=i<32?(Byte)(key[i]^pad):pad;
    ShaBlock(h,block);
    for(i=0;i<64;++i)block[i]=i<length?data[i]:0;
    block[length]=0x80;
    block[62]=(Byte)(bits>>8);block[63]=(Byte)bits;
    ShaBlock(h,block);
    for(i=0;i<8;++i){U word=h[i];out[i*4]=(Byte)(word>>24);out[i*4+1]=(Byte)(word>>16);out[i*4+2]=(Byte)(word>>8);out[i*4+3]=(Byte)word;}
}
static void NativePasswordMac(const Byte *key,const ProofInput *input,Byte *out)
{
    Byte inner[32];
    ShaKeyed(key,0x36,(const Byte *)input,sizeof(*input),inner);
    ShaKeyed(key,0x5c,inner,sizeof(inner),out);
}
static const char *StringData(const NativeString *str){return str->capacity<16?str->small:str->data;}
static int RoomId(U module,const void *lobby,U *id)
{
    void *room=*(void **)((const Byte *)lobby+0xc);
    if(!room||ReadWord(room,0)!=(U)At(module,0x216a510))return 0;
    id[0]=ReadWord(room,0x50);id[1]=ReadWord(room,0x54);
    return (id[0]|id[1])!=0;
}
static U64 Now(U module)
{
    U64 result=0;
    (*(GetFileTime *)At(module,0x1c84108))(&result);
    return result;
}
static int WithinWindow(U64 a,U64 b){return (a>b?a-b:b-a)<=300000000ULL;}
static int MarkOnce(const U *room,const Proof *proof,U64 now)
{
    long wasLocked;
    U i,slot=128;
    __asm { mov eax,1
            xchg eax,replayLock
            mov wasLocked,eax }
    if(wasLocked)return 0;
    if(room[0]!=usedRoom[0]||room[1]!=usedRoom[1]){
        usedRoom[0]=room[0];usedRoom[1]=room[1];
        for(i=0;i<128;++i)used[i].time=0;
    }
    for(i=0;i<128;++i){
        if(used[i].time&&WithinWindow(used[i].time,now)){
            if(used[i].time==proof->time&&used[i].nonce==proof->nonce){slot=128;break;}
        }else if(slot==128)slot=i;
    }
    if(slot<128){used[slot].time=proof->time;used[slot].nonce=proof->nonce;}
    replayLock=0;
    return slot<128;
}
__declspec(dllexport) U __cdecl NativeSendProtectedProfile(U module,NativeUiState *ui,void *lobby,void *connection,void *profile)
{
    NativePasswordState *state=NativePassword(ui);
    Byte *bytes=(Byte *)profile;
    ProofInput input;
    Proof proof;
    Byte fullTag[32],result;
    char encoded[43];
    NativeString *pair;
    AssignString assign=(AssignString)At(module,0x535ba0);
    if(state->enabled==1&&state->guestReady==1&&RoomId(module,lobby,input.room)&&
        state->joinLow==input.room[0]&&state->joinHigh==input.room[1]){
        if(bytes[0x40]>=16){state->result=2;return 0;}
        input.time=Now(module);
        CopyBytes(&input.nonce,state->nonceSeed,8);
        input.nonce^=(U64)++serial;
        CopyBytes(input.user,bytes+0x30,16);
        proof.time=input.time;proof.nonce=input.nonce;
        NativePasswordMac(state->guestKey,&input,fullTag);
        CopyBytes(proof.tag,fullTag,sizeof(proof.tag));
        pair=(NativeString *)(bytes+0x44+bytes[0x40]*0x30);
        EncodeProof(&proof,encoded);
        assign(pair,0,proofField,sizeof(proofField)-1);
        assign(pair+1,0,encoded,sizeof(encoded));
        ++bytes[0x40];
        result=((SendProfile)At(module,0x150bd90))(connection,0,profile);
        --bytes[0x40]; // The caller still owns and releases the two temporary strings.
        if(result){
            U *scoreRoom=(U *)((Byte *)ui+NATIVE_RACE_PROOF_ROOM_OFFSET);
            scoreRoom[0]=input.room[0];scoreRoom[1]=input.room[1];
            state->joinLow=0;state->joinHigh=0;
        }
        return result;
    }
    return ((SendProfile)At(module,0x150bd90))(connection,0,profile);
}
__declspec(dllexport) U __cdecl NativeAuthorizeProfile(U module,NativeUiState *ui,void *lobby,void *connection,void *profile)
{
    NativePasswordState *state=NativePassword(ui);
    Byte *bytes=(Byte *)profile,fullTag[32];
    NativeString *pair;
    ProofInput input;
    Proof proof;
    U64 now;
    int hasProof=0,isPrivate=(ui->policyBits&NUI_PRIVATE)!=0;
    if(state->enabled!=1)return 1;
    if(bytes[0x40]&&bytes[0x40]<=16){
        pair=(NativeString *)(bytes+0x44+(bytes[0x40]-1)*0x30);
        if(pair->size==sizeof(proofField)-1&&EqualBytes(StringData(pair),proofField,sizeof(proofField)-1)){
            --bytes[0x40];
            hasProof=DecodeProof(StringData(pair+1),pair[1].size,&proof);
        }
    }
    if(!isPrivate||ReadWord(connection,0x134))return 1;
    if(state->hostReady!=1||!hasProof||!RoomId(module,lobby,input.room))return 0;
    now=Now(module);
    if(!WithinWindow(now,proof.time))return 0;
    input.time=proof.time;input.nonce=proof.nonce;
    CopyBytes(input.user,bytes+0x30,16);
    NativePasswordMac(state->hostKey,&input,fullTag);
    return EqualBytes(proof.tag,fullTag,sizeof(proof.tag))&&MarkOnce(input.room,&proof,now);
}
