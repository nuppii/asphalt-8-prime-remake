using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;

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
        [DataMember(Name="private")] public bool IsPrivate { get; set; }
        [DataMember(Name="transport")] public string Transport { get; set; }
        [DataMember(Name="nativeRoomId")] public string NativeRoomId { get; set; }
        [DataMember(Name="discoveryPort")] public int DiscoveryPort { get; set; }
        internal LanRoomListing ToRoom()
        {
            return new LanRoomListing { RoomId=RoomId,Name=Name,Address=Address,Port=Port,
                Players=Players,Capacity=Capacity,IsPrivate=IsPrivate,Transport=Transport,
                NativeRoomId=NativeRoomId,DiscoveryPort=DiscoveryPort };
        }
    }

    [DataContract] internal sealed class PublicList
    {
        [DataMember(Name="rooms")] public List<PublicListing> Rooms { get; set; }
        [DataMember(Name="totalMatches")] public int TotalMatches { get; set; }
        [DataMember(Name="nextOffset")] public int? NextOffset { get; set; }
    }
    [DataContract] internal sealed class PublicResolved
    { [DataMember(Name="room")] public PublicListing Room { get; set; } }
    public sealed class RoomSearchPage
    {
        public List<LanRoomListing> Rooms { get; internal set; }
        public int TotalMatches { get; internal set; }
        public int? NextOffset { get; internal set; }
    }
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
        [DataMember(Name="transport")] public string Transport;
        [DataMember(Name="nativeRoomId",EmitDefaultValue=false)] public string NativeRoomId;
        [DataMember(Name="discoveryPort",EmitDefaultValue=false)] public int DiscoveryPort;
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
            if(response==null)throw new InvalidDataException("The directory returned an empty listing response.");
            if(response.Rooms==null)return result;
            foreach(PublicListing entry in response.Rooms)
            {
                if(entry==null || entry.IsPrivate || entry.Protocol!=1 || string.IsNullOrWhiteSpace(entry.Address) || entry.Address.Length>255)continue;
                LanRoomListing room=entry.ToRoom();
                if(LanRoomDiscovery.Valid(room))result.Add(room);
                if(result.Count>=256)break;
            }
            return result;
        }

        public static List<LanRoomListing> Search(Uri directory,string name,int limit=50,int offset=0,bool prefix=false)
        { return SearchPage(directory,name,limit,offset,prefix).Rooms; }

        public static RoomSearchPage SearchPage(Uri directory,string name,int limit=50,int offset=0,bool prefix=false)
        {
            name=ValidateName(name);
            if(limit<1||limit>256||offset<0||offset>10000)throw new ArgumentOutOfRangeException("limit","Search limit must be 1-256 and offset 0-10000.");
            string path="v1/rooms?name="+Uri.EscapeDataString(name)+"&match="+(prefix?"prefix":"exact")+
                "&limit="+limit.ToString(System.Globalization.CultureInfo.InvariantCulture)+"&offset="+offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var response=(PublicList)Request(directory,path,"GET",null,null,typeof(PublicList));
            if(response==null)throw new InvalidDataException("The directory returned an empty search response.");
            var rooms=new List<LanRoomListing>();
            if(response.Rooms!=null)foreach(PublicListing entry in response.Rooms)
            {
                if(entry==null||entry.IsPrivate||entry.Protocol!=1||string.IsNullOrWhiteSpace(entry.Address)||entry.Address.Length>255)continue;
                LanRoomListing room=entry.ToRoom();
                if(LanRoomDiscovery.Valid(room))rooms.Add(room);
                if(rooms.Count>=limit)break;
            }
            if(response.TotalMatches<0||response.TotalMatches>10000||
                (response.NextOffset.HasValue&&(response.NextOffset.Value<=offset||response.NextOffset.Value>10000)))
                throw new InvalidDataException("The directory supplied invalid search pagination.");
            return new RoomSearchPage{Rooms=rooms,TotalMatches=response.TotalMatches,NextOffset=response.NextOffset};
        }

        public static LanRoomListing Resolve(Uri directory,string roomId)
        {return Resolve(directory,roomId,null);}

        public static LanRoomListing Resolve(Uri directory,string roomId,string expectedName)
        {
            ValidateRoomId(roomId);
            string path="v1/rooms/"+Uri.EscapeDataString(roomId);
            if(expectedName!=null)path+="?name="+Uri.EscapeDataString(ValidateName(expectedName));
            PublicResolved response;
            try{response=(PublicResolved)Request(directory,path,"GET",null,null,typeof(PublicResolved));}
            catch(WebException error)
            {
                var http=error.Response as HttpWebResponse;
                bool absent=http!=null&&http.StatusCode==HttpStatusCode.NotFound;
                if(http!=null)http.Close();
                if(absent)throw new InvalidOperationException("The room is not listed or its lease expired.");
                throw;
            }
            PublicListing entry=response==null?null:response.Room;
            if(entry==null||entry.IsPrivate||entry.Protocol!=1||entry.RoomId!=roomId||
                string.IsNullOrWhiteSpace(entry.Address)||entry.Address.Length>255||!LanRoomDiscovery.Valid(entry.ToRoom()))
                throw new InvalidDataException("The directory returned an invalid public room reference.");
            return entry.ToRoom();
        }

        internal static void ValidateRoomId(string roomId)
        {if(roomId==null||roomId.Length<8||roomId.Length>64||!Regex.IsMatch(roomId,"^[A-Za-z0-9_-]+$"))throw new ArgumentException("Invalid room ID.");}
        internal static string ValidateName(string name)
        {
            string value=(name??"").Trim().Normalize(NormalizationForm.FormC);
            int scalars=0;
            for(int i=0;i<value.Length;i++)
            {
                var category=System.Globalization.CharUnicodeInfo.GetUnicodeCategory(value,i);
                if(category==System.Globalization.UnicodeCategory.Control||category==System.Globalization.UnicodeCategory.Format||
                    category==System.Globalization.UnicodeCategory.Surrogate||category==System.Globalization.UnicodeCategory.PrivateUse||
                    category==System.Globalization.UnicodeCategory.OtherNotAssigned)
                    throw new ArgumentException("Room name contains non-printable characters.");
                if(char.IsHighSurrogate(value[i]))i++;
                scalars++;
            }
            if(scalars<1||scalars>64)throw new ArgumentException("Room name must contain 1-64 printable characters.");
            return value;
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
                var body=new Announcement {Name=room.Name,Port=room.Port,Players=room.Players,Capacity=room.Capacity,Private=false,
                    Transport=room.EffectiveTransport,NativeRoomId=room.NativeRoomId,DiscoveryPort=room.DiscoveryPort};
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
