﻿/*
 * By David Barrett, Microsoft Ltd. 2012. Use at your own risk.  No warranties are given.
 * 
 * DISCLAIMER:
 * THIS CODE IS SAMPLE CODE. THESE SAMPLES ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND.
 * MICROSOFT FURTHER DISCLAIMS ALL IMPLIED WARRANTIES INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OF MERCHANTABILITY OR OF FITNESS FOR
 * A PARTICULAR PURPOSE. THE ENTIRE RISK ARISING OUT OF THE USE OR PERFORMANCE OF THE SAMPLES REMAINS WITH YOU. IN NO EVENT SHALL
 * MICROSOFT OR ITS SUPPLIERS BE LIABLE FOR ANY DAMAGES WHATSOEVER (INCLUDING, WITHOUT LIMITATION, DAMAGES FOR LOSS OF BUSINESS PROFITS,
 * BUSINESS INTERRUPTION, LOSS OF BUSINESS INFORMATION, OR OTHER PECUNIARY LOSS) ARISING OUT OF THE USE OF OR INABILITY TO USE THE
 * SAMPLES, EVEN IF MICROSOFT HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES. BECAUSE SOME STATES DO NOT ALLOW THE EXCLUSION OR LIMITATION
 * OF LIABILITY FOR CONSEQUENTIAL OR INCIDENTAL DAMAGES, THE ABOVE LIMITATION MAY NOT APPLY TO YOU.
 * */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.DirectoryServices;
using System.Globalization;
using SOAPe.DNSTools;

namespace SOAPe.EWSTools
{
    class ClassEWSAutodiscover
    {
        // This class performs autodiscovery in the same way as the Remote Connectivity Analyser (as far as I can work out anyway!)

        private static ListBox _logListBox = null;
        private static string _logData = "";
        private static bool _ignoreCertificateErrors = false;
        private static bool _lastUrlHadValidCertificate = false;
        private static DateTime _startTime;

        private string _SMTPAddress = "";
        private string _AutodiscoverXML = "";
        private XmlDocument _AutodiscoverXMLDoc = null;
        private ICredentials _credentials = null;
        private string _AutodiscoveXMLRequest = "";
        private string _AutodiscoverSOAPRequest = "";
        private List<string> _testedUrls = new List<string>();
        private ClassLogger _logger = null;

        public ClassEWSAutodiscover()
        {
            ClassTemplateReader oTemplateReader = new ClassTemplateReader();
            _AutodiscoveXMLRequest = oTemplateReader.ReadXMLTemplate("EWSAutodiscoverRequest", "EWSTools");
            _AutodiscoverSOAPRequest = oTemplateReader.ReadXMLTemplate("EWSAutodiscoverSOAPRequest", "EWSTools");
        }

        public ClassEWSAutodiscover(string SMTPAddress)
            : this()
        {
            _SMTPAddress = SMTPAddress;
        }

        public ClassEWSAutodiscover(string SMTPAddress, ICredentials Credentials)
            : this(SMTPAddress)
        {
            _credentials = Credentials;
        }

        public ClassEWSAutodiscover(string SMTPAddress, ListBox LogListBox)
            : this(SMTPAddress)
        {
            _logListBox = LogListBox;
        }

        public ClassEWSAutodiscover(string SMTPAddress, ICredentials Credentials, ListBox LogListBox)
            : this(SMTPAddress, Credentials)
        {
            _logListBox = LogListBox;
        }

        public ClassEWSAutodiscover(string SMTPAddress, ICredentials Credentials, ListBox LogListBox, ClassLogger Logger)
            : this(SMTPAddress, Credentials, LogListBox)
        {
            _logger = Logger;
        }

        public bool IgnoreCertificateErrors
        {
            get
            {
                return _ignoreCertificateErrors;
            }
            set
            {
                _ignoreCertificateErrors = value;
            }
        }

        public string AutoDiscoverXML
        {
            get { return _AutodiscoverXML; }
        }

        public string EWSUrl
        {
            get
            {
                string sEwsUrl = ReadElement("EwsUrl");
                if (String.IsNullOrEmpty(sEwsUrl))
                {
                    sEwsUrl = ReadUserSetting("ExternalEwsUrl");
                    if (String.IsNullOrEmpty(sEwsUrl))
                        sEwsUrl = ReadUserSetting("InternalEwsUrl");
                }
                return sEwsUrl;
            }
        }

        private static void Log(string Details)
        {
            TimeSpan elapsedTime = DateTime.Now.Subtract(_startTime);
            //Details = String.Format("{0,8:hh:mm:ss}", elapsedTime) + " " + Details;
            Details = elapsedTime.Hours.ToString("00") + ":" + elapsedTime.Minutes.ToString("00") + ":" + elapsedTime.Seconds.ToString("00") + " - " + Details;
            _logData = _logData + Details + Environment.NewLine;
            if (_logListBox == null)
                return;
            try
            {
                _logListBox.Items.Add(Details);
                _logListBox.SelectedIndex = _logListBox.Items.Count - 1;
                _logListBox.Refresh();
            }
            catch
            {
            }
        }

        private void WriteLog()
        {
            // Write the log data to the EWS log
            _logger.Log(_logData, "Autodiscover Process");
            _logData = "";
        }

        private string SMTPDomain()
        {
            if (!(_SMTPAddress.Contains('@'))) return "";
            int i = _SMTPAddress.IndexOf('@');
            return _SMTPAddress.Substring(i + 1);
        }

        public bool Autodiscover(bool SkipSCPAutodiscover = false)
        {
            // Set the start time
            _startTime = DateTime.Now;

            // Reset any existing data
            _AutodiscoverXML = "";
            _logData = "";

            // Store existing certificate callback
            RemoteCertificateValidationCallback oOriginalCallback = ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ValidateServerCertificate);

            if (!SkipSCPAutodiscover)
            {
                // Try SCP autodiscovery
                if (SCPAutodiscover())
                {
                    ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
                    WriteLog();
                    return true;
                }
            }

            // Attempt DNS autodiscovery
            Log("Starting DNS autodiscover");
            string sDomain = SMTPDomain();
            if (String.IsNullOrEmpty(sDomain))
            {
                WriteLog();
                return false;
            }

            // Check SRV records (_autodiscover._tcp.domain)
            ClassDnsSrvQuery srvQuery = new ClassDnsSrvQuery();
            List<SRVRecord> srvDnsRecords = srvQuery.SRVRecords(String.Format("_autodiscover._tcp.{0}", sDomain));

            // Try direct method using two common URLs (based on domain)
            if (TestXmlAutodiscoverUrl("https://" + sDomain + "/AutoDiscover/AutoDiscover.xml"))
            {
                ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
                WriteLog();
                return true;
            }

            if (TestXmlAutodiscoverUrl("https://autodiscover." + sDomain + "/AutoDiscover/AutoDiscover.xml"))
            {
                ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
                WriteLog();
                return true;
            }

            // That failed, so try redirect method
            string sAutodiscoverUrl = AutoDiscoverRedirectUrl();
            if (String.IsNullOrEmpty(sAutodiscoverUrl))
            {
                ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
                WriteLog();
                return false;
            }

            if (TestXmlAutodiscoverUrl(sAutodiscoverUrl))
            {
                ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
                WriteLog();
                return true;
            }

            ServicePointManager.ServerCertificateValidationCallback = oOriginalCallback;
            WriteLog();
            return false;
        }

        private string AutodiscoverRequest()
        {
            string sRequest = _AutodiscoveXMLRequest.Replace("<!--EMAILADDRESS-->", _SMTPAddress);
            return sRequest;
        }

        private HttpWebRequest CreateWebRequest(string Url, string Request, ICredentials Credentials=null)
        {
            // Return a web request for the given URL
            HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(Url);
            if (!(Credentials == null))
            {
                oReq.UseDefaultCredentials = false;
                oReq.Credentials = Credentials;
            }
            oReq.AllowAutoRedirect = false;
            oReq.Method = "POST";
            UTF8Encoding encoding = new UTF8Encoding();
            byte[] bRequest = encoding.GetBytes(Request);
            oReq.ContentType = "text/xml; charset=utf-8";
            oReq.ContentLength = bRequest.Length;
            Stream oReqStream = oReq.GetRequestStream();
            oReqStream.Write(bRequest, 0, bRequest.Length);
            oReqStream.Close();
            oReq.Headers.Add("charset", "utf-8");
            return oReq;
        }

        private bool TestXmlAutodiscoverUrl(string Url)
        {
            if (!Url.ToLower().StartsWith("http"))
            {
                Log("Invalid URL ignored: " + Url);
                return false;
            }

            Log("Testing url: " + Url);
            try
            {
                HttpWebRequest oReq = CreateWebRequest(Url, AutodiscoverRequest());

                HttpWebResponse oResponse = null;
                try
                {
                    oResponse = (HttpWebResponse)oReq.GetResponse();
                }
                catch (Exception ex)
                {
                    if (ex is System.Net.WebException)
                    {
                        WebException wEX = ex as WebException;
                        oResponse = (HttpWebResponse)wEX.Response;
                        if (oResponse.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            if (_lastUrlHadValidCertificate)
                            {
                                // Recreate web request but with authentication
                                Log("Authentication required, adding credentials and resending request");
                                oReq = CreateWebRequest(Url, AutodiscoverRequest(), _credentials);
                                oReq.PreAuthenticate = true;
                                oResponse = (HttpWebResponse)oReq.GetResponse();
                            }
                        }
                    }
                }
                try
                {
                    string sRedirect = oResponse.Headers["Location"];
                    if (!String.IsNullOrEmpty(sRedirect))
                        if (sRedirect != Url)
                        {
                            // We have a redirect
                            Log("Redirected to: " + sRedirect);
                            return TestXmlAutodiscoverUrl(sRedirect);
                        }
                }
                catch { }
                using (StreamReader oReader = new StreamReader(oResponse.GetResponseStream()))
                {
                    _AutodiscoverXML = oReader.ReadToEnd();
                    _AutodiscoverXMLDoc = new XmlDocument();
                    try
                    {
                        _AutodiscoverXMLDoc.LoadXml(_AutodiscoverXML);
                    }
                    catch
                    {
                        // Invalid XML
                        _AutodiscoverXMLDoc = null;
                    }
                }
                return ParseResponse(Url);
            }
            catch (Exception ex)
            {
                if (ex is System.Net.WebException)
                {
                    WebException wEx = (WebException)ex;
                    Log(wEx.Message);
                }
                return TestSOAPAutodiscoverUrl(Url);
            }
        }

        private bool TestSOAPAutodiscoverUrl(string Url)
        {
            if (!Url.ToLower().StartsWith("http"))
            {
                Log("Invalid URL ignored: " + Url);
                return false;
            }

            if (Url.EndsWith(".xml"))
            {
                Url = Url.Substring(0, Url.Length - 3) + "svc";
            }
            string sRequest = _AutodiscoverSOAPRequest.Replace("<!--EMAILADDRESS-->", _SMTPAddress);
            sRequest = sRequest.Replace("<!--AUTODISCOVERSERVICE-->", Url);
            Log("Testing SOAP service: " + Url);

            try
            {
                HttpWebRequest oReq = CreateWebRequest(Url, sRequest);

                HttpWebResponse oResponse = null;
                try
                {
                    oResponse = (HttpWebResponse)oReq.GetResponse();
                }
                catch (Exception ex)
                {
                    if (ex is System.Net.WebException)
                    {
                        WebException wEX = ex as WebException;
                        oResponse = (HttpWebResponse)wEX.Response;
                        if (oResponse.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            if (_lastUrlHadValidCertificate)
                            {
                                // Recreate web request but with authentication
                                Log("Authentication required, adding credentials and resending request");
                                oReq = CreateWebRequest(Url, AutodiscoverRequest(), _credentials);
                                oResponse = (HttpWebResponse)oReq.GetResponse();
                            }
                        }
                    }
                }

                using (StreamReader oReader = new StreamReader(oResponse.GetResponseStream()))
                {
                    _AutodiscoverXML = oReader.ReadToEnd();
                }
                _AutodiscoverXMLDoc = new XmlDocument();
                try
                {
                    _AutodiscoverXMLDoc.LoadXml(_AutodiscoverXML);
                }
                catch
                {
                    // Invalid XML
                    _AutodiscoverXMLDoc = null;
                }
                return ParseResponse(Url);
            }
            catch
            {
                return false;
            }
        }

        private bool ParseResponse(string FromUrl)
        {
            // Check the response and extract what we need...
            if (_AutodiscoverXMLDoc == null) return false;

            // We have some XML, so log this (assuming Logger specified)
            if (!(_logger == null))
            {
                _logger.Log(_AutodiscoverXML, "Autodiscover response from " + FromUrl);
            }

            // Check for error
            XmlNodeList oErrorCodes = _AutodiscoverXMLDoc.GetElementsByTagName("ErrorCode");
            if (oErrorCodes.Count > 0)
            {
                // We have an error
                XmlNode oErrorCode = _AutodiscoverXMLDoc.GetElementsByTagName("ErrorCode").Item(0);
                if (oErrorCode.InnerText == "601")
                {
                    // Provider is not available.  In this case, we'll try the SOAP service
                    Log("Provider not available, trying SOAP service");
                    return TestSOAPAutodiscoverUrl(FromUrl);
                }
                return false;
            }

            // Check for Ews Url
            string sElement = "EwsUrl";
            string sEwsUrl = ReadElement(sElement);
            if (String.IsNullOrEmpty(sEwsUrl))
            {
                sElement = "ExternalEwsUrl";
                sEwsUrl = ReadUserSetting(sElement);
                if (String.IsNullOrEmpty(sEwsUrl))
                {
                    sElement = "InternalEwsUrl";
                    sEwsUrl = ReadUserSetting(sElement);
                }
            }

            if (!String.IsNullOrEmpty(sEwsUrl))
            {
                Log("AutoDiscover Url found (" + sElement + "): " + sEwsUrl);
                return true;
            }

            // Check for redirect address


            Log("Autodiscover XML was found, but no EWS URL was present");

            return false;
        }

        private string ReadElement(string ElementName)
        {
            if (_AutodiscoverXMLDoc == null) return null;

            XmlNodeList oEwsUrls = _AutodiscoverXMLDoc.GetElementsByTagName(ElementName);
            if (oEwsUrls.Count > 0)
            {
                // We have an EWL Url
                XmlNode oEwsUrl = oEwsUrls.Item(0);
                return oEwsUrl.InnerText;
            }
            return null;
        }

        private string ReadUserSetting(string SettingName)
        {
            if (_AutodiscoverXMLDoc == null) return null;
            XmlNodeList oSettings = _AutodiscoverXMLDoc.GetElementsByTagName("UserSetting");
            foreach (XmlNode oSetting in oSettings)
            {
                string sName = "";
                string sValue = "";
                foreach (XmlNode oNode in oSetting.ChildNodes)
                {
                    switch (oNode.Name)
                    {
                        case "Name":
                            sName = oNode.InnerText;
                            break;

                        case "Value":
                            sValue = oNode.InnerText;
                            break;
                    }
                }
                if (sName == SettingName)
                {
                    return sValue;
                }
            }
            return null;
        }

        private string AutoDiscoverRedirectUrl()
        {
            try
            {
                string sAutodiscoverUrl = "http://autodiscover." + SMTPDomain() + "/Autodiscover/Autodiscover.xml";
                Log("Testing for redirect from: " + sAutodiscoverUrl);
                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create("http://autodiscover." + SMTPDomain() + "/Autodiscover/Autodiscover.xml");
                oReq.AllowAutoRedirect = false;
                WebResponse oResponse = oReq.GetResponse();
                sAutodiscoverUrl = oResponse.Headers["Location"];
                if (String.IsNullOrEmpty(sAutodiscoverUrl))
                    return "";
                Log("Redirect found: " + sAutodiscoverUrl);
                return sAutodiscoverUrl;
            }
            catch
            {
                return "";
            }
        }

        public static bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            _lastUrlHadValidCertificate = true;
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            // Confirm whether to allow the certificate.
            string sError = "";
            switch (sslPolicyErrors)
            {
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    sError += "Remote certificate chain errors";
                    break;

                case SslPolicyErrors.RemoteCertificateNameMismatch:
                    sError += "Certificate name mismatch";
                    break;

                case SslPolicyErrors.RemoteCertificateNotAvailable:
                    sError += "Remote certificate not available";
                    break;

                default:
                    sError += "Unknown error";
                    break;
            }

            Log("Certificate error: " + sError);

            if (_ignoreCertificateErrors) return true;

            _lastUrlHadValidCertificate = false;
            return false;
        }

        public bool SCPAutodiscover(string ServiceBindingInfo = "", bool ClearTestedList = true)
        {
            // Attempt SCP autodiscover
            bool bAutodiscoverDone = false;
            if (ClearTestedList)
                _testedUrls = new List<string>();

            string configurationNamingContext = "";
            try
            {
                string sRootDSEPath = "LDAP://";
                if (!String.IsNullOrEmpty(ServiceBindingInfo))
                    sRootDSEPath += ServiceBindingInfo + "/";
                sRootDSEPath += "rootDSE";
                DirectoryEntry rootDse = new DirectoryEntry(sRootDSEPath);
                configurationNamingContext = Convert.ToString(rootDse.Properties["configurationNamingContext"][0], CultureInfo.CurrentCulture);
            }
            catch { }

            if (String.IsNullOrEmpty(configurationNamingContext))
            {
                Log("SCP lookup failed");
                return false;
            }

            SearchResultCollection oResults = null;
            try
            {
                DirectorySearcher ds = new DirectorySearcher(new DirectoryEntry("LDAP://" + configurationNamingContext),
                    "(&(objectClass=serviceConnectionPoint)(|(keywords=67661d7F-8FC4-4fa7-BFAC-E1D7794C1F68)(keywords=77378F46-2C66-4aa9-A6A6-3E7A48B19596)))");
                ds.SearchScope = SearchScope.Subtree;
                ds.PropertiesToLoad.Add("serviceBindingInformation");
                ds.PropertiesToLoad.Add("keywords");
                oResults = ds.FindAll();
            }
            catch (Exception ex)
            {
                Log("SCP Lookup failed: " + ex.Message);
                return false;
            }

            if (oResults.Count < 1)
            {
                oResults.Dispose();
                Log("SCP Lookup failed: no service records found");
                return false;
            }
            else
                Log("SCP Lookup: " + oResults.Count.ToString() + " record(s) found");

            foreach (SearchResult oResult in oResults)
            {
                if (oResult.Properties.Contains("keywords"))
                {
                    // Check for domain scope
                    if (oResult.Properties["keywords"].Contains("Domain=" + SMTPDomain()))
                    {
                        string sURL = oResult.Properties["serviceBindingInformation"][0].ToString();
                        bAutodiscoverDone = SCPAutodiscover(sURL, false);
                        if (bAutodiscoverDone) break;
                    }
                }
            }
            Log("Completed SCP search: Domain=" + SMTPDomain());

            string sSite = System.DirectoryServices.ActiveDirectory.ActiveDirectorySite.GetComputerSite().ToString();
            if (!bAutodiscoverDone)
            {
                // No domain scoping, test site scoping
                foreach (SearchResult oResult in oResults)
                {
                    if (oResult.Properties.Contains("keywords"))
                    {
                        // Check for site scope
                        if (oResult.Properties["keywords"].Contains("Site=" + sSite))
                        {
                            if (oResult.Properties.Contains("serviceBindingInformation"))
                            {
                                string sURL = oResult.Properties["serviceBindingInformation"][0].ToString();
                                if (!_testedUrls.Contains(sURL))
                                {
                                    _testedUrls.Add(sURL);
                                    bAutodiscoverDone = TestXmlAutodiscoverUrl(sURL);
                                }
                                if (bAutodiscoverDone) break;
                            }
                        }
                    }
                }
            }
            Log("Completed SCP search: Site=" + sSite);

            if (!bAutodiscoverDone)
            {
                // No site scoping, test for ANY endpoint
                foreach (SearchResult oResult in oResults)
                {
                    if (oResult.Properties.Contains("keywords"))
                    {
                        // Check for site scope (if result is scoped to site, ignore it)
                        bool bIsSiteScoped = false;
                        foreach (string sKeyword in oResult.Properties.PropertyNames)
                        {
                            if (sKeyword.ToLower().StartsWith("site="))
                            {
                                bIsSiteScoped = true;
                                break;
                            }
                        }
                        if (!bIsSiteScoped)
                        {
                            if (oResult.Properties.Contains("serviceBindingInformation"))
                            {
                                string sURL = oResult.Properties["serviceBindingInformation"][0].ToString();
                                if (!_testedUrls.Contains(sURL))
                                {
                                    _testedUrls.Add(sURL);
                                    bAutodiscoverDone = TestXmlAutodiscoverUrl(sURL);
                                }
                                if (bAutodiscoverDone) break;
                            }
                        }
                    }
                }
            }
            Log("Finished SCP Autodiscover");

            oResults.Dispose();

            return bAutodiscoverDone;
        }
    }
}
