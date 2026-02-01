using System;
using System.Collections.Generic;

namespace RiotManifestCore.Models;

public static class RmanEndpoints
{
    public static readonly List<RmanProductConfig> Products = new List<RmanProductConfig>
    {
        // League of Legends - Usaremos FileVersionInfo (como hachoir)
        new RmanProductConfig
        {
            ParentName = "League of Legends",
            ProductName = "League of Legends (PBE)",
            Abbreviation = "lol",
            SieveId = "lol",
            Region = "PBE1",
            ConfigPath = "keystone.products.league_of_legends.patchlines.pbe",
            ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.league_of_legends.patchlines",
            ClientName = "League Client",
            VersionFileFilter = "LeagueClient.exe",
            VersionPattern = null // Sin patrón -> Usar FileVersionInfo
        },
        
        // Valorant - Usaremos el bucle binario del script de Python
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (Live)", Abbreviation = "valorant", SieveId = "valorant", Region = "live", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (NA)", Abbreviation = "valorant", SieveId = "valorant", Region = "na", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (EU)", Abbreviation = "valorant", SieveId = "valorant", Region = "eu", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (LATAM)", Abbreviation = "valorant", SieveId = "valorant", Region = "latam", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (BR)", Abbreviation = "valorant", SieveId = "valorant", Region = "br", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (AP)", Abbreviation = "valorant", SieveId = "valorant", Region = "ap", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (KR)", Abbreviation = "valorant", SieveId = "valorant", Region = "kr", ConfigPath = "keystone.products.valorant.patchlines.live", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" },
        new RmanProductConfig { ParentName = "Valorant", ProductName = "Valorant (PBE)", Abbreviation = "valorant", SieveId = "valorant", Region = "pbe", ConfigPath = "keystone.products.valorant.patchlines.pbe", ConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public?namespace=keystone.products.valorant.patchlines", ClientName = "Valorant Client", VersionFileFilter = "VALORANT-Win64-Shipping.exe", VersionPattern = "++Ares-Core+release-" }
    };

    public static string GetBundleUrl(string manifestUrl)
    {
        if (manifestUrl.Contains("valorant", StringComparison.OrdinalIgnoreCase))
            return "https://valorant.dyn.riotcdn.net/channels/public/bundles";
        
        return "https://lol.dyn.riotcdn.net/channels/public/bundles";
    }
}

public class RmanProductConfig
{
    public string ParentName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string SieveId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ConfigUrl { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string VersionFileFilter { get; set; } = string.Empty;
    public string VersionPattern { get; set; } = string.Empty;
}
