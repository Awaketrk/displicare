using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Displicare.Rooms
{
    /// <summary>
    /// Lets "Displicare.exe --room Living room" (a Steam shortcut, a Stream Deck button) hand the switch to the running
    /// app, so there is one prompt and one place that switches. One line per connection: arguments separated by tabs.
    /// </summary>
    internal sealed class CommandPipe : IDisposable
    {
        static string PipeName { get { return "Displicare-" + Environment.UserName; } }
        /// <summary>A room name is short; anything longer (in bytes) isn't a command from us.</summary>
        internal const int MaxLineLength = 1024;
        /// <summary>Our own client sends at once; one that hasn't finished by then is stuck or not ours.</summary>
        const int ReadTimeoutMs = 2000, ReplyTimeoutMs = 1000;

        readonly Action<string[]> handler;
        readonly SynchronizationContext ui;
        readonly Thread thread;
        volatile bool stopping;

        public CommandPipe(Action<string[]> handler)
        {
            this.handler = handler;
            ui = SynchronizationContext.Current;
            thread = new Thread(Listen);
            thread.IsBackground = true;
            thread.Name = "Displicare command pipe";
            thread.Start();
        }

        void Listen()
        {
            while (!stopping)
            {
                try
                {
                    // Asynchronous (overlapped) so a client that stalls can be cut off: closing the pipe cancels the read
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, OwnerOnly()))
                    {
                        server.WaitForConnection();
                        if (stopping) return;
                        string line = ReadCommand(server);
                        // No reply to a stalled or oversized command: the client gives up, and we listen again
                        if (string.IsNullOrEmpty(line)) continue;
                        byte[] ok = Encoding.UTF8.GetBytes("ok\n");
                        server.WriteAsync(ok, 0, ok.Length).Wait(ReplyTimeoutMs);
                        string[] args = line.Split('\t');
                        ui.Post(delegate { handler(args); }, null);
                    }
                }
                catch (Exception ex)
                {
                    if (stopping) return;
                    RoomLog.Write("Command pipe: " + ex.Message);
                    Thread.Sleep(500);
                }
            }
        }

        /// <summary>
        /// Only this Windows account may connect, and never over the network. The default pipe security also lets
        /// everyone read, and remote machines reach pipes through SMB.
        /// </summary>
        static PipeSecurity OwnerOnly()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return security;
        }

        /// <summary>
        /// The command line a client sends, or null if it takes longer than <see cref="ReadTimeoutMs"/> or runs past
        /// <see cref="MaxLineLength"/> bytes. Without these limits one stuck client would block the pipe for good.
        /// </summary>
        static string ReadCommand(Stream stream)
        {
            var buffer = new byte[MaxLineLength + 1];
            int count = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(ReadTimeoutMs);
            while (count < buffer.Length)
            {
                int wait = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (wait <= 0) return null;
                var read = stream.ReadAsync(buffer, count, buffer.Length - count);
                if (!read.Wait(wait)) return null;
                if (read.Result == 0) break; // client closed: take what it sent
                count += read.Result;
                string line = LineFrom(buffer, count, false);
                if (line != null) return line;
            }
            return count < buffer.Length ? LineFrom(buffer, count, true) : null;
        }

        /// <summary>
        /// The text up to the first newline (a trailing CR dropped), or null when there is no newline yet. With
        /// <paramref name="ended"/>, everything received counts as the line.
        /// </summary>
        internal static string LineFrom(byte[] data, int count, bool ended)
        {
            int end = Array.IndexOf(data, (byte)'\n', 0, count);
            if (end < 0)
            {
                if (!ended) return null;
                end = count;
            }
            if (end > 0 && data[end - 1] == '\r') end--;
            return Encoding.UTF8.GetString(data, 0, end);
        }

        /// <summary>Sends the arguments to a running tray app. False when none is listening.</summary>
        public static bool Send(string[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    client.Connect(1500);
                    var writer = new StreamWriter(client, new UTF8Encoding(false));
                    writer.WriteLine(string.Join("\t", args));
                    writer.Flush();
                    return new StreamReader(client, Encoding.UTF8).ReadLine() == "ok";
                }
            }
            catch (Exception) { return false; }
        }

        public void Dispose()
        {
            stopping = true;
            // Wake the listener out of WaitForConnection.
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut)) client.Connect(200);
            }
            catch (Exception) { }
        }
    }
}
