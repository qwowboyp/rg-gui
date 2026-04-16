using FramePFX.Themes;
using Ookii.Dialogs.Wpf;
using Peter;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using static rg_gui.RipGrepWrapper;

namespace rg_gui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double DEFAULT_MAINWINDOW_LEFT = 0;
        private const double DEFAULT_MAINWINDOW_TOP = 0;
        private const double DEFAULT_MAINWINDOW_WIDTH = 800;
        private const double DEFAULT_MAINWINDOW_HEIGHT = 450;
        private const int DEFAULT_MAINWINDOW_STATE = 0;

        private const string DEFAULT_BASEPATH = "";
        private const string DEFAULT_INCLUDEFILES = "";
        private const string DEFAULT_EXCLUDEFILES = "";
        private const string DEFAULT_CONTAININGTEXT = "";

        private const bool DEFAULT_CASESENSITIVE = false;
        private const bool DEFAULT_RECURSIVE = true;
        private const bool DEFAULT_REGULAREXPRESSION = false;

        private const string DEFAULT_FILEENCODING = "Auto";

        private const int DEFAULT_MAXFILESIZE = 0;
        private const string DEFAULT_MAXFILESIZEUNIT = "None";

        private const int DEFAULT_CONTEXTLINES = 0;

        private const int DEFAULT_MAXSEARCHTERMS = 10;

        private const int HIGHLIGHT_COLORS_COUNT = 4;

        private const double GRID_SPLITTER_WIDTH = 5.0;

        private string m_currentInput = string.Empty;
        private string? m_currentSuggestion = string.Empty;
        private string m_currentText = string.Empty;
        private int m_selectionStart;
        private int m_selectionLength;
        private IEnumerable<string> m_folderSuggestionValues = Enumerable.Empty<string>();

        private int m_maxSearchTerms;
        private int m_lastContextLines = 0;

        private const ThemeType DEFAULT_THEME = ThemeType.Dark; // opqlo [佈景主題]-預設深色模式
        private ThemeType m_currentTheme;

        private const bool DEFAULT_MULTIPLEHIGHLIGHTCOLORS = true;
        private bool m_multipleHighlightColors = DEFAULT_MULTIPLEHIGHLIGHTCOLORS;
        private bool m_sortByDate = false; // opqlo [排序模式]-false=依名稱 true=依修改日期

        public class FileSearchResult
        {
            public string Path { get; } // opqlo [搜尋結果]-檔案所在路徑
            public string Filename { get; } // opqlo [搜尋結果]-檔案名稱
            public DateTime LastWriteTime { get; } // opqlo [搜尋結果]-最後修改時間

            public FileSearchResult(string path, string filename, DateTime lastWriteTime)
            {
                Path = path;
                Filename = filename;
                LastWriteTime = lastWriteTime;
            }
        }

        public class ResultLine
        {
            public int Line { get; }

            public string Content { get; }

            public ResultLine(int line, string content)
            {
                Line = line;
                Content = content;
            }
        }

        private CancellationTokenSource? m_cancellationTokenSource;

        private readonly RipGrepWrapper m_ripGrepWrapper;

        public RangeObservableCollection<FileSearchResult> FileResultItems { get; } = new();
        public RangeObservableCollection<ResultLine> ResultLineItems { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            Left = double.TryParse(config.AppSettings.Settings["MainWindowLeft"]?.Value, out var left) ? left : DEFAULT_MAINWINDOW_LEFT;
            Top = double.TryParse(config.AppSettings.Settings["MainWindowTop"]?.Value, out var top) ? top : DEFAULT_MAINWINDOW_TOP;
            Width = double.TryParse(config.AppSettings.Settings["MainWindowWidth"]?.Value, out var width) ? width : DEFAULT_MAINWINDOW_WIDTH;
            Height = double.TryParse(config.AppSettings.Settings["MainWindowHeight"]?.Value, out var height) ? height : DEFAULT_MAINWINDOW_HEIGHT;
            WindowState = int.TryParse(config.AppSettings.Settings["MainWindowState"]?.Value, out var windowState) ? WindowState : DEFAULT_MAINWINDOW_STATE;

            txtBasePath.Text = config.AppSettings.Settings["BasePath"]?.Value ?? DEFAULT_BASEPATH;
            txtIncludeFiles.Text = config.AppSettings.Settings["IncludeFiles"]?.Value ?? DEFAULT_INCLUDEFILES;
            txtExcludeFiles.Text = config.AppSettings.Settings["ExcludeFiles"]?.Value ?? DEFAULT_EXCLUDEFILES;
            txtContainingText.Text = config.AppSettings.Settings["ContainingText"]?.Value ?? DEFAULT_CONTAININGTEXT;
            chkCaseSensitive.IsChecked = bool.TryParse(config.AppSettings.Settings["CaseSensitive"]?.Value, out var caseSensitive) ? caseSensitive : DEFAULT_CASESENSITIVE;
            chkRecursive.IsChecked = bool.TryParse(config.AppSettings.Settings["Recursive"]?.Value, out var recursive) ? recursive : DEFAULT_RECURSIVE;

            var gridFileResultsWidthStr = config.AppSettings.Settings["GridFileResultsWidth"]?.Value;
            var gridSplitterWidthStr = config.AppSettings.Settings["GridSplitterWidth"]?.Value;
            var gridResultLinesWidthStr = config.AppSettings.Settings["GridResultLinesWidth"]?.Value;

            var gridLengthConverter = new GridLengthConverter();

            if (gridFileResultsWidthStr != null && gridSplitterWidthStr != null && gridResultLinesWidthStr != null)
            {
                var gridFileResultsWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridFileResultsWidthStr);
                var gridSplitterWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridSplitterWidthStr);
                var gridResultLinesWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridResultLinesWidthStr);

                // Sanity check the column widths before restoring them.
                if (gridFileResultsWidth != null && gridSplitterWidth != null && gridResultLinesWidth != null &&
                    (gridFileResultsWidth.Value.Value + gridSplitterWidth.Value.Value + gridResultLinesWidth.Value.Value) < Width &&
                    gridSplitterWidth.Value.Value == GRID_SPLITTER_WIDTH)
                {
                    gridResults.ColumnDefinitions[0].Width = (GridLength)gridFileResultsWidth;
                    gridResults.ColumnDefinitions[1].Width = (GridLength)gridSplitterWidth;
                    gridResults.ColumnDefinitions[2].Width = (GridLength)gridResultLinesWidth;
                }
            }

            var fileEncoding = cmbEncoding.FindName(config.AppSettings.Settings["FileEncoding"]?.Value ?? DEFAULT_FILEENCODING);
            if (fileEncoding != null)
            {
                cmbEncoding.SelectedItem = fileEncoding;
            }
            else
            {
                cmbEncoding.SelectedIndex = 0;
            }

            txtMaxFileSize.Text = (int.TryParse(config.AppSettings.Settings["MaxFileSize"]?.Value, out var maxFileSize) ? maxFileSize : DEFAULT_MAXFILESIZE).ToString();
            var maxFileSizeUnit = cmbFileSizeUnit.FindName(config.AppSettings.Settings["MaxFileSizeUnit"]?.Value ?? DEFAULT_MAXFILESIZEUNIT);
            if (maxFileSizeUnit != null)
            {
                cmbFileSizeUnit.SelectedItem = maxFileSizeUnit;
            }
            else
            {
                cmbFileSizeUnit.SelectedIndex = 0;
            }

            txtContextLines.Text = (int.TryParse(config.AppSettings.Settings["ContextLines"]?.Value, out var contextLines) ? contextLines : DEFAULT_CONTEXTLINES).ToString();

            m_currentTheme = Enum.TryParse<ThemeType>(config.AppSettings.Settings["Theme"]?.Value, out var themeName) ? themeName : DEFAULT_THEME;
            ThemesController.SetTheme(m_currentTheme);

            var ripgrepPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty, "rg.exe");
            if (!File.Exists(ripgrepPath))
            {
                MessageBox.Show("安裝路徑中找不到 rg.exe。", "錯誤");
                throw new Exception("安裝路徑中找不到 rg.exe。");
            }

            m_maxSearchTerms = int.TryParse(config.AppSettings.Settings["MaxSearchTerms"]?.Value, out var maxSearchTerms) ? maxSearchTerms : DEFAULT_MAXSEARCHTERMS;
            m_multipleHighlightColors = bool.TryParse(config.AppSettings.Settings["MultipleHighlightColors"]?.Value, out var multipleHighlightColors) ? multipleHighlightColors : DEFAULT_MULTIPLEHIGHLIGHTCOLORS;

            m_ripGrepWrapper = new RipGrepWrapper(ripgrepPath);
            m_ripGrepWrapper.FileFound += OnFileAdded;
        }

        private static void SetConfigValue(Configuration config, string key, string value)
        {
            if (config.AppSettings.Settings[key] != null)
            {
                config.AppSettings.Settings[key].Value = value;
            }
            else
            {
                config.AppSettings.Settings.Add(key, value);
            }
        }

        private void OnClosing(object? sender, EventArgs e)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if (WindowState != WindowState.Minimized)
            {
                SetConfigValue(config, "MainWindowLeft", Left.ToString());
                SetConfigValue(config, "MainWindowTop", Top.ToString());
                SetConfigValue(config, "MainWindowWidth", Width.ToString());
                SetConfigValue(config, "MainWindowHeight", Height.ToString());
                SetConfigValue(config, "MainWindowState", ((int)WindowState).ToString());

                var gridLengthConverter = new GridLengthConverter();
                var gridFileResultsWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[0].Width);
                var gridSplitterWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[1].Width);
                var gridResultLinesWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[2].Width);

                if (gridFileResultsWidthStr != null && gridSplitterWidthStr != null && gridResultLinesWidthStr != null)
                {
                    SetConfigValue(config, "GridFileResultsWidth", gridFileResultsWidthStr);
                    SetConfigValue(config, "GridSplitterWidth", gridSplitterWidthStr);
                    SetConfigValue(config, "GridResultLinesWidth", gridResultLinesWidthStr);
                }
                else
                {
                    // Unable to get the current values for some reason.  Save default values instead.
                    SetConfigValue(config, "GridFileResultsWidth", "*");
                    SetConfigValue(config, "GridSplitterWidth", GRID_SPLITTER_WIDTH.ToString("N0"));
                    SetConfigValue(config, "GridResultLinesWidth", "*");
                }
            }

            SetConfigValue(config, "BasePath", txtBasePath.Text);
            SetConfigValue(config, "IncludeFiles", txtIncludeFiles.Text);
            SetConfigValue(config, "ExcludeFiles", txtExcludeFiles.Text);
            SetConfigValue(config, "ContainingText", txtContainingText.Text);
            SetConfigValue(config, "CaseSensitive", (chkCaseSensitive.IsChecked ?? DEFAULT_CASESENSITIVE).ToString());
            SetConfigValue(config, "Recursive", (chkRecursive.IsChecked ?? DEFAULT_RECURSIVE).ToString());
            SetConfigValue(config, "RegularExpression", (chkRegularExpression.IsChecked ?? DEFAULT_REGULAREXPRESSION).ToString());

            SetConfigValue(config, "FileEncoding", ((ComboBoxItem)cmbEncoding.SelectedItem).Name);
            SetConfigValue(config, "MaxFileSize", txtMaxFileSize.Text);
            SetConfigValue(config, "MaxFileSizeUnit", ((ComboBoxItem)cmbFileSizeUnit.SelectedItem).Name);
            SetConfigValue(config, "ContextLines", txtContextLines.Text);
            SetConfigValue(config, "Theme", m_currentTheme.ToString());
            SetConfigValue(config, "MultipleHighlightColors", m_multipleHighlightColors.ToString());
            config.Save();

            ConfigurationManager.RefreshSection("appSettings");
        }

        private void OnFileAdded(object? sender, (string path, string filename) result)
        {
            Application.Current.Dispatcher.Invoke(delegate
            {
                // Ensure the same result won't be added multiple times.
                if (!FileResultItems.Any(x => x.Path == result.path && x.Filename == result.filename))
                {
                    var fullPath = System.IO.Path.Combine(result.path, result.filename);
                    var lastWriteTime = File.Exists(fullPath) ? File.GetLastWriteTime(fullPath) : DateTime.MinValue;
                    FileResultItems.Add(new FileSearchResult(result.path, result.filename, lastWriteTime));
                    txtFileListStatus.Text = $"已找到 {FileResultItems.Count} 個檔案。";
                }
            });
        }

        private void gridFileResults_MouseDown(object? sender, MouseEventArgs e)
        {
            if ((e.RightButton == MouseButtonState.Pressed && !SystemParameters.SwapButtons) || (e.LeftButton == MouseButtonState.Pressed && SystemParameters.SwapButtons))
            {
                SelectRowUnderMouse(e);

                var selectedFiles = GetSelectedFiles();

                if (selectedFiles.Any())
                {
                    var point = PointToScreen(e.MouseDevice.GetPosition(this));
                    ShowFileResultsContextMenu(selectedFiles, new System.Drawing.Point((int)point.X, (int)point.Y));
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 選取滑鼠右鍵點到的檔案列。
        /// </summary>
        private void SelectRowUnderMouse(MouseEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject originalSource)
            {
                return;
            }

            var row = FindAncestor<DataGridRow>(originalSource);
            if (row == null || row.IsSelected)
            {
                return;
            }

            gridFileResults.SelectedItems.Clear();
            gridFileResults.SelectedItem = row.Item;
            row.IsSelected = true;
            row.Focus();
        }

        /// <summary>
        /// 往上尋找指定型別的視覺樹父節點。
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject current)
            where T : DependencyObject
        {
            while (true)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null)
                {
                    return null;
                }

                current = parent;
            }
        }

        /// <summary>
        /// 取得目前已選取的檔案清單。
        /// </summary>
        private List<FileInfo> GetSelectedFiles()
        {
            var selectedFiles = new List<FileInfo>();

            foreach (var selectedItem in gridFileResults.SelectedItems)
            {
                if (selectedItem is FileSearchResult fileSearchResult)
                {
                    selectedFiles.Add(new FileInfo(Path.Combine(fileSearchResult.Path, fileSearchResult.Filename)));
                }
            }

            return selectedFiles;
        }

        /// <summary>
        /// 顯示檔案結果的自訂右鍵選單。
        /// </summary>
        private void ShowFileResultsContextMenu(IReadOnlyList<FileInfo> selectedFiles, System.Drawing.Point screenPoint)
        {
            var contextMenu = new ContextMenu
            {
                PlacementTarget = gridFileResults,
                Placement = PlacementMode.MousePoint,
            };

            var openFileMenuItem = new MenuItem
            {
                Header = "開啟",
            };
            openFileMenuItem.Click += (_, _) => OpenSelectedFile(selectedFiles);

            var editFileMenuItem = new MenuItem
            {
                Header = "編輯",
            };
            editFileMenuItem.Click += (_, _) => EditSelectedFile(selectedFiles);

            var copyPathMenuItem = new MenuItem
            {
                Header = "複製路徑",
            };
            copyPathMenuItem.Click += (_, _) => CopySelectedPaths(selectedFiles);

            var copyFileNameMenuItem = new MenuItem
            {
                Header = "複製檔名",
            };
            copyFileNameMenuItem.Click += (_, _) => CopySelectedFileNames(selectedFiles);

            var openFileLocationMenuItem = new MenuItem
            {
                Header = "開啟檔案位置",
            };
            openFileLocationMenuItem.Click += (_, _) => OpenSelectedFileLocation(selectedFiles);

            var shellContextMenuItem = new MenuItem
            {
                Header = "Windows 系統選單...",
            };
            shellContextMenuItem.Click += (_, _) => ShowShellContextMenu(selectedFiles, screenPoint);

            var sortMenuItem = new MenuItem
            {
                Header = m_sortByDate ? "排序：依修改日期 ▼" : "排序：依名稱 ▼",
            };
            sortMenuItem.Click += (_, _) => ToggleSortMode();

            contextMenu.Items.Add(openFileMenuItem);
            contextMenu.Items.Add(editFileMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(copyPathMenuItem);
            contextMenu.Items.Add(copyFileNameMenuItem);
            contextMenu.Items.Add(openFileLocationMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(sortMenuItem);
            contextMenu.Items.Add(shellContextMenuItem);

            contextMenu.IsOpen = true;
        }

        private void ToggleSortMode()
        {
            m_sortByDate = !m_sortByDate;
            ApplyFileSort();
        }

        private void ApplyFileSort()
        {
            var view = (CollectionView)CollectionViewSource.GetDefaultView(gridFileResults.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();
            if (m_sortByDate)
            {
                view.SortDescriptions.Add(new SortDescription("LastWriteTime", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription("Filename", ListSortDirection.Ascending));
            }
            else
            {
                view.SortDescriptions.Add(new SortDescription("Path", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Filename", ListSortDirection.Ascending));
            }
        }

        /// <summary>
        /// 以預設程式開啟選取的檔案。
        /// </summary>
        private static void OpenSelectedFile(IEnumerable<FileInfo> selectedFiles)
        {
            foreach (var file in selectedFiles)
            {
                Process.Start(new ProcessStartInfo(file.FullName)
                {
                    UseShellExecute = true,
                });
            }
        }

        /// <summary>
        /// 以 Notepad 編輯第一個選取的檔案。
        /// </summary>
        private static void EditSelectedFile(IEnumerable<FileInfo> selectedFiles)
        {
            var firstSelectedFile = selectedFiles.FirstOrDefault();
            if (firstSelectedFile == null)
            {
                return;
            }

            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{firstSelectedFile.FullName}\"")
            {
                UseShellExecute = true,
            });
        }

        /// <summary>
        /// 將選取檔案的完整路徑複製到剪貼簿。
        /// </summary>
        private static void CopySelectedPaths(IEnumerable<FileInfo> selectedFiles)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, selectedFiles.Select(x => x.FullName)));
        }

        /// <summary>
        /// 將選取檔案名稱複製到剪貼簿。
        /// </summary>
        private static void CopySelectedFileNames(IEnumerable<FileInfo> selectedFiles)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, selectedFiles.Select(x => x.Name)));
        }

        /// <summary>
        /// 用檔案總管開啟第一個選取檔案的位置。
        /// </summary>
        private static void OpenSelectedFileLocation(IEnumerable<FileInfo> selectedFiles)
        {
            var firstSelectedFile = selectedFiles.FirstOrDefault();
            if (firstSelectedFile == null)
            {
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{firstSelectedFile.FullName}\"")
            {
                UseShellExecute = true,
            });
        }

        /// <summary>
        /// 顯示原本的 Windows Shell 系統選單。
        /// </summary>
        private static void ShowShellContextMenu(IEnumerable<FileInfo> selectedFiles, System.Drawing.Point screenPoint)
        {
            var shellContextMenu = new ShellContextMenu();
            shellContextMenu.ShowContextMenu(selectedFiles, screenPoint);
        }

        private void gridFileResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                if (e.AddedItems[0] is FileSearchResult addedItem)
                {
                    // Scroll gridResultLines back to left end.
                    GetScrollViewer(gridResultLines)?.ScrollToLeftEnd();

                    ResultLineItems.Reset(Enumerable.Empty<ResultLine>());

                    var lineResults = m_ripGrepWrapper.FileResults.Where(x => x.Key.path == addedItem.Path && x.Key.filename == addedItem.Filename);

                    foreach (var lineResult in lineResults)
                    {
                        ResultLineItems.Add(new ResultLine(lineResult.Key.lineNumber, GetColorizedString(lineResult.Value.LineContent, lineResult.Value.TermResults).Trim()));
                    }

                    txtResultLineStatus.Text = $"符合 {ResultLineItems.Count} 行。";
                    txtFilePathStatus.Text = System.IO.Path.Join(addedItem.Path, addedItem.Filename); // opqlo [狀態列路徑]-顯示選取檔案完整路徑
                }
            }
        }

        private void grid_RequestBringIntoViewHandler(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private static ScrollViewer? GetScrollViewer(UIElement? element)
        {
            if (element == null)
            {
                return null;
            }

            ScrollViewer? result = null;
            for (var i = 0; result == null && i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is ScrollViewer scrollViewer)
                {
                    result = scrollViewer;
                }
                else
                {
                    result = GetScrollViewer(child as UIElement);
                }
            }
            return result;
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if ((txtBasePath.Text.IndexOfAny(Path.GetInvalidPathChars()) != -1) || !Directory.Exists(txtBasePath.Text))
            {
                MessageBox.Show("無效的「搜尋資料夾」路徑。", "錯誤");
                return;
            }

            await RunSearch(null, null);
        }

        /// <summary>
        /// 執行搜尋。傳入欲恢復焦點的檔案與行號，為 null 則不恢復。
        /// </summary>
        private async Task RunSearch(string? restorePath, string? restoreFilename)
        {
            if (m_cancellationTokenSource != null)
            {
                return;
            }

            var searchTerms = Regex.Matches(txtContainingText.Text, @"""[^""\\]*(?:\\.[^""\\]*)*""|([^\s])+|[^\s""]+");
            if (searchTerms.Count < 1)
            {
                return;
            }

            if (m_maxSearchTerms < 1)
            {
                m_maxSearchTerms = 1;
            }

            if (searchTerms.Count > m_maxSearchTerms)
            {
                MessageBox.Show($"搜尋內容包含超過 {m_maxSearchTerms} 個詞。");
                return;
            }

            int? restoreLine = null;
            if (restorePath != null && restoreFilename != null && gridResultLines.SelectedItem is ResultLine selectedLine)
            {
                restoreLine = selectedLine.Line;
            }

            var stopwatch = Stopwatch.StartNew();
            btnStart.IsEnabled = false;
            btnCancel.IsEnabled = true;
            btnSettings.IsEnabled = false;
            var cancellationTokenSource = new CancellationTokenSource();
            m_cancellationTokenSource = cancellationTokenSource;

            ResultLineItems.Reset(Enumerable.Empty<ResultLine>());
            txtFileListStatus.Text = string.Empty;
            txtResultLineStatus.Text = string.Empty;
            txtFilePathStatus.Text = string.Empty; // opqlo [狀態列路徑]-搜尋開始時清除路徑

            m_ripGrepWrapper.Clear();

            var startPath = txtBasePath.Text;
            if (startPath.EndsWith(Path.DirectorySeparatorChar))
            {
                startPath = startPath.TrimEnd(Path.DirectorySeparatorChar);
            }

            try
            {
                var searchParameters = new SearchParameters
                {
                    StartPath = startPath,
                    SearchStrings = searchTerms.Cast<Match>().Select(x => x.Value),
                    IgnoreCase = !(chkCaseSensitive.IsChecked ?? false),
                    Recursive = chkRecursive.IsChecked ?? true,
                    IncludePatterns = txtIncludeFiles.Text,
                    ExcludePatterns = txtExcludeFiles.Text,
                    RegularExpression = chkRegularExpression.IsChecked ?? false,
                    Encoding = (FileEncoding)cmbEncoding.SelectedIndex,
                    MaxFileSize = int.Parse(txtMaxFileSize.Text),
                    MaxFileSizeUnit = (MaxFileSizeUnit)cmbFileSizeUnit.SelectedIndex,
                    ContextLines = int.TryParse(txtContextLines.Text, out var ctx) ? ctx : 0,
                };

                FileResultItems.Reset(Enumerable.Empty<FileSearchResult>());

                await m_ripGrepWrapper.Search(searchParameters, cancellationTokenSource.Token);
            }
            finally
            {
                btnCancel.IsEnabled = false;
                btnStart.IsEnabled = true;
                btnSettings.IsEnabled = true;

                m_cancellationTokenSource = null;
                cancellationTokenSource.Cancel();
            }

            stopwatch.Stop();
            txtFileListStatus.Text = $"已找到 {FileResultItems.Count} 個檔案。耗時 {stopwatch.Elapsed.TotalSeconds:0.00} 秒。";

            if (restorePath != null && restoreFilename != null)
            {
                RestoreSelection(restorePath, restoreFilename, restoreLine);
            }
        }

        /// <summary>
        /// 搜尋完成後恢復之前的選取狀態。
        /// </summary>
        private void RestoreSelection(string restorePath, string restoreFilename, int? restoreLine)
        {
            var fileItem = FileResultItems.FirstOrDefault(x => x.Path == restorePath && x.Filename == restoreFilename);
            if (fileItem != null)
            {
                gridFileResults.SelectedItem = fileItem;
                gridFileResults.ScrollIntoView(fileItem);

                if (restoreLine.HasValue)
                {
                    var lineItem = ResultLineItems.FirstOrDefault(x => x.Line == restoreLine.Value);
                    if (lineItem != null)
                    {
                        gridResultLines.SelectedItem = lineItem;
                        gridResultLines.ScrollIntoView(lineItem);
                    }
                }
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog()
            {
                Description = "Select folder",
                UseDescriptionForTitle = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                txtBasePath.Text = dialog.SelectedPath;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            m_cancellationTokenSource?.Cancel();
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this,
                Theme = m_currentTheme.GetName(),
                MaxSearchTerms = m_maxSearchTerms,
                Multicolor = m_multipleHighlightColors,
            };

            if (settingsWindow.ShowDialog() == true)
            {
                m_currentTheme = Enum.Parse<ThemeType>(settingsWindow.Theme);
                ThemesController.SetTheme(m_currentTheme);
                m_maxSearchTerms = settingsWindow.MaxSearchTerms;
                m_multipleHighlightColors = settingsWindow.Multicolor;
            }
        }

        private void txtContainingText_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;

                if (btnStart.IsEnabled)
                {
                    btnStart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        }

        private void txtBasePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFolderSuggestionValues();

            // Based on https://learn.microsoft.com/en-us/answers/questions/840981/auto-complete-for-textbox-in-wpf-(mvvm)
            var input = txtBasePath.Text;
            if (input.Length > m_currentInput.Length && input != m_currentSuggestion)
            {
                m_currentSuggestion = m_folderSuggestionValues.FirstOrDefault(x => x.StartsWith(input, StringComparison.CurrentCultureIgnoreCase));
                if (m_currentSuggestion != null)
                {
                    m_currentText = m_currentSuggestion;
                    m_selectionStart = input.Length;
                    m_selectionLength = m_currentSuggestion.Length - input.Length;

                    txtBasePath.Text = m_currentText;
                    txtBasePath.Select(m_selectionStart, m_selectionLength);
                }
            }
            m_currentInput = input;
        }

        private void UpdateFolderSuggestionValues()
        {
            var input = txtBasePath.Text;

            if (input.EndsWith(Path.DirectorySeparatorChar) && Directory.Exists(input))
            {
                m_folderSuggestionValues = Directory.GetDirectories(input);
            }
        }

        private void txtMaxFileSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            var input = txtMaxFileSize.Text;
            txtMaxFileSize.Text = new string(input.Where(c => char.IsDigit(c)).ToArray());
        }

        private async void txtContextLines_TextChanged(object sender, TextChangedEventArgs e)
        {
            var input = txtContextLines.Text;
            txtContextLines.Text = new string(input.Where(c => char.IsDigit(c)).ToArray());

            var newVal = int.TryParse(txtContextLines.Text, out var v) ? v : 0;
            if (newVal != m_lastContextLines && FileResultItems.Count > 0 && m_cancellationTokenSource == null)
            {
                m_lastContextLines = newVal;

                string? restorePath = null;
                string? restoreFilename = null;
                if (gridFileResults.SelectedItem is FileSearchResult selected)
                {
                    restorePath = selected.Path;
                    restoreFilename = selected.Filename;
                }

                if (!string.IsNullOrWhiteSpace(txtBasePath.Text) && Directory.Exists(txtBasePath.Text))
                {
                    await RunSearch(restorePath, restoreFilename);
                }
            }
            else
            {
                m_lastContextLines = newVal;
            }
        }

        /// <summary>
        /// 上下文行數加一。
        /// </summary>
        private void btnContextUp_Click(object sender, RoutedEventArgs e)
        {
            var val = int.TryParse(txtContextLines.Text, out var v) ? v : 0;
            txtContextLines.Text = (val + 1).ToString();
        }

        /// <summary>
        /// 上下文行數減一（最小為零）。
        /// </summary>
        private void btnContextDown_Click(object sender, RoutedEventArgs e)
        {
            var val = int.TryParse(txtContextLines.Text, out var v) ? v : 0;
            txtContextLines.Text = Math.Max(0, val - 1).ToString();
        }

        private void cmbFileSizeUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            txtMaxFileSize.IsEnabled = (cmbFileSizeUnit.SelectedIndex != 0);
        }

        private string GetColorizedString(string source, IEnumerable<TermResult> termResults)
        {
            var rangeColors = new Dictionary<Range, int>();

            // Only add ranges which don't overlap values we've already added to rangeColors.
            // If a range partially overlaps, take the non-overlapping portion.
            // Terms which are earlier in the list take priority.

            List<Range> remainingMatchRangesBefore;
            List<Range> remainingMatchRangesAfter;

            var termIndexes = termResults.Select(x => x.TermIndex).Distinct().OrderBy(x => x).ToList();

            foreach (var termIndex in termIndexes)
            {
                var termMatchRanges = termResults.Where(x => x.TermIndex == termIndex).OrderBy(x => x.Range.Start).Select(x => x.Range);
                remainingMatchRangesAfter = termMatchRanges.ToList();

                foreach (var previousRange in rangeColors.Keys)
                {
                    remainingMatchRangesBefore = remainingMatchRangesAfter;
                    remainingMatchRangesAfter = new List<Range>();
                    
                    foreach (var remainingMatchRange in remainingMatchRangesBefore)
                    {
                        remainingMatchRangesAfter.AddRange(remainingMatchRange.GetNonOverlappingRanges(previousRange));
                    }
                }

                foreach (var range in remainingMatchRangesAfter)
                {
                    rangeColors.Add(range, m_multipleHighlightColors ? termIndex % HIGHLIGHT_COLORS_COUNT : 0);
                }
            }

            var stringBuilder = new StringBuilder();

            var startingIndex = 0;
            foreach (var rangeKey in rangeColors.Keys.OrderBy(x => x.Start))
            {
                if (startingIndex != rangeKey.Start)
                {
                    stringBuilder.Append(EscapeString(source.Substring(startingIndex, rangeKey.Start - startingIndex)));
                }

                stringBuilder.Append($"<c{rangeColors[rangeKey]}>");
                stringBuilder.Append(EscapeString(source.Substring(rangeKey.Start, rangeKey.End - rangeKey.Start + 1)));
                stringBuilder.Append($"</c{rangeColors[rangeKey]}>");

                startingIndex = rangeKey.End + 1;
            }

            stringBuilder.Append(EscapeString(source.Substring(startingIndex)));

            return stringBuilder.ToString();
        }

        private static string EscapeString(string source)
        {
            return source.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
