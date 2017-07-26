using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using System.Xml.Linq;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace WorkerRole1
{
    public class WorkerRole : RoleEntryPoint
    {
        private const string urlQueueName = "myurls";
        private const string pageTableName = "crawledpagesdata";
        private const string sysInfoTableName = "sysinfo";
        private const string adminQueueName = "adminqueue";
        private const string testQueueName = "testqueue";

        private static List<string> disallowedList;
        private static HashSet<string> parsedUrls;
        private static bool isStarted;
        private static string crawlerState;
        private static Queue<string> last10UrlsQueue;
        private static int currentNumRows;
        private static int crawledUrls;

        private PerformanceCounter memProcess = new PerformanceCounter("Memory", "Available MBytes");
        private PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        public override void Run()
        {
            // This is a sample worker implementation. Replace with your logic.
            Trace.TraceInformation("WorkerRole1 entry point called");

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                 RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            while (true)
            {
                Thread.Sleep(500);
                ReadAdmin(queueClient, tableClient);
                //if (isStarted)
                //{
                    crawlerState = "Working";
                    CrawlAndSave(queueClient, tableClient);
                //}

                UpdateReports(queueClient, tableClient);
                Trace.TraceInformation("Working");
            }
        }

        /// <summary>
        /// Initializes the worker role to create any missing data containers
        /// </summary>
        /// <returns></returns>
        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            disallowedList = new List<string>();
            parsedUrls = new HashSet<string>();
            isStarted = false;
            crawlerState = "Stopped";
            last10UrlsQueue = new Queue<string>();


            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                 RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));

            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(pageTableName);
            CloudTable sysTable = tableClient.GetTableReference(sysInfoTableName);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue testQueue = queueClient.GetQueueReference(testQueueName);


            testQueue.CreateIfNotExists();
            sysTable.CreateIfNotExists();
            table.CreateIfNotExists();


            TableOperation retrieveOperation = TableOperation.Retrieve<ReportsTable>("allreports", "allreports");
            TableResult retrievedResult = sysTable.Execute(retrieveOperation);

            if (retrievedResult.Result != null)
            {
                currentNumRows = Convert.ToInt32(((ReportsTable)retrievedResult.Result).IndexSize);
                crawledUrls = Convert.ToInt32(((ReportsTable)retrievedResult.Result).NumUrls);
                string last10String = (((ReportsTable)retrievedResult.Result).Last10Urls).ToString();

                if (last10String != null && last10String.Length > 0)
                {
                    var last10Arr = last10String.Split(',');
                    foreach (string last in last10Arr)
                    {
                        last10UrlsQueue.Enqueue(last);
                    }
                }
            }
            else
            {
                currentNumRows = 0;
                crawledUrls = 0;
            }

            return base.OnStart();
        }

        /// <summary>
        /// Reads commands from the admin queue
        /// </summary>
        /// <param name="queueClient"></param>
        /// <param name="tableClient"></param>
        public void ReadAdmin(CloudQueueClient queueClient, CloudTableClient tableClient)
        {
            CloudQueue queue = queueClient.GetQueueReference(adminQueueName);
            if (queue.Exists())
            {
                CloudQueueMessage message = queue.GetMessage(TimeSpan.FromMinutes(5));
                if (message != null)
                {
                    string adminMessage = message.AsString;
                    if (adminMessage.Contains("start:"))
                    {
                        CloudTable table = tableClient.GetTableReference(pageTableName);
                        CloudTable sysTable = tableClient.GetTableReference(sysInfoTableName);
                        CloudQueue urlQueue = queueClient.GetQueueReference(urlQueueName);


                        urlQueue.CreateIfNotExists();
                        sysTable.CreateIfNotExists();
                        table.CreateIfNotExists();

                        string dissallowedJoined = adminMessage.Substring(6).Replace(" ", string.Empty);
                        string[] dissallowedArr = dissallowedJoined.Split(',');

                        foreach (string dissallowed in dissallowedArr)
                        {
                            if (!isDissallowed(disallowedList, dissallowed))
                            {
                                disallowedList.Add(dissallowed);
                            }
                        }

                        isStarted = true;
                    }
                    if (adminMessage.Contains("end"))
                    {
                        isStarted = false;
                        crawlerState = "Stopped";
                    }

                    queue.DeleteMessage(message);
                }
            }
        }

        /// <summary>
        /// Crawls webpages and saves them to table storage
        /// </summary>
        /// <param name="queueClient"></param>
        /// <param name="tableClient"></param>
        public void CrawlAndSave(CloudQueueClient queueClient, CloudTableClient tableClient)
        {
            CloudQueue queue = queueClient.GetQueueReference(urlQueueName);
            CloudTable table = tableClient.GetTableReference(pageTableName);
            CloudQueue testQueue = queueClient.GetQueueReference(testQueueName);

            if (queue.Exists())
            {
                CloudQueueMessage message = queue.GetMessage(TimeSpan.FromMinutes(5));

                if (message != null)
                {
                    string myUrl = message.AsString;

                    //Need to work in dissallowed stuff
                    //if (!isDissallowed(disallowedList, myUrl)) 
                    if (!parsedUrls.Contains(myUrl))
                    {
                        // Tracks Last 10 Urls Parsed, change for multiple threads
                        if (last10UrlsQueue.Count < 10)
                        {
                            last10UrlsQueue.Enqueue(myUrl.Trim());
                        }
                        else
                        {
                            while (last10UrlsQueue.Count > 10)
                            {
                                last10UrlsQueue.Dequeue();
                            }
                            last10UrlsQueue.Enqueue(myUrl.Trim());
                        }

                        crawledUrls++;

                        if (myUrl.EndsWith(".xml"))
                        {
                            //ParseXmlSiteMap(queue, myUrl);

                            XDocument doc = XDocument.Load(myUrl);
                            XNamespace xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9";

                            var xmlNode = doc.Descendants(xmlns + "sitemap").Select(x => new
                            {
                                Loc = x.Element(xmlns + "loc").Value,
                                Date = DateTime.Parse(x.Element(xmlns + "lastmod").Value)
                            }).ToList();

                            DateTime dt = DateTime.Parse("March 1, 2014");
                            var relevantNodes = xmlNode.Where(x => x.Date.CompareTo(dt) >= 0);

                            if (xmlNode.Count == 0)
                            {
                                var xmlNode2 = doc.Descendants(xmlns + "url").Select(x => new
                                {
                                    Loc = x.Element(xmlns + "loc").Value,
                                }).ToList();

                                foreach (var relevantXml in xmlNode2)
                                {
                                    string nodeLoc = relevantXml.Loc;
                                    CloudQueueMessage message2 = new CloudQueueMessage(nodeLoc);
                                    queue.AddMessage(message2);
                                }
                            }

                            foreach (var relevantXml in relevantNodes)
                            {
                                string nodeLoc = relevantXml.Loc;
                                CloudQueueMessage message3 = new CloudQueueMessage(nodeLoc);
                                queue.AddMessage(message3);
                            }

                        }
                        else if (myUrl.Contains(".htm") || myUrl.Contains(".html"))
                        {

                            CloudQueueMessage testMessage = new CloudQueueMessage(myUrl);
                            testQueue.AddMessage(testMessage);

                            DateTime dt = DateTime.Parse("March 1, 2014");

                            HtmlAgilityPack.HtmlDocument htmlDoc = new HtmlAgilityPack.HtmlDocument();

                            // There are various options, set as needed
                            // htmlDoc.OptionFixNestedTags = true;

                            using (WebClient client = new WebClient())
                            {
                                string downloadedHtml = "";

                                try { 
                                    downloadedHtml = client.DownloadString(myUrl);
                                }
                                catch {

                                }
                                // filePath is a path to a file containing the html
                                htmlDoc.LoadHtml(downloadedHtml);
                                

                                // ParseErrors is an ArrayList containing any errors from the Load statement
                                //if (htmlDoc.ParseErrors != null && htmlDoc.ParseErrors.Count() > 0)
                                //{
                                    // Handle any parse errors as required
                                //}
                                //else
                                //{
                                if (htmlDoc.DocumentNode != null)
                                {
                                    HtmlAgilityPack.HtmlNode titleNode = htmlDoc.DocumentNode.SelectSingleNode("//title");
                                    HtmlAgilityPack.HtmlNode dateNode = htmlDoc.DocumentNode.SelectSingleNode("//meta[@http-equiv='last-modified']");
                                    HtmlAgilityPack.HtmlNode bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body");

                                    string title = "";
                                    string date = "";
                                    string body = "";

                                    string stringDate = "";
                                    bool isCurrent = true;

                                    // Change to handle date comparison better
                                    if (dateNode != null)
                                    {
                                        HtmlAttribute desc = dateNode.Attributes["content"];
                                        stringDate = desc.Value;
                                        date = stringDate;
                                        try
                                        {
                                            DateTime webDate = DateTime.Parse(stringDate);
                                            if (webDate.Date.CompareTo(dt) < 0)
                                            {
                                                isCurrent = false;
                                            }
                                        }
                                        catch
                                        {

                                        }
                                    }

                                    if (isCurrent) 
                                    { 
                                        if (titleNode != null)
                                        {
                                            title = titleNode.InnerText;
                                        }

                                        if (bodyNode != null)
                                        {
                                            body = bodyNode.InnerText;
                                        }

                                        //string transformedUrl = myUrl.Trim();
                                        //transformedUrl = System.Uri.EscapeDataString(transformedUrl);

                                        string[] titleStrings = title.Split(' ');

                                        foreach (string key in titleStrings) {

                                            Regex rgx = new Regex("[^a-zA-Z0-9]");
                                            string regKey = rgx.Replace(key, "");
                                            regKey = regKey.ToLower();

                                            if (regKey.Length > 0 && !regKey.Equals("cnncom") && !regKey.Equals("-")) { 
                                                string transformedUrl = myUrl.Trim();
                                                var keyBytes = System.Text.Encoding.UTF8.GetBytes(transformedUrl);
                                                var base64 = System.Convert.ToBase64String(keyBytes);
                                                transformedUrl = base64.Replace('/', '_');

                                                //to Decode do this:
                                                //var base64 = encodedKey.Replace('_', '/');
                                                //byte[] bytes = System.Convert.FromBase64String(base64);
                                                //return System.Text.Encoding.UTF8.GetString(bytes);

                                                //CrawledSite currentSite = new CrawledSite(regKey, transformedUrl, myUrl, title, date, body);
                                                CrawledSite currentSite = new CrawledSite(regKey, transformedUrl, myUrl, title, date, body);
                                                TableOperation insertOperation = TableOperation.InsertOrReplace(currentSite);

                                                DateTime? nullableDt = null;
                                                nullableDt = DateTime.Now;

                                                currentSite.Timestamp = nullableDt.GetValueOrDefault();
                                                currentSite.ETag = "*";
                                                try 
                                                { 
                                                    table.Execute(insertOperation);
                                                    currentNumRows++;
                                                    parsedUrls.Add(myUrl);
                                                }
                                                catch
                                                {

                                                }
                                            }
                                        }
                                    }

                                    var linksOnPage = from lnks in htmlDoc.DocumentNode.Descendants()
                                                        where lnks.Name == "a" &&
                                                            lnks.Attributes["href"] != null &&
                                                            lnks.InnerText.Trim().Length > 0
                                                        select new
                                                        {
                                                            Url = lnks.Attributes["href"].Value,
                                                        };

                                    foreach (var link in linksOnPage)
                                    {
                                        string discoveredLink = link.Url;
                                        if (isCNN(discoveredLink))
                                        {
                                            CloudQueueMessage newUrl = new CloudQueueMessage(discoveredLink);
                                            queue.AddMessage(newUrl);
                                        }
                                    }
                                    //}
                                }
                            }
                        }
                    }
                    queue.DeleteMessage(message);
                }
            }
        }

        /// <summary>
        /// Helper function to parse XML
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="xmlAddress"></param>
        public void ParseXmlSiteMap(CloudQueue queue, string xmlAddress)
        {
            XDocument doc = XDocument.Load(xmlAddress);
            XNamespace xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var xmlNode = doc.Descendants(xmlns + "sitemap").Select(x => new
            {
                Loc = x.Element(xmlns + "loc").Value,
                Date = DateTime.Parse(x.Element(xmlns + "lastmod").Value)
            }).ToList();

            DateTime dt = DateTime.Parse("March 1, 2014");
            var relevantNodes = xmlNode.Where(x => x.Date.CompareTo(dt) >= 0);

            if (xmlNode.Count == 0)
            {
                var xmlNode2 = doc.Descendants(xmlns + "url").Select(x => new
                {
                    Loc = x.Element(xmlns + "loc").Value,
                }).ToList();

                foreach (var relevantXml in xmlNode2)
                {
                    string nodeLoc = relevantXml.Loc.Trim();
                    CloudQueueMessage message = new CloudQueueMessage(nodeLoc);
                    queue.AddMessage(message);
                }
            }

            foreach (var relevantXml in relevantNodes)
            {
                string nodeLoc = relevantXml.Loc.Trim();
                CloudQueueMessage message = new CloudQueueMessage(nodeLoc);
                queue.AddMessage(message);
            }
        }

        /// <summary>
        /// Helper function to find dissallowed urls
        /// </summary>
        /// <param name="disallowedList"></param>
        /// <param name="myUrl"></param>
        /// <returns></returns>
        public bool isDissallowed(List<string> disallowedList, string myUrl)
        {
            foreach (string dissallowed in disallowedList)
            {
                if (myUrl.Contains(dissallowed))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Helper function to see if it's still in the cnn domain
        /// </summary>
        /// <param name="myUrl"></param>
        /// <returns></returns>
        public bool isCNN(string myUrl)
        {
            if (myUrl.Contains("cnn.com"))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Updates the admin system information table
        /// </summary>
        /// <param name="queueClient"></param>
        /// <param name="tableClient"></param>
        public void UpdateReports(CloudQueueClient queueClient, CloudTableClient tableClient)
        {
            CloudQueue urlQueue = queueClient.GetQueueReference(urlQueueName);
            CloudTable reportsTable = tableClient.GetTableReference(sysInfoTableName);

            int memProcessInt = (int)memProcess.NextValue();
            int cpuUsageInt = (int)cpuCounter.NextValue();

            int urlQueueSize = 0;

            if (urlQueue.Exists())
            {
                urlQueue.FetchAttributes();
                if (urlQueue.ApproximateMessageCount != null)
                {
                    urlQueueSize = (int)urlQueue.ApproximateMessageCount;
                }
            }

            string currentCrawlerState = crawlerState;

            string last10Urls = "";

            for (int i = 0; i < last10UrlsQueue.Count; i++)
            {
                string oneUrl = last10UrlsQueue.Dequeue();
                last10Urls = last10Urls + oneUrl + ",";
                last10UrlsQueue.Enqueue(oneUrl);
            }

            
            TableOperation retrieveOperation = TableOperation.Retrieve<ReportsTable>("allreports", "allreports");
            TableResult retrievedResult = reportsTable.Execute(retrieveOperation);

            string lastTrieLine = "";
            int numWikiNodes = 0;
            if (retrievedResult.Result != null)
            { 
                lastTrieLine = ((ReportsTable)retrievedResult.Result).LastTrieLine;
                numWikiNodes = Convert.ToInt32(((ReportsTable)retrievedResult.Result).TrieSize);
            }

            ReportsTable currentState = new ReportsTable(currentCrawlerState, crawledUrls, last10Urls, urlQueueSize, currentNumRows, memProcessInt, cpuUsageInt, lastTrieLine, numWikiNodes);
            TableOperation insertOperation = TableOperation.InsertOrReplace(currentState);
            reportsTable.Execute(insertOperation);
        }
    }
}
