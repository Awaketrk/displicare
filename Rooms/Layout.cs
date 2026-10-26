using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Displicare.Rooms
{
    /// <summary>Identifies one physical monitor across reboots, driver updates and (where possible) port changes.</summary>
    [DataContract]
    internal sealed class DisplayIdentity
    {
        /// <summary>Monitor device path, e.g. \\?\DISPLAY#GSM82CD#5&amp;3a6f26f9&amp;3&amp;UID1036#{...}. Changes when the cable moves to another port.</summary>
        [DataMember(Order = 1)] public string DevicePath;
        [DataMember(Order = 2)] public string FriendlyName;
        [DataMember(Order = 3)] public ushort EdidManufacturer;
        [DataMember(Order = 4)] public ushort EdidProduct;
        [DataMember(Order = 5)] public uint ConnectorInstance;
        [DataMember(Order = 6)] public string AdapterPath;

        /// <summary>Three-letter PnP vendor code from the EDID manufacturer id, e.g. GSM for LG.</summary>
        public string VendorCode
        {
            get
            {
                // The EDID id is big-endian; DisplayConfig hands it over byte-swapped.
                int id = ((EdidManufacturer & 0xFF) << 8) | (EdidManufacturer >> 8);
                if (id == 0) return "";
                char a = (char)('A' - 1 + ((id >> 10) & 0x1F));
                char b = (char)('A' - 1 + ((id >> 5) & 0x1F));
                char c = (char)('A' - 1 + (id & 0x1F));
                return new string(new[] { a, b, c });
            }
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(FriendlyName) ? "Unknown display" : FriendlyName; }
        }
    }

    /// <summary>The readable part of a profile: what each display should look like.</summary>
    [DataContract]
    internal sealed class ProfileDisplay
    {
        [DataMember(Order = 1)] public DisplayIdentity Identity;
        [DataMember(Order = 2)] public bool Primary;
        [DataMember(Order = 3)] public int X;
        [DataMember(Order = 4)] public int Y;
        [DataMember(Order = 5)] public int Width;
        [DataMember(Order = 6)] public int Height;
        [DataMember(Order = 7)] public double RefreshHz;
        /// <summary>-1 = leave as is (or not supported), 0 = HDR off, 1 = HDR on, 2 = on where supported.</summary>
        [DataMember(Order = 8)] public int Hdr;
        /// <summary>Native resolution from the EDID, used when Width is 0 ("best"); 0 when unknown.</summary>
        public int NativeWidth, NativeHeight;
    }

    [DataContract]
    internal sealed class SavedAdapter
    {
        [DataMember(Order = 1)] public long Luid;
        [DataMember(Order = 2)] public string DevicePath;
    }

    /// <summary>One active DISPLAYCONFIG_PATH_INFO, with adapter LUIDs as saved (remapped on apply).</summary>
    [DataContract]
    internal sealed class SavedPath
    {
        [DataMember(Order = 1)] public int Display;
        [DataMember(Order = 2)] public long SourceAdapter;
        [DataMember(Order = 3)] public uint SourceId;
        [DataMember(Order = 4)] public uint SourceModeIdx;
        [DataMember(Order = 5)] public long TargetAdapter;
        [DataMember(Order = 6)] public uint TargetId;
        [DataMember(Order = 7)] public uint TargetModeIdx;
        [DataMember(Order = 8)] public uint OutputTechnology;
        [DataMember(Order = 9)] public uint Rotation;
        [DataMember(Order = 10)] public uint Scaling;
        [DataMember(Order = 11)] public uint RefreshNumerator;
        [DataMember(Order = 12)] public uint RefreshDenominator;
        [DataMember(Order = 13)] public uint ScanLineOrdering;
    }

    /// <summary>One DISPLAYCONFIG_MODE_INFO; the 48-byte union is kept verbatim so timings come back exactly.</summary>
    [DataContract]
    internal sealed class SavedMode
    {
        [DataMember(Order = 1)] public uint InfoType;
        [DataMember(Order = 2)] public uint Id;
        [DataMember(Order = 3)] public long Adapter;
        [DataMember(Order = 4)] public string Data;
    }

    [DataContract]
    internal sealed class Profile
    {
        [DataMember(Order = 1)] public string Name;
        [DataMember(Order = 2)] public string Saved;
        [DataMember(Order = 3)] public List<ProfileDisplay> Displays = new List<ProfileDisplay>();
        [DataMember(Order = 4)] public List<SavedAdapter> Adapters = new List<SavedAdapter>();
        [DataMember(Order = 5)] public List<SavedPath> Paths = new List<SavedPath>();
        [DataMember(Order = 6)] public List<SavedMode> Modes = new List<SavedMode>();
        /// <summary>Default sound output to switch to (endpoint id, with the name as a fallback); empty leaves sound alone.</summary>
        [DataMember(Order = 8)] public string AudioId;
        [DataMember(Order = 9)] public string AudioName;
        /// <summary>Use the display's own sound output (a TV's HDMI audio) instead of AudioId/AudioName.</summary>
        public bool AudioFollowsDisplay;

        public ProfileDisplay PrimaryDisplay
        {
            get
            {
                foreach (var d in Displays) if (d.Primary) return d;
                return Displays.Count > 0 ? Displays[0] : null;
            }
        }

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                foreach (var d in Displays)
                {
                    if (d.Width <= 0) { parts.Add(d.Identity.DisplayName + " (best mode)"); continue; }
                    parts.Add(string.Format("{0} {1}x{2} @ {3:0.##} Hz{4}{5}",
                        d.Identity.DisplayName, d.Width, d.Height, d.RefreshHz,
                        d.Hdr == 1 ? " HDR" : "", d.Primary && Displays.Count > 1 ? " (primary)" : ""));
                }
                return string.Join(", ", parts.ToArray());
            }
        }
    }
}
