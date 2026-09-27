using System.Windows;

namespace TypeFix
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            UiLanguage.Initialize();
            base.OnStartup(e);
        }
    }
}
