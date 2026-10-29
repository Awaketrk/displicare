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
        public const string PadUp = "up", PadRight = "right", PadDown = "down", PadLeft = "left";
        public static readonly string[] PadDirections = { PadUp, PadRight, PadDown, PadLeft };
        public const string LaunchNone = "none", LaunchBigPicture = "bigpicture", LaunchXbox = "xbox";

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
        /// <summary>
        /// View + this D-pad direction on a controller switches here, without looking at a screen (you're on the couch and
        /// the TV is still blank): "up", "right", "down", "left", "" for none, null until Displicare has given it one.
        /// </summary>
        [DataMember] public string PadDirection { get; set; }
        /// <summary>Settings from 2.4.0 and earlier; read once into <see cref="Launcher"/>.</summary>
        [DataMember] public bool OpenBigPicture { get; set; }
        /// <summary>What opens when switching here (and closes when leaving): none, Steam Big Picture or Xbox mode.</summary>
        [DataMember] public string Launcher { get; set; }
        /// <summary>Hidden rooms stay out of the tab, the tray and hotkeys (a portable screen you never game on).</summary>
        [DataMember] public bool Hidden { get; set; }
        /// <summary>A switch to this room was kept at least once with these settings, so it no longer asks.</summary>
        [DataMember] public bool Confirmed { get; set; }
        /// <summary>Modes Windows offered while this room was active ("3840x2160@144"), for the drop-downs.</summary>
        [DataMember] public List<string> KnownModes { get; set; }
        /// <summary>
        /// Other screens (their room keys) turned on next to this one when switching here; this screen stays the main
        /// one. Null until chosen: the home room picks up the screens next to it the first time you leave the desk.
        /// </summary>
        [DataMember] public List<string> AlsoOn { get; set; }
        /// <summary>
        /// How this room's screens were last arranged with all of them on (positions, exact modes, HDR), learned from
        /// Windows; null until then. Only used while <see cref="AlsoOn"/> still names the same screens.
        /// </summary>
        [DataMember] internal global::Displicare.Rooms.Profile Layout { get; set; }
    }
}
