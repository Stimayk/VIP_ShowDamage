using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Localization;
using System.Collections.Concurrent;
using VipCoreApi;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace VIP_ShowDamage
{
    public class VIP_ShowDamage : BasePlugin
    {
        public override string ModuleName => "[VIP] Show Damage";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IVipCoreApi? _api = null!;
        private ShowDamage _showDamage = null!;

        private PluginCapability<IVipCoreApi> PluginCapability { get; } = new("vipcore:core");

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _api = PluginCapability.Get();
            if (_api == null)
            {
                return;
            }

            _api.OnCoreReady += () =>
            {
                _showDamage = new ShowDamage(_api, Localizer);

                RegisterEventHandler<EventPlayerHurt>(_showDamage.OnPlayerHurt);
                RegisterListener<Listeners.OnTick>(_showDamage.ShowDamageMessages);

                _api.RegisterFeature(_showDamage);
            };
        }

        public override void Unload(bool hotReload)
        {
            foreach (Timer timer in _showDamage.deleteTimers.Values)
            {
                timer.Kill();
            }
            _showDamage.deleteTimers.Clear();
            _showDamage.messages.Clear();

            DeregisterEventHandler<EventPlayerHurt>(_showDamage.OnPlayerHurt);
            RemoveListener<Listeners.OnTick>(_showDamage.ShowDamageMessages);

            _api?.UnRegisterFeature(_showDamage);
        }
    }
    public class ShowDamage(IVipCoreApi api, IStringLocalizer localizer) : VipFeatureBase(api), IPluginConfig<VIP_ShowDamageConfig>
    {
        public override string Feature => "ShowDamage";

        public readonly ConcurrentDictionary<CCSPlayerController, string> messages = new();
        public readonly ConcurrentDictionary<CCSPlayerController, Timer> deleteTimers = new();

        public VIP_ShowDamageConfig Config { get; set; } = new();

        public void OnConfigParsed(VIP_ShowDamageConfig config)
        {
            Config = config;
        }

        [GameEventHandler()]
        public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? userid = @event.Userid;

            if (attacker == null || !attacker.IsValid || attacker.Connected != PlayerConnectedState.PlayerConnected || attacker.IsBot || attacker.IsHLTV || attacker == userid || attacker.TeamNum == userid?.TeamNum || !PlayerHasFeature(attacker) || GetPlayerFeatureState(attacker) != IVipCoreApi.FeatureState.Enabled)
            {
                return HookResult.Continue;
            }

            int dmgHealth = @event.DmgHealth;
            int health = @event.Health;
            LocalizedString hudMessage = localizer["HUD", dmgHealth, userid?.PlayerName ?? "Unknown", health];

            ManageTimerAndMessage(attacker, hudMessage);

            return HookResult.Continue;
        }

        private void ManageTimerAndMessage(CCSPlayerController attacker, string hudMessage)
        {
            if (deleteTimers.TryRemove(attacker, out Timer? timer))
            {
                timer.Kill();
            }

            messages[attacker] = hudMessage;
            Timer newTimer = new(Config.NotifyDuration, () =>
            {
                if (messages.TryRemove(attacker, out _) && deleteTimers.TryRemove(attacker, out Timer? removeTimer))
                {
                    removeTimer?.Kill();
                }
            });

            deleteTimers[attacker] = newTimer;
        }

        public void ShowDamageMessages()
        {
            foreach (KeyValuePair<CCSPlayerController, string> entry in messages)
            {
                PrintHtml(entry.Key, entry.Value);
            }
        }

        private static void PrintHtml(CCSPlayerController player, string hudContent)
        {
            EventShowSurvivalRespawnStatus eventShowSurvivalRespawnStatus = new(false)
            {
                LocToken = hudContent,
                Duration = 5L,
                Userid = player
            };
            try
            {
                eventShowSurvivalRespawnStatus.FireEvent(false);
            }
            catch (NativeException ex)
            {
                Console.WriteLine($"Failed to fire event: {ex.Message}");
            }
        }
    }
}