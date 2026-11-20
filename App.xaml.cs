namespace Understander
{
    public partial class App : Application
    {
        public App()
        {
            // КРИТИЧНО: без этого вызова Application.Resources остаётся пустым,
            // и любой {StaticResource ...} на страницах (BrandText, CardBorder и т.д.)
            // бросает XamlParseException -> приложение падает сразу после сплэша.
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
            
            // На Windows делаем окно узким, как на телефоне
#if WINDOWS
            window.Width = 450;
            window.Height = 850;
            window.MinimumWidth = 360;
            window.MinimumHeight = 640;
#endif
            return window;
        }
    }
}