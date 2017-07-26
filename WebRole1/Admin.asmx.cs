using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Script.Services;
using System.Web.Services;
using System.Xml.Linq;

namespace WebRole1
{
    /// <summary>
    /// Summary description for admin
    /// </summary>-
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class Admin : System.Web.Services.WebService
    {
        private static string[] robots = new string[] { "http://www.cnn.com/robots.txt", "http://sportsillustrated.cnn.com/robots.txt" };
        private const string urlQueueName = "myurls";
        private const string PAGE_TABLE_NAME = "crawledpagesdata";
        private const string sysInfoTableName = "sysinfo";
        private const string adminQueueName = "adminqueue";

        [WebMethod]
        public string TestCrawlOneLink()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings["StorageConnectionSTring"]);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(urlQueueName);
            queue.CreateIfNotExists();

            CloudQueue adminQueue = queueClient.GetQueueReference(adminQueueName);
            adminQueue.CreateIfNotExists();
            List<string> disallowedList = new List<string>();

            string siteMapUrl = "http://www.cnn.com/2014/06/02/travel/topless-tour/index.html";
            CloudQueueMessage message = new CloudQueueMessage(siteMapUrl);
            queue.AddMessage(message);

            string allDissallowed = string.Join(",", disallowedList);
            CloudQueueMessage adminMessage = new CloudQueueMessage("start: " + allDissallowed);
            adminQueue.AddMessage(adminMessage);

            return "Started Crawling";
        }

        [WebMethod]
        public string StartCrawling()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings["StorageConnectionSTring"]);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(urlQueueName);
            queue.CreateIfNotExists();

            CloudQueue adminQueue = queueClient.GetQueueReference(adminQueueName);
            adminQueue.CreateIfNotExists();

            string downloadedString;
            List<string> disallowedList = new List<string>();

            using (WebClient client = new WebClient())
            {
                foreach (string robot in robots)
                {
                    downloadedString = client.DownloadString(robot);
                    string[] lines = downloadedString.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];

                        if (line.Contains("Disallow:"))
                        {
                            string dissallowed = line.Substring(9).Replace(" ", string.Empty);
                            disallowedList.Add(dissallowed);
                        }

                        if (line.Contains("Sitemap:"))
                        {
                            string siteMapUrl = line.Substring(8).Replace(" ", string.Empty);
                            CloudQueueMessage message = new CloudQueueMessage(siteMapUrl);
                            queue.AddMessage(message);
                        }
                    }
                }
            }

            string allDissallowed = string.Join(",", disallowedList);
            CloudQueueMessage adminMessage = new CloudQueueMessage("start: " + allDissallowed);
            adminQueue.AddMessage(adminMessage);

            return "Started Crawling";
        }

        [WebMethod]
        public string ClearIndex()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
               ConfigurationManager.AppSettings["StorageConnectionSTring"]);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(urlQueueName);

            if (queue.Exists())
            {
                queue.Delete();
            }

            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(PAGE_TABLE_NAME);

            if (table.Exists())
            {
                table.Delete();
            }

            CloudTable reportsTable = tableClient.GetTableReference(sysInfoTableName);

            if (reportsTable.Exists())
            {
                TableOperation retrieveOperation = TableOperation.Retrieve<ReportsTable>("allreports", "allreports");
                TableResult retrievedResult = reportsTable.Execute(retrieveOperation);

                string lastTrieLine = "";
                int numWikiNodes = 0;
                if (retrievedResult.Result != null)
                {
                    lastTrieLine = ((ReportsTable)retrievedResult.Result).LastTrieLine;
                    numWikiNodes = Convert.ToInt32(((ReportsTable)retrievedResult.Result).TrieSize);
                }

                ReportsTable currentState = new ReportsTable("Stopped", 0, "", 0, 0, 0, 0, lastTrieLine, numWikiNodes);
                TableOperation insertOperation = TableOperation.InsertOrReplace(currentState);
                reportsTable.Execute(insertOperation);
            }

            CloudQueue adminQueue = queueClient.GetQueueReference(adminQueueName);

            if (adminQueue.Exists())
            {
                CloudQueueMessage message = new CloudQueueMessage("stop");
                adminQueue.AddMessage(message);
            }

            return "Crawling Stopped";
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string GetSysInfo()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable sysTable = tableClient.GetTableReference(sysInfoTableName);


            TableOperation retrieveOperation = TableOperation.Retrieve<ReportsTable>("allreports", "allreports");
            TableResult retrievedResult = sysTable.Execute(retrieveOperation);


            return new JavaScriptSerializer().Serialize(retrievedResult);
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string GetPageTitleAndBody(string url)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(PAGE_TABLE_NAME);

            string transformedUrl = url.Trim();
            transformedUrl = System.Uri.EscapeDataString(transformedUrl).Replace(" ", "");
            TableOperation retrieveOperation = TableOperation.Retrieve<CrawledSite>(transformedUrl, transformedUrl);
            TableResult retrievedResult = table.Execute(retrieveOperation);

            return new JavaScriptSerializer().Serialize(retrievedResult);
            //return transformedUrl;
        }
    }
}
