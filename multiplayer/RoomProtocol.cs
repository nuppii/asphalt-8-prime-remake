using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace OfflineLab.Multiplayer
{
    public static class RoomErrors
    {
        public static string Display(Exception error)
        {
            var socket = error as SocketException;
            if (socket != null) return "Network error: " + socket.SocketErrorCode + ". Check the host address and connection.";
            var web = error as System.Net.WebException;
            if (web != null) return "Directory request failed: " + web.Status + ".";
            if (error is IOException) return "The connection or file operation could not be completed.";
            if (error is System.ComponentModel.Win32Exception || error is System.Runtime.InteropServices.ExternalException)
                return "The Windows operation could not be completed. Please try again.";
            return error.Message;
        }
    }

    [DataContract]
    public sealed class RoomSettings
    {
        [DataMember] public bool AllowMutators;
        [DataMember] public bool AllowCheats;
        [DataMember] public bool RequireOwnedCars = true;
        [DataMember] public bool IgnoreClass;
        [DataMember] public bool FillBots = true;
        [DataMember] public int GridSlots = 8;
        [DataMember] public int Laps = 3;
        public int EffectiveGridSlots { get { return GridSlots == 0 ? 8 : GridSlots; } }
        public int EffectiveLaps { get { return Laps == 0 ? 3 : Laps; } }
        public void Validate()
        {
            if (GridSlots < 0 || GridSlots > 60) throw new ArgumentException("Grid size must be Default or between 1 and 60, including the host.");
            if (Laps < 0 || Laps > 60) throw new ArgumentException("Lap count must be Default or between 1 and 60.");
        }
        public RoomSettings Copy() { return (RoomSettings)MemberwiseClone(); }
    }

    [DataContract]
    public sealed class PlayerProfile
    {
        [DataMember] public string Nickname = "Driver";
        [DataMember] public string Status = "Ready";
        [DataMember] public string GroupTag = "";
        public void Validate()
        {
            Nickname = RoomProtocol.Clean(Nickname, 24, false, "Nickname");
            Status = RoomProtocol.Clean(Status, 64, true, "Status");
            GroupTag = RoomProtocol.Clean(GroupTag, 12, true, "Group tag");
        }
    }

    [DataContract]
    public sealed class RoomMember
    {
        [DataMember] public string Id;
        [DataMember] public string Nickname;
        [DataMember] public string Status;
        [DataMember] public string GroupTag;
        [DataMember] public string Endpoint;
        [DataMember] public bool IsHost;
        [DataMember] public bool IsModerator;
        [DataMember] public int RttMs;
        // One-based lobby slot. Zero is an older snapshot without slot assignments.
        [DataMember(EmitDefaultValue=false)] public int GridSlot;
        public RoomMember Copy() { return (RoomMember)MemberwiseClone(); }
    }

    [DataContract]
    public sealed class RoomSnapshot
    {
        [DataMember] public string RoomId;
        [DataMember] public string Name;
        [DataMember] public string HostId;
        [DataMember] public string MapId = "Sector 8";
        [DataMember] public RoomSettings Settings;
        [DataMember] public List<RoomMember> Members = new List<RoomMember>();
        [DataMember] public Dictionary<string, string> Votes = new Dictionary<string, string>();
        [DataMember] public int BotCount;
        [DataMember] public bool IsPrivate;
        [DataMember] public bool RaceBridgeReady;
        [DataMember] public bool HostMigrationReady;
        [DataMember] public bool AbruptHostMigrationReady;
        [DataMember] public long Revision;
    }

    [DataContract]
    internal sealed class WirePacket
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Type;
        [DataMember(EmitDefaultValue=false)] public string Id;
        [DataMember(EmitDefaultValue=false)] public string Text;
        [DataMember(EmitDefaultValue=false)] public string Salt;
        [DataMember(EmitDefaultValue=false)] public string Nonce;
        [DataMember(EmitDefaultValue=false)] public string Proof;
        [DataMember(EmitDefaultValue=false)] public string HostProof;
        [DataMember(EmitDefaultValue=false)] public string SessionProof;
        [DataMember(EmitDefaultValue=false)] public string SessionTicket;
        [DataMember(EmitDefaultValue=false)] public Dictionary<string,string> Tickets;
        [DataMember(EmitDefaultValue=false)] public PlayerProfile Profile;
        [DataMember(EmitDefaultValue=false)] public RoomSettings Settings;
        [DataMember(EmitDefaultValue=false)] public RoomSnapshot Snapshot;
        [DataMember(EmitDefaultValue=false)] public int Iterations;
        [DataMember(EmitDefaultValue=false)] public int Port;
        [DataMember(EmitDefaultValue=false)] public long Ping;
    }

    internal static class RoomProtocol
    {
        internal const int MaximumFrame = 65536;
        internal const int PasswordIterations = 100000;
        internal static byte[] RandomBytes(int count)
        { var value=new byte[count]; using(var random=RandomNumberGenerator.Create())random.GetBytes(value); return value; }
        internal static byte[] Derive(string password, byte[] salt)
        { using(var kdf=new Rfc2898DeriveBytes(password??"",salt,PasswordIterations))return kdf.GetBytes(32); }
        internal static string Proof(byte[] key,string room,string nonce,string clientNonce,PlayerProfile profile,string identity)
        {
            string value=room+"\n"+nonce+"\n"+clientNonce+"\n"+identity+"\n"+profile.Nickname+"\n"+profile.Status+"\n"+profile.GroupTag;
            using(var hmac=new HMACSHA256(key))return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        internal static bool EqualProof(string expected,string observed)
        {
            byte[] a,b;
            try { a=Convert.FromBase64String(expected??"");b=Convert.FromBase64String(observed??""); }
            catch(FormatException){return false;}
            int difference=a.Length^b.Length;
            for(int i=0;i<a.Length;i++)difference|=a[i]^(i<b.Length?b[i]:0);
            return difference==0;
        }
        internal static string Clean(string input,int limit,bool empty,string label)
        {
            string value=(input??"").Trim();
            if ((!empty&&value.Length==0)||value.Length>limit)throw new ArgumentException(label+" has an invalid length.");
            foreach(char c in value)if(char.IsControl(c))throw new ArgumentException(label+" contains control characters.");
            return value;
        }
        internal static void Write(NetworkStream stream,WirePacket packet)
        {
            byte[] bytes;
            using(var memory=new MemoryStream())
            { new DataContractJsonSerializer(typeof(WirePacket)).WriteObject(memory,packet);bytes=memory.ToArray(); }
            if(bytes.Length>MaximumFrame)throw new InvalidDataException("Room packet exceeds the frame limit.");
            byte[] header={ (byte)(bytes.Length>>24),(byte)(bytes.Length>>16),(byte)(bytes.Length>>8),(byte)bytes.Length };
            stream.Write(header,0,4);stream.Write(bytes,0,bytes.Length);
        }
        internal static WirePacket Read(NetworkStream stream)
        {
            byte[] header=new byte[4];ReadExact(stream,header);
            int size=(header[0]<<24)|(header[1]<<16)|(header[2]<<8)|header[3];
            if(size<2||size>MaximumFrame)throw new InvalidDataException("Invalid room frame length.");
            byte[] bytes=new byte[size];ReadExact(stream,bytes);
            using(var memory=new MemoryStream(bytes))
            {
                var result=(WirePacket)new DataContractJsonSerializer(typeof(WirePacket)).ReadObject(memory);
                if(result==null||result.Version!=1||string.IsNullOrEmpty(result.Type))throw new InvalidDataException("Unsupported room protocol.");
                return result;
            }
        }
        private static void ReadExact(Stream stream,byte[] bytes)
        {
            int offset=0;
            while(offset<bytes.Length)
            {int count=stream.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException("The room connection closed.");offset+=count;}
        }
    }
}
