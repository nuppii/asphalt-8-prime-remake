"""Build an explicit-PID, game-thread-only probe for our compiled AI adapter."""
import argparse
import json
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--image',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--pid',type=int,required=True)
p.add_argument('--state',type=lambda x:int(x,0),required=True)
a=p.parse_args()
if a.pid<=0 or not 0<a.state<2**32:
    p.error('An explicit x86 process and shared-state address are required')
source=r'''
if(Process.id!==PID)throw new Error('Unexpected process');
const game=Process.getModuleByName('Asphalt8.exe');
const state=ptr(STATE);
if(state.readU32()!==0x3849554e||state.add(4).readU32()!==1||state.add(8).readU32()!==3784)throw new Error('Shared-state contract differs');
const raw=new Uint8Array(IMAGE),view=new DataView(raw.buffer);
const pe=view.getUint32(0x3c,true),h=pe+24;
if(view.getUint32(pe,true)!==0x4550||view.getUint16(pe+4,true)!==0x14c)throw new Error('Expected x86 PE');
const size=view.getUint32(h+56,true),entry=view.getUint32(h+16,true);
if(!size||size>65536||entry>=size||view.getUint32(h+104,true))throw new Error('Unsupported helper image');
const image=Memory.alloc(size);image.writeByteArray(new Uint8Array(size));Memory.protect(image,size,'rwx');
const count=view.getUint16(pe+6,true),table=h+view.getUint16(pe+20,true);
for(let i=0;i<count;i++){
 const s=table+i*40,rva=view.getUint32(s+12,true),n=view.getUint32(s+16,true),off=view.getUint32(s+20,true);
 if(rva+n>size||off+n>raw.length)throw new Error('Invalid section');
 if(n)image.add(rva).writeByteArray(raw.slice(off,off+n));
}
let r=view.getUint32(h+136,true),left=view.getUint32(h+140,true);
const delta=image.toUInt32()-view.getUint32(h+28,true);
while(left){
 if(left<8||r+8>size)throw new Error('Invalid relocations');
 const page=image.add(r).readU32(),n=image.add(r+4).readU32();
 if(n<8||n>left||r+n>size||(n&1))throw new Error('Invalid block');
 for(let j=8;j<n;j+=2){const item=image.add(r+j).readU16(),kind=item>>>12,offset=page+(item&4095);if(!kind)continue;if(kind!==3||offset+4>size)throw new Error('Invalid fixup');const v=image.add(offset);v.writeU32((v.readU32()+delta)>>>0);}
 r+=n;left-=n;
}
const tick=new NativeFunction(image.add(entry),'uint',['uint','pointer','pointer'],'mscdecl');
let last='',samples=0;
// Observe the original private-room update; execute only after its UI-thread return.
Interceptor.attach(game.base.add(0xa98ef0),{
 onEnter(){this.screen=this.context.ecx;},
 onLeave(){
  const screen=this.screen;
  if(screen.readPointer().compare(game.base.add(0x1d4ac78))!==0)return;
  const before=screen.add(0x288).readU8(),changed=tick(game.base.toUInt32(),screen,state),after=screen.add(0x288).readU8();
  const text=[state.add(32).readU32(),before,after,changed,screen.add(0x280).readU32()].join('|');
  if(text!==last&&samples++<30){last=text;send({event:'NativeAiPolicyObservation',requested:!!(state.add(32).readU32()&16),before,after,manualSync:changed,menuState:screen.add(0x280).readU32()});}
 }
});
send({event:'NativeAiPolicyProbeReady',pid:Process.id,entry:image.add(entry).toString(),state:state.toString()});
'''
source=source.replace('PID',str(a.pid)).replace('STATE',str(a.state)).replace('IMAGE',json.dumps(list(a.image.read_bytes())))
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(source,encoding='utf-8')
print('Prepared explicit-PID UI-thread probe:',a.output)
