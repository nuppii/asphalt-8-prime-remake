// Bounded diagnostics for an explicitly selected isolated Asphalt 4.8.0 process.
// No packet payloads, hardware token, save data, or application secrets are read.
const module = Process.getModuleByName('Asphalt8.exe');
const at = va => module.base.add(va - 0x400000);
let events = 0;
function report(event, fields = {}) { if (events++ < 240) send({event, pid: Process.id, ...fields}); }
function bytes(p, n) { return Array.from(new Uint8Array(p.readByteArray(n))).map(x => x.toString(16).padStart(2, '0')).join(''); }
function endpoint(p) {
    try {
        const family = p.readU16();
        const port = p.add(2).readU8() * 256 + p.add(3).readU8();
        return {family, port, address: family === 2 ? [4,5,6,7].map(i => p.add(i).readU8()).join('.') : 'IPv6'};
    } catch (_) { return {unreadable: true}; }
}
const ws = Process.getModuleByName('ws2_32.dll');
Interceptor.attach(ws.getExportByName('bind'), {
    onEnter(args) { this.socket = args[0].toString(); this.endpoint = endpoint(args[1]); },
    onLeave(value) { const result=value.toInt32(), error=result === -1 ? this.lastError : 0; report('UdpBind', {socket:this.socket, ...this.endpoint, result, error}); }
});
const sent = new Set(), received = new Set();
Interceptor.attach(ws.getExportByName('sendto'), {
    onEnter(args) { const ep=endpoint(args[4]), key=args[0]+'|'+ep.address+'|'+ep.port; this.sample=!sent.has(key); this.ep=ep; this.socket=args[0].toString(); this.length=args[2].toInt32(); if(this.sample)sent.add(key); },
    onLeave(value) { if(this.sample) { const result=value.toInt32(), error=result===-1?this.lastError:0;report('UdpSendSample',{socket:this.socket,...this.ep,length:this.length,result,error}); } }
});
Interceptor.attach(ws.getExportByName('recvfrom'), {
    onEnter(args) {this.socket=args[0].toString();this.from=args[4];},
    onLeave(value) {const length=value.toInt32();if(length>0&&!received.has(this.socket)){received.add(this.socket);report('UdpReceiveSample',{socket:this.socket,...endpoint(this.from),length});}}
});
Interceptor.attach(at(0x150d4e0), {
    onEnter() {this.lobby=this.context.ecx;},
    onLeave(value) { try {const user=this.lobby.add(8).readPointer();report('NativeLobbyInitialized',{lobby:this.lobby.toString(),result:value.toInt32(),user:user.toString(),nativeId:user.isNull()?null:bytes(user.add(0xe8),16)});}catch(e){report('LobbyReadFailure',{message:String(e)});} }
});
Interceptor.attach(at(0x150d7a0), {
    onEnter(args) {this.lobby=this.context.ecx;this.capacity=args[1].toInt32();},
    onLeave(value) {try{const room=this.lobby.add(0xc).readPointer();report('NativeHost',{result:value.toInt32(),capacity:this.capacity,lobby:this.lobby.toString(),room:room.toString(),roomId:room.isNull()?null:bytes(room.add(0x50),8)});}catch(e){report('HostReadFailure',{message:String(e)});}}
});
Interceptor.attach(at(0x150d940), {
    onEnter(args) {report('NativeJoinEntered',{lobby:this.context.ecx.toString(),descriptor:args[0].toString()});},
    onLeave(value) {report('NativeJoinReturned',{result:value.toInt32()});}
});
Interceptor.attach(at(0xd6d200), {
    onEnter(args) {try{const v=args[1];report('NetworkServerStart',{server:this.context.ecx.toString(),humans:v.add(4).readU32()-v.readU32()>>>2});}catch(e){report('ServerReadFailure',{message:String(e)});}},
    onLeave(value) {report('NetworkServerStartReturned',{result:value.toInt32()});}
});
Interceptor.attach(at(0x11704b0), {onEnter(){report('NativeAloneDialog');}});
report('NativeLanDiagnosticsReady',{base:module.base.toString()});
