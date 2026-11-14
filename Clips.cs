using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Elysium
{
    internal sealed class Clip
    {
        public string Kind = "text";
        public string Text = "";
        public List<string> Files = new();
        public BitmapSource? Image;
        public DateTime Time = DateTime.Now;
        public bool Pinned;

        public string Summary => Kind switch
        {
            "image" => Image != null ? $"image {Image.PixelWidth}x{Image.PixelHeight}" : "image",
            "files" => Files.Count == 1 ? System.IO.Path.GetFileName(Files[0]) : $"{Files.Count} files",
            _ => Text.Replace("\r", " ").Replace("\n", " ").Trim()
        };
    }

    internal sealed class Clips : IDisposable
    {
        readonly Settings _s;
        readonly HwndSource _source;
        bool _ignoreNext;

        public List<Clip> History { get; } = new();
        public Queue<Clip> Stack { get; } = new();
        public bool Stacking { get; private set; }
        public Clip? Latest => History.FirstOrDefault();

        public event Action? Changed;
        public event Action? StackChanged;

        public Clips(Settings settings)
        {
            _s = settings;
            var p = new HwndSourceParameters("elysium-clip") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            _source = new HwndSource(p);
            _source.AddHook(Hook);
            Native.AddClipboardFormatListener(_source.Handle);
            foreach (var t in _s.PinnedClips) History.Add(new Clip { Kind = "text", Text = t, Pinned = true });
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE)
            {
                if (_ignoreNext)
                {
                    _ignoreNext = false;
                }
                else
                {
                    _ = Capture();
                }
            }
            return IntPtr.Zero;
        }

        async Task Capture()
        {
            Clip? clip = null;
            for (int attempt = 0; attempt < 4 && clip == null; attempt++)
            {
                try
                {
                    clip = Read();
                    break;
                }
                catch
                {
                    await Task.Delay(60);
                }
            }
            if (clip == null) return;

            if (_s.ClipHistory)
            {
                History.RemoveAll(c => !c.Pinned && Same(c, clip));
                int insert = History.Count(c => c.Pinned);
                History.Insert(Math.Min(insert, History.Count), clip);
                while (History.Count(c => !c.Pinned) > Math.Clamp(_s.ClipMax, 5, 200))
                {
                    var last = History.LastOrDefault(c => !c.Pinned);
                    if (last == null) break;
                    History.Remove(last);
                }
                Changed?.Invoke();
            }

            if (Stacking)
            {
                Stack.Enqueue(clip);
                StackChanged?.Invoke();
            }
        }

        static bool Same(Clip a, Clip b)
        {
            if (a.Kind != b.Kind) return false;
            return a.Kind switch
            {
                "text" => a.Text == b.Text,
                "files" => a.Files.SequenceEqual(b.Files),
                _ => false
            };
        }

        static Clip? Read()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var list = new List<string>();
                foreach (var f in files)
                    if (f != null) list.Add(f);
                return list.Count == 0 ? null : new Clip { Kind = "files", Files = list };
            }
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img == null) return null;
                BitmapSource small = img;
                if (img.PixelWidth > 640)
                {
                    double k = 640.0 / img.PixelWidth;
                    small = new TransformedBitmap(img, new System.Windows.Media.ScaleTransform(k, k));
                }
                var frozen = new WriteableBitmap(small);
                frozen.Freeze();
                return new Clip { Kind = "image", Image = frozen };
            }
            if (Clipboard.ContainsText())
            {
                var t = Clipboard.GetText();
                return string.IsNullOrEmpty(t) ? null : new Clip { Kind = "text", Text = t };
            }
            return null;
        }

        public void Put(Clip c, bool silent = true)
        {
            try
            {
                _ignoreNext = silent;
                switch (c.Kind)
                {
                    case "files":
                        var col = new StringCollection();
                        col.AddRange(c.Files.ToArray());
                        Clipboard.SetFileDropList(col);
                        break;
                    case "image":
                        if (c.Image != null) Clipboard.SetImage(c.Image);
                        break;
                    default:
                        Clipboard.SetText(c.Text);
                        break;
                }
            }
            catch
            {
                _ignoreNext = false;
            }
        }

        public void TogglePin(Clip c)
        {
            if (c.Kind != "text") return;
            c.Pinned = !c.Pinned;
            _s.PinnedClips = History.Where(x => x.Pinned && x.Kind == "text").Select(x => x.Text).ToList();
            _s.Touch();
            Changed?.Invoke();
        }

        public void Remove(Clip c)
        {
            History.Remove(c);
            if (c.Pinned)
            {
                _s.PinnedClips = History.Where(x => x.Pinned && x.Kind == "text").Select(x => x.Text).ToList();
                _s.Touch();
            }
            Changed?.Invoke();
        }

        public void ToggleStack()
        {
            Stacking = !Stacking;
            Stack.Clear();
            StackChanged?.Invoke();
        }

        public async void PasteNext()
        {
            if (Stack.Count == 0)
            {
                if (Stacking)
                {
                    Stacking = false;
                    StackChanged?.Invoke();
                }
                return;
            }
            var next = Stack.Dequeue();
            Put(next);
            await Native.WaitForModifiersUp();
            Native.Combo(0x11, 0x56);
            if (Stack.Count == 0) Stacking = false;
            StackChanged?.Invoke();
        }

        public static async void PasteNow(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                return;
            }
            await Native.WaitForModifiersUp();
            Native.Combo(0x11, 0x56);
        }

        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
        }
    }
}
