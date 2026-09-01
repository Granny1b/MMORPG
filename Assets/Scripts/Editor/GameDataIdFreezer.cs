using System.Collections.Generic;
using System.Linq;
using MultiplayerARPG;
using UnityEditor;
using UnityEngine;

namespace MMORPGGranny.EditorTools
{
    /// <summary>
    /// Writes each game data asset's current implicit ID into its serialized <c>id</c> field, so
    /// the ID stops tracking the file name.
    ///
    /// <see cref="BaseGameData.Id"/> returns <c>string.IsNullOrEmpty(id) ? name : id</c>, and
    /// <c>DataId</c> is <c>Id.GenerateHashId()</c> - a plain deterministic string hash. The MMO
    /// database stores that DataId in every row that refers to game data (<c>characteritem</c>,
    /// <c>characterskill</c>, <c>charactersummon</c>, and so on). So while <c>id</c> is blank,
    /// renaming an asset silently changes its DataId and orphans every saved row that referenced
    /// it: a non-issue in single player, where the fix is to wipe the save, and unrecoverable once
    /// other people own the item.
    ///
    /// Filling <c>id</c> with the asset's *current* name is a no-op for the hash - the same string
    /// reaches <c>GenerateHashId</c> either way, so existing saves keep resolving exactly as they
    /// did. What changes is that the ID is pinned, and renaming the file afterwards becomes free.
    /// This is why it is worth running early and worth running on the kit's own Demo assets too:
    /// they are referenced by <c>GameDatabase_G</c> and are just as fragile.
    ///
    /// The kit has no equivalent. <c>MMORPG KIT/Asset Tools/Validate Game Data And Prefabs</c>
    /// looks like it should do this, but <see cref="BaseGameData.Validate"/> only reconciles
    /// Addressable asset-reference hashes, and the <c>LiteNetLibIdentity</c> asset IDs it assigns to
    /// prefabs come from the prefab's asset GUID, which already survives renaming. Neither touches
    /// the <c>id</c> string.
    ///
    /// Not covered: <c>BaseNpcDialog.Id</c> is hardcoded to <c>name</c> with no <c>id</c> field to
    /// write to, so NPC dialog nodes stay rename-fragile whatever this tool does.
    /// </summary>
    public static class GameDataIdFreezer
    {
        private const string IdFieldName = "id";
        private const string MenuRoot = "Tools/Game Data IDs/";

        private readonly struct Entry
        {
            public readonly BaseGameData Data;
            public readonly string Path;
            public readonly string ExplicitId;

            public Entry(BaseGameData data, string path, string explicitId)
            {
                Data = data;
                Path = path;
                ExplicitId = explicitId;
            }

            /// <summary>The string that actually feeds <c>GenerateHashId</c> today.</summary>
            public string EffectiveId => string.IsNullOrEmpty(ExplicitId) ? Data.name : ExplicitId;

            public bool IsFrozen => !string.IsNullOrEmpty(ExplicitId);
        }

        [MenuItem(MenuRoot + "Report Unfrozen IDs", false, 0)]
        public static void Report()
        {
            List<Entry> entries = Collect();
            Dictionary<string, List<Entry>> collisions = FindCollisions(entries);
            int unfrozen = entries.Count(x => !x.IsFrozen);

            foreach (Entry entry in entries.Where(x => !x.IsFrozen).OrderBy(x => x.Path))
                Debug.Log($"[GameDataIdFreezer] Unfrozen: would set id = \"{entry.Data.name}\" on {entry.Path}", entry.Data);

            LogCollisions(collisions);

            Debug.Log($"[GameDataIdFreezer] {entries.Count} game data assets: " +
                      $"{entries.Count - unfrozen} already pinned, {unfrozen} still tracking their file name, " +
                      $"{collisions.Count} colliding ID(s).");
        }

        [MenuItem(MenuRoot + "Freeze Empty IDs From Asset Names", false, 1)]
        public static void Freeze()
        {
            List<Entry> entries = Collect();
            Dictionary<string, List<Entry>> collisions = FindCollisions(entries);

            // Assets sharing an effective ID already hash to the same DataId, so one of them is
            // being shadowed right now. Pinning them would make that permanent and take away the
            // one cheap fix - renaming a file - so they are left alone for a human to resolve.
            HashSet<BaseGameData> colliding = new HashSet<BaseGameData>(collisions.SelectMany(x => x.Value).Select(x => x.Data));
            List<Entry> toFreeze = entries.Where(x => !x.IsFrozen && !colliding.Contains(x.Data)).ToList();

            LogCollisions(collisions);

            if (toFreeze.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Freeze Game Data IDs",
                    collisions.Count > 0
                        ? $"Nothing to freeze.\n\n{collisions.Count} colliding ID(s) were skipped - see the Console."
                        : "Nothing to freeze: every game data asset already has an explicit ID.",
                    "Ok");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Freeze Game Data IDs",
                    $"Write the current asset name into the id field of {toFreeze.Count} game data asset(s)" +
                    (collisions.Count > 0 ? $", skipping {colliding.Count} involved in ID collisions" : string.Empty) +
                    ".\n\nDataIds do not change, so saved characters are unaffected. After this, renaming " +
                    "these assets is safe.\n\nCommit first - this rewrites a lot of .asset files.",
                    "Freeze", "Cancel"))
            {
                return;
            }

            int changed = 0;
            try
            {
                for (int i = 0; i < toFreeze.Count; ++i)
                {
                    Entry entry = toFreeze[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Freezing game data IDs", entry.Path, (float)i / toFreeze.Count))
                        break;

                    SerializedObject serialized = new SerializedObject(entry.Data);
                    SerializedProperty idProperty = serialized.FindProperty(IdFieldName);
                    if (idProperty == null)
                    {
                        Debug.LogWarning($"[GameDataIdFreezer] No serialized \"{IdFieldName}\" field on {entry.Path}, skipped.", entry.Data);
                        continue;
                    }

                    idProperty.stringValue = entry.Data.name;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(entry.Data);
                    ++changed;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[GameDataIdFreezer] Pinned {changed} game data ID(s) to their current asset names.");
            EditorUtility.DisplayDialog(
                "Freeze Game Data IDs",
                $"Pinned {changed} ID(s).\n\nDiff the result before committing - every changed file should show " +
                "only an id: line filled in with its own file name.",
                "Ok");
        }

        private static List<Entry> Collect()
        {
            List<Entry> entries = new List<Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:BaseGameData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseGameData data = AssetDatabase.LoadAssetAtPath<BaseGameData>(path);
                if (data == null)
                    continue;

                SerializedProperty idProperty = new SerializedObject(data).FindProperty(IdFieldName);
                entries.Add(new Entry(data, path, idProperty != null ? idProperty.stringValue : null));
            }
            return entries;
        }

        private static Dictionary<string, List<Entry>> FindCollisions(List<Entry> entries)
        {
            return entries
                .GroupBy(x => x.EffectiveId)
                .Where(x => x.Count() > 1)
                .ToDictionary(x => x.Key, x => x.ToList());
        }

        private static void LogCollisions(Dictionary<string, List<Entry>> collisions)
        {
            foreach (KeyValuePair<string, List<Entry>> collision in collisions)
            {
                // Deliberately an error: two assets resolving to one DataId is a live bug, whether
                // or not this tool ever runs. Addressable variants are the intended exception -
                // the kit ships Map001_AA sharing "Map001" on purpose - so read before renaming.
                Debug.LogError(
                    $"[GameDataIdFreezer] ID \"{collision.Key}\" is used by {collision.Value.Count} assets, " +
                    $"which means they share one DataId:\n  " +
                    string.Join("\n  ", collision.Value.Select(x => x.Path)),
                    collision.Value[0].Data);
            }
        }
    }
}
