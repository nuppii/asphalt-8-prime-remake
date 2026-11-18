using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace OfflineLab.Multiplayer
{
    [DataContract] public sealed class SocialProfile
    {
        [DataMember(Name="playerId")] public string PlayerId;
        [DataMember(Name="nickname")] public string Nickname;
        [DataMember(Name="status")] public string Status;
        [DataMember(Name="online")] public bool Online;
        [DataMember(Name="lastSeenUtc")] public double LastSeenUtc;
    }
    // Keep the token private. Nicknames do not establish identity.
    [DataContract] public sealed class SocialIdentity
    {
        [DataMember(Name="playerId")] public string PlayerId;
        [DataMember(Name="authToken")] public string AuthToken;
        [DataMember(Name="profile")] public SocialProfile Profile;
    }
    [DataContract] public sealed class SocialFriendRequest
    {
        [DataMember(Name="requestId")] public string RequestId;
        [DataMember(Name="sender")] public SocialProfile Sender;
        [DataMember(Name="recipient")] public SocialProfile Recipient;
        [DataMember(Name="state")] public string State;
        [DataMember(Name="createdUtc")] public double CreatedUtc;
        [DataMember(Name="expiresUtc")] public double ExpiresUtc;
    }
    [DataContract] public sealed class SocialInvitation
    {
        [DataMember(Name="invitationId")] public string InvitationId;
        [DataMember(Name="sender")] public SocialProfile Sender;
        [DataMember(Name="reference")] public string Reference;
        [DataMember(Name="createdUtc")] public double CreatedUtc;
        [DataMember(Name="expiresUtc")] public double ExpiresUtc;
    }
    [DataContract] public sealed class SocialAction
    {
        [DataMember(Name="state")] public string State;
        [DataMember(Name="invitationId")] public string InvitationId;
        [DataMember(Name="expiresUtc")] public double ExpiresUtc;
    }
    public sealed class SocialServiceException : IOException
    {
        public int StatusCode { get; private set; }
        internal SocialServiceException(int status,string message):base(message){StatusCode=status;}
    }
    [DataContract] internal sealed class SocialBody
    {
        [DataMember(Name="nickname",EmitDefaultValue=false)] public string Nickname;
        [DataMember(Name="status",EmitDefaultValue=false)] public string Status;
        [DataMember(Name="recipientId",EmitDefaultValue=false)] public string RecipientId;
        [DataMember(Name="reference",EmitDefaultValue=false)] public string Reference;
        [DataMember(Name="roomLeaseToken",EmitDefaultValue=false)] public string RoomLeaseToken;
    }
    [DataContract] internal sealed class SocialList
    {
        [DataMember(Name="friends")] public List<SocialProfile> Friends {get;set;}
        [DataMember(Name="requests")] public List<SocialFriendRequest> Requests {get;set;}
        [DataMember(Name="invitations")] public List<SocialInvitation> Invitations {get;set;}
        [DataMember(Name="error")] public string Error {get;set;}
    }
    public sealed class SocialClient
    {
        private readonly Uri directory;
        private readonly string playerId,token;
        public string PlayerId {get{return playerId;}}
        public Uri DirectoryUrl {get{return directory;}}
        public SocialClient(Uri directory,SocialIdentity identity)
        {
            PublicRoomDirectory.Validate(directory);
            if(identity==null)throw new ArgumentNullException("identity");
            ValidateId(identity.PlayerId,"p_");
            if(identity.AuthToken==null||!Regex.IsMatch(identity.AuthToken,@"\A[A-Za-z0-9_-]{32,128}\z"))
                throw new ArgumentException("Invalid social identity credentials.");
            this.directory=directory;playerId=identity.PlayerId;token=identity.AuthToken;
        }
        public static SocialIdentity Register(Uri directory,string nickname)
        {return (SocialIdentity)Request(directory,null,null,"identities",new SocialBody{Nickname=nickname},typeof(SocialIdentity));}
        public SocialProfile GetMe(){return Call<SocialProfile>("me",null);}
        public SocialProfile Heartbeat(){return Call<SocialProfile>("me",new SocialBody());}
        public SocialProfile UpdatePresence(string nickname,string status)
        {return Call<SocialProfile>("me",new SocialBody{Nickname=nickname,Status=status});}
        public List<SocialProfile> GetFriends(){return Call<SocialList>("friends",null).Friends??new List<SocialProfile>();}
        public SocialFriendRequest RequestFriend(string recipientId)
        {ValidateId(recipientId,"p_");return Call<SocialFriendRequest>("friend-requests",new SocialBody{RecipientId=recipientId});}
        public List<SocialFriendRequest> GetRequests(){return Call<SocialList>("friend-requests",null).Requests??new List<SocialFriendRequest>();}
        public SocialAction Accept(string requestId){return Decide(requestId,"accept");}
        public SocialAction Reject(string requestId){return Decide(requestId,"reject");}
        private SocialAction Decide(string id,string action)
        {ValidateId(id,"q_");return Call<SocialAction>("friend-requests/"+id+"/"+action,new SocialBody());}
        public SocialAction SendInvitation(string recipientId,RoomInvitation invitation,string roomLeaseToken=null)
        {
            ValidateId(recipientId,"p_");
            return Call<SocialAction>("invitations",new SocialBody{RecipientId=recipientId,Reference=RoomInvites.Encode(invitation),RoomLeaseToken=roomLeaseToken});
        }
        public List<SocialInvitation> GetInvitations(){return Call<SocialList>("invitations",null).Invitations??new List<SocialInvitation>();}
        public SocialAction Dismiss(string id)
        {ValidateId(id,"i_");return Call<SocialAction>("invitations/"+id+"/dismiss",new SocialBody());}
        private T Call<T>(string path,SocialBody body){return (T)Request(directory,playerId,token,path,body,typeof(T));}
        private static void ValidateId(string id,string prefix)
        {if(id==null||!Regex.IsMatch(id,@"\A"+prefix+@"[A-Za-z0-9_-]{20,48}\z"))throw new ArgumentException("Invalid social reference ID.");}
        private static object Request(Uri directory,string playerId,string token,string path,SocialBody body,Type responseType)
        {
            var request=(HttpWebRequest)WebRequest.Create(PublicRoomDirectory.Endpoint(directory,"v1/social/"+path));
            request.Method=body==null?"GET":"POST";request.AllowAutoRedirect=false;request.Proxy=null;
            request.Timeout=4000;request.ReadWriteTimeout=4000;request.Accept="application/json";
            if(playerId!=null){request.Headers["X-Player-ID"]=playerId;request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;}
            if(body!=null)
            {
                byte[] data;
                using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(SocialBody)).WriteObject(memory,body);data=memory.ToArray();}
                if(data.Length>8192)throw new ArgumentException("Social request is too large.");
                request.ContentType="application/json";request.ContentLength=data.Length;
                using(var stream=request.GetRequestStream())stream.Write(data,0,data.Length);
            }
            try
            {
                using(var response=(HttpWebResponse)request.GetResponse())
                {
                    if((int)response.StatusCode<200||(int)response.StatusCode>=300)throw new SocialServiceException((int)response.StatusCode,"The social service refused the request.");
                    using(var data=Read(response))return new DataContractJsonSerializer(responseType).ReadObject(data);
                }
            }
            catch(WebException ex)
            {
                var response=ex.Response as HttpWebResponse;
                if(response==null)throw new IOException("The social service could not be reached.",ex);
                using(response)
                {
                    string message="The social service refused the request.";
                    try{using(var data=Read(response)){var failure=(SocialList)new DataContractJsonSerializer(typeof(SocialList)).ReadObject(data);if(!String.IsNullOrEmpty(failure.Error)&&failure.Error.Length<=256)message=failure.Error;}}
                    catch(SerializationException){}catch(IOException){}
                    throw new SocialServiceException((int)response.StatusCode,message);
                }
            }
        }
        private static MemoryStream Read(HttpWebResponse response)
        {
            var memory=new MemoryStream();
            try
            {
                using(var stream=response.GetResponseStream())
                {byte[] buffer=new byte[4096];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(memory.Length+count>1048576)throw new InvalidDataException("The social response is too large.");memory.Write(buffer,0,count);}}
                memory.Position=0;return memory;
            }
            catch{memory.Dispose();throw;}
        }
    }
}
