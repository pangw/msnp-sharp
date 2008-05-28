using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using System.IO;
using System.Xml;

namespace MSNPSharp.IO
{
    [XmlRoot("Stream"), Serializable]
    public class SerializableMemoryStream : MemoryStream,IXmlSerializable
    {
        #region IXmlSerializable Members

        public System.Xml.Schema.XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(System.Xml.XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return;
            reader.Read();
            XmlSerializer valueSerializer = new XmlSerializer(typeof(string));
            reader.ReadStartElement("base64");
            reader.ReadStartElement("string");
            string base64str = (string)valueSerializer.Deserialize(reader);
            byte[] byt = Convert.FromBase64String(base64str);
            reader.ReadEndElement();
            reader.ReadEndElement();
            Write(byt, 0, byt.Length);
            Flush();
        }

        public void WriteXml(System.Xml.XmlWriter writer)
        {
            XmlSerializer valueSerializer = new XmlSerializer(typeof(string));
            writer.WriteStartElement("base64");
            if (ToArray() != null)
            {
                valueSerializer.Serialize(writer, Convert.ToBase64String(ToArray(),Base64FormattingOptions.InsertLineBreaks));
            }
            writer.WriteEndElement();
        }

        #endregion
    }
}
