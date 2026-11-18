using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace OfflineLab.Multiplayer
{
    [DataContract]
    public sealed class RoomInvitation
    {
        [DataMember] public int Version=1;
        [DataMember] public string RoomId;
        [DataMember] public string RoomName;
        [DataMember(EmitDefaultValue=false)] public string DirectoryUrl;
        [DataMember(EmitDefaultValue=false)] public string Address;
        [DataMember(EmitDefaultValue=false)] public int Port;
        [DataMember] public bool IsPrivate;
        // Passwords, lease tokens and player session tickets are never included.
    }

    public sealed class ResolvedRoomInvitation
    {
        public string RoomId,RoomName,Address;
        public int Port;
        public bool IsPrivate,CapacityKnown;
        public int Players=-1,Capacity=-1;
    }

    public sealed class AmbiguousRoomNameException : InvalidOperationException
    {
        public List<LanRoomListing> Choices { get; private set; }
        public int TotalMatches { get; private set; }
        public int? NextOffset { get; private set; }
        internal AmbiguousRoomNameException(RoomSearchPage page)
            : base("More than one room has that name. Select a room from the matching choices.")
        {Choices=page.Rooms;TotalMatches=page.TotalMatches;NextOffset=page.NextOffset;}
    }

    public static class RoomInvites
    {
        private const string Prefix="a8ol:v1:";
        public static RoomInvitation CreateDirect(string roomId,string name,string address,int port,bool isPrivate)
        {
            var invite=new RoomInvitation{RoomId=roomId,RoomName=name,Address=address,Port=port,IsPrivate=isPrivate};
            Validate(invite);return invite;
        }
        public static RoomInvitation CreateDirectory(string roomId,string name,Uri directory)
        {
            PublicRoomDirectory.Validate(directory);
            var invite=new RoomInvitation{RoomId=roomId,RoomName=name,DirectoryUrl=directory.AbsoluteUri};
            Validate(invite);return invite;
        }
        public static string Encode(RoomInvitation invite)
        {
            Validate(invite);byte[] data;
            using(var stream=new MemoryStream())
            {new DataContractJsonSerializer(typeof(RoomInvitation)).WriteObject(stream,invite);data=stream.ToArray();}
            if(data.Length>4096)throw new ArgumentException("Room invitation is too long.");
            return Prefix+Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        public static RoomInvitation Parse(string reference)
        {
            string value=(reference??"").Trim();
            if(value.Length>6000||!value.StartsWith(Prefix,StringComparison.Ordinal))throw new ArgumentException("Unsupported room invitation reference.");
            string body=value.Substring(Prefix.Length);
            if(body.Length<1||!Regex.IsMatch(body,"^[A-Za-z0-9_-]+$"))throw new ArgumentException("Invalid room invitation encoding.");
            body=body.Replace('-','+').Replace('_','/');
            if(body.Length%4==1)throw new ArgumentException("Invalid room invitation encoding.");
            body=body.PadRight(body.Length+(4-body.Length%4)%4,'=');
            RoomInvitation invite;
            try
            {
                byte[] data=Convert.FromBase64String(body);
                if(data.Length>4096)throw new ArgumentException("Room invitation is too long.");
                using(var stream=new MemoryStream(data))invite=(RoomInvitation)new DataContractJsonSerializer(typeof(RoomInvitation)).ReadObject(stream);
            }
            catch(SerializationException){throw new ArgumentException("Invalid room invitation payload.");}
            catch(FormatException){throw new ArgumentException("Invalid room invitation encoding.");}
            catch(System.Xml.XmlException){throw new ArgumentException("Invalid room invitation payload.");}
            Validate(invite);return invite;
        }
        public static ResolvedRoomInvitation ResolveForJoin(RoomInvitation invite)
        {
            Validate(invite);
            if(invite.DirectoryUrl!=null)
            {
                LanRoomListing room=PublicRoomDirectory.Resolve(new Uri(invite.DirectoryUrl),invite.RoomId);
                if(room.Players>=room.Capacity)throw new InvalidOperationException("The room is full.");
                return new ResolvedRoomInvitation{RoomId=room.RoomId,RoomName=room.Name,Address=room.Address,Port=room.Port,
                    IsPrivate=false,CapacityKnown=true,Players=room.Players,Capacity=room.Capacity};
            }
            // A direct invitation cannot know the live roster until the host's
            // authenticated room handshake. Unknown counts are never fabricated.
            return new ResolvedRoomInvitation{RoomId=invite.RoomId,RoomName=invite.RoomName,Address=invite.Address,
                Port=invite.Port,IsPrivate=invite.IsPrivate,CapacityKnown=false};
        }
        public static RoomClient Join(RoomInvitation invite,string password,PlayerProfile profile)
        {
            ResolvedRoomInvitation target=ResolveForJoin(invite);
            RoomClient client=RoomClient.Join(target.Address,target.Port,password,profile);
            if(client.Snapshot.RoomId!=target.RoomId)
            {client.Leave();throw new InvalidOperationException("The host now serves a different room. Ask for an updated invitation.");}
            return client;
        }
        public static RoomClient JoinByName(Uri directory,string name,string selectedRoomId,string password,PlayerProfile profile)
        {
            name=PublicRoomDirectory.ValidateName(name);
            if(!string.IsNullOrEmpty(selectedRoomId))
            {
                LanRoomListing selected=PublicRoomDirectory.Resolve(directory,selectedRoomId,name);
                return Join(CreateDirectory(selected.RoomId,selected.Name,directory),password,profile);
            }
            RoomSearchPage page=PublicRoomDirectory.SearchPage(directory,name);
            if(page.TotalMatches==0||page.Rooms.Count==0)throw new InvalidOperationException("No public room matches that name.");
            if(page.TotalMatches!=1||page.Rooms.Count!=1)throw new AmbiguousRoomNameException(page);
            return Join(CreateDirectory(page.Rooms[0].RoomId,page.Rooms[0].Name,directory),password,profile);
        }
        public static void Validate(RoomInvitation invite)
        {
            if(invite==null||invite.Version!=1)throw new ArgumentException("Unsupported room invitation version.");
            PublicRoomDirectory.ValidateRoomId(invite.RoomId);
            invite.RoomName=PublicRoomDirectory.ValidateName(invite.RoomName);
            if(!string.IsNullOrEmpty(invite.DirectoryUrl))
            {
                Uri directory;
                if(invite.IsPrivate||invite.Address!=null||invite.Port!=0||!Uri.TryCreate(invite.DirectoryUrl,UriKind.Absolute,out directory))
                    throw new ArgumentException("A public directory invitation cannot contain a direct endpoint or private room.");
                PublicRoomDirectory.Validate(directory);invite.DirectoryUrl=directory.AbsoluteUri;
            }
            else
            {
                invite.DirectoryUrl=null;
                if(invite.Port<1||invite.Port>65535)throw new ArgumentException("Invalid room invitation port.");
                invite.Address=ValidateAddress(invite.Address);
            }
        }
        private static string ValidateAddress(string address)
        {
            string value=(address??"").Trim();
            if(value.Length<1||value.Length>253)throw new ArgumentException("Invalid room invitation host address.");
            IPAddress ip;
            if(IPAddress.TryParse(value,out ip))
            {
                byte[] bytes=ip.GetAddressBytes();
                if(ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any)||ip.Equals(IPAddress.Broadcast)||
                    (bytes.Length==4&&bytes[0]>=224))throw new ArgumentException("The invitation must name a unicast host address.");
                return ip.ToString();
            }
            if(!Regex.IsMatch(value,"^(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)*[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$"))
                throw new ArgumentException("Invalid room invitation host address.");
            return value.ToLowerInvariant();
        }
    }

    [DataContract]
    public sealed class LocalFriendContact
    {
        [DataMember] public string Id;
        [DataMember] public string Nickname;
        internal LocalFriendContact Copy(){return (LocalFriendContact)MemberwiseClone();}
    }
    [DataContract]
    internal sealed class FriendBookDocument
    { [DataMember] public List<LocalFriendContact> Contacts=new List<LocalFriendContact>(); }

    // A local contact list, not an online account, presence or message service.
    // Contact IDs identify entries in this book, not authenticated remote users.
    public sealed class LocalFriendAddressBook
    {
        private readonly string path;
        private readonly object gate=new object();
        private readonly FriendBookDocument data;
        public LocalFriendAddressBook(string path)
        {
            if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("A local address-book path is required.");
            this.path=Path.GetFullPath(path);
            if(!File.Exists(this.path)){data=new FriendBookDocument();return;}
            if(new FileInfo(this.path).Length>262144)throw new InvalidDataException("The local address book exceeds its size limit.");
            using(var input=File.OpenRead(this.path))data=(FriendBookDocument)new DataContractJsonSerializer(typeof(FriendBookDocument)).ReadObject(input);
            if(data==null||data.Contacts==null||data.Contacts.Count>500)throw new InvalidDataException("Invalid local address book.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(LocalFriendContact contact in data.Contacts)
            {
                Guid parsed;
                if(contact==null||!Guid.TryParseExact(contact.Id,"N",out parsed)||!ids.Add(contact.Id))throw new InvalidDataException("Invalid or duplicate local contact identity.");
                contact.Nickname=RoomProtocol.Clean(contact.Nickname,24,false,"Friend nickname");
            }
        }
        public List<LocalFriendContact> Contacts
        { get{lock(gate){var copy=new List<LocalFriendContact>();foreach(LocalFriendContact contact in data.Contacts)copy.Add(contact.Copy());return copy;}} }
        public LocalFriendContact Add(string nickname)
        {
            nickname=RoomProtocol.Clean(nickname,24,false,"Friend nickname");
            lock(gate)
            {
                if(data.Contacts.Count>=500)throw new InvalidOperationException("The local address book is full.");
                var contact=new LocalFriendContact{Id=Guid.NewGuid().ToString("N"),Nickname=nickname};data.Contacts.Add(contact);
                try{Save();}catch{data.Contacts.Remove(contact);throw;}return contact.Copy();
            }
        }
        public void Remove(string contactId)
        {
            lock(gate)
            {
                LocalFriendContact contact=RequireContact(contactId);int at=data.Contacts.IndexOf(contact);data.Contacts.RemoveAt(at);
                try{Save();}catch{data.Contacts.Insert(at,contact);throw;}
            }
        }
        public string PrepareInvitation(string contactId,RoomInvitation invite)
        {
            lock(gate){RequireContact(contactId);return RoomInvites.Encode(invite);}
            // The caller may copy/share the returned reference. No invitation is
            // transmitted and no online delivery/presence status is invented.
        }
        public RoomInvitation ReadReceivedInvitation(string contactId,string reference)
        {lock(gate){RequireContact(contactId);return RoomInvites.Parse(reference);}}
        private LocalFriendContact RequireContact(string id)
        {
            foreach(LocalFriendContact contact in data.Contacts)if(contact.Id==id)return contact;
            throw new InvalidOperationException("Select a friend stored in the local address book first.");
        }
        private void Save()
        {
            string directory=Path.GetDirectoryName(path);Directory.CreateDirectory(directory);
            string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                using(var output=File.Create(temporary))new DataContractJsonSerializer(typeof(FriendBookDocument)).WriteObject(output,data);
                if(new FileInfo(temporary).Length>262144)throw new InvalidDataException("The local address book exceeds its size limit.");
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
