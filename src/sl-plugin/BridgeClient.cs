using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using Logger = LabApi.Features.Console.Logger;

namespace SlBridge
{
    /// <summary>
    /// TCP-клиент к моду Minecraft. Сам переподключается. Входящие сообщения складываются в очередь
    /// и разбираются на главном потоке Unity (через <see cref="Drain"/>).
    /// </summary>
    public sealed class BridgeClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly ConcurrentQueue<Dictionary<string, object>> _inbox = new ConcurrentQueue<Dictionary<string, object>>();
        private readonly object _writeLock = new object();
        private volatile bool _running;
        private volatile StreamWriter _writer;
        private TcpClient _tcp;
        private Thread _thread;

        public bool Connected => _writer != null;
        public event Action OnConnected;

        public BridgeClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "SlBridge-MC" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            Close();
        }

        private void Loop()
        {
            bool warned = false;
            while (_running)
            {
                try
                {
                    _tcp = new TcpClient { NoDelay = true };
                    _tcp.Connect(_host, _port);
                    var stream = _tcp.GetStream();
                    var reader = new StreamReader(stream, new UTF8Encoding(false));
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
                    Logger.Info(L.T($"[SlBridge] Подключено к Minecraft {_host}:{_port}", $"[SlBridge] Connected to Minecraft {_host}:{_port}"));
                    warned = false;
                    _connectedFlag = true;
                    string line;
                    while (_running && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        try
                        {
                            var obj = MiniJson.ParseObject(line);
                            if (obj != null) _inbox.Enqueue(obj);
                        }
                        catch (Exception e)
                        {
                            Logger.Warn(L.T($"[SlBridge] Плохое сообщение от Minecraft: {e.Message}", $"[SlBridge] Bad message from Minecraft: {e.Message}"));
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!warned)
                    {
                        Logger.Warn(L.T($"[SlBridge] Minecraft недоступен ({e.Message}). Жду, пока запустится мир с модом…", $"[SlBridge] Minecraft is not reachable ({e.Message}). Waiting for a world with the mod…"));
                        warned = true;
                    }
                }
                Close();
                if (_running) Thread.Sleep(2000);
            }
        }

        private volatile bool _connectedFlag;

        private void Close()
        {
            _writer = null;
            try { _tcp?.Close(); } catch { }
            _tcp = null;
        }

        /// <summary>Вызывать с главного потока каждый кадр.</summary>
        public void Drain(Action<Dictionary<string, object>> handler)
        {
            if (_connectedFlag)
            {
                _connectedFlag = false;
                OnConnected?.Invoke();
            }
            int n = 0;
            while (n++ < 2000 && _inbox.TryDequeue(out var m))
            {
                try { handler(m); }
                catch (Exception e) { Logger.Error(L.T($"[SlBridge] Ошибка обработки: {e}", $"[SlBridge] Handler error: {e}")); }
            }
        }

        public void Send(string json)
        {
            var w = _writer;
            if (w == null) return;
            lock (_writeLock)
            {
                try
                {
                    w.Write(json);
                    w.Write('\n');
                    w.Flush();
                }
                catch
                {
                    Close();
                }
            }
        }
    }
}
