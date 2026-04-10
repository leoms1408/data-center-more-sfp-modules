using System.Collections.Generic;

namespace MoreSFPModules
{
    internal static class ModuleRegistry
    {
        internal readonly struct Entry
        {
            /// <summary>Internal speed value to set on the cloned SFPModule (= SpeedGbps / 5).</summary>
            internal readonly float SpeedInternal;

            /// <summary>sfpType kept on the module — same as vanilla QSFP+ for port compatibility.</summary>
            internal readonly int ModuleSfpType;

            /// <summary>sfpBoxType set on the box — equals the custom prefabID to distinguish it from vanilla.</summary>
            internal readonly int BoxSfpType;

            /// <summary>prefabID of the vanilla base module used as clone source.</summary>
            internal readonly int BasePrefabID;

            /// <summary>Number of modules inside the box (5 = standard, 32 = large box).</summary>
            internal readonly int ModuleCount;

            internal Entry(float speedInternal, int moduleSfpType, int boxSfpType, int basePrefabID,
                           int moduleCount = 5)
            {
                SpeedInternal = speedInternal;
                ModuleSfpType = moduleSfpType;
                BoxSfpType    = boxSfpType;
                BasePrefabID  = basePrefabID;
                ModuleCount   = moduleCount;
            }
        }

        private static readonly Dictionary<int, Entry> _entries = new();

        internal static IReadOnlyDictionary<int, Entry> Entries => _entries;

        internal static void Register(int prefabID, Entry entry) => _entries[prefabID] = entry;

        internal static bool TryGet(int prefabID, out Entry entry) =>
            _entries.TryGetValue(prefabID, out entry);

        internal static void Clear() => _entries.Clear();
    }
}
