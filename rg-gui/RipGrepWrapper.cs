using CliWrap;
using CliWrap.EventStream;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace rg_gui
{
    public class RipGrepWrapper
    {
        public enum FileEncoding
        {
            Auto,
            GBK,
            Big5
        }

        public enum MaxFileSizeUnit
        {
            None,
            B,
            K,
            M,
            G
        }

        public class SearchParameters
        {
            public string StartPath { get; set; } = string.Empty;

            public IEnumerable<string> SearchStrings { get; set; } = Enumerable.Empty<string>();

            public string IncludePatterns { get; set; } = string.Empty;

            public string ExcludePatterns { get; set; } = string.Empty;

            public bool IncludeHiddenFiles { get; set; } = true;

            public bool IgnoreCase { get; set; } = true;

            public bool Recursive { get; set; } = true;

            public bool RegularExpression { get; set; } = true;

            public FileEncoding Encoding { get; set; } = FileEncoding.Auto;

            public int MaxFileSize { get; set; }

            public MaxFileSizeUnit MaxFileSizeUnit { get; set; } = MaxFileSizeUnit.None;
        }

        public class TermResult
        {
            public TermResult(int termIndex, Range range)
            {
                TermIndex = termIndex;
                Range = range;
            }

            public int TermIndex { get; }

            public Range Range { get; }
        }

        public class LineResult
        {
            public LineResult(string lineContent)
            {
                LineContent = lineContent;
                TermResults = new();
            }

            public string LineContent { get; }
            public ConcurrentBag<TermResult> TermResults { get; }
        }

        public readonly ConcurrentBag<(string path, string filename, int termIndex)> FilesFound = new();
        public readonly ConcurrentDictionary<(string path, string filename, int lineNumber), LineResult> FileResults = new();
        private int m_searchTermCount;

        public event EventHandler<(string path, string filename)>? FileFound;
        protected void RaiseFileFound(string path, string filename)
        {
            FileFound?.Invoke(this, (path, filename));
        }

        private readonly string m_ripGrepPath;

        public RipGrepWrapper(string ripGrepPath)
        {
            m_ripGrepPath = ripGrepPath;
        }

        public void Clear()
        {
            FilesFound.Clear();
            FileResults.Clear();
        }

        public async Task Search(SearchParameters searchParameters, CancellationToken cancellationToken)
        {
            var searchTasks = new List<Task>();

            m_searchTermCount = searchParameters.SearchStrings.Count();

            if (searchParameters.Encoding == FileEncoding.Auto)
            {
                searchParameters.Encoding = DetectEncoding(searchParameters.StartPath);
            }

            for (var i = 0; i < m_searchTermCount; i++)
            {
                searchTasks.Add(Search(searchParameters, cancellationToken, i));
            }

            await Task.WhenAll(searchTasks);
        }

        /// <summary>
        /// 取樣目錄中的前幾個檔案，偵測是否為 Big5 編碼
        /// </summary>
        private static FileEncoding DetectEncoding(string startPath)
        {
            try
            {
                var sampleFile = Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (sampleFile == null) return FileEncoding.Auto;

                var bytes = new byte[Math.Min(4096, new FileInfo(sampleFile).Length)];
                using (var stream = File.OpenRead(sampleFile))
                {
                    stream.Read(bytes, 0, bytes.Length);
                }

                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    return FileEncoding.Auto;

                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    return FileEncoding.Auto;

                var decoded = Encoding.UTF8.GetString(bytes);
                var replacementCount = 0;
                for (var i = 0; i < decoded.Length; i++)
                {
                    if (decoded[i] == '\uFFFD') replacementCount++;
                }

                if (replacementCount > bytes.Length / 100)
                    return FileEncoding.Big5;
            }
            catch
            {
            }

            return FileEncoding.Auto;
        }

        private async Task Search(SearchParameters searchParameters, CancellationToken cancellationToken, int termIndex)
        {
            const string fieldMatchSeparator = "\t";

            if (string.IsNullOrWhiteSpace(searchParameters.StartPath))
            {
                return;
            }

            var cmd = Cli.Wrap(m_ripGrepPath)
                .WithArguments(args =>
                {
                    args.Add("-uu");
                    args.Add("--no-heading");
                    args.Add("--line-number");
                    args.Add("--field-match-separator").Add(fieldMatchSeparator);

                    if (searchParameters.IgnoreCase)
                    {
                        args.Add("-i");
                    }

                    if (searchParameters.IncludeHiddenFiles)
                    {
                        args.Add("--hidden");
                    }

                    if (!searchParameters.Recursive)
                    {
                        args.Add("--max-depth=1");
                    }

                    if (!searchParameters.RegularExpression)
                    {
                        args.Add("--fixed-strings");
                    }

                    if (!string.IsNullOrWhiteSpace(searchParameters.IncludePatterns))
                    {
                        var includePatterns = GetSearchPatterns(searchParameters.IncludePatterns);
                        if (includePatterns.Any())
                        {
                            args.Add($"--iglob={string.Join(",", includePatterns)}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(searchParameters.ExcludePatterns))
                    {
                        var excludePatterns = GetSearchPatterns(searchParameters.ExcludePatterns);
                        if (excludePatterns.Any())
                        {
                            args.Add($"--iglob=!{string.Join(",", excludePatterns)}");
                        }
                    }

                    args.Add("--color");
                    args.Add("always");

                    if (searchParameters.Encoding != FileEncoding.Auto)
                    {
                        args.Add("-E");
                        args.Add(EncodingTypes[searchParameters.Encoding]);
                    }

                    if (searchParameters.MaxFileSizeUnit != MaxFileSizeUnit.None)
                    {
                        var unitSuffix = searchParameters.MaxFileSizeUnit != MaxFileSizeUnit.B
                            ? searchParameters.MaxFileSizeUnit.ToString()
                            : string.Empty;
                        args.Add($"--max-filesize={searchParameters.MaxFileSize}{unitSuffix}");
                    }

                    args.Add("--");

                    var searchString = searchParameters.SearchStrings.ElementAt(termIndex);
                    if (!string.IsNullOrWhiteSpace(searchString))
                    {
                        args.Add(searchString);
                    }

                    args.Add(searchParameters.StartPath);
                })
                .WithValidation(CommandResultValidation.None);
            
            try
            {
                await foreach (var cmdEvent in cmd.ListenAsync(Encoding.UTF8, cancellationToken))
                {
                    switch (cmdEvent)
                    {
                        case StandardOutputCommandEvent stdOut:
                            {
                                var result = stdOut.Text.Split(fieldMatchSeparator, 3);

                                if (result.Length == 3 &&
                                    !string.IsNullOrWhiteSpace(result[0]) &&
                                    !string.IsNullOrWhiteSpace(result[1]) &&
                                    !string.IsNullOrWhiteSpace(result[2]) &&
                                    int.TryParse(RemoveAnsiColors(result[1]), out int lineNumber)
                                    )
                                {
                                    var fullPath = RemoveAnsiColors(result[0]);
                                    var path = Path.GetDirectoryName(fullPath);
                                    var filename = Path.GetFileName(fullPath);

                                    if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(filename))
                                    {
                                        var alreadyReported = FilesFound.Any(x => x.path == path && x.filename == filename && x.termIndex != termIndex);
                                        if (!FilesFound.Contains((path, filename, termIndex)))
                                        {
                                            FilesFound.Add((path, filename, termIndex));
                                            if (!alreadyReported)
                                            {
                                                RaiseFileFound(path, filename);
                                            }
                                        }

                                        if (!FileResults.ContainsKey((path, filename, lineNumber)))
                                        {
                                            FileResults.GetOrAdd((path, filename, lineNumber), new LineResult(RemoveAnsiColors(result[2])));
                                        }

                                        var termMatches = GetTermMatches(result[2]);
                                        foreach (var termMatch in termMatches)
                                        {
                                            FileResults[(path, filename, lineNumber)].TermResults.Add(new TermResult(termIndex, termMatch));
                                        }
                                    }
                                }
                            }
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static readonly char[] PatternDelimiters = { ' ', ':', ';', ',' };

        private static readonly Dictionary<FileEncoding, string> EncodingTypes = new()
        {
            { FileEncoding.Auto, string.Empty },
            { FileEncoding.GBK, "GBK" },
            { FileEncoding.Big5, "big5" },
        };

        private static IEnumerable<string> GetSearchPatterns(string patternString)
        {
            var searchPatterns = new List<string>();
            var splitPatternString = patternString.Split(PatternDelimiters, StringSplitOptions.RemoveEmptyEntries);

            var invalidChars = Path.GetInvalidFileNameChars().Where(x => x != Path.DirectorySeparatorChar && x != '*').ToList();
            invalidChars.Add('{');
            invalidChars.Add('}');

            foreach (var token in splitPatternString)
            {
                var pattern = token;

                // Remove any invalid characters from patterns.
                foreach (var c in invalidChars)
                {
                    pattern = pattern.Replace(c.ToString(), string.Empty);
                }

                // Remove any whitespace from patterns.
                pattern = Regex.Replace(pattern, @"\s+", "");

                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    searchPatterns.Add(pattern);
                }
            }

            return searchPatterns;
        }

        private static string RemoveAnsiColors(string source)
        {
            return Regex.Replace(source, @"\x1B\[[^@-~]*[@-~]", string.Empty);
        }

        private static IList<Range> GetTermMatches(string source)
        {
            var ripGrepMatches = Regex.Matches(source, @"\x1B\[0m\x1B\[1m\x1B\[31m(.+?)\x1B\[0m");

            var termMatches = new List<Range>();

            var processIndex = 0;
            var originalStringIndex = 0;
            for (var i = 0; i < ripGrepMatches.Count; i++)
            {
                if (processIndex != ripGrepMatches[i].Groups[0].Index)
                {
                    originalStringIndex += (ripGrepMatches[i].Groups[0].Index - processIndex);
                }

                var start = originalStringIndex;
                originalStringIndex += ripGrepMatches[i].Groups[1].Value.Length;
                termMatches.Add(new Range(start, originalStringIndex - 1));
                processIndex = ripGrepMatches[i].Index + ripGrepMatches[i].Length;
            }

            return termMatches;
        }
    }
}
