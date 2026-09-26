using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace PartyTime
{
    internal sealed class JukeboxButtons
    {
        private const float MountHeight = 1.6f;
        private const float MaxWallDistance = 2.5f;
        private const float WallFacing = 0.9f;
        private const float BoxWidth = 0.26f;
        private const float BoxHeight = 0.34f;
        private const float BoxDepth = 0.08f;
        private const float Spacing = 0.4f;
        private const float DomeSizeFactor = 0.85f;
        private const float DomeDrop = 0.03f;
        private const float LabelRise = 0.115f;
        private const float LabelFontSize = 0.55f;
        private const float LabelWidth = 0.24f;
        private const float LabelHeight = 0.08f;
        private const float HitRadius = 0.2f;
        private const int PlapHitboxLayer = 6;
        private const float PressDepth = 0.02f;
        private const float PressSeconds = 0.25f;
        private const float PressInShare = 0.25f;
        private const float GlowOff = 0.12f;
        private const float GlowReady = 0.6f;
        private const float GlowOnBeat = 0.35f;
        private const float GlowBeatBoost = 1.1f;
        private const float GlowSkipIdle = 0.15f;
        private const float GlowSkipPlaying = 0.45f;
        private const float GlowPressFlash = 1.5f;
        private const float GlowFollowSeconds = 0.08f;

        private static readonly Color PartyColor = new Color(1f, 0.2f, 0.7f);
        private static readonly Color SkipColor = new Color(0.15f, 0.8f, 1f);
        private static readonly Color BoxColor = new Color(0.14f, 0.14f, 0.15f);
        private static readonly Color LabelColor = new Color(1f, 0.95f, 0.85f);

        private static readonly AccessTools.FieldRef<InteractableDismissButton, Animator> DismissButtonModel = AccessTools.FieldRefAccess<InteractableDismissButton, Animator>("buttonAnimator");
        private static readonly AccessTools.FieldRef<InteractableJukebox, AudioResource> JukeboxClick = AccessTools.FieldRefAccess<InteractableJukebox, AudioResource>("buzzSound");

        private sealed class Unit
        {
            public WallButton Button;
            public Transform Dome;
            public Vector3 DomeRest;
            public Material DomeMaterial;
            public string EmissionProperty;
            public Color Color;
            public float Glow;
        }

        private readonly GameObject _root;
        private readonly List<Object> _assets = new List<Object>();
        private readonly AudioResource _click;
        private Unit _party;
        private Unit _skip;

        private JukeboxButtons(GameObject root, AudioResource click)
        {
            _root = root;
            _click = click;
        }

        public static JukeboxButtons Create(InteractableJukebox jukebox, Action party, Action skip)
        {
            var jukeboxCollider = jukebox.GetComponent<Collider>();
            if (jukeboxCollider == null) return null;
            var model = jukebox.transform.parent != null ? jukebox.transform.parent : jukebox.transform;
            if (!TryFindWall(model, jukeboxCollider.bounds, out var wallPoint, out var wallNormal)) return null;

            var rotation = Quaternion.LookRotation(wallNormal, Vector3.up);
            var viewerRight = Vector3.Cross(Vector3.up, -wallNormal);
            var partyCenter = wallPoint + wallNormal * (BoxDepth * 0.5f) - viewerRight * (Spacing * 0.5f);
            var skipCenter = wallPoint + wallNormal * (BoxDepth * 0.5f) + viewerRight * (Spacing * 0.5f);
            if (Blocked(partyCenter, rotation) || Blocked(skipCenter, rotation)) return null;

            var buttons = new JukeboxButtons(new GameObject("PartyTime Buttons"), JukeboxClick(jukebox));
            var looks = new Looks(model, buttons._assets);
            buttons._party = buttons.BuildUnit("Party Button", "PARTY", PartyColor, partyCenter, rotation, looks, party);
            buttons._skip = buttons.BuildUnit("Skip Button", "SKIP", SkipColor, skipCenter, rotation, looks, skip);
            return buttons;
        }

        public void Update(float deltaTime, bool partyOn, bool songPlaying, float beatPulse)
        {
            if (_root == null) return;
            float partyGlow = partyOn ? (songPlaying ? GlowOnBeat + GlowBeatBoost * beatPulse : GlowReady) : GlowOff;
            float skipGlow = songPlaying ? GlowSkipPlaying : GlowSkipIdle;
            Animate(_party, partyGlow, deltaTime);
            Animate(_skip, skipGlow, deltaTime);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            foreach (var asset in _assets) Object.Destroy(asset);
            _assets.Clear();
        }

        private static bool TryFindWall(Transform model, Bounds jukebox, out Vector3 point, out Vector3 normal)
        {
            var front = Vector3.ProjectOnPlane(model.forward, Vector3.up).normalized;
            var left = Vector3.Cross(Vector3.up, front);
            float height = jukebox.min.y + MountHeight;
            foreach (var side in new[] { left, -left })
            {
                float reach = Mathf.Abs(jukebox.extents.x * side.x) + Mathf.Abs(jukebox.extents.z * side.z);
                var origin = new Vector3(jukebox.center.x, height, jukebox.center.z) + side * (reach + 0.05f);
                if (!Physics.Raycast(origin, side, out var hit, MaxWallDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.transform.IsChildOf(model) || Vector3.Dot(hit.normal, side) > -WallFacing) continue;
                point = hit.point;
                normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                return true;
            }
            point = normal = Vector3.zero;
            return false;
        }

        private static bool Blocked(Vector3 center, Quaternion rotation)
        {
            var halfExtents = new Vector3(BoxWidth * 0.45f, BoxHeight * 0.45f, BoxDepth * 0.4f);
            return Physics.CheckBox(center, halfExtents, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private Unit BuildUnit(string name, string label, Color color, Vector3 center, Quaternion rotation, Looks looks, Action pressed)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_root.transform, false);
            root.SetPositionAndRotation(center, rotation);

            var box = CreateMesh("Box", root, looks.BoxMesh, looks.BoxMaterial);
            box.localScale = new Vector3(BoxWidth, BoxHeight, BoxDepth);
            box.gameObject.AddComponent<BoxCollider>();

            var unit = new Unit { Color = color };
            unit.DomeMaterial = looks.CreateDomeMaterial(color, out unit.EmissionProperty);
            unit.Dome = CreateMesh("Dome", root, looks.DomeMesh, unit.DomeMaterial);
            unit.DomeRest = new Vector3(0f, -DomeDrop, BoxDepth * 0.5f);
            unit.Dome.localPosition = unit.DomeRest;
            unit.Dome.localScale = looks.DomeScale * DomeSizeFactor;

            var text = new GameObject("Label", typeof(RectTransform));
            text.transform.SetParent(root, false);
            text.transform.localPosition = new Vector3(0f, LabelRise, BoxDepth * 0.5f + 0.002f);
            text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = text.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = LabelFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = LabelColor;
            ((RectTransform)text.transform).sizeDelta = new Vector2(LabelWidth, LabelHeight);

            unit.Button = root.gameObject.AddComponent<WallButton>();
            unit.Button.Pressed = () =>
            {
                if (_click != null) AudioHelper.PlaySFXAtPoint(_click, unit.Dome.position);
                pressed();
            };
            var hitbox = new GameObject("Plappable", typeof(SphereCollider));
            hitbox.layer = PlapHitboxLayer;
            hitbox.transform.SetParent(root, false);
            hitbox.transform.localPosition = unit.DomeRest;
            hitbox.GetComponent<SphereCollider>().radius = HitRadius;
            hitbox.AddComponent<WallButtonHitbox>().Button = unit.Button;
            return unit;
        }

        private static Transform CreateMesh(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private static void Animate(Unit unit, float glow, float deltaTime)
        {
            if (unit == null || unit.Dome == null) return;
            float sincePress = Time.unscaledTime - unit.Button.LastPress;
            float press = 0f;
            if (sincePress < PressSeconds)
            {
                float t = sincePress / PressSeconds;
                press = t < PressInShare ? t / PressInShare : 1f - (t - PressInShare) / (1f - PressInShare);
                glow += GlowPressFlash * (1f - t);
            }
            unit.Dome.localPosition = unit.DomeRest - Vector3.forward * (PressDepth * press);
            unit.Glow = Mathf.Lerp(unit.Glow, glow, 1f - Mathf.Exp(-deltaTime / GlowFollowSeconds));
            var emission = unit.Color * unit.Glow;
            emission.a = 1f;
            unit.DomeMaterial.SetColor(unit.EmissionProperty, emission);
        }

        private sealed class Looks
        {
            private const string GameTint = "_ColorTint";
            private const string GameEmission = "_EmissiveTint";
            private const string LitColor = "_BaseColor";
            private const string LitEmission = "_EmissionColor";

            private readonly List<Object> _assets;
            private readonly Material _domeTemplate;
            private readonly Shader _lit;

            public readonly Mesh BoxMesh;
            public readonly Mesh DomeMesh;
            public readonly Vector3 DomeScale;
            public readonly Material BoxMaterial;

            public Looks(Transform jukeboxModel, List<Object> assets)
            {
                _assets = assets;
                _lit = Shader.Find("Universal Render Pipeline/Lit");
                Material boxTemplate = null;
                foreach (var renderer in jukeboxModel.GetComponentsInChildren<Renderer>())
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || !material.HasProperty(GameTint)) continue;
                        if (boxTemplate == null && material.name == "metal") boxTemplate = material;
                        if (_domeTemplate == null && material.HasProperty(GameEmission) && material.GetColor(GameEmission).maxColorComponent > 0.01f) _domeTemplate = material;
                    }
                }

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                BoxMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cube);

                var dismiss = Object.FindFirstObjectByType<InteractableDismissButton>();
                var dome = dismiss != null ? DismissButtonModel(dismiss) : null;
                var domeFilter = dome != null ? dome.GetComponent<MeshFilter>() : null;
                if (domeFilter != null && domeFilter.sharedMesh != null)
                {
                    DomeMesh = domeFilter.sharedMesh;
                    DomeScale = Vector3.one;
                }
                else
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    DomeMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(sphere);
                    DomeScale = new Vector3(0.21f, 0.21f, 0.07f);
                }

                if (boxTemplate != null)
                {
                    BoxMaterial = Track(new Material(boxTemplate) { name = "PartyTime Button Box" });
                    BoxMaterial.SetFloat("_UseVertexTinting", 0f);
                    BoxMaterial.SetColor(GameTint, BoxColor);
                }
                else
                {
                    BoxMaterial = Track(new Material(_lit) { name = "PartyTime Button Box" });
                    BoxMaterial.SetColor(LitColor, BoxColor);
                }
            }

            public Material CreateDomeMaterial(Color color, out string emissionProperty)
            {
                Material material;
                if (_domeTemplate != null)
                {
                    material = Track(new Material(_domeTemplate) { name = "PartyTime Button Dome" });
                    material.SetTexture("_BaseColorMap", Texture2D.whiteTexture);
                    material.SetTexture("_EmissiveMap", Texture2D.whiteTexture);
                    material.SetColor(GameTint, color);
                    emissionProperty = GameEmission;
                }
                else
                {
                    material = Track(new Material(_lit) { name = "PartyTime Button Dome" });
                    material.SetColor(LitColor, color);
                    material.EnableKeyword("_EMISSION");
                    emissionProperty = LitEmission;
                }
                return material;
            }

            private Material Track(Material material)
            {
                _assets.Add(material);
                return material;
            }
        }
    }
}
