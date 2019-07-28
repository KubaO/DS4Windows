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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace DS4Windows
{
    [XmlRoot("DS4Windows")]
    [XmlInclude(typeof(DS4ProfileLegacy))]
    public class DS4Profile
    {
        // Note: The field names here match those in the XML serialization.
        // If you change names, add [XmlElement("oldName")] with the old name
        // so that the XML file can be deserialized.
        public X<bool> flushHIDQueue = true;
        public X<int> idleDisconnectTimeout = 0;
        public DS4Color Color = System.Drawing.Color.White;
        public X<byte> RumbleBoost = 100;
        public X<bool> ledAsBatteryIndicator = false;
        public X<byte> FlashType = 0;
        public X<byte> flashBatteryAt = 0;
        public X<byte> touchSensitivity = 100;
        public DS4Color LowColor = System.Drawing.Color.Black;
        public DS4Color ChargingColor = System.Drawing.Color.Black;
        public DS4Color FlashColor = System.Drawing.Color.Black;
        public X<bool> touchpadJitterCompensation = true;

        public X<bool> lowerRCOn = false;
        public X<byte> tapSensitivity = 0;
        public X<bool> doubleTap = false;
        public X<int> scrollSensitivity = 0;
        [XmlElement("LeftTriggerMiddle")] public X<byte> l2DeadZone = 0;
        [XmlElement("RightTriggerMiddle")] public X<byte> r2DeadZone = 0;
        public X<int> ButtonMouseSensitivity = 25;
        public X<double> Rainbow = 0;
        public X<int> LSDeadZone = 0;
        public X<int> RSDeadZone = 0;
        public X<double> SXDeadZone = 0.25;
        public X<double> SZDeadZone = 0.25;
        public Sensitivity Sensitivity = new Sensitivity();
        public X<int> ChargingType = 0;
        public X<bool> MouseAcceleration = true;

        [XmlIgnore] public X<int> ShiftModifier; // Currently unused
        public string LaunchProgram = String.Empty;
        public X<bool> DinputOnly = false;
        public X<bool> StartTouchpadOff = false;
        public X<bool> UseTPforControls = false;
        public X<bool> UseSAforMouse = false;
        public string SATriggers = String.Empty;
        public X<int> GyroSensitivity = 100;
        public X<int> GyroInvert = 0;
        public X<int> LSCurve = 0;
        public X<int> RSCurve = 0;
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

    public class Sensitivity : IXmlSerializable
    {
        public double LSSens = 1.0;
        public double RSSens = 1.0;
        public double l2Sens = 1.0;
        public double r2Sens = 1.0;
        public double SXSens = 1.0;
        public double SZSens = 1.0;

        public void ReadXml(XmlReader reader)
        {
            var raw = reader.ReadElementContentAsString();
            string[] s = raw.Split('|');
            if (s.Length == 1) s = raw.Split(',');
            double[] d = s.Select(str => Math.Min(0.5f, double.Parse(str))).ToArray();
            LSSens = d[0];
            RSSens = d[1];
            l2Sens = d[2];
            r2Sens = d[3];
            SXSens = d[4];
            SZSens = d[5];
        }

        public void WriteXml(XmlWriter writer)
        {
            var raw = $"{LSSens}|{RSSens}|{l2Sens}|{r2Sens}|{SXSens}|{SZSens}";
            writer.WriteString(raw);
        }

        public XmlSchema GetSchema() => null;
    }

    [Serializable, XmlRoot("DS4Windows")]
    public sealed class DS4ProfileLegacy : DS4Profile
    {
        // This is an adapter used to convert the profile to the current
        // schema. All properties should be write-only, i.e. have a dummy getter,
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
            set { LowColor.red = value; }
            get { return 0; }
        }
        public bool ShouldSerializeLowGreen() => false;
        public byte LowGreen
        {
            set { LowColor.green = value; }
        }
        public bool ShouldSerializeLowBlue() => false;
        public byte LowBlue
        {
            set { LowColor.blue = value; }
        }
        public bool ShouldSerializeChargingRed() => false;
        public byte ChargingRed
        {
            set { ChargingColor.red = value; }
        }
        public bool ShouldSerializeChargingGreen() => false;
        public byte ChargingGreen
        {
            set { ChargingColor.green = value; }
        }
        public bool ShouldSerializeChargingBlue() => false;
        public byte ChargingBlue
        {
            set { ChargingColor.blue = value; }
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
            object value = _value;
            if (value is bool)
            {
                writer.WriteString(value.ToString());
                if (false) writer.WriteString(XmlConvert.ToString((bool)value));
                // This would be preferable for XML compliance
            }
            else if (value is byte)
                writer.WriteString(XmlConvert.ToString((byte)value));
            else if (value is ushort)
                writer.WriteString(XmlConvert.ToString((ushort)value));
            else if (value is int)
                writer.WriteString(XmlConvert.ToString((int)value));
            else if (value is double)
                writer.WriteString(XmlConvert.ToString((double)value));
            else
                throw new NotImplementedException(
                    string.Format("X<T>: T is of an unsupported type {0}", typeof(T).ToString()));
          }

        public void ReadXml(XmlReader reader)
        {
            var raw = reader.ReadElementContentAsString();
            object result = null;
            try
            {
                if (typeof(T) == typeof(bool))
                {
                    bool b;
                    bool ok = Boolean.TryParse(raw, out b);
                    if (!ok) b = XmlConvert.ToBoolean(raw);
                    result = b;
                }
                else if (typeof(T) == typeof(byte))
                    result = XmlConvert.ToByte(raw);
                else if (typeof(T) == typeof(ushort))
                    result = XmlConvert.ToUInt16(raw);
                else if (typeof(T) == typeof(int))
                    result = XmlConvert.ToInt32(raw);
                else if (typeof(T) == typeof(double))
                    result = XmlConvert.ToDouble(raw);
                else
                    result = new NotImplementedException(
                        string.Format("X<T>: T is of an unsupported type {0}", typeof(T).ToString()));
            } catch { }
            if (result is Exception) throw (Exception)result;
            else if (result != null) _value = (T) result;
        }
        public XmlSchema GetSchema() => null;
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
