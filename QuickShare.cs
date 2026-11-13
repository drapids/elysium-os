using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
    internal sealed class QuickShare : IDisposable
    {
        readonly Settings _s;
        readonly Notifier _notes;
        readonly DispatcherTimer _expire = new();
        TcpListener? _listener;
        CancellationTokenSource? _cts;
        ShareWindow? _win;
        string _token = "";
        string _file = "";
        string _name = "";
        string? _tempDir;
        int _sent;

        public QuickShare(Settings s, Notifier notes)
        {
            _s = s;
            _notes = notes;
            _expire.Tick += (_, _) => Stop();
        }

        public bool Active => _listener != null;

        public void Share(IList<string> paths)
        {
            var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (list.Count == 0)
            {
                _notes.Post(new Note { Title = "Nothing to send", Body = "pick a file first", Glyph = "\uE8EA" });
                return;
            }
            Stop();

            string? ip = LanIp();
            if (ip == null)
            {
                _notes.Post(new Note { Title = "Not on a network", Body = "your pc and phone need to be on the same wi-fi", Glyph = "\uE8EA" });
                return;
            }

            try
            {
                if (list.Count == 1 && File.Exists(list[0]))
                {
                    _file = list[0];
                    _name = Path.GetFileName(_file);
                }
                else
                {
                    _tempDir = Path.Combine(Path.GetTempPath(), "heft-share-" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(_tempDir);
                    string baseName = list.Count == 1 ? Path.GetFileName(list[0].TrimEnd('\\')) : $"{list.Count} files";
                    _name = baseName + ".zip";
                    _file = Path.Combine(_tempDir, _name);
                    using (var zip = ZipFile.Open(_file, ZipArchiveMode.Create))
                    {
                        foreach (var p in list)
                        {
                            if (File.Exists(p))
                            {
                                zip.CreateEntryFromFile(p, Path.GetFileName(p), CompressionLevel.Fastest);
                            }
                            else
                            {
                                string root = Path.GetDirectoryName(p.TrimEnd('\\')) ?? p;
                                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                                    zip.CreateEntryFromFile(f, Path.GetRelativePath(root, f), CompressionLevel.Fastest);
                            }
                        }
                    }
                }

                _token = Guid.NewGuid().ToString("N")[..12];
                _listener = new TcpListener(IPAddress.Any, 0);
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _cts = new CancellationTokenSource();
                _sent = 0;
                var listener = _listener;
                var token = _cts.Token;
                _ = Task.Run(() => Serve(listener, token));

                string url = $"http://{ip}:{port}/{_token}";
                long size = new FileInfo(_file).Length;
                int minutes = Math.Clamp(_s.ShareMinutes, 1, 120);
                _expire.Interval = TimeSpan.FromMinutes(minutes);
                _expire.Start();
                _win = new ShareWindow(url, _name, size, minutes);
                _win.StopRequested += Stop;
                _win.Open();
                PhysicsEngine.Log("sharing " + _name + " at " + url);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("share failed " + ex.Message);
                Stop();
                _notes.Post(new Note { Title = "Couldn't start sharing", Body = ex.Message, Glyph = "\uE8EA" });
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
            if (_tempDir != null)
            {
                string dir = _tempDir;
                _tempDir = null;
                Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                    }
                });
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

        async Task Handle(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 30000;
                    var stream = client.GetStream();
                    string head = await ReadHead(stream, token);
                    string[] first = head.Split("\r\n")[0].Split(' ');
                    if (first.Length < 2)
                    {
                        await Reply(stream, "400 Bad Request", "text/plain", "nope", token);
                        return;
                    }
                    string method = first[0];
                    string path = Uri.UnescapeDataString(first[1].Split('?')[0]);
                    string page = "/" + _token;
                    if (path == page || path == page + "/")
                    {
                        await Reply(stream, "200 OK", "text/html; charset=utf-8", Page(), token);
                        return;
                    }
                    if (path.StartsWith(page + "/get", StringComparison.Ordinal))
                    {
                        var info = new FileInfo(_file);
                        if (!info.Exists)
                        {
                            await Reply(stream, "410 Gone", "text/plain", "that file is gone", token);
                            return;
                        }
                        string ascii = new string(_name.Select(c => c < 128 && c != '"' && c != '\\' ? c : '_').ToArray());
                        var sb = new StringBuilder();
                        sb.Append("HTTP/1.1 200 OK\r\n");
                        sb.Append("Content-Type: application/octet-stream\r\n");
                        sb.Append($"Content-Length: {info.Length}\r\n");
                        sb.Append($"Content-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(_name)}\r\n");
                        sb.Append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");
                        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                        await stream.WriteAsync(bytes, token);
                        if (method != "HEAD")
                        {
                            using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, true);
                            await fs.CopyToAsync(stream, 1 << 16, token);
                            if (Interlocked.Increment(ref _sent) == 1)
                            {
                                string n = _name;
                                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _notes.Post(new Note { Title = "Sent to your phone", Body = n, Glyph = "\uE8EA" });
                                    _win?.Sent();
                                }));
                            }
                        }
                        return;
                    }
                    await Reply(stream, "404 Not Found", "text/plain", "not here", token);
                }
                catch
                {
                }
            }
        }

        static async Task<string> ReadHead(NetworkStream stream, CancellationToken token)
        {
            var buf = new byte[8192];
            int len = 0;
            while (len < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(len, buf.Length - len), token);
                if (n <= 0) break;
                len += n;
                string sofar = Encoding.ASCII.GetString(buf, 0, len);
                if (sofar.Contains("\r\n\r\n")) return sofar;
            }
            return Encoding.ASCII.GetString(buf, 0, len);
        }

        static async Task Reply(NetworkStream stream, string status, string type, string body, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(body);
            var head = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {data.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, token);
            await stream.WriteAsync(data, token);
        }

        string Page()
        {
            string name = WebUtility.HtmlEncode(_name);
            string size = Kit.Size(File.Exists(_file) ? new FileInfo(_file).Length : 0);
            string link = "/" + _token + "/get/" + Uri.EscapeDataString(_name);
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + name + "</title>"
                   + "<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#0f1012;color:#f2f2f4;font-family:-apple-system,system-ui,Segoe UI,sans-serif}"
                   + ".c{width:min(86vw,380px);padding:28px 24px;border-radius:22px;background:#1b1c20;border:1px solid #2c2d33;text-align:center}"
                   + ".n{font-size:18px;font-weight:600;word-break:break-word;margin:6px 0 4px}.s{color:#9a9ba3;font-size:13px}"
                   + "a{display:block;margin-top:22px;padding:14px;border-radius:14px;background:#0a84ff;color:#fff;text-decoration:none;font-weight:600}"
                   + ".h{color:#6f7078;font-size:12px;margin-top:14px}</style></head><body><div class=\"c\"><div class=\"s\">heft is sending you</div>"
                   + "<div class=\"n\">" + name + "</div><div class=\"s\">" + size + "</div><a href=\"" + link + "\">download</a>"
                   + "<div class=\"h\">only works while you're on the same wi-fi</div></div></body></html>";
        }

        static string? LanIp()
        {
            var good = new List<(int rank, string ip)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var props = ni.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    bool virt = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("vpn") || desc.Contains("tap-") || desc.Contains("wsl") || desc.Contains("loopback");
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                        var b = ua.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        bool priv = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                        int rank = (gateway ? 0 : 4) + (virt ? 2 : 0) + (priv ? 0 : 1) + (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 0);
                        good.Add((rank, ua.Address.ToString()));
                    }
                }
            }
            catch
            {
            }
            return good.OrderBy(g => g.rank).Select(g => g.ip).FirstOrDefault();
        }

        public void Dispose() => Stop();
    }

    internal sealed class ShareWindow : Window
    {
        readonly Border _surface;
        readonly TextBlock _status;
        readonly string _url;

        public event Action? StopRequested;

        public ShareWindow(string url, string name, long size, int minutes)
        {
            _url = url;
            Glass.Attach(this, 20);
            Title = "heft share";
            SizeToContent = SizeToContent.WidthAndHeight;
            _surface = Glass.Surface(20);

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Width = 300 };
            var head = new DockPanel();
            var close = Kit.Plain("\uE8BB", () => StopRequested?.Invoke(), "stop sharing", 10);
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(Kit.Text("scan it with your phone", 16, true));
            root.Children.Add(head);
            var cap = Kit.Caption("same wi-fi, nothing goes through the internet");
            cap.Margin = new Thickness(0, 2, 0, 14);
            root.Children.Add(cap);

            var qrHost = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Width = 252, Height = 252, HorizontalAlignment = HorizontalAlignment.Center };
            var img = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            img.Source = Qr(url);
            qrHost.Child = img;
            root.Children.Add(qrHost);

            var file = Kit.Text(name, 13, true);
            file.Margin = new Thickness(0, 14, 0, 0);
            file.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(file);
            var meta = Kit.Text($"{Kit.Size(size)}  ·  stops in {minutes} min", 11.5, false, "SubText");
            meta.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(meta);

            var link = new TextBox { Text = url, IsReadOnly = true, Style = (Style)Application.Current.FindResource("Field"), Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5 };
            root.Children.Add(link);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            var copy = Kit.Push("Copy link", () => Files.CopyText(_url));
            copy.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(copy);
            buttons.Children.Add(Kit.Push("Stop", () => StopRequested?.Invoke(), true));
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

        public void Sent()
        {
            _status.Text = "sent. you can close this whenever";
        }

        public void Open()
        {
            var d = Displays.AtCursor();
            Glass.Present(this, _surface, () =>
            {
                var h = new WindowInteropHelper(this).Handle;
                UpdateLayout();
                double hDip = ActualHeight > 0 ? ActualHeight : 560;
                double wDip = ActualWidth > 0 ? ActualWidth : 350;
                int w = (int)(wDip * d.Scale), hp = (int)(hDip * d.Scale);
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

        static ImageSource? Qr(string text)
        {
            try
            {
                var gen = new QRCoder.QRCodeGenerator();
                var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
                var m = data.ModuleMatrix;
                int n = m.Count;
                var geo = new GeometryGroup { FillRule = FillRule.Nonzero };
                for (int y = 0; y < n; y++)
                {
                    var row = m[y];
                    for (int x = 0; x < row.Length; x++)
                        if (row[x]) geo.Children.Add(new RectangleGeometry(new Rect(x, y, 1.02, 1.02)));
                }
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, n, n))));
                group.Children.Add(new GeometryDrawing(Brushes.Black, null, geo));
                group.Freeze();
                return new DrawingImage(group);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("qr failed " + ex.Message);
                return null;
            }
        }
    }
}
