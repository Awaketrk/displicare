using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Displicare.Rooms
{
    internal sealed class ApplyResult
    {
        public bool Success;
        public string Message;
        /// <summary>"exact" (saved timings restored as-is) or "rebuilt" (topology rebuilt, modes set afterwards).</summary>
        public string Method;
        /// <summary>The switch went through but wasn't confirmed, so the previous layout is back.</summary>
        public bool Reverted;
        /// <summary>Snapshot of the layout before this switch (null if it couldn't be read).</summary>
        public Profile Previous;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>Saves the live layout as a profile and applies profiles back.</summary>
    internal static class LayoutEngine
    {
        const int UnionOffset = 16;
        const int UnionSize = 48;

        // ---------------------------------------------------------------- capture

        /// <summary>Snapshot of the active layout. Virtual displays (Apollo/Sunshine, Miracast) are left out unless asked for.</summary>
        public static Profile Capture(string name, bool includeVirtual)
        {
            var config = DisplayTopology.Query(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS);
            var profile = new Profile();
            profile.Name = name;
            profile.Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            var kept = new List<DISPLAYCONFIG_PATH_INFO>();
            var displays = new List<ProfileDisplay>();
            foreach (var p in config.Paths)
            {
                uint tech;
                var identity = DisplayTopology.GetIdentity(p.targetInfo.adapterId, p.targetInfo.id, out tech);
                if (tech == 0) tech = p.targetInfo.outputTechnology;
                if (!includeVirtual && DisplayConfigApi.IsVirtualOutput(tech)) continue;

                var d = new ProfileDisplay();
                d.Identity = identity;
                d.Hdr = DisplayTopology.GetHdrState(p.targetInfo.adapterId, p.targetInfo.id);
                d.RefreshHz = DisplayTopology.Hz(p.targetInfo.refreshRate);
                if (p.sourceInfo.modeInfoIdx < config.Modes.Length)
                {
                    var src = config.Modes[p.sourceInfo.modeInfoIdx].sourceMode;
                    d.Width = (int)src.width;
                    d.Height = (int)src.height;
                    d.X = src.position.x;
                    d.Y = src.position.y;
                }
                if (p.targetInfo.modeInfoIdx < config.Modes.Length)
                {
                    double hz = DisplayTopology.Hz(config.Modes[p.targetInfo.modeInfoIdx].targetMode.vSyncFreq);
                    if (hz > 0) d.RefreshHz = hz;
                }
                kept.Add(p);
                displays.Add(d);
            }
            if (kept.Count == 0) throw new InvalidOperationException("No physical displays are active.");

            // Leaving a virtual display out can leave nothing at 0,0 (Windows' primary); move the rest so the first one is.
            bool hasOrigin = false;
            foreach (var d in displays) if (d.X == 0 && d.Y == 0) hasOrigin = true;
            int dx = hasOrigin ? 0 : -displays[0].X, dy = hasOrigin ? 0 : -displays[0].Y;

            var modeIndex = new Dictionary<uint, uint>();
            var adapters = new Dictionary<long, string>();
            for (int i = 0; i < kept.Count; i++)
            {
                var p = kept[i];
                var d = displays[i];
                d.X += dx;
                d.Y += dy;
                d.Primary = d.X == 0 && d.Y == 0;

                var sp = new SavedPath();
                sp.Display = i;
                sp.SourceAdapter = p.sourceInfo.adapterId.Value;
                sp.SourceId = p.sourceInfo.id;
                sp.SourceModeIdx = CopyMode(config, p.sourceInfo.modeInfoIdx, profile, modeIndex, dx, dy);
                sp.TargetAdapter = p.targetInfo.adapterId.Value;
                sp.TargetId = p.targetInfo.id;
                sp.TargetModeIdx = CopyMode(config, p.targetInfo.modeInfoIdx, profile, modeIndex, 0, 0);
                sp.OutputTechnology = p.targetInfo.outputTechnology;
                sp.Rotation = p.targetInfo.rotation;
                sp.Scaling = p.targetInfo.scaling;
                sp.RefreshNumerator = p.targetInfo.refreshRate.Numerator;
                sp.RefreshDenominator = p.targetInfo.refreshRate.Denominator;
                sp.ScanLineOrdering = p.targetInfo.scanLineOrdering;
                profile.Paths.Add(sp);
                profile.Displays.Add(d);

                adapters[sp.SourceAdapter] = DisplayTopology.GetAdapterPath(p.sourceInfo.adapterId);
                adapters[sp.TargetAdapter] = DisplayTopology.GetAdapterPath(p.targetInfo.adapterId);
            }
            foreach (var pair in adapters)
            {
                var a = new SavedAdapter();
                a.Luid = pair.Key;
                a.DevicePath = pair.Value;
                profile.Adapters.Add(a);
            }

            try
            {
                var audio = AudioOutputs.Default();
                if (audio != null)
                {
                    profile.AudioId = audio.Id;
                    profile.AudioName = audio.Name;
                }
            }
            catch (Exception ex) { RoomLog.Write("Couldn't read the sound output: " + ex.Message); }
            return profile;
        }

        static uint CopyMode(DisplayConfig config, uint index, Profile profile, Dictionary<uint, uint> modeIndex, int dx, int dy)
        {
            if (index >= config.Modes.Length) return DisplayConfigApi.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            uint mapped;
            if (modeIndex.TryGetValue(index, out mapped)) return mapped;

            var mode = config.Modes[index];
            if (mode.infoType == DisplayConfigApi.MODE_INFO_TYPE_SOURCE)
            {
                mode.sourceMode.position.x += dx;
                mode.sourceMode.position.y += dy;
            }
            profile.Modes.Add(ToSaved(mode));
            mapped = (uint)(profile.Modes.Count - 1);
            modeIndex[index] = mapped;
            return mapped;
        }

        internal static SavedMode ToSaved(DISPLAYCONFIG_MODE_INFO mode)
        {
            var saved = new SavedMode();
            saved.InfoType = mode.infoType;
            saved.Id = mode.id;
            saved.Adapter = mode.adapterId.Value;
            byte[] all = StructBytes(mode);
            var union = new byte[UnionSize];
            Buffer.BlockCopy(all, UnionOffset, union, 0, UnionSize);
            saved.Data = Convert.ToBase64String(union);
            return saved;
        }

        internal static DISPLAYCONFIG_MODE_INFO FromSaved(SavedMode saved, long adapter, uint id)
        {
            var all = new byte[UnionOffset + UnionSize];
            byte[] union = Convert.FromBase64String(saved.Data ?? "");
            if (union.Length != UnionSize) throw new FormatException("Saved display mode is damaged.");
            Buffer.BlockCopy(union, 0, all, UnionOffset, UnionSize);
            var mode = BytesStruct(all);
            mode.infoType = saved.InfoType;
            mode.id = id;
            mode.adapterId = LUID.FromValue(adapter);
            return mode;
        }

        static byte[] StructBytes(DISPLAYCONFIG_MODE_INFO mode)
        {
            int size = Marshal.SizeOf(typeof(DISPLAYCONFIG_MODE_INFO));
            var bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(mode, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return bytes;
        }

        static DISPLAYCONFIG_MODE_INFO BytesStruct(byte[] bytes)
        {
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                return (DISPLAYCONFIG_MODE_INFO)Marshal.PtrToStructure(ptr, typeof(DISPLAYCONFIG_MODE_INFO));
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        // ---------------------------------------------------------------- matching

        /// <summary>
        /// Index of the connected target that is this monitor, or -1. Tries the exact device path first (same port),
        /// then the EDID maker/model so a monitor still matches after its cable moves to another port or GPU.
        /// </summary>
        internal static int MatchTarget(DisplayIdentity want, IList<TargetInfo> targets)
        {
            if (want == null) return -1;
            if (!string.IsNullOrEmpty(want.DevicePath))
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Available && targets[i].Identity != null
                        && string.Equals(targets[i].Identity.DevicePath, want.DevicePath, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            if (want.EdidManufacturer == 0 && want.EdidProduct == 0) return -1;

            int best = -1, bestScore = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (!t.Available || t.Identity == null) continue;
                if (t.Identity.EdidManufacturer != want.EdidManufacturer || t.Identity.EdidProduct != want.EdidProduct) continue;
                int score = 0;
                if (string.Equals(t.Identity.AdapterPath, want.AdapterPath, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (t.Identity.ConnectorInstance == want.ConnectorInstance) score += 1;
                if (score > bestScore) { best = i; bestScore = score; }
            }
            return best;
        }

        // ---------------------------------------------------------------- exact config

        /// <summary>
        /// Rebuilds the saved path and mode arrays for the current boot: adapter LUIDs change on every restart, and target ids
        /// change when a cable moves port. Returns null with a reason when the saved arrays can't be reused.
        /// </summary>
        internal static DisplayConfig BuildExactConfig(Profile profile, IDictionary<string, long> adapterLuidByPath,
            IList<TargetInfo> targets, out string error)
        {
            error = null;
            var adapterMap = new Dictionary<long, long>();
            foreach (var a in profile.Adapters)
            {
                long current;
                if (a.DevicePath == null || !adapterLuidByPath.TryGetValue(a.DevicePath, out current))
                {
                    error = "a graphics adapter from the profile is missing";
                    return null;
                }
                adapterMap[a.Luid] = current;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[profile.Paths.Count];
            var targetMap = new Dictionary<string, TargetInfo>();
            for (int i = 0; i < profile.Paths.Count; i++)
            {
                var sp = profile.Paths[i];
                if (sp.Display < 0 || sp.Display >= profile.Displays.Count) { error = "the profile file is damaged"; return null; }
                var display = profile.Displays[sp.Display];
                int match = MatchTarget(display.Identity, targets);
                if (match < 0) { error = display.Identity.DisplayName + " isn't connected"; return null; }
                var target = targets[match];

                long sourceAdapter;
                if (!adapterMap.TryGetValue(sp.SourceAdapter, out sourceAdapter)) { error = "the profile file is damaged"; return null; }
                if (sourceAdapter != target.AdapterLuid)
                {
                    error = display.Identity.DisplayName + " is now on a different graphics adapter";
                    return null;
                }
                targetMap[sp.TargetAdapter + ":" + sp.TargetId] = target;

                var p = new DISPLAYCONFIG_PATH_INFO();
                p.flags = DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE;
                p.sourceInfo.adapterId = LUID.FromValue(sourceAdapter);
                p.sourceInfo.id = sp.SourceId;
                p.sourceInfo.modeInfoIdx = sp.SourceModeIdx;
                p.targetInfo.adapterId = LUID.FromValue(target.AdapterLuid);
                p.targetInfo.id = target.TargetId;
                p.targetInfo.modeInfoIdx = sp.TargetModeIdx;
                p.targetInfo.outputTechnology = sp.OutputTechnology;
                p.targetInfo.rotation = sp.Rotation;
                p.targetInfo.scaling = sp.Scaling;
                p.targetInfo.refreshRate.Numerator = sp.RefreshNumerator;
                p.targetInfo.refreshRate.Denominator = sp.RefreshDenominator;
                p.targetInfo.scanLineOrdering = sp.ScanLineOrdering;
                p.targetInfo.targetAvailable = 1;
                paths[i] = p;
            }

            var modes = new DISPLAYCONFIG_MODE_INFO[profile.Modes.Count];
            for (int i = 0; i < profile.Modes.Count; i++)
            {
                var m = profile.Modes[i];
                long adapter;
                if (!adapterMap.TryGetValue(m.Adapter, out adapter)) { error = "the profile file is damaged"; return null; }
                uint id = m.Id;
                TargetInfo target;
                if (m.InfoType == DisplayConfigApi.MODE_INFO_TYPE_TARGET && targetMap.TryGetValue(m.Adapter + ":" + m.Id, out target))
                {
                    adapter = target.AdapterLuid;
                    id = target.TargetId;
                }
                modes[i] = FromSaved(m, adapter, id);
            }

            var config = new DisplayConfig();
            config.Paths = paths;
            config.Modes = modes;
            return config;
        }

        // ---------------------------------------------------------------- rebuilt topology

        /// <summary>
        /// Fallback when the exact arrays don't fit any more: picks a free source for each wanted monitor from the
        /// all-paths list and lets Windows choose modes; <see cref="ApplyModes"/> sets resolution and refresh afterwards.
        /// </summary>
        internal static DISPLAYCONFIG_PATH_INFO[] BuildTopology(Profile profile, DisplayConfig all, IList<TargetInfo> targets, out string error)
        {
            error = null;
            var chosen = new List<DISPLAYCONFIG_PATH_INFO>();
            var usedSources = new HashSet<string>();
            var ordered = new List<int>();
            for (int i = 0; i < profile.Displays.Count; i++) if (profile.Displays[i].Primary) ordered.Add(i);
            for (int i = 0; i < profile.Displays.Count; i++) if (!profile.Displays[i].Primary) ordered.Add(i);

            foreach (int di in ordered)
            {
                var display = profile.Displays[di];
                int match = MatchTarget(display.Identity, targets);
                if (match < 0) { error = display.Identity.DisplayName + " isn't connected"; return null; }
                var target = targets[match];

                uint wantSource = uint.MaxValue;
                foreach (var sp in profile.Paths) if (sp.Display == di) wantSource = sp.SourceId;

                int pick = -1;
                for (int i = 0; i < all.Paths.Length; i++)
                {
                    var p = all.Paths[i];
                    if (p.targetInfo.adapterId.Value != target.AdapterLuid || p.targetInfo.id != target.TargetId) continue;
                    if (usedSources.Contains(p.sourceInfo.adapterId.Value + ":" + p.sourceInfo.id)) continue;
                    if (pick < 0 || p.sourceInfo.id == wantSource) pick = i;
                    if (p.sourceInfo.id == wantSource) break;
                }
                if (pick < 0) { error = "no free display output for " + display.Identity.DisplayName; return null; }

                var path = all.Paths[pick];
                path.flags = DisplayConfigApi.DISPLAYCONFIG_PATH_ACTIVE;
                path.sourceInfo.modeInfoIdx = DisplayConfigApi.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                path.targetInfo.modeInfoIdx = DisplayConfigApi.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                usedSources.Add(path.sourceInfo.adapterId.Value + ":" + path.sourceInfo.id);
                chosen.Add(path);
            }
            return chosen.ToArray();
        }

        /// <summary>Every progressive 32-bit mode the display offers right now, without duplicates.</summary>
        public static List<DisplayMode> ListModes(string gdiName)
        {
            var list = new List<DisplayMode>();
            var seen = new HashSet<string>();
            var dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            for (int i = 0; DisplayConfigApi.EnumDisplaySettings(gdiName, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel < 32 || (dm.dmDisplayFlags & 0x2 /* DM_INTERLACED */) != 0 || dm.dmDisplayFrequency <= 1) continue;
                var m = new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);
                if (seen.Add(m.ToString())) list.Add(m);
            }
            return list;
        }

        /// <summary>
        /// The mode to use: the wanted size at the refresh rate closest to the wanted one; with no wanted size (or one the
        /// display doesn't offer any more), the most pixels at the highest refresh rate.
        /// </summary>
        internal static DisplayMode? PickMode(IList<DisplayMode> modes, int width, int height, double hz)
        {
            if (modes == null || modes.Count == 0) return null;
            if (width > 0 && height > 0)
            {
                DisplayMode? best = null;
                foreach (var m in modes)
                {
                    if (m.Width != width || m.Height != height) continue;
                    if (best == null) { best = m; continue; }
                    double d = hz > 0 ? Math.Abs(m.Hz - hz) : -m.Hz;
                    double bd = hz > 0 ? Math.Abs(best.Value.Hz - hz) : -best.Value.Hz;
                    if (d < bd || (d == bd && m.Hz > best.Value.Hz)) best = m;
                }
                if (best != null) return best;
            }
            DisplayMode top = modes[0];
            foreach (var m in modes)
            {
                long px = (long)m.Width * m.Height, topPx = (long)top.Width * top.Height;
                if (px > topPx || (px == topPx && m.Hz > top.Hz)) top = m;
            }
            return top;
        }

        /// <summary>
        /// Sets resolution, refresh rate, position and primary through the classic GDI call. A display without a saved
        /// mode gets the best one it offers (<see cref="PickMode"/>).
        /// </summary>
        static void ApplyModes(Profile profile, ApplyResult result)
        {
            var config = DisplayTopology.Query(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS);
            var active = DisplayTopology.DescribeTargets(config);
            bool pending = false;
            foreach (var display in profile.Displays)
            {
                int match = MatchTarget(display.Identity, active);
                if (match < 0) continue;
                string gdi = null;
                foreach (var p in config.Paths)
                {
                    if (p.targetInfo.adapterId.Value == active[match].AdapterLuid && p.targetInfo.id == active[match].TargetId)
                        gdi = DisplayTopology.GetSourceGdiName(p.sourceInfo.adapterId, p.sourceInfo.id);
                }
                if (gdi == null) continue;

                var dm = new DEVMODE();
                dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
                if (!DisplayConfigApi.EnumDisplaySettings(gdi, DisplayConfigApi.ENUM_CURRENT_SETTINGS, ref dm)) continue;

                var pick = PickMode(ListModes(gdi), display.Width, display.Height, display.RefreshHz);
                if (pick == null) continue;
                var mode = pick.Value;
                if (display.Width > 0 && (mode.Width != display.Width || mode.Height != display.Height))
                    result.Warnings.Add(string.Format("{0} doesn't offer {1}x{2} now; using {3}.",
                        display.Identity.DisplayName, display.Width, display.Height, mode));
                uint hz = (uint)mode.Hz;
                if (dm.dmPelsWidth == mode.Width && dm.dmPelsHeight == mode.Height && dm.dmDisplayFrequency == hz
                    && dm.dmPositionX == display.X && dm.dmPositionY == display.Y)
                    continue;

                dm.dmPelsWidth = (uint)mode.Width;
                dm.dmPelsHeight = (uint)mode.Height;
                dm.dmDisplayFrequency = hz;
                dm.dmPositionX = display.X;
                dm.dmPositionY = display.Y;
                dm.dmFields = DisplayConfigApi.DM_PELSWIDTH | DisplayConfigApi.DM_PELSHEIGHT
                    | DisplayConfigApi.DM_DISPLAYFREQUENCY | DisplayConfigApi.DM_POSITION;
                uint flags = DisplayConfigApi.CDS_UPDATEREGISTRY | DisplayConfigApi.CDS_NORESET;
                if (display.Primary) flags |= DisplayConfigApi.CDS_SET_PRIMARY;
                int rc = DisplayConfigApi.ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
                if (rc != DisplayConfigApi.DISP_CHANGE_SUCCESSFUL)
                    result.Warnings.Add(string.Format("Couldn't set {0} to {1} (code {2}).",
                        display.Identity.DisplayName, mode, rc));
                else pending = true;
            }
            if (pending) DisplayConfigApi.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        // ---------------------------------------------------------------- apply

        /// <summary>
        /// Makes the profile's displays the only active ones. Waits up to <paramref name="waitSeconds"/> for a display
        /// that isn't connected yet (a TV that is still waking up). The layout from before goes into
        /// <see cref="ApplyResult.Previous"/> so the caller can switch back.
        /// </summary>
        public static ApplyResult Apply(Profile profile, int waitSeconds)
        {
            var result = new ApplyResult();
            RoomLog.Write("Applying \"" + profile.Name + "\": " + profile.Summary);

            Profile before = null;
            try { before = Capture("Previous layout", true); }
            catch (Exception ex) { RoomLog.Write("Couldn't record the current layout: " + ex.Message); }

            DisplayConfig all = null;
            List<TargetInfo> targets = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, waitSeconds));
            string missing;
            while (true)
            {
                all = DisplayTopology.Query(DisplayConfigApi.QDC_ALL_PATHS);
                targets = DisplayTopology.DescribeTargets(all);
                missing = null;
                foreach (var d in profile.Displays)
                    if (MatchTarget(d.Identity, targets) < 0) { missing = d.Identity.DisplayName; break; }
                if (missing == null || DateTime.UtcNow >= deadline) break;
                Thread.Sleep(1000);
            }
            if (missing != null)
            {
                result.Message = missing + " isn't connected. Is it switched on?";
                RoomLog.Write("Failed: " + result.Message);
                return result;
            }

            var adapterLuids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targets)
            {
                string path = DisplayTopology.GetAdapterPath(LUID.FromValue(t.AdapterLuid));
                if (!string.IsNullOrEmpty(path)) adapterLuids[path] = t.AdapterLuid;
            }

            // A profile made by "only" has no saved arrays: it goes straight to the rebuilt topology.
            string error = "no saved layout";
            var exact = profile.Paths.Count > 0 ? BuildExactConfig(profile, adapterLuids, targets, out error) : null;
            int rc = -1;
            if (exact != null)
            {
                rc = DisplayConfigApi.SetDisplayConfig((uint)exact.Paths.Length, exact.Paths, (uint)exact.Modes.Length, exact.Modes,
                    DisplayConfigApi.SDC_APPLY | DisplayConfigApi.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | DisplayConfigApi.SDC_SAVE_TO_DATABASE | DisplayConfigApi.SDC_ALLOW_CHANGES);
                RoomLog.Write("Exact apply returned " + rc);
                if (rc == 0) result.Method = "exact";
            }
            else RoomLog.Write("Exact apply skipped: " + error);

            if (rc != 0)
            {
                var topology = BuildTopology(profile, all, targets, out error);
                if (topology == null)
                {
                    result.Message = "Couldn't build the layout: " + error + ".";
                    RoomLog.Write("Failed: " + result.Message);
                    return result;
                }
                rc = DisplayConfigApi.SetDisplayConfig((uint)topology.Length, topology, 0, null,
                    DisplayConfigApi.SDC_APPLY | DisplayConfigApi.SDC_TOPOLOGY_SUPPLIED | DisplayConfigApi.SDC_ALLOW_PATH_ORDER_CHANGES);
                RoomLog.Write("Topology apply returned " + rc);
                if (rc != 0)
                {
                    rc = DisplayConfigApi.SetDisplayConfig((uint)topology.Length, topology, 0, null,
                        DisplayConfigApi.SDC_APPLY | DisplayConfigApi.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                        | DisplayConfigApi.SDC_SAVE_TO_DATABASE | DisplayConfigApi.SDC_ALLOW_CHANGES);
                    RoomLog.Write("Supplied-topology apply returned " + rc);
                }
                if (rc != 0)
                {
                    result.Message = "Windows refused the layout (error " + rc + ").";
                    RoomLog.Write("Failed: " + result.Message);
                    return result;
                }
                result.Method = "rebuilt";
                ApplyModes(profile, result);
            }

            ApplyHdr(profile, result);
            ApplyAudio(profile, result);

            result.Previous = before;
            result.Success = true;
            result.Message = "Switched to \"" + profile.Name + "\".";
            foreach (var w in result.Warnings) RoomLog.Write("Warning: " + w);
            RoomLog.Write(result.Message + " (" + result.Method + ")");
            return result;
        }

        /// <summary>
        /// Sets the profile's sound output. A TV's HDMI output appears a few seconds after the TV becomes active, so this
        /// waits up to 8 s for it.
        /// </summary>
        static void ApplyAudio(Profile profile, ApplyResult result)
        {
            bool follow = profile.AudioFollowsDisplay && profile.Displays.Count > 0;
            if (!follow && string.IsNullOrEmpty(profile.AudioId) && string.IsNullOrEmpty(profile.AudioName)) return;
            string label = follow ? "the sound output of " + profile.Displays[0].Identity.DisplayName
                : string.IsNullOrEmpty(profile.AudioName) ? "the profile's sound output" : profile.AudioName;
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                while (true)
                {
                    var outputs = AudioOutputs.List();
                    int match = follow
                        ? AudioOutputs.ForDisplay(profile.Displays[0].Identity.FriendlyName, outputs)
                        : AudioOutputs.Match(profile.AudioId, profile.AudioName, outputs);
                    if (match >= 0)
                    {
                        if (outputs[match].IsDefault) return;
                        if (AudioOutputs.SetDefault(outputs[match].Id)) RoomLog.Write("Sound output: " + outputs[match].Name);
                        else result.Warnings.Add("Couldn't switch sound to " + outputs[match].Name + ".");
                        return;
                    }
                    if (DateTime.UtcNow >= deadline) break;
                    Thread.Sleep(500);
                }
                result.Warnings.Add(label + " isn't available, so sound stays where it was.");
            }
            catch (Exception ex) { result.Warnings.Add("Couldn't switch sound: " + ex.Message); }
        }

        static void ApplyHdr(Profile profile, ApplyResult result)
        {
            List<TargetInfo> active;
            try { active = DisplayTopology.DescribeTargets(DisplayTopology.Query(DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS)); }
            catch (Exception ex) { result.Warnings.Add("Couldn't check HDR: " + ex.Message); return; }

            foreach (var display in profile.Displays)
            {
                if (display.Hdr < 0) continue;
                int match = MatchTarget(display.Identity, active);
                if (match < 0) continue;
                var luid = LUID.FromValue(active[match].AdapterLuid);
                int want = display.Hdr == 0 ? 0 : 1; // 1 = on, 2 = on where supported (unsupported is skipped below)
                int now = DisplayTopology.GetHdrState(luid, active[match].TargetId);
                if (now < 0 || now == want) continue;
                if (!DisplayTopology.SetHdrState(luid, active[match].TargetId, want == 1))
                    result.Warnings.Add("Couldn't turn HDR " + (want == 1 ? "on" : "off") + " for " + display.Identity.DisplayName + ".");
            }
        }
    }
}
