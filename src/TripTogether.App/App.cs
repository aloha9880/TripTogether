namespace TripTogether.App;
public class App(MainPage page) : Application
{
 protected override Window CreateWindow(IActivationState? activationState)
 {
  var window = new Window(new NavigationPage(page));
  window.Destroying += (_, _) => page.StopSharing();
  return window;
 }
}
