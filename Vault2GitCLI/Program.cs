using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Windows.Forms;
using Vault2Git.Lib;
using System.IO;

namespace Vault2Git.CLI
{
    static class Program
    {

        class Params
        {
            public int Limit { get; protected set; }
            public bool UseConsole { get; protected set; }
            public bool UseCapsLock { get; protected set; }
            public bool SkipEmptyCommits { get; protected set; }
            public bool IgnoreLabels { get; protected set; }
            public IEnumerable<string> Branches;
            public IEnumerable<string> Errors;

            protected Params()
            {
            }

            private const string _limitParam = "--limit=";
            private const string _branchParam = "--branch=";
            private const string _mapParam = "--map=";

            public static IDictionary<string, string> ParseMapping(string[] args) {
                var paths = ConfigurationManager.AppSettings["Convertor.Paths"];

                // if present, use the command line argument instead of the config
                // this way, the config settings set in Converter.Paths are overridden
                if (args.Any(n => n.StartsWith(_mapParam))) {
                    paths = args.First(n => n.StartsWith(_mapParam)).Substring(_mapParam.Length);
                }
                if (paths.Contains("~")) {
                    return paths.Split(';')
                        .ToDictionary(
                            pair =>
                                pair.Split('~')[1], pair => pair.Split('~')[0]
                            );
                } else {
                    // single folder to master - no branches involved
                    Dictionary<string, string> pairs = new Dictionary<string, string>();
                    pairs.Add("master", paths);
                    return pairs;
                }
            }

            public static Params Parse(string[] args, IEnumerable<string> gitBranches) {
                var errors = new List<string>();
                var branches = new List<string>();

                var p = new Params();
                foreach (var o in args) {
                    if (o.Equals("--console-output"))
                        p.UseConsole = true;
                    else if (o.Equals("--caps-lock"))
                        p.UseCapsLock = true;
                    else if (o.Equals("--skip-empty-commits"))
                        p.SkipEmptyCommits = true;
                    else if (o.Equals("--ignore-labels"))
                        p.IgnoreLabels = true;
                    else if (o.Equals("--help")) {
                        errors.Add("Usage: vault2git [options]");
                        errors.Add("options:");
                        errors.Add("   --help                  This screen");
                        errors.Add("   --console-output        Use console output (default=no output)");
                        errors.Add("   --caps-lock             Use caps lock to stop at the end of the cycle with proper finalizers (default=no caps-lock)");
                        errors.Add("   --branch=<branch>       Process only one branch from config. Branch name should be in git terms. Default=all branches from config");
                        errors.Add("   --map=<mappings>        Set vault folder to branch mappings");
                        errors.Add("   --limit=<n>             Max number of versions to take from Vault for each branch");
                        errors.Add("   --skip-empty-commits    Do not create empty commits in Git");
                        errors.Add("   --ignore-labels         Do not create Git tags from Vault labels");
                        errors.Add(string.Empty);
                        errors.Add("<mappings>:");
                        errors.Add("   format                  Format is <vault_folder>~master;<vault_folder>~<git_branch_name>.");
                        errors.Add("                           If only <vault_folder> is specficed, master is assumed.");
                    } else if (o.StartsWith(_limitParam)) {
                        var l = o.Substring(_limitParam.Length);
                        var max = 0;
                        if (int.TryParse(l, out max))
                            p.Limit = max;
                        else
                            errors.Add(string.Format("Incorrect limit ({0}). Use integer.", l));
                    } else if (o.StartsWith(_branchParam)) {
                        var b = o.Substring(_limitParam.Length);
                        if (gitBranches.Contains(b))
                            branches.Add(b);
                        else
                            errors.Add(string.Format("Unknown branch {0}. Use one specified in .config", b));
                    } else {
                        errors.Add(string.Format("Unknown option {0}", o));
                    }
                }
                p.Branches = 0 == branches.Count() 
                    ? gitBranches 
                    : branches;
                p.Errors = errors;    
                return p;
                }
            }
        

        private static bool _useCapsLock = false;
        private static bool _useConsole = false;
        private static bool _ignoreLabels = false;


        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        //[STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Vault2Git -- converting history from Vault repositories to Git");
            System.Console.InputEncoding = System.Text.Encoding.UTF8;

            //get configuration for branches
            var paths = ConfigurationManager.AppSettings["Convertor.Paths"];
            var pathPairs = Params.ParseMapping(args);

            //parse params
            var param = Params.Parse(args, pathPairs.Keys);

            //get count from param
            if (param.Errors.Count() > 0)
            {
                foreach (var e in param.Errors)
                    Console.WriteLine(e);
                return;
            }

            Console.WriteLine("   use Vault2Git --help to get additional info");

            _useConsole = param.UseConsole;
            _useCapsLock = param.UseCapsLock;
            _ignoreLabels = param.IgnoreLabels;

            var processor = new Vault2Git.Lib.Processor()
                                {
                                    WorkingFolder = Directory.GetCurrentDirectory(),
                                    GitCmd = ConfigurationManager.AppSettings["Convertor.GitCmd"],
                                    GitDomainName = ConfigurationManager.AppSettings["Git.DomainName"],
                                    VaultServer = ConfigurationManager.AppSettings["Vault.Server"],
                                    VaultRepository = ConfigurationManager.AppSettings["Vault.Repo"],
                                    VaultUser = ConfigurationManager.AppSettings["Vault.User"],
                                    VaultPassword = ConfigurationManager.AppSettings["Vault.Password"],
                                    Progress = ShowProgress,
                                    SkipEmptyCommits = param.SkipEmptyCommits
                                };


            processor.Pull
                (
                    pathPairs.Where(p => param.Branches.Contains(p.Key))
                    , 0 == param.Limit ? 999999999 : param.Limit
                );

            if (!_ignoreLabels)
                processor.CreateTagsFromLabels();

#if DEBUG
                        Console.WriteLine("Press ENTER");
                        Console.ReadLine();
#endif
        }

        static bool ShowProgress(long version, int ticks)
        {
            var timeSpan = TimeSpan.FromMilliseconds(ticks);
            if (_useConsole)
            {

                if (Processor.ProgressSpecialVersionInit == version)
                    Console.WriteLine("init took {0}", timeSpan);
                else if (Processor.ProgressSpecialVersionGc == version)
                    Console.WriteLine("gc took {0}", timeSpan);
                else if (Processor.ProgressSpecialVersionFinalize == version)
                    Console.WriteLine("finalization took {0}", timeSpan);
                else if (Processor.ProgressSpecialVersionTags == version)
                    Console.WriteLine("tags creation took {0}", timeSpan);
                else
                    Console.WriteLine("processing version {0} took {1}", version, timeSpan);
            }

            return _useCapsLock && Console.CapsLock; //cancel flag
        }
    }
}
