using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Hosting;
using System.Web.Script.Serialization;
using System.Web.Script.Services;
using System.Web.Services;

namespace WebRole1
{
    /// <summary>
    /// Summary description for Search
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class Search : System.Web.Services.WebService
    {

        private const string PAGE_TABLE_NAME = "crawledpagesdata";
        private const string SYS_INFO_TABLE_NAME = "sysinfo";


        private static string rawDataFile = HostingEnvironment.ApplicationPhysicalPath + "\\data\\enwiki-20131104-all-titles-in-ns0";
        private static string cleanDataFile = HostingEnvironment.ApplicationPhysicalPath + "\\data\\wikiData.txt";
        private static WikiTrie wikiTrie = new WikiTrie();
        private PerformanceCounter memProcess = new PerformanceCounter("Memory", "Available MBytes");

        private static Dictionary<string, string> cache = new Dictionary<string, string>();

        /// <summary>
        /// Blob downloader from Azure cloud storage
        /// </summary>
        /// <returns></returns>
        [WebMethod]
        public string GetBlob()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("StorageConnectionString"));
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("pa2blob");

            if (container.Exists())
            {
                CloudBlockBlob blockBlob = container.GetBlockBlobReference("wikiData.txt");
                using (var fileStream = System.IO.File.OpenWrite(cleanDataFile))
                {
                    blockBlob.DownloadToStream(fileStream);
                }
            }

            return "Download Complete";
        }

        /// <summary>
        /// Helper to see how much memory is being used
        /// </summary>
        /// <returns></returns>
        [WebMethod]
        public float GetAvailableMBytes()
        {
            float memUsage = memProcess.NextValue();
            return memUsage;
        }

        /// <summary>
        /// Loads titles into trie structure
        /// </summary>
        /// <returns></returns>
        [WebMethod]
        public string LoadData()
        {
            float memUsage = GetAvailableMBytes();
            int processedLines = 0;
            string lastLine = "";
            using (StreamReader sr = new StreamReader(cleanDataFile))
            {
                while (sr.EndOfStream == false && memUsage >= 20)
                {
                    processedLines++;
                    string line = sr.ReadLine();
                    wikiTrie.AddTitle(line);
                    lastLine = line;

                    if (processedLines >= 10000)
                    {
                        processedLines = 0;
                        memUsage = GetAvailableMBytes();
                    }
                }
            }

            int numWikiNodes = wikiTrie.GetNumNodes();

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable reportsTable = tableClient.GetTableReference(SYS_INFO_TABLE_NAME);

            if (!reportsTable.Exists()) 
            {
                reportsTable.CreateIfNotExists();
                ReportsTable currentState = new ReportsTable("", 0, "", 0, 0, 0, 0, lastLine, numWikiNodes);
                TableOperation insertOperation = TableOperation.InsertOrReplace(currentState);
                reportsTable.Execute(insertOperation);
            }
            else 
            { 
                TableOperation retrieveOperation = TableOperation.Retrieve<ReportsTable>("allreports", "allreports");
                TableResult retrievedResult = reportsTable.Execute(retrieveOperation);

                int memProcessInt = (int)memProcess.NextValue();
                int cpuUsageInt = Convert.ToInt32(((ReportsTable)retrievedResult.Result).CpuUsage);
                int urlQueueSize = Convert.ToInt32(((ReportsTable)retrievedResult.Result).QueueSize);
                int currentNumRows = Convert.ToInt32(((ReportsTable)retrievedResult.Result).IndexSize);
                int crawledUrls = Convert.ToInt32(((ReportsTable)retrievedResult.Result).NumUrls);
                string currentCrawlerState = ((ReportsTable)retrievedResult.Result).CrawlerState;
                string last10Urls = ((ReportsTable)retrievedResult.Result).Last10Urls;


                ReportsTable currentState = new ReportsTable(currentCrawlerState, crawledUrls, last10Urls, urlQueueSize, currentNumRows, memProcessInt, cpuUsageInt, lastLine, numWikiNodes);
                TableOperation insertOperation = TableOperation.InsertOrReplace(currentState);
                reportsTable.Execute(insertOperation);
            }

            return numWikiNodes.ToString();
        }

        /// <summary>
        /// Searcher for finding hits based off of a string
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string FindPrefix(string query)
        {
            List<string> results = new List<string>();
            if (wikiTrie.GetNumNodes() == 0)
            {
                results.Add("Sorry, our service is currently down.");
                return new JavaScriptSerializer().Serialize(results);
            }

            results = wikiTrie.SearchForPrefix(query);
            return new JavaScriptSerializer().Serialize(results);
        }

        /// <summary>
        /// Helper to sanitize raw data from wikipedia
        /// </summary>
        /// <returns></returns>
        [WebMethod]
        public string CleanData()
        {
            using (StreamReader sr = new StreamReader(rawDataFile))
            {
                using (StreamWriter sw = new StreamWriter(cleanDataFile))
                {
                    while (sr.EndOfStream == false)
                    {
                        string line = sr.ReadLine();
                        line = line.Replace("_", " ");
                        Regex pattern = new Regex("^([a-zA-Z]| )*$");
                        Match match = pattern.Match(line);

                        if (match.Success)
                        {
                            sw.WriteLine(line);
                        }
                    }
                }
            }

            return "All Processed";
        }

        /// <summary>
        /// Fetches the urls from storage, sorts them, and returns them as JSON
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string GetPageTitleAndBody(string query)
        {
            if (!cache.ContainsKey(query)) { 
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference(PAGE_TABLE_NAME);

                query = query.Trim();
                string[] queryWords = query.Split('+');
                List<CrawledSite> queryResultsList = new List<CrawledSite>();

                foreach (string queryWord in queryWords)
                {
                    string lowerQuery = queryWord.ToLower();
                    TableQuery<CrawledSite> tableQuery = new TableQuery<CrawledSite>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, lowerQuery));
                    List<CrawledSite> queryResults = table.ExecuteQuery(tableQuery).ToList();
                    queryResultsList.AddRange(queryResults);
                }

                //Dictionary<string, int> urlCounts = new Dictionary<string, int>();

                //foreach (CrawledSite oneResult in queryResultsList)
                //{
                //    string url = oneResult.Url;
                //    if (!urlCounts.ContainsKey(url))
                //        urlCounts.Add(url, 1);
                //    else
                //        urlCounts[url]++;
                //}

                var test = queryResultsList.GroupBy(x => x.Url).Select(x => new
                            {
                                Count = x.Count(),
                                Url = x.Key,
                                Title = x.First().Title,
                                Date = x.First().Date,
                            })
                            .OrderByDescending(x => x.Count);

                int total = test.Count();
            
                // int numSkip = (pageNum - 1) * 10;
                // test.Skip(numSkip).take(10);

                string returnJson = new JavaScriptSerializer().Serialize(test.Take(10));

                cache.Add(query, returnJson);

                return returnJson;
            }
            else
            {
                return cache[query];
            }
            //return new JavaScriptSerializer().Serialize(urlCounts.OrderByDescending(x => x.Value));
            //return transformedUrl;
        }
    }
}
