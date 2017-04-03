using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using HtmlAgilityPack;
using System.Collections.Generic;
using Reddit_Un_Pauser.Model;
using Newtonsoft.Json;
// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable ConditionIsAlwaysTrueOrFalse

namespace Reddit_Un_Pauser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public CookieContainer Cookies;
        public List<Campaign> Campaigns;

        public MainWindow()
        {
            InitializeComponent();

            GeneralProgressBar.Visibility = Visibility.Hidden;
            PauseAllActivecampaignsButton.IsEnabled = false;
            ResumeAllPausedcampaignsButton.IsEnabled = false;
        }

        /// <summary>
        /// Register events to MainWindow status textblock and Log file
        /// </summary>
        /// <param name="type">Log message type (INFO, WARNING, ERROR)</param>
        /// <param name="msg">Message to display</param>
        private void Log(string type, string msg)
        {
            var status = string.Empty;

            switch (type)
            {
                case "INFO":

                    status += $"[INFO] {DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {msg}";

                    break;

                case "ERROR":

                    status += $"[ERROR] {DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {msg}";

                    break;

                case "WARNING":

                    status += $"[WARNING] {DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {msg}";

                    break;
            }

            StatusTextBlock.Text += status + Environment.NewLine;
            StatusTextBlock.ScrollToEnd();

            using (var writer = new StreamWriter("Log.txt", true))
            {
                writer.Write(status + Environment.NewLine);
            }
        }

        private async void LoadActiveCampaignsStateButton_Click(object sender, RoutedEventArgs e)
        {
            Log("INFO", "Starting Campaigns gathering process...");
            GeneralProgressBar.Visibility = Visibility.Visible;

            var doc = new HtmlDocument();
            var stop = false;

            Campaigns = new List<Campaign>();

            var task = Task.Factory.StartNew(() =>
            {
                doc = GetHtmlDocument("https://www.reddit.com/promoted/");
            });

            await task;

            do
            {
                var promotedAds = doc.DocumentNode.SelectNodes("//div[div[ul[li[span[@class='promoted-tag']]]]]");

                foreach (var traffic in promotedAds)
                {
                    var url = new Uri($"https://www.reddit.com/promoted/edit_promo/{traffic.GetAttributeValue("data-fullname", "").Split('_')[1]}");

                    Log("INFO", $"Checking ad ID {url.Segments.Last().Replace("/", "")}");

                    var adPage = new HtmlDocument();

                    task = Task.Factory.StartNew(() =>
                    {
                        adPage = GetHtmlDocument(url.ToString());
                    });

                    await task;

                    var liveCampaigns = adPage.DocumentNode.SelectNodes("//tr[@data-is_live='True']");
                    var uh = adPage.DocumentNode.SelectNodes("//form")[0].SelectNodes("//input")[0]
                                    .Attributes[2].Value;

                    foreach (var liveCampaign in liveCampaigns)
                    {
                        var campaign = new Campaign();

                        campaign.CampaignID = liveCampaign.GetAttributeValue("data-campaign_id36", "");
                        campaign.PromotionID = liveCampaign.GetAttributeValue("data-link_id36", "");
                        campaign.Uh = uh;

                        if (liveCampaign.SelectSingleNode("td/button[@class='pause']") != null)
                            campaign.State = State.Running;
                        else
                            campaign.State = State.Paused;

                        Campaigns.Add(campaign);
                    }
                }

                if (doc.DocumentNode.SelectSingleNode("//a[contains(text(), 'next')]") != null)
                {
                    Log("INFO", (doc.DocumentNode.SelectSingleNode("//a[contains(text(), 'next')]") != null).ToString());
                    Log("INFO", (stop).ToString());
                    Log("INFO", "Handling pagination");

                    var newUrl =
                        doc.DocumentNode.SelectSingleNode("//a[contains(text(), 'next')]").GetAttributeValue("href", "");

                    await Task.Factory.StartNew(() =>
                    {
                        doc = GetHtmlDocument(newUrl);
                    });
                }
                else
                    stop = true;
            }
            while (stop != true);

            GeneralProgressBar.Visibility = Visibility.Hidden;

            ShowCampaignsStatus();

            PauseAllActivecampaignsButton.IsEnabled = Campaigns.Where(x => x.State == State.Running).Count() > 0 ? true : false;
            ResumeAllPausedcampaignsButton.IsEnabled = Campaigns.Where(x => x.State == State.Paused).Count() > 0 ? true : false;
        }

        private void ShowCampaignsStatus()
        {
            Log("INFO", "Ended Campaigns gathering process...");
            Log("INFO", $"Live campaigns total: {Campaigns.Count}");
            Log("INFO", $"Running campaigns: {Campaigns.Where(x => x.State == State.Running).Count()}");
            Log("INFO", $"Paused campaigns: {Campaigns.Where(x => x.State == State.Paused).Count()}");
        }

        /// <summary>
        /// Get a HtmlDocument from the supplied Url
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private HtmlDocument GetHtmlDocument(string url)
        {
            var doc = new HtmlDocument();

            var docRequest = WebRequest.Create(url) as HttpWebRequest;

            if (docRequest != null)
            {
                docRequest.CookieContainer = Cookies;
                docRequest.Method = "GET";
                docRequest.Accept =
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";

                var docResponse = (HttpWebResponse)docRequest.GetResponse();

                if (docResponse.StatusCode == HttpStatusCode.OK)
                    using (var s = docResponse.GetResponseStream())
                    {
                        using (var sr = new StreamReader(s, Encoding.GetEncoding(name: docResponse.CharacterSet)))
                        {
                            doc.Load(sr);
                        }
                    }
            }

            return doc;
        }

        private async void PauseAllActivecampaignsButton_Click(object sender, RoutedEventArgs e)
        {
            GeneralProgressBar.Visibility = Visibility.Visible;

            var runningCampaigns = Campaigns.Where(x => x.State == State.Running).ToList();

            foreach (var campaign in runningCampaigns)
            {
                var errorMsg = string.Empty;
                var error = false;
                var result = new RedditAdJson();

                try
                {
                    var task = Task.Factory.StartNew(() =>
                    {
                        result = ToggleCampaign(campaign, true);
                    });

                    await task;
                }
                catch (AggregateException ae)
                {
                    ae.Handle(x =>
                    {
                        errorMsg = x.Message + " | " + x.StackTrace;
                        error = true;

                        return error;
                    });
                }

                if (error)
                {
                    Log("ERROR", errorMsg);
                }
                else
                {
                    if (result.Success)
                    {
                        Log("INFO", $"Campaign #{campaign.CampaignID} on ad #{campaign.PromotionID} successfully paused");
                        campaign.State = State.Paused;
                    }
                }
            }

            GeneralProgressBar.Visibility = Visibility.Hidden;
            ShowCampaignsStatus();
            ResumeAllPausedcampaignsButton.IsEnabled = true;
            PauseAllActivecampaignsButton.IsEnabled = false;
        }

        private async void ResumeAllPausedcampaignsButton_Click(object sender, RoutedEventArgs e)
        {
            GeneralProgressBar.Visibility = Visibility.Visible;

            var runningCampaigns = Campaigns.Where(x => x.State == State.Paused).ToList();
            
            foreach (var campaign in runningCampaigns)
            {
                var errorMsg = string.Empty;
                var error = false;
                var result = new RedditAdJson();

                try
                {
                    var task = Task.Factory.StartNew(() =>
                    {
                        result = ToggleCampaign(campaign, false);
                    });

                    await task;
                }
                catch (AggregateException ae)
                {
                    ae.Handle(x =>
                    {
                        errorMsg = x.Message + " | " + x.StackTrace;
                        error = true;

                        return error;
                    });
                }

                if (error)
                {
                    Log("ERROR", errorMsg);
                }
                else
                {
                    if (result.Success)
                    {
                        Log("INFO", $"Campaign #{campaign.CampaignID} on ad #{campaign.PromotionID} successfully resumed");
                        campaign.State = State.Running;
                    }
                }
            }

            GeneralProgressBar.Visibility = Visibility.Hidden;
            ShowCampaignsStatus();
            ResumeAllPausedcampaignsButton.IsEnabled = false;
            PauseAllActivecampaignsButton.IsEnabled = true;
        }

        private RedditAdJson ToggleCampaign(Campaign campaign, bool v)
        {
            var result = new RedditAdJson();

            string postString =
                            $"campaign_id36={campaign.CampaignID}&link_id36={campaign.PromotionID}&should_pause={v.ToString().ToLower()}&uh={campaign.Uh}&renderstyle=html";
            var postUrl = "https://www.reddit.com//api/toggle_pause_campaign";

            var toggleRequest = WebRequest.Create(postUrl) as HttpWebRequest;

            if (toggleRequest == null) throw new ArgumentNullException(nameof(toggleRequest));

            toggleRequest.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
            toggleRequest.Method = "POST";
            toggleRequest.CookieContainer = Cookies;
            toggleRequest.Accept = "application/json, text/javascript, */*; q=0.01";
            toggleRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            toggleRequest.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/56.0.2924.87 Safari/537.36";
            toggleRequest.Referer = "https://www.reddit.com/";
            toggleRequest.Timeout = 15000;

            var customHeaders = toggleRequest.Headers;

            customHeaders.Add("accept-language", "en;q=0.4");
            customHeaders.Add("origin", "https://www.reddit.com");
            customHeaders.Add("x-requested-with", "XMLHttpRequest");

            var bytes = Encoding.ASCII.GetBytes(postString);

            toggleRequest.ContentLength = bytes.Length;

            using (var os = toggleRequest.GetRequestStream())
            {
                os.Write(bytes, 0, bytes.Length);
            }

            var toggleResponse = toggleRequest.GetResponse() as HttpWebResponse;


            if (toggleResponse != null && toggleResponse.StatusCode == HttpStatusCode.OK)
                using (var s = toggleResponse.GetResponseStream())
                {
                    // ReSharper disable once AssignNullToNotNullAttribute
                    using (var sr = new StreamReader(s, Encoding.GetEncoding(toggleResponse.CharacterSet)))
                    {
                        result = JsonConvert.DeserializeObject<RedditAdJson>(sr.ReadToEnd());
                    }
                }

            return result;
        }
    }

    public class RedditAdJson
    {
        public List<List<object>> Jquery { get; set; }
        public bool Success { get; set; }
    }

    public class Field
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class AdS3Json
    {
        public string Action { get; set; }
        public List<Field> Fields { get; set; }
    }
}
