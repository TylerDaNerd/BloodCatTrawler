using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        public MainWindow()
        {
            InitializeComponent();

            osuInstallationInput.Text = string.Empty;

            localSongsListBox.ItemsSource = _localSongs;
            BindingOperations.EnableCollectionSynchronization(_localSongs, _localSongsLock);

            processingSongsListBox.ItemsSource = _processingSongs;
            BindingOperations.EnableCollectionSynchronization(_processingSongs, _processingSongsLock);

            trawlingPageLabel.Visibility = Visibility.Collapsed;
            trawlingErrorLabel.Visibility = Visibility.Collapsed;
        }

        private void osuInstallationConfirm_Click(object sender, RoutedEventArgs e)
        {
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

        void SetTrawlingPageLabel(int page) => Dispatcher.BeginInvoke(new Action(() => { trawlingPageLabel.Content = $"Trawling Page {page}"; }));

        private void startStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_running)
            {
                startStopButton.Content = "Stopping";
                startStopButton.IsEnabled = false;

                _stopRequested = true;
            }
            else
            {
                startStopButton.Content = "Starting";
                startStopButton.IsEnabled = false;

                searchTermsWrapper.IsEnabled = false;

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
                        trawlingErrorLabel.Visibility = Visibility.Collapsed;
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
                            var request = WebRequest.CreateHttp(uri);
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            {
                                XmlDocument doc = new XmlDocument();
                                using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max))
                                {
                                    XElement xml = XElement.Load(reader);
                                    doc.LoadXml(xml.ToString());
                                }

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
                            }

                            ProcessedSongInfo song = null;
                            while (!_stopRequested && (song = _processingSongs.FirstOrDefault(x => x.State != ProcessedSongInfo.ProcessState.Completed)) != null)
                            {
                                song.State = ProcessedSongInfo.ProcessState.Downloading;
                                Dispatcher.BeginInvoke(new Action(() => { processingSongsListBox.Items.Refresh(); }));

                                using (var client = new WebClient())
                                {
                                    var fileName = song.ID + (!string.IsNullOrEmpty(song.Name) ? " " + song.Name : string.Empty) + ".osz";
                                    var tempPath = Path.Combine(Path.GetTempPath(), fileName);

                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        downloadProgress.Value = 0;
                                        downloadProgress.Visibility = Visibility.Visible;
                                    }));
                                    client.DownloadProgressChanged += (object dSender, DownloadProgressChangedEventArgs dE) =>
                                    {
                                        Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            if (dE.ProgressPercentage != downloadProgress.Value)
                                                downloadProgress.Value = dE.ProgressPercentage;
                                        }));
                                    };
                                    client.DownloadFileTaskAsync($"http://bloodcat.com/osu/s/{song.ID}", tempPath).Wait();
                                    Dispatcher.BeginInvoke(new Action(() => { downloadProgress.Visibility = Visibility.Collapsed; }));

                                    File.Move(tempPath, Path.Combine(_osuSongPath.FullName, fileName));
                                }

                                song.State = ProcessedSongInfo.ProcessState.Completed;
                                Dispatcher.BeginInvoke(new Action(() => { processingSongsListBox.Items.Refresh(); }));
                            }

                            Dispatcher.BeginInvoke(new Action(() => { trawlingErrorLabel.Visibility = Visibility.Collapsed; }));
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                trawlingErrorLabel.Visibility = Visibility.Visible;
                                trawlingErrorLabel.Content = ex.Message;
                            }));
                        }

                        foreach (var s in _processingSongs)
                        {
                            if (s.State == ProcessedSongInfo.ProcessState.Downloading)
                                s.State = ProcessedSongInfo.ProcessState.Waiting;
                        }

                        Dispatcher.BeginInvoke(new Action(() => { processingSongsListBox.Items.Refresh(); }));
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
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

            _stopRequested = true;

            if (_workingThread != null)
                _workingThread.Join();
        }
    }
}
