using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Mvvm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TipGrab.Properties;
using TipGrab.Views;
using TwitchLib;
using TwitchLib.Events.Client;
using TwitchLib.Models.Client;

namespace TipGrab.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _twtichAuthToken;
        private string _streamTipClientID;
        private string _streamTipAuthToken;
        private string _channel;
        private string _buttonText;
        private bool _running;
        private ICommand _authorizeTwitchCommand;
        private ICommand _buttonCommand;
        Regex _cheerPattern = new Regex("^cheer([0-9]+)\\s?(.*)?", RegexOptions.Compiled);
        private TwitchClient _client;
        private Thread _streamTipPollThread;
        private string _outputPath;
        private object _lock = new object();

        public MainWindowViewModel()
        {
            StreamTipClientID = Settings.Default.StreamTipClientID;
            StreamTipAccessToken = Settings.Default.StreamTipAccessToken;
            TwitchAuthToken = Settings.Default.TwitchAuthToken;
            Channel = Settings.Default.Channel;
            ButtonText = "Start";
            OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tip.txt");

            _authorizeTwitchCommand = new DelegateCommand(AuthorizeTwitch);
            _buttonCommand = new DelegateCommand(Start);
        }

        public string StreamTipClientID
        {
            get { return _streamTipClientID; }
            set { SetProperty(ref _streamTipClientID, value); }
        }

        public string StreamTipAccessToken
        {
            get { return _streamTipAuthToken; }
            set { SetProperty(ref _streamTipAuthToken, value); }
        }

        public string TwitchAuthToken
        {
            get { return _twtichAuthToken; }
            set { SetProperty(ref _twtichAuthToken, value); }
        }

        public string Channel
        {
            get { return _channel; }
            set { SetProperty(ref _channel, value); }
        }

        public string OutputPath
        {
            get { return _outputPath; }
            set { SetProperty(ref _outputPath, value); }
        }

        public string ButtonText
        {
            get { return _buttonText; }
            set { SetProperty(ref _buttonText, value); }
        }

        public bool Running
        {
            get { return _running; }
            set { SetProperty(ref _running, value); }
        }

        public ICommand AuthorizeTwitchCommand
        {
            get { return _authorizeTwitchCommand; }
            set { SetProperty(ref _authorizeTwitchCommand, value); }
        }

        public ICommand ButtonCommand
        {
            get { return _buttonCommand; }
            set { SetProperty(ref _buttonCommand, value); }
        }

        private void AuthorizeTwitch()
        {
            var authWindow = new TwitchAuthWindow() { Owner = App.Current.MainWindow };

            authWindow.ShowDialog();
            TwitchAuthToken = Settings.Default.TwitchAuthToken;
        }

        private void Start()
        {
            Settings.Default.StreamTipClientID = StreamTipClientID;
            Settings.Default.StreamTipAccessToken = StreamTipAccessToken;
            Settings.Default.Channel = Channel;
            Settings.Default.Save();

            ButtonText = "Stop";
            ButtonCommand = new DelegateCommand(Stop);
            Running = true;

            _streamTipPollThread = new Thread(PollStreamTip);
            ScanForCheers();

            _streamTipPollThread.Start();
        }

        private void Stop()
        {
            ButtonText = "Start";
            ButtonCommand = new DelegateCommand(Start);
            Running = false;
            _client.Disconnect();
        }

        private void ScanForCheers()
        {
            _client = new TwitchClient(new ConnectionCredentials("CompileNConquer", TwitchAuthToken), Channel, logging: true);
            _client.OnConnected += Client_OnConnected;
            _client.OnConnectionError += Client_OnConnectionError;
            _client.OnIncorrectLogin += Client_OnIncorrectLogin;
            _client.OnMessageReceived += Client_OnMessageReceived;

            _client.Connect();
        }

        private void Client_OnIncorrectLogin(object sender, OnIncorrectLoginArgs e)
        { 

        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
        }

        private async void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {

            if (e.ChatMessage.Message.StartsWith("cheer"))
            {
                Match cheerMatch = _cheerPattern.Match(e.ChatMessage.Message);

                if (cheerMatch.Success)
                {
                    string cheerValue = cheerMatch.Groups[1].Value;
                    string message = cheerMatch.Groups[2].Value;

                    WriteTip(e.ChatMessage.DisplayName, cheerValue, message, true);
                    await Task.Delay(500);
                }
            }
        }

        private async void PollStreamTip()
        {
            string dateFrom = DateTime.Now.ToString("o");
            int offset = 0;

            using (HttpClient client = new HttpClient())
            {
                while (Running)
                {
                    string url = $"https://streamtip.com/api/tips?date_from={dateFrom}&offset={offset}&direction=asc";

                    HttpRequestMessage request = new HttpRequestMessage()
                    {
                        RequestUri = new Uri(url),
                        Method = HttpMethod.Get,
                    };
                    request.Headers.Add("Authorization", $"{StreamTipClientID} {StreamTipAccessToken}");

                    HttpResponseMessage response = await client.SendAsync(request);

                    using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                    using (StreamReader streamReader = new StreamReader(responseStream))
                    using (JsonReader jsonReader = new JsonTextReader(streamReader))
                    {
                        JsonSerializer serializer = new JsonSerializer();

                        var obj = serializer.Deserialize<JObject>(jsonReader);
                        var tips = obj["tips"];

                        foreach (var tip in tips)
                        {
                            WriteTip(tip["username"].ToString(), tip["amount"].ToString(), tip["note"].ToString());
                            offset++;
                        }
                    }

                    int requestRemaining = Int32.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());

                    if (requestRemaining < 1)
                    {
                        int epocDate = Int32.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());
                        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        epoch = epoch.AddSeconds(epocDate);// your case results to 4/5/2013 8:48:34 AM

                        await Task.Delay(epoch.Subtract(DateTime.UtcNow));
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
            }
        }

        private void WriteTip(string user, string tip, string message, bool isCheer = false)
        {
            decimal cheerTip = Decimal.Parse(tip);

            if (isCheer)
            {
                cheerTip /= 100;
            }

            lock (_lock)
            {
                File.WriteAllText(OutputPath, $"{user}: {cheerTip.ToString("C")} - {message}");
            }
        }
    }
}
