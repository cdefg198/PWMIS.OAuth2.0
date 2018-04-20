﻿/*
 * 本文代码来自网上，请参考 
 * http://blog.csdn.net/sqqyq/article/details/50920261
 * https://docs.microsoft.com/en-us/aspnet/web-api/overview/advanced/http-message-handlers
 * 有关 HttpClient 的使用注意事项，请参考链接： 
 */


using PWMIS.OAuth2.Tools;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PWMIS.OAuth2.Tools
{
    /// <summary>  
    /// HTTP代理请求消息拦截器  
    /// </summary>  
    public class ProxyRequestHandler : DelegatingHandler
    {
        ProxyConfig _config;
        private static object sync_obj = new object();
        //多个不同站点用同一个httpClient会出问题，待解决
        //private static readonly HttpClient _httpClient;
        private static Dictionary<string, HttpClient> dictHttpClient;
        private static CookieContainer cc = new CookieContainer();

        static ProxyRequestHandler()
        {
            var sp = ServicePointManager.FindServicePoint(new Uri("http://foo.bar"));
            sp.ConnectionLeaseTimeout = 60 * 1000; // 1 分钟

            dictHttpClient = new Dictionary<string, HttpClient>();
        }

        private HttpClient GetHttpClient(Uri baseAddress, HttpRequestMessage request)
        {
            //注意：应该每个浏览器客户端一个HttpClient 实例，这样才可以保证各自的会话不冲突

            //string key = baseAddress.ToString();
            //if (dictHttpClient.ContainsKey(key))
            //{
            //    return dictHttpClient[key];
            //}
            //else
            //{
            //    lock (sync_obj)
            //    {
            //        if (dictHttpClient.ContainsKey(key))
            //        {
            //            return dictHttpClient[key];
            //        }
            //        else
            //        {

            CookieContainer cc = new CookieContainer();
            HttpClientHandler httpClientHandler = new HttpClientHandler();
            httpClientHandler.CookieContainer = cc;
            httpClientHandler.UseCookies = true;

            HttpClient client = new HttpClient(httpClientHandler);
            client.Timeout = new TimeSpan(0, 0, 30); //不易太长，Token只有10秒有效
            //client.DefaultRequestHeaders.Connection.Add("keep-alive");
            

            client.BaseAddress = baseAddress;
          
            //复制Cookies
            var cookies = request.Headers.GetCookies();
            foreach (var c in cookies)
            {
                foreach (var item in c.Cookies)
                {
                    Cookie cookie1 = new Cookie(item.Name, item.Value);
                    cookie1.Domain = baseAddress.Host;
                    cc.Add(cookie1);
                }
            }

            //复制请求头，转发请求
            foreach (var item in request.Headers)
            {
                client.DefaultRequestHeaders.Add(item.Key, item.Value);
            }

            client.DefaultRequestHeaders.Add("Proxy-Server", this.Config.ServerName);
            client.DefaultRequestHeaders.Host = baseAddress.Host;

           

            //dictHttpClient.Add(key, client);

            return client;
            //        }
            //    }
            //}
        }
        /// <summary>
        /// 获取或者设置代理服务配置
        /// </summary>
        public ProxyConfig Config {
            get 
            {
                if (_config == null)
                {
                   string filePath=  HttpContext.Current.Server.MapPath("/ProxyServer.config");
                   if (!System.IO.File.Exists(filePath))
                       throw new Exception("当前站点根目录下没有发现代理配置文件：ProxyServer.config");
                    //每行 # 开头，表示注释内容，忽略
                    string[] configArr = System.IO.File.ReadAllLines(filePath);
                    string[] configArr1= configArr.Where(p => !p.TrimStart(' ', '\t').StartsWith("#")).ToArray();
                    string configStr = string.Concat(configArr1);
                    //string configStr = System.IO.File.ReadAllText(filePath);
                   _config = Newtonsoft.Json.JsonConvert.DeserializeObject<ProxyConfig>(configStr);
                }
                return _config;
            }
            set { _config = value; }
        }

      

        /// <summary>  
        /// 拦截请求  
        /// </summary>  
        /// <param name="request">请求</param>  
        /// <param name="cancellationToken">用于发送取消操作信号</param>  
        /// <returns></returns>  
        protected async override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //获取URL参数  
            //NameValueCollection query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            //获取Post正文数据，比如json文本  
            //string fRequesContent = request.Content.ReadAsStringAsync().Result;

            //可以做一些其他安全验证工作，比如Token验证，签名验证。  
            //可以在需要时自定义HTTP响应消息  
            //return SendError("自定义的HTTP响应消息", HttpStatusCode.OK);  

            ////请求处理耗时跟踪  
            //Stopwatch sw = new Stopwatch();
            //sw.Start();
            ////调用内部处理接口，并获取HTTP响应消息  
            ////HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            ////篡改HTTP响应消息正文  
            ////response.Content = new StringContent(response.Content.ReadAsStringAsync().Result.Replace(@"\\", @"\"));
            //HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            //response.Content = new StringContent("被拦截了，哈哈");
            //sw.Stop();
            ////记录处理耗时  
            //long exeMs = sw.ElapsedMilliseconds;

            //代理服务
          

            bool matched = false;
            string url = request.RequestUri.PathAndQuery;
            Uri baseAddress=null;
            //处理代理规则
            foreach (var route in this.Config.RouteMaps)
            { 
                if (url.StartsWith(route.Prefix))
                {
                    baseAddress = new Uri("http://" + route.Host + "/");
                    if (!string.IsNullOrEmpty(route.Match))
                    {
                        if (route.Map == null) route.Map = "";
                        url = url.Replace(route.Match, route.Map);
                    }
                    matched = true;
                    //break;
                    //只要不替换前缀，还可以继续匹配并且替换剩余部分
                }
            }
            //未匹配到代理，返回本机请求响应结果
            if (!matched)
            {
                return await base.SendAsync(request, cancellationToken);
            }
            //处理代理URL地址中的服务器变量，变量名使用[]中括号表示：
            //注意：在这里无法使用HttpContext.Current.Session，所以下面的方法出错
            //url = url.Replace("[SessionID]", HttpContext.Current.Session.SessionID);

            //如果缓存没有，将继续处理
            if (request.Headers.CacheControl!=null &&
                request.Headers.CacheControl.Public &&
                this.Config.EnableCache && ProxyCacheProcess != null)
            {
                var response = ProxyCacheProcess(this,request);
                if (response == null)
                {
                    response = await GetNewResponseMessage(request, url, baseAddress);
                    SetRequestCache(url, response);
                }
                return response;
            }
            return await GetNewResponseMessage(request, url, baseAddress);
        }

        #region 缓存相关

        /*
         * 缓存相关资料：
         * Http头介绍:Expires,Cache-Control,Last-Modified,ETag http://www.51testing.com/html/28/116228-238337.html
         * 带缓存的HTTP代理服务器（五） http://blog.csdn.net/sakeven/article/details/37611967
         * 缓存 HTTP POST请求和响应  http://www.oschina.net/question/82993_74342
         */

        /// <summary>
        /// 代理服务器缓存处理程序，如果代理配置设置了允许使用缓存，那么将调用此方法。
        /// 该方法将根据HttpRequestMessage 决定如何使用缓存，如果缓存已经过期，返回空响应
        /// </summary>
        public Func<ProxyRequestHandler, HttpRequestMessage, HttpResponseMessage> ProxyCacheProcess;

        public void SetRequestCache(string url, HttpResponseMessage value)
        {
            throw new NotSupportedException("当前版本不支持代理缓存");
        }

        public HttpResponseMessage GetRequestCache(string url)
        {
            throw new NotSupportedException("当前版本不支持代理缓存");
        }

        #endregion


        /// <summary>
        /// 请求目标服务器，获取响应结果
        /// </summary>
        /// <param name="request"></param>
        /// <param name="url"></param>
        /// <param name="baseAddress"></param>
        /// <returns></returns>
        private async Task<HttpResponseMessage> GetNewResponseMessage(HttpRequestMessage request, string url, Uri baseAddress)
        {
            HttpResponseMessage result = null;
            HttpClient client = GetHttpClient(baseAddress, request);

            /*
             * 以下设置已经在GetHttpClient 处理过，不可重复设置，否则异常
            client.BaseAddress = baseAddress;
            //复制请求头，转发请求
            foreach (var item in request.Headers)
            {
                client.DefaultRequestHeaders.Add(item.Key, item.Value);
            }
            client.DefaultRequestHeaders.Add("Proxy-Server", this.Config.ServerName);
            client.DefaultRequestHeaders.Host =  baseAddress.Host;
            */

            var identity = HttpContext.Current.User.Identity;
            if (identity == null || identity.IsAuthenticated == false)
            {
                return await ProxyReuqest(request, url, result, client);
            }

            //处理代理的服务器变量：
            //url = url.Replace("[UserName]", identity.Name);

            using (TokenManager tm = new TokenManager(identity.Name, null))
            {
                TokenResponse token = tm.TakeToken();
                //存在客户端登录，但是服务器重启会话丢失的情况，这时候将无法取到令牌，
                //这种情况下视为客户未登录，由资源服务器来决定该访问是否需要验证授权
                //所以代理服务不直接抛出错误请求。
                if (token == null)
                {
                    if (this.Config.EnableRequestLog)
                    {
                        string logTxt = string.Format("Begin Time:{0} ,\r\n  Request-Url:{1} {2} ,\r\n  Map-Url:{3} {4} ,\r\n  Old-Token:{5}\r\n  Statue:{6} \r\n  ExctionMessage:{7}\r\n",
                            DateTime.Now.ToLongTimeString(),
                            request.Method.ToString(), request.RequestUri.ToString(),
                            client.BaseAddress.ToString(), url,
                            tm.OldToken==null? "[OldToken=null]" : tm.OldToken.AccessToken,
                            "TokenGainFailure",
                            tm.TokenExctionMessage
                            );

                        WriteLogFile(logTxt);
                    }
                    if(tm.TokenExctionMessage== "UserNoToken")
                        return await ProxyReuqest(request, url, result, client);
                    else
                        return SendError("代理请求刷新令牌失败：" + tm.TokenExctionMessage, HttpStatusCode.BadRequest);
                }
                else
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    return await ProxyReuqest(request, url, result, client);
                }
            }
            
        }

        private async Task<HttpResponseMessage> ProxyReuqest(HttpRequestMessage request, string url, HttpResponseMessage result, HttpClient client)
        {
            string allLogText = "";
            if (this.Config.EnableRequestLog)
            {
                string token = client.DefaultRequestHeaders.Authorization == null ? "" : client.DefaultRequestHeaders.Authorization.ToString();
                string logTxt = string.Format("Begin Time:{0} ,\r\n  Request-Url:{1} {2} ,\r\n  Map-Url:{3} {4} ,\r\n  Token:{5}\r\n  ",
                    DateTime.Now.ToLongTimeString(),
                    request.Method.ToString(), request.RequestUri.ToString(),
                    client.BaseAddress.ToString(), url,
                    token
                    );

                //WriteLogFile(logTxt);
                allLogText = logTxt;
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();
            if (request.Method == HttpMethod.Get)
            {
                result = await client.GetAsync(url);
            }
            else if (request.Method == HttpMethod.Post)
            {
                result = await client.PostAsync(url, request.Content);
            }
            else if (request.Method == HttpMethod.Put)
            {
                result = await client.PutAsync(url, request.Content);
            }
            else if (request.Method == HttpMethod.Delete)
            {
                result = await client.DeleteAsync(url);
            }
            else
            {
                result = SendError("PWMIS ASP.NET Proxy 不支持这种 Method:" + request.Method.ToString(), HttpStatusCode.BadRequest);
            }
            sw.Stop();
            result.Headers.Add("Proxy-Server", this.Config.ServerName);

            if (this.Config.EnableRequestLog)
            {
              
                string logTxt = string.Format("End Time:{0} ,\r\n  Statue:{1} ,\r\n  Elapsed(ms):{2} \r\n",
                    DateTime.Now.ToLongTimeString(),
                    //request.Method.ToString(), request.RequestUri.ToString(),
                    //client.BaseAddress.ToString(), url,
                    result.StatusCode.ToString(),
                    sw.Elapsed.TotalMilliseconds
                    );
                if (result.StatusCode != HttpStatusCode.OK)
                {
                    logTxt += "\r\n Error Text:" + result.Content.ReadAsStringAsync().Result;
                    logTxt += "\r\n Request Headers:" + client.DefaultRequestHeaders.ToString() + "---------End Error Messages-----------\r\n";

                }
                allLogText += logTxt;
                WriteLogFile(allLogText);
            }

            //
            if (string.IsNullOrEmpty(this.Config.OAuthRedirUrl))
            {
                return result;
            }
            else
            {
                if (result.StatusCode == HttpStatusCode.Unauthorized && this.Config.UnauthorizedRedir)
                {
                    //如果未登录，禁止访问API，跳转到相应的页面
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new Uri(this.Config.OAuthRedirUrl);
                    return response;
                }
                else
                {
                    return result;
                }
            }
        }

        private void WriteLogFile(string logTxt)
        {
            string fileName = string.Format("ProxyLog_{0}.txt", DateTime.Now.ToString("yyyy-MM-dd"));
            string filePath = System.IO.Path.Combine(this.Config.LogFilePath, fileName);
            try
            {
                if (!System.IO.Directory.Exists(this.Config.LogFilePath))
                    System.IO.Directory.CreateDirectory(this.Config.LogFilePath);
                System.IO.File.AppendAllText(filePath, logTxt);
            }
            catch
            {

            }
        }

        /// <summary>  
        /// 构造自定义HTTP响应消息  
        /// </summary>  
        /// <param name="error"></param>  
        /// <param name="code"></param>  
        /// <returns></returns>  
        private HttpResponseMessage SendError(string error, HttpStatusCode code)
        {
            var response = new HttpResponseMessage();
            response.Content = new StringContent(error);
            response.StatusCode = code;
            return response;
        }
    }  
}