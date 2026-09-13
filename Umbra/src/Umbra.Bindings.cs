using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using Umbra.AuxBar;
using Umbra.Widgets.System;
using Umbra.Windows;
using Umbra.Windows.Library.Installer;
using Umbra.Windows.Settings;

namespace Umbra;

[Service]
internal sealed class UmbraBindings : IDisposable
{
    [ConfigVariable("IsFirstTimeStart.V3")]
    public static bool IsFirstTimeStart { get; set; } = true;
    
    [ConfigVariable("General.UiScale", "General", null, 50, 250)]
    public static int UiScale { get; set; } = 100;

    [ConfigVariable("General.UseGameMouseCursor", "General")]
    public static bool UseGameMouseCursor { get; set; } = true;

    [ConfigVariable("General.ShowInputControlDescriptions", "General")]
    public static bool ShowInputControlDescriptions { get; set; } = true;
    
    private ICommandManager _commandManager;

    private readonly IChatGui        _chatGui;
    private readonly WindowManager   _windowManager;
    private readonly WidgetManager   _widgetManager;

    public UmbraBindings(
        IChatGui chatGui,
        ICommandManager commandManager,
        WindowManager windowManager,
        WidgetManager widgetManager
    )
    {
        _chatGui        = chatGui;
        _commandManager = commandManager;
        _windowManager  = windowManager;
        _widgetManager  = widgetManager;
        
        _commandManager.AddHandler(
            "/umbra",
            new(HandleUmbraCommand) {
                HelpMessage = "打开 Umbra 设置窗口。",
                ShowInHelp  = true,
            }
        );

        _commandManager.AddHandler(
            "/umbra-aux",
            new(HandleUmbraCommand) {
                HelpMessage = "显示或隐藏辅助工具栏。用法：/umbra-aux <show|hide|toggle> [name]。",
                ShowInHelp  = true,
            }
        );
        
        _commandManager.AddHandler(
            "/umbra-toggle",
            new(HandleUmbraCommand) {
                HelpMessage = "切换指定 Umbra 设置。用法：/umbra-toggle <setting>。不带参数可查看设置列表。",
                ShowInHelp  = true,
            }
        );

        _commandManager.AddHandler(
            "/umbra-toolbar-profile",
            new(HandleUmbraCommand) {
                HelpMessage = "切换工具栏配置。用法：/umbra-toolbar-profile <profile name>。",
                ShowInHelp  = true,
            }
        );

        Framework.DalamudPlugin.UiBuilder.OpenConfigUi += OpenSettingsWindow;
        Framework.DalamudPlugin.UiBuilder.OpenMainUi   += OpenSettingsWindow;

        Node.ScaleFactor = 1.0f;

        // #if DEBUG
        // _windowManager.Present("UmbraSettings", new SettingsWindow());
        // #endif

        if (IsFirstTimeStart) {
            _windowManager.Present("Installer", new InstallerWindow());
        }
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler("/umbra");
        _commandManager.RemoveHandler("/umbra-toggle");
        _commandManager.RemoveHandler("/umbra-toolbar-profile");
        _commandManager.RemoveHandler("/umbra-aux");

        _commandManager = null!;

        Framework.DalamudPlugin.UiBuilder.OpenConfigUi -= OpenSettingsWindow;
        Framework.DalamudPlugin.UiBuilder.OpenMainUi   -= OpenSettingsWindow;
    }

    private void OpenSettingsWindow()
    {
        _windowManager.Present("UmbraSettings", new SettingsWindow());
    }

    [OnTick(interval: 60)]
    private void OnTick()
    {
        Node.ScaleFactor = (float)Math.Round(Math.Clamp(UiScale / 100f, 0.5f, 3.0f), 2);

        Framework.DalamudPlugin.UiBuilder.OverrideGameCursor = !UseGameMouseCursor;
    }

    private void HandleUmbraCommand(string command, string args)
    {
        switch (command.ToLower()) {
            case "/umbra":
                _windowManager.Present("UmbraSettings", new SettingsWindow());
                break;
            case "/umbra-toggle":
                string arg  = args.Trim();

                if (arg == string.Empty) {
                    ShowCvarToggleHelp();
                    return;
                }

                Cvar?  cvar = ConfigManager.GetCvar(arg);

                if (cvar is not { Default: bool }) {
                    _chatGui.PrintError($"Invalid setting: \"{arg}\".");
                    return;
                }

                ConfigManager.Set(cvar.Id, !(bool)cvar.Value!);
                break;
            case "/umbra-toolbar-profile":
                string profile = args.Trim();

                if (profile == string.Empty || !_widgetManager.GetProfileNames().Contains(profile)) {
                    _chatGui.PrintError("Usage: /umbra-toolbar-profile <profile name>.");
                    _chatGui.Print($"Available profiles: {String.Join(", ", _widgetManager.GetProfileNames())}");
                    return;
                }

                _widgetManager.ActivateProfile(profile);
                break;
            case "/umbra-aux":
                var parts  = args.Trim().Split(' ');
                var cmd = parts.FirstOrDefault();
                var name = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;
                
                if (cmd != "show" && cmd != "hide" && cmd != "toggle") {
                    _chatGui.PrintError("Usage: /umbra-aux <show|hide|toggle> [name].");
                    return;
                }

                var am = Framework.Service<AuxBarManager>();
                if (!am.NameExists(name)) {
                    _chatGui.PrintError($"Invalid auxiliary toolbar name: \"{name}\".");
                    _chatGui.Print($"Available toolbars: {String.Join(", ", am.All.Select(a => a.Name))}");
                    return;
                }
                
                am.ToggleByName(name, cmd == "toggle" ? null : cmd == "show");
                break;
        }
    }

    private void ShowCvarToggleHelp()
    {
        using var rssb = new RentedSeStringBuilder();

        foreach (string category in ConfigManager.GetCategories()) {
            if (!I18N.Has($"CVAR.Group.{category}")) continue;

            var cvars = ConfigManager.GetVariablesFromCategory(category).Where(c => c.Default is bool && I18N.Has($"CVAR.{c.Id}.Name")).ToList();
            if (cvars.Count == 0) continue;

            rssb.Builder.Append("Category: ")
                        .PushColorType(42)
                        .Append(I18N.Translate($"CVAR.Group.{category}"))
                        .PopColorType()
                        .Append("\n");

            foreach (var cvar in cvars) {
                rssb.Builder.Append("    \"")
                            .PushColorType(32)
                            .Append(cvar.Id)
                            .PopColorType()
                            .Append("\" - ")
                            .PushColorType(4)
                            .Append(I18N.Translate($"CVAR.{cvar.Id}.Name"))
                            .PopColorType()
                            .Append("\n");
            }
        }

        rssb.Builder.Append("Usage: ")
                    .PushColorType(32)
                    .Append("/umbra-toggle <setting>")
                    .PopColorType();
        _chatGui.Print(rssb.Builder.ToReadOnlySeString());
    }
}
