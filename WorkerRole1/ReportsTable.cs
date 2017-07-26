using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

class ReportsTable : TableEntity
{
    public ReportsTable(string crawlerState, int numUrls, string last10Urls, int queueSize, int indexSize, int memProcessInt, int cpuUsageInt, string lastTrieLine, int trieSize)
    {

        this.PartitionKey = "allreports";
        this.RowKey = "allreports";

        this.CrawlerState = crawlerState;
        this.NumUrls = numUrls.ToString();
        this.Last10Urls = last10Urls;
        this.QueueSize = queueSize.ToString();
        this.IndexSize = indexSize.ToString();
        this.MemProcess = memProcessInt.ToString();
        this.CpuUsage = cpuUsageInt.ToString();
        this.LastTrieLine = lastTrieLine;
        this.TrieSize = trieSize.ToString();
    }

    public ReportsTable() { }

    public string CrawlerState { get; set; }
    public string NumUrls { get; set; }
    public string Last10Urls { get; set; }
    public string QueueSize { get; set; }
    public string IndexSize { get; set; }
    public string MemProcess { get; set; }
    public string CpuUsage { get; set; }
    public string LastTrieLine { get; set; }
    public string TrieSize { get; set; }
}
