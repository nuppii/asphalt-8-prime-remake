using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;

namespace OfflineLab.Multiplayer
{
    public static class RoomTransports
    {
        public const string RoomControlTcp="room-control-tcp";
        public const string Asphalt480Udp="asphalt480-udp";
    }
    [DataContract]
    public sealed class LanRoomListing
    {
        [DataMember] public string RoomId;
        [DataMember] public string Name;
        [DataMember] public string Address;
        [DataMember] public int Port;
        [DataMember] public int Players;
        [DataMember] public int Capacity;
        [DataMember] public bool IsPrivate;
        [DataMember(EmitDefaultValue=false)] public bool PasswordProtected;
        [DataMember(EmitDefaultValue=false)] public int NativePasswordVersion;
        [DataMember(EmitDefaultValue=false)] public string Transport;
        [DataMember(EmitDefaultValue=false)] public string NativeRoomId;
        [DataMember(EmitDefaultValue=false)] public int DiscoveryPort;
        public string EffectiveTransport {get{return Transport??RoomTransports.RoomControlTcp;}}
        public bool IsNative {get{return EffectiveTransport==RoomTransports.Asphalt480Udp;}}
    }

    [DataContract]
    internal sealed class DiscoveryReply
    {
        [DataMember] public string Nonce;
        [DataMember] public LanRoomListing Room;
    }

    public static class LanRoomDiscovery
    {
        public const int DiscoveryPort = 47780;
        private const string Prefix = "A8OL_DISCOVER_V1 ";

        public static List<LanRoomListing> Discover(int milliseconds = 1200)
        {
            if (milliseconds < 100 || milliseconds > 10000) throw new ArgumentOutOfRangeException("milliseconds");
            string nonce = Guid.NewGuid().ToString("N");
            byte[] query = Encoding.ASCII.GetBytes(Prefix + nonce);
            var rooms = new Dictionary<string, LanRoomListing>(StringComparer.Ordinal);
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                socket.EnableBroadcast = true;
                socket.Client.ReceiveTimeout = 100;
                socket.Send(query, query.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                socket.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                DateTime stop = DateTime.UtcNow.AddMilliseconds(milliseconds);
                while (DateTime.UtcNow < stop && rooms.Count < 256)
                {
                    var source = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data;
                    try { data = socket.Receive(ref source); }
                    catch (SocketException ex)
                    {
                        // Windows reports an ICMP port-unreachable reply as a reset
                        // when no local advertiser is listening. This is an empty scan.
                        if (ex.SocketErrorCode == SocketError.TimedOut ||
                            ex.SocketErrorCode == SocketError.ConnectionReset ||
                            ex.SocketErrorCode == SocketError.ConnectionRefused) continue;
                        throw;
                    }
                    if (data.Length > 4096) continue;
                    try
                    {
                        DiscoveryReply reply;
                        using (var stream = new MemoryStream(data))
                            reply = (DiscoveryReply)new DataContractJsonSerializer(typeof(DiscoveryReply)).ReadObject(stream);
                        if (reply == null || reply.Nonce != nonce || !Valid(reply.Room) || reply.Room.IsPrivate) continue;
                        reply.Room.Address = source.Address.ToString();
                        rooms[reply.Room.RoomId + ":" + reply.Room.Port] = reply.Room;
                    }
                    catch (SerializationException) { }
                    catch (ArgumentException) { }
                    catch (System.Xml.XmlException) { }
                }
            }
            var result = new List<LanRoomListing>(rooms.Values);
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        internal static bool Valid(LanRoomListing room)
        {
            return room != null && !string.IsNullOrWhiteSpace(room.RoomId) && room.RoomId.Length <= 128 &&
                !string.IsNullOrWhiteSpace(room.Name) && room.Name.Length <= 64 && room.Port > 0 && room.Port <= 65535 &&
                room.Players >= 0 && room.Capacity >= 1 && room.Capacity <= 60 && room.Players <= room.Capacity && ValidTransport(room);
        }

        public static bool ValidTransport(LanRoomListing room)
        {
            if(room==null)return false;
            if(room.EffectiveTransport==RoomTransports.RoomControlTcp)return room.NativeRoomId==null&&room.DiscoveryPort==0&&room.NativePasswordVersion==0;
            return room.IsNative&&room.NativePasswordVersion==(room.PasswordProtected?1:0)&&room.NativeRoomId!=null&&Regex.IsMatch(room.NativeRoomId,@"\A[0-9a-fA-F]{16}\z")&&
                room.DiscoveryPort==6529&&!string.Equals(room.RoomId,room.NativeRoomId,StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Query(byte[] data, out string nonce)
        {
            nonce = null;
            if (data.Length != Prefix.Length + 32) return false;
            string text = Encoding.ASCII.GetString(data);
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            Guid parsed;
            nonce = text.Substring(Prefix.Length);
            return Guid.TryParseExact(nonce, "N", out parsed);
        }
    }

    public sealed class LanRoomAdvertiser : IDisposable
    {
        private readonly UdpClient socket;
        private readonly Func<LanRoomListing> listing;
        private readonly Thread worker;
        private volatile bool disposed;

        public LanRoomAdvertiser(Func<LanRoomListing> listing)
        {
            if (listing == null) throw new ArgumentNullException("listing");
            this.listing = listing;
            socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                socket.Client.ExclusiveAddressUse = false;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, LanRoomDiscovery.DiscoveryPort));
                socket.Client.ReceiveTimeout = 200;
            }
            catch { socket.Close(); throw; }
            worker = new Thread(Run) { IsBackground = true, Name = "Asphalt room discovery" };
            worker.Start();
        }

        private void Run()
        {
            while (!disposed)
            {
                try
                {
                    var source = new IPEndPoint(IPAddress.Any, 0);
                    byte[] query = socket.Receive(ref source);
                    string nonce;
                    if (!LanRoomDiscovery.Query(query, out nonce)) continue;
                    LanRoomListing room = listing();
                    if (!LanRoomDiscovery.Valid(room) || room.IsPrivate) continue;
                    var reply = new DiscoveryReply { Nonce = nonce, Room = room };
                    byte[] data;
                    using (var stream = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(DiscoveryReply)).WriteObject(stream, reply);
                        data = stream.ToArray();
                    }
                    if (data.Length <= 4096) socket.Send(data, data.Length, source);
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (disposed) return; }
                catch (InvalidOperationException) { }
                catch (SerializationException) { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            socket.Close();
            if (Thread.CurrentThread != worker) worker.Join(1000);
        }
    }
}
