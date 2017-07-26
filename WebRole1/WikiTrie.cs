using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

/// <summary>
/// Wei Kong - Info 344
/// 
/// Trie class for handling effecient indexing and searching of strings
/// 
/// </summary>
public class WikiTrie
{
    private const int NUM_LETTERS = 27;
    private const string LETTER_DICTIONARY = " abcdefghijklmnopqrstuvwxyz";

    private Node root;
    private int numNodes;

    /// <summary>
    /// Simple constructor for the class
    /// </summary>
    public WikiTrie()
    {
        numNodes = 0;
        root = new Node();

    }

    /// <summary>
    /// Nested custom node class to serve as node objects
    /// </summary>
    public class Node
    {
        public List<string> nodeTitles = new List<string>();
        public Node[] child;
        public int numChildren = 0;
        public string title;
        public bool isConverted = false;

        public Node() { }

        public void convertNode()
        {
            child = new Node[NUM_LETTERS];
            nodeTitles = null;
            isConverted = true;
        }
    }

    /// <summary>
    /// Getter for numNodes
    /// (It's slightly verbose, but I found it to be clearer that way)
    /// </summary>
    /// <returns></returns>
    public int GetNumNodes()
    {
        return numNodes;
    }

    /// <summary>
    /// Method for adding titles for indexing
    /// </summary>
    /// <param name="raw"></param>
    public void AddTitle(string raw)
    {
        Regex pattern = new Regex("^([a-zA-Z]| )*$");
        Match match = pattern.Match(raw);

        if (match.Success)
        {
            string lower = raw.ToLower();
            root = AddTitle(raw, lower, root, 0, true);
        }
    }

    /// <summary>
    /// Helper recursive method for adding titles for indexing
    /// </summary>
    /// <param name="raw"></param>
    /// <param name="lower">Assumed to be lowercase string. Error if not.</param>
    /// <param name="node"></param>
    /// <returns></returns>
    public Node AddTitle(string raw, string lower, Node node, int depth, bool isNewTitle)
    {
        if (node == null) { node = new Node(); }
        if (node.isConverted)
        {
            string testString = raw.Substring(depth);
            if (testString.Length == 0)
            {
                if (node.title == null && isNewTitle) { numNodes++; }
                node.title = raw;

                return node;
            }
            else
            {
                lower = raw.ToLower();
                char ch0 = lower[depth];
                int index = LETTER_DICTIONARY.IndexOf(ch0);

                depth = depth + 1;
                //lower = raw.Substring(depth).ToLower();
                node.child[index] = AddTitle(raw, lower, node.child[index], depth, isNewTitle);
                depth = depth - 1;

                node.numChildren++;
                return node;
            }
        }
        else
        {
            if (node.nodeTitles.Contains(raw))
            {
                return node;
            }
            else 
            { 
                node.nodeTitles.Add(raw);
                if (isNewTitle) { numNodes++; }
                if (node.nodeTitles.Count > 5)
                {
                    List<string> holder = node.nodeTitles;
                    node.convertNode();
                    foreach (string title in holder)
                    {
                        node = AddTitle(title, title.ToLower(), node, depth, false);
                    }
                }
                return node;
            }
        }
    }

    /// <summary>
    /// Searches for a node given a string query
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public Node SearchForNode(string query)
    {
        string lower = query.ToLower();
        Node node = SearchForNode(root, lower);
        if (node == null) { return null; }

        return node;
    }

    /// <summary>
    /// Helper method to recursively search for a given string query
    /// </summary>
    /// <param name="node"></param>
    /// <param name="query"></param>
    /// <param name="lower">Assumed to be lowercase string, </param>
    /// <returns></returns>
    public Node SearchForNode(Node node, string lower)
    {
        if (node == null) { return null; }
        if (lower.Length == 0) { return node; }

        char ch0 = lower[0];
        int index = LETTER_DICTIONARY.IndexOf(ch0);
        lower = lower.Substring(1);

        if (node.child != null) 
        {
            return SearchForNode(node.child[index], lower);
        }
        else if (node.nodeTitles.Count > 0)
        {
            foreach (string title in node.nodeTitles)
            {
                if (title.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return node;
                }
            }

            return null;
        }
        else
        {
            return null;
        }
    }



    /// <summary>
    /// Searches for words with given prefix
    /// </summary>
    /// <param name="pre"></param>
    /// <returns>Returns an empty list on invalid entries</returns>
    public List<string> SearchForPrefix(string pre)
    {
        List<string> results = new List<string>();

        Regex pattern = new Regex("^([a-zA-Z]| )*$");
        Match match = pattern.Match(pre);
        if (!match.Success) { return results; }

        pre = pre.ToLower();
        Node node = SearchForNode(pre);
        if (node == null) { return results; }

        results = SearchForPrefix(node, results);
        return results;
    }

    /// <summary>
    /// Helper method for finding all the words given a tree node
    /// Stops searching at 10 results
    /// </summary>
    /// <param name="node"></param>
    /// <param name="results">Results are returned as a List<string> </param>
    /// <returns></returns>
    public List<string> SearchForPrefix(Node node, List<string> results)
    {
        if (node.title != null)
        {
            results.Add(node.title);
        }
        if (!node.isConverted && node.nodeTitles.Count > 0)
        {
            List<string> holder = node.nodeTitles;
            foreach (string title in holder)
            {
                if (results.Count < 10) 
                {
                    results.Add(title);
                }
            }
        }
        if (node.numChildren > 0)
        {
            if (node.isConverted) 
            { 
                for (int i = 0; i < node.child.Length; i++)
                {
                    if (node.child[i] != null)
                    {
                        SearchForPrefix(node.child[i], results);
                    }
                    if (results.Count == 10) { return results; }
                }
            }
        }

        return results;
    }
}