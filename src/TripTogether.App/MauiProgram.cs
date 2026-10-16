using Microsoft.Maui.LifecycleEvents;
using TripTogether.App.Services;
namespace TripTogether.App;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<TripStore>();
        builder.Services.AddSingleton<MainPage>();
        builder.ConfigureLifecycleEvents(events =>
        {
#if IOS
            events.AddiOS(ios => ios
                .SceneWillConnect((scene, session, options) =>
                {
                    foreach (var context in options.UrlContexts)
                        InvitationInbox.Receive(context.Url.AbsoluteString);
                })
                .SceneOpenUrl((scene, contexts) =>
                {
                    var handled = false;
                    foreach (var context in contexts)
                    {
                        if (context.Url.Scheme != "triptogether") continue;
                        InvitationInbox.Receive(context.Url.AbsoluteString);
                        handled = true;
                    }
                    return handled;
                })
                .OpenUrl((application, url, options) =>
                {
                    InvitationInbox.Receive(url.AbsoluteString);
                    return url.Scheme == "triptogether";
                }));
#endif
        });
        return builder.Build();
    }
}
