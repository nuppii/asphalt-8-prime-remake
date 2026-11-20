using System;

// The UI ABI remains v1/3784. Networking has its own versioned extension.
internal static class NativeNetworkState
{
    internal const int UiBytes=3784, Offset=3784, Bytes=256, BufferBytes=4040;
    internal const uint Magic=0x38544e4e;
    internal const int HostRevision=3796,HostAlive=3800,HostIdLow=3804,HostIdHigh=3808,HostLobbyPort=3812,HostDiscoveryPort=3816,HostPlayers=3820,HostCapacity=3824,HostPrivate=3828,HostName=3832;
    internal const int ClientConnected=3896,ClientIdLow=3900,ClientIdHigh=3904;
    internal const int JoinSeq=3912,JoinAck=3916,JoinPhase=3920,JoinIdLow=3924,JoinIdHigh=3928,JoinAddress=3932,JoinName=3948,JoinableMask=4012,HostEndRevision=4016,JoinLobbyPort=4020;
    internal static bool Valid(byte[] state){return state!=null&&state.Length>=BufferBytes&&BitConverter.ToUInt32(state,Offset)==Magic&&BitConverter.ToUInt32(state,Offset+4)==1&&BitConverter.ToUInt32(state,Offset+8)==Bytes;}
}
