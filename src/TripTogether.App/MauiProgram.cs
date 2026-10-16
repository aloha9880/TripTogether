using TripTogether.App.Services;
namespace TripTogether.App;
public static class MauiProgram
{
 public static MauiApp CreateMauiApp() { var b=MauiApp.CreateBuilder(); b.UseMauiApp<App>(); b.Services.AddSingleton<TripStore>(); b.Services.AddSingleton<MainPage>(); return b.Build(); }
}
