using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Elysium.Ui
{
    internal static class Frames
    {
        sealed class Job
        {
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public double Ms;
            public Action<double> Step = _ => { };
            public Action? Done;
            public object? Key;
            public bool Dead;
        }

        static readonly List<Job> Jobs = new();
        static bool _hooked;

        public static int Rate { get; private set; } = 60;

        public static void Boost()
        {
            int best = 60;
            try
            {
                foreach (var d in Displays.All()) best = Math.Max(best, Native.RefreshRate(d));
            }
            catch
            {
            }
            Rate = Math.Clamp(best, 60, 360);
            try
            {
                Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = Rate });
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("frame rate override failed " + ex.Message);
            }
        }

        public static void Run(double ms, Action<double> step, Action? done = null, object? key = null)
        {
            if (key != null)
                foreach (var j in Jobs)
                    if (Equals(j.Key, key)) j.Dead = true;
            var job = new Job { Ms = ms, Step = step, Done = done, Key = key };
            Jobs.Add(job);
            try
            {
                step(0);
            }
            catch (Exception ex)
            {
                PhysicsEngine.Log("frame step failed " + ex.Message);
            }
            if (_hooked) return;
            _hooked = true;
            Native.timeBeginPeriod(1);
            CompositionTarget.Rendering += Tick;
        }

        public static void Stop(object key)
        {
            foreach (var j in Jobs)
                if (Equals(j.Key, key)) j.Dead = true;
        }

        public static bool Busy(object key)
        {
            foreach (var j in Jobs)
                if (!j.Dead && Equals(j.Key, key)) return true;
            return false;
        }

        static void Tick(object? sender, EventArgs e)
        {
            foreach (var j in Jobs.ToArray())
            {
                if (j.Dead)
                {
                    Jobs.Remove(j);
                    continue;
                }
                double t = j.Ms <= 0 ? 1 : Math.Min(1, j.Clock.Elapsed.TotalMilliseconds / j.Ms);
                try
                {
                    j.Step(t);
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("frame step failed " + ex.Message);
                    t = 1;
                }
                if (t < 1) continue;
                j.Dead = true;
                Jobs.Remove(j);
                try
                {
                    j.Done?.Invoke();
                }
                catch (Exception ex)
                {
                    PhysicsEngine.Log("frame done failed " + ex.Message);
                }
            }
            if (Jobs.Count > 0 || !_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= Tick;
            Native.timeEndPeriod(1);
        }

        public static double Out(double t) => 1 - Math.Pow(1 - t, 3);

        public static double Quint(double t) => 1 - Math.Pow(1 - t, 5);

        public static double Spring(double t)
        {
            if (t >= 1) return 1;
            const double zeta = 0.78, w = 9.5;
            double wd = w * Math.Sqrt(1 - zeta * zeta);
            return 1 - Math.Exp(-zeta * w * t) * (Math.Cos(wd * t) + zeta * w / wd * Math.Sin(wd * t));
        }
    }
}
