using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Displicare
{
    /// <summary>
    /// One place you use the PC from: a display that becomes the only (and main) screen when you switch to it. Rooms are
    /// created automatically for every connected display and pick up the settings you use there.
    /// </summary>
    [DataContract]
    public class Room
    {
        public const string HdrAuto = "auto", HdrOn = "on", HdrOff = "off";
        /// <summary>Audio "auto": the display's own sound output (a TV's HDMI audio) if it has one, else leave sound alone.</summary>
        public const string AudioAuto = "auto", AudioKeep = "keep", AudioDevice = "device";

        public Room()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Hdr = HdrAuto;
            Audio = AudioAuto;
            Hotkey = "";
            KnownModes = new List<string>();
        }

        /// <summary>Monitor model id from the EDID, e.g. GSM82CD: stays the same on any port or graphics card.</summary>
        [DataMember] public string Key { get; set; }
        [DataMember] public string Name { get; set; }
        /// <summary>The display's own name from its EDID, e.g. "LG TV SSCR2".</summary>
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string DevicePath { get; set; }
        [DataMember] public string AdapterPath { get; set; }
        [DataMember] public int EdidManufacturer { get; set; }
        [DataMember] public int EdidProduct { get; set; }
        [DataMember] public int ConnectorInstance { get; set; }

        /// <summary>0 = the best the display offers.</summary>
        [DataMember] public int Width { get; set; }
        [DataMember] public int Height { get; set; }
        /// <summary>0 = the highest at that resolution.</summary>
        [DataMember] public int RefreshHz { get; set; }
        [DataMember] public string Hdr { get; set; }

        [DataMember] public string Audio { get; set; }
        [DataMember] public string AudioId { get; set; }
        [DataMember] public string AudioName { get; set; }

        [DataMember] public string Hotkey { get; set; }
        [DataMember] public bool OpenBigPicture { get; set; }
        /// <summary>Hidden rooms stay out of the tab, the tray and hotkeys (a portable screen you never game on).</summary>
        [DataMember] public bool Hidden { get; set; }
        /// <summary>A switch to this room was kept at least once with these settings, so it no longer asks.</summary>
        [DataMember] public bool Confirmed { get; set; }
        /// <summary>Modes Windows offered while this room was active ("3840x2160@144"), for the drop-downs.</summary>
        [DataMember] public List<string> KnownModes { get; set; }
    }
}
