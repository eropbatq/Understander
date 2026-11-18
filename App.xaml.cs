namespace Understander
{
    public partial class App : Application
    {
        public App()
        {
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