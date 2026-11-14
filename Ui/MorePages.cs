using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        void WindowExtras(Panel h)
        {
            h.Children.Add(Kit.Header("Focus"));
            h.Children.Add(Kit.Group(
                Kit.Row("Focus border", "a soft glow around the window you're typing in, handy with two screens", Sw(_s.FocusBorder, v => _s.FocusBorder = v)),
                Kit.SliderRow("Spotlight dim", 0.2, 0.9, _s.SpotlightDim, v => Math.Round(v * 100) + "%", v =>
                {
                    _s.SpotlightDim = v;
                    Touch();
                }),
                Kit.Row("Spotlight", "dims everything except the window you're in. " + KeyHint("spotlight"), null)));

            h.Children.Add(Kit.Header("Screens"));
            h.Children.Add(Kit.Group(
                Kit.Row("Remember window spots per setup", "unplug a monitor or wake the pc and heft puts every window back where it was for that setup", Sw(_s.MonitorMemory, v => _s.MonitorMemory = v)),
                Kit.Row("Rescue a stuck window", KeyHint("rescue") + " sends the window you're in to the other screen, again flips fullscreen, again forces it borderless", null),
                Kit.Row("Free the mouse", KeyHint("unstick") + " if a game crashed and left your cursor trapped", null)));
        }

        string KeyHint(string id)
        {
            string spec = Hotkeys.Spec(_s, id);
            return spec.Length == 0 ? "give it a key in the keys tab" : spec.ToLowerInvariant().Replace("+", " + ");
        }

        void AudioExtras(Panel h)
        {
            h.Children.Add(Kit.Header("Music"));
            h.Children.Add(Kit.Group(
                Kit.Row("Pause music for videos", "start a video while spotify's going and the music pauses, then picks back up when the video stops", Sw(_s.MediaHandoff, v => _s.MediaHandoff = v)),
                Kit.Row("Pause when headphones come out", "like on a phone", Sw(_s.HeadphonePause, v => _s.HeadphonePause = v))));

            h.Children.Add(Kit.Header("Mic"));
            var rec = Recorder(_s.PttKey, true, v =>
            {
                _s.PttKey = v;
                Touch();
                Rebuild();
            }, "pick a key");
            var clear = Kit.Push("Clear", () =>
            {
                _s.PttKey = "";
                Touch();
                Rebuild();
            });
            clear.Margin = new Thickness(6, 0, 0, 0);
            var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            keyRow.Children.Add(rec);
            keyRow.Children.Add(clear);
            var mouse = Kit.Segments(new[] { "Key", "Mouse 4", "Mouse 5" }, new[] { "key", "Mouse4", "Mouse5" }, _s.PttKey is "Mouse4" or "Mouse5" ? _s.PttKey : "key", v =>
            {
                _s.PttKey = v == "key" ? "" : v;
                Touch();
                Rebuild();
            });
            h.Children.Add(Kit.Group(
                Kit.Row("Mute your mic", KeyHint("micmute") + " mutes it everywhere. a red mic shows in the bar while it's muted", null),
                Kit.Block("Hold to", null, Kit.Segments(new[] { "Off", "Talk", "Mute" }, new[] { "off", "talk", "mute" }, _s.PttMode, v =>
                {
                    _s.PttMode = v;
                    Touch();
                })),
                Kit.Block("Hold what", null, mouse),
                Kit.Row("Key", _s.PttKey is "Mouse4" or "Mouse5" ? "using the mouse side button" : "click and press the key you want to hold. f13-f24 or a mouse side button work great", _s.PttKey is "Mouse4" or "Mouse5" ? null : keyRow),
                Kit.Row("Don't type the key", "so holding it doesn't also type in whatever app you're in", Sw(_s.PttSwallow, v => _s.PttSwallow = v)),
                Kit.Row("Little sounds", "a blip when the mic turns on and off", Sw(_s.MicSounds, v => _s.MicSounds = v))));
        }

        void ShellExtras(Panel h)
        {
            h.Children.Add(Kit.Header("Start"));
            h.Children.Add(Kit.Group(
                Kit.Row("Screen time card", "how long you were in each app today. stays on your pc", Sw(_s.ScreenTimeCard, v => _s.ScreenTimeCard = v)),
                Kit.Row("Reset pinned apps", "puts your pins back to what windows had", Kit.Push("Reset", () =>
                {
                    _s.StartPins.Clear();
                    _s.StartPinsSeeded = false;
                    Touch();
                }))));

            h.Children.Add(Kit.Header("Notes on apps"));
            if (_s.AppNotes.Count == 0)
            {
                var none = Kit.Caption("none yet. run \"note on this app\" from the launcher or right-click a window in overview");
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(14, 4, 14, 0);
                h.Children.Add(none);
                return;
            }
            var rows = _s.AppNotes.Select(kv =>
            {
                string exe = kv.Key;
                var remove = Kit.Push("Remove", () =>
                {
                    _s.AppNotes.Remove(exe);
                    Touch();
                    Rebuild();
                });
                string text = kv.Value.Replace("\r", "").Replace("\n", " ");
                return (UIElement)Kit.Row(exe, text.Length > 80 ? text[..80] + "..." : text, remove);
            }).ToArray();
            h.Children.Add(Kit.Group(rows));
        }
    }
}
