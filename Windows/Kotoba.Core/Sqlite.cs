using System.Runtime.InteropServices;
using System.Text;

// Small adapter for Windows' supported SQLite API; no downloaded native DLL required.
namespace Kotoba.Storage;

public enum SqliteOpenMode { ReadOnly, ReadWriteCreate }
public sealed class SqliteConnectionStringBuilder
{
    public string DataSource { get; set; } = "";
    public SqliteOpenMode Mode { get; set; }
    public override string ToString() => (Mode == SqliteOpenMode.ReadOnly ? "ro:" : "rw:") + DataSource;
}
public sealed class SqliteException(string message) : Exception(message);
public sealed class SqliteConnection(string connectionString) : IDisposable
{
    internal IntPtr Handle;
    public void Open()
    {
        int rc = Native.sqlite3_open_v2(Utf8(connectionString[3..]), out Handle, (connectionString.StartsWith("ro:") ? 1 : 6) | 0x10000, IntPtr.Zero);
        if (rc != 0) { var message = Error; Dispose(); throw new SqliteException(message); }
        Native.sqlite3_busy_timeout(Handle, 10000);
    }
    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
    internal string Error => Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(Handle)) ?? "SQLite error";
    public SqliteCommand CreateCommand() => new(this);
    public void Dispose() { if (Handle != IntPtr.Zero) { Native.sqlite3_close_v2(Handle); Handle = IntPtr.Zero; } }
}
public sealed class SqliteParameterCollection : List<(string Name, object Value)>
{
    public void AddWithValue(string name, object value) => Add((name, value));
}
public sealed class SqliteCommand(SqliteConnection db) : IDisposable
{
    public string CommandText { get; set; } = "";
    public SqliteParameterCollection Parameters { get; } = [];
    public SqliteDataReader ExecuteReader()
    {
        int rc = Native.sqlite3_prepare_v2(db.Handle, SqliteConnection.Utf8(CommandText), -1, out var stmt, IntPtr.Zero);
        if (rc != 0) throw new SqliteException(db.Error);
        try
        {
            foreach (var (name, value) in Parameters)
            {
                int index = Native.sqlite3_bind_parameter_index(stmt, SqliteConnection.Utf8(name));
                if (index == 0) throw new SqliteException("Unknown parameter " + name);
                rc = value is DBNull ? Native.sqlite3_bind_null(stmt, index) : value is double d ? Native.sqlite3_bind_double(stmt, index, d) : value is int n ? Native.sqlite3_bind_int64(stmt, index, n) : value is long l ? Native.sqlite3_bind_int64(stmt, index, l) : Native.sqlite3_bind_text(stmt, index, SqliteConnection.Utf8(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!), -1, new IntPtr(-1));
                if (rc != 0) throw new SqliteException(db.Error);
            }
            return new(db, stmt);
        }
        catch { Native.sqlite3_finalize(stmt); throw; }
    }
    public void Dispose() { }
}
public sealed class SqliteDataReader(SqliteConnection db, IntPtr stmt) : IDisposable
{
    public bool Read() { int rc = Native.sqlite3_step(stmt); if (rc == 100) return true; if (rc == 101) return false; throw new SqliteException(db.Error); }
    public int FieldCount => Native.sqlite3_column_count(stmt);
    public bool IsDBNull(int i) => Native.sqlite3_column_type(stmt, i) == 5;
    public string GetString(int i) => Marshal.PtrToStringUTF8(Native.sqlite3_column_text(stmt, i)) ?? "";
    public int GetInt32(int i) => (int)Native.sqlite3_column_int64(stmt, i);
    public void Dispose() => Native.sqlite3_finalize(stmt);
}
internal static class Native
{
    private const string Lib = "winsqlite3.dll";
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_parameter_index(IntPtr stmt, byte[] name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_double(IntPtr stmt, int i, double value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr stmt, int i, long value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr stmt, int i, byte[] value, int bytes, IntPtr destructor);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr stmt, int i);
}
