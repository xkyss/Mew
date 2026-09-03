using Aprillz.MewUI;
using Mew.PluginHost;

Win32Platform.Register();
Direct2DBackend.Register();

var app = new PluginHostApp();
app.Run();
