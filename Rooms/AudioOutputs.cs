using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Displicare.Rooms
{
    internal sealed class AudioOutput
    {
        public string Id;
        public string Name;
        public bool IsDefault;
    }

    /// <summary>Sound outputs (render endpoints) and the default one, which a profile switches along with the displays.</summary>
    internal static class AudioOutputs
    {
        /// <summary>Active outputs. A TV's HDMI output only shows up while that TV is an active display.</summary>
        public static List<AudioOutput> List()
        {
            var list = new List<AudioOutput>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                string defaultId = DefaultId(enumerator);
                IMMDeviceCollection devices;
                if (enumerator.EnumAudioEndpoints(AudioApi.eRender, AudioApi.DEVICE_STATE_ACTIVE, out devices) != 0) return list;
                try
                {
                    int count;
                    devices.GetCount(out count);
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice device;
                        if (devices.Item(i, out device) != 0) continue;
                        try
                        {
                            var output = new AudioOutput();
                            device.GetId(out output.Id);
                            output.Name = FriendlyName(device) ?? output.Id;
                            output.IsDefault = output.Id == defaultId;
                            list.Add(output);
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(devices); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
            return list;
        }

        public static AudioOutput Default()
        {
            foreach (var o in List()) if (o.IsDefault) return o;
            return null;
        }

        static string DefaultId(IMMDeviceEnumerator enumerator)
        {
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(AudioApi.eRender, AudioApi.eMultimedia, out device) != 0) return null;
            try
            {
                string id;
                device.GetId(out id);
                return id;
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0) return null;
            try
            {
                var key = AudioApi.PKEY_Device_FriendlyName;
                PROPVARIANT value;
                if (store.GetValue(ref key, out value) != 0) return null;
                string name = value.vt == AudioApi.VT_LPWSTR ? Marshal.PtrToStringUni(value.pointerValue) : null;
                AudioApi.PropVariantClear(ref value);
                return name;
            }
            finally { Marshal.ReleaseComObject(store); }
        }

        /// <summary>Makes the output the default for all three roles (games, media and calls), as Sound settings does.</summary>
        public static bool SetDefault(string id)
        {
            var policy = (IPolicyConfig)new PolicyConfigClientComObject();
            try
            {
                bool ok = true;
                foreach (int role in new[] { AudioApi.eConsole, AudioApi.eMultimedia, AudioApi.eCommunications })
                    ok &= policy.SetDefaultEndpoint(id, role) == 0;
                return ok;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }

        /// <summary>
        /// The output a profile meant: the same endpoint id if it's there, else the same name, else the same device before
        /// the brackets ("LG TV SSCR2 (NVIDIA High Definition Audio)" still matches after the cable moves to another GPU).
        /// </summary>
        internal static int Match(string id, string name, IList<AudioOutput> outputs)
        {
            for (int i = 0; i < outputs.Count; i++)
                if (!string.IsNullOrEmpty(id) && string.Equals(outputs[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < outputs.Count; i++)
                if (string.Equals(outputs[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            string device = DevicePart(name);
            int hit = -1;
            for (int i = 0; i < outputs.Count; i++)
            {
                if (!string.Equals(DevicePart(outputs[i].Name), device, StringComparison.OrdinalIgnoreCase)) continue;
                if (hit >= 0) return -1; // two outputs share that name: don't guess
                hit = i;
            }
            return hit;
        }

        /// <summary>Partial match for typing: whole name, else the single output whose name contains the text.</summary>
        internal static int Find(string query, IList<AudioOutput> outputs, out string error)
        {
            error = null;
            int hit = -1;
            for (int i = 0; i < outputs.Count; i++)
                if (string.Equals(outputs[i].Name, query, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (hit >= 0) { error = "\"" + query + "\" matches several outputs."; return -1; }
                hit = i;
            }
            if (hit < 0) error = "No active sound output matches \"" + query + "\".";
            return hit;
        }

        /// <summary>
        /// The sound output that belongs to a display: Windows names HDMI/DisplayPort audio after the monitor's EDID name,
        /// e.g. "LG TV SSCR2 (NVIDIA High Definition Audio)". -1 when the display has none (or it isn't active yet).
        /// </summary>
        internal static int ForDisplay(string displayName, IList<AudioOutput> outputs)
        {
            if (string.IsNullOrEmpty(displayName)) return -1;
            for (int i = 0; i < outputs.Count; i++)
                if (string.Equals(DevicePart(outputs[i].Name), displayName.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// The endpoint name without its device in brackets and without the "1 - " Windows puts in front when two outputs
        /// share a name: "1 - LG TV SSCR2 (AMD High Definition Audio Device)" gives "LG TV SSCR2".
        /// </summary>
        internal static string DevicePart(string name)
        {
            if (name == null) return "";
            int open = name.LastIndexOf(" (", StringComparison.Ordinal);
            string part = (open > 0 && name.EndsWith(")") ? name.Substring(0, open) : name).Trim();
            int dash = part.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                int n;
                if (int.TryParse(part.Substring(0, dash), out n)) part = part.Substring(dash + 3).Trim();
            }
            return part;
        }
    }

    /// <summary>
    /// Listens for sound output changes without polling: a TV's HDMI output appearing a while after the TV comes on,
    /// and the default output being changed by the user. Callbacks arrive on a Windows thread and are passed to the
    /// UI thread.
    /// </summary>
    [ComVisible(true)]
    internal sealed class AudioWatcher : IMMNotificationClient, IDisposable
    {
        readonly IMMDeviceEnumerator enumerator;
        readonly Action<string> defaultChanged;
        readonly Action outputsChanged;
        readonly System.Windows.Threading.Dispatcher dispatcher;
        bool registered;

        public AudioWatcher(Action<string> defaultChanged, Action outputsChanged, System.Windows.Threading.Dispatcher dispatcher)
        {
            this.defaultChanged = defaultChanged;
            this.outputsChanged = outputsChanged;
            this.dispatcher = dispatcher;
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            registered = enumerator.RegisterEndpointNotificationCallback(this) == 0;
        }

        public void OnDeviceStateChanged(string deviceId, int newState) { Post(outputsChanged); }
        public void OnDeviceAdded(string deviceId) { Post(outputsChanged); }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key) { }

        public void OnDefaultDeviceChanged(int dataFlow, int role, string defaultDeviceId)
        {
            // One notification per role; the multimedia one is enough.
            if (dataFlow != AudioApi.eRender || role != AudioApi.eMultimedia) return;
            dispatcher.BeginInvoke((Action)delegate { defaultChanged(defaultDeviceId); });
        }

        void Post(Action action)
        {
            dispatcher.BeginInvoke(action);
        }

        public void Dispose()
        {
            try { if (registered) enumerator.UnregisterEndpointNotificationCallback(this); }
            catch (Exception) { }
            registered = false;
            try { Marshal.ReleaseComObject(enumerator); }
            catch (Exception) { }
        }
    }
}
