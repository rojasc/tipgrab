using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TipGrab.Properties;

namespace TipGrab.Views
{
    /// <summary>
    /// Interaction logic for TwitchAuthWindow.xaml
    /// </summary>
    public partial class TwitchAuthWindow : Window
    {
        private string _redirectUrl;

        public TwitchAuthWindow()
        {
            InitializeComponent();

            string clientId = Settings.Default.ClientID;
            string url = $"https://api.twitch.tv/kraken/oauth2/authorize?response_type=token&client_id={clientId}&redirect_uri=http://localhost/&scope=chat_login";

            this.webBrowser.Source = new Uri(url);
            this.webBrowser.Navigating += WebBrowser_Navigating;

            _redirectUrl = Settings.Default.RedirectUrl;
        }

        private void WebBrowser_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e.Uri.AbsoluteUri.StartsWith(_redirectUrl))
            {
                Regex accessTokenPattern = new Regex("access_token=([a-zA-Z0-9]+)&");
                Settings.Default.TwitchAuthToken = accessTokenPattern.Match(e.Uri.Fragment).Groups[1].Value;
                Settings.Default.Save();

                Close();
            }
        }
    }
}
