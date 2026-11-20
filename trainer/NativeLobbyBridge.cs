using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
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
    private readonly bool internetHosting;
    private readonly Timer timer = new Timer { Interval = 200 };
    private readonly NativeRaceScoreBridge raceScores = new NativeRaceScoreBridge();
    private readonly object gate = new object();
    private static readonly object credentialGate=new object();
    private Profile profile;
    private int processId;
    private uint handled;
    private bool busy, disposed;
    private bool networkAvailable;
    private PublicRoomAdvertiser nativeAdvertiser;
    private volatile LanRoomListing nativeListing;
    private sealed class ListingSource { internal volatile LanRoomListing Value; }
    private ListingSource listingSource;
    private string advertisedNativeId,advertisedDirectory;
    private int generation;
    private int socialView=3;
    private DateTime nextHeartbeat=DateTime.MinValue;
    private SocialClient social;
    private List<SocialProfile> friends=new List<SocialProfile>();
    private List<SocialFriendRequest> requests=new List<SocialFriendRequest>();
    private List<SocialInvitation> invitations=new List<SocialInvitation>();
    private List<LanRoomListing> rooms = new List<LanRoomListing>();
    private const int RoomPageSize=4;
    private int roomOffset;
    private int? nextRoomOffset;
    private string roomQuery;
    private readonly Queue<Action> completions = new Queue<Action>();

    internal NativeLobbyBridge(bool internetHosting):this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"_offline_patch_backup","multiplayer"),true,internetHosting){}
    internal NativeLobbyBridge(string storageRoot,bool startTimer,bool internetHosting=false)
    {
        this.internetHosting=internetHosting;
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
    private void Word(int offset,int value)
    {
        if(offset==44)
        {
            value&=~(64|128);
            if(NativeInjectionEngine.NativeDirectDiscoveryReady(processId))value|=32;
            if(networkAvailable)value|=2;
            if(roomOffset>0)value|=64;
            if(nextRoomOffset.HasValue)value|=128;
        }
        NativeInjectionEngine.WriteNativeLobbyState(processId,offset,BitConverter.GetBytes(value));
    }
    private void Status(string message){Text(52,160,message);}
    private void SearchStatus(int code,string message){Word(48,code);Status(message);}
    private void ClearRooms()
    {
        rooms=new List<LanRoomListing>();roomOffset=0;nextRoomOffset=null;roomQuery=null;
        Word(1460,0);Word(1464,-1);Word(1476,0);Word(NativeNetworkState.JoinableMask,0);PublishRoomNavigation();
    }
    private void PublishRoomNavigation()
    {
        int pid;byte[] state;
        if(NativeInjectionEngine.ReadNativeLobbyState(out pid,out state)&&pid==processId)
            Word(44,BitConverter.ToInt32(state,44));
    }
    private bool RoomSearchVisible(){int pid;byte[] state;return NativeInjectionEngine.ReadNativeLobbyState(out pid,out state)&&pid==processId&&BitConverter.ToInt32(state,28)==2;}
    private void Poll()
    {
        if(disposed)return;
        int pid;byte[] state;
        if(!NativeInjectionEngine.ReadNativeLobbyState(out pid,out state)){raceScores.Dispose();return;}
        raceScores.Poll(pid,state);
        bool ready=NativeInjectionEngine.NativeDirectDiscoveryReady(pid)&&NativeNetworkState.Valid(state);
        if(pid!=processId)
        {
            StopNativeAdvertisement();networkAvailable=ready;
            processId=pid;handled=0;generation++;busy=false;ClearRooms();
            Word(32,profile.Policy);Word(36,profile.Grid);Word(40,profile.Laps);
            Text(212,64,profile.RoomName);Text(276,32,profile.Nickname);
            Text(436,1024,profile.DirectoryUrl);
            Word(44,8); // Nickname uses the existing verified native name command.
            Status("Native AI fill is linked. Other room rules are still pending.");
            nextHeartbeat=DateTime.MinValue;
            return;
        }
        if(ready!=networkAvailable){networkAvailable=ready;Word(44,BitConverter.ToInt32(state,44)&~2);}
        UpdateNativeAdvertisement(state);
        lock(gate)while(completions.Count>0)completions.Dequeue()();
        int refreshedPid;if(!NativeInjectionEngine.ReadNativeLobbyState(out refreshedPid,out state)||refreshedPid!=pid)return;
        uint sequence=BitConverter.ToUInt32(state,12);
        if(busy)return;
        if(sequence==handled)
        {
            if(!string.IsNullOrWhiteSpace(profile.DirectoryUrl)&&DateTime.UtcNow>=nextHeartbeat)
            {try{Social(0,null);}catch(Exception failure){nextHeartbeat=DateTime.UtcNow.AddSeconds(15);if(!RoomSearchVisible())Status(RoomErrors.Display(failure));}}
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
            else if(request==2)Search(Read(state,308,64),pid,0);
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
            else if(request==6||request==21)SetPassword(state,request==6);
            else if(request==7)JoinSelected(state,pid);
            else if(request==8)Social(8,Read(state,436,1024));
            else if(request==9)
            {
                string value=Clipboard.GetText(TextDataFormat.UnicodeText);
                if(Encoding.UTF8.GetByteCount(value)>1023)throw new ArgumentException("Clipboard text exceeds 1023 UTF-8 bytes.");
                // Keyboard-only scratch reuses hidden room rows, so pasting a
                // password cannot overwrite the saved directory edit buffer.
                ClearRooms();Text(1480,1024,value);Word(48,200);Status("Clipboard text pasted. Confirm to use it.");
            }
            else if(request==10)
            {
                string value=Read(state,436,1024).Trim();
                if(value.Length!=0){Uri uri=new Uri(value,UriKind.Absolute);PublicRoomDirectory.Validate(uri);value=uri.AbsoluteUri;}
                StopNativeAdvertisement();profile.DirectoryUrl=value;Save();generation++;busy=false;social=null;ClearSocial();ClearRooms();Word(48,0);nextHeartbeat=DateTime.MinValue;
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
            else if(request==19||request==20)
            {
                string query=Read(state,308,64);
                if(query!=roomQuery)throw new InvalidOperationException("Search again to list rooms for the new name.");
                if(request==19&&roomOffset>0)Search(query,pid,Math.Max(0,roomOffset-RoomPageSize));
                if(request==20&&nextRoomOffset.HasValue)Search(query,pid,nextRoomOffset.Value);
            }
        }
        catch(Exception error){Status(RoomErrors.Display(error));}
        finally{handled=sequence;Word(16,unchecked((int)sequence));}
    }
    private void SetPassword(byte[] state,bool host)
    {
        string password=Read(state,372,64);
        Text(372,64,"");
        if(BitConverter.ToInt32(state,NativeNetworkState.PasswordEnabled)!=1)
            throw new InvalidOperationException("Native room password support is unavailable.");
        int ready=host?NativeNetworkState.HostPasswordReady:NativeNetworkState.GuestPasswordReady;
        int destination=host?NativeNetworkState.HostPasswordKey:NativeNetworkState.GuestPasswordKey;
        byte[] key=new byte[32];
        Word(ready,0);
        if(password.Length!=0)
        {
            using(var kdf=new Rfc2898DeriveBytes(password,Encoding.ASCII.GetBytes("Asphalt480-NativePassword-v1"),100000))
                key=kdf.GetBytes(32);
        }
        try
        {
            if(!NativeInjectionEngine.WriteNativeLobbyState(processId,destination,key))
                throw new InvalidOperationException("Could not update the native password.");
            if(!host)
            {
                byte[] seed=new byte[16];
                using(var rng=RandomNumberGenerator.Create())rng.GetBytes(seed);
                if(!NativeInjectionEngine.WriteNativeLobbyState(processId,NativeNetworkState.PasswordNonceSeed,seed))
                    throw new InvalidOperationException("Could not initialize the join attempt.");
            }
            Word(ready,password.Length==0?0:1);
        }
        finally{Array.Clear(key,0,key.Length);}
        Status(password.Length==0?(host?"Host password cleared. Private rooms require a password.":"Join password cleared."):
            (host?"Host password set for this session. Enable Private Room to require it.":"Join password set. Select the locked room or use Join by IPv4 Address."));
    }
    private void Search(string query,int pid,int offset)
    {
        ClearRooms();
        if(string.IsNullOrWhiteSpace(profile.DirectoryUrl))
        {SearchStatus(303,"Public directory is not configured. Use Join Room for local games.");return;}
        Uri directory=new Uri(profile.DirectoryUrl,UriKind.Absolute);PublicRoomDirectory.Validate(directory);
        busy=true;int expected=generation;SearchStatus(300,"Searching for matching rooms...");
        Task.Run(()=>
        {
            RoomSearchPage found=null;string error=null;
            try
            {
                found=string.IsNullOrWhiteSpace(query)?PublicRoomDirectory.BrowsePage(directory,RoomPageSize,offset):
                    PublicRoomDirectory.SearchPage(directory,query,RoomPageSize,offset,true);
            }
            catch(Exception failure){error=RoomErrors.Display(failure);}
            lock(gate)completions.Enqueue(()=>
            {
                if(disposed||processId!=pid||generation!=expected)return;busy=false;
                int currentPid;byte[] currentState;
                if(!NativeInjectionEngine.ReadNativeLobbyState(out currentPid,out currentState)||currentPid!=pid)return;
                if(Read(currentState,308,64)!=query){ClearRooms();SearchStatus(306,"Search changed. Search again to refresh the list.");return;}
                if(error!=null){ClearRooms();SearchStatus(304,error);return;}
                rooms=found.Rooms;roomOffset=offset;nextRoomOffset=found.NextOffset;roomQuery=query;
                int joinable=0,protectedRooms=0;
                for(int i=0;i<rooms.Count;i++)
                {
                    if(NativeListingReady(rooms[i]))joinable|=1<<i;
                    if(rooms[i].PasswordProtected)protectedRooms|=1<<i;
                    Text(1480+i*144,80,RoomLabel(rooms[i],null));
                    Text(1560+i*144,64,rooms[i].RoomId);
                }
                Word(1476,protectedRooms);Word(NativeNetworkState.JoinableMask,joinable);Word(1464,-1);Word(1460,rooms.Count);
                PublishRoomNavigation();
                string status=rooms.Count==0?"No matching rooms found.":"Page "+(offset/RoomPageSize+1)+"  |  "+found.TotalMatches+" rooms  |  Host ping";
                SearchStatus(rooms.Count==0?302:301,status);
                MeasureRoomPings(directory,rooms,pid,expected,query);
            });
        });
    }
    private static string RoomLabel(LanRoomListing room,int? ping)
    {
        string suffix="  "+room.Players+"/"+room.Capacity+(room.Players>=room.Capacity?"  FULL":"")+
            (room.IsNative?"":"  TCP")+(room.PasswordProtected?"  [LOCKED]":"")+"  "+
            (ping.HasValue?ping.Value.ToString(CultureInfo.InvariantCulture)+" ms":"-- ms");
        byte[] name=Encoding.UTF8.GetBytes(room.Name);
        int length=Math.Min(name.Length,79-suffix.Length);
        while(length>0&&length<name.Length&&(name[length]&0xc0)==0x80)length--;
        return Encoding.UTF8.GetString(name,0,length)+suffix;
    }
    private static int? MeasureHostPing(string host,bool localDirectory)
    {
        IPAddress address;
        if(!IPAddress.TryParse(host,out address))return null;
        byte[] ip=address.GetAddressBytes();
        if(ip.Length!=4||ip[0]==0||ip[0]>=224||(!localDirectory&&
            (ip[0]==10||ip[0]==127||(ip[0]==169&&ip[1]==254)||(ip[0]==172&&ip[1]>=16&&ip[1]<32)||
             (ip[0]==192&&ip[1]==168)||(ip[0]==100&&ip[1]>=64&&ip[1]<128))))return null;
        try
        {
            using(var ping=new Ping())
            {
                PingReply reply=ping.Send(address,750);
                return reply.Status==IPStatus.Success?(int?)reply.RoundtripTime:null;
            }
        }
        catch(PingException){return null;}
        catch(System.Security.SecurityException){return null;}
    }
    private void MeasureRoomPings(Uri directory,List<LanRoomListing> page,int pid,int expected,string query)
    {
        var checkedHosts=new HashSet<string>();
        for(int i=0;i<page.Count;i++)
        {
            string host=page[i].Address;
            if(!checkedHosts.Add(host))continue;
            Task.Run(()=>
            {
                int? result=MeasureHostPing(host,directory.IsLoopback);
                if(!result.HasValue)return;
                lock(gate)
                {
                    if(disposed)return;
                    completions.Enqueue(()=>
                    {
                        int currentPid;byte[] state;
                        if(processId!=pid||generation!=expected||rooms!=page||
                            !NativeInjectionEngine.ReadNativeLobbyState(out currentPid,out state)||currentPid!=pid||
                            Read(state,308,64)!=query||BitConverter.ToInt32(state,1460)!=page.Count)return;
                        for(int row=0;row<page.Count;row++)
                            if(page[row].Address==host)Text(1480+row*144,80,RoomLabel(page[row],result));
                    });
                }
            });
        }
    }
    private static bool NativeListingReady(LanRoomListing room)
    {
        if(room==null||!room.IsNative||!LanRoomDiscovery.ValidTransport(room)||room.NativeRoomId=="0000000000000000"||room.IsPrivate||room.Players>=room.Capacity||room.Port<1||room.Port>65535||Encoding.UTF8.GetByteCount(room.Name??"")>63)return false;
        string[] parts=(room.Address??"").Split('.');if(parts.Length!=4)return false;
        for(int i=0;i<4;i++){int value;if(parts[i].Length<1||parts[i].Length>3||!int.TryParse(parts[i],NumberStyles.None,CultureInfo.InvariantCulture,out value)||value>255||(i==0&&(value==0||value>=224)))return false;}
        return true;
    }
    private void JoinSelected(byte[] state,int pid)
    {
        int index=BitConverter.ToInt32(state,1464),count=BitConverter.ToInt32(state,1460);
        if(!networkAvailable||!NativeNetworkState.Valid(state))throw new InvalidOperationException("Native directory connection adapter is unavailable.");
        int phase=BitConverter.ToInt32(state,NativeNetworkState.JoinPhase);
        if(phase>=1&&phase<=3)throw new InvalidOperationException("A native join is already in progress.");
        if(index<0||index>=count||index>=rooms.Count||index>=8||Read(state,1560+index*144,64)!=rooms[index].RoomId)throw new ArgumentException("Search again and select a current room.");
        LanRoomListing selected=rooms[index];
        if(!NativeListingReady(selected))throw new InvalidOperationException("Select a non-full native IPv4 room. TCP prototype rooms are incompatible.");
        if(selected.PasswordProtected&&(BitConverter.ToInt32(state,NativeNetworkState.PasswordEnabled)!=1||
            BitConverter.ToInt32(state,NativeNetworkState.GuestPasswordReady)!=1))
            throw new InvalidOperationException("Set the join password before joining this room.");
        Uri directory=new Uri(profile.DirectoryUrl,UriKind.Absolute);int expected=generation;string query=Read(state,308,64);busy=true;
        SearchStatus(307,"Checking the selected native room...");
        Task.Run(()=>{
            LanRoomListing fresh=null;string error=null;
            try{fresh=PublicRoomDirectory.Resolve(directory,selected.RoomId,selected.Name);if(!NativeListingReady(fresh)||fresh.NativeRoomId!=selected.NativeRoomId||fresh.PasswordProtected!=selected.PasswordProtected)throw new InvalidOperationException("The selected native room changed or is full. Search again.");}
            catch(Exception failure){error=RoomErrors.Display(failure);}
            lock(gate)completions.Enqueue(()=>{
                if(disposed||processId!=pid||generation!=expected)return;busy=false;
                int now;byte[] current;
                if(!NativeInjectionEngine.ReadNativeLobbyState(out now,out current)||now!=pid||!NativeNetworkState.Valid(current))return;
                if(Read(current,308,64)!=query||BitConverter.ToInt32(current,1464)!=index||Read(current,1560+index*144,64)!=selected.RoomId){SearchStatus(306,"Selection changed. Choose the room again.");return;}
                if(error!=null){Word(NativeNetworkState.JoinableMask,BitConverter.ToInt32(current,NativeNetworkState.JoinableMask)&~(1<<index));SearchStatus(308,error);return;}
                uint high=uint.Parse(fresh.NativeRoomId.Substring(0,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture),low=uint.Parse(fresh.NativeRoomId.Substring(8,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
                Text(NativeNetworkState.JoinAddress,16,fresh.Address);Text(NativeNetworkState.JoinName,64,fresh.Name);
                Word(NativeNetworkState.JoinUsePassword,fresh.PasswordProtected?1:0);
                Word(NativeNetworkState.JoinIdLow,unchecked((int)low));Word(NativeNetworkState.JoinIdHigh,unchecked((int)high));Word(NativeNetworkState.JoinLobbyPort,fresh.Port);Word(NativeNetworkState.JoinPhase,1);
                Word(NativeNetworkState.JoinSeq,unchecked(BitConverter.ToInt32(current,NativeNetworkState.JoinSeq)+1));
                SearchStatus(309,"Native join queued. Waiting for matching discovery and lobby confirmation.");
            });
        });
    }
    private void StopNativeAdvertisement()
    {
        nativeListing=null;if(listingSource!=null)listingSource.Value=null;listingSource=null;var previous=nativeAdvertiser;nativeAdvertiser=null;advertisedNativeId=null;advertisedDirectory=null;
        if(previous!=null)Task.Run(()=>previous.Dispose());
    }
    private void UpdateNativeAdvertisement(byte[] state)
    {
        if(!NativeNetworkState.Valid(state)||string.IsNullOrWhiteSpace(profile.DirectoryUrl))
        {if(nativeAdvertiser!=null)StopNativeAdvertisement();return;}
        uint revision=BitConverter.ToUInt32(state,NativeNetworkState.HostRevision);
        if((revision&1)!=0||revision!=BitConverter.ToUInt32(state,NativeNetworkState.HostEndRevision))return;
        bool protectedRoom=BitConverter.ToInt32(state,NativeNetworkState.HostPrivate)!=0;
        if(BitConverter.ToInt32(state,NativeNetworkState.HostAlive)!=1||
            (protectedRoom&&(BitConverter.ToInt32(state,NativeNetworkState.PasswordEnabled)!=1||
            BitConverter.ToInt32(state,NativeNetworkState.HostPasswordReady)!=1)))
        {if(nativeAdvertiser!=null)StopNativeAdvertisement();return;}
        int capacity=BitConverter.ToInt32(state,NativeNetworkState.HostCapacity),players=BitConverter.ToInt32(state,NativeNetworkState.HostPlayers),port=BitConverter.ToInt32(state,NativeNetworkState.HostLobbyPort);
        if(internetHosting&&port!=NativeNetworkState.InternetLobbyPort)
        {
            if(nativeAdvertiser!=null)StopNativeAdvertisement();
            return;
        }
        string nativeId=BitConverter.ToUInt32(state,NativeNetworkState.HostIdHigh).ToString("x8")+BitConverter.ToUInt32(state,NativeNetworkState.HostIdLow).ToString("x8"),name=Read(state,NativeNetworkState.HostName,64);
        if(capacity<1||capacity>12||players<1||players>capacity||port<1||port>65535||name.Length==0||BitConverter.ToInt32(state,NativeNetworkState.HostDiscoveryPort)!=6529)return;
        bool changed=nativeAdvertiser==null||advertisedNativeId!=nativeId||advertisedDirectory!=profile.DirectoryUrl||
            nativeListing.PasswordProtected!=protectedRoom;
        if(changed)StopNativeAdvertisement();
        nativeListing=new LanRoomListing{RoomId=changed?Guid.NewGuid().ToString("N"):nativeListing.RoomId,Name=name,Address="127.0.0.1",
            Port=port,Players=players,Capacity=capacity,Transport=RoomTransports.Asphalt480Udp,NativeRoomId=nativeId,DiscoveryPort=6529,
            PasswordProtected=protectedRoom,NativePasswordVersion=protectedRoom?1:0};
        if(changed){advertisedNativeId=nativeId;advertisedDirectory=profile.DirectoryUrl;var source=new ListingSource{Value=nativeListing};listingSource=source;nativeAdvertiser=new PublicRoomAdvertiser(new Uri(profile.DirectoryUrl),()=>source.Value);}
        else listingSource.Value=nativeListing;
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
                    if(error!=null){Word(44,8);if(operation!=0||!RoomSearchVisible())Status(error);return;}
                    social=client;socialView=view;Word(44,12);
                    if(found!=null)friends=found;if(pending!=null)requests=pending;if(inbox!=null)invitations=inbox;
                    PublishSocial(operation!=0);if(operation!=0||(existing==null&&!RoomSearchVisible()))Status(message);
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
    public void Dispose(){lock(gate){disposed=true;generation++;completions.Clear();}raceScores.Dispose();StopNativeAdvertisement();timer.Stop();timer.Dispose();}
}
