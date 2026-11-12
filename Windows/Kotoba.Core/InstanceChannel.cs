using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Kotoba.Core;
public static class InstanceChannel
{
    public static async Task Send(string pipe,string message,CancellationToken cancellation=default,bool allowForeground=false)
    {
        using var client=new NamedPipeClientStream(".",pipe,PipeDirection.Out);
        await client.ConnectAsync(5000,cancellation);
        if(allowForeground&&GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out var process)) AllowSetForegroundWindow(process);
        var bytes=System.Text.Encoding.UTF8.GetBytes(message+"\n");
        await client.WriteAsync(bytes,cancellation);await client.FlushAsync(cancellation);
    }
    public static async Task<string?> Receive(string pipe,CancellationToken cancellation=default)
    {
        using var server=new NamedPipeServerStream(pipe,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(cancellation);
        using var reader=new StreamReader(server);return await reader.ReadLineAsync(cancellation);
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint processId);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool AllowSetForegroundWindow(uint processId);
}
