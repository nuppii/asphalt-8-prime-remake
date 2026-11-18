using System;
using OfflineLab.Multiplayer;

internal static class SocialCheck
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Denied(Action action,int expected)
    {try{action();throw new Exception("Expected service refusal.");}catch(SocialServiceException ex){Check(ex.StatusCode==expected,"Unexpected refusal status.");}}
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=1)throw new ArgumentException("Usage: SocialCheck <local-test-directory-url>");
            var url=new Uri(args[0]);if(!url.IsLoopback)throw new ArgumentException("This test only uses a local service.");
            SocialIdentity first=SocialClient.Register(url,"CSharp test player");
            SocialIdentity second=SocialClient.Register(url,"CSharp test player");
            var alice=new SocialClient(url,first);var bob=new SocialClient(url,second);
            Check(alice.PlayerId!=bob.PlayerId,"Nicknames must not establish identity.");
            var bad=new SocialClient(url,new SocialIdentity{PlayerId=first.PlayerId,AuthToken=new string('x',43)});
            Denied(()=>bad.GetMe(),401);
            var room=RoomInvites.CreateDirect("social-test-room","Private test room","127.0.0.1",47781,true);
            Denied(()=>alice.SendInvitation(bob.PlayerId,room),403);
            SocialFriendRequest request=alice.RequestFriend(bob.PlayerId);
            Check(bob.GetRequests().Count==1&&alice.GetFriends().Count==0,"Friendship must require acceptance.");
            Denied(()=>alice.Accept(request.RequestId),404);
            Check(bob.Accept(request.RequestId).State=="accepted","Acceptance failed.");
            Check(alice.GetFriends().Count==1&&bob.GetFriends().Count==1,"Friendship must be mutual.");
            Check(bob.UpdatePresence("Renamed CSharp player","In a lobby").Online,"Presence heartbeat failed.");
            Check(alice.GetFriends()[0].Nickname=="Renamed CSharp player","Friend profile update failed.");
            SocialAction sent=alice.SendInvitation(bob.PlayerId,room);
            var inbox=bob.GetInvitations();
            Check(inbox.Count==1&&inbox[0].InvitationId==sent.InvitationId,"Invitation was not delivered.");
            Check(inbox[0].Sender.PlayerId==alice.PlayerId&&alice.GetInvitations().Count==0,"Invitation inbox was not isolated.");
            Check(RoomInvites.Parse(inbox[0].Reference).RoomId==room.RoomId,"Invitation reference changed.");
            Denied(()=>alice.Dismiss(sent.InvitationId),404);
            bob.Dismiss(sent.InvitationId);Check(bob.GetInvitations().Count==0,"Dismissal failed.");
            Check(bob.Heartbeat().PlayerId==bob.PlayerId,"Heartbeat changed identity.");
            Console.WriteLine("PASS: C# identities, authentication, mutual friends, presence, invitation delivery and dismissal.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}
    }
}
