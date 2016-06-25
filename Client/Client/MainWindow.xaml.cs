using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool _stopUpdate = false;
        Thread _updateCheckThread;
        string _updateBinaryUrl;
        Version _updateVersion;
        Thread _updateDownloadThread;

        public MainWindow()
        {
            InitializeComponent();

            // This might become a thing, prefer to hide version info for now
            //Title = $"BloodCat Trawler {Assembly.GetExecutingAssembly().GetName().Version}";

            switch ((Application.Current as App).ProgramUpdateState)
            {
                case App.UpdateState.UpdateCompleted:
                    updateCompleteStatus.Foreground = Brushes.Green;
                    updateCompleteStatus.Content = $"Successfully updated to version {Assembly.GetExecutingAssembly().GetName().Version}";
                    break;
                case App.UpdateState.UpdateFailed:
                    updateCompleteStatus.Foreground = Brushes.Red;
                    updateCompleteStatus.Content = "Update failed, could not copy new update binary, make sure you are not running extra instances of the program";
                    break;
                case App.UpdateState.Nothing:
                    updateCompleteStatus.Visibility = Visibility.Collapsed;
                    break;
            }

            osuInstallationInput.Text = Properties.Settings.Default.OsuInstallPath ?? string.Empty;

            localSongsListBox.ItemsSource = _localSongs;
            BindingOperations.EnableCollectionSynchronization(_localSongs, _localSongsLock);

            processingSongsListBox.ItemsSource = _processingSongs;
            BindingOperations.EnableCollectionSynchronization(_processingSongs, _processingSongsLock);

            trawlingPageLabel.Visibility = Visibility.Collapsed;
            trawlingException.Visibility = Visibility.Collapsed;

            updateButton.Visibility = Visibility.Collapsed;
            updateStatus.Visibility = Visibility.Collapsed;
            updateProgress.Visibility = Visibility.Collapsed;
            updateException.Visibility = Visibility.Collapsed;

            downloadProgressWrapper.Visibility = Visibility.Collapsed;

            _updateCheckThread = new Thread(() =>
            {
                while (!_stopUpdate)
                {
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        var localVersion = Assembly.GetExecutingAssembly().GetName().Version;

                        var request = WebRequest.CreateHttp("https://api.github.com/repos/Zogzer/BloodCatTrawler/releases/latest");
                        request.UserAgent = $"BloodCatTrawler/{localVersion}";
                        using (var response = request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max))
                            doc.Load(reader);

                        var root = doc.SelectSingleNode("root");
                        var versionString = root["tag_name"].InnerText;

                        var versionBuffer = new int[4] { 0, 0, 0, 0 };
                        var versionSegments = Array.ConvertAll(versionString.Split('.'), int.Parse);
                        Array.Copy(versionSegments, versionBuffer, versionSegments.Length);

                        var latestVersion = new Version(versionBuffer[0], versionBuffer[1], versionBuffer[2], versionBuffer[3]);

                        if (latestVersion > localVersion)
                        {
                            _updateBinaryUrl = root["assets"].FirstChild["browser_download_url"].InnerText;
                            _updateVersion = latestVersion;

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                updateStatus.Visibility = Visibility.Visible;
                                updateStatus.Content = $"New update (version {latestVersion}) is avaliable";

                                updateButton.Visibility = Visibility.Visible;
                                updateButton.Content = "Update";
                            }));
                        }

                        _stopUpdate = true;
                    }
                    catch
                    {
                        Thread.Sleep(10000); // Idk if it is worth doing anything more than this
                    }
                }
            });
            _updateCheckThread.Start();
        }

        private void osuInstallationConfirm_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.OsuInstallPath = osuInstallationInput.Text;
            Properties.Settings.Default.Save();

            updateCompleteStatus.Visibility = Visibility.Collapsed;

            osuInstallationWrapper.IsEnabled = false;
            osuInstallationStatus.Visibility = Visibility.Collapsed;
            osuInstallationConfirm.Visibility = Visibility.Collapsed;

            programBodyWrapper.IsEnabled = true;

            _osuInstallationPath = new DirectoryInfo(osuInstallationInput.Text);
            _osuSongPath = _osuInstallationPath.GetDirectories().First(x => x.Name == "Songs");

            _songsFolderWatcher = new FileSystemWatcher(_osuSongPath.FullName);
            _songsFolderWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            _songsFolderWatcher.Changed += SongsFolderWatcherEvent;
            _songsFolderWatcher.Created += SongsFolderWatcherEvent;
            _songsFolderWatcher.Deleted += SongsFolderWatcherEvent;
            _songsFolderWatcher.Renamed += SongsFolderWatcherEvent;

            _songsFolderWatcher.EnableRaisingEvents = true;

            UpdateLocalSongInfos();
        }

        void SongsFolderWatcherEvent(object sender, object e)
        {
            UpdateLocalSongInfos();
        }

        static string[] _osuInstallFileChecks = { "osu!.exe", "Songs" };

        bool IsValidOsuInstall(string path)
        {
            if (!Directory.Exists(path))
                return false;

            DirectoryInfo osuInstall = new DirectoryInfo(path);
            var files = osuInstall.GetFileSystemInfos().Select(x => x.Name);

            if (!_osuInstallFileChecks.All(x => files.Contains(x)))
                return false;

            return true;
        }

        private void osuInstallationInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            var path = osuInstallationInput.Text;

            if (osuInstallationConfirm == null)
                return;

            if (IsValidOsuInstall(path))
            {
                osuInstallationStatus.Content = "Valid osu! installation detected";
                osuInstallationStatus.Foreground = Brushes.Green;
                osuInstallationConfirm.IsEnabled = true;
            }
            else
            {
                if (path == string.Empty)
                {
                    osuInstallationStatus.Content = "Input your osu install path in the box above";
                    osuInstallationStatus.Foreground = Brushes.Blue;
                }
                else
                {
                    osuInstallationStatus.Content = "No osu! installation detected at path";
                    osuInstallationStatus.Foreground = Brushes.Red;
                }

                osuInstallationConfirm.IsEnabled = false;
            }
        }

        //

        FileSystemWatcher _songsFolderWatcher;

        DirectoryInfo _osuInstallationPath;
        DirectoryInfo _osuSongPath;

        class SongInfo
        {
            public long ID { get; set; }
            public string Name { get; set; }

            public SongInfo(long id, string name)
            {
                ID = id;
                Name = name;
            }
        };

        class ProcessedSongInfo : SongInfo
        {
            public enum ProcessState
            {
                Waiting,
                Downloading,
                Completed
            }

            public ProcessState State { get; set; }

            public ProcessedSongInfo(long id, string name, ProcessState state)
                : base(id, name)
            {
                State = state;
            }

            public ProcessedSongInfo(SongInfo info, ProcessState state)
                : this(info.ID, info.Name, state)
            { }

        }

        ObservableCollection<SongInfo> _localSongs = new ObservableCollection<SongInfo>();
        object _localSongsLock = new object();

        ObservableCollection<ProcessedSongInfo> _processingSongs = new ObservableCollection<ProcessedSongInfo>();
        object _processingSongsLock = new object();

        void UpdateLocalSongInfos()
        {
            var songs = _osuSongPath.GetFileSystemInfos();
            var songInfos = new List<SongInfo>();

            foreach (var s in songs)
            {
                var parts = s.Name.Split(' ');

                if (!Regex.IsMatch(parts[0], "^[0-9]+$"))
                    continue;

                string name = parts.Length > 1 ? s.Name.Substring(parts[0].Length + 1) : null;

                if (!string.IsNullOrEmpty(s.Extension) && s.Extension == ".osz")
                    name = name.Substring(0, name.Length - 4);

                songInfos.Add(new SongInfo(long.Parse(parts[0]), name));
            }

            lock (_localSongsLock)
            {
                _localSongs.Clear();

                songInfos.ForEach(x => _localSongs.Add(x));
            }
        }

        public class InlineEqualityComparer<T> : IEqualityComparer<T>
        {
            public InlineEqualityComparer(Func<T, T, bool> cmp)
            {
                this.cmp = cmp;
            }
            public bool Equals(T x, T y)
            {
                return cmp(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }

            public Func<T, T, bool> cmp { get; set; }
        }

        Thread _workingThread;
        bool _running = false;
        bool _stopRequested = false;

        List<string> _modes = new List<string>();
        List<string> _statuses = new List<string>();
        string _terms;

        WebClient _downloadClient;

        void SetTrawlingPageLabel(int page) => Dispatcher.BeginInvoke(new Action(() => { trawlingPageLabel.Content = $"Trawling Page {page}"; }));

        private void startStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_running)
            {
                startStopButton.Content = "Stopping";
                startStopButton.IsEnabled = false;

                _stopRequested = true;

                if (_downloadClient != null)
                    _downloadClient.CancelAsync();
            }
            else
            {
                updateButton.Content = "Stop song downloading to update";
                updateButton.IsEnabled = false;

                startStopButton.Content = "Starting";
                startStopButton.IsEnabled = false;

                searchTermsWrapper.IsEnabled = false;

                _modes.Clear();
                _statuses.Clear();

                if (checkStandard.IsChecked.Value)
                    _modes.Add("0");
                if (checkTaiko.IsChecked.Value)
                    _modes.Add("1");
                if (checkCTB.IsChecked.Value)
                    _modes.Add("2");
                if (checkMania.IsChecked.Value)
                    _modes.Add("3");

                if (checkUnranked.IsChecked.Value)
                    _statuses.Add("0");
                if (checkRanked.IsChecked.Value)
                    _statuses.Add("1");
                if (checkApproved.IsChecked.Value)
                    _statuses.Add("2");
                if (checkQualified.IsChecked.Value)
                    _statuses.Add("3");

                _terms = searchTermsBox.Text;

                _workingThread = new Thread(() =>
                {
                    int page = 1;
                    SetTrawlingPageLabel(page);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _running = true;

                        startStopButton.Content = "Stop";
                        startStopButton.IsEnabled = true;

                        trawlingPageLabel.Visibility = Visibility.Visible;
                        trawlingException.Visibility = Visibility.Collapsed;
                    }));

                    bool noNew = false;

                    while (!_stopRequested)
                    {
                        var query = HttpUtility.ParseQueryString(string.Empty);
                        query["mod"] = "json";
                        query["m"] = string.Join(",", _modes);
                        query["s"] = string.Join(",", _statuses);
                        query["q"] = _terms;
                        query["p"] = page.ToString();

                        Uri uri = new Uri("http://bloodcat.com/osu?" + query.ToString());

                        try
                        {
                            XmlDocument doc = new XmlDocument();

                            var request = WebRequest.CreateHttp(uri);
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max))
                                doc.Load(reader);

                            var songs = doc.SelectNodes("/root/item");

                            if (songs.Count == 0)
                                break;

                            int newCount = 0;
                            foreach (XmlNode s in songs)
                            {
                                var info = new SongInfo(long.Parse(s["id"].InnerText), s["title"].InnerText);

                                if (_localSongs.Contains(info, new InlineEqualityComparer<SongInfo>((x, y) => x.ID == y.ID)))
                                    continue;

                                if (_processingSongs.Contains(info, new InlineEqualityComparer<SongInfo>((x, y) => x.ID == y.ID)))
                                    continue;

                                _processingSongs.Add(new ProcessedSongInfo(info, ProcessedSongInfo.ProcessState.Waiting));
                                newCount++;
                            }

                            if (newCount == 0)
                                if (noNew)
                                {
                                    page++;
                                    SetTrawlingPageLabel(page);
                                    noNew = false;
                                }
                                else
                                    noNew = true;

                            ProcessedSongInfo song = null;
                            while (!_stopRequested && (song = _processingSongs.FirstOrDefault(x => x.State != ProcessedSongInfo.ProcessState.Completed)) != null)
                            {
                                song.State = ProcessedSongInfo.ProcessState.Downloading;
                                Dispatcher.BeginInvoke(new Action(processingSongsListBox.Items.Refresh));

                                using (_downloadClient = new WebClient())
                                {
                                    _downloadClient.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:40.0) Gecko/20100101 Firefox/40.1"; // server side fix

                                    var fileName = song.ID + (!string.IsNullOrEmpty(song.Name) ? " " + song.Name : string.Empty) + ".osz";

                                    fileName = string.Join("-", fileName.Split(Path.GetInvalidFileNameChars()));

                                    var tempPath = Path.Combine(Path.GetTempPath(), fileName);

                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        downloadProgress.Value = 0;
                                        downloadProgressWrapper.Visibility = Visibility.Visible;
                                        downloadProgressLabel.Content = string.Empty;
                                    }));
                                    _downloadClient.DownloadProgressChanged += (object dSender, DownloadProgressChangedEventArgs dE) =>
                                    {
                                        Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            downloadProgressLabel.Content = $"{dE.BytesReceived}/{dE.TotalBytesToReceive} B";

                                            if (dE.ProgressPercentage != downloadProgress.Value)
                                                downloadProgress.Value = dE.ProgressPercentage;
                                        }));
                                    };
                                    try
                                    {
                                        _downloadClient.DownloadFileTaskAsync($"http://bloodcat.com/osu/s/{song.ID}", tempPath).Wait();
                                        File.Copy(tempPath, Path.Combine(_osuSongPath.FullName, fileName), true);
                                        File.Delete(tempPath);
                                    }
                                    catch (AggregateException ex)
                                    {
                                        if (!(ex.InnerException is WebException && (ex.InnerException as WebException).Status == WebExceptionStatus.RequestCanceled))
                                            throw;

                                        song.State = ProcessedSongInfo.ProcessState.Waiting;
                                    }
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        downloadProgressWrapper.Visibility = Visibility.Collapsed;
                                        trawlingException.Visibility = Visibility.Collapsed;
                                    }));
                                }

                                if (song.State == ProcessedSongInfo.ProcessState.Downloading)
                                    song.State = ProcessedSongInfo.ProcessState.Completed;
                                Dispatcher.BeginInvoke(new Action(() => { processingSongsListBox.Items.Refresh(); }));
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                trawlingException.Visibility = Visibility.Visible;
                                trawlingException.Content = ex.Message;
                            }));
                        }

                        foreach (var s in _processingSongs)
                        {
                            if (s.State == ProcessedSongInfo.ProcessState.Downloading)
                                s.State = ProcessedSongInfo.ProcessState.Waiting;
                        }

                        lock (_processingSongsLock)
                        {
                            ProcessedSongInfo s;
                            while ((s = _processingSongs.FirstOrDefault(x => x.State == ProcessedSongInfo.ProcessState.Waiting)) != null)
                                _processingSongs.Remove(s);
                        }

                        Dispatcher.BeginInvoke(new Action(processingSongsListBox.Items.Refresh));
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        updateButton.IsEnabled = true;
                        updateButton.Content = "Update";

                        startStopButton.Content = "Start";
                        startStopButton.IsEnabled = true;
                        searchTermsWrapper.IsEnabled = true;

                        _running = false;
                        _stopRequested = false;

                        trawlingPageLabel.Visibility = Visibility.Collapsed;
                    }));
                });

                _workingThread.Start();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Hide();

            if (_downloadClient != null)
                _downloadClient.CancelAsync();

            _stopRequested = true;
            _stopUpdate = true;

            if (_workingThread != null)
                _workingThread.Join();

            if (_updateCheckThread != null)
                _updateCheckThread.Join();

            if (_updateDownloadThread != null)
                _updateDownloadThread.Join();
        }

        private void updateButton_Click(object sender, RoutedEventArgs e)
        {
            osuInstallationWrapper.IsEnabled = false;
            programBodyWrapper.IsEnabled = false;

            updateButton.Visibility = Visibility.Collapsed;
            updateProgress.Visibility = Visibility.Visible;

            _updateDownloadThread = new Thread(() =>
            {
                using (var client = new WebClient())
                {
                    var fileName = Assembly.GetExecutingAssembly().GetName().Name + "." + _updateVersion.ToString() + ".exe";
                    var tempPath = Path.Combine(Path.GetTempPath(), fileName);

                    client.DownloadProgressChanged += (object dSender, DownloadProgressChangedEventArgs dE) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (dE.ProgressPercentage != updateProgress.Value)
                                updateProgress.Value = dE.ProgressPercentage;
                        }));
                    };
                    int tryCount = 0;
                    while (true)
                    {
                        tryCount++;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            updateStatus.Content = "Downloading update" + (tryCount > 1 ? ", attempt " + tryCount : string.Empty);
                            updateException.Visibility = Visibility.Collapsed;
                        }));

                        try
                        {
                            client.DownloadFileTaskAsync(new Uri(_updateBinaryUrl), tempPath).Wait();

                            Process.Start(tempPath, $"--updateMove \"{Assembly.GetExecutingAssembly().Location}\"");

                            Dispatcher.BeginInvoke(new Action(Close));

                            break;
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                updateException.Visibility = Visibility.Visible;
                                updateException.Content = ex.Message;
                            }));

                            Thread.Sleep(5000);
                        }
                    }
                }
            });
            _updateDownloadThread.Start();
        }

        private void clearCompletedButton_Click(object sender, RoutedEventArgs e)
        {
            lock (_processingSongsLock)
            {
                ProcessedSongInfo s;
                while ((s = _processingSongs.FirstOrDefault(x => x.State == ProcessedSongInfo.ProcessState.Completed)) != null)
                    _processingSongs.Remove(s);
            }
        }

        private void searchTermsBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                if (startStopButton.IsEnabled)
                    startStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void osuInstallationInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                if (osuInstallationConfirm.IsEnabled)
                    osuInstallationConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }
}
