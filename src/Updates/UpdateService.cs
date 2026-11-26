using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Security.Principal;
using System.Security.AccessControl;

namespace ZiloreMiniMap {
    internal sealed class UpdateCheckSchedule {
        DateTime nextCheckUtc;
        internal UpdateCheckSchedule(DateTime now) { RecordCheck(now); }
        internal void RecordCheck(DateTime now) { nextCheckUtc=now.AddHours(1); }
        internal bool TryBegin(DateTime now,bool allowed) {
            if(!allowed || now<nextCheckUtc)return false;
            RecordCheck(now);return true;
        }
    }
    internal sealed class UpdatePreferences {
        internal bool AutoCheckEnabled = true;
        internal bool AutoDownloadEnabled = true;
        internal bool AutoApplyOnExitEnabled = true;
        internal string SkippedVersion = "";
        internal string LastCheckedUtc = "";
        internal string LastKnownVersion = "";
    }
    internal static partial class UpdatePreferencesStore {
        [GeneratedRegex(@"\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z", RegexOptions.CultureInvariant)] private static partial Regex RxVer();
        const string FileName = "updates.ini";
        static readonly object gate = new object();
        static string dataDir;
        static UpdatePreferences current = new UpdatePreferences();
        internal static UpdatePreferences Current {
            get {
                lock (gate) {
                    return new UpdatePreferences {
                        AutoCheckEnabled = current.AutoCheckEnabled,
                        AutoDownloadEnabled = current.AutoDownloadEnabled,
                        AutoApplyOnExitEnabled = current.AutoApplyOnExitEnabled,
                        SkippedVersion = current.SkippedVersion ?? "",
                        LastCheckedUtc = current.LastCheckedUtc ?? "",
                        LastKnownVersion = current.LastKnownVersion ?? ""
                    };
                }
            }
        }
        static string SanitizeVersion(string v) {
            string t = (v ?? "").Trim();
            return RxVer().IsMatch(t) ? t : "";
        }
        internal static UpdatePreferences Load(string folder) {
            var prefs = new UpdatePreferences();
            if (!string.IsNullOrWhiteSpace(folder)) {
                try {
                    var ini = SettingsStore.ReadIni(Path.Combine(folder, FileName));
                    string v;
                    if (ini.TryGetValue("AutoCheck", out v)) { prefs.AutoCheckEnabled = v != "0"; }
                    if (ini.TryGetValue("AutoDownload", out v)) { prefs.AutoDownloadEnabled = v != "0"; }
                    if (ini.TryGetValue("AutoApplyOnExit", out v)) { prefs.AutoApplyOnExitEnabled = v != "0"; }
                    if (ini.TryGetValue("SkippedVersion", out v)) { prefs.SkippedVersion = SanitizeVersion(v); }
                    if (ini.TryGetValue("LastCheckedUtc", out v)) { prefs.LastCheckedUtc = (v ?? "").Trim(); }
                    if (ini.TryGetValue("LastKnownVersion", out v)) { prefs.LastKnownVersion = SanitizeVersion(v); }
                } catch (Exception ex) { Program.LogException("UpdatePrefsLoad", ex); }
            }
            lock (gate) { dataDir = folder; current = prefs; }
            return Current;
        }
        internal static void Save(UpdatePreferences next, string explicitFolder = null) {
            if (next == null) { return; }
            var clean = new UpdatePreferences {
                AutoCheckEnabled = next.AutoCheckEnabled,
                AutoDownloadEnabled = next.AutoDownloadEnabled,
                AutoApplyOnExitEnabled = next.AutoApplyOnExitEnabled,
                SkippedVersion = SanitizeVersion(next.SkippedVersion),
                LastCheckedUtc = (next.LastCheckedUtc ?? "").Trim(),
                LastKnownVersion = SanitizeVersion(next.LastKnownVersion)
            };
            string targetDir;
            lock (gate) { current = clean; targetDir = !string.IsNullOrWhiteSpace(explicitFolder) ? explicitFolder : dataDir; }
            if (string.IsNullOrWhiteSpace(targetDir)) { return; }
            try {
                string body = "AutoCheck=" + (clean.AutoCheckEnabled ? "1" : "0") + Environment.NewLine
                            + "AutoDownload=" + (clean.AutoDownloadEnabled ? "1" : "0") + Environment.NewLine
                            + "AutoApplyOnExit=" + (clean.AutoApplyOnExitEnabled ? "1" : "0") + Environment.NewLine
                            + "SkippedVersion=" + clean.SkippedVersion + Environment.NewLine
                            + "LastCheckedUtc=" + clean.LastCheckedUtc + Environment.NewLine
                            + "LastKnownVersion=" + clean.LastKnownVersion + Environment.NewLine;
                SettingsStore.WriteAtomic(Path.Combine(targetDir, FileName), body);
            } catch (Exception ex) { Program.LogException("UpdatePrefsSave", ex); }
        }
        internal static void Update(Action<UpdatePreferences> mutate) {
            if (mutate == null) { return; }
            var copy = Current;
            mutate(copy);
            Save(copy);
        }
    }
    public sealed partial class UpdateRelease {
        [GeneratedRegex(@"\A\d{5}\z", RegexOptions.CultureInvariant)] private static partial Regex RxBuild();
        [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)] private static partial Regex RxSha();
        [GeneratedRegex(@"\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z", RegexOptions.CultureInvariant)] private static partial Regex RxVer();
        public Version Version { get; private set; }
        public string Sha256 { get; private set; }
        public string PackageSha256 { get; private set; }
        public string BuildNumber { get; private set; }
        public string ReleaseNotes { get; private set; }
        public bool IsPackage { get { return PackageSha256!=null; } }
        public string VersionText { get { return Version.ToString(3); } }
        public string DisplayTitle { get { return string.IsNullOrEmpty(BuildNumber) ? ("ZiloreMiniMap v" + VersionText) : ("ZiloreMiniMap v" + VersionText + " Build (" + BuildNumber + ")"); } }
        public string Filename { get { return "v" + VersionText + " - ZiloreMiniMap.exe"; } }

        public static UpdateRelease Parse(string manifest) {
            if(string.IsNullOrWhiteSpace(manifest) || manifest.Length > 16384) throw new FormatException("Invalid update manifest.");
            var values = new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string raw in manifest.TrimStart('\uFEFF').Split('\n')) {
                string line = raw.Trim();
                if(line.Length == 0) continue;
                int split = line.IndexOf('=');
                if(split < 1 || values.ContainsKey(line.Substring(0,split))) throw new FormatException("Invalid update manifest field.");
                values.Add(line.Substring(0,split), line.Substring(split+1));
            }
            string schema, version, hash, packageHash=null, build=null, notes=null;
            Version parsed;
            int expectedCount = (values.TryGetValue("schema",out schema) && schema=="2") ? 4 : 3;
            if(values.TryGetValue("build",out build)) {
                if(!RxBuild().IsMatch(build)) throw new FormatException("Invalid build number in update manifest.");
                expectedCount++;
            }
            if(values.TryGetValue("notes",out notes)) {
                notes = notes.Replace("\\n", Environment.NewLine).Trim();
                expectedCount++;
            }
            if((schema!="1" && schema!="2") ||
               values.Count!=expectedCount ||
               (schema=="2" && (!values.TryGetValue("package-sha256",out packageHash) || !RxSha().IsMatch(packageHash))) ||
               !values.TryGetValue("version",out version) || !RxVer().IsMatch(version) ||
               !System.Version.TryParse(version + ".0",out parsed) ||
               !values.TryGetValue("sha256",out hash) || !RxSha().IsMatch(hash))
                throw new FormatException("The update manifest does not describe a valid stable release.");
            return new UpdateRelease {
                Version=parsed,
                Sha256=hash.ToLowerInvariant(),
                PackageSha256=packageHash==null?null:packageHash.ToLowerInvariant(),
                BuildNumber=build,
                ReleaseNotes=notes
            };
        }
        public void VerifyFile(string path) {
            string actual;
            using(var stream=File.OpenRead(path)) actual=Convert.ToHexStringLower(SHA256.HashData(stream));
            if(!string.Equals(actual,Sha256,StringComparison.Ordinal)) throw new InvalidDataException("Update checksum mismatch. The download was discarded.");
        }
    }

    public sealed partial class UpdateService {
        [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant)] private static partial Regex RxRepo();
        [GeneratedRegex(@"\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z", RegexOptions.CultureInvariant)] private static partial Regex RxPinVer();
        // Set to the release repository before distributing a build. No credentials are shipped.
        public const string Repository = "Zilore-Anatoliy/ZiloreMiniMap";
        public const string WebsiteHost = "minimap.zilore.app";
        readonly string repository;
        readonly string cacheDirectory;
        readonly Func<Uri,int,byte[]> fetch;
        public UpdateService(string repo, string cache, Func<Uri,int,byte[]> transport) {
            if(!RxRepo().IsMatch(repo ?? ""))
                throw new InvalidOperationException("The GitHub update repository has not been configured.");
            repository=repo;
            cacheDirectory=cache;
            fetch=transport ?? Fetch;
        }
        public Uri DomainManifestUri { get { return new Uri("https://"+WebsiteHost+"/update.txt"); } }
        public Uri ManifestUri { get { return new Uri("https://github.com/"+repository+"/releases/latest/download/update.txt"); } }
        public Uri DownloadUri(UpdateRelease release) {
            return new Uri("https://github.com/"+repository+"/releases/download/v"+release.VersionText+"/"+(release.IsPackage?"ZiloreMiniMap.zip":"ZiloreMiniMap.exe"));
        }
        public Uri DownloadFallbackUri(UpdateRelease release) {
            return new Uri("https://"+WebsiteHost+"/releases/v"+release.VersionText+"/"+(release.IsPackage?"ZiloreMiniMap.zip":"ZiloreMiniMap.exe"));
        }
        public Uri ReleasePageUri(UpdateRelease release) {
            return new Uri("https://github.com/"+repository+"/releases/tag/v"+release.VersionText);
        }
        public UpdateRelease Check(bool manual) {
            // Cache is scoped to this repository; never use another repository's manifest.
            string cachePath=Path.Combine(cacheDirectory,repository.Replace('/','_')+"-release.txt");
            string retryPath=Path.Combine(cacheDirectory,repository.Replace('/','_')+"-retry-after.txt");
            try {
                DateTime retry;
                if(File.Exists(retryPath) && DateTime.TryParse(File.ReadAllText(retryPath),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out retry) && retry.ToUniversalTime()>DateTime.UtcNow)
                    throw new UpdateRetryException(retry.ToUniversalTime());
                TimeSpan age=DateTime.UtcNow-File.GetLastWriteTimeUtc(cachePath);
                if(File.Exists(cachePath) && age>=TimeSpan.Zero && age<TimeSpan.FromMinutes(manual?1:60))
                    return UpdateRelease.Parse(File.ReadAllText(cachePath));
            } catch(UpdateRetryException) { throw; }
              catch(IOException) {} catch(UnauthorizedAccessException) {} catch(FormatException) {}
            try {
                byte[] manifestBytes=null;
                Exception firstError=null;
                bool any404=false;
                foreach(Uri candidate in new[]{ManifestUri,DomainManifestUri}) {
                    try {
                        manifestBytes=fetch(candidate,16384);
                        if(manifestBytes!=null && manifestBytes.Length>0) break;
                    } catch(UpdateRetryException retryEx) {
                        TrySave(retryPath,retryEx.RetryAt.ToString("o",CultureInfo.InvariantCulture));
                        throw;
                    } catch(Exception ex) {
                        if(ex.Message!=null && ex.Message.Contains("(404)")) any404=true;
                        if(firstError==null) firstError=ex;
                    }
                }
                if(manifestBytes==null) {
                    if(any404) {
                        bool ru=Localization.Current==AppLanguage.Russian;
                        throw new InvalidDataException(ru
                            ? "Файл обновлений (update.txt) ещё не опубликован на GitHub Releases и minimap.zilore.app."
                            : "Update manifest (update.txt) is not published yet on GitHub Releases or minimap.zilore.app.");
                    }
                    if(firstError!=null) {
                        Exception root=firstError;
                        while(root.InnerException!=null) root=root.InnerException;
                        string detail=root==firstError ? firstError.Message : (firstError.Message+" ("+root.Message+")");
                        throw new InvalidDataException(detail,firstError);
                    }
                    throw new InvalidDataException("Unable to reach update servers.");
                }
                string manifest=Encoding.UTF8.GetString(manifestBytes);
                UpdateRelease release=UpdateRelease.Parse(manifest);
                TrySave(cachePath,manifest);
                return release;
            } catch(WebException ex) {
                var response=ex.Response as HttpWebResponse;
                if(response!=null) using(response) {
                    if((int)response.StatusCode==429 || (int)response.StatusCode==503 ||
                       ((int)response.StatusCode==403 && response.Headers["Retry-After"]!=null)) {
                        DateTime retry=RetryAfter(response.Headers["Retry-After"],DateTime.UtcNow);
                        TrySave(retryPath,retry.ToString("o",CultureInfo.InvariantCulture));
                        throw new UpdateRetryException(retry);
                    }
                }
                throw;
            }
        }
        public UpdateRelease CheckVersion(string version) {
            // A downloaded older release remains verifiable after a newer one becomes latest.
            Version parsed;
            if(!RxPinVer().IsMatch(version??"") ||
                !Version.TryParse(version+".0",out parsed))throw new FormatException("Invalid pinned update version.");
            var uri=new Uri("https://github.com/"+repository+"/releases/download/v"+version+"/update.txt");
            byte[] bytes=null;
            try {
                bytes=fetch(uri,16384);
            } catch(UpdateRetryException) { throw; }
              catch(Exception) {
                if(fetch!=Fetch) throw;
                bytes=fetch(DomainManifestUri,16384);
            }
            var release=UpdateRelease.Parse(Encoding.UTF8.GetString(bytes));
            if(release.Version!=parsed)throw new InvalidDataException("The pinned update manifest describes a different version.");
            return release;
        }
        public static DateTime RetryAfter(string value, DateTime now) {
            double seconds;
            DateTime date;
            if(double.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out seconds) && seconds>=0 && seconds<=86400)
                return now.AddSeconds(Math.Max(60,seconds));
            if(DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date) && date>now)
                return date;
            return now.AddMinutes(5);
        }
        public void Download(UpdateRelease release, string destination) { Download(release, destination, null, CancellationToken.None); }
        public void Download(UpdateRelease release, string destination, Action<long, long?> onProgress) { Download(release, destination, onProgress, CancellationToken.None); }
        public void Download(UpdateRelease release, string destination, Action<long, long?> onProgress, CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            // Local cache is a notification hint, never authority for executable bytes.
            var authoritative=CheckVersion(release.VersionText);
            if(authoritative.Sha256!=release.Sha256 || authoritative.PackageSha256!=release.PackageSha256)
                throw new InvalidDataException("The release identity changed. Check for updates again.");
            const int maxPackageBytes = 256*1024*1024;
            if(release.IsPackage) {
                if(fetch==Fetch) {
                    string zipStaging=destination+".zip.partial";
                    bool keepPartialOnCancel=false;
                    try {
                        string actualZipHash=DownloadToFileWithFallback(release,zipStaging,maxPackageBytes,onProgress,cancel);
                        if(!string.Equals(actualZipHash,release.PackageSha256,StringComparison.Ordinal)) throw new InvalidDataException("Update package checksum mismatch.");
                        UpdatePackage.ExtractFromFile(zipStaging,destination,release);
                        if(!IntegrityGuard.IsFileSignatureIntact(destination)) { UpdatePackage.Cleanup(destination); throw new InvalidDataException("Update executable digital signature is corrupted."); }
                    } catch(OperationCanceledException) {
                        keepPartialOnCancel=true;
                        throw;
                    } finally {
                        if(!keepPartialOnCancel) SafeCleanup.DeleteFile(zipStaging);
                    }
                    return;
                }
                byte[] package=fetch(DownloadUri(release),maxPackageBytes);
                using(var stream=new MemoryStream(package,false))if(InstallationAssets.Digest(stream)!=release.PackageSha256)throw new InvalidDataException("Update package checksum mismatch.");
                UpdatePackage.Extract(package,destination,release);
                if(!IntegrityGuard.IsFileSignatureIntact(destination)) { UpdatePackage.Cleanup(destination); throw new InvalidDataException("Update executable digital signature is corrupted."); }
                return;
            }
            // The caller chooses a new file. Never overwrite a running installation or existing file.
            string staging=destination+"."+Guid.NewGuid().ToString("N")+".partial";
            try {
                if(fetch==Fetch) {
                    string actualHash=DownloadToFileWithFallback(release,staging,maxPackageBytes,onProgress,cancel);
                    if(!string.Equals(actualHash,release.Sha256,StringComparison.Ordinal)) throw new InvalidDataException("Update checksum mismatch. The download was discarded.");
                } else {
                    byte[] bytes=fetch(DownloadUri(release),maxPackageBytes);
                    using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)) stream.Write(bytes,0,bytes.Length);
                }
                release.VerifyFile(staging);
                byte[] mz=new byte[2];
                using(var fs=File.OpenRead(staging)) { if(fs.Read(mz,0,2)<2 || mz[0]!='M' || mz[1]!='Z') throw new InvalidDataException("The update is not a Windows executable."); }
                if(!IntegrityGuard.IsFileSignatureIntact(staging)) throw new InvalidDataException("Update executable digital signature is corrupted.");
                File.Move(staging,destination);
            } finally { if(File.Exists(staging)) File.Delete(staging); }
        }
        string DownloadToFileWithFallback(UpdateRelease release,string filePath,long limit,Action<long,long?> onProgress,CancellationToken cancel) {
            try {
                return FetchToFile(DownloadUri(release),filePath,limit,onProgress,cancel);
            } catch(OperationCanceledException) { throw; }
              catch(UpdateRetryException) { throw; }
              catch(Exception primaryEx) {
                Program.LogWarn("UpdateDownload","Primary GitHub download failed ("+primaryEx.Message+"); trying mirror...");
                SafeCleanup.DeleteFile(filePath);
                try {
                    return FetchToFile(DownloadFallbackUri(release),filePath,limit,onProgress,cancel);
                } catch(OperationCanceledException) { throw; }
                  catch {
                    throw primaryEx;
                }
            }
        }
        static void TrySave(string path,string text) {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
            catch(IOException ex) { Program.LogWarn("UpdateCache", "Could not save cache: " + ex.Message); }
            catch(UnauthorizedAccessException ex) { Program.LogWarn("UpdateCache", "Access denied saving cache: " + ex.Message); }
        }
        internal static bool AllowedDownloadUri(Uri uri,bool steam) {
            if(uri==null || !uri.IsAbsoluteUri || uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo!="")return false;
            string host=uri.DnsSafeHost.ToLowerInvariant();
            return steam?host=="steamcommunity.com":host==WebsiteHost || host=="github.com" || host=="release-assets.githubusercontent.com" || host=="objects.githubusercontent.com" || host=="github-releases.githubusercontent.com";
        }
        static readonly HttpClient SharedHttp = CreateHttpClient(TimeSpan.FromMinutes(10));
        static readonly HttpClient MetadataHttp = CreateHttpClient(TimeSpan.FromSeconds(12));
        static HttpClient CreateHttpClient(TimeSpan timeout) {
            var handler=new SocketsHttpHandler {
                AllowAutoRedirect=false,
                ConnectTimeout=TimeSpan.FromSeconds(timeout.TotalSeconds<=15 ? 8 : 15),
                PooledConnectionLifetime=TimeSpan.FromMinutes(5),
                SslOptions=new System.Net.Security.SslClientAuthenticationOptions {
                    EnabledSslProtocols=System.Security.Authentication.SslProtocols.None
                }
            };
            var client=new HttpClient(handler) { Timeout=timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ZiloreMiniMap/"+MapWindow.VersionString);
            return client;
        }
        internal static TimeSpan EffectiveFetchTimeout(int limit,bool steam) {
            return (steam || limit<=131072) ? TimeSpan.FromSeconds(12) : TimeSpan.FromMinutes(10);
        }
        static byte[] Fetch(Uri uri,int limit) { return FetchBounded(uri,limit,false); }
        internal static byte[] FetchBounded(Uri uri,int limit,bool steam) { return FetchBounded(uri,limit,steam,null); }
        static bool IsTransientStatus(int status) { return status==500 || status==502 || status==504; }
        internal static byte[] FetchBounded(Uri uri,int limit,bool steam,Action<long,long?> onProgress) {
            HttpClient http=(steam || limit<=131072) ? MetadataHttp : SharedHttp;
            int maxAttempts=steam ? 2 : 3;
            Exception lastTransient=null;
            for(int attempt=0;attempt<maxAttempts;attempt++) {
                if(attempt>0) Thread.Sleep(steam ? 400 : attempt*1000);
                Uri currentUri=uri;
                try {
                    for(int redirect=0;redirect<=8;redirect++) {
                        if(!AllowedDownloadUri(currentUri,steam))throw new InvalidDataException("Unapproved download endpoint.");
                        using(var request=new HttpRequestMessage(HttpMethod.Get,currentUri)) {
                            if(limit<=32768) request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue { NoCache=true };
                            using(var response=http.Send(request,HttpCompletionOption.ResponseHeadersRead)) {
                                int status=(int)response.StatusCode;
                                if(status==301 || status==302 || status==303 || status==307 || status==308) {
                                    Uri location=response.Headers.Location;
                                    if(redirect==8 || location==null)throw new InvalidDataException("Invalid download redirect.");
                                    currentUri=location.IsAbsoluteUri?location:new Uri(currentUri,location);
                                    continue;
                                }
                                if(status==429 || status==503 || (status==403 && response.Headers.RetryAfter!=null)) {
                                    string retryVal=response.Headers.RetryAfter!=null?response.Headers.RetryAfter.ToString():null;
                                    throw new UpdateRetryException(RetryAfter(retryVal,DateTime.UtcNow));
                                }
                                if(IsTransientStatus(status)) throw new HttpRequestException("Transient HTTP "+status);
                                if(status!=200)throw new InvalidDataException("Unexpected download response ("+status+").");
                                long? totalBytes=response.Content.Headers.ContentLength;
                                if(totalBytes.HasValue && totalBytes.Value>limit)
                                    throw new InvalidDataException("Update response exceeds the size limit.");
                                int initCap=(totalBytes.HasValue&&totalBytes.Value>0&&totalBytes.Value<=limit)?(int)totalBytes.Value:65536;
                                byte[] buffer=System.Buffers.ArrayPool<byte>.Shared.Rent(32768);
                                try {
                                    using(var input=response.Content.ReadAsStream())
                                    using(var output=new MemoryStream(initCap)) {
                                        int count;
                                        long lastReported=0;
                                        if(onProgress!=null) onProgress(0,totalBytes);
                                        while((count=input.Read(buffer,0,buffer.Length))>0) {
                                            if(output.Length+count>limit) throw new InvalidDataException("Update response exceeds the size limit.");
                                            output.Write(buffer,0,count);
                                            if(onProgress!=null && (output.Length-lastReported>=65536 || (totalBytes.HasValue && output.Length>=totalBytes.Value))) {
                                                lastReported=output.Length;
                                                onProgress(output.Length,totalBytes);
                                            }
                                        }
                                        if(onProgress!=null) onProgress(output.Length,totalBytes);
                                        return output.ToArray();
                                    }
                                } finally {
                                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                                }
                            }
                        }
                    }
                    throw new InvalidDataException("Too many download redirects.");
                } catch(HttpRequestException ex) {
                    lastTransient=ex;
                } catch(IOException ex) {
                    lastTransient=ex;
                }
            }
            throw lastTransient ?? new InvalidDataException("Download failed.");
        }
        internal static string FetchToFile(Uri uri,string filePath,long limit,Action<long,long?> onProgress,CancellationToken cancel) {
            string dir=Path.GetDirectoryName(filePath);
            if(!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Exception lastTransient=null;
            for(int attempt=0;attempt<3;attempt++) {
                cancel.ThrowIfCancellationRequested();
                if(attempt>0) { cancel.WaitHandle.WaitOne(attempt*1200); cancel.ThrowIfCancellationRequested(); }
                long existingLen=0;
                if(File.Exists(filePath)) {
                    long fl=new FileInfo(filePath).Length;
                    if(fl>0 && fl<limit) existingLen=fl;
                    else SafeCleanup.DeleteFile(filePath);
                }
                Uri currentUri=uri;
                try {
                    for(int redirect=0;redirect<=8;redirect++) {
                        cancel.ThrowIfCancellationRequested();
                        if(!AllowedDownloadUri(currentUri,false)) throw new InvalidDataException("Unapproved download endpoint.");
                        using(var request=new HttpRequestMessage(HttpMethod.Get,currentUri)) {
                            if(existingLen>0) request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(existingLen,null);
                            using(var response=SharedHttp.Send(request,HttpCompletionOption.ResponseHeadersRead,cancel)) {
                                int status=(int)response.StatusCode;
                                if(status==301 || status==302 || status==303 || status==307 || status==308) {
                                    Uri location=response.Headers.Location;
                                    if(redirect==8 || location==null) throw new InvalidDataException("Invalid download redirect.");
                                    currentUri=location.IsAbsoluteUri?location:new Uri(currentUri,location);
                                    continue;
                                }
                                if(status==429 || status==503 || (status==403 && response.Headers.RetryAfter!=null)) {
                                    string retryVal=response.Headers.RetryAfter!=null?response.Headers.RetryAfter.ToString():null;
                                    throw new UpdateRetryException(RetryAfter(retryVal,DateTime.UtcNow));
                                }
                                if(status==416 && existingLen>0) {
                                    // Range not satisfiable: file may already be complete or changed; restart cleanly.
                                    SafeCleanup.DeleteFile(filePath);
                                    existingLen=0; continue;
                                }
                                if(IsTransientStatus(status)) throw new HttpRequestException("Transient HTTP "+status);
                                var cr=response.Content.Headers.ContentRange;
                                bool resuming=(status==206 && existingLen>0 && (cr==null || !cr.From.HasValue || cr.From.Value==existingLen));
                                if(status==206 && !resuming) { SafeCleanup.DeleteFile(filePath); existingLen=0; continue; }
                                if(status!=200 && !resuming) throw new InvalidDataException("Unexpected download response ("+status+").");
                                long? contentLen=response.Content.Headers.ContentLength;
                                long? totalBytes=resuming ? (response.Content.Headers.ContentRange!=null && response.Content.Headers.ContentRange.Length.HasValue ? response.Content.Headers.ContentRange.Length : (contentLen.HasValue ? (long?)(existingLen+contentLen.Value) : null)) : contentLen;
                                if(totalBytes.HasValue && totalBytes.Value>limit) throw new InvalidDataException("Update response exceeds the size limit.");
                                long written=resuming ? existingLen : 0;
                                byte[] buffer=System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
                                try {
                                    using(var input=response.Content.ReadAsStream(cancel)) {
                                        using(var fs=new FileStream(filePath,resuming?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,65536)) {
                                            long lastReported=written;
                                            if(onProgress!=null) onProgress(written,totalBytes);
                                            int count;
                                            while((count=input.Read(buffer,0,buffer.Length))>0) {
                                                cancel.ThrowIfCancellationRequested();
                                                if(written+count>limit) throw new InvalidDataException("Update response exceeds the size limit.");
                                                fs.Write(buffer,0,count);
                                                written+=count;
                                                if(onProgress!=null && (written-lastReported>=131072 || (totalBytes.HasValue && written>=totalBytes.Value))) {
                                                    lastReported=written;
                                                    onProgress(written,totalBytes);
                                                }
                                            }
                                            fs.Flush(true);
                                            if(onProgress!=null) onProgress(written,totalBytes);
                                        }
                                    }
                                } finally {
                                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                                }
                                return InstallationAssets.Digest(filePath);
                            }
                        }
                    }
                    throw new InvalidDataException("Too many download redirects.");
                } catch(OperationCanceledException) { throw; }
                  catch(UpdateRetryException) { throw; }
                  catch(HttpRequestException ex) { lastTransient=ex; }
                  catch(IOException ex) {
                    lastTransient=ex;
                }
            }
            throw lastTransient ?? new InvalidDataException("Download failed.");
        }
    }
    internal sealed class UpdateProcessIdentity {
        internal readonly int ProcessId;
        internal readonly long StartTimeUtcTicks;
        internal readonly string ExecutablePath;
        internal UpdateProcessIdentity(int processId,long startTimeUtcTicks,string executablePath) {
            if(processId<=0 || startTimeUtcTicks<=0)throw new ArgumentException("Invalid update process identity.");
            ProcessId=processId;StartTimeUtcTicks=startTimeUtcTicks;ExecutablePath=Path.GetFullPath(executablePath);
        }
    }
    internal interface IUpdateProcess:IDisposable {
        int Id { get; }
        long StartTimeUtcTicks { get; }
        string ExecutablePath { get; }
        bool HasExited { get; }
        bool CloseMainWindow();
        bool WaitForExit(int milliseconds);
        void Kill();
    }
    public static partial class UpdateInstaller {
        [GeneratedRegex(@"\AScumMiniMap-update-[0-9a-f]{32}\z", RegexOptions.CultureInvariant)] private static partial Regex RxHelperDir();
        internal static DirectorySecurity HelperDirectorySecurity(bool elevated,SecurityIdentifier user) {
            var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
            var administrators=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
            security.SetOwner(elevated?administrators:user);
            foreach(var sid in elevated?new[]{administrators,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)}:
                new[]{user,administrators,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            return security;
        }
        sealed class OwnedProcess:IUpdateProcess {
            readonly Process process;
            internal OwnedProcess(int id) {
                process=Process.GetProcessById(id);
                try {
                    // Pin the OS process handle. PID reuse cannot redirect a later close/kill
                    // after the identity check while this exact handle remains owned.
                    if(process.Handle==IntPtr.Zero)throw new IOException("Unable to acquire the approved MiniMap process handle.");
                }catch { process.Dispose();throw; }
            }
            public int Id { get { return process.Id; } }
            public long StartTimeUtcTicks { get { return process.StartTime.ToUniversalTime().Ticks; } }
            public string ExecutablePath { get { return process.MainModule.FileName; } }
            public bool HasExited { get { return process.HasExited; } }
            public bool CloseMainWindow() { return process.CloseMainWindow(); }
            public bool WaitForExit(int milliseconds) { return process.WaitForExit(milliseconds); }
            public void Kill() { process.Kill(); }
            public void Dispose() { process.Dispose(); }
        }
        internal static void CloseApprovedProcess(UpdateProcessIdentity approved,Func<int,IUpdateProcess> acquire,int graceMilliseconds,int forceWaitMilliseconds) {
            ArgumentNullException.ThrowIfNull(approved);
            ArgumentNullException.ThrowIfNull(acquire);
            if(graceMilliseconds<0 || forceWaitMilliseconds<1)throw new ArgumentOutOfRangeException("graceMilliseconds");
            IUpdateProcess process;
            try { process=acquire(approved.ProcessId); }catch(ArgumentException) { return; }
            using(process) {
                if(process.HasExited)return;
                ValidateProcess(approved,process);
                // Leave time for at least one graceful close request even with a short budget.
                int first=Math.Min(500,Math.Max(0,graceMilliseconds-1));
                if(first>0 && process.WaitForExit(first))return;
                int remaining=graceMilliseconds-first;
                while(remaining>0) {
                    if(process.HasExited)return;
                    ValidateProcess(approved,process);process.CloseMainWindow();
                    int wait=Math.Min(250,remaining);remaining-=wait;
                    if(process.WaitForExit(wait))return;
                }
                if(process.HasExited)return;
                ValidateProcess(approved,process);
                process.Kill();
                if(!process.WaitForExit(forceWaitMilliseconds))throw new IOException("The approved MiniMap process did not exit; update cancelled.");
            }
        }
        static void ValidateProcess(UpdateProcessIdentity approved,IUpdateProcess actual) {
            if(actual.Id!=approved.ProcessId || actual.StartTimeUtcTicks!=approved.StartTimeUtcTicks ||
                !string.Equals(Path.GetFullPath(actual.ExecutablePath),approved.ExecutablePath,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The running MiniMap process identity changed; update cancelled.");
        }
        internal static bool IsElevated() {
            using(var identity=WindowsIdentity.GetCurrent())return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        internal static ProcessStartInfo CreateHelperStartInfo(string helper,string config,bool elevated) {
            return new ProcessStartInfo {
                FileName=helper,Arguments="-ApplyVerifiedUpdate "+Convert.ToBase64String(Encoding.UTF8.GetBytes(config)),
                UseShellExecute=!elevated,Verb=elevated?"":"runas",CreateNoWindow=elevated,WindowStyle=ProcessWindowStyle.Hidden,
                WorkingDirectory=Path.GetDirectoryName(helper)
            };
        }
        // The helper is a private copy of this executable. Paths are binary data, never shell code.
        internal static bool IsDirectoryWritable(string directory) {
            if(string.IsNullOrWhiteSpace(directory)||!Directory.Exists(directory))return false;
            string probe=Path.Combine(directory,".zlr-write-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                using(var fs=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { fs.WriteByte(1); }
                File.Delete(probe);return true;
            } catch { SafeCleanup.DeleteFile(probe); return false; }
        }
        internal static bool IsRunningFromTempArchive(string executablePath) {
            if(string.IsNullOrWhiteSpace(executablePath))return false;
            string full=Path.GetFullPath(executablePath);
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            return full.StartsWith(temp,StringComparison.OrdinalIgnoreCase);
        }
        public static void Start(string staged,string installed,string statusPath) { Start(staged,installed,statusPath,true); }
        internal static void Start(string staged,string installed,string statusPath,bool relaunchAfterUpdate) {
            UpdateProcessIdentity parent;
            using(var current=Process.GetCurrentProcess())parent=new UpdateProcessIdentity(current.Id,current.StartTime.ToUniversalTime().Ticks,current.MainModule.FileName);
            bool noUac=IsElevated()||IsDirectoryWritable(Path.GetDirectoryName(Path.GetFullPath(installed)));
            StartForTarget(staged,installed,statusPath,parent,noUac,Process.Start,UpdateBootstrap.ReadStableVersion,(Environment.ProcessPath ?? AppContext.BaseDirectory),relaunchAfterUpdate);
        }
        internal static void StartForTarget(string staged,string installed,string statusPath,UpdateProcessIdentity parent) {
            bool noUac=IsElevated()||IsDirectoryWritable(Path.GetDirectoryName(Path.GetFullPath(installed)));
            StartForTarget(staged,installed,statusPath,parent,noUac,Process.Start);
        }
        internal static void StartForTarget(string staged,string installed,string statusPath,UpdateProcessIdentity parent,bool elevated,Func<ProcessStartInfo,Process> launch) {
            StartForTarget(staged,installed,statusPath,parent,elevated,launch,UpdateBootstrap.ReadStableVersion,
                (Environment.ProcessPath ?? AppContext.BaseDirectory));
        }
        internal static void StartForTarget(string staged,string installed,string statusPath,UpdateProcessIdentity parent,bool elevated,Func<ProcessStartInfo,Process> launch,Func<string,Version> readVersion,string helperSource) {
            StartForTarget(staged,installed,statusPath,parent,elevated,launch,readVersion,helperSource,true);
        }
        internal static void StartForTarget(string staged,string installed,string statusPath,UpdateProcessIdentity parent,bool elevated,Func<ProcessStartInfo,Process> launch,Func<string,Version> readVersion,string helperSource,bool relaunchAfterUpdate) {
            staged=Path.GetFullPath(staged);installed=Path.GetFullPath(installed);statusPath=Path.GetFullPath(statusPath);
            if(string.Equals(staged,installed,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The staged update must be a separate file.");
            ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(readVersion);
            if(readVersion(staged)<=readVersion(installed))throw new InvalidDataException("The replacement must be a newer stable MiniMap release.");
            if(parent!=null && !string.Equals(parent.ExecutablePath,installed,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The approved MiniMap path does not match the installation.");
            using(var requiredAssets=typeof(InstallationAssets).Assembly.GetManifestResourceStream("installation-assets.txt"))
                if(requiredAssets!=null && UpdatePackage.AssetIdentity(staged)=="")throw new InvalidDataException("The update is missing its installation assets. Use the complete ZIP package.");
            string stagedHash=Hash(staged),installedHash=Hash(installed);
            string directory=Path.Combine(Path.GetTempPath(),"ScumMiniMap-update-"+Guid.NewGuid().ToString("N"));
            using(var identity=WindowsIdentity.GetCurrent())
                System.IO.FileSystemAclExtensions.CreateDirectory(HelperDirectorySecurity(IsElevated(), identity.User), directory);
            string helper=Path.Combine(directory,"Updater.exe"), config=Path.Combine(directory,"request.bin");
            bool committed=false;
            try {
                File.Copy(helperSource,helper);
                Directory.CreateDirectory(Path.GetDirectoryName(statusPath)); File.WriteAllText(statusPath+".helper", helper);
                using(var writer=new BinaryWriter(File.Create(config))) {
                    writer.Write("SCUM-MiniMap-update-v3");
                    writer.Write(parent==null?0:parent.ProcessId);writer.Write(parent==null?0:parent.StartTimeUtcTicks);
                    writer.Write(staged);writer.Write(installed);writer.Write(statusPath);writer.Write(stagedHash);writer.Write(installedHash);
                    writer.Write(Hash(helper));
                    writer.Write(UpdatePackage.AssetIdentity(staged));
                }
                Process process;
                try {
                    process=launch(CreateHelperStartInfo(helper,config,elevated));
                } catch(Win32Exception ex740) when (elevated && ex740.NativeErrorCode==740) {
                    process=launch(CreateHelperStartInfo(helper,config,false));
                }
                if(process==null)throw new IOException("Unable to start the update helper.");
                using(process) {
                    DateTime deadline=DateTime.UtcNow.AddSeconds(25);
                    while(!File.Exists(config+".ready")) {
                        if(process.WaitForExit(25) || DateTime.UtcNow>=deadline)
                            throw new IOException("The update helper did not become ready; the current application will stay open.");
                    }
                    // A helper must not close the app unless the caller has observed readiness.
                    if(!relaunchAfterUpdate) File.WriteAllText(config+".norelaunch","1");
                    File.WriteAllText(config+".commit","approved");committed=true;
                }
            }catch(Win32Exception ex) {
                if(ex.NativeErrorCode==1223)throw new IOException("Administrator permission was cancelled; the current MiniMap stays open.",ex);
                throw;
            }finally {
                if(!committed) {
                    SafeCleanup.Try(() => File.WriteAllText(config+".cancel","cancelled"));
                    CleanupHelper(statusPath);
                }
            }
        }
        static string Hash(string path) {
            using(var file=File.OpenRead(path)) return Convert.ToBase64String(SHA256.HashData(file));
        }
        public static bool CleanupHelper(string statusPath) {
            string marker=statusPath+".helper";
            if(!File.Exists(marker)) return true;
            try {
                string helper=Path.GetFullPath(File.ReadAllText(marker));
                string directory=Path.GetDirectoryName(helper);
                string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if(Path.GetFileName(helper)!="Updater.exe" ||
                   !string.Equals(Path.GetDirectoryName(directory),temp,StringComparison.OrdinalIgnoreCase) ||
                   !RxHelperDir().IsMatch(Path.GetFileName(directory))) return false;
                // Delete only our known files, never recursively remove a supplied path.
                File.Delete(helper);
                File.Delete(Path.Combine(directory,"request.bin"));
                File.Delete(Path.Combine(directory,"request.bin.ready"));
                File.Delete(Path.Combine(directory,"request.bin.commit"));
                File.Delete(Path.Combine(directory,"request.bin.cancel"));
                File.Delete(Path.Combine(directory,"request.bin.norelaunch"));
                if(Directory.Exists(directory)) Directory.Delete(directory,false);
                File.Delete(marker);
                return true;
            } catch(IOException) { return false; } catch(UnauthorizedAccessException) { return false; }
              catch(ArgumentException) { return false; }
        }
        public static void ReplaceAndLaunch(string staged,string installed,string expectedHash,Action<string> launch) {
            if(!string.Equals(Hash(staged),expectedHash,StringComparison.Ordinal)) throw new InvalidDataException("Staged update checksum changed.");
            string backup=installed+"."+Guid.NewGuid().ToString("N")+".previous";
            string localStaged=staged;
            bool crossVolume=!string.Equals(Path.GetPathRoot(Path.GetFullPath(staged)),Path.GetPathRoot(Path.GetFullPath(installed)),StringComparison.OrdinalIgnoreCase);
            bool replaced=false;
            try {
                if(crossVolume) {
                    localStaged=installed+"."+Guid.NewGuid().ToString("N")+".incoming";
                    File.Copy(staged,localStaged,true);
                    if(!string.Equals(Hash(localStaged),expectedHash,StringComparison.Ordinal)) throw new InvalidDataException("Staged update checksum changed on target volume.");
                }
                File.Replace(localStaged,installed,backup); replaced=true;
                launch(installed);
            } catch {
                if(replaced) File.Replace(backup,installed,null);
                if(crossVolume && !replaced) SafeCleanup.DeleteFile(localStaged);
                throw;
            }
            // A backup cleanup failure must not roll back a successfully launched update.
            SafeCleanup.DeleteFile(backup);
            if(crossVolume) SafeCleanup.DeleteFile(staged);
        }
        public static void Run(string encodedConfig) {
            string status=null, installed=null, config=null,oldHash=null;bool parentExited=false,installedLaunched=false;
            try {
                config=Encoding.UTF8.GetString(Convert.FromBase64String(encodedConfig));
                string executing=Path.GetFullPath((Environment.ProcessPath ?? AppContext.BaseDirectory));
                string directory=Path.GetDirectoryName(executing),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if(Path.GetFileName(executing)!="Updater.exe" ||
                    !string.Equals(Path.GetDirectoryName(directory),temp,StringComparison.OrdinalIgnoreCase) ||
                    !RxHelperDir().IsMatch(Path.GetFileName(directory)) ||
                    !string.Equals(Path.GetFullPath(config),Path.Combine(directory,"request.bin"),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid update-helper location.");
                int pid;long ticks;string staged,hash,helperHash,assetIdentity;
                if(new FileInfo(config).Length>16384)throw new InvalidDataException("The update-helper request is too large.");
                using(var reader=new BinaryReader(File.OpenRead(config))) {
                    if(reader.ReadString()!="SCUM-MiniMap-update-v3")throw new InvalidDataException("Invalid update-helper request.");
                    pid=reader.ReadInt32(); ticks=reader.ReadInt64(); staged=reader.ReadString();
                    installed=reader.ReadString();status=reader.ReadString();hash=reader.ReadString();oldHash=reader.ReadString();helperHash=reader.ReadString();assetIdentity=reader.ReadString();
                    if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Unexpected update-helper request data.");
                }
                if(pid<0 || (pid==0? ticks!=0:ticks<=0))throw new InvalidDataException("Invalid update parent identity.");
                if(Hash(executing)!=helperHash || Hash(staged)!=hash || Hash(installed)!=oldHash)
                    throw new InvalidDataException("Update files changed before installation.");
                if(UpdatePackage.AssetIdentity(staged)!=assetIdentity)throw new InvalidDataException("Update assets changed before installation.");
                if(UpdateBootstrap.ReadStableVersion(staged)<=UpdateBootstrap.ReadStableVersion(installed))
                    throw new InvalidDataException("The replacement must be a newer stable MiniMap release.");
                File.WriteAllText(status,"Update pending: preparing to close MiniMap.");
                File.WriteAllText(config+".ready","ready");
                DateTime deadline=DateTime.UtcNow.AddSeconds(15);
                while(!File.Exists(config+".commit")) {
                    if(File.Exists(config+".cancel") || DateTime.UtcNow>=deadline)throw new IOException("The update was not approved; MiniMap stays open.");
                    System.Threading.Thread.Sleep(25);
                }
                if(pid!=0)CloseApprovedProcess(new UpdateProcessIdentity(pid,ticks,installed),id=>new OwnedProcess(id),10000,5000);
                parentExited=true;
                if(Hash(installed)!=oldHash)throw new InvalidDataException("The installed MiniMap changed while closing.");
                bool noRelaunch=File.Exists(config+".norelaunch");
                UpdatePackage.ReplaceAndLaunch(staged,installed,hash,assetIdentity,path=> {
                    TrySyncInnoUninstallVersion(path);
                    if(!noRelaunch) Launch(path);
                    installedLaunched=true;
                });
                // Reporting cannot roll back or launch again after a successful restart.
                SafeCleanup.Try(() => File.WriteAllText(status,"Update installed successfully."));
            } catch(Exception ex) {
                if(status!=null) SafeCleanup.Try(() => File.WriteAllText(status,"Update failed. Check the installation and any .previous backup before retrying. "+ex.Message));
                if(parentExited && !installedLaunched && installed!=null && oldHash!=null && File.Exists(installed))try {
                    if(Hash(installed)==oldHash) { UpdateBootstrap.ReadStableVersion(installed);Launch(installed); }
                }catch{}
            } finally {
                if(config!=null) { SafeCleanup.DeleteFile(config);SafeCleanup.DeleteFile(config+".ready");SafeCleanup.DeleteFile(config+".commit");SafeCleanup.DeleteFile(config+".cancel");SafeCleanup.DeleteFile(config+".norelaunch"); }
            }
        }
        internal static void TrySyncInnoUninstallVersion(string installedExePath) {
            try {
                if(string.IsNullOrWhiteSpace(installedExePath)||!File.Exists(installedExePath))return;
                string newVer=UpdateBootstrap.NormalizeStableVersion(FileVersionInfo.GetVersionInfo(installedExePath).ProductVersion);
                if(string.IsNullOrWhiteSpace(newVer))return;
                string installDir=Path.GetFullPath(Path.GetDirectoryName(installedExePath)).TrimEnd(Path.DirectorySeparatorChar);
                const string subKey=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C2E91-5B3A-4D7E-9A1C-7E6D5F4A3B21}_is1";
                foreach(var hive in new[]{Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryHive.LocalMachine}) {
                    try {
                        using(var root=Microsoft.Win32.RegistryKey.OpenBaseKey(hive,Microsoft.Win32.RegistryView.Registry64))
                        using(var key=root.OpenSubKey(subKey,true)) {
                            if(key==null)continue;
                            string regLoc=Convert.ToString(key.GetValue("InstallLocation"))??"";
                            if(string.IsNullOrWhiteSpace(regLoc))continue;
                            if(string.Equals(Path.GetFullPath(regLoc).TrimEnd(Path.DirectorySeparatorChar),installDir,StringComparison.OrdinalIgnoreCase)) {
                                key.SetValue("DisplayVersion",newVer);
                                key.SetValue("DisplayName","ZiloreMiniMap v"+newVer);
                            }
                        }
                    } catch {}
                }
            } catch {}
        }
        static void Launch(string path) {
            var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName=path,UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)
            });
            if(process==null) throw new IOException("Unable to restart the application.");
            process.Dispose();
        }
    }
    public sealed class UpdateRetryException:Exception {
        public DateTime RetryAt { get; private set; }
        public UpdateRetryException(DateTime retry):base("Update service is temporarily unavailable.") { RetryAt=retry; }
    }
}
