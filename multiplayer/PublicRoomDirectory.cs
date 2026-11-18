using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace OfflineLab.Multiplayer
{
    [DataContract]
    internal sealed class PublicListing
    {
        [DataMember(Name="roomId")] public string RoomId { get; set; }
        [DataMember(Name="name")] public string Name { get; set; }
        [DataMember(Name="address")] public string Address { get; set; }
        [DataMember(Name="port")] public int Port { get; set; }
        [DataMember(Name="players")] public int Players { get; set; }
        [DataMember(Name="capacity")] public int Capacity { get; set; }
        [DataMember(Name="protocol")] public int Protocol { get; set; }
        internal LanRoomListing ToRoom()
        {
            return new LanRoomListing { RoomId=RoomId,Name=Name,Address=Address,Port=Port,
                Players=Players,Capacity=Capacity,IsPrivate=false };
        }
    }

    [DataContract] internal sealed class PublicList
    { [DataMember(Name="rooms")] public List<PublicListing> Rooms { get; set; } }
    [DataContract] internal sealed class LeaseReply
    {
        [DataMember(Name="leaseToken")] public string Token { get; set; }
        [DataMember(Name="leaseSeconds")] public double Seconds { get; set; }
    }
    [DataContract] internal sealed class Announcement
    {
        [DataMember(Name="name")] public string Name;
        [DataMember(Name="port")] public int Port;
        [DataMember(Name="players")] public int Players;
        [DataMember(Name="capacity")] public int Capacity;
        [DataMember(Name="private")] public bool Private;
    }

    public static class PublicRoomDirectory
    {
        public static void Validate(Uri directory)
        {
            if(directory==null || !directory.IsAbsoluteUri || directory.UserInfo.Length!=0 ||
                directory.Query.Length!=0 || directory.Fragment.Length!=0 ||
                (directory.Scheme!="https" && !(directory.Scheme=="http" && directory.IsLoopback)))
                throw new ArgumentException("Use an HTTPS directory URL. HTTP is allowed only for local tests.");
        }

        internal static Uri Endpoint(Uri directory,string relative)
        {
            Validate(directory);
            return new Uri(directory.AbsoluteUri.TrimEnd('/')+"/"+relative);
        }

        public static List<LanRoomListing> Fetch(Uri directory)
        {
            var response=(PublicList)Request(directory,"v1/rooms","GET",null,null,typeof(PublicList));
            var result=new List<LanRoomListing>();
            if(response.Rooms==null)return result;
            foreach(PublicListing entry in response.Rooms)
            {
                if(entry==null || entry.Protocol!=1 || string.IsNullOrWhiteSpace(entry.Address) || entry.Address.Length>255)continue;
                LanRoomListing room=entry.ToRoom();
                if(LanRoomDiscovery.Valid(room))result.Add(room);
                if(result.Count>=256)break;
            }
            return result;
        }

        internal static object Request(Uri directory,string path,string method,object body,string token,Type responseType)
        {
            var request=(HttpWebRequest)WebRequest.Create(Endpoint(directory,path));
            request.Method=method;
            request.Timeout=4000;
            request.ReadWriteTimeout=4000;
            request.AllowAutoRedirect=false;
            request.Proxy=null;
            request.ContentType="application/json";
            if(token!=null)request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
            if(body!=null)
            {
                byte[] data;
                using(var stream=new MemoryStream())
                {
                    new DataContractJsonSerializer(body.GetType()).WriteObject(stream,body);
                    data=stream.ToArray();
                }
                request.ContentLength=data.Length;
                using(Stream output=request.GetRequestStream())output.Write(data,0,data.Length);
            }
            using(var response=(HttpWebResponse)request.GetResponse())
            using(Stream input=response.GetResponseStream())
            using(var data=new MemoryStream())
            {
                if(response.ContentLength>1048576)throw new InvalidDataException("Directory response exceeds its limit.");
                byte[] buffer=new byte[4096];int count;
                while((count=input.Read(buffer,0,buffer.Length))!=0)
                {
                    if(data.Length+count>1048576)throw new InvalidDataException("Directory response exceeds its limit.");
                    data.Write(buffer,0,count);
                }
                if(responseType==null)return null;
                data.Position=0;
                return new DataContractJsonSerializer(responseType).ReadObject(data);
            }
        }
    }

    public sealed class PublicRoomAdvertiser : IDisposable
    {
        private readonly Uri directory;
        private readonly Func<LanRoomListing> listing;
        private readonly Timer timer;
        private readonly object gate=new object();
        private string token,roomId;
        private bool disposed;
        public event Action<string> StatusChanged;

        public PublicRoomAdvertiser(Uri directory,Func<LanRoomListing> listing)
        {
            PublicRoomDirectory.Validate(directory);
            if(listing==null)throw new ArgumentNullException("listing");
            this.directory=directory;this.listing=listing;
            timer=new Timer(Publish,null,0,30000);
        }

        private void Status(string value)
        {
            Action<string> handler=StatusChanged;
            if(handler!=null)handler(value);
        }

        private void Publish(object ignored)
        {
            if(!Monitor.TryEnter(gate))return;
            try
            {
                if(disposed)return;
                LanRoomListing room=listing();
                if(!LanRoomDiscovery.Valid(room) || room.IsPrivate)return;
                if(roomId!=null && roomId!=room.RoomId)throw new InvalidOperationException("A directory lease cannot change rooms.");
                var body=new Announcement {Name=room.Name,Port=room.Port,Players=room.Players,Capacity=room.Capacity,Private=false};
                LeaseReply reply;
                try { reply=(LeaseReply)PublicRoomDirectory.Request(directory,"v1/rooms/"+Uri.EscapeDataString(room.RoomId),"POST",body,token,typeof(LeaseReply)); }
                catch(WebException ex)
                {
                    var response=ex.Response as HttpWebResponse;
                    bool expired=response!=null && response.StatusCode==HttpStatusCode.Forbidden && token!=null;
                    if(response!=null)response.Close();
                    if(!expired)throw;
                    token=null;
                    throw new InvalidOperationException("The public room lease was lost. Recreate the room to publish it again.");
                }
                roomId=room.RoomId;
                if(reply.Token!=null)token=reply.Token;
                Status("Public directory: room listed (host reachability is not verified).");
            }
            catch(Exception ex) { Status("Public directory unavailable: "+RoomErrors.Display(ex)); }
            finally { Monitor.Exit(gate); }
        }

        public void Dispose()
        {
            timer.Dispose();
            lock(gate)
            {
                if(disposed)return;
                disposed=true;
                if(token!=null && roomId!=null)
                    try { PublicRoomDirectory.Request(directory,"v1/rooms/"+Uri.EscapeDataString(roomId),"DELETE",null,token,null); }
                    catch(WebException) { /* The lease expires if the directory is unreachable. */ }
            }
        }
    }
}
