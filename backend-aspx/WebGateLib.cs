﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

/* 
* ==============================================================================================
* 메가펜스 유량제어서비스 Backend Library for ASP.NET / V.21.1.10
* 이 라이브러리는 메가펜스 서비스 계약 및 테스트(POC) 고객에게 제공됩니다.
* 오류조치 및 개선을 목적으로 자유롭게 수정 가능하며 수정된 내용은 반드시 공급처에 통보해야 합니다.
* 허가된 고객 및 환경 이외의 열람, 복사, 배포, 수정, 실행, 테스트 등 일체의 이용을 금합니다.
* 작성자 : ysd@devy.co.kr
* All rights reserved to DEVY / https://devy.kr
* ==============================================================================================
* V.21.1.30 (2021-10-29) 
*   resize default WG_GATE_SERVER_MAX 10 --> 3
* V.21.1.10 (2021-08-08) 
*   WG_TRACE 내용 축소(apiUrl은 Error 시에만 포함)
*   rename cookie WG_VERSION --> WG_VER_BACKEND
*   add ReadCookie()
*   add GATE-ID가 일치하는 경우에만 OUT api call (STEP-2)
* V.21.1.5 (2021-07-31) 
*   change cookie default expire 7 --> 1 days
* V.21.1.5 (2021-07-31) 
*   [minor fix] change api url protocol, http --> https
* ----------------------------------------------------------------------------------------------
* V.21.1.4 (2021-07-29) 
*   [minor fix] missing getparm of IsLoadTest
* V.21.1.3 (2021-07-23) 
*   [minor update] auto make $WG_GATE_SERVERS list
* ----------------------------------------------------------------------------------------------
* V.21.1.1 (2021-06-28) 
*   [minor fix] WG_GetWaitingUi() : html & body style (width 100 --> 100%)
*   [minor fix] WG_GetWaitingUi() : remove whitespace starting html template($html)
*   [fix] WG_GetRandomString() index overflow
* ----------------------------------------------------------------------------------------------
* 2021-10-29 : resize default WG_GATE_SERVER_MAX 10 --> 3
* 2021-04-03 : UI응답부 template fileload 대체
*              server list update
* 2021-03-24 : response.setContentType() 처리 추가
* 2021-01-20 : 부하발생용 parameter 처리
* 	            api call timeout 1초 --> 2초
 	            
* ==============================================================================================
*/

namespace devy.WebGateLib
{
    public class WebGate
    {
        #region property
        const string WG_VERSION = "V.21.1.30";
        public int WG_SERVICE_ID = 0;
        public int WG_GATE_ID = 0;
        const int WG_MAX_TRY_COUNT = 3;     // [fixed] failover api retry count
        public bool WG_IS_CHECKOUT_OK = false; // [fixed] 대기를 완료한 정상 대기표 여부 (true : 대기완료한 정상 대기표, false : 정상대기표 아님)
        const int WG_GATE_SERVER_MAX = 3;    // [fixed] was dns record count
        public List<string> WG_GATE_SERVERS;            // [fixed] 대기표 발급서버 Address List
        public string WG_TOKEN_NO = "";    // 대기표 ID
        public string WG_TOKEN_KEY = "";    // 대기표 key
        public string WG_WAS_IP = "";    // 대기표 발급서버
        public string WG_TRACE = "";    // TRACE 정보 (쿠키응답)
        public string WG_IS_LOADTEST = "N";   // jmeter 등으로 발생시킨 요청인지 여부

        HttpRequest REQ;
        HttpResponse RES;
        #endregion


        #region constructor
        public WebGate(int serviceId, int gateId)
        {
            WG_SERVICE_ID = serviceId;
            WG_GATE_ID = gateId;

            REQ = HttpContext.Current.Request;
            RES = HttpContext.Current.Response;


            /* init gate server list */
            WG_GATE_SERVERS = new List<string>();
            for (var i = 0; i < WG_GATE_SERVER_MAX; i++)
                WG_GATE_SERVERS.Add($"{serviceId}-{i}.devy.kr");
        }

        #endregion

        /// <summary>
        /// 유량제어 체크함수
        /// 고객사 업무페이지에 삽입된 호출 코드에서 대기할지 여부를 판단하기 위해 호출됨
        /// </summary>
        /// <returns>true:WAIT,false:PASS</returns>
        public bool WG_IsNeedToWaiting()
        {
            // init value
            WG_IS_CHECKOUT_OK = false;  // 대기를 완료한 정상 대기표 여부 (true : 대기완료한 정상 대기표, false : 정상대기표 아님)
            WG_TOKEN_NO = "";           // 대기표 ID
            WG_TOKEN_KEY = "";          // 대기표 key
            WG_WAS_IP = "";             // 대기표 발급서버



            /******************************************************************************
            STEP-1 : URL Prameter로 대기표 검증 (CDN Landing 방식을 이용하는 경우에 해당)
            *******************************************************************************/
            try
            {
                WG_TRACE = "STEP1:";

                string parameter = REQ["WG_TOKEN"];
                if (parameter != null && parameter.Length > 0)
                {

                    // WG_TOKEN paramter를 '|'로 분리 및 분리된 개수 체크
                    string[] parameterValues = parameter.Split(',');
                    if (parameterValues.Length == "GATE_ID,TOKEN_NO,TOKEN_KEY,WAS_IP".Split(',').Length)
                    {
                        // WG_TOKEN parameter에 세팅된 값 GET
                        WG_TOKEN_NO = parameterValues[1];
                        WG_TOKEN_KEY = parameterValues[2];
                        WG_WAS_IP = parameterValues[3];

                        if (!string.IsNullOrEmpty(WG_TOKEN_NO) &&
                            !string.IsNullOrEmpty(WG_TOKEN_KEY) &&
                            !string.IsNullOrEmpty(WG_WAS_IP))
                        {
                            // 대기표 Validation(checkout api call)
                            string apiUrl = $"https://{WG_WAS_IP}/?ServiceId={WG_SERVICE_ID}&GateId={WG_GATE_ID}&Action=OUT&TokenNo={WG_TOKEN_NO}&TokenKey={WG_TOKEN_KEY}";
                            string responseText = GetHttpText(apiUrl);
                            if (!string.IsNullOrEmpty(responseText) && responseText.IndexOf("\"ResultCode\":0") >= 0)
                            {
                                WG_IS_CHECKOUT_OK = true;
                                WG_TRACE += $"OK";
                            }else
                            {
                                WG_TRACE += $"{apiUrl}--> FAIL, ";
                            }
                        }
                        else
                        {
                            WG_TRACE += $"SKIP1";
                        }

                    }
                }
                else
                {
                    WG_TRACE += $"SKIP2";
                }
            }
            catch (Exception ex)
            {
                WG_TRACE += $"ERROR:{ex.Message}";
                // ignore & goto next
            }
            /* end of STEP-1 */


            /******************************************************************************
            STEP-2 : Cookie로 대기표 검증 (CDN Landing 방식 이외의 일반적인 방식에 해당)
            *******************************************************************************/
            try
            {
                WG_TRACE += "→STEP2:";

                if (WG_IS_CHECKOUT_OK == false)
                {

                    // 쿠키값을 읽어서 대기완료한 쿠키인지 체크 
                    WG_TOKEN_NO = ReadCookie("WG_TOKEN_NO");
                    WG_TOKEN_KEY = ReadCookie("WG_CLIENT_ID");
                    WG_WAS_IP = ReadCookie("WG_WAS_IP");
                    var cookieGateId = ReadCookie("WG_GATE_ID");

                    if (string.IsNullOrEmpty(WG_TOKEN_KEY))
                    {
                        WG_TOKEN_KEY = WG_GetRandomString(10);
                        WriteCookie("WG_CLIENT_ID", WG_TOKEN_KEY);
                    }

                    if (!string.IsNullOrEmpty(WG_TOKEN_NO) && 
                        !string.IsNullOrEmpty(WG_TOKEN_KEY) && 
                        !string.IsNullOrEmpty(WG_WAS_IP) && 
                        !string.IsNullOrEmpty(cookieGateId))
                    {
                        
                        if($"{WG_GATE_ID}".Equals($"{cookieGateId}"))
                        {
                            // 대기표 Validation(checkout api call)
                            string apiUrl = $"https://{WG_WAS_IP}/?ServiceId={WG_SERVICE_ID}&GateId={WG_GATE_ID}&Action=OUT&TokenNo={WG_TOKEN_NO}&TokenKey={WG_TOKEN_KEY}";
                            string responseText = GetHttpText(apiUrl);

                            if (!string.IsNullOrEmpty(responseText) && responseText.IndexOf("\"ResultCode\":0") >= 0)
                            {
                                WG_TRACE += $"OK";
                                WG_IS_CHECKOUT_OK = true;
                            }
                            else
                            {
                                WG_TRACE += $"FAIL";
                            }
                        }
                        else
                        {
                            WG_TRACE += $"SKIP";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WG_TRACE += $"ERROR:{ex.Message}";
                // ignore & goto next
            }
            /* end of STEP-2 */


            /******************************************************************************
            STEP-3 : 대기표가 정상이 아니면(=체크아웃실패) 신규접속자로 간주하고 대기열 표시여부 판단
                     WG_GATE_SERVERS 서버 중 임의의 서버에 API 호출
            *******************************************************************************/
            WG_TRACE += "→STEP3:";
            var isNeedToWait = false;
            if (WG_IS_CHECKOUT_OK == false)
            {
                var drawResult = new Random().Next(WG_GATE_SERVERS.Count);

                // Fail-over를 위해 최대 3차까지 시도
                for (int i = 0; i < WG_MAX_TRY_COUNT; i++)
                {

                    try
                    {
                        // WG_GATE_SERVERS 서버 중 임의의 서버에 API 호출 --> json 응답

                        // 임의의 대기열 서버 선택하여 대기상태 확인 (대기해야 하는지 web api로 확인)
                        var serverIndex = (drawResult + i) % (WG_GATE_SERVERS.Count);
                        WG_WAS_IP = WG_GATE_SERVERS[serverIndex];
                        String apiUrl = $"https://{WG_WAS_IP}/?ServiceId={WG_SERVICE_ID}&GateId={WG_GATE_ID}&Action=CHECK";

                        // 부하테스트(LoadTest)용으로 호출된 경우 IsLoadTest=Y paramter를 URL에 추가하여 대기열 통계가 정확하게 계산되도록 합니다. (일반적인경우에는 상관없음)
                        if (REQ.QueryString["IsLoadTest"] != null && REQ.QueryString["IsLoadTest"].Equals("Y", StringComparison.OrdinalIgnoreCase))
                        {
                            apiUrl += "&IsLoadTest=Y";
                        }
                        string responseText = GetHttpText(apiUrl);

                        // 현재 대기자가 있으면 응답문자열에 "WAIT"가 포함, 대기자 수가 없으면 "PASS"가 포함됨
                        if (!string.IsNullOrEmpty(responseText))
                        {
                            // 대기자가 있으면 WAIT(대기열 UI 표시) : 응답을 WG_WAITING_FILE로 교체
                            if (responseText.IndexOf("WAIT") >= 0)
                            {
                                isNeedToWait = true;
                                WG_TRACE += $"WAIT";
                                break;
                            }
                            else if (responseText.IndexOf("PASS") >= 0)
                            {
                                isNeedToWait = false;
                                WG_TRACE += $"PASS";
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 오류 시 오류 무시하고 재시도
                        WG_TRACE += $"ERROR:{ex.Message}";
                    }
                }

                // 코드가 여기까지 왔다는 것은
                // 대기열서버응답에 실패 OR 대기자가 없는("PASS") 상태이므로 원래 페이지를 로드합니다.
            }
            /* end of STEP-3 */


            // write cookie for trace
            try
            {
                WriteCookie("WG_VER_BACKEND", WG_VERSION);
                WriteCookie("WG_TIME", DateTime.Now.ToString("o"));
                WriteCookie("WG_TRACE", WG_TRACE);
            }
            catch
            {
                // ignore & goto next
            }


            return isNeedToWait;

        }

        /// <summary>
        /// TokenKey(=DEVICE KEY) 생성
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        private string WG_GetRandomString(int length)
        {
            var characters = "0123456789ABCDEF";
            var randomText = "";

            var rand = new Random();

            for (var i = 0; i < length; i++) {
                randomText += characters[rand.Next(0, characters.Length) % characters.Length];
            }

            return randomText;
        }

        private void WriteCookie(string key, string value, int expireDays = 1)
        {
            RES.Cookies.Add(new HttpCookie(key)
            {
                Value = value,
                Path = "/",
                HttpOnly = false,
                Expires = DateTime.Now.AddDays(expireDays)
            });
        }

        private string ReadCookie(string key)
        {
            if (REQ.Cookies[key] != null)
                return REQ.Cookies[key].Value ?? "";
            else
                return "";
        }


        /// <summary>
        /// 대개UI Template return
        /// </summary>
        public string WG_GetWaitingUi()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta http-equiv='X-UA-Compatible' content='IE=edge'>");
            sb.AppendLine("    <meta charset='utf-8'>");
            sb.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, minimum-scale=1.0, user-scalable=no'>");
            sb.AppendLine("    <title></title>");
            sb.AppendLine("    <link href='//cdn2.devy.kr/WG_SERVICE_ID/css/default.css' rel='stylesheet'>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div id='wg-body-wrapper'></div>");
            sb.AppendLine("    <script type='text/javascript' src='//cdn2.devy.kr/WG_SERVICE_ID/js/webgate.js?v=21.1.0'></script>");
            sb.AppendLine("    <script>");
            sb.AppendLine("        window.addEventListener('load', function () {");
            sb.AppendLine("            WG_StartWebGate('WG_GATE_ID', window.location.href); //reload ");
            sb.AppendLine("        });");
            sb.AppendLine("    </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            sb.Replace("WG_SERVICE_ID", $"{WG_SERVICE_ID}");
            sb.Replace("WG_GATE_ID", $"{WG_GATE_ID}");
 
            return sb.ToString();
        }



        /// <summary>
        /// url에 해당하는 HTTP GET 요청 후 응답을 리턴
        /// </summary>
        /// <param name="url"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static string GetHttpText(string url, int timeout = 2000)
        {
            var request = WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeout;


            var response = request.GetResponse();
            using (var stream = response.GetResponseStream())
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
