using IBLL;
using Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.WebSockets;

namespace Hua.WS
{
    /// <summary>
    /// 离线消息
    /// </summary>
    public class MessageInfo
    {
        public MessageInfo(DateTime _MsgTime, ArraySegment<byte> _MsgContent)
        {
            MsgTime = _MsgTime;
            MsgContent = _MsgContent;
        }
        public DateTime MsgTime { get; set; }
        public ArraySegment<byte> MsgContent { get; set; }
    }

    /// </summary>
    public class Handler1 : IHttpHandler
    {
        private IB_h_userinfo h_userinfo = BLLContainer.UnityIOC.Resolve<IB_h_userinfo>();
        private IB_liaotian b_liaotian = BLLContainer.UnityIOC.Resolve<IB_liaotian>();
        private static Dictionary<string, WebSocket> CONNECT_POOL = new Dictionary<string, WebSocket>();//用户连接池
        private static Dictionary<string, List<MessageInfo>> MESSAGE_POOL = new Dictionary<string, List<MessageInfo>>();//离线消息池
        public void ProcessRequest(HttpContext context)
        {
            //context.Response.ContentType = "text/plain";
            //context.Response.Write("Hello World");
            if (context.IsWebSocketRequest)
            {
                context.AcceptWebSocketRequest(ProcessChat);
            }
        }

        //添加用户信息，用做后期统计
        private void AddUser(string user, string address)
        {

            h_userinfo userinfo = h_userinfo.GetModel(p => p.username.Equals(user));
            if (userinfo != null)
            {
                //是否获取到定位地址
                if (address != null && userinfo.Address != address.Replace(',', ' '))
                {
                    string[] adds = address.Split(',');
                    userinfo.Province = adds[0];//省
                    userinfo.City = adds[1];//所在市
                    userinfo.Address = address.Replace(',',' ');//详情地址
                }
                userinfo.LastLandTime = DateTime.Now;//最后登录时间
                h_userinfo.Update(userinfo);//更新用户数据
                return;
            }
            else
            {
                h_userinfo m_UserDb = new h_userinfo();
                m_UserDb.username = user;
                m_UserDb.pwd = Tools.DESEncrypt.GetMd5("123456", 32).ToUpper();
                m_UserDb.NickName = "";
                m_UserDb.LastLandTime = DateTime.Now;
                m_UserDb.RegTime = DateTime.Now;
                m_UserDb.IsActivation = 0;
                m_UserDb.email = "";
                m_UserDb.RegType = 2;
                m_UserDb.Address = address;

                int regOk = h_userinfo.CompatibleUser_Add(m_UserDb);//添加一条用户数据
            }
        }

        private async Task ProcessChat(AspNetWebSocketContext context)
        {
            WebSocket socket = context.WebSocket;
            string user = context.QueryString["user"].ToString();//用户名，ip地址
            var ToUser = context.QueryString["ToUser"];//目的用户
            var type = context.QueryString["type"];//类型 1管理员
            var address = context.QueryString["address"];//用户地址

            try
            {
                //将用户信息添加到数据库
                AddUser(user, address);
                #region 用户添加连接池
                //第一次open时，添加到连接池中
                if (!CONNECT_POOL.ContainsKey(user))
                    CONNECT_POOL.Add(user, socket);//不存在，添加
                else
                    if (socket != CONNECT_POOL[user])//当前对象不一致，更新
                        CONNECT_POOL[user] = socket;
                #endregion

                #region 离线消息处理
                if (MESSAGE_POOL.ContainsKey(user))
                {
                    List<MessageInfo> msgs = MESSAGE_POOL[user];
                    foreach (MessageInfo item in msgs)
                    {
                        await socket.SendAsync(item.MsgContent, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    MESSAGE_POOL.Remove(user);//移除离线消息
                }
                else
                {
                    if (user!="18313364090" || (ToUser != null && type == null))
                    {
                        string message = "您好！我可以为您做点什么？(你可以对我说“讲个笑话”，“天气”等)";
                        SendMessage(user, message);//自动回复
                    }
                }
                #endregion

                string descUser = string.Empty;//目的用户
                bool isRenGong = false;
                while (true)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[1024*8*10]);
                        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);//接收发送过来的信息

                        #region 消息处理（字符截取、消息转发）
                        try
                        {
                            #region 关闭Socket处理，删除连接池
                            if (socket.State != WebSocketState.Open)//连接关闭
                            {
                                if (CONNECT_POOL.ContainsKey(user)) CONNECT_POOL.Remove(user);//删除连接池
                                break;
                            }
                            #endregion

                            string userMsg = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);//发送过来的消息
                            string[] msgList = userMsg.Split('|');
                            if (msgList.Length == 2)
                            {
                                if(address == null)
                                if (msgList[0].Trim().Length > 0)
                                    descUser = msgList[0].Trim();//记录消息目的用户
                                string message = msgList[1];
                                MessageModel mes = new MessageModel();
                                mes.content = message;
                                mes.username = user;
                                mes.datetime = DateTime.Now.ToString("MM-dd HH:mm:ss");
                                mes.address = address;
                                string obj = JsonConvert.SerializeObject(mes);
                                buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(obj));

                                if (message.Trim() == "呼叫主人" || isRenGong || (type != null && type.ToZMInt32() == 1))//判断是否真人回复，（客户端发送“呼叫主人”或者type=1时，信息会发送给目的用户）
                                {
                                    isRenGong = true;
                                    if (CONNECT_POOL.ContainsKey(descUser))//判断客户端是否在线
                                    {
                                        WebSocket destSocket = CONNECT_POOL[descUser];//目的客户端
                                        if (destSocket != null && destSocket.State == WebSocketState.Open)
                                            await destSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                    else
                                    {
                                        await Task.Run(() =>
                                        {
                                            if (!MESSAGE_POOL.ContainsKey(descUser))//将用户添加至离线消息池中
                                                MESSAGE_POOL.Add(descUser, new List<MessageInfo>());
                                            MESSAGE_POOL[descUser].Add(new MessageInfo(DateTime.Now, buffer));//添加离线消息
                                        });
                                    }
                                    if (message.Trim() == "呼叫主人")
                                    {
                                        message = "主人正在赶来的路上~~~~";
                                        SendMessage(user, message);//自动回复
                                    }
                                }
                                else if (message.Contains("笑话"))//信息中包含“笑话”表示回复笑话
                                {
                                    string con = h_userinfo.SearchValue("select content from lengxiaohua where id=" + new Random().Next(1, 1000)).ToString();
                                    SendMessage(user, con);//自动回复
                                }
                                else if (message.Contains("天气"))
                                {
                                    object city_code = null;
                                    message = message.Replace("市", "").Replace("县", "").Replace("今天", "").Replace("的", "").Replace("天气", "").Trim();
                                    if (message.Replace("明天", "") != "" || message.Replace("后天", "") != "")
                                    {
                                        string tianqi = "";
                                        if(message.Contains("明天")){
                                            tianqi += "%mingtiantianqi";
                                            message = message.Replace("明天", "");
                                        }
                                        if (message.Contains("后天"))
                                        {
                                            tianqi += "%houtiantianqi";
                                            message = message.Replace("后天", "");
                                        }
                                        if (message.Length > 2)
                                        {
                                            message = message.Substring(0, 2);
                                        }
                                        city_code = h_userinfo.SearchValue("select city_code from city_code where area like '%" + message + "%' or city like '%" + message + "%'");
                                        if (city_code != null)
                                            tianqi += Tools.HttpHelper.submitData("", "http://t.weather.sojson.com/api/weather/city/" + city_code + "", "json", "get");
                                        if (tianqi!="")
                                            SendMessage(user, tianqi + "%tianqi");//自动回复
                                    }
                                    if (address != null && city_code == null)
                                    {
                                        string tianqi = "";
                                        if (message.Contains("明天"))
                                        {
                                            tianqi += "%mingtiantianqi";
                                        }
                                        if (message.Contains("后天"))
                                        {
                                            tianqi += "%houtiantianqi";
                                        }
                                        string[] adds = address.Split(',');
                                        string city = adds[1].Replace("市", "");
                                        if (city.Length > 4)
                                        {
                                            city = city.Substring(0, 2);
                                        }
                                        string area = adds[2].Substring(0, 2);
                                        city_code = h_userinfo.SearchValue("select city_code from city_code where area like '%" + area + "%' and city like '%" + city + "%'");
                                        if (city_code == null)
                                        {
                                            city_code = h_userinfo.SearchValue("select city_code from city_code where city like '%" + city + "%'");
                                        }
                                        if (city_code != null)
                                        {
                                            tianqi += Tools.HttpHelper.submitData("", "http://t.weather.sojson.com/api/weather/city/" + city_code + "", "json", "get");
                                        }

                                        SendMessage(user, tianqi + "%tianqi");//自动回复
                                    }
                                    if (address == null && city_code == null)
                                    {
                                        SendMessage(user, "%tianqi");//自动回复
                                    }
                                    
                                }
                                else
                                {
                                    AutomaticReply(user, message);//智能回复
                                }

                            }
                            else//不存在目的用户时默认群发
                            {
                                MessageModel mes = new MessageModel();
                                mes.content = userMsg;
                                mes.username = user;
                                mes.datetime = DateTime.Now.ToString("MM-dd HH:mm:ss");
                                mes.address = address;
                                string obj = JsonConvert.SerializeObject(mes);
                                buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(obj));

                                //foreach (KeyValuePair<string, WebSocket> item in CONNECT_POOL)
                                //{
                                //    if (item.Value != CONNECT_POOL[user])
                                //        await item.Value.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                //}

                                List<h_userinfo> users = h_userinfo.GetModels(p => p.RegType == 2).ToList();
                                foreach (h_userinfo userinfo in users)
                                {
                                    if (CONNECT_POOL.ContainsKey(userinfo.username))//判断客户端是否在线
                                    {
                                        WebSocket destSocket = CONNECT_POOL[userinfo.username];//目的客户端
                                        if (destSocket != null && destSocket.State == WebSocketState.Open && destSocket != CONNECT_POOL[user])
                                            await destSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                    else
                                    {
                                        await Task.Run(() =>
                                        {
                                            if (!MESSAGE_POOL.ContainsKey(userinfo.username))//将用户添加至离线消息池中
                                                MESSAGE_POOL.Add(userinfo.username, new List<MessageInfo>());
                                            MESSAGE_POOL[userinfo.username].Add(new MessageInfo(DateTime.Now, buffer));//添加离线消息
                                        });
                                    }
                                }
                            }

                        }
                        catch (Exception exs)
                        {
                            //消息转发异常处理，本次消息忽略 继续监听接下来的消息
                        }
                        #endregion
                    }
                    else
                    {
                        break;
                    }
                }//while end
            }
            catch (Exception ex)
            {
                //整体异常处理
                if (CONNECT_POOL.ContainsKey(user)) CONNECT_POOL.Remove(user);
            }
        }

        private void AutomaticReply(string user, string message)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[1024*8*10]);
            WebSocket destSocket = null;
            
            MessageModel mes = new MessageModel();
            var con = h_userinfo.SearchValue("select answer from liaotian where question like '" + message + "'");
            if (con == null)
            {
                con = h_userinfo.SearchValue("select answer from liaotian where question like '" + message + "%'");
            }
            if (con == null)
            {
                con = h_userinfo.SearchValue("select answer from liaotian where question like '%" + message + "%'");
            }

            if (con != null)
            {
                string content = con.ToString();
                content = content.Replace("[cqname]", "笑笑").Replace("[name]", "你");
                mes.content = content;
            }
            else
            {
                mes.content = "笑笑同学还是个孩子，听不懂你在说什么，让我的主人来帮你解答，你可以对我说“<span style='font-weight: bold;'>呼叫主人<span>”";
            }
            mes.username = "笑笑机器人";
            mes.datetime = DateTime.Now.ToString("MM-dd HH:mm:ss");
            string obj = JsonConvert.SerializeObject(mes);
            buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(obj));
            destSocket = CONNECT_POOL[user];//目的客户端
            if (destSocket != null && destSocket.State == WebSocketState.Open)
                destSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        private void SendMessage(string user, string message)
        {
            MessageModel mes = new MessageModel();
            mes.content = message;
            mes.username = "笑笑机器人";
            mes.datetime = DateTime.Now.ToString("MM-dd HH:mm:ss");
            string obj = JsonConvert.SerializeObject(mes);
            ArraySegment<byte> buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(obj));
            WebSocket destSocket = CONNECT_POOL[user];//目的客户端
            if (destSocket != null && destSocket.State == WebSocketState.Open)
                destSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }

        public class MessageModel
        {
            public string username { get; set; }
            public string datetime { get; set; }
            public string content { get; set; }
            public string address { get; set; }
        }
    }
}