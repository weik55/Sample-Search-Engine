using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

class CrawledSite : TableEntity
{
    public CrawledSite(string key, string formatedUrl, string url, string title, string date, string body)
    {

        this.PartitionKey = key;
        this.RowKey = formatedUrl;

        this.Url = url;
        this.Title = title;
        this.Date = date;
        this.Body = body;
    }

    public CrawledSite() { }

    public string Url { get; set; }
    public string Title { get; set; }
    public string Date { get; set; }
    public string Body { get; set; }
}
