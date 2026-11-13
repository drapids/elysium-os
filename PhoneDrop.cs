using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Heft.Ui;

namespace Heft
{
    internal sealed class PhoneDrop : IDisposable
    {
        const long MaxBytes = 8L * 1024 * 1024 * 1024;

        readonly Settings _s;
        readonly Notifier _notes;
        readonly DispatcherTimer _expire = new();
        TcpListener? _listener;
        CancellationTokenSource? _cts;
        PhoneDropWindow? _win;
        string _token = "";
        int _got;

        public event Action<string>? Received;

        public PhoneDrop(Settings s, Notifier notes)
        {
            _s = s;
            _notes = notes;
            _expire.Tick += (_, _) => Stop();
        }

        public static string Folder => Path.Combine(Library.DownloadsDir, "from phone");

        public bool Active => _listener != null;

        public void Toggle()
        {
            if (Active) Stop();
            else Start();
        }

        public void Start()
        {
            Stop();
            string? ip = QuickShare.LanIp();
            if (ip == null)
            {
                _notes.Post(new Note { Title = "Not on a network", Body = "your pc and phone need to be on the same wi-fi", Glyph = "\uE8EA" });
                return;
            }
            try
            {
                _token = Guid.NewGuid().ToString("N")[..12];
                _listener = new TcpListener(IPAddress.Any, 0);
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _cts = new CancellationTokenSource();
                _got = 0;
                var listener = _listener;
                var token = _cts.Token;
                _ = Task.Run(() => Serve(listener, token));
                string url = $"http://{ip}:{port}/{_token}";
                int minutes = Math.Clamp(_s.ShareMinutes, 1, 120);
                _expire.Interval = TimeSpan.FromMinutes(minutes);
                _expire.Start();
                _win = new PhoneDropWindow(url, minutes);
                _win.StopRequested += Stop;
                _win.Open();
                PhysicsEngine.Log("phone drop open at " + url);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("phone drop failed " + ex.Message);
                Stop();
                _notes.Post(new Note { Title = "Couldn't open phone drop", Body = ex.Message, Glyph = "\uE8EA" });
            }
        }

        public void Stop()
        {
            _expire.Stop();
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }
            try
            {
                _listener?.Stop();
            }
            catch
            {
            }
            _listener = null;
            _cts = null;
            var w = _win;
            _win = null;
            if (w != null)
            {
                w.StopRequested -= Stop;
                w.Close2();
            }
        }

        async Task Serve(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch
                {
                    return;
                }
                _ = Task.Run(() => Handle(client, token));
            }
        }

        static async Task<(string head, byte[] extra)> ReadHead(NetworkStream stream, CancellationToken token)
        {
            var buf = new byte[16384];
            int len = 0;
            while (len < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(len, buf.Length - len), token);
                if (n <= 0) break;
                len += n;
                for (int i = 3; i < len; i++)
                {
                    if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n')
                    {
                        string head = Encoding.UTF8.GetString(buf, 0, i + 1);
                        var extra = buf.AsSpan(i + 1, len - i - 1).ToArray();
                        return (head, extra);
                    }
                }
            }
            return (Encoding.UTF8.GetString(buf, 0, len), Array.Empty<byte>());
        }

        static async Task Reply(NetworkStream stream, string status, string type, string body, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(body);
            var head = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {data.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, token);
            await stream.WriteAsync(data, token);
        }

        static string SafeName(string raw)
        {
            string name = Path.GetFileName(raw.Replace('/', '\\'));
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().Trim('.');
            if (name.Length == 0) name = "from phone " + DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            if (name.Length > 140) name = name[..100] + Path.GetExtension(name);
            return name;
        }

        static string Unique(string dir, string name)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) return path;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                path = Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(path)) return path;
            }
            return Path.Combine(dir, stem + " " + Guid.NewGuid().ToString("N")[..6] + ext);
        }

        async Task Handle(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    client.SendTimeout = 30000;
                    var stream = client.GetStream();
                    var (head, extra) = await ReadHead(stream, token);
                    var lines = head.Split("\r\n");
                    string[] first = lines[0].Split(' ');
                    if (first.Length < 2)
                    {
                        await Reply(stream, "400 Bad Request", "text/plain", "nope", token);
                        return;
                    }
                    string method = first[0];
                    string target = first[1];
                    string path = Uri.UnescapeDataString(target.Split('?')[0]);
                    string page = "/" + _token;
                    if (method == "GET" && (path == page || path == page + "/"))
                    {
                        await Reply(stream, "200 OK", "text/html; charset=utf-8", Page(), token);
                        return;
                    }
                    if (method == "POST" && path == page + "/up")
                    {
                        long length = -1;
                        foreach (var l in lines.Skip(1))
                        {
                            int colon = l.IndexOf(':');
                            if (colon <= 0) continue;
                            if (l[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                long.TryParse(l[(colon + 1)..].Trim(), out length);
                        }
                        if (length < 0)
                        {
                            await Reply(stream, "411 Length Required", "text/plain", "need a length", token);
                            return;
                        }
                        if (length > MaxBytes)
                        {
                            await Reply(stream, "413 Payload Too Large", "text/plain", "too big", token);
                            return;
                        }
                        string query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
                        string rawName = "";
                        foreach (var pair in query.Split('&'))
                        {
                            var kv = pair.Split('=', 2);
                            if (kv.Length == 2 && kv[0] == "name") rawName = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
                        }
                        string dir = Folder;
                        Directory.CreateDirectory(dir);
                        string file = Unique(dir, SafeName(rawName));
                        string part = file + ".heftpart";
                        long got = 0;
                        try
                        {
                            using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                            {
                                if (extra.Length > 0)
                                {
                                    int take = (int)Math.Min(extra.Length, length);
                                    await fs.WriteAsync(extra.AsMemory(0, take), token);
                                    got += take;
                                }
                                var buf = new byte[1 << 16];
                                while (got < length)
                                {
                                    int n = await stream.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, length - got)), token);
                                    if (n <= 0) break;
                                    await fs.WriteAsync(buf.AsMemory(0, n), token);
                                    got += n;
                                }
                            }
                            if (got < length) throw new IOException("the upload got cut off");
                            File.Move(part, file);
                        }
                        catch
                        {
                            try
                            {
                                File.Delete(part);
                            }
                            catch
                            {
                            }
                            throw;
                        }
                        await Reply(stream, "200 OK", "text/plain", "ok", token);
                        int count = Interlocked.Increment(ref _got);
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _notes.Post(new Note { App = "heft", Title = "From your phone", Body = Path.GetFileName(file), Glyph = "\uE8EA", Open = file });
                            _win?.Got(count, Path.GetFileName(file));
                            Received?.Invoke(file);
                        }));
                        return;
                    }
                    await Reply(stream, "404 Not Found", "text/plain", "not here", token);
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("phone drop request failed " + ex.Message);
                }
            }
        }

        string Page()
        {
            string up = "/" + _token + "/up";
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>send to pc</title>"
                   + "<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#0f1012;color:#f2f2f4;font-family:-apple-system,system-ui,Segoe UI,sans-serif}"
                   + ".c{width:min(88vw,380px);padding:28px 22px;border-radius:22px;background:#1b1c20;border:1px solid #2c2d33;text-align:center}"
                   + ".t{font-size:19px;font-weight:600;margin:4px 0 4px}.s{color:#9a9ba3;font-size:13px}"
                   + "label{display:block;margin-top:22px;padding:15px;border-radius:14px;background:#0a84ff;color:#fff;font-weight:600;cursor:pointer}"
                   + "input{display:none}#l{margin-top:16px;text-align:left;font-size:13px}.r{display:flex;justify-content:space-between;gap:10px;padding:8px 2px;border-top:1px solid #2c2d33}"
                   + ".r span:first-child{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.ok{color:#30d158}.bad{color:#ff6b5e}</style></head><body><div class=\"c\">"
                   + "<div class=\"s\">heft on your pc</div><div class=\"t\">send stuff over</div><div class=\"s\">photos, videos, anything. it lands in Downloads</div>"
                   + "<label>pick files<input id=\"f\" type=\"file\" multiple></label><div id=\"l\"></div></div>"
                   + "<script>const up='" + up + "';const l=document.getElementById('l');"
                   + "function row(n){const d=document.createElement('div');d.className='r';const a=document.createElement('span');a.textContent=n;const b=document.createElement('span');b.textContent='0%';d.append(a,b);l.prepend(d);return b}"
                   + "function send(f){return new Promise(r=>{const p=row(f.name);const x=new XMLHttpRequest();x.open('POST',up+'?name='+encodeURIComponent(f.name));"
                   + "x.upload.onprogress=e=>{if(e.lengthComputable)p.textContent=Math.round(e.loaded/e.total*100)+'%'};"
                   + "x.onload=()=>{p.textContent=x.status==200?'sent':'failed';p.className=x.status==200?'ok':'bad';r()};x.onerror=()=>{p.textContent='failed';p.className='bad';r()};x.send(f)})}"
                   + "document.getElementById('f').onchange=async e=>{for(const f of e.target.files)await send(f);e.target.value=''}</script></body></html>";
        }

        public void Dispose() => Stop();
    }

    internal sealed class PhoneDropWindow : Window
    {
        readonly Border _surface;
        readonly TextBlock _status;
        readonly string _url;

        public event Action? StopRequested;

        public PhoneDropWindow(string url, int minutes)
        {
            _url = url;
            Glass.Attach(this, 20);
            Title = "heft phone drop";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(20);

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Width = 300 };
            var head = new DockPanel();
            var close = Kit.Plain("\uE8BB", () => StopRequested?.Invoke(), "stop", 10);
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(Kit.Text("send from your phone", 16, true));
            root.Children.Add(head);
            var cap = Kit.Caption("scan it, pick files, they show up here. same wi-fi only");
            cap.TextWrapping = TextWrapping.Wrap;
            cap.Margin = new Thickness(0, 2, 0, 14);
            root.Children.Add(cap);

            var qrHost = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Width = 252, Height = 252, HorizontalAlignment = HorizontalAlignment.Center };
            var img = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            img.Source = ShareWindow.Qr(url);
            qrHost.Child = img;
            root.Children.Add(qrHost);

            var meta = Kit.Text($"open for {minutes} min  ·  saves to Downloads\\from phone", 11.5, false, "SubText");
            meta.HorizontalAlignment = HorizontalAlignment.Center;
            meta.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(meta);

            var link = new TextBox { Text = url, IsReadOnly = true, Style = (Style)Application.Current.FindResource("Field"), Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5 };
            root.Children.Add(link);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            var folder = Kit.Push("Open folder", () =>
            {
                Directory.CreateDirectory(PhoneDrop.Folder);
                Files.Open(PhoneDrop.Folder);
            });
            folder.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(folder);
            buttons.Children.Add(Kit.Push("Done", () => StopRequested?.Invoke(), true));
            root.Children.Add(buttons);

            _status = Kit.Caption("if windows asks about the firewall, allow it on private networks");
            _status.TextWrapping = TextWrapping.Wrap;
            _status.TextAlignment = TextAlignment.Center;
            _status.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(_status);

            _surface.Child = root;
            Content = _surface;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                StopRequested?.Invoke();
            };
        }

        public void Got(int count, string name)
        {
            _status.Text = count == 1 ? "got " + name : $"got {count} files. last one: {name}";
            _status.SetResourceReference(TextBlock.ForegroundProperty, "AccentFill");
        }

        public void Open()
        {
            var d = Displays.AtCursor();
            Glass.Present(this, _surface, () =>
            {
                var h = new WindowInteropHelper(this).Handle;
                _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _surface.DesiredSize;
                int w = (int)(Math.Max(300, size.Width) * d.Scale), hp = (int)(Math.Max(400, size.Height) * d.Scale);
                Native.SetWindowPos(h, Native.HWND_TOPMOST, d.Work.Left + (d.Work.Width - w) / 2, d.Work.Top + Math.Max(10, (d.Work.Height - hp) / 2), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }, 10, 0.96, 240);
            Activate();
        }

        public void Close2()
        {
            Glass.Dismiss(this, _surface, () =>
            {
                try
                {
                    Close();
                }
                catch
                {
                }
            });
        }
    }
}
