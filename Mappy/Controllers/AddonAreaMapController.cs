using System;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using KamiLib.CommandManager;
using KamiLib.Extensions;
using Mappy.Extensions;
using Mappy.Windows;

namespace Mappy.Controllers;

public unsafe class AddonAreaMapController : IDisposable
{
    public AddonAreaMapController()
    {
        Service.Log.Debug("Beginning Listening for AddonAreaMap");
        Service.Framework.Update += AddonAreaMapListener;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "AreaMap", OnAreaMapDraw);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PreShow, "AreaMap", OnAreaMapPreShow);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreHide, "AreaMap", OnAreaMapPreHide);

        // Add a special error handler for the case that somehow the addon is stuck offscreen
        System.CommandManager.RegisterCommand(new CommandHandler
        {
            ActivationPath = "/areamap/reset",
            Delegate = _ =>
            {
                var addon = Service.GameGui.GetAddonByName<AddonAreaMap>("AreaMap");
                if (addon is not null && addon->RootNode is not null)
                {
                    addon->RootNode->SetPositionFloat(addon->X, addon->Y);
                }
            },
        });
    }

    private void AddonAreaMapListener(IFramework framework)
    {
        var addonAreaMap = Service.GameGui.GetAddonByName<AddonAreaMap>("AreaMap");

        if (addonAreaMap is null) return;

        // addonAreaMap->OpenSoundEffectId = 0;
        // addonAreaMap->Flags1A2 |= (byte)(1 << BitOperations.Log2(0x20));

        Service.Framework.Update -= AddonAreaMapListener;
    }

    public void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(OnAreaMapDraw);
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PreShow, "AreaMap");
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PreHide, "AreaMap");
        Service.Framework.Update -= AddonAreaMapListener;

        // Reset windows root node position on dispose
        var addonAreaMap = Service.GameGui.GetAddonByName<AddonAreaMap>("AreaMap");
        if (addonAreaMap is not null)
        {
            addonAreaMap->RootNode->SetPositionFloat(addonAreaMap->X, addonAreaMap->Y);
            // addonAreaMap->OpenSoundEffectId = 23;
            // addonAreaMap->Flags1A2 &= (byte)~(1 << BitOperations.Log2(0x20));
        }
    }

    // public void EnableIntegrations()
    // {
    //     Service.Log.Debug("Enabling Area Map Integrations");
    // }
    //
    // //
    // public void DisableIntegrations()
    // {
    //     Service.Log.Debug("Disabling Area Map Integrations");
    // }
    //

    private void OnAreaMapPreShow(AddonEvent type, AddonArgs args)
    {
        Service.Log.Verbose($"[AreaMap] AddonEventPreShow");
        System.WindowManager.GetWindow<MapWindow>()?.Open();
    }

    private void OnAreaMapPreHide(AddonEvent type, AddonArgs args)
    {
        Service.Log.Verbose($"[AreaMap] AreaMapPreHide");

        if (System.SystemConfig.KeepOpen)
        {
            Service.Log.Verbose("[AreaMap] Keeping Open");
            return;
        }

        // If the window actually considered closed by the agent.
        if (AgentMap.Instance()->AddonId is 0)
        {
            System.WindowManager.GetWindow<MapWindow>()?.Close();
        }
    }
    

    private void OnAreaMapDraw(AddonEvent type, AddonArgs args)
    {
        var addon = args.GetAddon<AddonAreaMap>();

        if (Service.ClientState is { IsPvP: true })
        {
            if (addon->IsOffscreen())
                addon->RestorePosition();
            return;
        }

        // Have to check for color, because it likes to animate a fadeout,
        // and we want the map to stay completely hidden until it's done.
        if (addon->IsVisible || addon->RootNode->Color.A is not 0x00)
        {
            addon->ForceOffscreen();

            return;
        }

        // only if the window is actually closed
        if (AgentMap.Instance()->AddonId is 0)
        {
            addon->RestorePosition();
        }
    }
}