using Eleon.Modding;
using EmpyrionNetAPIDefinitions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace EmpyrionShipBuying
{
    public class PlayfieldPosition
    {
        public string playfield { get; set; }
        public PVector3 pos { get; set; }
    }
    public class PlayfieldPositionRotation : PlayfieldPosition
    {
        public PVector3 rot { get; set; }
    }
    public class Configuration
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public LogLevel LogLevel { get; set; } = LogLevel.Message;
        public string ChatCommandPrefix { get; set; } = "\\";
        [JsonConverter(typeof(StringEnumConverter))]
        public PermissionType SellPermission { get; set; } = PermissionType.Player;
        [JsonConverter(typeof(StringEnumConverter))]
        public PermissionType AddPermission { get; set; } = PermissionType.Moderator;
        public int MaxBuyingPosDistance { get; set; } = 4;
        public int CancelPercentageSaleFee { get; set; } = 10;
        public List<string> ForbiddenPlayfields { get; set; } = new List<string>();
        public class ShipInfo
        {
            public string DisplayName { get; set; }
            [JsonConverter(typeof(StringEnumConverter))]
            public EntityType EntityType { get; set; }
            public string StructureDirectoryOrEPBName { get; set; }
            public string ShipDetails { get; set; }
            public double Price { get; set; }
            public string Seller { get; set; }
            public int SellerId { get; set; }
            public bool OnetimeTransaction { get; set; }
            public PlayfieldPositionRotation SpawnLocation { get; set; }
            public PlayfieldPosition BuyLocation { get; set; }
        }

        public List<ShipInfo> Ships { get; set; } = new List<ShipInfo>();
        public Dictionary<int, double> SaleProfits { get; set; } = new Dictionary<int, double>();
    }
}
