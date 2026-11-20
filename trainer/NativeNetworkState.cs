using System;

// The UI ABI remains v1/3784. Networking has its own versioned extension.
internal static class NativeNetworkState
{
    internal const int UiBytes=3784, Offset=3784, Bytes=256, BufferBytes=0x16bc;
    internal const int RaceTabHeld=0x10a0, RaceGuestAddress=0x10a4, RaceGuestLow=0x10a8, RaceGuestHigh=0x10ac, RaceProofRoom=0x10b0,
        HostRaceBoard=0x1100, GuestRaceBoard=0x1400, RaceBoardBytes=700, RaceRowBytes=56;
    internal const int PasswordOffset=0x1020, PasswordEnabled=PasswordOffset, HostPasswordReady=PasswordOffset+4,
        GuestPasswordReady=PasswordOffset+8, HostPasswordKey=PasswordOffset+12, GuestPasswordKey=PasswordOffset+44,
        PasswordNonceSeed=PasswordOffset+76, JoinUsePassword=PasswordOffset+104;
    internal const int InternetLobbyPort=6530, InternetRacePort=6540;
    internal const uint Magic=0x38544e4e;
    internal const int EditToken=4024, KeyWord=4028, KeySequence=4032, KeyAck=4036;
    internal const int HostRevision=3796,HostAlive=3800,HostIdLow=3804,HostIdHigh=3808,HostLobbyPort=3812,HostDiscoveryPort=3816,HostPlayers=3820,HostCapacity=3824,HostPrivate=3828,HostName=3832;
    internal const int ClientConnected=3896,ClientIdLow=3900,ClientIdHigh=3904;
    internal const int JoinSeq=3912,JoinAck=3916,JoinPhase=3920,JoinIdLow=3924,JoinIdHigh=3928,JoinAddress=3932,JoinName=3948,JoinableMask=4012,HostEndRevision=4016,JoinLobbyPort=4020;
    internal static bool Valid(byte[] state){return state!=null&&state.Length>=BufferBytes&&BitConverter.ToUInt32(state,Offset)==Magic&&BitConverter.ToUInt32(state,Offset+4)==1&&BitConverter.ToUInt32(state,Offset+8)==Bytes;}
}
