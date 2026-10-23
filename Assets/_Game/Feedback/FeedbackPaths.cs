using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Game.Feedback
{
    /// <summary>
    /// Where tickets go: feedback-tickets/ at the root of the game's git checkout (found up from the editor's or the
    /// build's data folder), or the game's persistent data folder when it runs from anywhere else. <c>-feedback-dir</c>
    /// on the command line puts them somewhere else (the smoke test's scratch folder).
    /// </summary>
    public static class FeedbackPaths
    {
        public const string FolderName = "feedback-tickets";

        static string _repoRoot, _tickets;
        static bool _looked;

        /// <summary>Call once on the main thread (Application's paths are main-thread only).</summary>
        public static void Init()
        {
            if (_looked) return;
            _looked = true;
            for (DirectoryInfo dir = new DirectoryInfo(Application.dataPath); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    _repoRoot = dir.FullName;
                    break;
                }
            _tickets = Argument("-feedback-dir")
                       ?? (_repoRoot != null ? Path.Combine(_repoRoot, FolderName) : Path.Combine(Application.persistentDataPath, FolderName));
        }

        /// <summary>The game's git checkout, or null when it runs outside one.</summary>
        public static string RepoRoot
        {
            get
            {
                Init();
                return _repoRoot;
            }
        }

        public static string TicketsRoot
        {
            get
            {
                Init();
                return _tickets;
            }
        }

        public static bool HasFlag(string flag) => Environment.GetCommandLineArgs().Contains(flag);

        /// <summary>The command-line value after <paramref name="name"/>, or null.</summary>
        public static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    /// <summary>Runs a command-line tool (git, gh) and returns its exit code and output, without a console window.</summary>
    public static class Shell
    {
        public struct Result
        {
            public int Code;
            public string Output, Error;
            public bool Ok => Code == 0;
        }

        /// <summary>Runs <paramref name="exe"/>; a tool that isn't installed gives code -1 and says so in Error.</summary>
        public static Result Run(string exe, string workingDirectory, IEnumerable<string> args, int timeoutMs = 120000)
        {
            var info = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            // Never wait on a prompt nobody can see, and no colour codes in the output.
            info.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
            info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            info.EnvironmentVariables["NO_COLOR"] = "1";
            try
            {
                using (Process process = Process.Start(info))
                {
                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return new Result { Code = -2, Error = $"{exe} took longer than {timeoutMs / 1000} s" };
                    }
                    process.WaitForExit(); // flushes the redirected output
                    return new Result { Code = process.ExitCode, Output = output.ToString().Trim(), Error = error.ToString().Trim() };
                }
            }
            catch (Win32Exception e)
            {
                return new Result { Code = -1, Error = $"{exe} couldn't be run: {e.Message}" };
            }
        }

        /// <summary>One argument quoted for a Windows command line (as CommandLineToArgvW reads it); plain words as they are.</summary>
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var quoted = new StringBuilder("\"");
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }
                if (i == arg.Length)
                {
                    quoted.Append('\\', backslashes * 2); // before the closing quote, every backslash is doubled
                    break;
                }
                if (arg[i] == '"') quoted.Append('\\', backslashes * 2 + 1).Append('"');
                else quoted.Append('\\', backslashes).Append(arg[i]);
            }
            return quoted.Append('"').ToString();
        }
    }
}
