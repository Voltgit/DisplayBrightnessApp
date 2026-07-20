using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace DisplayBrightnessApp
{
    /// <summary>
    /// Enumerates all brightness-controllable monitors (external monitors via
    /// DDC/CI and the built-in laptop panel via WMI) and exposes a uniform
    /// get/set brightness API (0-100) per monitor id, hiding which backend
    /// is actually used underneath.
    /// </summary>
    internal sealed class MonitorBrightnessService : IDisposable
    {
        internal sealed class MonitorInfo
        {
            public required string Id { get; init; }
            public required string Name { get; init; }
            public int Brightness { get; set; }
            public string? Resolution { get; init; }
        }

        private sealed class DdcMonitorEntry
        {
            public IntPtr Handle;
            public uint Min;
            public uint Max;
        }

        private sealed class WmiMonitorEntry
        {
            public string InstanceName = string.Empty;
        }

        /// <summary>
        /// One row of the shared desktop-topology table built once per
        /// Refresh(): for each HMONITOR, its GDI device name, native
        /// resolution, and the "DISPLAY\HWID\InstanceId" prefix used to
        /// correlate it against root\WMI's WmiMonitorID/WmiMonitorBrightness
        /// InstanceName values (which append a "_0"-style suffix).
        /// </summary>
        private sealed class DisplayTopologyEntry
        {
            public IntPtr HMonitor;
            public string? SzDevice;
            public string? Resolution;
            public string? WmiInstancePrefix;
        }

        /// <summary>One decoded row from root\WMI's WmiMonitorID class.</summary>
        private sealed class WmiMonitorIdEntry
        {
            public string InstanceName = string.Empty;
            public string? FriendlyName;
        }

        private readonly List<NativeMethods.PHYSICAL_MONITOR[]> _physicalMonitorArrays = new();
        private readonly Dictionary<string, DdcMonitorEntry> _ddcMonitors = new();
        private readonly Dictionary<string, WmiMonitorEntry> _wmiMonitors = new();

        public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();

        /// <summary>
        /// (Re)enumerates all monitors and their current brightness. Safe to
        /// call repeatedly (e.g. on SystemEvents.DisplaySettingsChanged) --
        /// releases any previously held physical monitor handles first.
        /// </summary>
        public void Refresh()
        {
            Cleanup();

            var monitors = new List<MonitorInfo>();

            List<DisplayTopologyEntry> topology = BuildDisplayTopology();
            List<WmiMonitorIdEntry> wmiMonitorIds = QueryWmiMonitorIds();

            DiscoverDdcMonitors(monitors, topology, wmiMonitorIds);
            DiscoverWmiMonitor(monitors, topology, wmiMonitorIds);

            Monitors = monitors;
        }

        /// <summary>
        /// Enumerates every HMONITOR once and, for each, resolves its GDI
        /// device name (szDevice), native resolution, and the WMI instance
        /// prefix used to correlate it against WmiMonitorID/WmiMonitorBrightness.
        /// Shared by both the DDC and WMI discovery passes so the (relatively
        /// expensive) EnumDisplayDevices/EnumDisplaySettings calls only ever
        /// run once per monitor per Refresh().
        /// </summary>
        private static List<DisplayTopologyEntry> BuildDisplayTopology()
        {
            var hMonitors = new List<IntPtr>();

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
                {
                    hMonitors.Add(hMonitor);
                    return true;
                }, IntPtr.Zero);

            var result = new List<DisplayTopologyEntry>();

            foreach (IntPtr hMonitor in hMonitors)
            {
                var entry = new DisplayTopologyEntry { HMonitor = hMonitor };

                try
                {
                    var mi = new NativeMethods.MONITORINFOEX
                    {
                        cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
                    };

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi) && !string.IsNullOrEmpty(mi.szDevice))
                    {
                        entry.SzDevice = mi.szDevice;

                        var dm = new NativeMethods.DEVMODE
                        {
                            dmSize = (short)Marshal.SizeOf<NativeMethods.DEVMODE>()
                        };

                        if (NativeMethods.EnumDisplaySettings(mi.szDevice, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm)
                            && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                        {
                            entry.Resolution = $"{dm.dmPelsWidth}×{dm.dmPelsHeight}";
                        }

                        var dd = new NativeMethods.DISPLAY_DEVICE
                        {
                            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>()
                        };

                        if (NativeMethods.EnumDisplayDevices(mi.szDevice, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME)
                            && !string.IsNullOrEmpty(dd.DeviceID))
                        {
                            string[] parts = dd.DeviceID.Split('#');
                            if (parts.Length >= 3)
                            {
                                entry.WmiInstancePrefix = $@"DISPLAY\{parts[1]}\{parts[2]}";
                            }
                        }
                    }
                }
                catch
                {
                    // Best-effort enrichment only -- a monitor we can't
                    // resolve topology for just keeps its fallback name and
                    // no resolution badge, never a crash.
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Queries root\WMI's WmiMonitorID class once per Refresh() and
        /// decodes each row's EDID-derived friendly name. Returns an empty
        /// list (never throws) on machines where the class is unavailable.
        /// </summary>
        private static List<WmiMonitorIdEntry> QueryWmiMonitorIds()
        {
            var result = new List<WmiMonitorIdEntry>();

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorID");
                using var results = searcher.Get();

                foreach (var moBase in results)
                {
                    using var mo = (ManagementObject)moBase;

                    string instanceName = Convert.ToString(mo["InstanceName"]) ?? string.Empty;
                    if (string.IsNullOrEmpty(instanceName))
                    {
                        continue;
                    }

                    string friendly = DecodeWmiChars(
                        mo["UserFriendlyName"] as ushort[],
                        mo["UserFriendlyNameLength"] != null ? Convert.ToInt32(mo["UserFriendlyNameLength"]) : null);

                    string? name;
                    if (!string.IsNullOrWhiteSpace(friendly))
                    {
                        name = friendly;
                    }
                    else
                    {
                        string manufacturer = DecodeWmiChars(mo["ManufacturerName"] as ushort[]);
                        name = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer + " Monitor";
                    }

                    result.Add(new WmiMonitorIdEntry { InstanceName = instanceName, FriendlyName = name });
                }
            }
            catch (ManagementException)
            {
                // WmiMonitorID isn't available on this machine -- ignore,
                // callers fall back to their existing naming logic.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (COMException)
            {
            }

            return result;
        }

        /// <summary>
        /// Decodes a WMI-provided ushort[] of already-resolved ASCII
        /// character codes (per WmiMonitorID's own documented field format --
        /// no manual EDID bit-unpacking needed) into a trimmed string.
        /// </summary>
        private static string DecodeWmiChars(ushort[]? values, int? length = null)
        {
            if (values == null || values.Length == 0)
            {
                return string.Empty;
            }

            int count = Math.Min(length ?? values.Length, values.Length);
            var chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)values[i];
            }

            return new string(chars).TrimEnd('\0').Trim();
        }

        /// <summary>
        /// Finds the WmiMonitorID row whose InstanceName starts with the
        /// given topology entry's WMI instance prefix (WMI appends a
        /// "_0"-style suffix to the raw PDO instance name, so this must be a
        /// prefix match, not equality) and returns its decoded friendly
        /// name, or null if there's no match / no usable name.
        /// </summary>
        private static string? ResolveFriendlyName(DisplayTopologyEntry topo, List<WmiMonitorIdEntry> wmiMonitorIds)
        {
            if (string.IsNullOrEmpty(topo.WmiInstancePrefix))
            {
                return null;
            }

            foreach (WmiMonitorIdEntry entry in wmiMonitorIds)
            {
                if (entry.InstanceName.StartsWith(topo.WmiInstancePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(entry.FriendlyName) ? null : entry.FriendlyName;
                }
            }

            return null;
        }

        private void DiscoverDdcMonitors(List<MonitorInfo> monitors, List<DisplayTopologyEntry> topology, List<WmiMonitorIdEntry> wmiMonitorIds)
        {
            int ddcIndex = 0;

            foreach (DisplayTopologyEntry topo in topology)
            {
                IntPtr hMonitor = topo.HMonitor;

                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                {
                    continue;
                }

                var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                {
                    continue;
                }

                _physicalMonitorArrays.Add(physicalMonitors);

                foreach (var pm in physicalMonitors)
                {
                    // Not every physical monitor supports DDC/CI brightness -
                    // GetMonitorBrightness fails for those; skip them rather
                    // than showing a broken slider.
                    if (!NativeMethods.GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint current, out uint max)
                        || max <= min)
                    {
                        continue;
                    }

                    ddcIndex++;
                    string id = $"ddc-{ddcIndex}";

                    // szPhysicalMonitorDescription is NOT the EDID friendly
                    // name (Windows API limitation -- it's typically the
                    // generic "Generic PnP Monitor" string); prefer the
                    // EDID-derived name from WmiMonitorID when we can
                    // correlate it, falling back to the old behavior.
                    string fallbackName = string.IsNullOrWhiteSpace(pm.szPhysicalMonitorDescription)
                        ? $"Monitor {ddcIndex}"
                        : pm.szPhysicalMonitorDescription;
                    string name = ResolveFriendlyName(topo, wmiMonitorIds) ?? fallbackName;

                    _ddcMonitors[id] = new DdcMonitorEntry { Handle = pm.hPhysicalMonitor, Min = min, Max = max };

                    monitors.Add(new MonitorInfo
                    {
                        Id = id,
                        Name = name,
                        Brightness = ScaleToPercent(current, min, max),
                        Resolution = topo.Resolution
                    });
                }
            }
        }

        private void DiscoverWmiMonitor(List<MonitorInfo> monitors, List<DisplayTopologyEntry> topology, List<WmiMonitorIdEntry> wmiMonitorIds)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness");
                using var results = searcher.Get();

                int wmiIndex = 0;
                foreach (var moBase in results)
                {
                    using var mo = (ManagementObject)moBase;

                    wmiIndex++;
                    byte current = Convert.ToByte(mo["CurrentBrightness"]);
                    string instanceName = Convert.ToString(mo["InstanceName"]) ?? $"wmi-{wmiIndex}";
                    string id = $"wmi-{wmiIndex}";

                    _wmiMonitors[id] = new WmiMonitorEntry { InstanceName = instanceName };

                    // WmiMonitorBrightness and WmiMonitorID use the exact
                    // same InstanceName convention (both root\WMI classes
                    // keyed off the same PDO) -- correlate by equality.
                    string name = "Built-in Display";
                    WmiMonitorIdEntry? idEntry = wmiMonitorIds.Find(w =>
                        string.Equals(w.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));
                    if (idEntry != null && !string.IsNullOrWhiteSpace(idEntry.FriendlyName))
                    {
                        name = idEntry.FriendlyName!;
                    }

                    // Resolution isn't available from WmiMonitorBrightness
                    // itself -- find the topology row whose WMI instance
                    // prefix is a prefix of this monitor's InstanceName.
                    string? resolution = null;
                    DisplayTopologyEntry? topo = topology.Find(t =>
                        !string.IsNullOrEmpty(t.WmiInstancePrefix)
                        && instanceName.StartsWith(t.WmiInstancePrefix!, StringComparison.OrdinalIgnoreCase));
                    if (topo != null)
                    {
                        resolution = topo.Resolution;
                    }

                    monitors.Add(new MonitorInfo
                    {
                        Id = id,
                        Name = name,
                        Brightness = Math.Clamp((int)current, 0, 100),
                        Resolution = resolution
                    });
                }
            }
            catch (ManagementException)
            {
                // WmiMonitorBrightness class isn't available on this machine
                // (e.g. a desktop with only external monitors) -- ignore.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (COMException)
            {
            }
        }

        /// <summary>Applies a 0-100 brightness value to the given monitor.</summary>
        public bool SetBrightness(string id, int percent)
        {
            percent = Math.Clamp(percent, 0, 100);

            if (_ddcMonitors.TryGetValue(id, out var ddc))
            {
                uint raw = ScaleFromPercent(percent, ddc.Min, ddc.Max);
                return NativeMethods.SetMonitorBrightness(ddc.Handle, raw);
            }

            if (_wmiMonitors.TryGetValue(id, out var wmi))
            {
                return SetWmiBrightness(wmi.InstanceName, (byte)percent);
            }

            return false;
        }

        private static bool SetWmiBrightness(string instanceName, byte percent)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                using var results = searcher.Get();

                foreach (var moBase in results)
                {
                    using var mo = (ManagementObject)moBase;

                    if (!string.Equals(Convert.ToString(mo["InstanceName"]), instanceName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var inParams = mo.GetMethodParameters("WmiSetBrightness");
                    inParams["Timeout"] = 1; // seconds
                    inParams["Brightness"] = percent;
                    mo.InvokeMethod("WmiSetBrightness", inParams, null);
                    return true;
                }
            }
            catch (ManagementException)
            {
            }
            catch (COMException)
            {
            }

            return false;
        }

        private static int ScaleToPercent(uint value, uint min, uint max)
        {
            if (max <= min)
            {
                return 0;
            }

            double pct = (value - min) * 100.0 / (max - min);
            return (int)Math.Round(Math.Clamp(pct, 0, 100));
        }

        private static uint ScaleFromPercent(int percent, uint min, uint max)
        {
            double raw = min + ((max - min) * (percent / 100.0));
            return (uint)Math.Round(raw);
        }

        private void Cleanup()
        {
            foreach (var arr in _physicalMonitorArrays)
            {
                NativeMethods.DestroyPhysicalMonitors((uint)arr.Length, arr);
            }

            _physicalMonitorArrays.Clear();
            _ddcMonitors.Clear();
            _wmiMonitors.Clear();
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
