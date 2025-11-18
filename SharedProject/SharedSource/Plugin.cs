using Barotrauma;
using HarmonyLib;

namespace TraumaticPresence;

public partial class Plugin : IAssemblyPlugin
{
    private static readonly Harmony harmony = new("lenny.barotrauma.traumaticpresence");

    public void Initialize()
    {
        InitClient();
    }

    public void OnLoadCompleted()
    {
    }

    public void PreInitPatching()
    {
        harmony.PatchAll();
    }

    public void Dispose()
    {
        RpcClient.Dispose();
        Timer.Dispose();
        harmony.UnpatchSelf();
    }
}