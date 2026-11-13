using System;
using System.Linq;
using System.Threading.Tasks;

namespace Heft
{
    public partial class App
    {
        public static readonly (string id, string label)[] GestureActions =
        {
            ("none", "Nothing"),
            ("back", "Back"),
            ("forward", "Forward"),
            ("refresh", "Refresh"),
            ("minimize", "Minimize the window"),
            ("maximize", "Maximize / restore"),
            ("close", "Close the window"),
            ("overview", "Overview"),
            ("start", "Start"),
            ("launcher", "Launcher"),
            ("shelf", "Shelf"),
            ("spaces", "Spaces"),
            ("desktop", "Show desktop"),
            ("deskleft", "Desktop to the left"),
            ("deskright", "Desktop to the right"),
            ("newtab", "New tab"),
            ("closetab", "Close tab"),
            ("reopen", "Reopen last closed app")
        };

        string _snipSig = "";

        void SyncSnippets()
        {
            string sig = _s.SnippetsOn + "|" + string.Join("|", _s.Snippets.Select(x => x.Trigger + "=" + x.Text));
            if (sig == _snipSig) return;
            _snipSig = sig;
            _keyHook.SetSnippets(_s.SnippetsOn ? _s.Snippets : Enumerable.Empty<Snippet>());
        }

        async void ExpandSnippet(int back, string text)
        {
            if (_game || !_s.SnippetsOn) return;
            try
            {
                string body = Expander.Fill(text);
                await Native.WaitForModifiersUp();
                await Task.Delay(15);
                Expander.Type(back, body);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("snippet failed " + ex.Message);
            }
        }

        void OnGesture(string dirs, Native.POINT start)
        {
            if (_game) return;
            if (!_s.GestureMap.TryGetValue(dirs, out var action) || action == "none")
            {
                Flash("no gesture for " + Arrows(dirs));
                return;
            }
            IntPtr target = Native.GetAncestor(Native.WindowFromPoint(start), Native.GA_ROOT);
            Flash(Arrows(dirs) + "  " + (GestureActions.FirstOrDefault(g => g.id == action).label ?? action).ToLowerInvariant());
            switch (action)
            {
                case "back":
                    SendTo(target, 0x12, 0x25);
                    break;
                case "forward":
                    SendTo(target, 0x12, 0x27);
                    break;
                case "refresh":
                    SendTo(target, 0x74);
                    break;
                case "newtab":
                    SendTo(target, 0x11, 0x54);
                    break;
                case "closetab":
                    SendTo(target, 0x11, 0x57);
                    break;
                case "minimize":
                    if (target != IntPtr.Zero) Native.ShowWindow(target, Native.SW_MINIMIZE);
                    break;
                case "maximize":
                    if (target != IntPtr.Zero) Native.ShowWindow(target, Native.IsZoomed(target) ? Native.SW_RESTORE : Native.SW_MAXIMIZE);
                    break;
                case "close":
                    if (target != IntPtr.Zero) Native.PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    break;
                case "deskleft":
                    VirtualDesktops.Go(_deskIndex, _deskIndex - 1);
                    break;
                case "deskright":
                    VirtualDesktops.Go(_deskIndex, _deskIndex + 1);
                    break;
                case "reopen":
                    ReopenLast();
                    break;
                default:
                    if (!FireAction(action)) RunAction(action);
                    break;
            }
        }

        static void SendTo(IntPtr target, params byte[] keys)
        {
            if (target != IntPtr.Zero && Native.GetForegroundWindow() != target) Native.ForceForeground(target);
            Native.Combo(keys);
        }

        public static string Arrows(string dirs)
        {
            return string.Concat(dirs.Select(c => c switch
            {
                'U' => "↑",
                'D' => "↓",
                'L' => "←",
                'R' => "→",
                _ => ""
            }));
        }
    }
}
