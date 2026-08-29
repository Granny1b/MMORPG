using System.Collections.Generic;
using System.IO;
using System.Text;
using MultiplayerARPG;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;

namespace MMORPGGranny.EditorTools
{
    /// <summary>
    /// Fills the locomotion clips of a <see cref="PlayableCharacterModel"/> from the Synty
    /// "Animation Base Locomotion" pack.
    ///
    /// The kit picks one clip per movement direction (<c>MoveStates.forwardState</c>,
    /// <c>backwardLeftState</c>, ...), and <c>TopDownAimController</c> reports that direction
    /// relative to where the character is facing rather than always saying "forward" - so the
    /// eight directions need eight different clips. That is what the pack's strafe sets are for.
    ///
    /// Synty ships two strafe families per move type: <c>FwdStrafe</c> leads with the front foot,
    /// <c>BckStrafe</c> with the back one. Their own sample controller cross-fades between the two
    /// around the character's facing so the feet never cross; the kit's selection is discrete, so
    /// here the forward hemisphere takes the Fwd set and the rear hemisphere the Bck set.
    ///
    /// Only <c>clip</c> is written. Speeds, transition durations and IK flags are left alone, so
    /// this can be re-run over a hand-tuned model without losing that tuning.
    /// </summary>
    public class SyntyLocomotionAnimationBuilder : EditorWindow
    {
        public enum Gender { Masculine, Feminine }

        private const string DefaultAnimationRoot = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon";

        // --- Mapping ----------------------------------------------------------------------------

        private class DirectionDef
        {
            public readonly string Field;
            public readonly string Suffix;
            public readonly System.Func<MoveStates, AnimState> Pick;

            public DirectionDef(string field, string suffix, System.Func<MoveStates, AnimState> pick)
            {
                Field = field;
                Suffix = suffix;
                Pick = pick;
            }
        }

        /// <summary>
        /// Suffixes are relative to a move-type prefix, so "FwdStrafeFL" resolves to
        /// "A_Run_FwdStrafeFL_Masc", "A_Walk_FwdStrafeFL_Femn", and so on.
        /// </summary>
        private static readonly DirectionDef[] Directions =
        {
            new DirectionDef("forwardState", "FwdStrafeF", m => m.forwardState),
            new DirectionDef("forwardLeftState", "FwdStrafeFL", m => m.forwardLeftState),
            new DirectionDef("forwardRightState", "FwdStrafeFR", m => m.forwardRightState),
            new DirectionDef("leftState", "FwdStrafeL", m => m.leftState),
            new DirectionDef("rightState", "FwdStrafeR", m => m.rightState),
            new DirectionDef("backwardState", "BckStrafeB", m => m.backwardState),
            new DirectionDef("backwardLeftState", "BckStrafeBL", m => m.backwardLeftState),
            new DirectionDef("backwardRightState", "BckStrafeBR", m => m.backwardRightState),
        };

        private class MoveTypeDef
        {
            public readonly string Label;
            public readonly string Prefix;
            public readonly System.Func<DefaultAnimations, MoveStates> Pick;

            public MoveTypeDef(string label, string prefix, System.Func<DefaultAnimations, MoveStates> pick)
            {
                Label = label;
                Prefix = prefix;
                Pick = pick;
            }
        }

        private class SingleDef
        {
            public readonly string Label;
            public readonly string ClipName;
            public readonly System.Func<DefaultAnimations, AnimState> Pick;

            public SingleDef(string label, string clipName, System.Func<DefaultAnimations, AnimState> pick)
            {
                Label = label;
                ClipName = clipName;
                Pick = pick;
            }
        }

        // --- Window state -----------------------------------------------------------------------

        private GameObject _characterRoot;
        private Gender _gender = Gender.Masculine;
        private string _animationRoot = DefaultAnimationRoot;
        private bool _fillIdle = true;
        private bool _fillRun = true;
        private bool _fillWalk = true;
        private bool _fillCrouch = true;
        private bool _fillSprint = true;
        private bool _fillAirborne = true;
        private bool _overwriteExisting = true;
        private bool _measurePhases = true;
        private string _report = string.Empty;
        private Vector2 _reportScroll;

        private Dictionary<string, string> _clipIndex;

        [MenuItem("Tools/MMORPG KIT/Synty Locomotion Animation Builder")]
        private static void Open()
        {
            GetWindow<SyntyLocomotionAnimationBuilder>(false, "Synty Locomotion", true).minSize = new Vector2(460f, 460f);
        }

        /// <summary>
        /// Runs the same pass as the window's Assign button without opening it, for scripted or
        /// batch use. Returns the report the window would have shown.
        /// </summary>
        public static string Assign(GameObject characterRoot, Gender gender, string animationRoot = DefaultAnimationRoot, bool overwriteExisting = true)
        {
            SyntyLocomotionAnimationBuilder builder = CreateInstance<SyntyLocomotionAnimationBuilder>();
            try
            {
                builder._characterRoot = characterRoot;
                builder._gender = gender;
                builder._animationRoot = animationRoot;
                builder._overwriteExisting = overwriteExisting;
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
                "Wires the eight directional move states of a PlayableCharacterModel to the Synty " +
                "strafe sets, so a cursor-facing character back-pedals and side-steps instead of " +
                "running forwards sideways.\n" +
                "Any direction left empty falls back to the run set, which is why sprint only needs " +
                "its forward clip.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _characterRoot = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Character Root", "Prefab asset, prefab-stage root, or scene instance carrying the PlayableCharacterModel."),
                _characterRoot, typeof(GameObject), true);
            _gender = (Gender)EditorGUILayout.EnumPopup(
                new GUIContent("Animation Set", "Which half of the pack to read: Masculine (_Masc) or Feminine (_Femn)."), _gender);
            _animationRoot = EditorGUILayout.TextField(
                new GUIContent("Animation Root", "Folder searched for clips. Every .fbx under it is indexed by file name."),
                _animationRoot);

            EditorGUILayout.Space();
            _fillIdle = EditorGUILayout.Toggle(new GUIContent("Idle", "Standing and crouching idles."), _fillIdle);
            _fillRun = EditorGUILayout.Toggle(new GUIContent("Run", "The eight-direction default move set."), _fillRun);
            _fillWalk = EditorGUILayout.Toggle(new GUIContent("Walk", "Used while the walk toggle is held."), _fillWalk);
            _fillCrouch = EditorGUILayout.Toggle(new GUIContent("Crouch", "Used while crouching."), _fillCrouch);
            _fillSprint = EditorGUILayout.Toggle(
                new GUIContent("Sprint (forward only)", "The pack only sprints forwards; the other seven directions stay empty and fall back to the run set."),
                _fillSprint);
            _fillAirborne = EditorGUILayout.Toggle(new GUIContent("Jump / Fall / Land", "Airborne states."), _fillAirborne);

            EditorGUILayout.Space();
            _overwriteExisting = EditorGUILayout.Toggle(
                new GUIContent("Overwrite Existing", "Off: only states with no clip are filled, so hand-picked clips survive."),
                _overwriteExisting);
            _measurePhases = EditorGUILayout.Toggle(
                new GUIContent("Measure Stride Phase", "Measure where each move clip sits in the stride cycle and store it on the LocomotionPhaseSync component, so forward-to-backward transitions blend in step."),
                _measurePhases);
            if (EditorGUI.EndChangeCheck())
                _report = string.Empty;

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_characterRoot == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Preview", GUILayout.Height(26f)))
                        Run(false);
                    if (GUILayout.Button("Assign Clips", GUILayout.Height(26f)))
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

        // --- Clip lookup ------------------------------------------------------------------------

        private string GenderToken
        {
            get { return _gender == Gender.Masculine ? "Masc" : "Femn"; }
        }

        /// <summary>
        /// Indexes every .fbx under the root by file name. Matching the exact name is what keeps the
        /// root-motion variants out - "A_Run_FwdStrafeF_Masc" never matches
        /// "A_Run_FwdStrafeF_RootMotion_Masc", and the kit drives position itself.
        /// </summary>
        private bool BuildClipIndex(out string error)
        {
            error = null;
            _clipIndex = new Dictionary<string, string>();

            if (!Directory.Exists(_animationRoot))
            {
                error = $"Animation root '{_animationRoot}' does not exist. Import the Synty Animation Base Locomotion pack, or point this at wherever it landed.";
                return false;
            }

            foreach (string path in Directory.GetFiles(_animationRoot, "*.fbx", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!_clipIndex.ContainsKey(name))
                    _clipIndex[name] = path.Replace('\\', '/');
            }

            if (_clipIndex.Count == 0)
            {
                error = $"No .fbx files under '{_animationRoot}'.";
                return false;
            }

            return true;
        }

        private AnimationClip LoadClip(string clipName)
        {
            if (!_clipIndex.TryGetValue(clipName, out string path))
                return null;

            // Clips inside an .fbx are sub-assets, so the imported model itself is not what we want.
            foreach (Object representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                AnimationClip clip = representation as AnimationClip;
                if (clip != null && !clip.name.StartsWith("__preview__"))
                    return clip;
            }

            return null;
        }

        // --- Planning ---------------------------------------------------------------------------

        private class Assignment
        {
            public string Target;
            public string ClipName;
            public AnimationClip Clip;
            public AnimState State;
            public bool Skipped;
            /// <summary>Null for the single states; set for the directional ones, which is what the phase pass groups by.</summary>
            public MoveTypeDef MoveType;
            public DirectionDef Direction;
        }

        private List<MoveTypeDef> EnabledMoveTypes()
        {
            List<MoveTypeDef> moveTypes = new List<MoveTypeDef>();
            if (_fillRun)
                moveTypes.Add(new MoveTypeDef("moveStates", "A_Run", d => d.moveStates));
            if (_fillWalk)
                moveTypes.Add(new MoveTypeDef("walkStates", "A_Walk", d => d.walkStates));
            if (_fillCrouch)
                moveTypes.Add(new MoveTypeDef("crouchMoveStates", "A_Crouch", d => d.crouchMoveStates));
            return moveTypes;
        }

        private List<SingleDef> EnabledSingles()
        {
            List<SingleDef> singles = new List<SingleDef>();
            if (_fillIdle)
            {
                singles.Add(new SingleDef("idleState", "A_Idle_Standing", d => d.idleState));
                singles.Add(new SingleDef("crouchIdleState", "A_Idle_Crouching", d => d.crouchIdleState));
            }
            if (_fillSprint)
                singles.Add(new SingleDef("sprintStates.forwardState", "A_Sprint_F", d => d.sprintStates.forwardState));
            if (_fillAirborne)
            {
                singles.Add(new SingleDef("jumpState", "A_Jump_Idle", d => d.jumpState));
                singles.Add(new SingleDef("fallState", "A_InAir_FallShort", d => d.fallState));
                singles.Add(new SingleDef("landedState", "A_Land_IdleMedium", d => d.landedState));
            }
            return singles;
        }

        private List<Assignment> Plan(PlayableCharacterModel model)
        {
            List<Assignment> assignments = new List<Assignment>();
            DefaultAnimations animations = model.defaultAnimations;
            string token = GenderToken;

            foreach (MoveTypeDef moveType in EnabledMoveTypes())
            {
                MoveStates states = moveType.Pick(animations);
                if (states == null)
                    continue;
                foreach (DirectionDef direction in Directions)
                {
                    Assignment assignment = Consider(
                        $"{moveType.Label}.{direction.Field}",
                        $"{moveType.Prefix}_{direction.Suffix}_{token}",
                        direction.Pick(states));
                    assignment.MoveType = moveType;
                    assignment.Direction = direction;
                    assignments.Add(assignment);
                }
            }

            foreach (SingleDef single in EnabledSingles())
            {
                assignments.Add(Consider(
                    single.Label,
                    $"{single.ClipName}_{token}",
                    single.Pick(animations)));
            }

            return assignments;
        }

        private Assignment Consider(string target, string clipName, AnimState state)
        {
            Assignment assignment = new Assignment
            {
                Target = target,
                ClipName = clipName,
                State = state,
            };

            if (state == null)
            {
                assignment.Skipped = true;
                return assignment;
            }

            if (!_overwriteExisting && state.clip != null)
            {
                assignment.Skipped = true;
                assignment.Clip = state.clip;
                return assignment;
            }

            assignment.Clip = LoadClip(clipName);
            return assignment;
        }

        // --- Apply ------------------------------------------------------------------------------

        private void Run(bool apply)
        {
            if (!BuildClipIndex(out string indexError))
            {
                _report = indexError;
                return;
            }

            GameObject source = _characterRoot;
            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(source);
            string assetPath = isAsset ? AssetDatabase.GetAssetPath(source) : null;

            // A prefab asset selected in the Project window cannot be edited in place; open an
            // isolated copy, edit that, and save it back.
            GameObject working = apply && isAsset ? PrefabUtility.LoadPrefabContents(assetPath) : source;

            try
            {
                PlayableCharacterModel model = working.GetComponent<PlayableCharacterModel>();
                if (model == null)
                    model = working.GetComponentInChildren<PlayableCharacterModel>(true);
                if (model == null)
                {
                    _report = $"'{source.name}' has no PlayableCharacterModel component.";
                    return;
                }
                if (model.defaultAnimations == null)
                {
                    _report = $"'{model.name}' has no Default Animations block to write into.";
                    return;
                }

                List<Assignment> assignments = Plan(model);
                _report = BuildReport(model, assignments, apply);
                if (!apply)
                    return;

                if (!isAsset)
                    Undo.RegisterFullObjectHierarchyUndo(working, "Assign Synty Locomotion Animations");

                foreach (Assignment assignment in assignments)
                {
                    if (assignment.Skipped || assignment.Clip == null)
                        continue;
                    assignment.State.clip = assignment.Clip;
                }

                if (_measurePhases)
                    _report += "\n" + WritePhaseOffsets(model, assignments);

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

        // --- Stride phase -----------------------------------------------------------------------

        private const int PhaseSamples = 42;

        /// <summary>
        /// A shift only counts when it beats no-shift by this much. Measured on the masculine set,
        /// the backward clips land at 0.04-0.19 of their unshifted error while same-family clips sit
        /// at 0.85-1.00, so the two cases separate cleanly either side of this.
        /// </summary>
        private const double DecisiveImprovement = 0.5d;

        /// <summary>
        /// Measures where each move clip sits in the shared stride cycle and stores it on the
        /// character's <see cref="LocomotionPhaseSync"/>.
        ///
        /// Within a strafe family Synty's clips are already phase-locked, but `FwdStrafe` and
        /// `BckStrafe` are authored a third to a half of a cycle apart - which is why jogging
        /// forward and then backward blends through a mess of feet. Each clip is compared against
        /// the forward clip of its own move type by cross-correlating the height of the left foot
        /// over one cycle, a signal that survives the direction of travel reversing.
        /// </summary>
        private string WritePhaseOffsets(PlayableCharacterModel model, List<Assignment> assignments)
        {
            LocomotionPhaseSync sync = model.GetComponent<LocomotionPhaseSync>();
            if (sync == null)
                return "Stride phase skipped: no LocomotionPhaseSync on " + model.name + ".";

            Animator animator = model.animator != null ? model.animator : model.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                return "Stride phase skipped: measuring needs a humanoid Avatar on the Animator.";

            // Sampling poses the rig, so it runs on a throwaway copy - doing it on the model itself
            // would bake a mid-stride pose into the saved prefab.
            GameObject sampler = Instantiate(model.gameObject);
            sampler.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                sampler.SetActive(true);
                Animator samplerAnimator = sampler.GetComponent<Animator>();
                if (samplerAnimator == null)
                    samplerAnimator = sampler.GetComponentInChildren<Animator>();

                Transform foot = samplerAnimator != null ? samplerAnimator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
                Transform hips = samplerAnimator != null ? samplerAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (foot == null || hips == null)
                    return "Stride phase skipped: the Avatar has no left foot / hips mapping.";

                List<LocomotionPhaseSync.ClipPhase> phases = new List<LocomotionPhaseSync.ClipPhase>();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Stride phase (fraction of a cycle, vs the forward clip of each set):");

                // Group by the definitions the assignments actually carry. Calling EnabledMoveTypes()
                // again would hand back fresh instances that match nothing by reference.
                List<MoveTypeDef> moveTypes = new List<MoveTypeDef>();
                foreach (Assignment assignment in assignments)
                {
                    if (assignment.MoveType != null && !moveTypes.Contains(assignment.MoveType))
                        moveTypes.Add(assignment.MoveType);
                }

                foreach (MoveTypeDef moveType in moveTypes)
                {
                    AnimationClip reference = FindAssignedClip(assignments, moveType, "forwardState");
                    if (reference == null)
                    {
                        sb.AppendLine("  " + moveType.Label + ": no forward clip to measure against, skipped");
                        continue;
                    }

                    float[] referenceSamples = SampleFootHeight(sampler, foot, hips, reference);
                    foreach (Assignment assignment in assignments)
                    {
                        if (assignment.MoveType != moveType || assignment.Clip == null)
                            continue;

                        float offset;
                        bool shifted = TryMeasureOffset(referenceSamples, SampleFootHeight(sampler, foot, hips, assignment.Clip), out offset);
                        phases.Add(new LocomotionPhaseSync.ClipPhase()
                        {
                            clip = assignment.Clip,
                            offset = shifted ? offset : 0f,
                        });
                        sb.AppendLine("  " + assignment.Target + ": " + (shifted ? offset.ToString("F3") : "0.000 (already in step)"));
                    }
                }

                sync.ClipPhases = phases.ToArray();
                EditorUtility.SetDirty(sync);
                return sb.ToString();
            }
            finally
            {
                DestroyImmediate(sampler);
            }
        }

        private static AnimationClip FindAssignedClip(List<Assignment> assignments, MoveTypeDef moveType, string field)
        {
            foreach (Assignment assignment in assignments)
            {
                if (assignment.MoveType == moveType && assignment.Direction != null && assignment.Direction.Field == field)
                    return assignment.Clip;
            }
            return null;
        }

        private static float[] SampleFootHeight(GameObject sampler, Transform foot, Transform hips, AnimationClip clip)
        {
            float[] samples = new float[PhaseSamples];
            for (int i = 0; i < PhaseSamples; ++i)
            {
                clip.SampleAnimation(sampler, clip.length * i / PhaseSamples);
                samples[i] = hips.InverseTransformPoint(foot.position).y;
            }
            return samples;
        }

        /// <summary>
        /// Finds the shift that best lines the candidate up with the reference, and reports whether
        /// it is worth taking. Clips inside one family already match unshifted, and a clip whose
        /// gait is structurally different - the right-strafe run against the forward run, say - has
        /// no alignment worth carrying. Both are left at zero rather than nudged by noise.
        /// </summary>
        private static bool TryMeasureOffset(float[] reference, float[] candidate, out float offset)
        {
            offset = 0f;

            double atZero = 0d;
            double best = double.MaxValue;
            int bestShift = 0;

            for (int shift = 0; shift < PhaseSamples; ++shift)
            {
                double sum = 0d;
                for (int i = 0; i < PhaseSamples; ++i)
                {
                    double difference = reference[i] - candidate[(i + shift) % PhaseSamples];
                    sum += difference * difference;
                }
                if (shift == 0)
                    atZero = sum;
                if (sum < best)
                {
                    best = sum;
                    bestShift = shift;
                }
            }

            if (atZero <= 0d || best > atZero * DecisiveImprovement)
                return false;

            offset = bestShift / (float)PhaseSamples;
            return true;
        }

        // --- Report -----------------------------------------------------------------------------

        private string BuildReport(PlayableCharacterModel model, List<Assignment> assignments, bool applied)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(applied ? "ASSIGNED" : "PREVIEW");
            sb.AppendLine($"{_clipIndex.Count} clips indexed under {_animationRoot}");
            sb.AppendLine();

            int assigned = 0;
            int missing = 0;
            int kept = 0;

            foreach (Assignment assignment in assignments)
            {
                if (assignment.Skipped)
                {
                    ++kept;
                    sb.AppendLine($"  = {assignment.Target}: kept existing");
                    continue;
                }
                if (assignment.Clip == null)
                {
                    ++missing;
                    sb.AppendLine($"  ! {assignment.Target}: NOT FOUND - {assignment.ClipName}");
                    continue;
                }
                ++assigned;
                sb.AppendLine($"  + {assignment.Target}: {assignment.ClipName}");
            }

            sb.AppendLine();
            sb.AppendLine($"{assigned} assigned, {missing} missing, {kept} kept.");

            foreach (string warning in Warnings(model))
                sb.AppendLine("WARNING: " + warning);

            return sb.ToString();
        }

        /// <summary>
        /// Reported, not fixed: these live on the Animator rather than in the animation data this
        /// window owns, and silently flipping them would be a surprise.
        /// </summary>
        private IEnumerable<string> Warnings(PlayableCharacterModel model)
        {
            Animator animator = model.animator != null ? model.animator : model.GetComponentInChildren<Animator>();

            if (animator == null)
            {
                yield return "no Animator found - the model cannot play anything without one.";
                yield break;
            }

            if (animator.applyRootMotion)
                yield return "the Animator has Apply Root Motion on. The kit drives position itself, so root motion fights it - turn it off.";

            if (animator.avatar == null)
                yield return "the Animator has no Avatar; humanoid retargeting needs one.";
            else if (!animator.avatar.isHuman)
                yield return "the Animator's Avatar is not Humanoid, so the pack's humanoid clips will not retarget onto it.";
        }
    }
}
