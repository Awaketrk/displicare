using System;
using System.IO;
using System.IO.Pipes;
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
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1))
                    {
                        server.WaitForConnection();
                        if (stopping) return;
                        var reader = new StreamReader(server, Encoding.UTF8);
                        string line = reader.ReadLine();
                        var writer = new StreamWriter(server, new UTF8Encoding(false));
                        writer.WriteLine("ok");
                        writer.Flush();
                        if (!string.IsNullOrEmpty(line))
                        {
                            string[] args = line.Split('\t');
                            ui.Post(delegate { handler(args); }, null);
                        }
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
