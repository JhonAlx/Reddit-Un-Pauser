using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Polly.Retry;
using Reddit_Un_Pauser.Model;
using Polly;

namespace Reddit_Un_Pauser
{
    /// <summary>
    ///     Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow
    {
        private static RetryPolicy _retryPolicy;

        public LoginWindow()
        {
            InitializeComponent();

            try
            {
                Configuration.Load();
            }
            catch (Exception)
            {
                Configuration.Default();
            }

            UserNameTextBox.Text = Configuration.Instance.Username;
            PasswordTextBox.Password = Configuration.Instance.Password;

            LoginProgressLabel.Visibility = Visibility.Hidden;
            LoginProgressBar.Visibility = Visibility.Hidden;


            if (!string.IsNullOrEmpty(Configuration.Instance.Username) &&
                !string.IsNullOrEmpty(Configuration.Instance.Password))
                RememberPasswordCheckBox.IsChecked = true;
        }

        internal CookieContainer Cookies { get; set; }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(UserNameTextBox.Text) || string.IsNullOrEmpty(PasswordTextBox.Password))
            {
                MessageBox.Show("Username or password fields are empty!");
            }
            else
            {
                var taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
                var ct = new CancellationToken();
                var c = string.Empty;
                string username = UserNameTextBox.Text, password = PasswordTextBox.Password;

                _retryPolicy = Policy
                        .Handle<WebException>()
                        .WaitAndRetry(
                            3,
                            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            (ex, i) =>
                            {
                                MessageBox.Show("Caught web exception! retrying in a moment...");
                            });

                SubmitButton.IsEnabled = false;
                LoginProgressBar.Visibility = Visibility.Visible;
                LoginProgressBar.IsIndeterminate = true;
                LoginProgressLabel.Visibility = Visibility.Visible;
                LoginProgressLabel.Content = "Logging into Reddit...";

                _retryPolicy.Execute(() => Task.Factory.StartNew(() => Login(username, password, out c), ct).
                    ContinueWith(w =>
                        {
                            SubmitButton.IsEnabled = true;
                            LoginProgressBar.Visibility = Visibility.Hidden;
                            LoginProgressLabel.Visibility = Visibility.Hidden;

                            if (c.Contains("reddit_session"))
                            {
                                var result = MessageBox.Show("Logged in!", "Success", MessageBoxButton.OK);

                                Cookies = new CookieContainer();

                                Cookies.SetCookies(new Uri("http://reddit.com"), c);

                                if (RememberPasswordCheckBox.IsChecked == true)
                                {
                                    Configuration.Instance.Username = UserNameTextBox.Text;
                                    Configuration.Instance.Password = PasswordTextBox.Password;

                                    Configuration.Instance.Save();
                                }

                                if (result == MessageBoxResult.OK)
                                {
                                    var main = new MainWindow {Cookies = Cookies};
                                    Application.Current.MainWindow = main;
                                    Close();
                                    main.Show();
                                }
                            }
                            else
                            {
                                MessageBox.Show("Incorrect credentials, please try again!", "Error", MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                            }
                        },
                        ct,
                        TaskContinuationOptions.None, taskScheduler));
            }
        }

        private void Login(string username, string password, out string cookie)
        {

            var loginUrl = "https://www.reddit.com/api/login/";
            string loginParams = $"op=login&user={username}&passwd={password}&api_type=json";

            var loginRequest = WebRequest.Create(loginUrl) as HttpWebRequest;
            // ReSharper disable once PossibleNullReferenceException
            loginRequest.ContentType = "application/x-www-form-urlencoded";
            loginRequest.Method = "POST";

            var bytes = Encoding.ASCII.GetBytes(loginParams);

            loginRequest.ContentLength = bytes.Length;

            using (var os = loginRequest.GetRequestStream())
            {
                os.Write(bytes, 0, bytes.Length);
            }

            var response = loginRequest.GetResponse() as HttpWebResponse;
            // ReSharper disable once PossibleNullReferenceException
            cookie = response.Headers["set-cookie"];
        }
    }
}