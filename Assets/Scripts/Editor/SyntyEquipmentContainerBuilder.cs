using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MultiplayerARPG;
using UnityEditor;
using UnityEngine;

namespace MMORPGGranny.EditorTools
{
    /// <summary>
    /// Builds <see cref="EquipmentContainer"/> entries on a <see cref="BaseCharacterModel"/> from a
    /// Synty modular character hierarchy.
    ///
    /// The wardrobe meshes of a Synty modular character are already children of the rig and already
    /// skinned to it, so equipment items should show/hide them (EquipmentModel.useInstantiatedObject
    /// = true, picking a variant with instantiatedObjectIndex) rather than instantiating a prefab and
    /// rebinding bones. This window wires the containers those items resolve against.
    ///
    /// Variant index = the trailing number in the mesh name (Chr_HandRight_Male_07 -> 7), so an
    /// item's `Instantiated Object Index` reads the same as Synty's own numbering. Slots whose
    /// children are not numbered (Male_00_Head, All_12_Extra) fall back to sibling order.
    /// </summary>
    public class SyntyEquipmentContainerBuilder : EditorWindow
    {
        public enum Gender { Male, Female }

        // --- Slot map ---------------------------------------------------------------------------
        // Paths are relative to the "Modular_Characters" transform. "{G}" becomes Male or Female.
        // Edit these if your hierarchy differs from stock Synty.

        private class SlotDef
        {
            public readonly string Socket;
            public readonly string[] PartPaths;

            /// <summary>
            /// Only consulted for slots whose meshes carry no trailing number. Numbered slots derive
            /// this from the numbering itself: body parts run from _00 (bare skin, the default),
            /// attachments run from _01 and have no "nothing equipped" mesh at all.
            /// </summary>
            public bool DefaultToFirst;

            public SlotDef(string socket, params string[] partPaths)
            {
                Socket = socket;
                PartPaths = partPaths;
            }
        }

        private static readonly SlotDef[] GenderSlots =
        {
            new SlotDef("Body",
                "{G}_Parts/{G}_03_Torso",
                "{G}_Parts/{G}_04_Arm_Upper_Right",
                "{G}_Parts/{G}_05_Arm_Upper_Left",
                "{G}_Parts/{G}_06_Arm_Lower_Right",
                "{G}_Parts/{G}_07_Arm_Lower_Left"),
            new SlotDef("Gloves",
                "{G}_Parts/{G}_08_Hand_Right",
                "{G}_Parts/{G}_09_Hand_Left"),
            // Socket names follow the kit's armor-type convention, which is body parts rather than
            // garments: Body, Head, Gloves, Shoes - so Legs, not Pants.
            new SlotDef("Legs",
                "{G}_Parts/{G}_10_Hips",
                "{G}_Parts/{G}_11_Leg_Right",
                "{G}_Parts/{G}_12_Leg_Left"),
            // Head children are named ..._All_Elements / ..._No_Elements, so they index by order and
            // need to be told that the first one is what shows with nothing equipped.
            new SlotDef("Head", "{G}_Parts/{G}_00_Head") { DefaultToFirst = true },
            new SlotDef("Eyebrows", "{G}_Parts/{G}_01_Eyebrows"),
            new SlotDef("FacialHair", "{G}_Parts/{G}_02_FacialHair"),
        };

        private static readonly SlotDef[] SharedSlots =
        {
            // The three HeadCoverings folders are alternative cuts of the same hat (fitted for hair,
            // for no hair, for no facial hair) - not parts meant to show together, so one socket each.
            new SlotDef("HeadCovering_BaseHair", "All_Gender_Parts/All_00_HeadCoverings/HeadCoverings_Base_Hair"),
            new SlotDef("HeadCovering_NoFacialHair", "All_Gender_Parts/All_00_HeadCoverings/HeadCoverings_No_FacialHair"),
            new SlotDef("HeadCovering_NoHair", "All_Gender_Parts/All_00_HeadCoverings/HeadCoverings_No_Hair"),
            new SlotDef("Hair", "All_Gender_Parts/All_01_Hair"),
            new SlotDef("Helmet", "All_Gender_Parts/All_02_Head_Attachment/Helmet"),
            new SlotDef("ChestAttachment", "All_Gender_Parts/All_03_Chest_Attachment"),
            new SlotDef("Cloak", "All_Gender_Parts/All_04_Back_Attachment"),
            new SlotDef("Shoulders",
                "All_Gender_Parts/All_05_Shoulder_Attachment_Right",
                "All_Gender_Parts/All_06_Shoulder_Attachment_Left"),
            new SlotDef("Elbows",
                "All_Gender_Parts/All_07_Elbow_Attachment_Right",
                "All_Gender_Parts/All_08_Elbow_Attachment_Left"),
            new SlotDef("HipsAttachment", "All_Gender_Parts/All_09_Hips_Attachment"),
            new SlotDef("Knees",
                // Synty misspells this one as "Attachement" in the stock hierarchy.
                "All_Gender_Parts/All_10_Knee_Attachement_Right",
                "All_Gender_Parts/All_11_Knee_Attachement_Left"),
            // Elf_Ear is unnumbered and is an opt-in extra, so it gets no default - leaving
            // DefaultToFirst off is what keeps it hidden until something equips it.
            new SlotDef("Extra", "All_Gender_Parts/All_12_Extra"),
        };

        private static readonly Regex VariantSuffix = new Regex(@"_(\d+)$", RegexOptions.Compiled);

        // --- Window state -----------------------------------------------------------------------

        private GameObject _characterRoot;
        private Gender _gender = Gender.Male;
        private string _socketSuffix = string.Empty;
        private string _modularRootName = "Modular_Characters";
        private bool _includeSharedSlots = true;
        private bool _forceGroups;
        private bool _resetToDefaultState = true;
        private string _report = string.Empty;
        private Vector2 _reportScroll;

        [MenuItem("Tools/MMORPG KIT/Synty Equipment Container Builder")]
        private static void Open()
        {
            GetWindow<SyntyEquipmentContainerBuilder>(false, "Synty Equipment", true).minSize = new Vector2(460f, 460f);
        }

        /// <summary>
        /// Runs the same pass as the window's Build button without opening it, for scripted or batch
        /// use - rebuilding a character after its rig has been swapped, for instance.
        /// Returns the report the window would have shown.
        /// </summary>
        public static string Build(GameObject characterRoot, Gender gender, bool includeSharedSlots = true, bool resetToDefaultState = true, string socketSuffix = "")
        {
            SyntyEquipmentContainerBuilder builder = CreateInstance<SyntyEquipmentContainerBuilder>();
            try
            {
                builder._characterRoot = characterRoot;
                builder._gender = gender;
                builder._includeSharedSlots = includeSharedSlots;
                builder._resetToDefaultState = resetToDefaultState;
                builder._socketSuffix = socketSuffix;
                builder.Run(true);
                return builder._report;
            }
            finally
            {
                DestroyImmediate(builder);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Fills the character model's Equipment Containers from a Synty modular hierarchy.\n" +
                "Items then equip with Use Instantiated Object = true and Instantiated Object Index = " +
                "the number in the Synty mesh name.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _characterRoot = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Character Root", "Prefab asset, prefab-stage root, or scene instance carrying the BaseCharacterModel."),
                _characterRoot, typeof(GameObject), true);
            _gender = (Gender)EditorGUILayout.EnumPopup(
                new GUIContent("Gender Parts", "Which of Male_Parts / Female_Parts to wire."), _gender);
            _socketSuffix = EditorGUILayout.TextField(
                new GUIContent("Socket Suffix", "Appended to every socket, e.g. (M). Use this when one item asset must serve separate male and female character models."),
                _socketSuffix);
            _modularRootName = EditorGUILayout.TextField(
                new GUIContent("Modular Root", "Child transform holding the wardrobe."), _modularRootName);
            _includeSharedSlots = EditorGUILayout.Toggle(
                new GUIContent("Include Shared Slots", "Wire the gender-neutral All_Gender_Parts slots too."), _includeSharedSlots);
            _forceGroups = EditorGUILayout.Toggle(
                new GUIContent("Always Use Groups", "Use Instantiated Object Groups even for single-part slots, so adding a second part later needs no rewiring."), _forceGroups);
            _resetToDefaultState = EditorGUILayout.Toggle(
                new GUIContent("Reset To Default State", "After wiring, hide every variant and show the default (bare) parts."), _resetToDefaultState);
            if (EditorGUI.EndChangeCheck())
                _report = string.Empty;

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_characterRoot == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Preview", GUILayout.Height(26f)))
                        Run(false);
                    if (GUILayout.Button("Build Containers", GUILayout.Height(26f)))
                        Run(true);
                }
            }

            if (string.IsNullOrEmpty(_report))
                return;

            EditorGUILayout.Space();
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll);
            EditorGUILayout.TextArea(_report, EditorStyles.label, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // --- Collection -------------------------------------------------------------------------

        private class BuiltSlot
        {
            public string Socket;
            public Transform Anchor;
            public int PartCount;
            public bool Ordinal;
            public bool HasDefault;
            public List<GameObject>[] Variants;
            public readonly List<string> Notes = new List<string>();
        }

        private BuiltSlot Collect(Transform modularRoot, SlotDef def, string genderToken)
        {
            BuiltSlot slot = new BuiltSlot { Socket = def.Socket + _socketSuffix };

            List<Transform> parents = new List<Transform>();
            foreach (string rawPath in def.PartPaths)
            {
                string path = rawPath.Replace("{G}", genderToken);
                Transform part = modularRoot.Find(path);
                if (part == null)
                {
                    slot.Notes.Add("no such transform: " + path);
                    continue;
                }
                if (part.childCount == 0)
                {
                    slot.Notes.Add("empty: " + path);
                    continue;
                }
                parents.Add(part);
            }

            if (parents.Count == 0)
                return null;

            slot.Anchor = parents[0];
            slot.PartCount = parents.Count;

            // Prefer Synty's own numbering so item indices read like the mesh names. If any child in
            // the slot is unnumbered, the whole slot falls back to sibling order to stay consistent.
            slot.Ordinal = parents.Any(p => Enumerable.Range(0, p.childCount)
                .Any(i => !VariantSuffix.IsMatch(p.GetChild(i).name)));
            if (slot.Ordinal)
                slot.Notes.Add("unnumbered meshes - indexed by sibling order");

            Dictionary<int, List<GameObject>> byIndex = new Dictionary<int, List<GameObject>>();
            foreach (Transform part in parents)
            {
                for (int i = 0; i < part.childCount; ++i)
                {
                    Transform child = part.GetChild(i);
                    int index;
                    if (slot.Ordinal)
                    {
                        index = i;
                    }
                    else
                    {
                        Match match = VariantSuffix.Match(child.name);
                        index = int.Parse(match.Groups[1].Value);
                    }
                    if (!byIndex.TryGetValue(index, out List<GameObject> list))
                        byIndex[index] = list = new List<GameObject>();
                    list.Add(child.gameObject);
                }
            }

            // Dense array so array index == variant number; holes stay empty on purpose.
            int max = byIndex.Keys.Max();
            slot.Variants = new List<GameObject>[max + 1];
            for (int i = 0; i <= max; ++i)
                slot.Variants[i] = byIndex.TryGetValue(i, out List<GameObject> list) ? list : new List<GameObject>();

            // A numbered slot has a "nothing equipped" mesh only if its numbering starts at _00.
            slot.HasDefault = slot.Ordinal ? def.DefaultToFirst : slot.Variants[0].Count > 0;

            return slot;
        }

        private List<BuiltSlot> CollectAll(GameObject root, out string error)
        {
            error = null;

            Transform modularRoot = root.transform.Find(_modularRootName);
            if (modularRoot == null)
            {
                error = $"'{root.name}' has no child transform named '{_modularRootName}'.";
                return null;
            }

            string genderToken = _gender.ToString();
            List<SlotDef> defs = new List<SlotDef>(GenderSlots);
            if (_includeSharedSlots)
                defs.AddRange(SharedSlots);

            List<BuiltSlot> slots = new List<BuiltSlot>();
            foreach (SlotDef def in defs)
            {
                BuiltSlot slot = Collect(modularRoot, def, genderToken);
                if (slot != null)
                    slots.Add(slot);
            }

            if (slots.Count == 0)
                error = $"Found '{_modularRootName}' but none of the expected Synty part transforms under it.";

            return slots;
        }

        // --- Apply ------------------------------------------------------------------------------

        private void Run(bool apply)
        {
            GameObject source = _characterRoot;
            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(source);
            string assetPath = isAsset ? AssetDatabase.GetAssetPath(source) : null;

            // A prefab asset selected in the Project window cannot be edited in place; open an
            // isolated copy, edit that, and save it back.
            GameObject working = apply && isAsset ? PrefabUtility.LoadPrefabContents(assetPath) : source;

            try
            {
                BaseCharacterModel model = working.GetComponent<BaseCharacterModel>();
                if (model == null)
                {
                    _report = $"'{source.name}' has no BaseCharacterModel component (expected e.g. PlayableCharacterModel).";
                    return;
                }

                List<BuiltSlot> slots = CollectAll(working, out string error);
                if (slots == null || slots.Count == 0)
                {
                    _report = error;
                    return;
                }

                _report = BuildReport(slots, apply, error);
                if (!apply)
                    return;

                if (!isAsset)
                    Undo.RegisterFullObjectHierarchyUndo(working, "Build Synty Equipment Containers");

                ApplyContainers(model, slots);

                if (_resetToDefaultState)
                    model.DeactivateInstantiatedObjects();

                if (isAsset)
                {
                    PrefabUtility.SaveAsPrefabAsset(working, assetPath, out bool saved);
                    _report += saved
                        ? "\n\nSaved to " + assetPath
                        : "\n\nFAILED to save " + assetPath;
                }
                else
                {
                    EditorUtility.SetDirty(model);
                    if (PrefabUtility.IsPartOfPrefabInstance(model))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
                    _report += "\n\nApplied to " + working.name + " (scene / prefab stage - save it yourself).";
                }
            }
            finally
            {
                if (apply && isAsset && working != null)
                    PrefabUtility.UnloadPrefabContents(working);
            }
        }

        private void ApplyContainers(BaseCharacterModel model, List<BuiltSlot> slots)
        {
            // Keep any container whose socket this run does not own, so hand-made sockets survive.
            List<EquipmentContainer> containers = model.EquipmentContainers != null
                ? new List<EquipmentContainer>(model.EquipmentContainers)
                : new List<EquipmentContainer>();

            foreach (BuiltSlot slot in slots)
            {
                EquipmentContainer container = new EquipmentContainer
                {
                    equipSocket = slot.Socket,
                    transform = slot.Anchor,
                };

                bool useGroups = _forceGroups || slot.PartCount > 1;
                bool hasDefault = slot.HasDefault;

                if (useGroups)
                {
                    EquipmentInstantiatedObjectGroup[] groups = new EquipmentInstantiatedObjectGroup[slot.Variants.Length];
                    for (int i = 0; i < slot.Variants.Length; ++i)
                    {
                        groups[i] = new EquipmentInstantiatedObjectGroup
                        {
                            instantiatedObjects = slot.Variants[i].ToArray(),
                        };
                    }
                    container.instantiatedObjectGroups = groups;
                    // Attachment slots start at _01 and have no "nothing equipped" mesh, so they get
                    // no default and simply show nothing when unequipped.
                    container.defaultInstantiatedObjectGroup = hasDefault ? groups[0] : null;
                }
                else
                {
                    GameObject[] objects = new GameObject[slot.Variants.Length];
                    for (int i = 0; i < slot.Variants.Length; ++i)
                        objects[i] = slot.Variants[i].Count > 0 ? slot.Variants[i][0] : null;
                    container.instantiatedObjects = objects;
                    container.defaultModel = hasDefault ? objects[0] : null;
                }

                int existing = containers.FindIndex(c => c != null && c.equipSocket == slot.Socket);
                if (existing >= 0)
                    containers[existing] = container;
                else
                    containers.Add(container);
            }

            model.EquipmentContainers = containers.ToArray();
        }

        // --- Report -----------------------------------------------------------------------------

        private string BuildReport(List<BuiltSlot> slots, bool applied, string warning)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(applied ? "BUILT" : "PREVIEW");
            sb.AppendLine($"{slots.Count} sockets, gender parts: {_gender}");
            if (!string.IsNullOrEmpty(warning))
                sb.AppendLine("! " + warning);
            sb.AppendLine();

            foreach (BuiltSlot slot in slots)
            {
                bool useGroups = _forceGroups || slot.PartCount > 1;
                int last = slot.Variants.Length - 1;

                List<int> partial = new List<int>();
                List<int> empty = new List<int>();
                for (int i = 0; i <= last; ++i)
                {
                    int count = slot.Variants[i].Count;
                    if (count == 0)
                        empty.Add(i);
                    else if (count < slot.PartCount)
                        partial.Add(i);
                }

                sb.AppendLine(slot.Socket);
                sb.AppendLine($"    anchor      {slot.Anchor.name}");
                sb.AppendLine($"    mode        {(useGroups ? "object groups" : "single objects")}, {slot.PartCount} part folder(s)");
                sb.AppendLine($"    indices     0..{last}   default: {(slot.HasDefault ? "index 0" : "none (nothing shown when unequipped)")}");
                if (partial.Count > 0)
                    sb.AppendLine($"    incomplete  {Summarize(partial)}  (fewer than {slot.PartCount} meshes - will leave part of the body bare)");
                if (empty.Count > 0)
                    sb.AppendLine($"    unused      {Summarize(empty)}");
                foreach (string note in slot.Notes)
                    sb.AppendLine("    note        " + note);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Summarize(List<int> values)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < values.Count;)
            {
                int start = i;
                while (i + 1 < values.Count && values[i + 1] == values[i] + 1)
                    ++i;
                parts.Add(start == i ? values[start].ToString() : $"{values[start]}-{values[i]}");
                ++i;
            }
            return string.Join(", ", parts);
        }
    }
}
