#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ZiloreMiniMap {
    internal static partial class IntegrityGuard {
        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_NONE = 0;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

        // WinVerifyTrust status codes
        const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        const int CERT_E_CHAINING = unchecked((int)0x800B010A);

        static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_DATA {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public string? pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsDebuggerPresent();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);

        [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetVolumeInformation(
            string lpRootPathName,
            IntPtr lpVolumeNameBuffer,
            int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            IntPtr lpFileSystemNameBuffer,
            int nFileSystemNameSize);

        static int cachedTrustStatus = int.MinValue;
        static readonly object hwidGate = new object();
        static byte[]? cachedHwidEntropy;

        static int QueryWinVerifyTrust(string? filePath = null) {
            try {
                string? path = filePath ?? Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return TRUST_E_NOSIGNATURE;

                var fileInfo = new WINTRUST_FILE_INFO {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = path,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                bool marshaled = false;
                try {
                    Marshal.StructureToPtr(fileInfo, pFileInfo, false);
                    marshaled = true;
                    var data = new WINTRUST_DATA {
                        cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                        dwUIChoice = WTD_UI_NONE,
                        fdwRevocationChecks = WTD_REVOKE_NONE,
                        dwUnionChoice = WTD_CHOICE_FILE,
                        pFile = pFileInfo,
                        dwStateAction = WTD_STATEACTION_VERIFY,
                        dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL
                    };

                    int status = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
                    data.dwStateAction = WTD_STATEACTION_CLOSE;
                    _ = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
                    return status;
                } finally {
                    if (marshaled) Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFileInfo);
                    Marshal.FreeHGlobal(pFileInfo);
                }
            } catch {
                return TRUST_E_NOSIGNATURE;
            }
        }

        internal static bool VerifyAuthenticodeSignature(string? filePath = null) {
            int status = QueryWinVerifyTrust(filePath);
            if (filePath == null) cachedTrustStatus = status;
            return status == 0;
        }

        internal static bool IsDebuggerDetected() {
#if DEBUG
            return false;
#else
            if (Program.IsTestBuild) return false;
            try {
                if (Debugger.IsAttached || IsDebuggerPresent()) return true;
                bool remote = false;
                using (var proc = Process.GetCurrentProcess()) {
                    if (CheckRemoteDebuggerPresent(proc.Handle, out remote) && remote)
                        return true;
                }
            } catch { }
            return false;
#endif
        }

        internal static bool IsBinaryIntact() {
#if DEBUG
            return true;
#else
            if (Program.IsTestBuild) return true;
            string? proc = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(proc)) return false;
            string name = Path.GetFileName(proc);
            if (string.Equals(name, "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return true;

            int status = cachedTrustStatus;
            if (status == int.MinValue) {
                status = QueryWinVerifyTrust(proc);
                cachedTrustStatus = status;
            }

            if (status == TRUST_E_BAD_DIGEST) return false;
            return status == 0 || status == CERT_E_UNTRUSTEDROOT || status == CERT_E_CHAINING;
#endif
        }

        internal static bool IsFileSignatureIntact(string? filePath) {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            int status = QueryWinVerifyTrust(filePath);
            return status != TRUST_E_BAD_DIGEST;
        }

        internal static string VerifyReleaseAuthenticity() {
            string status = "official";
            try {
                bool endpointsValid = string.Equals(UpdateService.Repository, "Zilore-Anatoliy/ZiloreMiniMap", StringComparison.Ordinal)
                    && string.Equals(UpdateService.WebsiteHost, "minimap.zilore.app", StringComparison.Ordinal);
                bool binaryIntact = IsBinaryIntact();
                bool trustedSig = VerifyAuthenticodeSignature();
                if (!endpointsValid) status = "modified-endpoints";
                else if (!binaryIntact) status = "tampered-binary";
                else if (!trustedSig) status = "self-signed-or-unsigned";
                string msg = "v" + MapWindow.VersionString + " (" + MapWindow.BuildNumber + ") · status=" + status
                    + " · host=" + UpdateService.WebsiteHost + " · repo=" + UpdateService.Repository;
                if (status == "official" || status == "self-signed-or-unsigned") Program.LogInfo("Authenticity", msg);
                else Program.LogWarn("Authenticity", "Non-standard build detected: " + msg);
            } catch (Exception ex) {
                Program.LogException("Authenticity", ex);
                status = "check-error";
            }
            return status;
        }

        internal static bool IsRuntimeSafe() {
            return !IsDebuggerDetected() && IsBinaryIntact();
        }

        internal static byte[] GetHardwareEntropy(string? purpose) {
            byte[]? baseEntropy = cachedHwidEntropy;
            if (baseEntropy == null) {
                lock (hwidGate) {
                    if (cachedHwidEntropy == null) {
                        string machineGuid = "";
                        try {
                            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                            using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")) {
                                if (key != null) machineGuid = Convert.ToString(key.GetValue("MachineGuid")) ?? "";
                            }
                        } catch { }

                        uint serial = 0;
                        try {
                            string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
                            _ = GetVolumeInformation(sysDrive, IntPtr.Zero, 0, out serial, out _, out _, IntPtr.Zero, 0);
                        } catch { }

                        string raw = "ZLR-HWID::" + machineGuid + "::" + serial.ToString("X8") + "::" + Environment.MachineName;
                        cachedHwidEntropy = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
                    }
                    baseEntropy = cachedHwidEntropy;
                }
            }

            byte[] p = Encoding.UTF8.GetBytes(purpose ?? "default");
            byte[] combined = new byte[baseEntropy.Length + p.Length];
            Buffer.BlockCopy(baseEntropy, 0, combined, 0, baseEntropy.Length);
            Buffer.BlockCopy(p, 0, combined, baseEntropy.Length, p.Length);
            return SHA256.HashData(combined);
        }

        internal static string GetHardwareId() {
            byte[] h = GetHardwareEntropy("ZiloreMiniMap-HWID-Public-v1");
            return Convert.ToHexStringLower(h, 0, 16);
        }
    }
}
