using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace ItemInfoFinder
{
    public class WorkshopItemInfo
    {
        private const string API_KEY = "9D15AF36A08F5131B61680F64BA4D73E";
        private const string GET_PUBLISHED_FILE_DETAILS_URL = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        public static WorkshopItemInfo GetWorkshopItemInfo(List<long> itemIds)
        {
            try
            {
                var request = (HttpWebRequest)HttpWebRequest.Create(GET_PUBLISHED_FILE_DETAILS_URL);
                request.Method = "POST";
                request.Timeout = 10000;
                request.Accept = "application/xml";
                request.Headers.Add(HttpRequestHeader.AcceptLanguage, "en-US");
                request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip, deflate");

                var data = new StringBuilder();
                data.AppendFormat("key={0}&format=xml&itemcount={1}", API_KEY, itemIds.Count);
                for (int i = 0; i < itemIds.Count; ++i)
                    data.AppendFormat("&publishedfileids[{0}]={1}", i, itemIds[i]);
                var encoding = new UTF8Encoding(false);
                var raw = encoding.GetBytes(data.ToString());
                request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                request.ContentLength = raw.Length;

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(raw, 0, raw.Length);
                    stream.Flush();
                }

                var settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Parse;
                XmlDocument resp;

                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = XmlReader.Create(stream, settings))
                {
                    resp = new XmlDocument();
                    resp.Load(reader);
                }

                var result = new WorkshopItemInfo();
                if (result.ReadResponse(resp))
                    return result;
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool ReadResponse(XmlDocument resp)
        {
            if (resp.DocumentElement.LocalName != "response")
                return false;

            Data = new List<PublishedFileNode>();
            var ser = new XmlSerializer(typeof(PublishedFileNode));

            foreach (XmlNode mainnode in resp.DocumentElement.ChildNodes)
            {
                if (mainnode.LocalName != "publishedfiledetails")
                    continue;

                foreach (XmlNode node in mainnode.ChildNodes)
                {
                    if (node.LocalName != "publishedfile")
                        continue;

                    using (var reader = new StringReader(node.OuterXml))
                    {
                        var item = (PublishedFileNode)ser.Deserialize(reader);
                        Data.Add(item);
                    }
                }
            }

            return true;
        }

        public List<PublishedFileNode> Data { get; private set; }
    }
}
