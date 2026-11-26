using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
namespace ZiloreMiniMap {
    internal static class PublicPositionAddress {
        internal static string Parse(string text) {
            if(text==null||text.Length>64)return null;string value=text.Trim();System.Net.IPAddress address;
            if(!System.Net.IPAddress.TryParse(value,out address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||address.ToString()!=value)return null;
            byte[] b=address.GetAddressBytes();
            if(b[0]==0||b[0]==10||b[0]==127||b[0]>=224||(b[0]==100&&b[1]>=64&&b[1]<=127)||(b[0]==169&&b[1]==254)||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168)||(b[0]==192&&b[1]==0&&(b[2]==0||b[2]==2))||(b[0]==198&&(b[1]==18||b[1]==19||(b[1]==51&&b[2]==100)))||(b[0]==203&&b[1]==0&&b[2]==113))return null;
            return value;
        }
        internal static string Lookup() {
            return RouterAddressDiscovery.Find(PeerRouter.Gateways(),gateway=>PeerRouter.PublicAddress(PeerRouter.Exchange(gateway,new byte[]{0,0})),RouterAddressDiscovery.Upnp);
        }
    }
    // RFC 6886: only the local default gateway is contacted. No discovery service,
    // account, relay or public HTTP endpoint is involved. Leases expire after ten
    // minutes if the app crashes; normal shutdown requests deletion immediately.
    internal sealed class PeerRouter:IDisposable {
        static readonly object lifecycle=new object();static System.Threading.Tasks.Task cleanup=System.Threading.Tasks.Task.FromResult(0);
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int,PeerRouter> owners=new System.Collections.Concurrent.ConcurrentDictionary<int,PeerRouter>();
        readonly object gate=new object();readonly System.Net.IPAddress gateway;readonly int port;readonly Timer renewal;readonly Func<System.Net.IPAddress,byte[],byte[]> exchange;
        System.Threading.Tasks.TaskCompletionSource<bool> renewalFinished;
        bool stopped;volatile bool ready;volatile string address;internal bool Ready { get { return ready; }private set { ready=value; } }internal string Address { get { return address; }private set { address=value; } }
        internal static System.Net.IPAddress[] Gateways() {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n=>n.GetIPProperties().GatewayAddresses.Select(g=>g.Address))
                .Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!System.Net.IPAddress.IsLoopback(a)&&!a.Equals(System.Net.IPAddress.Any)).Distinct().Take(4).ToArray();
        }
        internal static byte[] Exchange(System.Net.IPAddress gateway,byte[] request) {
            using(var client=new System.Net.Sockets.UdpClient()) {
                client.Connect(gateway,5351);client.Client.ReceiveTimeout=750;
                for(int attempt=0;attempt<3;attempt++)try {
                    client.Send(request,request.Length);var endpoint=new System.Net.IPEndPoint(gateway,5351);return client.Receive(ref endpoint);
                }catch(System.Net.Sockets.SocketException) { if(attempt==2)throw; }
            }
            throw new IOException();
        }
        internal static string PublicAddress(byte[] reply) {
            if(reply==null||reply.Length!=12||reply[0]!=0||reply[1]!=128||reply[2]!=0||reply[3]!=0)throw new InvalidDataException();
            var value=PublicPositionAddress.Parse(new System.Net.IPAddress(reply.Skip(8).Take(4).ToArray()).ToString());
            if(value==null)throw new InvalidDataException();return value;
        }
        internal static byte[] Request(int port,bool remove) {
            if(port<1024||port>65535)throw new ArgumentOutOfRangeException(nameof(port));
            return new byte[]{0,2,0,0,(byte)(port>>8),(byte)port,remove?(byte)0:(byte)(port>>8),remove?(byte)0:(byte)port,0,0,remove?(byte)0:(byte)2,remove?(byte)0:(byte)88};
        }
        internal static bool MappingAccepted(byte[] reply,int port) {
            return reply!=null&&reply.Length==16&&reply[0]==0&&reply[1]==130&&reply[2]==0&&reply[3]==0&&
                ((reply[8]<<8)|reply[9])==port&&((reply[10]<<8)|reply[11])==port&&
                reply[12]==0&&reply[13]==0&&((reply[14]<<8)|reply[15])>=600;
        }
        PeerRouter(System.Net.IPAddress gateway,int port,string address,Func<System.Net.IPAddress,byte[],byte[]> exchange) {
            this.gateway=gateway;this.port=port;this.exchange=exchange;Address=address;Ready=true;renewal=new Timer(Renew,null,TimeSpan.FromSeconds(240),TimeSpan.FromSeconds(240));
        }
        internal static System.Threading.Tasks.Task<PeerRouter> OpenAsync(int port) {
            return OpenAsync(port,Gateways(),Exchange);
        }
        internal static System.Threading.Tasks.Task<PeerRouter> OpenAsync(int port,System.Net.IPAddress[] gateways,Func<System.Net.IPAddress,byte[],byte[]> exchange) {
            // Finish an older host's deletion before a rapid restart can create
            // a replacement lease for the same internal port.
            System.Threading.Tasks.Task<PeerRouter> opening;
            lock(lifecycle) {
                opening=cleanup.ContinueWith(previous=>OpenCore(port,gateways,exchange),System.Threading.Tasks.TaskScheduler.Default);
                cleanup=opening.ContinueWith(previous=> { },System.Threading.Tasks.TaskScheduler.Default);
            }
            return opening;
        }
        internal static PeerRouter Open(int port) {
            return OpenAsync(port,Gateways(),Exchange).GetAwaiter().GetResult();
        }
        internal static PeerRouter Open(int port,System.Net.IPAddress[] gateways,Func<System.Net.IPAddress,byte[],byte[]> exchange) {
            return OpenAsync(port,gateways,exchange).GetAwaiter().GetResult();
        }
        static PeerRouter OpenCore(int port,System.Net.IPAddress[] gateways,Func<System.Net.IPAddress,byte[],byte[]> exchange) {
            foreach(var gateway in gateways)try {
                string address=PublicAddress(exchange(gateway,new byte[]{0,0}));
                byte[] reply=exchange(gateway,Request(port,false));
                if(MappingAccepted(reply,port)) { var mapping=new PeerRouter(gateway,port,address,exchange);owners[port]=mapping;return mapping; }
                // A router may assign another external port. Never advertise the
                // requested port as successful; withdraw this internal-port lease.
                if(reply!=null&&reply.Length==16&&reply[0]==0&&reply[1]==130&&reply[2]==0&&reply[3]==0&&((reply[8]<<8)|reply[9])==port)exchange(gateway,Request(port,true));
            }catch(Exception) {}
            return null;
        }
        void Renew(object unused) {
            System.Threading.Tasks.TaskCompletionSource<bool> finished;
            lock(gate) { if(stopped||renewalFinished!=null&&!renewalFinished.Task.IsCompleted)return;renewalFinished=finished=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously); }
            try {
                string value=PublicAddress(exchange(gateway,new byte[]{0,0}));bool accepted=MappingAccepted(exchange(gateway,Request(port,false)),port);
                lock(gate)if(!stopped) { Address=value;Ready=accepted; }
            }catch(Exception) { lock(gate)if(!stopped)Ready=false; }
            finally { finished.TrySetResult(true); }
        }
        public void Dispose() {
            System.Threading.Tasks.Task renewing;
            lock(gate) { if(stopped)return;stopped=true;Ready=false;renewal.Dispose();renewing=renewalFinished==null?System.Threading.Tasks.Task.FromResult(true):renewalFinished.Task; }
            lock(lifecycle)cleanup=System.Threading.Tasks.Task.WhenAll(cleanup,renewing).ContinueWith(previous=> {
                // A stopped session may finish discovery after a replacement
                // host starts. It must not remove the replacement host's lease.
                PeerRouter owner;if(!owners.TryGetValue(port,out owner)||owner!=this)return;
                try { exchange(gateway,Request(port,true)); }catch(Exception) {}
            },System.Threading.Tasks.TaskScheduler.Default);
        }
    }
    // Opt-in, session-only sharing over a user-controlled private folder.
    internal sealed class PrivatePositions:IDisposable {
        internal const int MaximumPacketSize=131072;
        internal sealed class Peer { internal bool Online=true;internal string Id,Name;internal Position Position;internal DateTime Updated; }
        readonly object gate=new object();readonly string folder,id=Guid.NewGuid().ToString("N"),name;
        readonly byte[] key,encKey,macKey;readonly Timer timer;Position local;DateTime updated;volatile bool stopped;
        volatile Peer[] peers=Array.Empty<Peer>();internal string Error { get;private set; }
        internal PrivatePositions(string folder,string secret,string name) {
            key=Convert.FromBase64String(secret.Trim());if(key.Length!=32)throw new ArgumentException("Use a generated group key.");DeriveKeys(key,out encKey,out macKey);
            this.folder=Path.GetFullPath(folder);if(!Directory.Exists(this.folder))throw new DirectoryNotFoundException();
            this.name=(name??"").Trim();if(this.name.Length<1||this.name.Length>32||this.name.Any(char.IsControl))throw new ArgumentException("Use a name of 1–32 characters.");
            timer=new Timer(Tick,null,0,2000);
        }
        internal static string NewKey() { var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return Convert.ToBase64String(bytes); }
        internal void Update(Position p,DateTime timestamp) { if(!Monitor.TryEnter(gate))return;try { local=p==null?null:new Position { X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw };updated=timestamp.ToUniversalTime(); }finally { Monitor.Exit(gate); } }
        internal Peer[] Snapshot() { if(stopped)return Array.Empty<Peer>();return peers.Where(p=>Fresh(p.Updated,DateTime.UtcNow)).ToArray(); }
        internal static bool Fresh(DateTime timestamp,DateTime now) { double age=(now-timestamp).TotalSeconds;return age>=-5&&age<=30; }
        static readonly byte[] EncPurposeBytes=Encoding.UTF8.GetBytes("positions encryption v1");
        static readonly byte[] MacPurposeBytes=Encoding.UTF8.GetBytes("positions authentication v1");
        internal static void DeriveKeys(byte[] masterKey,out byte[] encryptionKey,out byte[] authenticationKey) {
            encryptionKey=HMACSHA256.HashData(masterKey,EncPurposeBytes);
            authenticationKey=HMACSHA256.HashData(masterKey,MacPurposeBytes);
        }
        internal static byte[] Seal(byte[] key,string payload) {
            byte[] ek,mk; DeriveKeys(key,out ek,out mk);
            try { return SealWithDerived(ek,mk,payload); }
            finally { Array.Clear(ek,0,ek.Length); Array.Clear(mk,0,mk.Length); }
        }
        internal static byte[] SealWithDerived(byte[] encryptionKey,byte[] authenticationKey,string payload) {
            byte[] plain=Encoding.UTF8.GetBytes(payload);
            int cipherLen=((plain.Length/16)+1)*16;
            byte[] packet=new byte[16+cipherLen+32];
            RandomNumberGenerator.Fill(packet.AsSpan(0,16));
            using(var aes=Aes.Create()) {
                aes.Key=encryptionKey;
                int written=aes.EncryptCbc(plain,packet.AsSpan(0,16),packet.AsSpan(16,cipherLen),PaddingMode.PKCS7);
                HMACSHA256.HashData(authenticationKey,packet.AsSpan(0,16+written),packet.AsSpan(16+written,32));
                if(written!=cipherLen) {
                    byte[] trimmed=new byte[16+written+32];
                    Buffer.BlockCopy(packet,0,trimmed,0,trimmed.Length);
                    return trimmed;
                }
                return packet;
            }
        }
        internal static string Open(byte[] key,byte[] packet) {
            byte[] ek,mk; DeriveKeys(key,out ek,out mk);
            try { return OpenWithDerived(ek,mk,packet); }
            finally { Array.Clear(ek,0,ek.Length); Array.Clear(mk,0,mk.Length); }
        }
        internal static string OpenWithDerived(byte[] encryptionKey,byte[] authenticationKey,byte[] packet) {
            if(packet==null||packet.Length<64||packet.Length>MaximumPacketSize||(packet.Length-48)%16!=0)throw new InvalidDataException();
            int size=packet.Length-32;
            Span<byte> tag=stackalloc byte[32];
            HMACSHA256.HashData(authenticationKey,packet.AsSpan(0,size),tag);
            if(!CryptographicOperations.FixedTimeEquals(tag,packet.AsSpan(size,32)))throw new CryptographicException();
            using(var aes=Aes.Create()) {
                aes.Key=encryptionKey;
                byte[] plain=aes.DecryptCbc(packet.AsSpan(16,size-16),packet.AsSpan(0,16),PaddingMode.PKCS7);
                return Encoding.UTF8.GetString(plain);
            }
        }
        void Tick(object unused) {
            if(!Monitor.TryEnter(gate))return;
            try {
                if(stopped)return;var now=DateTime.UtcNow;string path=Path.Combine(folder,id+".scumpos");
                if(local!=null&&Fresh(updated,now)) {
                    string payload=string.Join("\t",new[]{"1",name,updated.Ticks.ToString(CultureInfo.InvariantCulture),local.X.ToString("R",CultureInfo.InvariantCulture),local.Y.ToString("R",CultureInfo.InvariantCulture),local.Z.ToString("R",CultureInfo.InvariantCulture),local.Yaw.ToString("R",CultureInfo.InvariantCulture)});
                    string temp=path+".tmp";File.WriteAllBytes(temp,SealWithDerived(encKey,macKey,payload));if(stopped) { File.Delete(temp);return; }if(File.Exists(path))File.Delete(path);File.Move(temp,path);
                }else if(File.Exists(path))File.Delete(path);
                var found=new System.Collections.Generic.List<Peer>();
                foreach(var file in Directory.EnumerateFiles(folder,"*.scumpos").Take(128)) {
                    if(Path.GetFileNameWithoutExtension(file)==id)continue;
                    try {
                        if(new FileInfo(file).Length>1024)continue;var fields=OpenWithDerived(encKey,macKey,File.ReadAllBytes(file)).Split('\t');
                        if(fields.Length!=7||fields[0]!="1"||fields[1].Length<1||fields[1].Length>32||fields[1].Any(char.IsControl))continue;
                        long ticks;if(!long.TryParse(fields[2],out ticks)||ticks<0||ticks>DateTime.MaxValue.Ticks)continue;
                        var stamp=new DateTime(ticks,DateTimeKind.Utc);if(!Fresh(stamp,now))continue;
                        var values=new double[4];bool valid=true;for(int i=0;i<4;i++)valid&=double.TryParse(fields[i+3],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])&&!double.IsNaN(values[i])&&!double.IsInfinity(values[i])&&Math.Abs(values[i])<=(i==3?360:2000000);
                        if(valid)found.Add(new Peer { Name=fields[1],Updated=stamp,Position=new Position { X=values[0],Y=values[1],Z=values[2],Yaw=values[3] } });
                    }catch(IOException) {}catch(CryptographicException) {}
                }
                peers=found.ToArray();Error=null;
            }catch(Exception error) { peers=Array.Empty<Peer>();Error=error.Message; }finally { Monitor.Exit(gate); }
        }
        public void Dispose() {
            stopped=true;peers=Array.Empty<Peer>();timer.Dispose();
            System.Threading.Tasks.Task.Run(()=> { lock(gate) { local=null;try { File.Delete(Path.Combine(folder,id+".scumpos")); }catch(IOException) {}catch(UnauthorizedAccessException) {}Array.Clear(key,0,key.Length);Array.Clear(encKey,0,encKey.Length);Array.Clear(macKey,0,macKey.Length); } });
        }
    }
}

namespace ZiloreMiniMap {
    internal sealed class SharedMapMarker {
        internal const string GpsId="FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
        internal string Id,Name,Owner,OwnerId;internal double X,Y;internal bool Death;
        internal static bool Valid(SharedMapMarker marker) {
            if(marker==null||marker.Id==null||marker.Id.Length!=64||string.IsNullOrWhiteSpace(marker.Name)||marker.Name.Length>96||double.IsNaN(marker.X)||double.IsNaN(marker.Y)||marker.X<0||marker.X>1||marker.Y<0||marker.Y>1) return false;
            string id=marker.Id; for(int i=0;i<64;i++) if(!Uri.IsHexDigit(id[i])) return false;
            string n=marker.Name; for(int i=0;i<n.Length;i++) if(char.IsControl(n[i])) return false;
            return true;
        }
        internal SharedMapMarker Copy() { return new SharedMapMarker { Id=Id,Name=Name,Owner=Owner,OwnerId=OwnerId,X=X,Y=Y,Death=Death }; }
    }
    // Direct TCP transport: a private group key authenticates and encrypts every
    // framed message. The host forwards only authenticated, fresh publications.
    internal sealed class DirectPositions:IDisposable {
        sealed class Publication { internal string Id;internal PrivatePositions.Peer Peer;internal long Sequence;internal DateTime Seen;internal SharedMapMarker[] Markers;internal bool Compact,IncludeMarkers=true; }
        sealed class Link { internal System.Net.Sockets.TcpClient Client;internal Stream Stream;internal string Id;internal readonly System.Collections.Generic.Dictionary<string,long> Sequences=new System.Collections.Generic.Dictionary<string,long>(); }
        readonly object gate=new object();readonly byte[] key,encKey,macKey;readonly string id=Guid.NewGuid().ToString("N"),name;
        readonly System.Collections.Generic.Dictionary<string,Publication> publications=new System.Collections.Generic.Dictionary<string,Publication>();
        readonly System.Collections.Generic.List<Link> links=new System.Collections.Generic.List<Link>();
        readonly System.Net.Sockets.TcpListener listener;readonly Timer timer;readonly bool host;
        SharedMapMarker[] localMarkers=Array.Empty<SharedMapMarker>();long markerRevision,lastMarkerRevision=-1;DateTime nextFullMarkers;bool compactUpdates;
        internal bool CompactUpdates { get { lock(gate)return compactUpdates; } }internal long SentBytes;
        Position local;DateTime updated;volatile bool stopped,ready;volatile string error;int ticking;PeerRouter router;volatile bool routerFinished;long sequence;readonly Func<DateTime> clock;
        internal bool RouterFinished { get { return routerFinished; } }
        internal bool RouterReady { get { lock(gate)return router!=null&&router.Ready; } }
        volatile bool firewallFinished,firewallReady;
        internal bool FirewallFinished { get { return firewallFinished; } }
        internal bool FirewallReady { get { return firewallReady; } }
        internal bool Ready { get { return ready&&!stopped; } }
        internal string Error { get { return error; } }
        internal bool Host { get { return host; } }
        internal const string RelayAddress="92.5.169.22";
        internal const int RelayPort=47832;
        internal const string RelayCertificate="53678a99647bc63a5f86791cff101a420a680a9dc22b7dfd0440b983a47f8099";
        internal bool Relay { get;private set; }
        readonly string relayCertificate,relayGroup,memberToken,ownerToken;readonly bool persistentMember;
        readonly CancellationTokenSource stopCts=new CancellationTokenSource();
        int disposeGuard;
        internal bool ReconnectBlocked { get;private set; }
        internal static void ConnectWithTimeout(System.Net.Sockets.TcpClient client,string host,int port,int timeoutMs,CancellationToken externalCancel=default) {
            externalCancel.ThrowIfCancellationRequested();
            using(var cts=externalCancel.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(externalCancel) : new CancellationTokenSource()) {
                cts.CancelAfter(timeoutMs);
                try {
                    client.ConnectAsync(host,port,cts.Token).AsTask().GetAwaiter().GetResult();
                } catch(OperationCanceledException) {
                    if(externalCancel.IsCancellationRequested) throw new OperationCanceledException(externalCancel);
                    throw new TimeoutException("Relay connection timed out.");
                }
            }
        }
        internal static string RelayRoom(byte[] secret) {
            return Convert.ToHexStringLower(HMACSHA256.HashData(secret,Encoding.UTF8.GetBytes("SCUM MiniMap opaque relay room v1")));
        }
        internal string LocalId { get { return id; } }
        internal string Name { get { return name; } }
        internal string Address { get;private set; }
        internal void SetShareAddress(string value) { if(host&&value!=null&&value.Length<=253)Address=value.Trim(); }
        internal int Port { get;private set; }
        internal int Connections { get { lock(gate)return host?links.Count(link=>link.Id!=null):Relay?(ready?publications.Values.Count(p=>p.Id!=id&&PrivatePositions.Fresh(p.Seen,clock())):0):(ready?links.Count:0); } }
        internal DirectPositions(bool host,string address,int port,string secret,string name,bool configureRouter=false,Func<DateTime> clock=null):this(host,address,port,secret,name,configureRouter,clock,false,null) {}
        internal DirectPositions(bool host,string address,int port,string secret,string name,bool configureRouter,Func<DateTime> clock,bool relay,string certificate=null):this(host,address,port,secret,name,configureRouter,clock,relay,certificate,null) {}
        internal DirectPositions(bool host,string address,int port,string secret,string name,bool configureRouter,Func<DateTime> clock,bool relay,string certificate,string group,string colorSeed=null,string memberToken=null,string ownerToken=null,bool persistentMember=false) {
            this.persistentMember=persistentMember;
            if(memberToken!=null&&(memberToken.Length!=64||!memberToken.All(Uri.IsHexDigit)))throw new ArgumentException("Invalid member token.",nameof(memberToken));this.memberToken=memberToken;this.ownerToken=ownerToken;
            if(colorSeed!=null) {
                if(colorSeed.Length!=8||!colorSeed.All(Uri.IsHexDigit))throw new ArgumentException("Invalid member color seed");
                // Retain a stable color prefix and fresh replay identity per connection.
                id=colorSeed.ToLowerInvariant()+Guid.NewGuid().ToString("N").Substring(8);
            }
            if(host&&relay)throw new ArgumentException("Host mode cannot use relay.",nameof(host));Relay=relay;relayCertificate=certificate??RelayCertificate;
            if(group!=null&&(group.Length!=32||!group.All(Uri.IsHexDigit)))throw new ArgumentException("Invalid relay group id.",nameof(group));relayGroup=group;
            key=Convert.FromBase64String(secret.Trim());if(key.Length!=32)throw new ArgumentException("Secret key must be 32 bytes.",nameof(secret));PrivatePositions.DeriveKeys(key,out encKey,out macKey);
            this.name=(name??"").Trim();if(this.name.Length<1||this.name.Length>32||this.name.Any(char.IsControl)||port<1024||port>65535)throw new ArgumentException("Invalid display name or port.",nameof(name));
            this.host=host;this.clock=clock??(()=>DateTime.UtcNow);
            Address=address;Port=port;
            if(host) { listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any,port);listener.Start(8);ready=true;System.Threading.Tasks.Task.Run((Action)Accept); }
            else { if(string.IsNullOrWhiteSpace(address)||address.Length>253)throw new ArgumentException("Invalid remote address.",nameof(address));System.Threading.Tasks.Task.Run(()=>Connect(address.Trim(),port)); }
            timer=new Timer(Tick,null,500,1000);
            if(host&&configureRouter)System.Threading.Tasks.Task.Run(()=> { firewallReady=PeerFirewall.Ensure((System.Environment.ProcessPath ?? ""),port);firewallFinished=true; });
            if(host&&configureRouter)System.Threading.Tasks.Task.Run(async ()=> {
                PeerRouter mapping=null;try { mapping=await PeerRouter.OpenAsync(port).ConfigureAwait(false); }catch(Exception ex) { Program.LogException("PeerRouterOpen",ex); }
                lock(gate) { if(stopped) { if(mapping!=null)mapping.Dispose();return; }router=mapping;if(mapping!=null&&string.IsNullOrWhiteSpace(Address))Address=mapping.Address;routerFinished=true; }
            });
        }
        internal void Update(Position p,DateTime timestamp) { lock(gate) { local=p==null?null:new Position { X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw };updated=timestamp.ToUniversalTime(); } }
        internal void UpdateMarkers(SharedMapMarker[] markers) {
            SharedMapMarker[] next;
            if(markers==null||markers.Length==0) { next=Array.Empty<SharedMapMarker>(); }
            else {
                var list=new List<SharedMapMarker>(Math.Min(128,markers.Length));
                var seen=new HashSet<string>(StringComparer.Ordinal);
                for(int i=0;i<markers.Length&&list.Count<128;i++) {
                    var m=markers[i];
                    if(SharedMapMarker.Valid(m)&&seen.Add(m.Id)) list.Add(m.Copy());
                }
                next=list.Count==0?Array.Empty<SharedMapMarker>():list.ToArray();
            }
            lock(gate) {
                bool diff=next.Length!=localMarkers.Length;
                if(!diff) for(int i=0;i<next.Length;i++) { var a=next[i]; var b=localMarkers[i]; if(a.Id!=b.Id||a.Name!=b.Name||a.X!=b.X||a.Y!=b.Y||a.Death!=b.Death) { diff=true; break; } }
                if(diff) markerRevision++;
                localMarkers=next;
            }
        }
        internal PrivatePositions.Peer[] Snapshot() {
            lock(gate) {
                if(stopped||!ready||publications.Count==0) return Array.Empty<PrivatePositions.Peer>();
                DateTime now=clock();
                var list=new List<PrivatePositions.Peer>(publications.Count);
                foreach(var p in publications.Values) if(p.Id!=id&&p.Peer.Position!=null&&PrivatePositions.Fresh(p.Peer.Updated,now)) list.Add(p.Peer);
                return list.Count==0?Array.Empty<PrivatePositions.Peer>():list.ToArray();
            }
        }
        internal SharedMapMarker[] SnapshotMarkers() {
            lock(gate) {
                if(stopped||!ready||publications.Count==0) return Array.Empty<SharedMapMarker>();
                DateTime now=clock();
                var list=new List<SharedMapMarker>();
                foreach(var p in publications.Values) {
                    if(p.Id==id||!PrivatePositions.Fresh(p.Seen,now)||p.Markers==null) continue;
                    for(int i=0;i<p.Markers.Length;i++) list.Add(p.Markers[i].Copy());
                }
                return list.Count==0?Array.Empty<SharedMapMarker>():list.ToArray();
            }
        }
        internal PrivatePositions.Peer[] Roster() {
            lock(gate) {
                if(stopped||!ready||publications.Count==0) return Array.Empty<PrivatePositions.Peer>();
                DateTime now=clock();
                var list=new List<PrivatePositions.Peer>(publications.Count);
                foreach(var p in publications.Values) {
                    if(p.Id==id||!PrivatePositions.Fresh(p.Seen,now)) continue;
                    list.Add(new PrivatePositions.Peer { Id=p.Id,Name=p.Peer.Name,Position=p.Peer.Position!=null&&PrivatePositions.Fresh(p.Peer.Updated,now)?p.Peer.Position:null,Updated=p.Peer.Updated });
                }
                if(list.Count==0) return Array.Empty<PrivatePositions.Peer>();
                list.Sort((a,b)=> { int c=StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name); return c!=0?c:StringComparer.Ordinal.Compare(a.Id,b.Id); });
                return list.ToArray();
            }
        }
        void Accept() {
            while(!stopped)try { var client=listener.AcceptTcpClient();lock(gate) { if(stopped||links.Count>=8) { client.Close();continue; }Attach(client); } }catch(Exception e) { if(!stopped)error=e.Message;break; }
        }
        void Connect(string address,int port) {
            var client=new System.Net.Sockets.TcpClient();
            Stream stream=null;
            try {
                ConnectWithTimeout(client,address,port,5000,stopCts.Token);
                client.SendTimeout=5000;client.ReceiveTimeout=5000;stream=client.GetStream();
                if(Relay) {
                    var secure=new System.Net.Security.SslStream(stream,false,(sender,cert,chain,errors)=> {
                        if(cert==null)return false;return string.Equals(Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())),relayCertificate,StringComparison.OrdinalIgnoreCase);
                    });
                    stream=secure;
                    secure.AuthenticateAsClient("minimap-relay",null,(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13),false);
                    string line=relayGroup==null?"SCUMRELAY1 "+RelayRoom(key):memberToken==null?"SCUMGROUP1 "+relayGroup+" "+RelayRoom(key):(persistentMember?"SCUMGROUP4 ":"SCUMGROUP2 ")+relayGroup+" "+RelayRoom(key)+" "+memberToken+" "+Convert.ToBase64String(PrivatePositions.Seal(key,persistentMember?"member-name-v2:"+id+":"+name:"member-name-v1:"+name))+(string.IsNullOrEmpty(ownerToken)?"":" "+ownerToken);
                    byte[] hello=Encoding.ASCII.GetBytes(line+"\n");secure.Write(hello,0,hello.Length);Array.Clear(hello,0,hello.Length);
                    byte[] reply=new byte[3];ReadExact(secure,reply);if(Encoding.ASCII.GetString(reply)!="OK\n")throw new InvalidDataException();
                }
                lock(gate) { if(stopped) { stream.Dispose();client.Close();return; }Attach(client,stream);if(Relay)ready=true; }
            }catch(Exception e) { if(stream!=null) { try { stream.Dispose(); } catch {} } client.Close();if(!stopped) { error=e.Message; } }
        }
        void Attach(System.Net.Sockets.TcpClient client,Stream stream=null) {
            client.NoDelay=true;client.SendTimeout=2000;client.ReceiveTimeout=10000;
            var link=new Link { Client=client,Stream=stream??client.GetStream() };links.Add(link);System.Threading.Tasks.Task.Run(()=>Read(link));
        }
        internal static byte[] ReadFrame(Stream stream) {
            var header=new byte[4];ReadExact(stream,header);int size=System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header,0));
            if(size<64||size>PrivatePositions.MaximumPacketSize)throw new InvalidDataException();var packet=new byte[size];ReadExact(stream,packet);return packet;
        }
        byte[] ReadNetworkFrame(Stream stream) {
            if(!persistentMember)return ReadFrame(stream);
            while(true) {
                var header=new byte[4];ReadExact(stream,header);uint raw=unchecked((uint)System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header,0)));int size=(int)(raw&0x7fffffff);
                if(raw==1) { int mode=stream.ReadByte();if(mode!=0&&mode!=1)throw new InvalidDataException();lock(gate) { compactUpdates=mode==1;lastMarkerRevision=-1; }continue; }
                if(size<64||size>PrivatePositions.MaximumPacketSize)throw new InvalidDataException();var packet=new byte[size];ReadExact(stream,packet);return packet;
            }
        }
        static void ReadExact(Stream stream,byte[] bytes) { int offset=0;while(offset<bytes.Length) { int count=stream.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count; } }
        static void WriteFrame(Stream stream,byte[] packet,bool compact=false) { byte[] header=BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(compact?packet.Length|unchecked((int)0x80000000):packet.Length));stream.Write(header,0,4);stream.Write(packet,0,packet.Length); }
        Publication Decode(byte[] packet) {
            string text=PrivatePositions.OpenWithDerived(encKey,macKey,packet);var lines=text.Split('\n');if(lines.Length>129)throw new InvalidDataException();var fields=lines[0].Split('\t');Guid identity;long incomingSequence;int age,heartbeatAge;
            // Version 3 carries capture age, not the sender's wall-clock time.
            // Comparing another PC's UTC clock silently hid one direction when
            // its clock was more than five seconds ahead of the receiver.
            bool compact=fields.Length==11&&fields[0]=="4";if((!compact&&(fields.Length!=10||fields[0]!="3"))||compact&&(fields[10]!="M"&&fields[10]!="=")||!Guid.TryParseExact(fields[1],"N",out identity)||fields[2].Length<1||fields[2].Length>32||fields[2].Any(char.IsControl)||!long.TryParse(fields[3],out incomingSequence)||incomingSequence<1||!int.TryParse(fields[4],out age)||age<0||!int.TryParse(fields[5],out heartbeatAge)||heartbeatAge<0)throw new InvalidDataException();
            if(age>30000||heartbeatAge>30000)return null;var now=clock();var stamp=now.AddMilliseconds(-age);
            Position position=null;
            if(fields[6]!="-") {
                var values=new double[4];for(int i=0;i<4;i++)if(!double.TryParse(fields[6+i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||double.IsNaN(values[i])||double.IsInfinity(values[i])||Math.Abs(values[i])>(i==3?360:2000000))throw new InvalidDataException();
                position=new Position { X=values[0],Y=values[1],Z=values[2],Yaw=values[3] };
            }else if(fields.Skip(6).Take(4).Any(value=>value!="-"))throw new InvalidDataException();
            var markers=new System.Collections.Generic.List<SharedMapMarker>();var markerIds=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(string line in lines.Skip(1)) {
                var values=line.Split('\t');double x,y;if(values.Length!=5||(values[1]!="D"&&values[1]!="W")||values[2].Length>512||!double.TryParse(values[3],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(values[4],NumberStyles.Float,CultureInfo.InvariantCulture,out y))throw new InvalidDataException();
                var marker=new SharedMapMarker { Id=values[0],Death=values[1]=="D",Name=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(values[2])),X=x,Y=y,Owner=fields[2],OwnerId=fields[1] };
                if(!SharedMapMarker.Valid(marker)||!markerIds.Add(marker.Id))throw new InvalidDataException();markers.Add(marker);
            }
            if(compact&&fields[10]=="="&&markers.Count!=0)throw new InvalidDataException();
            return new Publication { Id=fields[1],Peer=new PrivatePositions.Peer { Id=fields[1],Name=fields[2],Position=position,Updated=stamp },Sequence=incomingSequence,Seen=now.AddMilliseconds(-heartbeatAge),Markers=compact&&fields[10]=="="?null:markers.ToArray() };
        }
        byte[] Encode(Publication publication,DateTime now) {
            var peer=publication.Peer;var p=PrivatePositions.Fresh(peer.Updated,now)?peer.Position:null;
            int age=p==null?0:(int)Math.Max(0,Math.Min(30000,(now-peer.Updated).TotalMilliseconds));
            var markers=(publication.IncludeMarkers&&publication.Markers!=null)?publication.Markers:Array.Empty<SharedMapMarker>();
            var sb=new StringBuilder(160+markers.Length*140);
            sb.Append(publication.Compact?"4":"3").Append('\t').Append(publication.Id).Append('\t').Append(peer.Name).Append('\t')
              .Append(publication.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(age.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(((int)Math.Max(0,(now-publication.Seen).TotalMilliseconds)).ToString(CultureInfo.InvariantCulture)).Append('\t');
            if(p!=null) sb.Append(p.X.ToString("R",CultureInfo.InvariantCulture)).Append('\t').Append(p.Y.ToString("R",CultureInfo.InvariantCulture)).Append('\t').Append(p.Z.ToString("R",CultureInfo.InvariantCulture)).Append('\t').Append(p.Yaw.ToString("R",CultureInfo.InvariantCulture));
            else sb.Append("-\t-\t-\t-");
            if(publication.Compact) sb.Append('\t').Append(publication.IncludeMarkers?"M":"=");
            for(int i=0;i<markers.Length;i++) {
                var marker=markers[i];
                sb.Append('\n').Append(marker.Id).Append('\t').Append(marker.Death?"D":"W").Append('\t')
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(marker.Name))).Append('\t')
                  .Append(marker.X.ToString("R",CultureInfo.InvariantCulture)).Append('\t')
                  .Append(marker.Y.ToString("R",CultureInfo.InvariantCulture));
            }
            return PrivatePositions.SealWithDerived(encKey,macKey,sb.ToString());
        }
        void Read(Link link) {
            try {
                var stream=link.Stream;
                while(!stopped) {
                    var publication=Decode(ReadNetworkFrame(stream));if(publication==null)continue;
                    lock(gate) {
                        if(stopped)return;
                        if(host) { if(publication.Id==id||(link.Id!=null&&link.Id!=publication.Id)||links.Any(other=>other!=link&&other.Id==publication.Id))throw new InvalidDataException();link.Id=publication.Id; }
                        if(publication.Id==id)continue;
                        long previous;if(link.Sequences.TryGetValue(publication.Id,out previous)&&previous>=publication.Sequence)continue;
                        if(link.Sequences.Count>=128&&!link.Sequences.ContainsKey(publication.Id))throw new InvalidDataException();
                        link.Sequences[publication.Id]=publication.Sequence;
                        if(publications.Count>=32&&!publications.ContainsKey(publication.Id))throw new InvalidDataException();
                        if(publication.Markers==null) { Publication previousPublication;publication.Markers=publications.TryGetValue(publication.Id,out previousPublication)?previousPublication.Markers:Array.Empty<SharedMapMarker>(); }
                        publications[publication.Id]=publication;ready=true;
                    }
                }
            }catch(Exception e) { if(!host&&!stopped)lock(gate) { error=e.Message;if(e is InvalidDataException)ReconnectBlocked=true;ready=false;publications.Clear(); } }
            finally { lock(gate) { links.Remove(link);if(host&&link.Id!=null)publications.Remove(link.Id);if(!host) { ready=false;publications.Clear(); } }link.Stream.Dispose();link.Client.Close(); }
        }
        void Tick(object unused) {
            if(stopped||Interlocked.Exchange(ref ticking,1)!=0)return;
            try {
                Link[] connections;byte[][] packets;bool compact;var now=clock();
                lock(gate) {
                    if(stopped)return;
                    List<string> stalePubs=null;
                    foreach(var pair in publications) if(!PrivatePositions.Fresh(pair.Value.Seen,now)) { if(stalePubs==null) stalePubs=new List<string>(4); stalePubs.Add(pair.Key); }
                    if(stalePubs!=null) for(int si=0;si<stalePubs.Count;si++) publications.Remove(stalePubs[si]);
                    bool valid=local!=null&&PrivatePositions.Fresh(updated,now);var p=local;
                    compact=Relay&&persistentMember&&compactUpdates;bool includeMarkers=!compact||markerRevision!=lastMarkerRevision||now>=nextFullMarkers;
                    if(includeMarkers) { lastMarkerRevision=markerRevision;nextFullMarkers=now.AddSeconds(5); }
                    var own=new Publication { Compact=compact,IncludeMarkers=includeMarkers,Id=id,Peer=new PrivatePositions.Peer { Name=name,Updated=valid?updated:now,Position=valid?p:null },Sequence=++sequence,Seen=now,Markers=localMarkers };publications[id]=own;
                    // Re-encode relayed publications with their current age so
                    // forwarding never makes an old captured location fresh.
                    if(host) { packets=new byte[publications.Count][]; int pi=0; foreach(var pub in publications.Values) packets[pi++]=Encode(pub,now); }
                    else packets=new[]{Encode(own,now)};
                    connections=links.ToArray();
                }
                foreach(var connection in connections)try { var stream=connection.Stream;foreach(var packet in packets) { if(stopped)return;WriteFrame(stream,packet,compact);Interlocked.Add(ref SentBytes,packet.Length+4); } }catch(Exception) { connection.Client.Close(); }
            }catch(Exception e) { if(!stopped)error=e.Message; }finally { Interlocked.Exchange(ref ticking,0); }
        }
        public void Dispose() {
            if(Interlocked.Exchange(ref disposeGuard,1)!=0) return;
            stopped=true;ready=false;
            try { stopCts.Cancel(); } catch(ObjectDisposedException) {}
            timer.Dispose();if(listener!=null)listener.Stop();
            lock(gate) {
                if(router!=null) { router.Dispose();router=null; }
                foreach(var link in links) {
                    if(link.Stream!=null) { try { link.Stream.Dispose(); } catch {} }
                    try { link.Client.Close(); } catch {}
                }
                publications.Clear();local=null;localMarkers=Array.Empty<SharedMapMarker>();
                Array.Clear(key,0,key.Length);Array.Clear(encKey,0,encKey.Length);Array.Clear(macKey,0,macKey.Length);
            }
            stopCts.Dispose();
        }
    }
}
