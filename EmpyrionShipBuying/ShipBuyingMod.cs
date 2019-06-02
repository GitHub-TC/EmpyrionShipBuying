using Eleon.Modding;
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

            log("**EmpyrionShipBuying: loaded", LogLevel.Message);

            LoadConfiguration();
            LogLevel = Configuration.Current.LogLevel;
            ChatCommandManager.CommandPrefix = Configuration.Current.ChatCommandPrefix;

            ChatCommands.Add(new ChatCommand(@"ship help",                                     (I, A) => DisplayCatalog(I.playerId), "display help"));
            ChatCommands.Add(new ChatCommand(@"ship catalog",                                  (I, A) => DisplayHelp(I.playerId, S => true), "display help with full catalog"));
            ChatCommands.Add(new ChatCommand(@"ship buy (?<number>\d*)",                       (I, A) => ShipBuy (I, A, true), "buy the ship with the (number)"));
            ChatCommands.Add(new ChatCommand(@"ship sell (?<id>\d+) (?<price>\d+)",            (I, A) => ShipSell(I, A, true), "sell the ship with id (id) from your position", Configuration.Current.SellPermission));
            ChatCommands.Add(new ChatCommand(@"ship cancel (?<number>\d+)",                    (I, A) => ShipBuy (I, A, false), "get your ship back", Configuration.Current.SellPermission));
            ChatCommands.Add(new ChatCommand(@"ship add (?<id>\d+) (?<price>\d+)",             (I, A) => ShipSell(I, A, false), "add the ship with id (id) from your position to the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship rename (?<number>\d+) (?<name>.+)",        (I, A) => ShipRename(I, A), "rename the ship with id (id) in the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship price (?<number>\d+) (?<price>\d+)",       (I, A) => ShipPrice(I, A), "set new price of the ship with id (id) in the catalog", Configuration.Current.AddPermission));
            ChatCommands.Add(new ChatCommand(@"ship profit",                                   (I, A) => ShipGetSaleProfit(I), "get your sale profit"));
        }

        private async Task ShipPrice(ChatInfo chatinfo, Dictionary<string, string> arguments)
        {
            if (int.TryParse(arguments["price"], out int price)) return;

            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var shipsToBuyFromPosition = Configuration.Current.Ships
                .OrderBy(S => S.EntityType)
                .OrderBy(S => S.DisplayName)
                .Where(S => Distance(S.BuyLocation.pos, P.pos) <= Configuration.Current.MaxBuyingPosDistance).ToArray();

            if (shipsToBuyFromPosition.Length == 0)
            {
                InformPlayer(chatinfo.playerId, $"no ships available from this position prease go to a 'ship buy position'");
                log($"Ship buy at wrong position {P.playfield} [X:{P.pos.x} Y:{P.pos.y} Z:{P.pos.z}] start", LogLevel.Message);
                return;
            }

            if (int.TryParse(arguments["number"], out int number))
            {
                if (number <= 0 || number > shipsToBuyFromPosition.Length)
                {
                    InformPlayer(chatinfo.playerId, $"select a ship number from 1 to {shipsToBuyFromPosition.Length}");
                    return;
                }
                number--;
            }
            else number = shipsToBuyFromPosition.Length - 1;

            var buyship = shipsToBuyFromPosition[number];
            buyship.Price = price;

            Configuration.Save();
        }

        private async Task ShipRename(ChatInfo chatinfo, Dictionary<string, string> arguments)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var shipsToBuyFromPosition = Configuration.Current.Ships
                .OrderBy(S => S.EntityType)
                .OrderBy(S => S.DisplayName)
                .Where(S => Distance(S.BuyLocation.pos, P.pos) <= Configuration.Current.MaxBuyingPosDistance).ToArray();

            if(shipsToBuyFromPosition.Length == 0)
            {
                InformPlayer(chatinfo.playerId, $"no ships available from this position prease go to a 'ship buy position'");
                log($"Ship buy at wrong position {P.playfield} [X:{P.pos.x} Y:{P.pos.y} Z:{P.pos.z}] start", LogLevel.Message);
                return;
            }

            if (int.TryParse(arguments["number"], out int number))
            {
                if (number <= 0 || number > shipsToBuyFromPosition.Length)
                {
                    InformPlayer(chatinfo.playerId, $"select a ship number from 1 to {shipsToBuyFromPosition.Length}");
                    return;
                }
                number--;
            }
            else number = shipsToBuyFromPosition.Length - 1;

            var buyship = shipsToBuyFromPosition[number];
            buyship.DisplayName = arguments["name"];

            Configuration.Save();
        }

        private async Task ShipGetSaleProfit(ChatInfo chatinfo)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            if (!Configuration.Current.SaleProfits.ContainsKey(P.steamId))
            {
                InformPlayer(chatinfo.playerId, $"no sales profits found for you");
                log($"No sales profit found for {P.playerName}", LogLevel.Message);
                return;
            }

            var profit = Configuration.Current.SaleProfits[P.steamId];
            await Request_Player_SetCredits(new IdCredits(P.entityId, P.credits + profit));

            InformPlayer(chatinfo.playerId, $"congratulations you get {profit} credits");
            log($"Player get profit {P.playerName} => {profit}", LogLevel.Message);
        }

        private async Task DisplayCatalog(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());
            await DisplayHelp(playerId, S => S.BuyLocation.playfield == P.playfield);
        }

        private async Task ShipBuy(ChatInfo chatinfo, Dictionary<string, string> arguments, bool buy)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            var shipsToBuyFromPosition = Configuration.Current.Ships
                .OrderBy(S => S.EntityType)
                .OrderBy(S => S.DisplayName)
                .Where(S => Distance(S.BuyLocation.pos, P.pos) <= Configuration.Current.MaxBuyingPosDistance).ToArray();

            if(shipsToBuyFromPosition.Length == 0)
            {
                InformPlayer(chatinfo.playerId, $"no ships to buy from this position prease go to a 'ship buy position'");
                log($"Ship buy at wrong position {P.playfield} [X:{P.pos.x} Y:{P.pos.y} Z:{P.pos.z}] start", LogLevel.Message);
                return;
            }

            if (int.TryParse(arguments["number"], out int number))
            {
                if(number <= 0 || number > shipsToBuyFromPosition.Length)
                {
                    InformPlayer(chatinfo.playerId, $"select a ship number from 1 to {shipsToBuyFromPosition.Length}");
                    return;
                }
                number--;
            }
            else number = shipsToBuyFromPosition.Length - 1;

            var buyship = shipsToBuyFromPosition[number];

            if (!buy && buyship.SellerId == P.steamId) buyship.Price = (buyship.Price * Configuration.Current.CancelPercentageSaleFee) / 100;

            if (P.credits < buyship.Price)
            {
                InformPlayer(chatinfo.playerId, $"the ship costs {buyship.Price} but you have only {P.credits} sorry.");
                return;
            }

            var answer = await ShowDialog(chatinfo.playerId, P, 
                $"Are you sure you want to buy this Ship", 
                $"\"{buyship.DisplayName}\"\nfor [c][ffffff]{buyship.Price}[-][/c] Credits from {buyship.Seller}\n\n{buyship.ShipDetails}", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            log($"Ship buy {buyship.DisplayName} at {buyship.BuyLocation.playfield} start", LogLevel.Message);

            await CreateStructure(buyship, P);

            log($"Ship buy {P.playerName}: {P.credits} - {buyship.Price}", LogLevel.Message);
            await Request_Player_SetCredits(new IdCredits(P.entityId, P.credits - buyship.Price));

            log($"Ship buy {buyship.DisplayName} at {buyship.BuyLocation.playfield} complete", LogLevel.Message);

            if (buyship.OnetimeTransaction) {
                Configuration.Current.Ships.Remove(buyship);

                if (buy)
                {
                    if (!Configuration.Current.SaleProfits.ContainsKey(buyship.SellerId)) Configuration.Current.SaleProfits.Add(buyship.SellerId, buyship.Price);
                    else Configuration.Current.SaleProfits[buyship.SellerId] = Configuration.Current.SaleProfits[buyship.SellerId] + buyship.Price;
                }

                try { Directory.Delete(Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", buyship.StructureDirectoryOrEPBName), true); } catch { }
            }

            Configuration.Save();

            await ShowDialog(chatinfo.playerId, P, "Congratulations", $"your new ship \"{buyship.DisplayName} for {P.playerName}\" is ready for pick-up at  {buyship.SpawnLocation.playfield}[X:{(int)buyship.SpawnLocation.pos.x} Y:{(int)buyship.SpawnLocation.pos.y} Z:{(int)buyship.SpawnLocation.pos.z}]");
        }

        public async Task CreateStructure(ShipInfo ship, PlayerInfo player)
        {
            var NewID = await Request_NewEntityId();

            var isEPBFile = string.Compare(Path.GetExtension(ship.StructureDirectoryOrEPBName), ".epb", StringComparison.InvariantCultureIgnoreCase) == 0;

            var SpawnInfo = new EntitySpawnInfo()
            {
                forceEntityId   = NewID.id,
                playfield       = ship.SpawnLocation.playfield,
                pos             = ship.SpawnLocation.pos,
                rot             = ship.SpawnLocation.rot,
                name            = $"{ship.DisplayName} for {player.playerName}",
                type            = (byte)ship.EntityType,
                entityTypeName  = "", // 'Kommentare der Devs:  ...or set this to f.e. 'ZiraxMale', 'AlienCivilian1Fat', etc
                factionGroup    = 1,
                factionId       = player.entityId
            };

            if (isEPBFile)
            {
                SpawnInfo.prefabName = Path.GetFileNameWithoutExtension (ship.StructureDirectoryOrEPBName);
                SpawnInfo.prefabDir = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData");
            }
            else
            {
                var SourceDir = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", ship.StructureDirectoryOrEPBName);
                var TargetDir = Path.Combine(EmpyrionConfiguration.SaveGamePath,    "Shared", $"{ship.EntityType}_Player_{NewID.id}");

                Directory.CreateDirectory(Path.GetDirectoryName(TargetDir));
                CopyAll(new DirectoryInfo(SourceDir), new DirectoryInfo(TargetDir));
            }

            try { await Request_Load_Playfield(new PlayfieldLoad(20, ship.SpawnLocation.playfield, 0)); }
            catch { }  // Playfield already loaded

            await Request_Entity_Spawn(SpawnInfo);
            await Request_Structure_Touch(NewID); // Sonst wird die Struktur sofort wieder gelöscht !!!
        }

        private double Distance(PVector3 pos1, PVector3 pos2)
        {
            return Math.Sqrt(Math.Pow(pos1.x - pos2.x, 2) + Math.Pow(pos1.y - pos2.y, 2) + Math.Pow(pos1.z - pos2.z, 2));
        }

        private async Task ShipSell(ChatInfo chatinfo, Dictionary<string, string> arguments, bool onetimeTransaction)
        {
            var P = await Request_Player_Info(chatinfo.playerId.ToId());

            if (Configuration.Current.ForbiddenPlayfields.Contains(P.playfield))
            {
                InformPlayer(chatinfo.playerId, $"no ship selling allowed in this playfield");
                return;
            }

            var G = await Request_GlobalStructure_List();

            var ship = SearchEntity(G, int.Parse(arguments["id"]));
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

            var answer = await ShowDialog(chatinfo.playerId, P, $"Are you sure you want to {(onetimeTransaction ? "sell" : "add")}", $"[c][00ff00]\"{ship.name}\"[-][/c] for [c][ffffff]{price}[-][/c] Credits?", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            log($"Ship sell {ship.id}/{ship.name} at {P.playfield} start", LogLevel.Message);

            var sourceDataDir = Path.Combine(EmpyrionConfiguration.SaveGamePath,    "Shared", $"{(EntityType)ship.type}_Player_{ship.id}");
            var targetDataDir = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipsData", $"{(EntityType)ship.type}_{ship.id}");

            Configuration.Current.Ships.Add(new Configuration.ShipInfo() {
                DisplayName         = ship.name,
                Price               = price,
                EntityType          = (EntityType)ship.type,
                Seller              = P.playerName,
                SellerId            = onetimeTransaction ? P.steamId : string.Empty,
                StructureDirectoryOrEPBName = Path.GetFileName(targetDataDir),
                SpawnLocation       = new PlayfieldPositionRotation() { playfield = P.playfield, pos = ship.pos, rot = ship.rot },
                BuyLocation         = new PlayfieldPosition        () { playfield = P.playfield, pos = P.pos },
                OnetimeTransaction  = onetimeTransaction,
                ShipDetails         = $"{(EntityType)ship.type} Class:{ship.classNr} Blocks:{ship.cntBlocks} Devices:{ship.cntDevices} Lights:{ship.cntLights} Triangles:{ship.cntTriangles}"
            });

            Configuration.Save();

            await Request_Entity_Destroy(ship.id.ToId());

            Thread.Sleep(5000);

            CopyAll(new DirectoryInfo(sourceDataDir), new DirectoryInfo(targetDataDir));

            log($"Ship sell {ship.id}/{ship.name} at {P.playfield} complete", LogLevel.Message);
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

        public static GlobalStructureInfo SearchEntity(GlobalStructureList aGlobalStructureList, int aSourceId)
        {
            foreach (var TestPlayfieldEntites in aGlobalStructureList.globalStructures)
            {
                var FoundEntity = TestPlayfieldEntites.Value.FirstOrDefault(E => E.id == aSourceId);
                if (FoundEntity.id != 0) return FoundEntity;
            }
            return new GlobalStructureInfo();
        }

        private void LoadConfiguration()
        {
            Configuration = new ConfigurationManager<Configuration>() {
                ConfigFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "ShipConfigurations.json")
            };

            Configuration.Load();
            Configuration.Save();
        }

        private async Task DisplayHelp(int aPlayerId, Func<ShipInfo, bool> customSelector)
        {
            await DisplayHelp(aPlayerId,
                    Configuration.Current.Ships
                    .Where(customSelector)
                    .OrderBy(S => S.BuyLocation.playfield)
                    .GroupBy(S => S.BuyLocation.playfield)
                    .Aggregate("", (p, g) => p + $"\n[c][00ffff]Ships at {g.Key}:[-][/c]\n" +
                        Configuration.Current.Ships
                        .Where(S => S.BuyLocation.playfield == g.Key)
                        .OrderBy(S => S.EntityType)
                        .OrderBy(S => S.DisplayName)
                        .Aggregate(new { count = 0, line = "" }, (o, s) => new
                        {
                            count = o.count + 1,
                            line  = o.line + $"[c][00ffff]{o.count + 1}:[-][/c] [c][ffffff][{s.EntityType}][-][/c] [c][00ff00]\"{s.DisplayName}\"[-][/c]{(string.IsNullOrEmpty(s.SellerId) ? "" : $" from [c][00ff00]{s.Seller}[-][/c]")} to buy at [[c][ff00ff]X:{(int)s.BuyLocation.pos.x} Y:{(int)s.BuyLocation.pos.y} Z:{(int)s.BuyLocation.pos.z}[-][/c]] for [c][ffffff]{s.Price}[-][/c] credits\n"
                        })
                        .line)
            );
        }

    }
}
