using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class InviteCheck
{
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length==0||args[0]=="selftest")SelfTest();
            else if(args.Length==2&&args[0]=="directory")DirectoryTest(new Uri(args[1]));
            else throw new ArgumentException("Usage: InviteCheck selftest | directory <loopback-directory-url>");
            Console.WriteLine("PASS: invitation and named-room checks completed.");return 0;
        }
        catch(Exception error){Console.WriteLine("FAIL: "+error);return 1;}
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Reject<T>(Action action,string message) where T:Exception
    {try{action();}catch(T){return;}throw new Exception(message);}
    private static void Wait(Func<bool> condition,string message)
    {for(int i=0;i<200;i++){if(condition())return;Thread.Sleep(25);}throw new Exception(message);}
    private static void SelfTest()
    {
        string id=Guid.NewGuid().ToString("N");
        RoomInvitation direct=RoomInvites.CreateDirect(id,"Friends Night","127.0.0.1",47781,true);
        string reference=RoomInvites.Encode(direct);
        RoomInvitation parsed=RoomInvites.Parse(reference);
        Check(parsed.RoomId==id&&parsed.RoomName=="Friends Night"&&parsed.IsPrivate,"Direct invitation round-trip changed its reference.");
        Check(!RoomInvites.ResolveForJoin(parsed).CapacityKnown,"A direct invitation fabricated a live capacity.");
        string encoded=reference.Substring("a8ol:v1:".Length).Replace('-','+').Replace('_','/');
        encoded=encoded.PadRight(encoded.Length+(4-encoded.Length%4)%4,'=');
        string json=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Check(json.IndexOf("password",StringComparison.OrdinalIgnoreCase)<0&&json.IndexOf("ticket",StringComparison.OrdinalIgnoreCase)<0,"Invitation leaked authentication data.");
        RoomInvitation publicInvite=RoomInvites.CreateDirectory(id,"Public Room",new Uri("https://directory.example.test/"));
        Check(RoomInvites.Parse(RoomInvites.Encode(publicInvite)).DirectoryUrl=="https://directory.example.test/","Directory invitation round-trip failed.");
        Reject<ArgumentException>(()=>RoomInvites.CreateDirect(id," ","127.0.0.1",47781,false),"Empty room name was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.CreateDirect(id,"Valid","user:secret@example.test",47781,false),"Credential-bearing endpoint was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.CreateDirect(id,"Valid","0.0.0.0",47781,false),"Wildcard address was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.CreateDirect(id,"Valid","127.0.0.1",0,false),"Zero port was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.CreateDirectory(id,"Valid",new Uri("https://user:secret@example.test/")),"Credential-bearing directory URL was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.Parse("a8ol:v1:bad!"),"Invalid invitation encoding was accepted.");
        Reject<ArgumentException>(()=>RoomInvites.Parse("a8ol:v2:AAAA"),"Unknown invitation version was accepted.");
        string temporary=Path.Combine(Path.GetTempPath(),"OfflineLabFriendCheck-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);string bookPath=Path.Combine(temporary,"friends.json");
        try
        {
            var book=new LocalFriendAddressBook(bookPath);LocalFriendContact friend=book.Add("Known Friend");
            string prepared=book.PrepareInvitation(friend.Id,direct);
            var restored=new LocalFriendAddressBook(bookPath);
            Check(restored.Contacts.Count==1&&restored.Contacts[0].Id==friend.Id,"Local friend identity did not survive reload.");
            Check(restored.ReadReceivedInvitation(friend.Id,prepared).RoomId==id,"Stored friend's invitation reference failed.");
            Reject<InvalidOperationException>(()=>restored.PrepareInvitation(Guid.NewGuid().ToString("N"),direct),"An unknown friend was allowed to prepare an invitation.");
            restored.Remove(friend.Id);Check(new LocalFriendAddressBook(bookPath).Contacts.Count==0,"Friend removal was not saved.");
        }
        finally{if(File.Exists(bookPath))File.Delete(bookPath);Directory.Delete(temporary);}
        using(RoomClient host=RoomClient.Create("Private test",new PlayerProfile{Nickname="Host"},new RoomSettings{GridSlots=2},"test-only-secret",0))
        {
            RoomInvitation live=RoomInvites.CreateDirect(host.Snapshot.RoomId,host.Snapshot.Name,"127.0.0.1",host.HostedServer.Port,true);
            Reject<InvalidOperationException>(()=>RoomInvites.Join(live,"wrong",new PlayerProfile{Nickname="Wrong"}),"A wrong private password was accepted.");
            using(RoomClient joined=RoomInvites.Join(live,"test-only-secret",new PlayerProfile{Nickname="Invited"}))
            {
                Wait(()=>host.Snapshot.Members.Count==2,"Invited client did not join the actual room.");
                Reject<InvalidOperationException>(()=>RoomInvites.Join(live,"test-only-secret",new PlayerProfile{Nickname="Full"}),"Direct invitation bypassed host capacity.");
            }
            Wait(()=>host.Snapshot.Members.Count==1,"Invited client did not leave cleanly.");
            RoomInvitation stale=RoomInvites.CreateDirect(Guid.NewGuid().ToString("N"),live.RoomName,live.Address,live.Port,true);
            Reject<InvalidOperationException>(()=>RoomInvites.Join(stale,"test-only-secret",new PlayerProfile{Nickname="Stale"}),"An invitation joined a different room at the same endpoint.");
        }
    }
    private static void DirectoryTest(Uri directory)
    {
        if(!directory.IsLoopback)throw new ArgumentException("Integration checks are restricted to a local directory.");
        string name="Stra\u00dfe Search "+Guid.NewGuid().ToString("N").Substring(0,8);
        using(RoomClient one=RoomClient.Create(name,new PlayerProfile{Nickname="One"},new RoomSettings{GridSlots=4},"",0))
        using(RoomClient two=RoomClient.Create(name.ToUpperInvariant(),new PlayerProfile{Nickname="Two"},new RoomSettings{GridSlots=4},"",0))
        using(RoomClient full=RoomClient.Create(name+" Full",new PlayerProfile{Nickname="Full"},new RoomSettings{GridSlots=1},"",0))
        using(RoomClient hidden=RoomClient.Create(name+" Private",new PlayerProfile{Nickname="Hidden"},new RoomSettings{GridSlots=4},"private",0))
        using(RoomClient locked=RoomClient.Create(name+" Locked",new PlayerProfile{Nickname="Locked"},new RoomSettings{GridSlots=4},"locked-test-secret",0))
        using(var advertiseOne=new PublicRoomAdvertiser(directory,()=>one.HostedServer.CreateListing()))
        using(var advertiseTwo=new PublicRoomAdvertiser(directory,()=>two.HostedServer.CreateListing()))
        using(var advertiseFull=new PublicRoomAdvertiser(directory,()=>full.HostedServer.CreateListing()))
        using(var advertiseHidden=new PublicRoomAdvertiser(directory,()=>hidden.HostedServer.CreateListing()))
        using(var advertiseLocked=new PublicRoomAdvertiser(directory,()=>locked.HostedServer.CreateListing(true)))
        {
            Wait(()=>PublicRoomDirectory.SearchPage(directory,name).TotalMatches==2,"Both named room choices were not listed.");
            RoomSearchPage page=PublicRoomDirectory.SearchPage(directory,name.ToLowerInvariant(),1);
            Check(page.TotalMatches==2&&page.Rooms.Count==1&&page.NextOffset==1,"Named room pagination failed.");
            Reject<AmbiguousRoomNameException>(()=>RoomInvites.JoinByName(directory,name,null,"",new PlayerProfile{Nickname="Choice"}),"Duplicate names silently selected the first room.");
            using(RoomClient selected=RoomInvites.JoinByName(directory,name,one.Snapshot.RoomId,"",new PlayerProfile{Nickname="Selected"}))
            {Check(selected.Snapshot.RoomId==one.Snapshot.RoomId,"Explicit named-room choice joined a different host.");}
            using(RoomClient unicodeSelected=RoomInvites.JoinByName(directory,name.Replace("Stra\u00dfe","STRASSE"),one.Snapshot.RoomId,"",new PlayerProfile{Nickname="Unicode"}))
            {Check(unicodeSelected.Snapshot.RoomId==one.Snapshot.RoomId,"Unicode case-insensitive selection did not retain the selected room.");}
            RoomInvitation directoryInvite=RoomInvites.CreateDirectory(one.Snapshot.RoomId,name,directory);
            Check(RoomInvites.ResolveForJoin(directoryInvite).Port==one.HostedServer.Port,"Directory invitation did not resolve the live host endpoint.");
            Wait(()=>PublicRoomDirectory.SearchPage(directory,name+" Full").TotalMatches==1,"Full room listing is absent.");
            Reject<InvalidOperationException>(()=>RoomInvites.ResolveForJoin(RoomInvites.CreateDirectory(full.Snapshot.RoomId,full.Snapshot.Name,directory)),"Full public room was accepted for joining.");
            Check(PublicRoomDirectory.Search(directory,name+" Private").Count==0,"A private room was published.");
            Reject<InvalidOperationException>(()=>PublicRoomDirectory.Resolve(directory,hidden.Snapshot.RoomId),"A private room was resolved from the public directory.");
            string lockedName=locked.Snapshot.Name;
            string lockedId=locked.Snapshot.RoomId;
            Wait(()=>PublicRoomDirectory.Search(directory,lockedName).Count==1,"The listed password-protected room is missing.");
            Check(PublicRoomDirectory.Resolve(directory,lockedId).PasswordProtected,"The directory lost the password flag.");
            var guest=new PlayerProfile{Nickname="Locked guest"};
            Reject<InvalidOperationException>(()=>RoomInvites.JoinByName(directory,lockedName,lockedId,"",guest),"A password-protected room accepted an empty password.");
            Reject<InvalidOperationException>(()=>RoomInvites.JoinByName(directory,lockedName,lockedId,"wrong",guest),"A password-protected room accepted an incorrect password.");
            Reject<InvalidOperationException>(()=>RoomClient.Join("127.0.0.1",locked.HostedServer.Port,"",guest),"Direct connection bypassed the password.");
            using(RoomClient selected=RoomInvites.JoinByName(directory,lockedName,lockedId,"locked-test-secret",guest))
            {Check(selected.Snapshot.RoomId==lockedId,"The password-protected room did not accept the correct password.");}
            Reject<ArgumentException>(()=>PublicRoomDirectory.Search(directory,"  "),"Empty name search was accepted.");
        }
    }
}
