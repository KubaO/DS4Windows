// Copyright Ⓒ 2019 Kuba Ober
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is furnished
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace DS4Windows
{
    [XmlRoot("DS4Windows")]
    [XmlInclude(typeof(DS4ProfileXML))]
    public class DS4Profile
    {
        // This is the serializable profile that ends up in the XML file.
        // All XML-specific adaptations are in the DS4ProfileXML adapter class.
        // All fields marked as XmlIgnore have serialization adapters implemented in DS4ProfileXML.

        public X<bool> flushHIDQueue = true;
        [XmlElement("touchToggle")] public X<bool> enableTouchToggle;
        public X<int> idleDisconnectTimeout = 0;
        public DS4Color Color = System.Drawing.Color.White;
        [XmlElement("RumbleBoost")] public X<byte> rumble = 100;
        [XmlElement("ledAsBatteryIndicator")] public X<bool> ledAsBattery = false;
        public X<byte> FlashType = 0;
        [XmlElement("flashBatteryAt")] public X<byte> flashAt = 0;
        public X<byte> touchSensitivity = 100;
        [XmlElement("LowColor")] public DS4Color LowLed = System.Drawing.Color.Black;
        [XmlElement("ChargingColor")] public DS4Color ChargingLed = System.Drawing.Color.Black;
        [XmlElement("FlashColor")] public DS4Color FlashLed = System.Drawing.Color.Black;
        public X<bool> touchpadJitterCompensation = true;

        public X<bool> lowerRCOn = false;
        public X<byte> tapSensitivity = 0;
        public X<bool> doubleTap = false;
        public X<int> scrollSensitivity = 0;
        [XmlElement("TouchpadInvert")] public X<int> touchpadInvert;

        [XmlIgnore] public TriggerDeadZoneZInfo l2ModInfo, r2ModInfo;
        
        [XmlIgnore] public double LSRotation; // in radians
        [XmlIgnore] public double RSRotation; // in radians

        [XmlElement("ButtonMouseSensitivity")] public X<int> buttonMouseSensitivity = 25;
        [XmlElement("Rainbow")] public X<double> rainbow = 0;

        [XmlIgnore] public StickDeadZoneInfo lsModInfo, rsModInfo;

        public X<double> SXDeadZone = 0.02;
        public X<double> SZDeadZone = 0.02;
        [XmlIgnore] public double SXMaxZone = 1.0;
        [XmlIgnore] public double SZMaxZone = 1.0;
        [XmlIgnore] public double SXAntiDeadZone = 0.0;
        [XmlIgnore] public double SZAntiDeadZone = 0.0;

        [XmlIgnore] public double LSSens = 1.0;
        [XmlIgnore] public double RSSens = 1.0;
        [XmlIgnore] public double l2Sens = 1.0;
        [XmlIgnore] public double r2Sens = 1.0;
        [XmlIgnore] public double SXSens = 1.0;
        [XmlIgnore] public double SZSens = 1.0;

        [XmlElement("ChargingType")] public X<int> chargingType = 0;
        [XmlElement("MouseAcceleration")] public X<bool> mouseAcceleration = true;

        [XmlIgnore, XmlElement("ShiftModifier")] public X<int> shiftM; // Currently unused
        [XmlElement("LaunchProgram")] public string launchProgram = String.Empty;
        [XmlElement("DinputOnly")] public X<bool> dinputOnly = false;
        [XmlElement("StartTouchpadOff")] public X<bool> startTouchpadOff = false;
        [XmlElement("UseTPforControls")] public X<bool> useTPforControls = false;
        [XmlElement("UseSAforMouse")] public X<bool> useSAforMouse = false;
        [XmlElement("SATriggers")] public string sATriggers = String.Empty;
        [XmlIgnore] public bool sATriggerCond;
        [XmlIgnore] public SASteeringWheelEmulationAxisType sASteeringWheelEmulationAxis =
            SASteeringWheelEmulationAxisType.None;
        [XmlElement("SASteeringWheelEmulationRange")]
        public X<int> sASteeringWheelEmulationRange = 360;

        [XmlIgnore] public int[] touchDisInvertTriggers = {-1};
        [XmlElement("GyroSensitivity")] public X<int> GyroSensitivity = 100;
        [XmlElement("GyroInvert")] public X<int> GyroInvert = 0;
        [XmlElement("LSCurve")] public X<int> LSCurve = 0;
        [XmlElement("RSCurve")] public X<int> RSCurve = 0;
        [XmlIgnore] public List<string> ProfileActions;

        [XmlElement("ProfileActions")]
        public string _ProfileActions
        {
            get { return (ProfileActions != null) ? String.Join("/", ProfileActions) : String.Empty; }
            set { ProfileActions = value?.Split('/').ToList(); }
        }

        public ModNode Control;
        public ModNode ShiftControl;
    }

    public class ModNode
    {
        [XmlElement("Button")] public NamedElements<ModButton> Buttons;
        [XmlElement("Macro")] public NamedElements<ModMacro> Macros;
        [XmlElement("Key")] public NamedElements<ModKey> Keys;
        public NamedElements<ModExtra> Extras;
        [XmlElement("KeyType")] public NamedElements<ModKeyType> KeyTypes;
    }

    public interface INamedElement
    {
        string name { get; set; }
    }

    public class ModButton : INamedElement
    {
        [XmlIgnore] public string name { get; set; }
        [XmlText] public string text;
    }

    public class ModMacro : INamedElement
    {
        private string _text;
        private int[] _keys;

        [XmlIgnore] public string name { get; set; }

        [XmlText]
        public string text
        {
            get { return _text; }
            set
            {
                _text = value;
                value?.Split('/').Select(s =>
                {
                    int val;
                    return int.TryParse(s, out val) ? (int?) val : null;
                }).OfType<int>().ToArray();
            }
        }

        public int[] keys
        {
            get { return _keys; }
            set
            {
                _keys = value;
                _text = String.Join("/", _keys.Select(k => k.ToString()).ToArray());
            }
        }
    }

    public class ModKey : INamedElement
    {
        [XmlIgnore] public string name { get; set; }
        [XmlText(typeof(ushort))] public X<ushort> wvk;
    }

    public class ModExtra : INamedElement
    {
        [XmlIgnore] public string name { get; set; }
        [XmlText] public string text;
    }

    public class ModKeyType : INamedElement
    {
        private DS4KeyType _type;
        private string _text;

        [XmlIgnore] public string name { get; set; }

        [XmlText]
        public string text
        {
            set
            {
                _text = value;
                _type = DS4KeyType.None;
                if (value.Contains(DS4KeyType.ScanCode.ToString()))
                    _type |= DS4KeyType.ScanCode;
                if (value.Contains(DS4KeyType.Toggle.ToString()))
                    _type |= DS4KeyType.Toggle;
                if (value.Contains(DS4KeyType.Macro.ToString()))
                    _type |= DS4KeyType.Macro;
                if (value.Contains(DS4KeyType.HoldMacro.ToString()))
                    _type |= DS4KeyType.HoldMacro;
                if (value.Contains(DS4KeyType.Unbound.ToString()))
                    _type |= DS4KeyType.Unbound;
            }
            get { return _text; }
        }

        [XmlIgnore]
        public DS4KeyType type
        {
            get { return _type; }
            set
            {
                _type = value;
                var sb = new StringBuilder();
                if ((_type & DS4KeyType.ScanCode) != 0) sb.Append(DS4KeyType.ScanCode.ToString());
                if ((_type & DS4KeyType.Toggle) != 0) sb.Append(DS4KeyType.Toggle.ToString());
                if ((_type & DS4KeyType.Macro) != 0) sb.Append(DS4KeyType.Macro.ToString());
                if ((_type & DS4KeyType.HoldMacro) != 0) sb.Append(DS4KeyType.HoldMacro.ToString());
                if ((_type & DS4KeyType.Unbound) != 0) sb.Append(DS4KeyType.Unbound.ToString());
            }
        }
    }

    public class Sensitivity
    {
        public double LSSens = 1.0;
        public double RSSens = 1.0;
        public double l2Sens = 1.0;
        public double r2Sens = 1.0;
        public double SXSens = 1.0;
        public double SZSens = 1.0;
    }

    [Serializable, XmlRoot("DS4Windows")]
    public sealed class DS4ProfileXML : DS4Profile
    {
        // This class adapts the DS4Profile to storage in the XML file.
        
        // The section below maps the XML elements to sub-structure fields.

        public X<byte> LeftTriggerMiddle
        {
            get => l2ModInfo.deadZone;
            set => l2ModInfo.deadZone = value;
        }
        public X<byte> RightTriggerMiddle
        {
            get => r2ModInfo.deadZone;
            set => r2ModInfo.deadZone = value;
        }
        public X<int> L2AntiDeadZone
        {
            get => l2ModInfo.antiDeadZone;
            set => r2ModInfo.antiDeadZone = value;
        }
        public X<int> R2AntiDeadZone
        {
            get => r2ModInfo.antiDeadZone;
            set => r2ModInfo.antiDeadZone = value;
        }
        public X<int> L2MaxZone
        {
            get => l2ModInfo.maxZone;
            set => l2ModInfo.maxZone = Util.Clamp((int)value, 0, 100);
        }
        public X<int> R2MaxZone
        {
            get => r2ModInfo.maxZone;
            set => r2ModInfo.maxZone = Util.Clamp((int)value, 0, 100);
        }

        [XmlElement("LSRotation")]
        public X<int> _LSRotation  // stored in degrees
        {
            get => (int)Math.Round(LSRotation * 180.0 / Math.PI);
            set => LSRotation = value * Math.PI / 180.0;
        }
        [XmlElement("RSRotation")]
        public X<int> _RSRotation  // stored in degrees
        {
            get => (int)Math.Round(RSRotation * 180.0 / Math.PI);
            set => RSRotation = value * Math.PI / 180.0;
        }

        public X<int> LSDeadZone
        {
            get => lsModInfo.deadZone;
            set => lsModInfo.deadZone = value;
        }
        public X<int> RSDeadZone
        {
            get => rsModInfo.deadZone;
            set => rsModInfo.deadZone = value;
        }
        public X<int> LSAntiDeadZone
        {
            get => lsModInfo.antiDeadZone;
            set => lsModInfo.antiDeadZone = value;
        }
        public X<int> RSAntiDeadZone
        {
            get => rsModInfo.antiDeadZone;
            set => rsModInfo.antiDeadZone = value;
        }

        public X<int> LSMaxZone
        {
            get => lsModInfo.maxZone;
            set => lsModInfo.maxZone = Util.Clamp((int)value, 0, 100);
        }
        public X<int> RSMaxZone
        {
            get => rsModInfo.maxZone;
            set => rsModInfo.maxZone = Util.Clamp((int)value, 0, 100);
        }

        [XmlElement("SXMaxZone")]
        public X<int> _SXMaxZone
        {
            get => (int) (SXMaxZone * 0.01);
            set => SXMaxZone = Util.Clamp(value * 0.01, 0.0, 1.0);
        }
        [XmlElement("SZMaxZone")]
        public X<int> _SZMaxZone
        {
            get => (int)(SZMaxZone * 0.01);
            set => SZMaxZone = Util.Clamp(value * 0.01, 0.0, 1.0);
        }
        [XmlElement("SXAntiDeadZone")]
        public X<int> _SXAntiDeadZone
        {
            get => (int)(SXAntiDeadZone * 0.01);
            set => SXAntiDeadZone = Util.Clamp(value * 0.01, 0.0, 1.0);
        }
        [XmlElement("SZAntiDeadZone")]
        public X<int> _SZAntiDeadZone
        {
            get => (int)(SZAntiDeadZone * 0.01);
            set => SZAntiDeadZone = Util.Clamp(value * 0.01, 0.0, 1.0);
        }

        [XmlElement("Sensitivity")]
        public string _Sensitivity
        {
            get => $"{LSSens}|{RSSens}|{l2Sens}|{r2Sens}|{SXSens}|{SZSens}";
            set
            {
                try
                {
                    string[] s = value.Split('|');
                    if (s.Length == 1) s = value.Split(',');
                    double[] d = s.Select(str =>  double.Parse(str)).ToArray();
                    LSSens = d[0] < .5f ? d[0] : 1.0;
                    RSSens = d[1] < .5f ? d[1] : 1.0;
                    l2Sens = d[2] < .1f ? d[2] : 1.0;
                    r2Sens = d[3] < .1f ? d[3] : 1.0;
                    SXSens = d[4] < .5f ? d[4] : 1.0;
                    SZSens = d[5] < .5f ? d[5] : 1.0;
                } catch { }
            }
        }

        [XmlElement("SATriggerCond")]
        public string _SATriggerCond
        {
            get => sATriggerCond ? "and" : "or";
            set => sATriggerCond = value == "and" || value != "or";
        }

        [XmlElement("SASteeringWheelEmulationAxis")]
        public string _SASteeringWheelEmulationAxis
        {
            get => sASteeringWheelEmulationAxis.ToString();
            set { SASteeringWheelEmulationAxisType.TryParse(value, out sASteeringWheelEmulationAxis); }
        }

        [XmlElement("TouchDisInvTriggers")]
        public string _TouchDisInvTriggers
        {
            get => String.Join(",", touchDisInvertTriggers.Select(s => s.ToString()));
            set => touchDisInvertTriggers = value.Split(',')
                .Select(s => Util.TryParse<int>(s)).Where(i => i.HasValue).Select(i => i.Value).ToArray();
        }

        // The section below maps the old profile schema on reading to the
        // current schema. All properties should be write-only, i.e. have a dummy getter,
        // and have ShouldSerialize{PropertyName} return false.
        // **The dummy getters are essential. XmlSerializer will ignore set-only
        // properties!**

        public bool ShouldSerializeRed() => false;
        public byte Red
        {
            set { Color.red = value; }
            get { return 0; }
        }
        public bool ShouldSerializeGreen() => false;
        public byte Green
        {
            set { Color.green = value; }
            get { return 0; }
        }
        public bool ShouldSerializeBlue() => false;
        public byte Blue
        {
            set { Color.blue = value; }
            get { return 0; }
        }
        public bool ShouldSerializeLowRed() => false;
        public byte LowRed
        {
            set { LowLed.red = value; }
            get { return 0; }
        }
        public bool ShouldSerializeLowGreen() => false;
        public byte LowGreen
        {
            set { LowLed.green = value; }
        }
        public bool ShouldSerializeLowBlue() => false;
        public byte LowBlue
        {
            set { LowLed.blue = value; }
        }
        public bool ShouldSerializeChargingRed() => false;
        public byte ChargingRed
        {
            set { ChargingLed.red = value; }
        }
        public bool ShouldSerializeChargingGreen() => false;
        public byte ChargingGreen
        {
            set { ChargingLed.green = value; }
        }
        public bool ShouldSerializeChargingBlue() => false;
        public byte ChargingBlue
        {
            set { ChargingLed.blue = value; }
        }
    }

    public struct X<T> : IXmlSerializable where T: struct
    {
        // X: The gracefully-degrading serialization wrapper.
        // It doesn't throw.
        private T _value;
        public static implicit operator T(X<T> o) => o._value;
        public static implicit operator X<T>(T o) => new X<T>(o);

        X(T init) { _value = init; }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteString(_value.ToString());
        }

        public void ReadXml(XmlReader reader)
        {
            var raw = reader.ReadElementContentAsString();
            var conv = TypeDescriptor.GetConverter(_value);
            if (conv != null && conv.CanConvertFrom(typeof(string)))
                _value = (T) conv.ConvertFromInvariantString(raw);
        }

        public XmlSchema GetSchema() => null;
    }

    public struct XRef<T, U> where T : struct
    {
        // Based on https://stackoverflow.com/a/2982037/1329652
        // This has dubious usability.
        private U _via;
        private readonly Func<U, T> getter;
        private readonly Action<U, T> setter;
        public static implicit operator T(XRef<T, U> o) => o.Value;
        public XRef(U via, Func<U, T> getter, Action<U, T> setter)
        {
            this._via = via;
            this.getter = getter;
            this.setter = setter;
        }
        
        public T Value
        {
            get => getter(_via);
            set => setter(_via, value);
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteString(Value.ToString());
        }

        public void ReadXml(XmlReader reader)
        {
            var raw = reader.ReadElementContentAsString();
            var conv = TypeDescriptor.GetConverter(default(T));
            if (conv.CanConvertFrom(typeof(string)))
                Value = (T)conv.ConvertFromInvariantString(raw);
        }
    }

    public class NamedElements<T> : IXmlSerializable, IEnumerable<T> where T : INamedElement, new()
    {
        private XmlSerializer serializer;
        public List<T> items { get; set; } = new List<T>();

        public void ReadXml(XmlReader reader)
        {
            if (serializer == null) serializer = new XmlSerializer(typeof(T));
            if (!reader.IsStartElement()) throw new XmlException();
            items.Clear();
            if (reader.IsEmptyElement) return;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var name = reader.Name;
                    var item = (T)serializer.Deserialize(reader);
                    item.name = name;
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    break;
                }
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            if (serializer == null) serializer = new XmlSerializer(typeof(T));
            var sb = new StringBuilder();
            var strWriter = new StringWriter(sb);

            foreach (T item in items)
            {
                // serialize all items with their default name
                serializer.Serialize(strWriter, item);
            }

            var preSerialized = sb.ToString();
            var reader = new XmlTextReader(new StringReader(preSerialized));
            foreach (T item in items)
            {
                // write out the serialized items with correct names
                if (!reader.IsStartElement()) throw new XmlException();
                //bool empty = reader.IsEmptyElement;
                writer.WriteStartElement(item.name);
                if (reader.HasAttributes)
                    writer.WriteAttributes(reader, true);
                var contents = reader.ReadInnerXml();
                writer.WriteRaw(contents);
                writer.WriteEndElement();
                while (reader.Read() && !reader.IsStartElement())
                {
                }
            }
        }

        public XmlSchema GetSchema() => null;

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)items).GetEnumerator();
        }

        private IEnumerator GetEnumeratorNonGeneric()
        {
            return this.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumeratorNonGeneric();
        }
    }
}
