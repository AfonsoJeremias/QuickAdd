using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using QuickAdd.Services;
using QuickAdd.UI;
using System;
using System.Collections.Generic;

namespace QuickAdd
{
    public class QuickAddPlugin : GenericPlugin
    {
        public override Guid Id { get; } = new Guid("c1a5e0d2-7b3f-4e6a-9d18-5f2b8a4c7e91");

        public QuickAddPlugin(IPlayniteAPI api) : base(api) { MoveService.Init(api); }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Add a game or app...",
                MenuSection = "Quick Add",
                Action = a => new AddSession(PlayniteApi, GetPluginUserDataPath()).Run()
            };
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            PickerService.RouteController(args.Button, args.State == ControllerInputState.Pressed);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            PickerService.Configure(PlayniteApi, GetPluginUserDataPath());
            IconService.Cleanup(GetPluginUserDataPath()); // only removes this plugin's own leftovers
        }
    }
}
