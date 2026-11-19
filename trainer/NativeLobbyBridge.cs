using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using OfflineLab.Multiplayer;

// Network and file work stays outside the game's UI thread. Bridge readiness
// means an implemented native operation, not merely a visible policy switch.
internal sealed class NativeLobbyBridge : IDisposable
{
    [DataContract]
    private sealed class Profile
    {
        [DataMember] public int Policy = 20;
        [DataMember] public int Grid;
        [DataMember] public int Laps;
        [DataMember] public string RoomName = "Asphalt 8 room";
        [DataMember] public string Nickname = "Driver";
        [DataMember] public string DirectoryUrl = "";
    }
    private readonly string path;
    private readonly Timer timer = new Timer { Interval = 200 };
    private readonly object gate = new object();
    private static readonly object credentialGate=new object();
    private Profile profile;
    private int processId;
    private uint handled;
    private bool busy, disposed;
    private int generation;
    private int socialView=3;
    private DateTime nextHeartbeat=DateTime.MinValue;
    private SocialClient social;
    private List<SocialProfile> friends=new List<SocialProfile>();
    private List<SocialFriendRequest> requests=new List<SocialFriendRequest>();
    private List<SocialInvitation> invitations=new List<SocialInvitation>();
    private List<LanRoomListing> rooms = new List<LanRoomListing>();
    private readonly Queue<Action> completions = new Queue<Action>();

    internal NativeLobbyBridge():this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"_offline_patch_backup","multiplayer"),true){}
    internal NativeLobbyBridge(string storageRoot,bool startTimer)
    {
        path=Path.Combine(storageRoot,"native-profile.json");
        profile=Load();timer.Tick+=(_,__)=>Poll();if(startTimer)timer.Start();
    }
    private Profile Load()
    {
        try
        {
            if(!File.Exists(path))return new Profile();
            if(new FileInfo(path).Length>16384)throw new InvalidDataException("Native lobby profile is too large.");
            using(var input=File.OpenRead(path))
            {
                var value=(Profile)new DataContractJsonSerializer(typeof(Profile)).ReadObject(input);
                if(value==null || value.Grid<0 || value.Grid>60 || value.Laps<0 || value.Laps>60 || value.Policy<0 || value.Policy>63)throw new InvalidDataException("Invalid native lobby settings.");
                new PlayerProfile{Nickname=value.Nickname}.Validate();
                if(string.IsNullOrWhiteSpace(value.RoomName)||Encoding.UTF8.GetByteCount(value.RoomName)>63)throw new InvalidDataException("Invalid room name.");
                if(!string.IsNullOrWhiteSpace(value.DirectoryUrl))PublicRoomDirectory.Validate(new Uri(value.DirectoryUrl,UriKind.Absolute));
                return value;
            }
        }
        catch(IOException){return new Profile();}
        catch(SerializationException){return new Profile();}
        catch(ArgumentException){return new Profile();}
        catch(FormatException){return new Profile();}
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+".tmp";
        using(var output=File.Create(temporary))new DataContractJsonSerializer(typeof(Profile)).WriteObject(output,profile);
        if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    }
    private static string Read(byte[] state,int offset,int length)
    {
        int end=offset;while(end<offset+length && state[end]!=0)end++;
        return new UTF8Encoding(false,true).GetString(state,offset,end-offset);
    }
    private void Text(int offset,int capacity,string value)
    {
        byte[] data=new byte[capacity];byte[] text=Encoding.UTF8.GetBytes(value??"");
        int length=Math.Min(text.Length,capacity-1);
        while(length>0 && length<text.Length && (text[length]&0xc0)==0x80)length--;
        Buffer.BlockCopy(text,0,data,0,length);NativeInjectionEngine.WriteNativeLobbyState(processId,offset,data);
    }
    private void Word(int offset,int value){NativeInjectionEngine.WriteNativeLobbyState(processId,offset,BitConverter.GetBytes(value));}
    private void Status(string message){Text(52,160,message);}
    private void Poll()
    {
        if(disposed)return;
        int pid;byte[] state;
        if(!NativeInjectionEngine.ReadNativeLobbyState(out pid,out state))return;
        if(pid!=processId)
        {
            processId=pid;handled=0;rooms.Clear();generation++;busy=false;
            Word(32,profile.Policy);Word(36,profile.Grid);Word(40,profile.Laps);
            Text(212,64,profile.RoomName);Text(276,32,profile.Nickname);
            Text(436,1024,profile.DirectoryUrl);
            Word(44,8); // Nickname uses the existing verified native name command.
            Status("Native AI fill is linked. Other room rules are still pending.");
            nextHeartbeat=DateTime.MinValue;
            return;
        }
        lock(gate)while(completions.Count>0)completions.Dequeue()();
        uint sequence=BitConverter.ToUInt32(state,12);
        if(busy)return;
        if(sequence==handled)
        {
            if(!string.IsNullOrWhiteSpace(profile.DirectoryUrl)&&DateTime.UtcNow>=nextHeartbeat)
            {try{Social(0,null);}catch(Exception failure){nextHeartbeat=DateTime.UtcNow.AddSeconds(15);Status(RoomErrors.Display(failure));}}
            return;
        }
        int request=BitConverter.ToInt32(state,20);
        try
        {
            if(request==1)
            {
                int bits=BitConverter.ToInt32(state,32),grid=BitConverter.ToInt32(state,36),laps=BitConverter.ToInt32(state,40);
                if(bits<0||bits>63||grid<0||grid>60||laps<0||laps>60)throw new ArgumentException("Invalid room options.");
                profile.Policy=bits;profile.Grid=grid;profile.Laps=laps;Save();
                Status("Saved. AI fill applies in host lobbies; other room rules are pending.");
            }
            else if(request==2)Search(Read(state,308,64),pid);
            else if(request==3||request==17||request==14)Social(request,null);
            else if(request==4)
            {
                string name=Read(state,276,32);new PlayerProfile{Nickname=name}.Validate();
                string error;if(!NativeInjectionEngine.QueueDriverName(name,out error))throw new InvalidOperationException(error);
                profile.Nickname=name;Save();generation++;busy=false;nextHeartbeat=DateTime.MinValue;Status("Nickname change queued for the native profile.");
            }
            else if(request==5)
            {
                string name=Read(state,212,64).Trim();
                if(name.Length==0 || name.IndexOfAny(new[]{'\r','\n','\t'})>=0)throw new ArgumentException("Enter a valid room name.");
                profile.RoomName=name;Save();Status("Room name saved for the next native room.");
            }
            else if(request==6)Status("Password kept for this session. Native private-room authentication is pending.");
            else if(request==7)Status("Native race connection for directory rooms is not connected yet.");
            else if(request==8)Social(8,Read(state,436,1024));
            else if(request==9)
            {
                string value=Clipboard.GetText(TextDataFormat.UnicodeText);
                if(Encoding.UTF8.GetByteCount(value)>1023)throw new ArgumentException("Clipboard text exceeds 1023 UTF-8 bytes.");
                // Keyboard-only scratch reuses hidden room rows, so pasting a
                // password cannot overwrite the saved directory edit buffer.
                Word(1460,0);Text(1480,1024,value);Word(48,200);Status("Clipboard text pasted. Confirm to use it.");
            }
            else if(request==10)
            {
                string value=Read(state,436,1024).Trim();
                if(value.Length!=0){Uri uri=new Uri(value,UriKind.Absolute);PublicRoomDirectory.Validate(uri);value=uri.AbsoluteUri;}
                profile.DirectoryUrl=value;Save();generation++;busy=false;social=null;ClearSocial();nextHeartbeat=DateTime.MinValue;
                Text(436,1024,value);
                Status(value.Length==0?"Directory cleared. Online social service disconnected.":"Directory saved. Connecting to the social service...");
            }
            else if(request==11)Social(request,Read(state,308,64));
            else if(request==12||request==13||request==15||request==16)Social(request,SelectedId(state));
            else if(request==18)
            {
                if(social==null)throw new InvalidOperationException("Connect to the social service first.");
                Clipboard.SetText(social.PlayerId,TextDataFormat.UnicodeText);Status("Your player ID was copied to the clipboard.");
            }
        }
        catch(Exception error){Status(RoomErrors.Display(error));}
        finally{handled=sequence;Word(16,unchecked((int)sequence));}
    }
    private void Search(string query,int pid)
    {
        if(string.IsNullOrWhiteSpace(query))throw new ArgumentException("Enter the room name first.");
        if(string.IsNullOrWhiteSpace(profile.DirectoryUrl))
        {Word(1460,0);Status("Public directory is not configured. Use Join Room for local games.");return;}
        Uri directory=new Uri(profile.DirectoryUrl,UriKind.Absolute);PublicRoomDirectory.Validate(directory);
        busy=true;int expected=generation;Status("Searching for matching rooms...");
        Task.Run(()=>
        {
            List<LanRoomListing> found=null;string error=null;
            try{found=PublicRoomDirectory.Search(directory,query,8,0,false);}catch(Exception failure){error=RoomErrors.Display(failure);}
            lock(gate)completions.Enqueue(()=>
            {
                if(disposed||processId!=pid||generation!=expected)return;busy=false;
                if(error!=null){Word(1460,0);Status(error);return;}
                rooms=found;
                for(int i=0;i<rooms.Count;i++)
                {
                    Text(1480+i*144,80,rooms[i].Name+"  "+rooms[i].Players+"/"+rooms[i].Capacity);
                    Text(1560+i*144,64,rooms[i].RoomId);
                }
                Word(1464,0);Word(1460,rooms.Count);
                Status(rooms.Count+" matching room(s). Native joining is not connected yet.");
            });
        });
    }
    private string SelectedId(byte[] state)
    {
        int index=BitConverter.ToInt32(state,1472),count=BitConverter.ToInt32(state,1468);
        if(index<0||index>=count||index>=8)throw new ArgumentException("Select an entry first.");
        return Read(state,2632+index*144+80,64);
    }
    private void ClearSocial(){friends.Clear();requests.Clear();invitations.Clear();Word(1468,0);Word(44,8);}
    private SocialClient OpenSocial(Uri directory,string nickname)
    {
        lock(credentialGate)return LoadSocialIdentity(directory,nickname);
    }
    private SocialClient LoadSocialIdentity(Uri directory,string nickname)
    {
        string key;
        using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.AbsoluteUri))).Replace("-","");
        string file=Path.Combine(Path.GetDirectoryName(path),"identity-"+key+".private.bin");
        SocialIdentity identity;
        if(File.Exists(file))
        {
            if(new FileInfo(file).Length>32768)throw new InvalidDataException("Private social identity is too large.");
            byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(file),Encoding.UTF8.GetBytes("Asphalt8OfflineSocial-v1"),DataProtectionScope.CurrentUser);
            try{using(var input=new MemoryStream(plain))identity=(SocialIdentity)new DataContractJsonSerializer(typeof(SocialIdentity)).ReadObject(input);}
            finally{Array.Clear(plain,0,plain.Length);}
        }
        else
        {
            identity=SocialClient.Register(directory,nickname);
            byte[] plain;
            using(var output=new MemoryStream()){new DataContractJsonSerializer(typeof(SocialIdentity)).WriteObject(output,identity);plain=output.ToArray();}
            byte[] encrypted;
            try{encrypted=ProtectedData.Protect(plain,Encoding.UTF8.GetBytes("Asphalt8OfflineSocial-v1"),DataProtectionScope.CurrentUser);}
            finally{Array.Clear(plain,0,plain.Length);}
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(encrypted,0,encrypted.Length);
        }
        return new SocialClient(directory,identity);
    }
    private void Social(int operation,string argument)
    {
        if(string.IsNullOrWhiteSpace(profile.DirectoryUrl))throw new ArgumentException("Configure a directory URL in Connection first.");
        Uri directory=new Uri(profile.DirectoryUrl,UriKind.Absolute);PublicRoomDirectory.Validate(directory);
        int expected=generation,pid=processId;string nickname=profile.Nickname;
        SocialClient existing=social;int view=socialView;
        if(operation==3||operation==14||operation==17)view=operation;
        if((operation==12||operation==13)&&socialView!=17)throw new ArgumentException("Select an incoming friend request in Requests first.");
        if((operation==15||operation==16)&&socialView!=14)throw new ArgumentException("Select an invitation in Invitations first.");
        if(operation==8)
        {
            byte[] state;int current;
            if(!NativeInjectionEngine.ReadNativeLobbyState(out current,out state)||current!=pid)throw new IOException("The native lobby is unavailable.");
            string id=SelectedId(state);
            if(socialView!=3||!friends.Exists(x=>x.PlayerId==id))throw new ArgumentException("Select an accepted friend first.");
            RoomInvitation reference;
            try{reference=RoomInvites.Parse(argument);}catch(Exception){throw new ArgumentException("Native room invitation reference is not available yet.");}
            if(!reference.IsPrivate)throw new ArgumentException("Public native invitation ownership proof is not connected yet.");
            argument=id+"\n"+RoomInvites.Encode(reference);
        }
        SocialInvitation selected=operation==15?invitations.Find(x=>x.InvitationId==argument):null;
        if(operation==15&&selected==null)throw new ArgumentException("Refresh and select an invitation first.");
        busy=true;nextHeartbeat=DateTime.UtcNow.AddSeconds(15);
        if(operation!=0)Status("Contacting the social service...");
        Task.Run(()=>
        {
            SocialClient client=null;List<SocialProfile> found=null;List<SocialFriendRequest> pending=null;List<SocialInvitation> inbox=null;
            string message=null,error=null;
            try
            {
                client=existing??OpenSocial(directory,nickname);
                client.UpdatePresence(nickname,"In game");
                if(operation==11){client.RequestFriend(argument);message="Friend request sent. Acceptance is required.";}
                if(operation==12){client.Accept(argument);message="Friend request accepted.";}
                if(operation==13){client.Reject(argument);message="Friend request rejected.";}
                if(operation==16){client.Dismiss(argument);message="Invitation dismissed.";}
                if(operation==8){int split=argument.IndexOf('\n');client.SendInvitation(argument.Substring(0,split),RoomInvites.Parse(argument.Substring(split+1)));message="Invitation delivered. Recipient must explicitly join.";}
                if(operation==15)
                {
                    ResolvedRoomInvitation resolved=RoomInvites.ResolveForJoin(RoomInvites.Parse(selected.Reference));
                    message="Native race adapter pending; not joined. Resolved "+resolved.RoomName+" at "+resolved.Address+":"+resolved.Port+".";
                }
                if(view==3)found=client.GetFriends();
                if(view==17){pending=client.GetRequests();pending.RemoveAll(x=>x.Recipient==null||x.Recipient.PlayerId!=client.PlayerId);}
                if(view==14)inbox=client.GetInvitations();
                if(message==null)message="Social online. Your player ID: "+client.PlayerId;
            }
            catch(Exception failure){error=RoomErrors.Display(failure);}
            lock(gate)
            {
                if(disposed)return;
                completions.Enqueue(()=>
                {
                    if(disposed||processId!=pid||generation!=expected)return;
                    busy=false;
                    if(error!=null){Word(44,8);Status(error);return;}
                    social=client;socialView=view;Word(44,12);
                    if(found!=null)friends=found;if(pending!=null)requests=pending;if(inbox!=null)invitations=inbox;
                    PublishSocial(operation!=0);if(operation!=0||existing==null)Status(message);
                });
            }
        });
    }
    private void PublishSocial(bool resetSelection)
    {
        int previous=0,current;byte[] state;
        if(!resetSelection&&NativeInjectionEngine.ReadNativeLobbyState(out current,out state)&&current==processId)previous=BitConverter.ToInt32(state,1472);
        Word(1468,0);int count=0;
        if(socialView==3)foreach(var friend in friends){if(count==8)break;SocialRow(count++, (friend.Online?"Online: ":"Offline: ")+friend.Nickname,friend.PlayerId);}
        if(socialView==17)foreach(var request in requests){if(count==8)break;SocialRow(count++,"Request: "+request.Sender.Nickname,request.RequestId);}
        if(socialView==14)foreach(var invite in invitations){if(count==8)break;SocialRow(count++,"Invite: "+invite.Sender.Nickname,invite.InvitationId);}
        Word(1472,previous>=0&&previous<count?previous:0);Word(1468,count);
    }
    private void SocialRow(int index,string label,string id){Text(2632+index*144,80,label);Text(2712+index*144,64,id);}
    public void Dispose(){lock(gate){disposed=true;generation++;completions.Clear();}timer.Stop();timer.Dispose();}
}
