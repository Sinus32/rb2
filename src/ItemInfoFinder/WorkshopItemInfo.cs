using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ItemInfoFinder
{
    public class WorkshopItemInfo
    {
        private const string API_KEY = "9D15AF36A08F5131B61680F64BA4D73E";
        private const string GET_PUBLISHED_FILE_DETAILS_URL = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        public WorkshopItemInfo()
        {
            Data = new List<PublishedFileNode>();
        }

        public List<PublishedFileNode> Data { get; }

        public static WorkshopItemInfo GetWorkshopItemInfo(List<long> itemIds)
        {
            var result = new WorkshopItemInfo();
            if (itemIds.Count == 0)
                return result;

            var request = (HttpWebRequest)HttpWebRequest.Create(GET_PUBLISHED_FILE_DETAILS_URL);
            request.Method = "POST";
            request.Timeout = 10000;
            request.Accept = "application/xml";
            request.Headers.Add(HttpRequestHeader.AcceptLanguage, "en-US");
            request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip");

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
            {
                if (response.Headers[HttpResponseHeader.ContentEncoding] == "gzip")
                {
                    using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (var reader = XmlReader.Create(gzip, settings))
                    {
                        resp = new XmlDocument();
                        resp.Load(reader);
                    }
                }
                else
                {
                    using (var reader = XmlReader.Create(stream, settings))
                    {
                        resp = new XmlDocument();
                        resp.Load(reader);
                    }
                }
            }

            result.ReadResponse(resp);
            return result;
        }

        private void ReadResponse(XmlDocument resp)
        {
            if (Debugger.IsAttached)
            {
                using (var str = new StringWriter())
                {
                    using (var xml = XmlWriter.Create(str))
                        resp.WriteContentTo(xml);
                    Debug.WriteLine(str.ToString());
                }
            }

            if (resp.DocumentElement.LocalName != "response")
                throw new ArgumentException("Invalid response file.", nameof(resp));

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
                        try
                        {
                            var item = (PublishedFileNode)ser.Deserialize(reader);
                            Data.Add(item);
                        }
                        catch (Exception)
                        {
                            if (Debugger.IsAttached)
                                Debugger.Break();
                        }
                    }
                }
            }
        }
    }
}
