using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using OfflineLab.Multiplayer;

// Companion lobby UI. Native racing remains explicitly unavailable until the
// game bridge has established and tested race synchronization.
internal sealed class MultiplayerForm : Form
{
    private readonly Color navy = Color.FromArgb(16, 24, 37);
    private readonly Color cardColor = Color.FromArgb(26, 38, 55);
    private readonly Color cyan = Color.FromArgb(0, 210, 245);
    private readonly Color gold = Color.FromArgb(255, 191, 0);
    private readonly Color muted = Color.FromArgb(158, 176, 197);
    private readonly FlowLayoutPanel left = new FlowLayoutPanel();
    private readonly FlowLayoutPanel right = new FlowLayoutPanel();
    private readonly Label connection = new Label();
    private readonly Label footer = new Label();
    private readonly Label roomTitle = new Label();
    private readonly Label occupancy = new Label();
    private readonly Label votes = new Label();
    private readonly Label readiness = new Label();
    private readonly Label directoryStatus = new Label();
    private readonly Label phase = new Label();
    private readonly ListView servers = new ListView();
    private readonly ListView members = new ListView();
    private readonly TextBox nickname = new TextBox();
    private readonly TextBox status = new TextBox();
    private readonly TextBox group = new TextBox();
    private readonly TextBox roomName = new TextBox();
    private readonly CheckBox privateRoom = new CheckBox();
    private readonly TextBox createPassword = new TextBox();
    private readonly NumericUpDown createPort = new NumericUpDown();
    private readonly TextBox joinAddress = new TextBox();
    private readonly TextBox joinPassword = new TextBox();
    private readonly NumericUpDown joinPort = new NumericUpDown();
    private readonly CheckBox allowMutators = new CheckBox();
    private readonly CheckBox allowCheats = new CheckBox();
    private readonly CheckBox ownedCars = new CheckBox();
    private readonly CheckBox ignoreClass = new CheckBox();
    private readonly CheckBox fillBots = new CheckBox();
    private readonly ComboBox grid = new ComboBox();
    private readonly ComboBox laps = new ComboBox();
    private readonly TextBox directoryUrl = new TextBox();
    private readonly Button directoryBrowse = new Button();
    private readonly ComboBox map = new ComboBox();
    private readonly Button create = new Button();
    private readonly Button join = new Button();
    private readonly Button refresh = new Button();
    private readonly Button applyRules = new Button();
    private readonly Button kick = new Button();
    private readonly Button promote = new Button();
    private readonly Button invite = new Button();
    private readonly Button leave = new Button();
    private readonly Button vote = new Button();
    private readonly Button nextMap = new Button();
    private readonly Button start = new Button();
    private RoomClient client;
    private LanRoomAdvertiser advertiser;
    private PublicRoomAdvertiser publicAdvertiser;
    private RoomSnapshot current;
    private bool busy;
    private bool disposing;
    private bool renderingRules;
    private bool rulesDirty;
    private int rulesEditVersion;
    private int submittedRulesVersion;
    private RoomSettings pendingRules;
    private bool publicHandoverUnverified;
    private string connectionAddress = "";
    private int connectionPort;
    private readonly string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
        "_offline_patch_backup", "multiplayer", "profile.txt");

    internal event Action<RoomSnapshot> StartRaceRequested;

    internal MultiplayerForm()
    {
        Text = "Asphalt 8 • Self-Hosted Multiplayer";
        Size = new Size(1280, 900);
        MinimumSize = new Size(1150, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = navy;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        var header = new Panel { Dock=DockStyle.Top, Height=99, BackColor=navy };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var brush=new SolidBrush(Color.FromArgb(22,58,77)))
                e.Graphics.FillPolygon(brush,new[] { new Point(header.Width-360,0),new Point(header.Width,0),
                    new Point(header.Width,97),new Point(header.Width-415,97) });
            using(var pen=new Pen(cyan,3)) e.Graphics.DrawLine(pen,23,95,header.Width-23,95);
        };
        header.Controls.Add(new Label { Text="A8  /  SELF-HOSTED MULTIPLAYER", AutoSize=true,
            Font=new Font("Segoe UI",22f,FontStyle.Bold|FontStyle.Italic),Location=new Point(23,13) });
        phase.Text="ROOM CONTROL PREVIEW • NATIVE RACING NOT CONNECTED";phase.AutoSize=true;
        phase.ForeColor=gold;phase.Font=new Font("Segoe UI Semibold",8.5f);phase.Location=new Point(26,58);header.Controls.Add(phase);
        connection.Text="OFFLINE • NOT IN A ROOM";
        connection.AutoSize=false;connection.TextAlign=ContentAlignment.MiddleRight;
        connection.AutoEllipsis=true;
        connection.ForeColor=gold;connection.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        connection.SetBounds(810,33,420,30);header.Controls.Add(connection);
        header.Resize += (_,__) => connection.Left=header.Width-connection.Width-25;

        footer.Dock=DockStyle.Bottom;footer.Height=40;footer.Padding=new Padding(23,0,23,0);
        footer.TextAlign=ContentAlignment.MiddleLeft;footer.ForeColor=muted;
        footer.Text="Local rooms and direct connections • Close hides this window; Leave room disconnects.";
        Controls.Add(footer);Controls.Add(header);
        var split=new SplitContainer { Size=new Size(1240,700),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,
            SplitterDistance=395,Panel1MinSize=370,Panel2MinSize=650,BackColor=navy,SplitterWidth=10 };
        ConfigureFlow(left);ConfigureFlow(right);
        split.Panel1.Controls.Add(left);split.Panel2.Controls.Add(right);
        Controls.Add(split);split.BringToFront();

        BuildServerCard();BuildJoinCard();BuildCreateCard();BuildProfileCard();
        BuildRoomCard();BuildRulesCard();BuildVoteCard();BuildReadinessCard();
        left.SizeChanged += (_,__) => FitCards(left);
        right.SizeChanged += (_,__) => FitCards(right);
        privateRoom.CheckedChanged += (_,__) => createPassword.Enabled=privateRoom.Checked;
        createPassword.Enabled=false;
        LoadProfile();
        UpdateActions();
        Shown += (_,__) => { FitCards(left);FitCards(right); RefreshServers(); };
        FormClosing += (_,e) => { if(e.CloseReason==CloseReason.UserClosing && !disposing){e.Cancel=true;Hide();} };
    }

    private void ConfigureFlow(FlowLayoutPanel flow)
    {
        flow.Dock=DockStyle.Fill;flow.FlowDirection=FlowDirection.TopDown;flow.WrapContents=false;
        flow.AutoScroll=true;flow.Padding=new Padding(12,13,10,10);flow.BackColor=navy;
    }

    private void FitCards(FlowLayoutPanel flow)
    {
        int width=Math.Max(flow==left?340:610,flow.ClientSize.Width-46);
        foreach(Control child in flow.Controls)child.Width=width;
    }

    private Panel Card(FlowLayoutPanel parent,string title,int height)
    {
        var card=new Panel { Width=parent==left?350:750,Height=height,BackColor=cardColor,Margin=new Padding(0,0,0,13) };
        card.Controls.Add(new Label { Text=title,Font=new Font("Segoe UI Semibold",12f,FontStyle.Italic),
            ForeColor=cyan,Dock=DockStyle.Top,Height=40,Padding=new Padding(15,9,0,0),UseMnemonic=false });
        parent.Controls.Add(card);return card;
    }

    private TableLayoutPanel Body(Panel card,int columns)
    {
        var body=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(15,4,15,10),ColumnCount=columns,RowCount=1 };
        card.Controls.Add(body);body.BringToFront();
        return body;
    }

    private void StyleText(TextBox field,int maximum)
    {
        field.Dock=DockStyle.Fill;field.BackColor=Color.FromArgb(13,24,38);field.ForeColor=Color.White;
        field.BorderStyle=BorderStyle.FixedSingle;field.MaxLength=maximum;field.Margin=new Padding(0,4,0,4);
    }

    private void StyleNumber(NumericUpDown field,int minimum,int maximum,int value)
    {
        field.Minimum=minimum;field.Maximum=maximum;field.Value=value;field.Dock=DockStyle.Fill;
        field.BackColor=Color.FromArgb(13,24,38);field.ForeColor=Color.White;field.Margin=new Padding(0,4,0,4);
    }

    private void StyleDefaultChoice(ComboBox field)
    {
        field.Dock=DockStyle.Fill;field.DropDownStyle=ComboBoxStyle.DropDownList;
        field.BackColor=Color.FromArgb(13,24,38);field.ForeColor=Color.White;
        field.Items.Add("Default");for(int i=1;i<=60;i++)field.Items.Add(i.ToString());field.SelectedIndex=0;
    }

    private void StyleButton(Button button,string text,Color color)
    {
        button.Text=text;button.UseMnemonic=false;button.FlatStyle=FlatStyle.Flat;
        button.FlatAppearance.BorderColor=color;button.FlatAppearance.BorderSize=1;
        button.BackColor=Color.FromArgb(28,54,72);button.ForeColor=Color.White;
        button.Font=new Font("Segoe UI Semibold",8.5f);button.Height=33;
        button.AutoSize=true;button.MinimumSize=new Size(85,33);button.Margin=new Padding(0,4,8,3);
    }

    private void AddFormRow(TableLayoutPanel body,int row,string title,Control field)
    {
        body.RowCount=Math.Max(body.RowCount,row+1);body.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        body.Controls.Add(new Label { Text=title,ForeColor=muted,AutoSize=true,Anchor=AnchorStyles.Left,UseMnemonic=false },0,row);
        body.Controls.Add(field,1,row);
    }

    private void BuildServerCard()
    {
        var card=Card(left,"PUBLIC DIRECTORY  /  LOCAL LAN",270);
        var body=Body(card,1);body.RowCount=4;body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,23));body.RowStyles.Add(new RowStyle(SizeType.Absolute,31));body.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        ConfigureList(servers);servers.Columns.Add("Room",145);servers.Columns.Add("Players",60);servers.Columns.Add("Address",125);
        body.Controls.Add(servers,0,0);
        directoryStatus.Text="Public directory: not configured • enter its URL below";directoryStatus.ForeColor=muted;
        directoryStatus.Dock=DockStyle.Fill;directoryStatus.Font=new Font("Segoe UI",8f);body.Controls.Add(directoryStatus,0,1);
        StyleText(directoryUrl,512);body.Controls.Add(directoryUrl,0,2);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false };
        StyleButton(refresh,"REFRESH LAN",cyan);StyleButton(directoryBrowse,"BROWSE PUBLIC",gold);
        actions.Controls.Add(refresh);actions.Controls.Add(directoryBrowse);body.Controls.Add(actions,0,3);
        refresh.Click+=(_,__)=>RefreshServers();
        directoryBrowse.Click+=(_,__)=>
        {
            string url=directoryUrl.Text.Trim();
            if(url.Length==0){SetStatus("Public directory is not configured. Enter the URL of a running directory service.");return;}
            try
            {
                Uri directory=new Uri(url,UriKind.Absolute);PublicRoomDirectory.Validate(directory);
                List<LanRoomListing> found=null;
                Run(()=>found=PublicRoomDirectory.Fetch(directory),()=>
                {
                    PopulateServerList(found);directoryStatus.Text="Public directory connected • "+directory.Host;
                    SaveProfile(ReadProfile());SetStatus(servers.Items.Count+" public room(s) returned by the directory.");
                });
            }
            catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
        };
        servers.DoubleClick+=(_,__)=>SelectServer();servers.SelectedIndexChanged+=(_,__)=>SelectServer();
    }

    private void BuildJoinCard()
    {
        var card=Card(left,"JOIN A ROOM",203);var body=Body(card,2);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        StyleText(joinAddress,253);joinAddress.Text="127.0.0.1";StyleNumber(joinPort,1024,65535,47781);
        StyleText(joinPassword,128);joinPassword.UseSystemPasswordChar=true;
        AddFormRow(body,0,"Address",joinAddress);AddFormRow(body,1,"Port",joinPort);AddFormRow(body,2,"Password",joinPassword);
        StyleButton(join,"JOIN ROOM",cyan);body.Controls.Add(join,0,3);body.SetColumnSpan(join,2);join.Dock=DockStyle.Fill;
        join.Click+=(_,__)=>JoinRoom();
    }

    private void BuildCreateCard()
    {
        var card=Card(left,"CREATE A ROOM",231);var body=Body(card,2);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        StyleText(roomName,64);roomName.Text="Asphalt 8 room";StyleText(createPassword,128);createPassword.UseSystemPasswordChar=true;
        StyleNumber(createPort,1024,65535,47781);privateRoom.Text="Private • password required";privateRoom.AutoSize=true;privateRoom.Dock=DockStyle.Fill;
        AddFormRow(body,0,"Name",roomName);AddFormRow(body,1,"Visibility",privateRoom);AddFormRow(body,2,"Password",createPassword);AddFormRow(body,3,"Port",createPort);
        StyleButton(create,"CREATE & HOST",gold);body.Controls.Add(create,0,4);body.SetColumnSpan(create,2);create.Dock=DockStyle.Fill;
        create.Click+=(_,__)=>CreateRoom();
    }

    private void BuildProfileCard()
    {
        var card=Card(left,"DRIVER PROFILE",230);var body=Body(card,2);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        StyleText(nickname,24);StyleText(status,64);StyleText(group,12);
        nickname.Text="Driver";status.Text="Ready to race";
        AddFormRow(body,0,"Nickname",nickname);AddFormRow(body,1,"Status",status);AddFormRow(body,2,"Group tag",group);
        var save=new Button();StyleButton(save,"UPDATE PROFILE",cyan);body.Controls.Add(save,0,3);body.SetColumnSpan(save,2);save.Dock=DockStyle.Fill;
        var note=new Label { Text="Group tag is a profile label. Friends and persistent clans are not connected yet.",ForeColor=muted,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8f) };
        body.Controls.Add(note,0,4);body.SetColumnSpan(note,2);
        save.Click+=(_,__)=>
        {
            try
            {
                PlayerProfile profile=ReadProfile();SaveProfile(profile);
                if(client==null){SetStatus("Profile saved on this computer.");return;}
                RoomClient selected=client;Run(()=>selected.UpdateProfile(profile),()=>SetStatus("Profile updated in the room."));
            }
            catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
        };
    }

    private void ConfigureList(ListView list)
    {
        list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;
        list.HideSelection=false;list.BackColor=Color.FromArgb(15,27,42);list.ForeColor=Color.White;
        list.BorderStyle=BorderStyle.None;list.HeaderStyle=ColumnHeaderStyle.Nonclickable;
    }

    private void BuildRoomCard()
    {
        var card=Card(right,"ROOM  /  DRIVERS",314);var body=Body(card,1);body.RowCount=4;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,29));body.RowStyles.Add(new RowStyle(SizeType.Absolute,25));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        roomTitle.Text="NO ROOM CONNECTED";roomTitle.Font=new Font("Segoe UI Semibold",12f);roomTitle.Dock=DockStyle.Fill;roomTitle.AutoEllipsis=true;
        occupancy.Text="Create a room or connect to a host address.";occupancy.ForeColor=muted;occupancy.Dock=DockStyle.Fill;
        body.Controls.Add(roomTitle,0,0);body.Controls.Add(occupancy,0,1);
        ConfigureList(members);members.Columns.Add("Driver",155);members.Columns.Add("Role",85);members.Columns.Add("Ping",65);
        members.Columns.Add("Status",190);members.Columns.Add("Group",90);body.Controls.Add(members,0,2);
        members.SelectedIndexChanged+=(_,__)=>UpdateActions();
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true };
        StyleButton(kick,"KICK",gold);StyleButton(promote,"PROMOTE",cyan);StyleButton(invite,"COPY INVITE",cyan);StyleButton(leave,"LEAVE ROOM",gold);
        actions.Controls.AddRange(new Control[]{kick,promote,invite,leave});body.Controls.Add(actions,0,3);
        kick.Click+=(_,__)=>MemberAction(false);promote.Click+=(_,__)=>MemberAction(true);invite.Click+=(_,__)=>CopyInvite();leave.Click+=(_,__)=>LeaveRoom();
    }

    private void BuildRulesCard()
    {
        var card=Card(right,"HOST SETTINGS",219);var body=Body(card,2);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        body.RowCount=5;for(int i=0;i<5;i++)body.RowStyles.Add(new RowStyle(SizeType.Absolute,i==4?40:31));
        var checks=new[]{allowMutators,allowCheats,ownedCars,ignoreClass,fillBots};
        string[] texts={"Allow in-game mutators","Allow trainer cheats","Require genuinely owned cars","Ignore vehicle class","Fill empty grid slots with bots"};
        for(int i=0;i<checks.Length;i++){checks[i].Text=texts[i];checks[i].AutoSize=true;checks[i].Dock=DockStyle.Fill;body.Controls.Add(checks[i],i%2,i/2);}
        ownedCars.Checked=true;fillBots.Checked=true;StyleDefaultChoice(grid);StyleDefaultChoice(laps);
        var gridRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };gridRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));gridRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        gridRow.Controls.Add(new Label{Text="Grid slots (total)",AutoSize=true,Anchor=AnchorStyles.Left,ForeColor=muted},0,0);gridRow.Controls.Add(grid,1,0);
        var lapRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };lapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));lapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        lapRow.Controls.Add(new Label{Text="Laps",AutoSize=true,Anchor=AnchorStyles.Left,ForeColor=muted},0,0);lapRow.Controls.Add(laps,1,0);
        body.Controls.Add(gridRow,0,3);body.Controls.Add(lapRow,1,3);
        StyleButton(applyRules,"APPLY ROOM SETTINGS",cyan);applyRules.Dock=DockStyle.Fill;body.Controls.Add(applyRules,0,4);body.SetColumnSpan(applyRules,2);
        applyRules.Click+=(_,__)=>
        {
            if(client==null){SetStatus("Settings will be used when you create the room.");return;}
            try
            {
                RoomSettings settings=ReadSettings();RoomClient selected=client;
                pendingRules=settings.Copy();submittedRulesVersion=rulesEditVersion;rulesDirty=true;
                Run(()=>selected.UpdateSettings(settings),()=>SetStatus(pendingRules!=null?
                    "Room settings submitted; waiting for host confirmation.":rulesDirty?
                    "Unsaved room settings remain.":"Room settings applied."));
            }
            catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
        };
        foreach(CheckBox check in new[]{allowMutators,allowCheats,ownedCars,ignoreClass,fillBots})
            check.CheckedChanged+=(_,__)=>RulesEdited();
        grid.SelectedIndexChanged+=(_,__)=>RulesEdited();laps.SelectedIndexChanged+=(_,__)=>RulesEdited();
    }

    private void BuildVoteCard()
    {
        var card=Card(right,"NEXT MAP  /  VOTE",145);var body=Body(card,3);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));
        body.RowCount=2;body.RowStyles.Add(new RowStyle(SizeType.Absolute,41));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        map.Dock=DockStyle.Fill;map.DropDownStyle=ComboBoxStyle.DropDown;map.BackColor=Color.FromArgb(13,24,38);map.ForeColor=Color.White;
        map.Items.AddRange(new object[]{"Sector 8","Iceland","Tokyo","Nevada","Great Wall","Temple","San Diego Harbor"});map.SelectedIndex=0;
        StyleButton(vote,"VOTE",cyan);StyleButton(nextMap,"SELECT WINNER",gold);vote.Dock=DockStyle.Fill;nextMap.Dock=DockStyle.Fill;
        body.Controls.Add(map,0,0);body.Controls.Add(vote,1,0);body.Controls.Add(nextMap,2,0);
        votes.Text="One vote per driver. The host selects the winning map.";votes.ForeColor=muted;votes.Dock=DockStyle.Fill;
        body.Controls.Add(votes,0,1);body.SetColumnSpan(votes,3);
        vote.Click+=(_,__)=>{if(client==null)return;string choice=map.Text.Trim();RoomClient selected=client;Run(()=>selected.Vote(choice),()=>SetStatus("Map vote submitted."));};
        nextMap.Click+=(_,__)=>{if(client==null)return;RoomClient selected=client;Run(()=>selected.SelectNextMap(),()=>SetStatus("Winning map selected."));};
    }

    private void BuildReadinessCard()
    {
        var card=Card(right,"RACE BRIDGE  /  DEVELOPMENT STATUS",178);var body=Body(card,2);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));
        readiness.Text="Room networking is separate from race synchronization.\r\nGraceful lobby handover is available; abrupt host loss and in-race migration are unverified.";
        readiness.ForeColor=gold;readiness.Dock=DockStyle.Fill;readiness.Padding=new Padding(0,4,10,0);
        StyleButton(start,"START RACE",gold);start.Enabled=false;start.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        body.Controls.Add(readiness,0,0);body.Controls.Add(start,1,0);
        start.Click+=(_,__)=>{Action<RoomSnapshot> handler=StartRaceRequested;if(handler!=null && current!=null && current.RaceBridgeReady)handler(current);};
    }

    private PlayerProfile ReadProfile()
    {
        if(string.IsNullOrWhiteSpace(nickname.Text))throw new InvalidOperationException("Enter a driver nickname first.");
        var profile=new PlayerProfile { Nickname=nickname.Text.Trim(),Status=status.Text.Trim(),GroupTag=group.Text.Trim() };
        profile.Validate();return profile;
    }

    private RoomSettings ReadSettings()
    {
        return new RoomSettings { AllowMutators=allowMutators.Checked,AllowCheats=allowCheats.Checked,
            RequireOwnedCars=ownedCars.Checked,IgnoreClass=ignoreClass.Checked,FillBots=fillBots.Checked,
            GridSlots=grid.SelectedIndex,Laps=laps.SelectedIndex };
    }

    private void CreateRoom()
    {
        if(busy || client!=null)return;
        try
        {
            PlayerProfile profile=ReadProfile();RoomSettings settings=ReadSettings();string name=roomName.Text.Trim();
            bool isPrivate=privateRoom.Checked;string password=isPrivate?createPassword.Text:"";int port=(int)createPort.Value;
            if(isPrivate && string.IsNullOrEmpty(password))throw new InvalidOperationException("A private room requires a password.");
            Uri directory=null;
            if(!isPrivate && directoryUrl.Text.Trim().Length>0){directory=new Uri(directoryUrl.Text.Trim(),UriKind.Absolute);PublicRoomDirectory.Validate(directory);}
            SaveProfile(profile);RoomClient created=null;
            Run(()=>{created=RoomClient.Create(name,profile,settings,password,port);if(disposing)created.Dispose();},()=>
            {
                connectionAddress=LocalAddress();connectionPort=created.HostedServer==null?port:created.HostedServer.Port;Attach(created);
                SetStatus(isPrivate?"Private room hosted. Share the address and password separately.":directory==null?
                    "Room hosted on LAN. No public Internet directory is configured.":"Room hosted. Public directory announcement is pending.");
            });
        }
        catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
    }

    private void JoinRoom()
    {
        if(busy || client!=null)return;
        try
        {
            PlayerProfile profile=ReadProfile();string address=joinAddress.Text.Trim();int port=(int)joinPort.Value;string password=joinPassword.Text;
            SaveProfile(profile);RoomClient joined=null;
            Run(()=>{joined=RoomClient.Join(address,port,password,profile);if(disposing)joined.Dispose();},()=>{connectionAddress=address;connectionPort=port;Attach(joined);SetStatus("Connected to the room host.");});
        }
        catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
    }

    private void Attach(RoomClient connected)
    {
        client=connected;
        rulesDirty=false;pendingRules=null;publicHandoverUnverified=false;
        connected.SnapshotChanged+=snapshot=>Post(()=>{if(client==connected)Render(snapshot);});
        connected.ErrorReceived+=message=>Post(()=>
        {
            if(client!=connected)return;
            if(pendingRules!=null){pendingRules=null;rulesDirty=true;}
            SetStatus(message);
        });
        connected.Disconnected+=reason=>Post(()=>
        {
            if(client!=connected)return;
            StopAdvertiser();client=null;current=null;members.Items.Clear();UpdateActions();
            connection.Text="DISCONNECTED";roomTitle.Text="ROOM DISCONNECTED";SetStatus(reason);
        });
        if(connected.Snapshot!=null)Render(connected.Snapshot);else UpdateActions();
    }

    private void Render(RoomSnapshot snapshot)
    {
        bool differentRoom=current==null||current.RoomId!=snapshot.RoomId;
        bool newHost=current!=null&&current.HostId!=snapshot.HostId&&snapshot.HostId==client.LocalId;
        if(differentRoom){rulesDirty=false;pendingRules=null;publicHandoverUnverified=false;}
        if(newHost)publicHandoverUnverified=true;
        SyncHosting(snapshot);
        current=snapshot;roomTitle.Text=snapshot.Name+"  /  "+snapshot.MapId;
        int humans=snapshot.Members==null?0:snapshot.Members.Count;
        string slots=snapshot.Settings.GridSlots==0?"default":snapshot.Settings.GridSlots.ToString();
        string lapCount=snapshot.Settings.Laps==0?"default":snapshot.Settings.Laps.ToString();
        occupancy.Text=humans+" human drivers + "+snapshot.BotCount+" bots / "+slots+" grid slots • "+lapCount+" laps";
        connection.Text=(snapshot.IsPrivate?"PRIVATE • ":"PUBLIC • ")+(snapshot.HostId==client.LocalId?"HOSTING":"CONNECTED")+" • "+connectionAddress+":"+connectionPort;
        string selected=members.SelectedItems.Count==0?null:(string)members.SelectedItems[0].Tag;
        members.BeginUpdate();members.Items.Clear();
        if(snapshot.Members!=null)foreach(RoomMember member in snapshot.Members)
        {
            string role=member.IsHost?"HOST":member.IsModerator?"MODERATOR":"DRIVER";
            var row=new ListViewItem(member.Nickname+(member.Id==client.LocalId?" (you)":""));row.Tag=member.Id;
            row.SubItems.Add(role);row.SubItems.Add(member.RttMs<0?"—":member.RttMs+" ms");row.SubItems.Add(member.Status??"");row.SubItems.Add(member.GroupTag??"");
            if(member.IsHost)row.ForeColor=gold;members.Items.Add(row);if(member.Id==selected)row.Selected=true;
        }
        members.EndUpdate();
        RoomSettings rules=snapshot.Settings;
        bool host=snapshot.HostId==client.LocalId;
        if(pendingRules!=null&&submittedRulesVersion==rulesEditVersion&&SameRules(pendingRules,rules))
        {pendingRules=null;rulesDirty=false;SetStatus("Room settings applied.");}
        if(!host){rulesDirty=false;pendingRules=null;}
        if(!rulesDirty&&pendingRules==null)
        {
            renderingRules=true;
            try
            {
                allowMutators.Checked=rules.AllowMutators;allowCheats.Checked=rules.AllowCheats;ownedCars.Checked=rules.RequireOwnedCars;
                ignoreClass.Checked=rules.IgnoreClass;fillBots.Checked=rules.FillBots;
                grid.SelectedIndex=Math.Max(0,Math.Min(60,rules.GridSlots));laps.SelectedIndex=Math.Max(0,Math.Min(60,rules.Laps));
            }
            finally{renderingRules=false;}
        }
        var counts=new SortedDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        if(snapshot.Votes!=null)foreach(string choice in snapshot.Votes.Values){int count;counts.TryGetValue(choice,out count);counts[choice]=count+1;}
        var summaries=new List<string>();foreach(var pair in counts)summaries.Add(pair.Key+": "+pair.Value);
        votes.Text=summaries.Count==0?"No votes yet. One vote per driver.":string.Join("   •   ",summaries.ToArray());
        readiness.Text=(snapshot.RaceBridgeReady?"Native race bridge connected.":"Native race synchronization is NOT READY. Rooms and map voting do not launch a synchronized race.")+
            "\r\n"+(snapshot.HostMigrationReady?
                "Graceful lobby handover available; abrupt host loss and in-race migration are unverified.":
                "Lobby handover is unavailable; abrupt host loss and in-race migration are unverified.");
        phase.Text=snapshot.RaceBridgeReady?"NATIVE RACE BRIDGE CONNECTED • COMMUNITY ROOM CONTROL":"ROOM CONTROL PREVIEW • NATIVE RACING NOT CONNECTED";
        UpdateActions();
    }

    private void RulesEdited()
    {
        if(renderingRules)return;
        rulesDirty=true;pendingRules=null;rulesEditVersion++;
        SetStatus(client==null?"Settings will be used for your next room.":"Unsaved host settings • click Apply room settings to publish them.");
    }

    private static bool SameRules(RoomSettings a,RoomSettings b)
    {
        return a!=null&&b!=null&&a.AllowMutators==b.AllowMutators&&a.AllowCheats==b.AllowCheats&&
            a.RequireOwnedCars==b.RequireOwnedCars&&a.IgnoreClass==b.IgnoreClass&&a.FillBots==b.FillBots&&
            a.GridSlots==b.GridSlots&&a.Laps==b.Laps;
    }

    private void SyncHosting(RoomSnapshot snapshot)
    {
        bool host=snapshot.HostId==client.LocalId&&client.HostedServer!=null;
        if(host)
        {
            connectionAddress=LocalAddress();connectionPort=client.HostedServer.Port;
        }
        else
        {
            connectionAddress=client.HostAddress;connectionPort=client.HostPort;
        }
        if(!host||snapshot.IsPrivate){StopAdvertiser();return;}
        if(advertiser==null)
        {
            try{advertiser=new LanRoomAdvertiser(()=>AdvertisedRoom());}
            catch(Exception ex){SetStatus("Room hosted. LAN announcement unavailable: "+RoomErrors.Display(ex));}
        }
        if(publicHandoverUnverified)
        {
            if(publicAdvertiser!=null){PublicRoomAdvertiser old=publicAdvertiser;publicAdvertiser=null;Task.Run(()=>old.Dispose());}
            directoryStatus.Text="Lobby host changed • public-directory handover is unverified";
            return;
        }
        string url=directoryUrl.Text.Trim();
        if(publicAdvertiser==null&&url.Length>0)
        {
            try
            {
                Uri directory=new Uri(url,UriKind.Absolute);PublicRoomDirectory.Validate(directory);
                publicAdvertiser=new PublicRoomAdvertiser(directory,()=>AdvertisedRoom());
                publicAdvertiser.StatusChanged+=message=>Post(()=>directoryStatus.Text=message);
            }
            catch(Exception ex){directoryStatus.Text="Public announcement unavailable: "+RoomErrors.Display(ex);}
        }
    }

    private void UpdateActions()
    {
        bool connected=client!=null;bool host=connected && current!=null && current.HostId==client.LocalId;
        create.Enabled=!busy&&!connected;join.Enabled=!busy&&!connected;refresh.Enabled=!busy;directoryBrowse.Enabled=!busy;
        leave.Enabled=!busy&&connected;invite.Enabled=!busy&&connected;vote.Enabled=!busy&&connected;nextMap.Enabled=!busy&&host;
        applyRules.Enabled=!busy&&(!connected||host);
        foreach(Control check in new Control[]{allowMutators,allowCheats,ownedCars,ignoreClass,fillBots,grid,laps})check.Enabled=!busy&&(!connected||host);
        bool other=members.SelectedItems.Count>0 && connected && (string)members.SelectedItems[0].Tag!=client.LocalId;
        kick.Enabled=!busy&&host&&other;promote.Enabled=!busy&&host&&other;
        start.Enabled=!busy&&host&&current!=null&&current.RaceBridgeReady&&StartRaceRequested!=null;
        roomName.Enabled=!connected;privateRoom.Enabled=!connected;createPort.Enabled=!connected;
    }

    private void MemberAction(bool promotion)
    {
        if(client==null || members.SelectedItems.Count==0)return;
        string id=(string)members.SelectedItems[0].Tag;RoomClient selected=client;
        Run(()=>{if(promotion)selected.Promote(id);else selected.Kick(id);},()=>SetStatus(promotion?"Moderator promotion requested.":"Driver removal requested."));
    }

    private void LeaveRoom()
    {
        if(client==null || busy)return;
        RoomClient selected=client;StopAdvertiser();client=null;current=null;
        Run(()=>selected.Dispose(),()=>
        {
            members.Items.Clear();roomTitle.Text="NO ROOM CONNECTED";occupancy.Text="Create a room or connect to a host address.";
            connection.Text="OFFLINE • NOT IN A ROOM";votes.Text="No room connected.";SetStatus("Left the room.");UpdateActions();
        });
    }

    private void CopyInvite()
    {
        if(client==null)return;
        if(publicAdvertiser!=null && current!=null)
        {
            try
            {
                Uri directory=new Uri(directoryUrl.Text.Trim(),UriKind.Absolute);PublicRoomDirectory.Validate(directory);
                string roomId=current.RoomId;string address=connectionAddress;int port=connectionPort;
                Run(()=>
                {
                    foreach(LanRoomListing listing in PublicRoomDirectory.Fetch(directory))
                        if(listing.RoomId==roomId){address=listing.Address;port=listing.Port;break;}
                },()=>
                {
                    try{Clipboard.SetText(address+":"+port);SetStatus("Room endpoint copied. Internet reachability depends on the host's network configuration.");}
                    catch(Exception ex){SetStatus("Could not copy the invitation: "+RoomErrors.Display(ex));}
                });
            }
            catch(Exception ex){SetStatus(RoomErrors.Display(ex));}
            return;
        }
        try{Clipboard.SetText(connectionAddress+":"+connectionPort);SetStatus("Host address copied. Share a private room's password separately.");}
        catch(Exception ex){SetStatus("Could not copy the invitation: "+RoomErrors.Display(ex));}
    }

    private void RefreshServers()
    {
        if(busy)return;
        List<LanRoomListing> found=null;
        Run(()=>found=LanRoomDiscovery.Discover(1200),()=>
        {
            PopulateServerList(found);SetStatus(servers.Items.Count+" public room(s) found on the local network. Remote hosts can be joined by address.");
        });
    }

    private void PopulateServerList(IEnumerable<LanRoomListing> found)
    {
        servers.BeginUpdate();servers.Items.Clear();
        if(found!=null)foreach(LanRoomListing listing in found)
        {
            var row=new ListViewItem(listing.Name);row.SubItems.Add(listing.Players+"/"+listing.Capacity);
            row.SubItems.Add(listing.Address+":"+listing.Port);row.Tag=listing;servers.Items.Add(row);
        }
        servers.EndUpdate();
    }

    private void SelectServer()
    {
        if(servers.SelectedItems.Count==0)return;var listing=servers.SelectedItems[0].Tag as LanRoomListing;if(listing==null)return;
        joinAddress.Text=listing.Address;joinPort.Value=Math.Max(joinPort.Minimum,Math.Min(joinPort.Maximum,listing.Port));
    }

    private LanRoomListing AdvertisedRoom()
    {
        RoomClient selected=client;if(selected==null)return null;RoomSnapshot snapshot=selected.Snapshot;if(snapshot==null)return null;
        return new LanRoomListing { RoomId=snapshot.RoomId,Name=snapshot.Name,Address=connectionAddress,Port=connectionPort,
            Players=snapshot.Members==null?0:snapshot.Members.Count,Capacity=snapshot.Settings.GridSlots==0?8:snapshot.Settings.GridSlots,IsPrivate=false };
    }

    private string LocalAddress()
    {
        try{foreach(IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))if(address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(address))return address.ToString();}
        catch(SocketException){}
        return "127.0.0.1";
    }

    private void StopAdvertiser()
    {
        LanRoomAdvertiser lan=advertiser;PublicRoomAdvertiser internet=publicAdvertiser;
        advertiser=null;publicAdvertiser=null;
        if(lan==null&&internet==null)return;
        Task.Run(()=>
        {
            try{if(lan!=null)lan.Dispose();if(internet!=null)internet.Dispose();}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine("Room announcement shutdown: "+RoomErrors.Display(ex));}
        });
    }
    private void SetStatus(string message){footer.Text=message??"";}

    private void Run(Action work,Action completed)
    {
        if(busy)return;busy=true;UpdateActions();SetStatus("CONNECTING / APPLYING…");
        Task.Run(()=>
        {
            Exception failure=null;try{work();}catch(Exception ex){failure=ex;}
            Post(()=>{busy=false;if(failure!=null)SetStatus(RoomErrors.Display(failure));else if(completed!=null)completed();UpdateActions();});
        });
    }

    private void Post(Action action)
    {
        if(IsDisposed||disposing)return;
        try{if(InvokeRequired)BeginInvoke(action);else action();}
        catch(InvalidOperationException){}
    }

    private void LoadProfile()
    {
        try
        {
            if(!File.Exists(profilePath))return;string[] saved=File.ReadAllLines(profilePath);
            if(saved.Length>0)nickname.Text=saved[0];if(saved.Length>1)status.Text=saved[1];if(saved.Length>2)group.Text=saved[2];
            if(saved.Length>3)directoryUrl.Text=saved[3];
            if(directoryUrl.Text.Trim().Length>0)directoryStatus.Text="Public directory configured • not contacted yet";
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }

    private void SaveProfile(PlayerProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath));
        File.WriteAllLines(profilePath,new[]{CleanLine(profile.Nickname),CleanLine(profile.Status),CleanLine(profile.GroupTag),CleanLine(directoryUrl.Text.Trim())});
    }

    private static string CleanLine(string value){return (value??"").Replace("\r","").Replace("\n","");}

    protected override void Dispose(bool disposingManaged)
    {
        if(disposingManaged)
        {
            disposing=true;StopAdvertiser();RoomClient selected=client;client=null;
            if(selected!=null){try{selected.Dispose();}catch(Exception ex){System.Diagnostics.Trace.WriteLine("Room shutdown: "+RoomErrors.Display(ex));}}
        }
        base.Dispose(disposingManaged);
    }
}
