using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Heft.Ui
{
    internal sealed partial class SettingsPages
    {
        string _query = "";
        Action<string>? _go;

        public void Find(string query, Panel host, Action<string> go, bool classicHit)
        {
            string q = query.Trim();
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            host.Children.Clear();
            int hits = 0;

            if (classicHit)
            {
                hits++;
                host.Children.Add(PageLink("Dock & physics", "dock", go));
                var more = Kit.Caption("there's a match in here, click to open it");
                more.Margin = new Thickness(14, 0, 14, 4);
                host.Children.Add(more);
            }

            foreach (var (id, label) in Tabs)
            {
                if (id == "dock") continue;
                var temp = new StackPanel();
                try
                {
                    Build(id, temp);
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("settings search " + id + " " + ex.Message);
                    continue;
                }
                var keep = new List<UIElement>();
                bool under = false;
                foreach (UIElement child in temp.Children)
                {
                    if (child is TextBlock tb)
                    {
                        under = All(tb.Text, words);
                        continue;
                    }
                    if (under || All(Words(child), words)) keep.Add(child);
                }
                bool tabHit = All(label, words);
                if (keep.Count == 0 && !tabHit) continue;
                hits++;
                host.Children.Add(PageLink(label, id, go));
                foreach (var k in keep)
                {
                    temp.Children.Remove(k);
                    host.Children.Add(k);
                }
            }

            if (hits == 0)
            {
                var none = Kit.Caption("nothing matches \"" + q + "\"");
                none.Margin = new Thickness(14, 14, 14, 0);
                host.Children.Add(none);
            }

            _page = "";
            _host = host;
            _query = q;
            _go = go;
        }

        static UIElement PageLink(string label, string id, Action<string> go)
        {
            var head = Kit.Header(label + "  ›");
            head.Cursor = Cursors.Hand;
            head.ToolTip = "open " + label.ToLowerInvariant();
            head.MouseLeftButtonUp += (_, _) => go(id);
            return head;
        }

        static bool All(string text, string[] words)
        {
            if (words.Length == 0) return false;
            foreach (var w in words)
                if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        public static string Words(DependencyObject root)
        {
            var sb = new StringBuilder();
            Collect(root, sb, 0);
            return sb.ToString();
        }

        static void Collect(object node, StringBuilder sb, int depth)
        {
            if (depth > 40) return;
            switch (node)
            {
                case TextBlock tb:
                    sb.Append(tb.Text).Append(' ');
                    break;
                case string str:
                    sb.Append(str).Append(' ');
                    return;
            }
            if (node is FrameworkElement fe && fe.ToolTip is string tip) sb.Append(tip).Append(' ');
            if (node is DependencyObject d)
                foreach (var c in LogicalTreeHelper.GetChildren(d).Cast<object>().ToList())
                    Collect(c, sb, depth + 1);
        }
    }
}
