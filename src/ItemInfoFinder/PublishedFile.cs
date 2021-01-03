using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace ItemInfoFinder
{
    [XmlRoot("publishedfile")]
    public class PublishedFileNode
    {
        [XmlElement("publishedfileid")]
        public long PublishedFileId { get; set; }

        [XmlElement("creator")]
        public long Creator { get; set; }

        [XmlElement("creator_app_id")]
        public long CreatorAppId { get; set; }

        [XmlElement("consumer_app_id")]
        public long ConsumerAppId { get; set; }

        [XmlElement("filename")]
        public string Filename { get; set; }

        [XmlElement("file_size")]
        public long FileSize { get; set; }

        [XmlElement("file_url")]
        public string FileUrl { get; set; }

        [XmlElement("hcontent_file")]
        public long HContentFile { get; set; }

        [XmlElement("preview_url")]
        public string PreviewUrl { get; set; }

        [XmlElement("hcontent_preview")]
        public string HContentPreview { get; set; }

        [XmlElement("title")]
        public string Title { get; set; }

        [XmlIgnore]
        public string BaseTitle { get; set; }

        [XmlElement("description")]
        public string Description { get; set; }

        [XmlElement("time_created")]
        public DateTimeNode TimeCreated { get; set; }

        [XmlElement("time_updated")]
        public DateTimeNode TimeUpdated { get; set; }

        [XmlElement("visibility")]
        public int Visibility { get; set; }

        [XmlElement("banned")]
        public int Banned { get; set; }

        [XmlElement("ban_reason")]
        public string BanReason { get; set; }

        [XmlElement("subscriptions")]
        public long Subscriptions { get; set; }

        [XmlElement("favorited")]
        public long Favorited { get; set; }

        [XmlElement("lifetime_subscriptions")]
        public long LifetimeSubscriptions { get; set; }

        [XmlElement("lifetime_favorited")]
        public long LifetimeFavorited { get; set; }

        [XmlElement("views")]
        public long Views { get; set; }

        [XmlArray("tags")]
        [XmlArrayItem("tag")]
        public TagNode[] Tags { get; set; }

        public class TagNode
        {
            [XmlElement("tag")]
            public string Tag { get; set; }
        }

        public override string ToString()
        {
            return String.Format("{0}: {1}", PublishedFileId, Title);
        }

        public class DateTimeNode : IXmlSerializable
        {
            public DateTime? Value { get; set; }

            XmlSchema IXmlSerializable.GetSchema()
            {
                return null;
            }

            void IXmlSerializable.ReadXml(XmlReader reader)
            {
                if (reader.MoveToContent() != XmlNodeType.Element)
                    throw new FormatException();

                Boolean isEmptyElement = reader.IsEmptyElement;
                reader.ReadStartElement();
                if (!isEmptyElement)
                {
                    if (reader.NodeType == XmlNodeType.Text)
                    {
                        if (reader.HasValue)
                        {
                            int val;
                            if (Int32.TryParse(reader.Value, out val))
                                Value = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(val).ToLocalTime();
                        }
                        reader.Read();
                    }

                    reader.ReadEndElement();
                }
            }

            void IXmlSerializable.WriteXml(XmlWriter writer)
            {
                throw new NotImplementedException();
            }

            public override string ToString()
            {
                return Value.ToString();
            }
        }
    }
}
