using System.Collections.Generic;
using UnityEngine;

namespace PartyTime
{
    internal sealed class DiscoBall
    {
        private const float BallRadius = 0.45f;
        private const int BallRings = 12;
        private const int BallSegments = 24;
        private const float CableWidth = 0.02f;
        private const float SpeckAngle = 150f;
        private const float SpeckRange = 30f;
        private const float Strength = 25f;
        private const float SpinDegreesPerSecond = 20f;
        private const int CookieSize = 512;
        private const int CookieSpecks = 900;
        private const float PinSpotAngle = 14f;
        private const float PinSpotStrength = 30f;
        private static readonly Vector3 PinSpotOffset = new Vector3(1.2f, -2.2f, 1.2f);

        private static Texture2D _speckCookie;

        private readonly Vector3 _floor;
        private readonly float _ceiling;
        private readonly float _hangY;
        private readonly Transform _hanger;
        private readonly Transform _spinner;
        private readonly Transform _cable;
        private readonly Light _light;
        private readonly Light _pinSpot;
        private readonly List<Object> _assets = new List<Object>();
        private float _angle;

        public DiscoBall(Transform parent, PartyStage stage)
        {
            _floor = stage.Floor;
            _ceiling = stage.Ceiling;
            _hangY = stage.RigHeight;

            _hanger = new GameObject("Mirror Ball").transform;
            _hanger.SetParent(parent, false);
            _spinner = new GameObject("Spinner").transform;
            _spinner.SetParent(_hanger, false);

            var specks = new GameObject("Specks").transform;
            specks.SetParent(_spinner, false);
            specks.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            _light = specks.gameObject.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.spotAngle = SpeckAngle;
            _light.innerSpotAngle = SpeckAngle - 20f;
            _light.range = SpeckRange;
            _light.shadows = LightShadows.None;
            _light.cookie = SpeckCookie();
            _light.intensity = 0f;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            var mirror = CreateMaterial(shader, new Color(0.8f, 0.8f, 0.85f), 1f, 0.9f);
            var ball = CreateRenderer("Ball", _spinner, FacetedSphere(), mirror);
            ball.localScale = Vector3.one * BallRadius;
            var cableMaterial = CreateMaterial(shader, new Color(0.1f, 0.1f, 0.1f), 0.5f, 0.3f);
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var cableMesh = cylinder.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(cylinder);
            _cable = CreateRenderer("Cable", parent, cableMesh, cableMaterial);

            var pinSpot = new GameObject("Pin Spot").transform;
            pinSpot.SetParent(_hanger, false);
            pinSpot.localPosition = PinSpotOffset;
            pinSpot.localRotation = Quaternion.LookRotation(-PinSpotOffset);
            _pinSpot = pinSpot.gameObject.AddComponent<Light>();
            _pinSpot.type = LightType.Spot;
            _pinSpot.spotAngle = PinSpotAngle;
            _pinSpot.innerSpotAngle = PinSpotAngle * 0.5f;
            _pinSpot.range = PinSpotOffset.magnitude * 2f;
            _pinSpot.shadows = LightShadows.None;
            _pinSpot.intensity = 0f;
        }

        public void Update(float deltaTime, MusicAnalyzer music, float amount, float flash, Color tint)
        {
            float y = Mathf.Lerp(_ceiling - BallRadius, _hangY, Mathf.SmoothStep(0f, 1f, amount));
            _hanger.position = new Vector3(_floor.x, y, _floor.z);
            if (_cable != null)
            {
                float length = Mathf.Max(0.01f, _ceiling - y - BallRadius);
                _cable.position = new Vector3(_floor.x, y + BallRadius + length * 0.5f, _floor.z);
                _cable.localScale = new Vector3(CableWidth, length * 0.5f, CableWidth);
            }

            _angle = Mathf.Repeat(_angle + deltaTime * SpinDegreesPerSecond * (0.6f + 0.8f * music.Loudness), 360f);
            _spinner.localRotation = Quaternion.Euler(0f, _angle, 0f);
            float height = Mathf.Max(1f, y - _floor.y);
            _light.color = Color.Lerp(Color.white, tint, 0.3f);
            _light.intensity = Strength * height * height * amount * (0.85f + 0.6f * flash);
            if (_pinSpot != null) _pinSpot.intensity = PinSpotStrength * PinSpotOffset.sqrMagnitude * amount * (0.6f + 0.8f * music.Treble + flash);
        }

        public void Destroy()
        {
            foreach (var asset in _assets) Object.Destroy(asset);
            _assets.Clear();
        }

        private Material CreateMaterial(Shader shader, Color color, float metallic, float smoothness)
        {
            var material = new Material(shader) { name = "PartyTime Mirror Ball" };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            _assets.Add(material);
            return material;
        }

        private static Transform CreateRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private Mesh FacetedSphere()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring < BallRings; ring++)
            {
                float lat0 = Mathf.PI * ring / BallRings - Mathf.PI * 0.5f;
                float lat1 = Mathf.PI * (ring + 1) / BallRings - Mathf.PI * 0.5f;
                for (int segment = 0; segment < BallSegments; segment++)
                {
                    float lon0 = Mathf.PI * 2f * segment / BallSegments;
                    float lon1 = Mathf.PI * 2f * (segment + 1) / BallSegments;
                    var corners = new[] { OnSphere(lat0, lon0), OnSphere(lat1, lon0), OnSphere(lat1, lon1), OnSphere(lat0, lon1) };
                    var normal = (corners[0] + corners[1] + corners[2] + corners[3]).normalized;
                    int start = vertices.Count;
                    foreach (var corner in corners)
                    {
                        vertices.Add(Vector3.Lerp(normal, corner, 0.94f));
                        normals.Add(normal);
                    }
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }
            var mesh = new Mesh { name = "PartyTime Mirror Ball" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            _assets.Add(mesh);
            return mesh;
        }

        private static Vector3 OnSphere(float latitude, float longitude)
        {
            return new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude));
        }

        private static Texture2D SpeckCookie()
        {
            if (_speckCookie != null) return _speckCookie;
            var pixels = new Color32[CookieSize * CookieSize];
            var random = new System.Random(1977);
            for (int speck = 0; speck < CookieSpecks; speck++)
            {
                float centerX = (float)random.NextDouble() * CookieSize;
                float centerY = (float)random.NextDouble() * CookieSize;
                float radius = 1.5f + (float)random.NextDouble() * 2f;
                float brightness = 0.6f + (float)random.NextDouble() * 0.4f;
                int x0 = Mathf.Max(0, (int)(centerX - radius - 1f)), x1 = Mathf.Min(CookieSize - 1, (int)(centerX + radius + 1f));
                int y0 = Mathf.Max(0, (int)(centerY - radius - 1f)), y1 = Mathf.Min(CookieSize - 1, (int)(centerY + radius + 1f));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - centerX, dy = y + 0.5f - centerY;
                        byte value = (byte)(Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f) * brightness * 255f);
                        int index = y * CookieSize + x;
                        if (value > pixels[index].a) pixels[index] = new Color32(value, value, value, value);
                    }
                }
            }
            _speckCookie = new Texture2D(CookieSize, CookieSize, TextureFormat.RGBA32, false)
            {
                name = "PartyTime Mirror Ball Specks",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _speckCookie.SetPixels32(pixels);
            _speckCookie.Apply(false, true);
            return _speckCookie;
        }
    }
}
