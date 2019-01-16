using IBLL;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Tools;

namespace Hua.Controllers
{
    public class WSController : Controller
    {
        private IB_h_userinfo b_h_userinfo = BLLContainer.UnityIOC.Resolve<IB_h_userinfo>();

        // GET: WS
        public ActionResult Index(string name,int? type,string username,string address)
        {
            if (name != null)
            {
                string ip = ToolsZMDes.ZMDesEncrypt.overDecrypt(name);
                ViewBag.Name = ip;
            }
            if (type != null)
            {
                ViewBag.Type = type;
            }
            if (username != null)
            {
                username = ToolsZMDes.ZMDesEncrypt.overDecrypt(username);
                ViewBag.UserName = username;
            }
            if (address != null)
            {
                ViewBag.Address = address;
            }
            return View();
        }

        public void kf()
        {
            string ip = ToolsZMDes.ZMDesEncrypt.overEncrypt("18313364090");
            Response.Redirect("/ws?name=" + ip);
        }

        public void poor()
        {
            string ip = ToolsZMDes.ZMDesEncrypt.overEncrypt("18313364090");
            Response.Redirect("/ws?username=" + ip);
        }

        [HttpPost]
        public string UploadImg(byte[] hpf)
        {
            int isOK = 0;
            string filesUrl = "";
            string errMsg;
            //只能上传图片
            var extArray = new string[] { ".gif", ".jpg", ".jpeg", ".png", ".bmp", ".pdf" };
            try
            {
                //静态方法根据文件流判断文件类型。
                //string fileType = images.Substring(11, 3);
                //images = images.Substring(22);
                string fileType = IsAllowedExtension(hpf);
                //byte[] hpf = System.Text.Encoding.Default.GetBytes(images);
                if (hpf.Length >= 0 && !string.IsNullOrEmpty(fileType))
                {
                    if (extArray.Contains(fileType, StringComparer.CurrentCultureIgnoreCase))
                    {
                        var length = hpf.Length;
                        if (length <= 10 * 1024 * 1024)
                        {
                            FastDfsHelper dfsFile = new FastDfsHelper("claim");
                            string imgUrl = string.Empty;
                            Stream file = new MemoryStream(hpf);
                            string newfileName = string.Empty;
                            string litName = string.Empty;
                            using (Stream imageStream = file)
                            {
                                string path = "C:/hua/imgSite/hua/websocket/";
                                string host = "http://img.lwpoor.cn/hua/websocket/";

                                string imagename = Guid.NewGuid().ToString();

                                string imageGuid = imagename + fileType;
                                if (!Directory.Exists(path))
                                    Directory.CreateDirectory(path);
                                Bitmap bitmap = new Bitmap(imageStream);
                                bitmap.Save(path + imageGuid);
                                imgUrl = host + imageGuid;

                            };
                            isOK = 1;
                            filesUrl = imgUrl;
                            errMsg = "文件上传成功！";
                        }
                        else
                        {
                            isOK = 2;//文件大小不能超过10M
                            errMsg = "您选择的文件大小不能超过 10 MB！";
                        }
                    }
                    else
                    {
                        isOK = 3;
                        errMsg = string.Format("上传文件格式必须是：{0} 其中的一种！ 异常类型为{1}", extArray.Join("|"), fileType);
                    }
                }
                else
                {
                    isOK = 4;
                    errMsg = "上传文件为空！长度为:" + hpf.Length + "。或类型异常：" + fileType.ToString();
                }
            }
            catch (Exception ex)
            {
                isOK = 0;
                errMsg = "上传失败" + ex.ToString();
            }

            var returnMsg = new
            {
                isOK = isOK,
                filesUrl = filesUrl,
                errMsg = errMsg,
            };
            return JsonConvert.SerializeObject(returnMsg);
        }

        #region 图片流判断类型
        public static string IsAllowedExtension(byte[] fu)
        {
            int fileLen = fu.Length;
            byte[] imgArray = new byte[fileLen];
            MemoryStream ms = new MemoryStream(fu);
            System.IO.BinaryReader br = new BinaryReader(ms);
            string fileclass = "";
            byte buffer;
            try
            {
                buffer = br.ReadByte();
                fileclass = buffer.ToString();
                buffer = br.ReadByte();
                fileclass += buffer.ToString();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            br.Close();
            ms.Close();

            string type = ReturnImageType(Int32.Parse(fileclass));

            return type;
        }

        public static string ReturnImageType(Int32 i)
        {
            string type;
            switch (i)
            {
                case 255216:
                    return type = ".jpg";
                case 7173:
                    return type = ".gif";
                case 6677:
                    return type = ".bmp";
                case 13780:
                    return type = ".png";
                case 3780:
                    return type = ".pdf";
                default:
                    return type = "";
            }
        }

        #endregion


        public int ReadLineFile()
        {
            int res = 0;
            string filePath = @"C:\hua\file\chat.txt";

            FileStream fileStream = null;

            StreamReader streamReader = null;

            try
            {
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

                streamReader = new StreamReader(fileStream, Encoding.UTF8);

                fileStream.Seek(0, SeekOrigin.Begin);

                string content = null;
                string question = null;
                string answer = null;

                while (content == null || content == "")
                {
                    question = streamReader.ReadLine();
                    if (question == null || question == "")
                        question = streamReader.ReadLine();
                    
                    answer = streamReader.ReadLine();

                    content = streamReader.ReadLine().Trim();
                    if (content != null && content != "")
                        content = streamReader.ReadLine();
                    if (content != null && content != "")
                        content = streamReader.ReadLine().Trim();

                    if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(question) && !string.IsNullOrEmpty(answer))
                        res = b_h_userinfo.UpdateBySql("INSERT INTO liaotian (question,answer) VALUES ('" + question + "','" + answer + "')");
                }
            }
            catch
            {
            }
            finally
            {
                if (fileStream != null)
                {
                    fileStream.Close();
                }
                if (streamReader != null)
                {
                    streamReader.Close();
                }
            }
            return res;
        }

        public string AutomaticReply(string message)
        {
            var con = b_h_userinfo.SearchValue("select answer from liaotian where question like '%" + message + "%'");
            return con.ToString();
        }


        public string testWeather(string address)
        {
            string[] adds = address.Split(',');
            string city = adds[1].Replace("市", "");
            if (city.Length > 4)
            {
                city = city.Substring(0, 2);
            }
            string area = adds[2].Substring(0, 2);
            var con = b_h_userinfo.SearchValue("select city_code from city_code where area like '%" + area + "%' and city like '%" + city + "%'");
            if (con == null)
            {
                con = b_h_userinfo.SearchValue("select city_code from city_code where city like '%" + city + "%'");
            }
            if (con != null)
            {

            }

            string tianqi = Tools.HttpHelper.submitData("", "http://www.weather.com.cn/data/sk/" + con + ".html", "json", "get");

            return tianqi;
        }

    }
}