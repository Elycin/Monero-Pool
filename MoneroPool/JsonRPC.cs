using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    public class JsonRPC
    {
        public JsonRPC(string URL)
        {
            Url = URL;
        }

        public string Url { get; }

        public int GetBlockCount()
        {
            return (int) InvokeMethod("getblockcount")["result"]["count"];
        }

        public JObject InvokeMethod(string a_sMethod, params object[] a_params)
        {
            try
            {
                var webRequest = (HttpWebRequest) WebRequest.Create(Url + "/" + "json_rpc");
                //webRequest.Credentials = Credentials;

                webRequest.ContentType = "application/json-rpc";
                webRequest.Method = "POST";

                var joe = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = "test",
                    ["method"] = a_sMethod
                };

                if (a_params != null)
                    if (a_params.Length > 0)
                        joe.Add(new JProperty("params", a_params[0]));

                var s = JsonConvert.SerializeObject(joe);
                //s=s.Substring(0, s.Length - 1);
                // serialize json for the request
                var byteArray = Encoding.UTF8.GetBytes(s);
                webRequest.ContentLength = byteArray.Length;

                using (var dataStream = webRequest.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }

                using (var webResponse = webRequest.GetResponse())
                {
                    using (var str = webResponse.GetResponseStream())
                    {
                        using (var sr = new StreamReader(str))
                        {
                            return JsonConvert.DeserializeObject<JObject>(sr.ReadToEnd());
                        }
                    }
                }
            }
            catch (WebException)
            {
                Logger.Log(Logger.LogLevel.Error, "RPC request time out! Shutting down.");
                Environment.Exit(-1);
            }

            return null;
        }

        public async Task<JObject> InvokeMethodAsync(string a_sMethod, params object[] a_params)
        {
            try
            {
                var webRequest = (HttpWebRequest) WebRequest.Create(Url + "/" + "json_rpc");
                //webRequest.Credentials = Credentials;

                webRequest.ContentType = "application/json-rpc";
                webRequest.Method = "POST";

                var joe = new JObject();
                joe["jsonrpc"] = "2.0";
                joe["id"] = "test";
                joe["method"] = a_sMethod;

                if (a_params != null)
                    if (a_params.Length > 0)
                        joe.Add(new JProperty("params", a_params[0]));

                var s = JsonConvert.SerializeObject(joe);
                //s=s.Substring(0, s.Length - 1);
                // serialize json for the request
                var byteArray = Encoding.UTF8.GetBytes(s);
                webRequest.ContentLength = byteArray.Length;

                using (var dataStream = webRequest.GetRequestStream())
                {
                    await dataStream.WriteAsync(byteArray, 0, byteArray.Length);
                }

                using (var webResponse = webRequest.GetResponse())
                {
                    using (var str = webResponse.GetResponseStream())
                    {
                        using (var sr = new StreamReader(str))
                        {
                            return JsonConvert.DeserializeObject<JObject>(await sr.ReadToEndAsync());
                        }
                    }
                }
            }
            catch (WebException)
            {
                Logger.Log(Logger.LogLevel.Error, "RPC request time out! Shutting down.");
                Environment.Exit(-1);
            }

            return null;
        }
    }
}