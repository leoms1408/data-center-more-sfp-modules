namespace MoreSFPModules
{
    
    internal sealed class ModuleDefinition
    {
        /// <summary>Display name shown in the shop.</summary>
        public string DisplayName;

        /// <summary>
        /// Real-world speed in Gbps (e.g. 80 for 80 Gbps).
        /// Converted to the game's internal unit automatically (÷ 5).
        /// Game examples: 10 Gbps → internal 2, 25 Gbps → 5, 40 Gbps → 8.
        /// </summary>
        public float SpeedGbps;

        /// <summary>
        /// Shop price = vanilla QSFP+ box price × PriceMultiplier (rounded to int).
        /// </summary>
        public float PriceMultiplier;

        /// <summary>XP required to unlock in the shop (0 = always unlocked).</summary>
        public int XpToUnlock;

        /// <summary>
        /// Unique string used to persist the shop unlock state across saves.
        /// Never reuse or change this after the module has appeared in a save game.
        /// </summary>
        public string ShopGuid;

        // Internal helper — used by Core.cs
        internal float InternalSpeed => SpeedGbps / 5f;
    }

    internal static class ModuleList
    {
        internal static readonly ModuleDefinition[] All =
        {
            new ModuleDefinition
            {
                DisplayName     = "QSFP28 100Gbps",
                SpeedGbps       = 100f,
                PriceMultiplier = 2.5f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfp28_100g_v1",
            },
            
            new ModuleDefinition
            {
                DisplayName     = "QSFP56 200Gbps",
                SpeedGbps       = 200f,
                PriceMultiplier = 4.5f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfp56_200g_v1",
            },
            
            //Below this is the part i added, its the only part i am responsible for - Default
            new ModuleDefinition
            {
                DisplayName     = "QSFP-DD 400Gbps",
                SpeedGbps       = 400f,
                PriceMultiplier = 6.5f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfpdd_400g_v1",
            },
            new ModuleDefinition
            {
                DisplayName     = "QSFP-DD 800Gbps",
                SpeedGbps       = 800f,
                PriceMultiplier = 9f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfpdd_800g_v1",
            },

            //Added by Big Texas Jerky to be used with Quantum Servers
            new ModuleDefinition
            {
                DisplayName     = "QSFP-DWDM 1600Gbps",
                SpeedGbps       = 1600f,
                PriceMultiplier = 16f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfp_dwdm_1600g_v1",
            },

            new ModuleDefinition
            {
                DisplayName     = "QSFP-DWDM 3200Gbps",
                SpeedGbps       = 3200f,
                PriceMultiplier = 28f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfp_dwdm_3200g_v1",
            },

            new ModuleDefinition
            {
                DisplayName     = "QSFP-DWDM 6400Gbps",
                SpeedGbps       = 6400f,
                PriceMultiplier = 48f,
                XpToUnlock      = 0,
                ShopGuid        = "more_sfp_qsfp_dwdm_6400g_v1",
            },
        };
    }
}
