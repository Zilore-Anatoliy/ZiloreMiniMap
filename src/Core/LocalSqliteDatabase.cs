#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ZiloreMiniMap {
    internal sealed partial class LocalSqliteDatabase : IDisposable {
        IntPtr handle;

        internal LocalSqliteDatabase(string path, Action<LocalSqliteDatabase> initialize) {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(initialize);
            int result = Sqlite.Open(Encoding.UTF8.GetBytes(path + "\0"), out handle, 0x02 | 0x04 | 0x10000, IntPtr.Zero);
            try {
                Check(result);
                Check(Sqlite.BusyTimeout(handle, 2000));
                Execute("PRAGMA foreign_keys=ON");
                Execute("PRAGMA synchronous=FULL");
                initialize(this);
            } catch {
                Dispose();
                throw;
            }
        }

        internal void Check(int result) {
            if (result != 0) throw new IOException("SQLite: " + Utf8(Sqlite.Error(handle)) + " (" + result.ToString(CultureInfo.InvariantCulture) + ")");
        }

        internal Statement Prepare(string sql, params object[] values) {
            return new Statement(this, handle, sql, values);
        }

        internal void Execute(string sql, params object[] values) {
            using (var statement = Prepare(sql, values)) {
                while (statement.Read()) { }
            }
        }

        internal long ScalarLong(string sql, params object[] values) {
            using (var statement = Prepare(sql, values)) {
                if (!statement.Read()) throw new IOException(Localization.Get("SpawnCartInvalid"));
                return statement.Long(0);
            }
        }

        internal void Transaction(Action action) {
            Execute("BEGIN IMMEDIATE");
            try {
                action();
                Execute("COMMIT");
            } catch {
                SafeCleanup.Try(() => Execute("ROLLBACK"));
                throw;
            }
        }

        public void Dispose() {
            if (handle != IntPtr.Zero) {
                _ = Sqlite.Close(handle);
                handle = IntPtr.Zero;
            }
        }

        internal sealed class Statement : IDisposable {
            readonly LocalSqliteDatabase database;
            IntPtr handle;

            internal Statement(LocalSqliteDatabase database, IntPtr connection, string sql, object[] values) {
                this.database = database;
                try {
                    byte[] encoded = Encoding.UTF8.GetBytes(sql + "\0");
                    database.Check(Sqlite.Prepare(connection, encoded, encoded.Length, out handle, IntPtr.Zero));
                    for (int index = 0; index < values.Length; index++) {
                        string? text = values[index] as string;
                        if (text != null) {
                            byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
                            database.Check(Sqlite.BindText(handle, index + 1, bytes, bytes.Length - 1, new IntPtr(-1)));
                        } else {
                            database.Check(Sqlite.BindLong(handle, index + 1, Convert.ToInt64(values[index], CultureInfo.InvariantCulture)));
                        }
                    }
                } catch {
                    Dispose();
                    throw;
                }
            }

            internal bool Read() {
                int result = Sqlite.Step(handle);
                if (result == 100) return true;
                if (result == 101) return false;
                database.Check(result);
                return false;
            }

            internal long Long(int column) {
                if (Sqlite.ColumnType(handle, column) != 1) throw new FormatException(Localization.Get("SpawnCartInvalid"));
                return Sqlite.ColumnLong(handle, column);
            }

            internal string Text(int column) {
                if (Sqlite.ColumnType(handle, column) != 3) throw new FormatException(Localization.Get("SpawnCartInvalid"));
                IntPtr pointer = Sqlite.ColumnText(handle, column);
                int length = Sqlite.ColumnBytes(handle, column);
                if (length > 4096) throw new FormatException(Localization.Get("SpawnCartInvalid"));
                if (pointer == IntPtr.Zero || length <= 0) return "";
                return Marshal.PtrToStringUTF8(pointer, length) ?? "";
            }

            public void Dispose() {
                if (handle != IntPtr.Zero) {
                    _ = Sqlite.Finalize(handle);
                    handle = IntPtr.Zero;
                }
            }
        }

        static string Utf8(IntPtr pointer) {
            if (pointer == IntPtr.Zero) return "Unknown database error";
            int length = 0;
            while (length < 4096 && Marshal.ReadByte(pointer, length) != 0) length++;
            return length == 0 ? "" : (Marshal.PtrToStringUTF8(pointer, length) ?? "");
        }

        static partial class Sqlite {
            const string Library = "winsqlite3.dll";

            [LibraryImport(Library, EntryPoint = "sqlite3_open_v2")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int Open(byte[] path, out IntPtr database, int flags, IntPtr vfs);

            [LibraryImport(Library, EntryPoint = "sqlite3_close_v2")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int Close(IntPtr database);

            [LibraryImport(Library, EntryPoint = "sqlite3_busy_timeout")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int BusyTimeout(IntPtr database, int milliseconds);

            [LibraryImport(Library, EntryPoint = "sqlite3_errmsg")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial IntPtr Error(IntPtr database);

            [LibraryImport(Library, EntryPoint = "sqlite3_prepare_v2")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int Prepare(IntPtr database, byte[] sql, int length, out IntPtr statement, IntPtr tail);

            [LibraryImport(Library, EntryPoint = "sqlite3_bind_text")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int BindText(IntPtr statement, int index, byte[] text, int length, IntPtr destructor);

            [LibraryImport(Library, EntryPoint = "sqlite3_bind_int64")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int BindLong(IntPtr statement, int index, long value);

            [LibraryImport(Library, EntryPoint = "sqlite3_step")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int Step(IntPtr statement);

            [LibraryImport(Library, EntryPoint = "sqlite3_column_int64")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial long ColumnLong(IntPtr statement, int column);

            [LibraryImport(Library, EntryPoint = "sqlite3_column_text")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial IntPtr ColumnText(IntPtr statement, int column);

            [LibraryImport(Library, EntryPoint = "sqlite3_column_bytes")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int ColumnBytes(IntPtr statement, int column);

            [LibraryImport(Library, EntryPoint = "sqlite3_column_type")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int ColumnType(IntPtr statement, int column);

            [LibraryImport(Library, EntryPoint = "sqlite3_finalize")]
            [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static partial int Finalize(IntPtr statement);
        }
    }
}
