using System.Drawing;
using System.Net;
using System.Text;

namespace Miracom.WEBCore.Utils
{
    public static class ReApi
    {
        public static string HttpCallApi(string url, string body, string method = "POST")
        {
            try
            {
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                httpWebRequest.Method = method;
                httpWebRequest.Accept = "text/html";
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Timeout = 600000;
                if (method.ToUpper().Equals("POST"))
                {
                    Encoding uTF = Encoding.UTF8;
                    byte[] bytes = uTF.GetBytes(body);
                    httpWebRequest.ContentLength = bytes.Length;
                    httpWebRequest.GetRequestStream().Write(bytes, 0, bytes.Length);
                }

                HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8);
                return streamReader.ReadToEnd();
            }
            catch (WebException ex)
            {
                return ex.Message;
            }
        }

        public static string Base64ToImage(string dataURL, string fileName = "", string baseDir = "")
        {
            try
            {
                dataURL = dataURL.Replace("data:image/png;base64,", "").Replace("data:image/jgp;base64,", "").Replace("data:image/jpg;base64,", "")
                    .Replace("data:image/jpeg;base64,", "");
                byte[] buffer = Convert.FromBase64String(dataURL);
                MemoryStream stream = new MemoryStream(buffer);
                Image original = Image.FromStream(stream);
                Bitmap bitmap = new Bitmap(original);
                string text = baseDir + "\\download\\images\\";
                if (!Directory.Exists(text))
                {
                    Directory.CreateDirectory(text);
                }

                string text2 = (text + fileName).Replace(".png", ".jpg").Replace(".PNG", ".jpg");
                bitmap.Save(text2);
                return text2;
            }
            catch (Exception innerException)
            {
                throw new Exception("Base64ToImage", innerException);
            }
        }
    }
}
