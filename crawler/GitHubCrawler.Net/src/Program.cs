/*
 * https://github.com/gitspirit/match
 * MIT licence
 * Needs refactoring and unit tests
 * 
 * Other ideas
 * * parallelize by owner first letter
 * * continuesly run if there is config setting RunInterval set to non-zero (mins to wait between runs)
 * * Cache needs to go into a separate lib, will be used in Indexer and Search
 */
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Octokit;

namespace GitHubCrawler
{
	class MainClass
	{
		public static void Main (string[] args)
		{
            Log.Write("Configuration:");

            string cvRepo = ConfigurationManager.AppSettings["cv_repo"];
            string jobsRepo = ConfigurationManager.AppSettings["jobs_repo"];

            Log.Write("cvRepo: " + cvRepo);
            Log.Write("jobsRepo: " + jobsRepo);

            var cache = new Cache();
            var creds = cache.LoadCredentials();

            if (creds == null)
            {
                // Store creds in the cache. Don't forget to gitignore the cache folder!
                Console.WriteLine();
                Console.WriteLine("Please enter your credentials for github account");
                Console.WriteLine("It will be stored in the cache and reused next time.");
                Console.WriteLine("GitHub login:");
                var login = Console.ReadLine();
                Console.WriteLine("Password:");
                var pass = string.Empty;
                char ch;
                while ((ch = (char)Console.Read()) != '\n')
                {
                    Console.Write('\b');
                    Console.Write('*');
                    pass += ch;
                }
                creds = cache.SaveCredentials(login, pass);
            }

            var github = new GitHubClient(new ProductHeaderValue(Const.Gitspirit));
            github.Credentials = new Credentials(creds.Item1, creds.Item2);

            var matchConfig = new Repo(github, Const.Gitspirit, Const.Repo.Match, 0).Process().Config;

            IList<string> cvRepos = cvRepo != string.Empty ? new List<string>(new[] { cvRepo }) : matchConfig.CvRepos;
            IList<string> jobsRepos = jobsRepo != string.Empty ? new List<string>(new[] { jobsRepo }) : matchConfig.JobsRepos;

            Process(github, cvRepos, cache);

            Process(github, jobsRepos, cache);

            Log.Write("Finished. Press any key to exit.", 1);
			Console.Read();
		}

        private static void Process(IGitHubClient github, IEnumerable<string> repos, Cache cache)
        {
            foreach (var repo in repos)
            {
                try
                {
                    var on = repo.Split(true);
                    new Repo(github, on.Item1, on.Item2, Const.Repo.MaxLevel, cache).Process();
                }
                catch (Exception ex)
                {
                    Log.Write(ex);
                }
            }

        }
	}

    public class Log
    {
        public static void Write(object msg, int linesBefore = 0, params string[] args)
        {
            for (var i = 0; i < linesBefore; i++)
            {
                Console.WriteLine();
            }
            Console.WriteLine(msg);
        }
    }

    public class Cache
    {
        private DirectoryInfo _root;

        public Cache()
        {
            _root = FindCache(new FileInfo(Assembly.GetExecutingAssembly().Location).Directory);
        }

        public DirectoryInfo Root { get { return _root; } }

        private DirectoryInfo FindCache(DirectoryInfo current)
        {
            if (current == null)
            {
                throw new ArgumentNullException("Cannot find cache directory.");
            }
            var dirs = current.GetDirectories("cache");
            if (dirs.Length == 1)
            {
                return dirs[0];
            }
            dirs = current.GetDirectories("crawler");
            if (dirs.Length == 1)
            {
                return current.CreateSubdirectory("cache");
            }
            return FindCache(current.Parent);
        }

        public virtual string GetRepoPath(string owner, string repo)
        {
            var sb = new StringBuilder(1000);
            sb.Append(_root.FullName);
            sb.Append(Const.DirectorySeparator);
            sb.Append(owner);
            sb.Append(Const.DirectorySeparator);
            sb.Append(repo);
            return sb.ToString();
        }

        public virtual string GetContentPath(string owner, string repo, string path)
        {
            var sb = new StringBuilder(1000);
            sb.Append(GetRepoPath(owner, repo));
            sb.Append(path.Replace("/", Const.DirectorySeparator));
            return sb.ToString();
        }

        public virtual bool HasContent(string path, string sha)
        {
            var dir = new DirectoryInfo(path);
            if (dir.Exists)
            {
                return dir.GetFiles(sha + ".json").FirstOrDefault() != null;
            }
            return false;
        }

        public virtual void Save(Posting posting, string path, string sha)
        {
            var dir = new DirectoryInfo(path);
            if (dir.Exists)
            {
                dir.GetFiles().ToList().ForEach(fi => fi.Delete());
            }
            string json = JsonConvert.SerializeObject(posting, Formatting.Indented);
            Directory.CreateDirectory(path);
            path += Path.DirectorySeparatorChar + sha + ".json";
            File.WriteAllText(path, json);
        }

        public virtual void Confirm(string owner, string repo, IEnumerable<string> paths)
        {
            var repoDir = new DirectoryInfo(GetRepoPath(owner, repo));
            if (!repoDir.Exists)
            {
                return;
            }
            var dirs = repoDir.GetDirectories("*", SearchOption.AllDirectories).ToList();
            // Sort alpabetically to start from the deepest 
            dirs.Sort((x, y) => x.FullName.CompareTo(y.FullName));
            for (var i = dirs.Count - 1; i >= 0; i--)
            {
                Log.Write("Confirming " + dirs[i]);
                var confirmed = false;
                foreach (var path in paths)
                {
                    if (dirs[i].FullName.Contains(path))
                    {
                        confirmed = true;
                        break;
                    }
                }
                // If path is not confirmed check if this is a perant dir of others
                if (!confirmed && dirs[i].GetDirectories().Length == 0)
                {
                    Log.Write("Deleting from cache " + dirs[i]);
                    Directory.Delete(dirs[i].FullName, true);
                }
            }
        }

        public Tuple<string, string> LoadCredentials()
        {
            var path = Path.Combine(_root.FullName, "cred.txt");
            if (!File.Exists(path))
            {
                return null;
            }
            var creds = File.ReadAllText(path).Split(Environment.NewLine);
            return new Tuple<string, string>(creds[0], creds[1]);
        }

        public Tuple<string, string> SaveCredentials(string login, string pass)
        {
            var path = Path.Combine(_root.FullName, "cred.txt");
            File.WriteAllLines(path, new[] { login, pass });
            return LoadCredentials();
        }
    }

    public static class Const
    {
        public static string Gitspirit = "gitspirit";

        public static class Repo
        {
            public const int MaxLevel = 5;
            public const string Cv = "cv";
            public const string Jobs = "jobs";
            public const string Match = "match";
        }

        public static class FileName
        {
            public const string Config = "gitspirit.json";
            public const string Content = ".md";
        }

        public static class ContentSection 
        {
            public const string TextExclude = "[t-]";
            public const string Payment = "[p]";
            public const string Location = "[l]";
            public const string Start = "[s]";
            public const string Keywords = "[k]";
            public const string KeywordsExclude = "[k-]";
            public const string Contact = "[c]";
        }

        public static string DirectorySeparator = System.IO.Path.DirectorySeparatorChar.ToString();
        public static string PathSeparator = "/";
    }

    public static class Util
    {
        public static string Fmt(this string value, params object[] args)
        {
            return string.Format(value, args);
        }

        public static Tuple<string, string> Split(this string value, bool isRepo)
        {
            var owner = value.Split(new[] { '/' })[0];
            var name = value.Split(new[] { '/' })[1];
            return new Tuple<string, string>(owner, name);
        }

        public static string[] Split(this string value, string sep)
        {
            return value.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

	public class Repo 
	{
        IGitHubClient _github;
        string _owner;
        string _name;
        int _maxLevel;
        Cache _cache;
        Config _config;
        List<string> _confirmedPaths = new List<string>();

        public IGitHubClient GitHub { get { return _github; } }
        public string Owner { get { return _owner; } }
        public string Name { get { return _name; } }
        public int MaxLevel { get { return _maxLevel; } }
        public Cache Cache { get { return _cache; } }
        public Config Config { get { return _config; } }
        public IList<string> ConfirmedPaths { get { return _confirmedPaths; } }

        public Repo(IGitHubClient github, string owner, string name, int maxLevel, Cache cache = null)
        {
            _github = github;
            _owner = owner;
            _name = name;
            _maxLevel = maxLevel;
            _cache = cache;
        }

		public Repo Process()
        {
            Log.Write("Entering {0}/{1}".Fmt(Owner, Name), 1);
            LoadTree("master");
            if (Cache != null)
            {
                // Delete unconfirmed paths
                Cache.Confirm(Owner, Name, ConfirmedPaths);
            }
            return this;
        }

        protected virtual void LoadTree(string sha, string path = "", int level = 0)
        {
            var tree = GetTree(sha).Result;
            ProcessTree(tree, path, level);
        }

        protected virtual void ProcessTree(ICollection<TreeItem> items, string treePath, int level)
		{
            foreach (var item in items)
            {
                // Process files first to find the config
                if (item.Type == TreeType.Blob)
                {
                    if (level == 0 && item.Path == Const.FileName.Config)
                    {
                        // Always read the actual config
                        var content = GetContent(treePath, item);
                        if (content != null && content is Config)
                        {
                            _config = (Config)content;
                        }
                    }
                    else if (level != 0 && _config != null && _config.Active)
                    {
                        var fullPath = treePath + "/" + item.Path;
                        var cachePath = string.Empty;
                        var cached = false;
                        if (Cache != null)
                        {
                            // Check if content is in the cache
                            cachePath = Cache.GetContentPath(Owner, Name, fullPath);
                            cached = Cache.HasContent(cachePath, item.Sha);
                        }
                        if (cached)
                        {
                            Log.Write(fullPath + " is cached");
                        }
                        else
                        {
                            // No content in the cache, download
                            Log.Write("Downloading " + fullPath);
                            var content = GetContent(fullPath, item);
                            if (content != null && content is Posting)
                            {
                                var posting = (Posting)content;
                                if (Cache != null)
                                {
                                    Log.Write("Caching " + fullPath);
                                    Cache.Save(posting, cachePath, item.Sha);
                                }
                            }
                        }
                        ConfirmedPaths.Add(fullPath);
                    }
                }
            }

            // At here we've parsed all files in the root dir
            // Stop here if there is no config or active==false
            if (level == 0 && (Config == null || !Config.Active))
            {
                Log.Write("Repo is inactive. Leaving.");
                return;
            }
                
            if (level == MaxLevel)
            {
                Log.Write("Finished at level {0}. Leaving.".Fmt(level));
                return;
            }

            foreach (var item in items)
            {
                if (item.Type == TreeType.Tree)
                {
                    var path = treePath + "/" + item.Path;
                    if (Config.ExcludedFolders.Contains(path.ToLower()))
                    {
                        Log.Write("Excluded " + path);
                        continue;
                    }
                    Log.Write("Processing " + path);
                    LoadTree(item.Sha, path, ++level);
                }
            }
		}

        protected virtual async Task<ICollection<TreeItem>> GetTree(string sha)
        {
            var resp = await _github.GitDatabase.Tree.Get(_owner, _name, sha);
            return resp.Tree;
        }

        protected virtual async Task<byte[]> GetBlob(string sha)
        {
            var blob = await _github.GitDatabase.Blob.Get(_owner, _name, sha);
            var contentBase64 = blob.Content;
            var content = Convert.FromBase64String(contentBase64);
            return content;
        }

        protected virtual Content GetContent(string fullPath, TreeItem item)
        {
            var blob = GetBlob(item.Sha).Result;
            var text = new UTF8Encoding().GetString(blob);

            if (item.Path.Equals(Const.FileName.Config))
            {
                var config = new Config();
                config.Init(_owner, _name, item.Sha, fullPath);
                config.Process(text);
                return config;
            }               
            else if (item.Path.EndsWith(Const.FileName.Content))
            {
                var post = new Posting();
                post.Init(_owner, _name, item.Sha, fullPath);
                post.Process(text);
                return post;
            }

            return null;
        }
	}

    public abstract class Content
    {
        public string Owner { get; set; }
        public string Repo { get; set; }
        public string Sha { get; set; }
        public string Path { get; set; }

        public virtual void Init(string owner, string repo, string sha, string path)
        {
            Owner = owner;
            Repo = repo;
            Sha = sha;
            Path = path;
        }

        public virtual void Process(string content)
        {
        }
    }

    public class Config : Content
    {
        protected MatchConfig MatchData { get; set; }

        private IList<string> _cvRepos;
        public IList<string> CvRepos { get { return _cvRepos; } }

        private IList<string> _jobsRepos;
        public IList<string> JobsRepos { get { return _jobsRepos; } }

        protected ContentConfig ContentData { get; set; }

        private bool _active;
        public bool Active { get { return _active; } }

        private IList<string> _excludedFolders;
        public IList<string> ExcludedFolders { get { return _excludedFolders; } }

        public override void Process(string content)
        {
            base.Process(content);
            var matchData = JsonConvert.DeserializeObject<MatchConfig>(content);
            if (matchData.match != null)
            {
                MatchData = matchData;

                if (MatchData != null && MatchData.match != null)
                {
                    _cvRepos = MatchData.match.cv_repos != null ? new List<string>(MatchData.match.cv_repos) : new List<string>();
                    _jobsRepos = MatchData.match.jobs_repos != null ? new List<string>(MatchData.match.jobs_repos) : new List<string>();
                }
                else
                {
                    _cvRepos = new List<string>();
                    _jobsRepos = new List<string>();
                }
            }
            var contentData = JsonConvert.DeserializeObject<ContentConfig>(content);
            if (contentData != null)
            {
                ContentData = contentData;

                if (ContentData != null && ContentData.cv != null)
                {
                    _active = ContentData.cv.active;
                }
                else if (ContentData != null && ContentData.jobs != null)
                {
                    _active = ContentData.jobs.active;
                }

                if (ContentData != null && ContentData.cv != null && ContentData.cv.excluded_folders != null)
                {
                    _excludedFolders = new List<string>(ContentData.cv.excluded_folders);
                }
                else if (ContentData != null && ContentData.jobs != null && ContentData.jobs.excluded_folders != null)
                {
                    _excludedFolders = new List<string>(ContentData.jobs.excluded_folders);
                }
                else
                {
                    _excludedFolders = new List<string>();
                }

                for (var i = 0; i < _excludedFolders.Count; i++)
                {
                    var path = _excludedFolders[i];
                    path = path.Replace("\\", Const.PathSeparator);
                    path = path.StartsWith(Const.PathSeparator) ? path : Const.PathSeparator + path;
                    path = path.EndsWith(Const.PathSeparator) ? path.Substring(0, path.Length - 1) : path;
                    _excludedFolders[i] = path.ToLower();
                }
            }
        }
    }

    public class Posting : Content
    {
        public string Text { get; set; }
        public string TextExclude { get; set; }
        public string Payment { get; set; }
        public string Location { get; set; }
        public string Start { get; set; }
        public string Keywords { get; set; }
        public string KeywordsExclude { get; set; }
        public string Contact { get; set; }

        public override void Process(string content)
        {
            base.Process(content);
            Text = ReadText(content);
            TextExclude = ReadSection(content, Const.ContentSection.TextExclude);
            Payment = ReadSection(content, Const.ContentSection.Payment);
            Location = ReadSection(content, Const.ContentSection.Location);
            Start = ReadSection(content, Const.ContentSection.Start);
            Keywords = ReadSection(content, Const.ContentSection.Keywords);
            KeywordsExclude = ReadSection(content, Const.ContentSection.KeywordsExclude);
            Contact = ReadSection(content, Const.ContentSection.Contact);
        }

        protected virtual string ReadUntilNextSectionOrEnd(StringReader r)
        {
            var sb = new StringBuilder(1000);
            string line;
            while ((line = r.ReadLine()) != null && !line.Trim().StartsWith("["))
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        protected virtual bool FindSection(StringReader r, string section)
        {
            string line;
            while ((line = r.ReadLine()) != null && line.IndexOf(section) < 0)
            {
            }
            return line != null;
        }

        protected virtual string ReadText(string content)
        {
            var r = new StringReader(content);
            return ReadUntilNextSectionOrEnd(r);
        }

        protected virtual string ReadSection(string content, string section)
        {
            var r = new StringReader(content);
            if (FindSection(r, section))
                return ReadUntilNextSectionOrEnd(r);
            return string.Empty;
        }
    }

    public class ContentConfig
    {
        public class Settings
        {
            public bool active { get; set; }
            public string[] excluded_folders { get; set; }
        }

        public Settings cv { get; set; }
        public Settings jobs { get; set; }
    }

    public class MatchConfig
    {
        public class Settings
        {
            public string[] cv_repos { get; set; }
            public string[] jobs_repos { get; set; }
        }

        public Settings match { get; set; }
    }
}