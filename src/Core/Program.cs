using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZiloreMiniMap {

    public static class Program {
#if MINIMAP_TEST
        internal static readonly bool IsTestBuild=true;
#else
        internal static readonly bool IsTestBuild=false;
#endif
        internal static string DiagnosticDataFolderName(bool testBuild,string traceFlag,string session) {
            Guid id;
            if(testBuild && traceFlag=="1" && Guid.TryParseExact(session,"N",out id))
                return "ScumMiniMap-ShortcutDiagnostics-"+id.ToString("N");
            return testBuild?"ZiloreMiniMap-ResponsivenessTest":"ZiloreMiniMap";
        }
        internal static string DataFolderName { get {
            return DiagnosticDataFolderName(IsTestBuild,Environment.GetEnvironmentVariable("SCUM_MINIMAP_INPUT_TRACE"),
                Environment.GetEnvironmentVariable("SCUM_MINIMAP_DIAGNOSTIC_SESSION"));
        } }
        internal static bool IsShortcutDiagnosticSession { get {
            return IsTestBuild && DataFolderName.StartsWith("ScumMiniMap-ShortcutDiagnostics-",StringComparison.Ordinal);
        } }
        internal static int MinimumCopyIntervalMs { get { return 250; } }
        internal const int DefaultCopyModifierKey = 0;
        // VK_DIVIDE (the physical NumPad / key) is stable across language layouts.
        internal const int DefaultCopyKey = 0x6F;
        internal static int TrackingIntervalMs(int configured,int modifier) { return Math.Max(modifier>0?1000:MinimumCopyIntervalMs,configured); }
        internal static int CopyResponseTimeoutMs(int modifier) { return modifier>0?1000:350; }
        internal static int CopyRetryDelayMs(int modifier) { return modifier>0?5000:1000; }
        internal static string CopyBindingHelp { get { return Localization.Get("CopyBindingHelp"); } }
        internal static int UpgradeCopyInterval(int configured,bool revisionSeen) {
            return !revisionSeen && configured==1000 ? 250 : configured;
        }



        internal static string LocalUpdateSource;
        internal static void CompletePostUpdate(string folder,Action<string> notify) {
            string status=Path.Combine(folder,"update-result.txt");
            UpdateInstaller.CleanupHelper(status);
            try {
                string readyDir=Path.Combine(folder,"updates","ready");
                if(Directory.Exists(readyDir)) {
                    if(File.Exists(status)) { Directory.Delete(readyDir,true); }
                    else {
                        foreach(string sub in Directory.GetDirectories(readyDir)) {
                            string name=Path.GetFileName(sub); Version v;
                            if(name!=null && name.StartsWith("v",StringComparison.OrdinalIgnoreCase) && Version.TryParse(name.Substring(1)+".0",out v) && v<=MapWindow.CurrentVersion) {
                                try { Directory.Delete(sub,true); } catch {}
                            }
                        }
                    }
                }
            } catch {}
            try {
                if(!File.Exists(status))return;
                if(new FileInfo(status).Length>65536)throw new InvalidDataException("Update result exceeds the supported size.");
                string result=File.ReadAllText(status);
                notify(result);
                File.Delete(status);
            }catch(Exception ex) {
                if(!(ex is IOException)&&!(ex is UnauthorizedAccessException)&&!(ex is InvalidDataException))throw;
                LogException("PostUpdate",ex);
            }
        }

        static bool TryInstallDownloadedUpdate(string[] args) {
            bool requested=args!=null && args.Length>0 && args[0].Equals("-InstallUpdate",StringComparison.OrdinalIgnoreCase);
            if(IsTestBuild)return false;
            string source=Environment.ProcessPath ?? AppContext.BaseDirectory;
            try {
                Version version;
                try { version=UpdateBootstrap.ReadStableVersion(source); }
                catch(BadImageFormatException) {
                    var fvi=System.Diagnostics.FileVersionInfo.GetVersionInfo(source);
                    if(fvi==null || (fvi.FileMajorPart==0 && fvi.FileMinorPart==0 && fvi.FileBuildPart==0)) return false;
                    version=new Version(fvi.FileMajorPart,fvi.FileMinorPart,fvi.FileBuildPart,0);
                }
                var running=UpdateBootstrap.RunningInstallations(source,version);
                if(!requested && running.Count==0)return false;
                Localization.DetectSystemLanguage();
                string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DataFolderName,"settings.ini");
                if(File.Exists(settings) && new FileInfo(settings).Length<1048576)foreach(string line in File.ReadAllLines(settings))
                    if(line.StartsWith("Language=",StringComparison.OrdinalIgnoreCase)) { Localization.SetLanguage(line.Substring(9));break; }
                string explicitPath=requested && args.Length==2?args[1]:null;
                if(requested && args.Length>2)throw new FormatException("Use -InstallUpdate with an optional installed executable path.");
                if(string.IsNullOrWhiteSpace(explicitPath) && running.Count!=1) {
                    if(running.Count>1)MessageBox.Show(Localization.Get("UpdateInstallAmbiguous"),Localization.Get("UpdateInstallTitle"),MessageBoxButtons.OK,MessageBoxIcon.Information);
                    using(var picker=new OpenFileDialog { Title=Localization.Get("UpdateInstallChoose"),Filter="ZiloreMiniMap (*.exe)|*.exe",CheckFileExists=true,FileName="ZiloreMiniMap.exe" }) {
                        if(picker.ShowDialog()!=DialogResult.OK)return true;
                        explicitPath=picker.FileName;
                    }
                }
                var target=UpdateBootstrap.SelectInstallation(running,explicitPath,version,source);
                if(MessageBox.Show(Localization.T("UpdateInstallPrompt",target.Version.ToString(3),target.InstalledPath,version.ToString(3)),
                    Localization.Get("UpdateInstallTitle"),MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return true;
                string bundled=Path.Combine(Path.GetDirectoryName(source),"update.txt"),manifest;
                string packageManifest=Path.Combine(Path.GetDirectoryName(source),"package.txt");
                if(requested && File.Exists(packageManifest)) {
                    if(new FileInfo(packageManifest).Length>4096)throw new FormatException("The package manifest is too large.");
                    var fields=new System.Collections.Generic.Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(string line in File.ReadAllLines(packageManifest)) {
                        string[] pair=line.Split('=');if(pair.Length!=2 || fields.ContainsKey(pair[0]))throw new FormatException("Invalid package manifest.");
                        fields.Add(pair[0],pair[1]);
                    }
                    string packagedVersion,packagedHash;
                    if(fields.Count!=4 || !fields.TryGetValue("version",out packagedVersion) || (!fields.TryGetValue("ZiloreMiniMap.exe",out packagedHash) && !fields.TryGetValue("SkynettMiniMap.exe",out packagedHash)))throw new FormatException("Incomplete package manifest.");
                    InstallationAssets.Validate();
                    manifest="schema=1\nversion="+packagedVersion+"\nsha256="+packagedHash+"\n";
                } else if(requested && File.Exists(bundled)) {
                    if(new FileInfo(bundled).Length>16384)throw new FormatException("The update manifest is too large.");
                    manifest=File.ReadAllText(bundled);
                } else {
                    var service=new UpdateService(UpdateService.Repository,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DataFolderName,"updates"),null);
                    var release=service.CheckVersion(version.ToString(3));
                    manifest="schema=1\nversion="+release.VersionText+"\nsha256="+release.Sha256+"\n";
                }
                string status=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DataFolderName,"update-result.txt");
                UpdateBootstrap.Install(source,manifest,target,status);
                return true;
            }catch(Exception ex) {
                if(!requested) {
                    if(!(ex is InvalidDataException)) LogWarn("UpdateBootstrap", ex.Message);
                    return false;
                }
                LogException("UpdateBootstrap",ex);
                MessageBox.Show(Localization.T("UpdateFailedMsg",ex.Message),Localization.Get("UpdateInstallTitle"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return true;
            }
        }



        static void CopyDirectorySafe(string sourceDir, string targetDir) {
            Directory.CreateDirectory(targetDir);
            foreach(string file in Directory.GetFiles(sourceDir)) {
                string dest=Path.Combine(targetDir,Path.GetFileName(file));
                if(!File.Exists(dest)) try { File.Copy(file,dest,false); } catch { }
            }
            foreach(string sub in Directory.GetDirectories(sourceDir)) {
                string name=Path.GetFileName(sub);
                if(string.Equals(name,"updates",StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectorySafe(sub,Path.Combine(targetDir,name));
            }
        }
        internal static void LogInfo(string source, string message) {
            AppLogBuffer.Add("INFO", source, message);
        }

        internal static void LogWarn(string source, string message) {
            AppLogBuffer.Add("WARNING", source, message);
        }

        internal static void LogException(string source, Exception ex) {
            AppLogBuffer.Add("ERROR", source, ex != null ? (ex.GetType().Name + ": " + ex.Message) : "Unknown error");
            try {
                string msg = string.Format("{0} [{1}] {2}: {3}{4}{5}{4}",
                    DateTime.UtcNow.ToString("o"),
                    source,
                    ex != null ? ex.GetType().FullName : "Unknown",
                    IsShortcutDiagnosticSession ? "Exception details omitted from shortcut capture." : (ex != null ? ex.Message : "No message"),
                    Environment.NewLine,
                    IsShortcutDiagnosticSession ? "" : (ex != null ? ex.StackTrace : ""));
                AppLogBuffer.EnqueueFileWrite(msg);
            } catch {}
        }



        [STAThread]



        public static void Main(string[] args) {
            if(args!=null && args.Length==2 && args[0]=="-ApplyVerifiedUpdate") { UpdateInstaller.Run(args[1]); return; }



            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);



            Application.ThreadException += (s, e) => LogException("ThreadException", e.Exception);



            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogException("UnhandledException", e.ExceptionObject as Exception);



            TaskScheduler.UnobservedTaskException += (s, e) => {



                LogException("UnobservedTaskException", e.Exception);



                e.SetObserved();



            };



            Application.EnableVisualStyles();



            string baseFolder=AppDomain.CurrentDomain.BaseDirectory;



            string appData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),DataFolderName);



            try {



                if(!Directory.Exists(appData)) {
                    string legacyData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScumMiniMap");
                    if(!IsTestBuild && !IsShortcutDiagnosticSession && Directory.Exists(legacyData)) {
                        CopyDirectorySafe(legacyData,appData);
                        LogInfo("Profile","Migrated legacy profile from ScumMiniMap to ZiloreMiniMap.");
                    } else {
                        Directory.CreateDirectory(appData);
                    }
                }



                // Migrate existing config files from executable folder if present



                string[] toMigrate=new[]{"settings.ini","automatic.log"};



                foreach(string f in IsShortcutDiagnosticSession?Array.Empty<string>():toMigrate) {



                    string src=Path.Combine(baseFolder,f);



                    string dst=Path.Combine(appData,f);



                    if(File.Exists(src) && !File.Exists(dst)) {



                        try { File.Copy(src,dst); } catch {}



                    }



                }



            } catch {}



            if(args!=null && args.Length>0) {



                bool check=false,preview=false,checkAuto=false,bench=false;



                string screenshot=null,localUpdateSource=null;



                for(int i=0;i<args.Length;i++) {



                    string a=args[i];



                    if(a.Equals("-Check",StringComparison.OrdinalIgnoreCase)||a.Equals("/Check",StringComparison.OrdinalIgnoreCase)||a.Equals("--check",StringComparison.OrdinalIgnoreCase)) check=true;



                    else if(a.Equals("-Preview",StringComparison.OrdinalIgnoreCase)||a.Equals("/Preview",StringComparison.OrdinalIgnoreCase)||a.Equals("--preview",StringComparison.OrdinalIgnoreCase)) preview=true;
                    else if(a.Equals("-Bench",StringComparison.OrdinalIgnoreCase)||a.Equals("/Bench",StringComparison.OrdinalIgnoreCase)||a.Equals("--bench",StringComparison.OrdinalIgnoreCase)) bench=true;



                    else if(a.Equals("-CheckAutomatic",StringComparison.OrdinalIgnoreCase)||a.Equals("/CheckAutomatic",StringComparison.OrdinalIgnoreCase)||a.Equals("--check-automatic",StringComparison.OrdinalIgnoreCase)) checkAuto=true;



                    else if((a.Equals("-ImportScreenshot",StringComparison.OrdinalIgnoreCase)||a.Equals("/ImportScreenshot",StringComparison.OrdinalIgnoreCase)||a.Equals("--import-screenshot",StringComparison.OrdinalIgnoreCase)) && i+1<args.Length) { screenshot=args[++i]; }



                    else if((a.Equals("-TestLocalUpdate",StringComparison.OrdinalIgnoreCase)||a.Equals("/TestLocalUpdate",StringComparison.OrdinalIgnoreCase)||a.Equals("--test-local-update",StringComparison.OrdinalIgnoreCase)) && i+1<args.Length) { localUpdateSource=args[++i]; }



                }



                LocalUpdateSource=localUpdateSource;



                if(check) {



                    MapWindow.SelfTest();



                    Console.WriteLine("Compilation and coordinate checks passed.");



                    return;



                }



                if(bench) {
                    var summary = CoreBenchmarks.RunAll(60);
                    Console.WriteLine(summary.FormatLine());
                    return;
                }

                if(checkAuto) {



                    using(MapWindow miniForm=new MapWindow(appData,true)) {



                        miniForm.CheckAutomaticImport(screenshot,Path.Combine(baseFolder,"automatic-editor-preview.png"));



                    }



                    Console.WriteLine("Automatic import and naming checks passed.");



                    return;



                }



                if(preview) {
                    string previewOut = Path.Combine(baseFolder, "wpf-preview");
                    using(var console = new WpfFieldConsoleRuntime(appData, true)) {
                        console.Preview(previewOut);
                    }
                    Console.WriteLine("Pure WPF preview generated: " + previewOut);
                    return;
                }
            }

            // Release downloads can install over an older running copy before the normal
            // single-instance mutex rejects them. Self-checks/previews and test profiles bypass this.
            string zoneFile=null;
            try {
                if(args!=null&&args.Length==1&&args[0]=="--register-zone-files") { ZoneFileOpen.Register(Application.ExecutablePath);Localization.DetectSystemLanguage();MessageBox.Show(Localization.Get("ZoneAssociationInfo"),"ZiloreMiniMap");return; }
                zoneFile=ZoneFileOpen.ParseArguments(args);
            }catch(Exception error) { LogException("ZoneFileOpen",error);Localization.DetectSystemLanguage();MessageBox.Show(Localization.T("ZoneOpenError",error.Message),"ZiloreMiniMap");return; }
            if(zoneFile==null&&Array.IndexOf(args,"--auto-scum")<0&&TryInstallDownloadedUpdate(args))return;
            try { InstallationAssets.Validate(); }
            catch(Exception error) {
                LogException("InstallationAssets",error);
                Localization.DetectSystemLanguage();
                MessageBox.Show(Localization.Get("InstallationAssetsMissing")+"\n\n"+error.Message,
                    "ZiloreMiniMap",MessageBoxButtons.OK,MessageBoxIcon.Error);return;
            }

            if(zoneFile!=null)try { ZoneFileOpen.Enqueue(appData,zoneFile); }catch(Exception error) { LogException("ZoneFileOpen",error);Localization.DetectSystemLanguage();MessageBox.Show(Localization.T("ZoneOpenError",error.Message),"ZiloreMiniMap");return; }
            bool createdNew;



            using(System.Threading.Mutex mutex=new System.Threading.Mutex(true,"Local\\ZiloreMiniMapOverlay",out createdNew)) {



                if(!createdNew) {
                    if(zoneFile!=null)return;
                    if(IsTestBuild) MessageBox.Show("Exit the running SCUM MiniMap from its tray menu before starting this test package.","ZiloreMiniMap responsiveness test");
                    return;
                }



                try {
                    if(!IsTestBuild)try { ZoneFileOpen.Register(Application.ExecutablePath); }catch(Exception error) { LogException("ZoneFileAssociation",error); }



                    if(!IsTestBuild) {
                        bool sigOk=IntegrityGuard.VerifyAuthenticodeSignature();
                        LogInfo("Security",sigOk?"Authenticode signature verified.":"Authenticode signature not trusted or absent (expected for unsigned/unimported cert).");
                    }
                    WpfFieldConsoleRuntime.Run(appData, LocalUpdateSource);
                    Native.FlushInputTrace();
                    return;
                } catch(Exception ex) {



                    LogException("ApplicationRun", ex);



                }



            }



        }



    }

}
