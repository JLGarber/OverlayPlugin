using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.IO;
using Advanced_Combat_Tracker;
using FFXIV_ACT_Plugin.Common;

namespace RainbowMage.OverlayPlugin
{
    /* Taken from FFIXV_ACT_Plugin.Logfile. Copy&pasted to avoid issues if the FFXIV plugin ever changes this enum. */
    public enum LogMessageType
    {
        LogLine,
        ChangeZone,
        ChangePrimaryPlayer,
        AddCombatant,
        RemoveCombatant,
        AddBuff,
        RemoveBuff,
        FlyingText,
        OutgoingAbility,
        IncomingAbility = 10,
        PartyList,
        PlayerStats,
        CombatantHP,
        ParsedPartyMember,
        NetworkStartsCasting = 20,
        NetworkAbility,
        NetworkAOEAbility,
        NetworkCancelAbility,
        NetworkDoT,
        NetworkDeath,
        NetworkBuff,
        NetworkTargetIcon,
        NetworkTargetMarker = 29,
        NetworkBuffRemove,
        NetworkGauge,
        NetworkWorld,
        Network6D,
        NetworkNameToggle,
        NetworkTether,
        NetworkLimitBreak,
        NetworkEffectResult,
        NetworkStatusList,
        NetworkUpdateHp,
        Settings = 249,
        Process,
        Debug,
        PacketDump,
        Version,
        Error,
        Timer
    }

    class FFXIVRepository
    {
        private readonly ILogger logger;
        private IDataRepository repository;
        private IDataSubscription subscription;

        public FFXIVRepository(TinyIoCContainer container)
        {
            logger = container.Resolve<ILogger>();
        }

        private IDataRepository GetRepository()
        {
            if (repository != null)
                return repository;

            var FFXIV = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(x => x.lblPluginTitle.Text == "FFXIV_ACT_Plugin.dll");
            if (FFXIV != null && FFXIV.pluginObj != null)
            {
                try
                {
                    repository = (IDataRepository)FFXIV.pluginObj.GetType().GetProperty("DataRepository").GetValue(FFXIV.pluginObj);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Error, Resources.FFXIVDataRepositoryException, ex);
                }
            }

            return repository;
        }

        private IDataSubscription GetSubscription()
        {
            if (subscription != null)
                return subscription;

            var FFXIV = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(x => x.lblPluginTitle.Text == "FFXIV_ACT_Plugin.dll");
            if (FFXIV != null && FFXIV.pluginObj != null)
            {
                try
                {
                    subscription = (IDataSubscription)FFXIV.pluginObj.GetType().GetProperty("DataSubscription").GetValue(FFXIV.pluginObj);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Error, Resources.FFXIVDataSubscriptionException, ex);
                }
            }

            return subscription;
        }

        private Process GetCurrentFFXIVProcessImpl()
        {
            var repo = GetRepository();
            if (repo == null) return null;

            return repo.GetCurrentFFXIVProcess();
        }

        [Obsolete("Subscribe to the ProcessChanged event instead (See RegisterProcessChangedHandler())")]
        public Process GetCurrentFFXIVProcess()
        {
            try
            {
                return GetCurrentFFXIVProcessImpl();
            } catch (FileNotFoundException)
            {
                // The FFXIV plugin isn't loaded
                return null;
            }
        }

        private bool IsFFXIVPluginPresentImpl()
        {
            return GetRepository() != null;
        }

        public bool IsFFXIVPluginPresent()
        {
            try
            {
                return IsFFXIVPluginPresentImpl();
            } catch (FileNotFoundException)
            {
                return false;
            }
        }

        public Version GetPluginVersion()
        {
            return typeof(IDataRepository).Assembly.GetName().Version;
        }

        public string GetPluginPath()
        {
            return typeof(IDataRepository).Assembly.Location;
        }

        private string GetGameVersionImpl()
        {
            return GetRepository()?.GetGameVersion();
        }

        public string GetGameVersion()
        {
            try
            {
                return GetGameVersionImpl();
            } catch (FileNotFoundException)
            {
                // The FFXIV plugin isn't loaded
                return null;
            }
        }

        public uint GetPlayerIDImpl()
        {
            var repo = GetRepository();
            if (repo == null) return 0;

            return repo.GetCurrentPlayerID();
        }

        public uint GetPlayerID()
        {
            try
            {
                return GetPlayerIDImpl();
            } catch (FileNotFoundException)
            {
                // The FFXIV plugin isn't loaded
                return 0;
            }
        }

        public string GetPlayerNameImpl()
        {
            var repo = GetRepository();
            if (repo == null) return null;

            var playerId = repo.GetCurrentPlayerID();

            var playerInfo = repo.GetCombatantList().FirstOrDefault(x => x.ID == playerId);
            if (playerInfo == null) return null;

            return playerInfo.Name;
        }

        public string GetPlayerName()
        {
            try
            {
                return GetPlayerNameImpl();
            }
            catch (FileNotFoundException)
            {
                // The FFXIV plugin isn't loaded
                return null;
            }
        }

        public ReadOnlyCollection<FFXIV_ACT_Plugin.Common.Models.Combatant> GetCombatants()
        {
            var repo = GetRepository();
            if (repo == null) return null;

            return repo.GetCombatantList();
        }

        public Language GetLanguage()
        {
            var repo = GetRepository();
            if (repo == null)
                return Language.Unknown;
            return repo.GetSelectedLanguageID();
        }

        public string GetLocaleString()
        {
            switch (GetLanguage())
            {
                case Language.English:
                    return "en";
                case Language.French:
                    return "fr";
                case Language.German:
                    return "de";
                case Language.Japanese:
                    return "ja";
                case Language.Chinese:
                    return "cn";
                case Language.Korean:
                    return "ko";
                default:
                    return null;
            }
        }

        // LogLineDelegate(uint EventType, uint Seconds, string logline);
        public void RegisterLogLineHandler(Action<uint, uint, string> handler)
        {
            var sub = GetSubscription();
            if (sub != null)
                sub.LogLine += new LogLineDelegate(handler);
        }

        // NetworkReceivedDelegate(string connection, long epoch, byte[] message)
        public void RegisterNetworkParser(Action<string, long, byte[]> handler)
        {
            var sub = GetSubscription();
            if (sub != null)
                sub.NetworkReceived += new NetworkReceivedDelegate(handler);
        }

        // PartyListChangedDelegate(ReadOnlyCollection<uint> partyList, int partySize)
        //
        // Details: partySize may differ from partyList.Count.
        // In non-cross world parties, players who are not in the same
        // zone count in the partySize but do not appear in the partyList.
        // In cross world parties, nobody will appear in the partyList.
        // Alliance data members show up in partyList but not in partySize.
        public void RegisterPartyChangeDelegate(Action<ReadOnlyCollection<uint>, int> handler)
        {
            var sub = GetSubscription();
            if (sub != null)
                sub.PartyListChanged += new PartyListChangedDelegate(handler);
        }

        // ProcessChangedDelegate(Process process)
        public void RegisterProcessChangedHandler(Action<Process> handler)
        {
            var sub = GetSubscription();
            if (sub != null)
            {
                sub.ProcessChanged += new ProcessChangedDelegate(handler);
                /*
                var repo = GetRepository();
                if (repo != null)
                {
                    var process = repo.GetCurrentFFXIVProcess();
                    if (process != null) handler(process);
                }*/
            }
        }
    }
}
