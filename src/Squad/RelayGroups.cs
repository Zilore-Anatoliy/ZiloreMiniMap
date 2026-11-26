using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace ZiloreMiniMap {
    internal sealed class RelayStorageException:IOException { internal RelayStorageException(Exception error):base("Private group storage failed",error) {} }
    internal sealed class RelayGroupLimitException:InvalidOperationException {}
    internal sealed class RelayMembership {
        internal string Id,Key,Owner,Pending,Name,NameStamp="";
        internal DirectPositions Session;
        internal bool AutoConnect;internal string MemberToken=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
        internal DateTime RetryAt,NextRosterAt;internal int RetryCount;internal RelayGroupStore.Member[] Roster=Array.Empty<RelayGroupStore.Member>();
        internal string Code { get { return "MM1-"+Id+"-"+Key.TrimEnd('=').Replace('+','-').Replace('/','_'); } }
        internal bool IsOwner { get { return !string.IsNullOrEmpty(Owner); } }
        internal string Label { get { return string.IsNullOrWhiteSpace(Name)?Localization.Get("PrivateGroupLabel")+" "+Id.Substring(0,8):Name; } }
        internal static bool IsNameMarker(SharedMapMarker marker) { return marker!=null&&marker.Id!=null&&marker.Id.Length==64&&marker.Id.StartsWith("4752504E",StringComparison.Ordinal); }
        internal static void ValidateGroupName(string value) { if(string.IsNullOrWhiteSpace(value)||value.Length>48||value.Any(char.IsControl))throw new ArgumentException("Invalid group name.",nameof(value)); }
        internal void Rename(string value) {
            ValidateGroupName(value);ulong revision=0;if(NameStamp.Length==64&&!ulong.TryParse(NameStamp.AsSpan(8,16),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out revision))throw new InvalidDataException();
            if(revision==ulong.MaxValue)throw new InvalidOperationException();Name=value.Trim();NameStamp="4752504E"+(revision+1).ToString("X16")+Guid.NewGuid().ToString("N").ToUpperInvariant()+"00000000";
        }
        internal static RelayMembership Parse(string code) {
            code=(code??"").Trim();
            if(code.Length!=80||!code.StartsWith("MM1-",StringComparison.Ordinal)||code[36]!='-')throw new ArgumentException("Invalid group invite code.",nameof(code));
            string id=code.Substring(4,32),key=code.Substring(37).Replace('-','+').Replace('_','/')+"=";
            if(!id.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f')||Convert.FromBase64String(key).Length!=32)throw new ArgumentException("Invalid group key or identifier.",nameof(code));
            var result=new RelayMembership { Id=id,Key=key,Owner="",Pending="",Name="" };
            if(result.Code!=code)throw new ArgumentException("Non-canonical group invite code.",nameof(code));return result;
        }
    }
    internal sealed class RelayGroupStore:IDisposable {
        readonly object gate=new object(),operations=new object();readonly string file,address,certificate;readonly int port;
        readonly Random retryRandom=new Random();readonly Action<string,byte[]> persist;readonly Timer maintenance;
        int nameRevision,savedNameRevision;bool nameSaveWorker;DateTime nextNameSave;volatile string persistenceError;
        internal string PersistenceError { get { return persistenceError; } }
        readonly List<RelayMembership> groups=new List<RelayMembership>();bool disposed;
        readonly CancellationTokenSource stopCts=new CancellationTokenSource();
        int disposeGuard;
        internal string DisplayName;
        internal string MemberColorSeed;
        internal void SetMemberColor(string seed) {
            if(seed==null||seed.Length!=8||!seed.All(Uri.IsHexDigit))throw new ArgumentException("Invalid member color seed.",nameof(seed));
            lock(operations) {
                lock(gate)ObjectDisposedException.ThrowIf(disposed, this);
                try { persist(Path.Combine(Path.GetDirectoryName(file),"private-member-color.dat"),Protect(Encoding.UTF8.GetBytes(seed.ToLowerInvariant()),true)); }
                catch(Exception error) { throw new RelayStorageException(error); }
                lock(gate) {
                    MemberColorSeed=seed.ToLowerInvariant();
                    // A colour choice must not override Leave or host Disconnect.
                    foreach(var group in groups.Where(g=>g.AutoConnect&&g.Pending==""&&g.Session!=null&&!g.Session.ReconnectBlocked))OpenSession(group);
                }
            }
        }
        internal RelayGroupStore(string directory,string address=null,int port=0,string certificate=null,Action<string,byte[]> persist=null) {
            this.persist=persist??WriteProtectedFile;
            file=Path.Combine(directory,"private-groups.dat");this.address=address??DirectPositions.RelayAddress;this.port=port==0?DirectPositions.RelayPort:port;this.certificate=certificate??DirectPositions.RelayCertificate;
            string colorFile=Path.Combine(directory,"private-member-color.dat");
            if(File.Exists(colorFile)) {
                if(new FileInfo(colorFile).Length>4096)throw new InvalidDataException();
                MemberColorSeed=Encoding.UTF8.GetString(Protect(File.ReadAllBytes(colorFile),false));
                if(MemberColorSeed.Length!=8||!MemberColorSeed.All(Uri.IsHexDigit))throw new InvalidDataException();
            }else {
                MemberColorSeed=Guid.NewGuid().ToString("N").Substring(0,8);
                WriteProtectedFile(colorFile,Protect(Encoding.UTF8.GetBytes(MemberColorSeed),true));
            }
            DisplayName=Localization.Get("PrivateGroupSurvivor")+" "+Guid.NewGuid().ToString("N").Substring(0,4);
            if(!File.Exists(file)) { maintenance=new Timer(Maintain,null,500,500);return; }
            if(new FileInfo(file).Length>65536)throw new InvalidDataException();
            var lines=Encoding.UTF8.GetString(Protect(File.ReadAllBytes(file),false)).Split('\n');
            if(lines.Length<2||lines[0]!="1"&&lines[0]!="2"&&lines[0]!="3")throw new InvalidDataException();bool version3=lines[0]=="3",version2=lines[0]!="1";DisplayName=Text(lines[1]);ValidateName(DisplayName);
            foreach(string line in lines.Skip(2).Where(value=>value.Length>0)) {
                var fields=line.Split('\t');if(fields.Length!=(version3?8:version2?6:5))throw new InvalidDataException();var group=RelayMembership.Parse(fields[0]);
                group.Owner=fields[1];group.Pending=fields[2];group.Name=Text(fields[3]);
                group.NameStamp=version2?fields[4]:"";
                if(version3) { if(fields[6].Length!=64||!fields[6].All(Uri.IsHexDigit)||fields[7]!="0"&&fields[7]!="1")throw new InvalidDataException();group.MemberToken=fields[6];group.AutoConnect=fields[7]=="1"; }
                if(group.Name.Length>48||group.Name.Any(char.IsControl)||group.NameStamp!=""&&(group.NameStamp.Length!=64||!group.NameStamp.All(Uri.IsHexDigit)||!group.NameStamp.StartsWith("4752504E",StringComparison.Ordinal)))throw new InvalidDataException();
                if(fields[version2?5:4]!="1"||group.Owner.Length!=0&&(group.Owner.Length!=64||!group.Owner.All(Uri.IsHexDigit))||group.Pending!=""&&group.Pending!="CREATE"&&group.Pending!="ROTATE"&&group.Pending!="DELETE"||groups.Any(g=>g.Id==group.Id)||groups.Count>=16)throw new InvalidDataException();groups.Add(group);
            }
            foreach(var group in groups.Where(g=>g.AutoConnect&&g.Pending==""))OpenSession(group);
            maintenance=new Timer(Maintain,null,500,500);
        }
        static string Text(string value) { return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(value)); }
        static string Base(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value??"")); }
        static readonly byte[] GroupEntropy=Encoding.UTF8.GetBytes("SCUM MiniMap private groups v1");
        static byte[] Protect(byte[] bytes,bool encrypt) {
            return encrypt
                ? ProtectedData.Protect(bytes,GroupEntropy,DataProtectionScope.CurrentUser)
                : ProtectedData.Unprotect(bytes,GroupEntropy,DataProtectionScope.CurrentUser);
        }
        static void ValidateName(string name) { if(string.IsNullOrWhiteSpace(name)||name.Length>32||name.Any(char.IsControl))throw new ArgumentException("Invalid display name.",nameof(name)); }
        void Save() { PersistGroups(null); }
        void PersistGroups(RelayMembership excluded) {
            string text;int revision;lock(gate) { text="3\n"+Base(DisplayName)+"\n"+string.Join("\n",groups.Where(g=>g!=excluded).Select(g=>g.Code+"\t"+g.Owner+"\t"+g.Pending+"\t"+Base(g.Name)+"\t"+g.NameStamp+"\t1\t"+g.MemberToken+"\t"+(g.AutoConnect?"1":"0")));revision=nameRevision; }
            try {
                byte[] plain=Encoding.UTF8.GetBytes(text);byte[] encrypted;try { encrypted=Protect(plain,true); }finally { Array.Clear(plain,0,plain.Length); }
                persist(file,encrypted);lock(gate)savedNameRevision=Math.Max(savedNameRevision,revision);persistenceError=null;
            }catch(Exception error) { throw new RelayStorageException(error); }
        }
        static void WriteProtectedFile(string file,byte[] encrypted) {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            bool moved=false;
            try {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    stream.Write(encrypted,0,encrypted.Length);stream.Flush(true);
                }
                if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
                moved=true;
            } finally {
                if(!moved) try { if(File.Exists(temp))File.Delete(temp); } catch {}
            }
        }
        internal RelayMembership[] Groups { get { lock(gate)return groups.ToArray(); } }
        internal sealed class View {
            internal readonly RelayMembership Member;internal readonly string Id,Name,Pending,Label,Code;internal readonly bool IsOwner;internal readonly DirectPositions Session;
            internal View(RelayMembership group) { Member=group;Id=group.Id;Name=group.Name;Pending=group.Pending;Label=group.Label;Code=group.Code;IsOwner=group.IsOwner;Session=group.Session; }
        }
        internal View[] GroupViews { get { lock(gate)return groups.Select(g=>new View(g)).ToArray(); } }
        internal bool Connected(RelayMembership group) { lock(gate)return group.Session!=null&&group.Session.Error==null; }
        internal DirectPositions[] Sessions { get { lock(gate)return groups.Where(g=>g.Session!=null).Select(g=>g.Session).ToArray(); } }
        internal void SetName(string name) { ValidateName(name);lock(operations) { string previous;lock(gate) { ObjectDisposedException.ThrowIf(disposed, this);previous=DisplayName;DisplayName=name.Trim(); }try { Save(); }catch { lock(gate)DisplayName=previous;throw; } } }
        // Local housekeeping only: never sends an owner DELETE to the relay.
        // Persist the proposed list before removing anything; keep UI reads nonblocking during I/O.
        internal void Forget(RelayMembership group) {
            lock(operations) {
                lock(gate) {
                    if(disposed||!groups.Contains(group)||group.Pending!="")throw new InvalidOperationException();
                }
                PersistGroups(group);
                lock(gate) {
                    groups.Remove(group);
                    if(group.Session!=null)group.Session.Dispose();group.Session=null;
                }
                CloseMemberManagement(group);
            }
        }
        internal RelayMembership Create(string name="") {
            var group=new RelayMembership { Id=Guid.NewGuid().ToString("N"),Key=PrivatePositions.NewKey(),Owner=Convert.ToHexStringLower(Convert.FromBase64String(PrivatePositions.NewKey())),Pending="CREATE",Name="",AutoConnect=true };
            if(!string.IsNullOrWhiteSpace(name))group.Rename(name);
            lock(operations) { lock(gate) { ObjectDisposedException.ThrowIf(disposed, this);if(groups.Count>=16)throw new RelayGroupLimitException();groups.Add(group); }Save();Complete(group);return group; }
        }
        internal RelayMembership Join(string code) {
            var parsed=RelayMembership.Parse(code);RelayMembership group;
            lock(operations) { lock(gate) {
                ObjectDisposedException.ThrowIf(disposed, this);group=groups.FirstOrDefault(g=>g.Id==parsed.Id);
                if(group==null) { if(groups.Count>=16)throw new RelayGroupLimitException();groups.Add(parsed);group=parsed; }
                else if(group.Key!=parsed.Key) { if(group.Pending!="")throw new InvalidOperationException();if(group.Session!=null)group.Session.Dispose();group.Session=null;group.Key=parsed.Key; }
            }Save();Connect(group);return group; }
        }
        internal void Connect(RelayMembership group) {
            lock(operations) {
                bool previous;lock(gate) { if(disposed||!groups.Contains(group))throw new InvalidOperationException();previous=group.AutoConnect;group.AutoConnect=true;group.RetryCount=0; }
                try { Save(); }catch { lock(gate)group.AutoConnect=previous;throw; }if(group.Pending!="") { Complete(group);return; }
                lock(gate)OpenSession(group);
            }
        }
        void OpenSession(RelayMembership group) {
            if(group.Session!=null)group.Session.Dispose();
            group.Session=new DirectPositions(false,address,port,group.Key,DisplayName,false,null,true,certificate,group.Id,MemberColorSeed,group.MemberToken,group.Owner,true);
            group.RetryAt=DateTime.MaxValue;group.NextRosterAt=DateTime.MinValue;
        }
        internal void Leave(RelayMembership group) {
            lock(operations) {
                bool previous;lock(gate) { if(disposed||!groups.Contains(group))throw new InvalidOperationException();previous=group.AutoConnect;group.AutoConnect=false; }
                try { Save(); }catch { lock(gate)group.AutoConnect=previous;throw; }
                lock(gate) { if(group.Session!=null)group.Session.Dispose();group.Session=null; }
                CloseMemberManagement(group);
            }
        }
        void Maintain(object unused) {
            if(!Monitor.TryEnter(operations))return;
            try {
                foreach(var group in Groups) {
                    lock(gate) {
                        if(disposed)return;if(!group.AutoConnect||group.Pending!="")continue;
                        if(group.Session!=null&&group.Session.Error!=null&&!group.Session.ReconnectBlocked&&group.RetryAt==DateTime.MaxValue) {
                            double seconds=Math.Min(30,Math.Pow(2,Math.Min(group.RetryCount++,5)));group.RetryAt=DateTime.UtcNow.AddSeconds(seconds+retryRandom.NextDouble());
                        }
                        if(group.Session==null||group.Session.Error!=null&&!group.Session.ReconnectBlocked&&DateTime.UtcNow>=group.RetryAt)OpenSession(group);
                        if(!group.Session.Ready)continue;group.RetryCount=0;
                        if(DateTime.UtcNow<group.NextRosterAt)continue;group.NextRosterAt=DateTime.UtcNow.AddSeconds(5);
                    }
                    try { var roster=MemberControl(group,"ROSTER",new string('0',64));lock(gate)if(!disposed)group.Roster=roster.Items; }
                    catch { /* Keep known names offline while the relay is unavailable. */ }
                }
            }finally { Monitor.Exit(operations); }
        }
        internal void Rename(RelayMembership group,string name) { lock(operations) { string previous,stamp;lock(gate) { if(disposed||!groups.Contains(group))throw new InvalidOperationException();previous=group.Name;stamp=group.NameStamp;group.Rename(name); }try { Save(); }catch { lock(gate) { group.Name=previous;group.NameStamp=stamp; }throw; } } }
        internal void SynchronizeNames() {
            lock(gate) {
                bool changed=false;
                for(int gi=0;gi<groups.Count;gi++) {
                    var group=groups[gi];
                    if(group.Session==null) continue;
                    var snap=group.Session.SnapshotMarkers();
                    SharedMapMarker latest=null;
                    for(int mi=0;mi<snap.Length;mi++) {
                        var m=snap[mi];
                        if(RelayMembership.IsNameMarker(m)&&m.Name!=null&&m.Name.Length<=48&&(latest==null||string.CompareOrdinal(m.Id,latest.Id)>0)) latest=m;
                    }
                    if(latest!=null&&string.CompareOrdinal(latest.Id,group.NameStamp)>0) { group.Name=latest.Name;group.NameStamp=latest.Id;changed=true; }
                }
                if(changed)nameRevision++;
                if(nameRevision!=savedNameRevision&&!nameSaveWorker&&DateTime.UtcNow>=nextNameSave) { nameSaveWorker=true;System.Threading.Tasks.Task.Run((Action)SaveSynchronizedNames); }
            }
        }
        void SaveSynchronizedNames() {
            try {
                while(true) {
                    int revision;lock(operations) { lock(gate) { if(disposed) { nameSaveWorker=false;return; }revision=nameRevision; }Save(); }
                    lock(gate)if(revision==nameRevision) { nameSaveWorker=false;return; }
                }
            }catch(Exception error) { lock(gate) { persistenceError=error.Message;nextNameSave=DateTime.UtcNow.AddSeconds(2);nameSaveWorker=false; } }
        }
        internal SharedMapMarker[] MarkersFor(DirectPositions session,SharedMapMarker[] markers) {
            lock(gate) {
                RelayMembership group=null;
                for(int gi=0;gi<groups.Count;gi++) { if(groups[gi].Session==session) { group=groups[gi]; break; } }
                if(group==null||group.NameStamp=="")return markers;
                int copyCount=markers==null?0:Math.Min(127,markers.Length);
                var arr=new SharedMapMarker[1+copyCount];
                arr[0]=new SharedMapMarker { Id=group.NameStamp,Name=group.Name,X=0,Y=0 };
                if(copyCount>0) Array.Copy(markers,0,arr,1,copyCount);
                return arr;
            }
        }
        internal void Rotate(RelayMembership group) {
            lock(operations) { lock(gate) {
                if(disposed||!groups.Contains(group)||!group.IsOwner||group.Pending=="DELETE")throw new InvalidOperationException();
                if(group.Pending=="") { if(group.Session!=null)group.Session.Dispose();group.Session=null;group.Key=PrivatePositions.NewKey();group.Pending="ROTATE"; }
            }Save();Complete(group); }
        }
        internal void Delete(RelayMembership group) {
            lock(operations) { lock(gate) {
                if(disposed||!groups.Contains(group)||!group.IsOwner||group.Pending!=""&&group.Pending!="DELETE")throw new InvalidOperationException();
                CloseMemberManagement(group);
                if(group.Session!=null)group.Session.Dispose();group.Session=null;group.Pending="DELETE";
            }Save();Complete(group); }
        }
        void Complete(RelayMembership group) {
            // A retry must durably journal its key and owner capability as well,
            // even if an earlier local save failed before any network request.
            lock(gate) { if(disposed||!groups.Contains(group)||!group.IsOwner||group.Pending=="")throw new InvalidOperationException(); }Save();
            string action=group.Pending;
            byte[] keyBytes=Convert.FromBase64String(group.Key);
            string roomId; try { roomId=DirectPositions.RelayRoom(keyBytes); } finally { Array.Clear(keyBytes,0,keyBytes.Length); }
            Manage(action,group.Id,roomId,group.Owner);
            if(action=="DELETE") { int index;lock(gate) { index=groups.IndexOf(group);groups.Remove(group); }try { Save(); }catch { lock(gate)if(index>=0)groups.Insert(index,group);throw; }return; }
            lock(gate) { if(disposed)return;group.Pending=""; }Save();
            Connect(group);
        }
        void Manage(string action,string id,string room,string owner) {
            using(var client=new System.Net.Sockets.TcpClient()) {
                DirectPositions.ConnectWithTimeout(client,address,port,5000,stopCts.Token);client.ReceiveTimeout=client.SendTimeout=5000;
                using(var stream=new System.Net.Security.SslStream(client.GetStream(),false,(sender,cert,chain,errors)=> {
                    if(cert==null)return false;return string.Equals(Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())),certificate,StringComparison.OrdinalIgnoreCase);
                })) {
                    stream.AuthenticateAsClient("minimap-relay",null,(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13),false);
                    byte[] request=Encoding.ASCII.GetBytes("SCUM"+action+"1 "+id+" "+room+" "+owner+"\n");stream.Write(request,0,request.Length);Array.Clear(request,0,request.Length);
                    if(stream.ReadByte()!='O'||stream.ReadByte()!='K'||stream.ReadByte()!='\n')throw new IOException("Group operation rejected");
                }
            }
        }
        internal sealed class Member {
            internal string Id,Name,PeerId;internal bool IsOwner,Online=true;
        }
        internal sealed class Members {
            internal Member[] Items;
        }
        sealed class MemberLink:IDisposable {
            internal readonly System.Net.Sockets.TcpClient Client=new System.Net.Sockets.TcpClient();internal System.Net.Security.SslStream Stream;
            public void Dispose() { Client.Close();if(Stream!=null)Stream.Dispose(); }
        }
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,MemberLink> memberLinks=new System.Collections.Concurrent.ConcurrentDictionary<string,MemberLink>();
        internal void CloseMemberManagement(RelayMembership group) { MemberLink link;if(memberLinks.TryRemove(group.Id,out link))link.Dispose(); }
        internal Members ReadMembers(RelayMembership group) {
            lock(gate)if(disposed||!groups.Contains(group)||!group.IsOwner)throw new InvalidOperationException();
            var saved=MemberControl(group,"ROSTER",new string('0',64));var live=MemberControl(group,"LIST",new string('0',64));return new Members { Items=saved.Items.Concat(live.Items.Where(m=>!saved.Items.Any(previous=>previous.Id==m.Id))).ToArray() };
        }
        internal void DisconnectMember(RelayMembership group,string id) { MemberControl(group,"KICK",id); }
        Members MemberControl(RelayMembership group,string action,string id) {
            lock(operations) {
                lock(gate)if(disposed||!groups.Contains(group)||action!="ROSTER"&&!group.IsOwner||group.Pending!=""||id==null||id.Length!=64||!id.All(Uri.IsHexDigit))throw new InvalidOperationException();
                try {
                    MemberLink link;
                    if(!memberLinks.TryGetValue(group.Id,out link)) {
                        link=new MemberLink();
                        try {
                            lock(gate) { ObjectDisposedException.ThrowIf(disposed, this);memberLinks[group.Id]=link; }
                            DirectPositions.ConnectWithTimeout(link.Client,address,port,5000,stopCts.Token);link.Client.ReceiveTimeout=link.Client.SendTimeout=5000;
                            link.Stream=new System.Net.Security.SslStream(link.Client.GetStream(),false,(sender,cert,chain,errors)=> {
                                if(cert==null)return false;return string.Equals(Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())),certificate,StringComparison.OrdinalIgnoreCase);
                            });
                            link.Stream.AuthenticateAsClient("minimap-relay",null,(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13),false);
                            lock(gate) { MemberLink current;ObjectDisposedException.ThrowIf(disposed||!memberLinks.TryGetValue(group.Id,out current)||current!=link,"Member management"); }
                        }catch { MemberLink stale; if(memberLinks.TryGetValue(group.Id,out stale)&&object.ReferenceEquals(stale,link)) memberLinks.TryRemove(group.Id,out stale); link.Dispose();throw; }
                    }
                    var stream=link.Stream;
                        byte[] request=Encoding.ASCII.GetBytes((action=="ROSTER"?"SCUMROSTER3 "+group.Id+" "+DirectPositions.RelayRoom(Convert.FromBase64String(group.Key)):"SCUMMEMBERS2 "+action+" "+group.Id+" "+DirectPositions.RelayRoom(Convert.FromBase64String(group.Key))+" "+group.Owner+" "+id.ToLowerInvariant())+"\n");stream.Write(request,0,request.Length);Array.Clear(request,0,request.Length);
                        string response=ReadLine(stream);
                        if(action!="LIST"&&action!="ROSTER") { if(response!="OK")throw new IOException();return null; }
                        if(response!="OK")throw new IOException();var members=new List<Member>();
                        while(true) {
                            string row=ReadLine(stream);if(row=="END")break;if(members.Count>=(action=="ROSTER"?64:9))throw new InvalidDataException();var parts=row.Split(' ');
                            bool roster=action=="ROSTER";if(parts.Length!=(roster?4:3)||roster&&parts[2]!="0"&&parts[2]!="1"||parts[0].Length!=64||!parts[0].All(Uri.IsHexDigit)||(parts[1]!="0"&&parts[1]!="1"))throw new InvalidDataException();
                            string label=Localization.Get("GroupOlderClient")+" · "+parts[0].Substring(0,8);
                            string encrypted=parts[roster?3:2],peerId=null;
                            if(encrypted!="-")try {
                                byte[] openKey=Convert.FromBase64String(group.Key);
                                string decoded; try { decoded=PrivatePositions.Open(openKey,Convert.FromBase64String(encrypted)); } finally { Array.Clear(openKey,0,openKey.Length); }
                                if(decoded.StartsWith("member-name-v2:",StringComparison.Ordinal)) { peerId=decoded.Substring(15,32);if(peerId.Length!=32||!peerId.All(Uri.IsHexDigit)||decoded[47]!=':')throw new InvalidDataException();decoded=decoded.Substring(48); }
                                else if(decoded.StartsWith("member-name-v1:",StringComparison.Ordinal))decoded=decoded.Substring(15);else throw new InvalidDataException();
                                ValidateName(decoded);label=decoded;
                            }catch { label=Localization.Get("PrivateGroupSurvivor")+" · "+parts[0].Substring(0,8);peerId=null; }
                            members.Add(new Member { Id=parts[0],Name=label,PeerId=peerId,IsOwner=parts[1]=="1",Online=!roster||parts[2]=="1" });
                        }
                        return new Members { Items=members.ToArray() };
                }catch { CloseMemberManagement(group);throw; }
            }
        }
        static string ReadLine(Stream stream) { var text=new StringBuilder();for(int i=0;i<1024;i++) { int value=stream.ReadByte();if(value<0)throw new EndOfStreamException();if(value==10)return text.ToString();if(value<32||value>126)throw new InvalidDataException();text.Append((char)value); }throw new InvalidDataException(); }
        static bool IsOfflineMember(Member[] roster,string peerId) {
            if(roster==null||peerId==null) return false;
            for(int i=0;i<roster.Length;i++) { if(roster[i].PeerId==peerId&&!roster[i].Online) return true; }
            return false;
        }
        internal PrivatePositions.Peer[] Roster(bool positionsOnly=false) {
            lock(gate) {
                if(groups.Count==0) return Array.Empty<PrivatePositions.Peer>();
                List<PrivatePositions.Peer> result=null;
                for(int gi=0;gi<groups.Count;gi++) {
                    var g=groups[gi];
                    if(g.Session==null) continue;
                    var rawLive=positionsOnly?g.Session.Snapshot():g.Session.Roster();
                    var roster=g.Roster;
                    string grpLabel=g.Label;
                    for(int pi=0;pi<rawLive.Length;pi++) {
                        var p=rawLive[pi];
                        if(IsOfflineMember(roster,p.Id)) continue;
                        if(result==null) result=new List<PrivatePositions.Peer>(8);
                        result.Add(new PrivatePositions.Peer { Id=g.Id+":"+StableId(g,p.Id),Name=p.Name+" · "+grpLabel,Position=p.Position,Updated=p.Updated,Online=p.Online });
                    }
                    if(!positionsOnly&&roster!=null&&roster.Length>0) {
                        for(int mi=0;mi<roster.Length;mi++) {
                            var m=roster[mi];
                            if(m.Id==g.MemberToken||m.PeerId==null) continue;
                            bool alreadyLive=false;
                            for(int li=0;li<rawLive.Length;li++) { if(rawLive[li].Id==m.PeerId&&!IsOfflineMember(roster,rawLive[li].Id)) { alreadyLive=true; break; } }
                            if(alreadyLive) continue;
                            if(result==null) result=new List<PrivatePositions.Peer>(8);
                            result.Add(new PrivatePositions.Peer { Id=g.Id+":"+StableId(g,m.PeerId),Name=m.Name+" · "+grpLabel,Position=null,Updated=DateTime.MinValue,Online=m.Online&&g.Session.Ready });
                        }
                    }
                }
                return result==null||result.Count==0?Array.Empty<PrivatePositions.Peer>():result.ToArray();
            }
        }
        static string StableId(RelayMembership group,string peer) {
            var roster=group.Roster;
            if(roster!=null) {
                if(peer!=null&&peer.Length>=8) { for(int i=0;i<roster.Length;i++) { if(roster[i].PeerId==peer&&roster[i].Id!=null&&roster[i].Id.Length>=32) return peer.Substring(0,8)+roster[i].Id.Substring(8,24); } }
            }
            return peer;
        }
        internal SharedMapMarker[] Markers() {
            lock(gate) {
                if(groups.Count==0) return Array.Empty<SharedMapMarker>();
                List<SharedMapMarker> result=null;
                for(int gi=0;gi<groups.Count;gi++) {
                    var g=groups[gi];
                    if(g.Session==null) continue;
                    var snap=g.Session.SnapshotMarkers();
                    if(snap==null||snap.Length==0) continue;
                    var roster=g.Roster;
                    string grpLabel=g.Label;
                    for(int mi=0;mi<snap.Length;mi++) {
                        var m=snap[mi];
                        if(RelayMembership.IsNameMarker(m)||IsOfflineMember(roster,m.OwnerId)) continue;
                        if(result==null) result=new List<SharedMapMarker>(16);
                        result.Add(new SharedMapMarker { Id=m.Id,Name=m.Name,Owner=m.Owner+" · "+grpLabel,OwnerId=g.Id+":"+StableId(g,m.OwnerId),X=m.X,Y=m.Y,Death=m.Death });
                    }
                }
                return result==null||result.Count==0?Array.Empty<SharedMapMarker>():result.ToArray();
            }
        }
        public void Dispose() {
            if(Interlocked.Exchange(ref disposeGuard,1)!=0) return;
            try { stopCts.Cancel(); } catch(ObjectDisposedException) {}
            lock(gate) {
                disposed=true;maintenance.Dispose();
                foreach(var group in groups)if(group.Session!=null) { group.Session.Dispose();group.Session=null; }
                foreach(var link in memberLinks.Values)link.Dispose();memberLinks.Clear();
            }
            stopCts.Dispose();
        }
    }
}
