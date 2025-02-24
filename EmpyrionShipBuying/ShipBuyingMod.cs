﻿using Eleon.Modding;
using Eleon.Pda;
using EmpyrionNetAPIAccess;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EmpyrionShipBuying.Configuration;

namespace EmpyrionShipBuying
{
    public class ShipBuyingMod : EmpyrionModBase
    {
        public ConfigurationManager<Configuration> Configuration { get; set; }
        public ModGameAPI GameAPI { get; private set; }

        public ShipBuyingMod()
        {
            EmpyrionConfiguration.ModName = "EmpyrionShipBuying";
        }

        public override void Initialize(ModGameAPI dediAPI)
        {
            GameAPI = dediAPI;

            Log("**EmpyrionShipBuying: loaded", LogLevel.Message);

            LoadConfiguration();
            LogLevel = Configuration.Current.LogLevel;
            ChatCommandManager.CommandPrefix = Configuration.Current.ChatCommandPrefix;

            Event_Entity_PosAndRot += ShipBuyingMod_Event_Entity_PosAndRot;

            ChatCommands.Add(new ChatCommand(@"ship help",                                     (I, A) => DisplayCatalog(I.playerId), "display help"));
            ChatCommands.Add(new ChatCommand(@"ship catalog",                                  (I, A) => DisplayHelp(I.playerId, S => true), "display help with full catalog"));
            ChatCommands.Add(new ChatCommand(@"ship buy (?<number>\d*)",                       (I, A) => ShipBuy (I, A, true), "buy the ship with the (number)"));
            ChatCommands.Add(new ChatCommand(@"ship sell (?<id>\d+) (?<price>\d+)",            (I, A) => ShipSell(I, A, TransactionType.PlayerToPlayer), "sell the ship with id (id) from your position", Configuration.Current.SellPermission));
            ChatCommands.Add(new ChatCommand(@"ship cancel (?<number>\d+)",                    (I, A) => ShipBuy (I, A, false), "get your ship back", Configuration.Current.SellPermission));
            ChatCommands.Add(new ChatCommand(@"ship add (?<id>\d+) (?<price>\d+)",             (I, A) => ShipSell(I, A, TransactionType.Catalog), "add the ship with id (id) from your position to the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship addstarter (?<id>\d+) (?<price>\d+)",      (I, A) => ShipSell(I, A, TransactionType.CanOnlyBuyOnce), "add the ship with id (id) to the catalog which each player can only buy once", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship rename (?<number>\d+) (?<name>.+)",        (I, A) => ShipRename(I, A), "rename the ship with id (id) in the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship price (?<number>\d+) (?<price>\d+)",       (I, A) => ShipPrice(I, A), "set new price of the ship with id (id) in the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship remove (?<number>\d+)",                    (I, A) => ShipRemove(I, A), "remove a ship with id (id) from the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship profit",                                   (I, A) => ShipGetSaleProfit(I), "get your sale profit"));
        }

        private Task ShipRemove(ChatInfo chatinfo, Dictionary<string, string> arguments)
        {
            var buyship = GetShipFromBuyId(arguments, Configuration.Current.Ships);
            if (buyship == null)
            {
                InformPlayer(chatinfo.playerId, $"select a ship number");
                return Task.CompletedTask;
            }

            Configuration.Current.Ships.Remove(buyship);
            try { Directory.Delete(Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", buyship.StructureDirectoryOrEPBName), true); } catch { }

            Configuration.Save();
            EnumShipBuyIDs();

            return Task.CompletedTask;
        }

        private async void ShipBuyingMod_Event_Entity_PosAndRot(IdPositionRotation obj)
        {
            var found = Configuration.Current.Ships.FirstOrDefault(S => S.CurrentId == obj.id);
            if (found == null) return;

            if(Distance(found.SpawnLocation.pos, obj.pos) > Configuration.Current.MaxBuyingPosDistance)
            {
                if (found.TransactionType == TransactionType.Catalog)
                {
                    found.CurrentId = await CreateStructure(found, new PlayerInfo(), found.SpawnLocation);
                    Configuration.Save();
                    EnumShipBuyIDs();
                    return;
                }

                try
                {
                    var info = await Request_GlobalStructure_Info(obj.id.ToId());
                    Log($"Ship moved: {found.DisplayName} [{found.CurrentId}] -> {found.SpawnLocation.playfield} [{found.SpawnLocation.pos}] to {info.PlayfieldName} [{obj.pos}]", LogLevel.Message);

                    if (info.PlayfieldName != found.SpawnLocation.playfield)
                    {
                        try{ await Request_Load_Playfield(new PlayfieldLoad(60, found.SpawnLocation.playfield, 0)); } catch { }
                        await Request_Entity_ChangePlayfield(new IdPlayfieldPositionRotation(found.CurrentId, found.SpawnLocation.playfield, found.SpawnLocation.pos, found.SpawnLocation.rot));
                    }
                    else await Request_Entity_Teleport(new IdPositionRotation(found.CurrentId, found.SpawnLocation.pos, found.SpawnLocation.rot));
                }
                catch (Exception error)
                {
                    Log($"Ship moved: {found.DisplayName} [{found.CurrentId}] -> {error}", LogLevel.Error);
                }
            }
        }

        private async Task ShipPrice(ChatInfo chatinfo, Dictionary<string, string> arguments)
        {
            if (int.TryParse(arguments["price"], out int price)) return;

            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var buyship = GetShipFromBuyId(arguments, Configuration.Current.Ships);
            if (buyship == null)
            {
                InformPlayer(chatinfo.playerId, $"select a ship number");
                return;
            }

            buyship.Price = price;

            Configuration.Save();
            EnumShipBuyIDs();
        }

        private async Task ShipRename(ChatInfo chatinfo, Dictionary<string, string> arguments)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var buyship = GetShipFromBuyId(arguments, Configuration.Current.Ships);
            if (buyship == null)
            {
                InformPlayer(chatinfo.playerId, $"select a ship number");
                return;
            }

            buyship.DisplayName = arguments["name"];

            Configuration.Save();
            EnumShipBuyIDs();
        }

        private async Task ShipGetSaleProfit(ChatInfo chatinfo)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            if (!Configuration.Current.SaleProfits.ContainsKey(P.steamId))
            {
                InformPlayer(chatinfo.playerId, $"no sales profits found for you");
                Log($"No sales profit found for {P.playerName}", LogLevel.Message);
                return;
            }

            var profit = Configuration.Current.SaleProfits[P.steamId];
            await Request_Player_SetCredits(new IdCredits(P.entityId, P.credits + profit));

            InformPlayer(chatinfo.playerId, $"congratulations you get {profit} credits");
            Log($"Player get profit {P.playerName} => {profit}", LogLevel.Message);
        }

        private async Task DisplayCatalog(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());
            await DisplayHelp(playerId, S => 
                (S.SpawnLocation.playfield == P.playfield && Distance(S.SpawnLocation.pos, P.pos) <= Configuration.Current.MaxBuyingPosDistance)
            );
        }

        private async Task ShipBuy(ChatInfo chatinfo, Dictionary<string, string> arguments, bool buy)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var shipsToBuyFromPosition = Configuration.Current.Ships
                .Where(S => S.SpawnLocation.playfield == P.playfield || S.TransactionType == TransactionType.CanOnlyBuyOnce)
                .OrderByDescending(S => S.TransactionType)
                .OrderBy(S => S.EntityType)
                .OrderBy(S => S.DisplayName)
                .Where(S => S.TransactionType == TransactionType.CanOnlyBuyOnce || Distance(S.SpawnLocation.pos, P.pos) <= Configuration.Current.MaxBuyingPosDistance).ToArray();

            if (shipsToBuyFromPosition.Length == 0)
            {
                InformPlayer(chatinfo.playerId, $"no ships to buy from this position prease go to a 'ship buy position'");
                Log($"Ship buy at wrong position {P.playfield} [X:{P.pos.x} Y:{P.pos.y} Z:{P.pos.z}] start", LogLevel.Message);
                return;
            }

            var buyship = GetShipFromBuyId(arguments, shipsToBuyFromPosition);

            if (buyship == null)
            {
                InformPlayer(chatinfo.playerId, $"select a ship number");
                return;
            }

            if (!buy && buyship.SellerSteamId == P.steamId) buyship.Price = (buyship.Price * Configuration.Current.CancelPercentageSaleFee) / 100;

            if (P.credits < buyship.Price)
            {
                InformPlayer(chatinfo.playerId, $"the ship costs {buyship.Price} but you have only {P.credits} sorry.");
                return;
            }

            var answer = await ShowDialog(chatinfo.playerId, P,
                $"Are you sure you want to buy this Ship",
                $"\"{buyship.DisplayName}\"\nfor [c][ffffff]{buyship.Price}[-][/c] Credits from {buyship.Seller}\n\n{buyship.ShipDetails}", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            Log($"Ship buy {buyship.DisplayName} at {buyship.SpawnLocation.playfield} start", LogLevel.Message);

            if ((buyship.TransactionType == TransactionType.Catalog && buyship.SpawnLocation.playfield != P.playfield) || buyship.TransactionType == TransactionType.CanOnlyBuyOnce)
            {
                if (buyship.TransactionType == TransactionType.CanOnlyBuyOnce)
                {
                    if (buyship.PurchasedFromSteamId.Contains(P.steamId))
                    {
                        await ShowDialog(chatinfo.playerId, P, "OneTimeSell", $"your already purchased this ship \"{buyship.DisplayName}\"");
                        return;
                    }

                    buyship.PurchasedFromSteamId.Add(P.steamId);
                }

                await CreateStructure(buyship, P, new PlayfieldPositionRotation() { playfield = P.playfield, pos = new PVector3(P.pos.x, P.pos.y + 100, P.pos.z) });
            }
            else if (buyship.CurrentId != 0)
            {
                await Request_Entity_SetName(new IdPlayfieldName(buyship.CurrentId, buyship.SpawnLocation.playfield, $"{buyship.DisplayName} for {P.playerName}"));
                await Request_ConsoleCommand(new PString($"faction entity 'Public' {buyship.CurrentId}"));

                if (buyship.TransactionType == TransactionType.PlayerToPlayer) buyship.CurrentId = 0;
            }
            else await CreateStructure(buyship, P, buyship.SpawnLocation);

            Log($"Ship buy {P.playerName}: {P.credits} - {buyship.Price} = {P.credits - buyship.Price}", LogLevel.Message);
            await Request_Player_SetCredits(new IdCredits(P.entityId, P.credits - buyship.Price));

            Log($"Ship buy {buyship.DisplayName} at {buyship.SpawnLocation.playfield} complete", LogLevel.Message);

            if (buyship.TransactionType == TransactionType.PlayerToPlayer)
            {
                Configuration.Current.Ships.Remove(buyship);

                if (buy)
                {
                    if (!Configuration.Current.SaleProfits.ContainsKey(buyship.SellerSteamId)) Configuration.Current.SaleProfits.Add(buyship.SellerSteamId, buyship.Price);
                    else Configuration.Current.SaleProfits[buyship.SellerSteamId] = Configuration.Current.SaleProfits[buyship.SellerSteamId] + buyship.Price;
                }

                try { Directory.Delete(Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", buyship.StructureDirectoryOrEPBName), true); } catch { }
            }

            Configuration.Save();
            EnumShipBuyIDs();

            await ShowDialog(chatinfo.playerId, P, "Congratulations", $"your new ship \"{buyship.DisplayName} for {P.playerName}\" is ready for pick-up at {(buyship.TransactionType == TransactionType.CanOnlyBuyOnce ? " your position " : buyship.SpawnLocation.playfield)}[X:{(int)buyship.SpawnLocation.pos.x} Y:{(int)buyship.SpawnLocation.pos.y} Z:{(int)buyship.SpawnLocation.pos.z}]");
        }

        private static ShipInfo GetShipFromBuyId(Dictionary<string, string> arguments, IEnumerable<ShipInfo> shipsToBuyFromPosition) 
            => int.TryParse(arguments["number"], out int number)
                ? shipsToBuyFromPosition.FirstOrDefault(s => s.BuyId == number)
                : null;

        public async Task<int> CreateStructure(ShipInfo ship, PlayerInfo player, PlayfieldPositionRotation spawnPoint)
        {
            var NewID = await Request_NewEntityId();

            var epbBackupFile = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", ship.StructureDirectoryOrEPBName, "backup.epb");
            var factions = await Request_Get_Factions(new Id(1));
            var adminFactionId = factions.factions.FirstOrDefault(F => F.abbrev == "Adm").factionId;

            var SpawnInfo = new EntitySpawnInfo()
            {
                forceEntityId       = NewID.id,
                playfield           = spawnPoint.playfield,
                pos                 = spawnPoint.pos,
                rot                 = spawnPoint.rot,
                name                = player.entityId == 0 ? ship.DisplayName : $"{ship.DisplayName} for {player.playerName}",
                type                = (byte)ship.EntityType,
                entityTypeName      = "", // 'Kommentare der Devs:  ...or set this to f.e. 'ZiraxMale', 'AlienCivilian1Fat', etc
                factionGroup        = (byte)(player.entityId == 0 ? FactionGroup.Admin : FactionGroup.Player),
                factionId           =        player.entityId == 0 ? adminFactionId : player.entityId,
            };

            if (File.Exists(epbBackupFile))
            {
                SpawnInfo.prefabName = "backup";
                SpawnInfo.prefabDir  = Path.GetDirectoryName(epbBackupFile);
            }
            else if (string.Compare(Path.GetExtension(ship.StructureDirectoryOrEPBName), ".epb", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                SpawnInfo.prefabName = Path.GetFileNameWithoutExtension (ship.StructureDirectoryOrEPBName);
                SpawnInfo.prefabDir  = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData");
            }
            else
            {
                var SourceDir = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", ship.StructureDirectoryOrEPBName);
                var TargetDir = Path.Combine(EmpyrionConfiguration.SaveGamePath,    "Shared", $"{NewID.id}");

                var exportDat = Path.Combine(SourceDir, "Export.dat");
                exportDat = File.Exists(exportDat) ? exportDat : Path.Combine(SourceDir, "ents.dat");

                SpawnInfo.exportedEntityDat = File.Exists(exportDat) ? exportDat : null;

                Directory.CreateDirectory(Path.GetDirectoryName(TargetDir));
                CopyAll(new DirectoryInfo(SourceDir), new DirectoryInfo(TargetDir));
            }

            try { await Request_Load_Playfield(new PlayfieldLoad(20, ship.SpawnLocation.playfield, 0)); }
            catch { }  // Playfield already loaded

            Log($"Ship spawn {ship.DisplayName} name:{SpawnInfo.name} forceEntityId:{SpawnInfo.forceEntityId} playfield:{SpawnInfo.playfield} pos: x:{SpawnInfo.pos.x} y:{SpawnInfo.pos.y} z:{SpawnInfo.pos.z} exportedEntityDat:{SpawnInfo.exportedEntityDat} prefabName:{SpawnInfo.prefabName} prefabDir:{SpawnInfo.prefabDir} call", LogLevel.Message);

            await Request_Entity_Spawn(SpawnInfo);
            try
            {
                await Request_Structure_Touch(NewID); // Sonst wird die Struktur sofort wieder gelöscht !!!
            }
            catch (Exception error)
            {
                Log($"Request_Structure_Touch: {error}", LogLevel.Error);
            }
            

            return NewID.id;
        }

        private double Distance(PVector3 pos1, PVector3 pos2)
        {
            return Math.Sqrt(Math.Pow(pos1.x - pos2.x, 2) + Math.Pow(pos1.y - pos2.y, 2) + Math.Pow(pos1.z - pos2.z, 2));
        }

        private async Task ShipSell(ChatInfo chatinfo, Dictionary<string, string> arguments, TransactionType transactionType)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            if (Configuration.Current.ForbiddenPlayfields.Contains(P.playfield))
            {
                InformPlayer(chatinfo.playerId, $"no ship selling allowed in this playfield");
                return;
            }

            var ship = await SearchEntity(int.Parse(arguments["id"]));
            if(ship.id == 0)
            {
                InformPlayer(chatinfo.playerId, $"no ship found with id {arguments["id"]}");
                return;
            }

            double.TryParse(arguments["price"], out double price);
            var playerOwnsThisShip = ship.factionGroup == 0 && ship.factionId == P.factionId;
            playerOwnsThisShip = playerOwnsThisShip || (ship.factionGroup == 1 && ship.factionId == P.entityId);

            if (!playerOwnsThisShip)
            {
                InformPlayer(chatinfo.playerId, $"this ship {ship.id}/{ship.name} is not yours");
                return;
            }

            var answer = await ShowDialog(chatinfo.playerId, P, $"Are you sure you want to {(transactionType == TransactionType.PlayerToPlayer ? "sell" : "add")}", $"[c][00ff00]\"{ship.name}\"[-][/c] for [c][ffffff]{price}[-][/c] Credits?\n\n[c][ff0000]Note: Is the repair template up to date!![-][/c]\n", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            Log($"Ship sell {ship.id}/{ship.name} at {P.playfield} start", LogLevel.Message);

            var sourceDataDir = Path.Combine(EmpyrionConfiguration.SaveGamePath,    "Shared", $"{ship.id}");
            var targetDataDir = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", $"{(EntityType)ship.type}_{ship.id}");
            var targetDataExportDat = Path.Combine(targetDataDir, "Export.dat");

            Directory.CreateDirectory(targetDataDir);

            Configuration.Current.Ships.Add(new Configuration.ShipInfo() {
                DisplayName                 = ship.name,
                Price                       = price,
                EntityType                  = (EntityType)ship.type,
                Seller                      = P.playerName,
                SellerSteamId               = transactionType == TransactionType.PlayerToPlayer ? P.steamId : string.Empty,
                StructureDirectoryOrEPBName = Path.GetFileName(targetDataDir),
                SpawnLocation               = new PlayfieldPositionRotation() { playfield = P.playfield, pos = ship.pos, rot = ship.rot },
                TransactionType             = transactionType,
                ShipDetails                 = $"{(EntityType)ship.type} Class:{ship.classNr} Blocks:{ship.cntBlocks} Devices:{ship.cntDevices} Lights:{ship.cntLights} Triangles:{ship.cntTriangles}",
                CurrentId                   = ship.id,
            });

            Configuration.Save();
            EnumShipBuyIDs();

            await Request_Entity_Export(new EntityExportInfo()
            {
                id              = P.entityId,
                playfield       = P.playfield,
                filePath        = targetDataExportDat,
                isForceUnload   = false,
            });

            await Request_ConsoleCommand(new PString($"faction entity 'Adm' {ship.id}"));
            // await Request_Entity_Destroy(ship.id.ToId());

            Thread.Sleep(5000);

            CopyAll(new DirectoryInfo(sourceDataDir), new DirectoryInfo(targetDataDir));

            Log($"Ship sell {ship.id}/{ship.name} at {P.playfield} complete", LogLevel.Message);
        }

        public static void CopyAll(DirectoryInfo aSource, DirectoryInfo aTarget)
        {
            aSource.GetDirectories().AsParallel().ForAll(D =>
            {
                try { aTarget.CreateSubdirectory(D.Name); } catch { }
                CopyAll(D, new DirectoryInfo(Path.Combine(aTarget.FullName, D.Name)));
            });
            aSource.GetFiles().AsParallel().ForAll(F => {
                Directory.CreateDirectory(aTarget.FullName);
                try { F.CopyTo(Path.Combine(aTarget.FullName, F.Name), true); } catch { }
            });
        }

        public async Task<GlobalStructureInfo> SearchEntity(int aSourceId) => await Request_GlobalStructure_Info(aSourceId.ToId());
        private void LoadConfiguration()
        {
            Configuration = new ConfigurationManager<Configuration>()
            {
                ConfigFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipConfigurations.json")
            };

            Configuration.ConfigFileLoaded += (s, e) => EnumShipBuyIDs();

            Configuration.Load();
            Configuration.Save();
        }

        private void EnumShipBuyIDs()
        {
            int i = 1;
            Configuration.Current.Ships
                .OrderByDescending(S => S.TransactionType)
                .ThenBy(S => S.SpawnLocation.playfield)
                .ThenBy(S => S.EntityType)
                .ThenBy(S => S.DisplayName)
                .ToList()
                .ForEach(ship => ship.BuyId = i++);
        }

        private async Task DisplayHelp(int aPlayerId, Func<ShipInfo, bool> customSelector)
        {
            await DisplayHelp(aPlayerId,
                    Configuration.Current.Ships
                    .Where(customSelector)
                    .OrderBy(S => S.SpawnLocation.playfield)
                    .GroupBy(S => S.SpawnLocation.playfield)
                    .Aggregate(Configuration.Current.Ships
                        .Where(S => S.TransactionType == TransactionType.CanOnlyBuyOnce)
                        .OrderBy(S => S.EntityType)
                        .OrderBy(S => S.DisplayName)
                            .Aggregate($"\n[c][00ffff]Starterships:[-][/c]\n", (l, s) => l + $"[c][00ffff]{s.BuyId}:[-][/c] [c][ffffff][{s.EntityType}][-][/c] [c][00ff00]\"{s.DisplayName}\"[-][/c]{(string.IsNullOrEmpty(s.SellerSteamId) ? "" : $" from [c][00ff00]{s.Seller}[-][/c]")} {(s.TransactionType == TransactionType.CanOnlyBuyOnce ? "one-time purchase from everyware " : $"near to buy at [[c][ff00ff]X:{(int)s.SpawnLocation.pos.x} Y:{(int)s.SpawnLocation.pos.y} Z:{(int)s.SpawnLocation.pos.z}[-][/c]]")} for [c][ffffff]{s.Price}[-][/c] credits\n")
                        , 
                        (p, g) => Configuration.Current.Ships
                        .Where(S => S.SpawnLocation.playfield == g.Key && S.TransactionType != TransactionType.CanOnlyBuyOnce)
                        .OrderBy(S => S.EntityType)
                        .OrderBy(S => S.DisplayName)
                            .Aggregate(p + $"\n[c][00ffff]Ships at {g.Key}:[-][/c]\n", (l, s) => l + $"[c][00ffff]{s.BuyId}:[-][/c] [c][ffffff][{s.EntityType}][-][/c] [c][00ff00]\"{s.DisplayName}\"[-][/c]{(string.IsNullOrEmpty(s.SellerSteamId) ? "" : $" from [c][00ff00]{s.Seller}[-][/c]")} {(s.TransactionType == TransactionType.CanOnlyBuyOnce ? "one-time purchase from everyware " : $"near to buy at [[c][ff00ff]X:{(int)s.SpawnLocation.pos.x} Y:{(int)s.SpawnLocation.pos.y} Z:{(int)s.SpawnLocation.pos.z}[-][/c]]")} for [c][ffffff]{s.Price}[-][/c] credits\n")
                    )
            );
        }

    }
}
