using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace TNovClient
{
    public partial class NotificationWindow : Window
    {
        private DispatcherTimer _timer;
        private Storyboard _fadeInStoryboard;
        private Storyboard _fadeOutStoryboard;

        public Action? OpenMainWindow { get; set; }

        public NotificationWindow()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;

            // Получаем ссылки на анимации из ресурсов
            _fadeInStoryboard = (Storyboard)this.Resources["FadeInStoryboard"];
            _fadeOutStoryboard = (Storyboard)this.Resources["FadeOutStoryboard"];
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;

            // Гарантируем, что окно не уйдёт за границы экрана даже при очень больших размерах
            double width = Math.Max(1, this.ActualWidth);
            double height = Math.Max(1, this.ActualHeight);

            this.Left = Math.Max(0, workArea.Right - width - 20);
            this.Top = Math.Max(0, workArea.Bottom - height - 20);
        }
        /// <summary>
        /// Показать уведомление с заголовком и текстом.
        /// Автоматически закроется через заданное количество секунд с анимацией затухания.
        /// </summary>
        public void ShowNotification(string title, string message, int durationSeconds = 7)
        {
            // Устанавливаем содержимое
            TitleBlock.Text = title;
            SetMessageText(message);
            
            // Сброс прозрачности (на случай, если окно уже использовалось)
            this.Opacity = 0;

            // Показываем окно (невидимо из-за Opacity = 0)
            this.Show();

            // Запускаем анимацию появления
            _fadeInStoryboard.Begin(this);

            // Запускаем таймер для автоматического закрытия
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(durationSeconds)
            };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                // Запускаем анимацию исчезновения
                _fadeOutStoryboard.Begin(this);
            };
            _timer.Start();
        }

        private void SetMessageText(string message)
        {
            MessageBlock.Inlines.Clear();

            if (string.IsNullOrEmpty(message))
                return;

            string[] lines = message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    MessageBlock.Inlines.Add(new LineBreak());

                if (!string.IsNullOrEmpty(lines[i]))
                    MessageBlock.Inlines.Add(new Run(lines[i]));
            }
        }

        /// <summary>
        /// Обработчик завершения анимации Fade Out – закрываем окно.
        /// </summary>
        private void FadeOut_Completed(object sender, EventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Если пользователь кликнул по окну – закрываем его немедленно (опционально).
        /// </summary>
        protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            _timer?.Stop();
            OpenMainWindow?.Invoke();
            _fadeOutStoryboard.Begin(this);
        }

        // Очистка ресурсов при закрытии
        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _timer = null;
            base.OnClosed(e);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            string commandText = @"https://docs.google.com/spreadsheets/d/1Q3VKGR8WVSxlKCah8eKWT_wBBVtCSysXM2uha0qameI/edit?usp=sharing";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
}